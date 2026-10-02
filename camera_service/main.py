"""
Camera person-detection service (YOLOv8 + movement line-crossing).
Tracks persons and counts ENTRY / EXIT when they cross the door line.
"""

from __future__ import annotations

import os
import threading
import time
from typing import Any

import cv2
import numpy as np
from fastapi import FastAPI, HTTPException, Response
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

try:
    from ultralytics import YOLO
except ImportError as exc:  # pragma: no cover
    raise SystemExit(
        "ultralytics not installed. Run: pip install -r requirements.txt"
    ) from exc

app = FastAPI(title="SRP Camera Detection Service", version="1.5.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

_lock = threading.Lock()
_infer_lock = threading.Lock()
_workers: dict[str, "CameraWorker"] = {}
_model: YOLO | None = None
_model_lock = threading.Lock()


def _normalize_camera_id(camera_id: str | None) -> str:
    value = (camera_id or "default").strip()
    return value if value else "default"


def _detect_device() -> tuple[Any, bool, str]:
    """Returns (ultralytics device, use_half, label). CUDA when available, else CPU."""
    try:
        import torch

        if torch.cuda.is_available():
            return 0, True, f"cuda:{torch.cuda.get_device_name(0)}"
    except Exception:
        pass
    return "cpu", False, "cpu"


DEVICE, USE_HALF, DEVICE_LABEL = _detect_device()
ON_GPU = DEVICE != "cpu"
MODEL_NAME = os.environ.get("SRP_YOLO_MODEL") or ("yolov8m.pt" if ON_GPU else "yolov8n.pt")
INFER_IMGSZ = int(os.environ.get("SRP_YOLO_IMGSZ") or (640 if ON_GPU else 320))


def get_model() -> YOLO:
    global _model, MODEL_NAME
    with _model_lock:
        if _model is None:
            try:
                _model = YOLO(MODEL_NAME)
            except Exception:
                # Larger weights are downloaded on first use; fall back if offline.
                MODEL_NAME = "yolov8n.pt"
                _model = YOLO(MODEL_NAME)
        return _model


class StartRequest(BaseModel):
    cameraId: str = Field(default="default", min_length=1)
    rtspUrl: str = Field(min_length=3)
    enableDetection: bool = True
    minConfidence: float = 0.40
    zoneDividerPercent: int = 50
    cameraPurpose: str = "DOOR"
    showDoorLine: bool | None = None
    doorLineOrientation: str = "HORIZONTAL"


def _normalize_door_line_orientation(value: str | None) -> str:
    raw = (value or "HORIZONTAL").strip().upper()
    if raw in ("VERTICAL", "HORIZONTAL", "DIAGONAL"):
        return raw
    if raw in ("CORNER", "CORNER_WISE", "CORNERWISE"):
        return "DIAGONAL"
    return "HORIZONTAL"


def _resolve_show_door_line(camera_purpose: str, show_door_line: bool | None) -> bool:
    purpose = (camera_purpose or "DOOR").strip().upper()
    # Monitoring cameras stay occupancy-only. Everything else draws the IN/OUT line.
    if purpose == "MONITORING":
        return False
    if show_door_line is not None:
        return bool(show_door_line)
    return True


class CameraWorker:
    def __init__(
        self,
        rtsp_url: str,
        enable_detection: bool,
        min_confidence: float,
        zone_divider_percent: int,
        camera_purpose: str = "DOOR",
        show_door_line: bool | None = None,
        door_line_orientation: str = "HORIZONTAL",
    ) -> None:
        self.rtsp_url = rtsp_url
        self.enable_detection = enable_detection
        self.min_confidence = max(0.1, min(0.95, min_confidence))
        self.zone_divider_percent = max(20, min(80, zone_divider_percent))
        self.camera_purpose = (camera_purpose or "DOOR").strip().upper()
        self.show_door_line = _resolve_show_door_line(self.camera_purpose, show_door_line)
        self.door_line_orientation = _normalize_door_line_orientation(door_line_orientation)
        self._running = False
        self._thread: threading.Thread | None = None
        self._cap: cv2.VideoCapture | None = None

        self.connected = False
        self.status_message = "Idle"
        self.total_detected = 0
        # Cumulative movement events (session)
        self.entry_count = 0  # Outside -> Inside (IN)
        self.exit_count = 0  # Inside -> Outside (OUT)
        # Compatibility aliases used by Dashboard (mapped in snapshot)
        self.inside_count = 0
        self.outside_count = 0
        self.average_confidence = 0.0
        self.fps = 0.0
        self.last_event = ""
        self._frame_jpeg: bytes | None = None
        self._boxes: list[dict[str, Any]] = []

        # track_id -> last side ("OUT" left / "IN" right)
        self._track_side: dict[int, str] = {}
        self._track_last_cross_ts: dict[int, float] = {}
        self._track_centroids: dict[int, tuple[float, float]] = {}
        self._track_last_seen: dict[int, float] = {}
        self._track_side_pending: dict[int, str] = {}
        self._track_side_pending_frames: dict[int, int] = {}
        self._next_track_id = 1
        self._cross_cooldown_sec = 1.2
        self._side_confirm_frames = 2
        self._track_stale_sec = 4.0

    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self.status_message = "Connecting..."
        self._thread = threading.Thread(target=self._loop, name="CameraWorker", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._running = False
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=3)
        self._thread = None
        if self._cap is not None:
            self._cap.release()
            self._cap = None
        self.connected = False
        self.status_message = "Stopped"

    def snapshot(self) -> dict[str, Any]:
        # Dashboard historically used insideCount/outsideCount cards.
        # Map: inside = ENTRY (IN), outside = EXIT (OUT)
        return {
            "success": True,
            "connected": self.connected,
            "message": self.status_message,
            "totalDetected": self.total_detected,
            "entryCount": self.entry_count,
            "exitCount": self.exit_count,
            "insideCount": self.entry_count,
            "outsideCount": self.exit_count,
            "averageConfidence": round(self.average_confidence, 1),
            "fps": round(self.fps, 1),
            "lastEvent": self.last_event,
            "boxes": list(self._boxes),
            "detectionEngine": "YOLOv8n-track" if self.enable_detection else "Off",
            "mode": "occupancy" if not self.show_door_line else "line_crossing",
            "cameraPurpose": self.camera_purpose,
            "showDoorLine": self.show_door_line,
            "doorLineOrientation": self.door_line_orientation,
        }

    def jpeg(self) -> bytes | None:
        return self._frame_jpeg

    def _open_capture(self) -> bool:
        if self._cap is not None:
            self._cap.release()
        self._cap = cv2.VideoCapture(self.rtsp_url, cv2.CAP_FFMPEG)
        try:
            self._cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        except Exception:
            pass
        return bool(self._cap.isOpened())

    def _loop(self) -> None:
        fail_count = 0
        frame_count = 0
        t0 = time.time()
        if ON_GPU:
            detect_every = 1
        else:
            detect_every = 3 if self.show_door_line else 2
        frame_index = 0
        last_boxes: list[tuple[int, int, int, int, float, int]] = []

        while self._running:
            if self._cap is None or not self._cap.isOpened():
                if not self._open_capture():
                    self.connected = False
                    self.status_message = "Camera not reachable. Check RTSP URL and network."
                    self._publish_placeholder(self.status_message)
                    time.sleep(1.0)
                    continue
                self.status_message = (
                    "Live (YOLO occupancy)"
                    if self.enable_detection and not self.show_door_line
                    else ("Live (YOLO track)" if self.enable_detection else "Live")
                )
                fail_count = 0

            ok, frame = self._cap.read()
            if not ok or frame is None:
                fail_count += 1
                self.connected = False
                self.status_message = "Stream interrupted. Retrying..."
                self._publish_placeholder(self.status_message)
                if fail_count >= 5:
                    if self._cap is not None:
                        self._cap.release()
                        self._cap = None
                    fail_count = 0
                time.sleep(0.2)
                continue

            fail_count = 0
            self.connected = True
            self.status_message = (
                "Live (YOLO occupancy)"
                if self.enable_detection and not self.show_door_line
                else ("Live (YOLO track)" if self.enable_detection else "Live")
            )
            frame_index += 1

            if self.enable_detection and frame_index % detect_every == 0:
                last_boxes = self._track_and_count(frame)
                self._flush_rtsp()

            vis = frame
            if vis.shape[1] > 960:
                scale = 960.0 / vis.shape[1]
                vis = cv2.resize(
                    vis,
                    (960, max(1, int(vis.shape[0] * scale))),
                    interpolation=cv2.INTER_AREA,
                )
                scaled_boxes = [
                    (
                        int(x1 * scale),
                        int(y1 * scale),
                        int(x2 * scale),
                        int(y2 * scale),
                        conf,
                        tid,
                    )
                    for x1, y1, x2, y2, conf, tid in last_boxes
                ]
            else:
                scaled_boxes = last_boxes

            self._apply_overlay(vis, scaled_boxes)

            ok_jpg, buf = cv2.imencode(".jpg", vis, [int(cv2.IMWRITE_JPEG_QUALITY), 62])
            if ok_jpg:
                self._frame_jpeg = buf.tobytes()

            frame_count += 1
            elapsed = time.time() - t0
            if elapsed >= 1.0:
                self.fps = frame_count / elapsed
                frame_count = 0
                t0 = time.time()

        if self._cap is not None:
            self._cap.release()
            self._cap = None

    def _tracking_point(self, x1: int, y1: int, x2: int, y2: int) -> tuple[float, float]:
        """Point used for zone + line crossing (feet for horizontal door lines)."""
        px = (x1 + x2) / 2.0
        if self.door_line_orientation == "HORIZONTAL":
            py = y1 + (y2 - y1) * 0.92
        else:
            py = y1 + (y2 - y1) * 0.75
        return px, py

    def _line_endpoints(self, w: int, h: int) -> tuple[int, int, int, int]:
        p = self.zone_divider_percent / 100.0
        if self.door_line_orientation == "VERTICAL":
            x = int(w * p)
            return x, 0, x, h
        if self.door_line_orientation == "HORIZONTAL":
            y = int(h * p)
            return 0, y, w, y
        y0 = int(h * (1.0 - p))
        y1 = int(h * p)
        return 0, y0, w, y1

    def _side_of(self, px: float, py: float, w: int, h: int) -> str:
        if self.door_line_orientation == "VERTICAL":
            line_x = w * self.zone_divider_percent / 100.0
            return "OUT" if px < line_x else "IN"
        if self.door_line_orientation == "HORIZONTAL":
            line_y = h * self.zone_divider_percent / 100.0
            return "OUT" if py < line_y else "IN"
        x1, y1, x2, y2 = self._line_endpoints(w, h)
        cross = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1)
        return "OUT" if cross > 0 else "IN"

    def _flush_rtsp(self) -> None:
        if self._cap is None:
            return
        try:
            for _ in range(2):
                self._cap.grab()
        except Exception:
            pass

    def _assign_track_id(
        self,
        cx: float,
        cy: float,
        max_dist: float,
        now: float,
        used_ids: set[int],
    ) -> int:
        best_id = -1
        best_dist = max_dist
        for tid, (px, py) in self._track_centroids.items():
            if tid in used_ids:
                continue
            dist = ((cx - px) ** 2 + (cy - py) ** 2) ** 0.5
            if dist < best_dist:
                best_dist = dist
                best_id = tid

        if best_id < 0:
            # Re-use side history if centroid tracker lost ID mid-crossing.
            inherit_id = -1
            inherit_dist = max_dist * 1.35
            for tid, (px, py) in list(self._track_centroids.items()):
                if tid in used_ids:
                    continue
                dist = ((cx - px) ** 2 + (cy - py) ** 2) ** 0.5
                if dist < inherit_dist:
                    inherit_dist = dist
                    inherit_id = tid
            if inherit_id >= 0:
                best_id = inherit_id
            else:
                best_id = self._next_track_id
                self._next_track_id += 1

        self._track_centroids[best_id] = (cx, cy)
        self._track_last_seen[best_id] = now
        return best_id

    def _stable_side(self, track_id: int, raw_side: str) -> str:
        pending = self._track_side_pending.get(track_id)
        frames = self._track_side_pending_frames.get(track_id, 0)
        if raw_side == pending:
            frames += 1
        else:
            pending = raw_side
            frames = 1
        self._track_side_pending[track_id] = pending
        self._track_side_pending_frames[track_id] = frames

        committed = self._track_side.get(track_id)
        if committed is None:
            if frames >= self._side_confirm_frames:
                return pending
            return raw_side

        if frames >= self._side_confirm_frames:
            return pending
        return committed

    def _track_and_count(
        self, frame: np.ndarray
    ) -> list[tuple[int, int, int, int, float, int]]:
        model = get_model()
        h, w = frame.shape[:2]
        now = time.time()
        match_dist = max(72.0, w * 0.20)

        # Same fast predict path as monitoring. Line-crossing uses a light
        # centroid tracker instead of ByteTrack (which dropped FPS to <1).
        with _infer_lock:
            results = model.predict(
                source=frame,
                conf=max(0.25, self.min_confidence * 0.85) if not self.show_door_line else self.min_confidence,
                classes=[0],
                verbose=False,
                imgsz=INFER_IMGSZ,
                device=DEVICE,
                half=USE_HALF,
            )

        boxes: list[tuple[int, int, int, int, float, int]] = []
        conf_sum = 0.0
        seen_ids: set[int] = set()
        used_ids: set[int] = set()

        if results:
            r0 = results[0]
            if r0.boxes is not None and len(r0.boxes) > 0:
                for box in r0.boxes:
                    xyxy = box.xyxy[0].tolist()
                    conf = float(box.conf[0].item()) * 100.0
                    x1, y1, x2, y2 = map(int, xyxy)
                    px, py = self._tracking_point(x1, y1, x2, y2)
                    track_id = -1
                    if self.show_door_line:
                        track_id = self._assign_track_id(px, py, match_dist, now, used_ids)
                        used_ids.add(track_id)
                        seen_ids.add(track_id)

                        raw_side = self._side_of(px, py, w, h)
                        side = self._stable_side(track_id, raw_side)
                        prev = self._track_side.get(track_id)
                        last_cross = self._track_last_cross_ts.get(track_id, 0.0)

                        if (
                            prev is not None
                            and prev != side
                            and (now - last_cross) >= self._cross_cooldown_sec
                        ):
                            if prev == "OUT" and side == "IN":
                                self.entry_count += 1
                                self.last_event = f"IN #{self.entry_count}"
                                self._track_last_cross_ts[track_id] = now
                            elif prev == "IN" and side == "OUT":
                                self.exit_count += 1
                                self.last_event = f"OUT #{self.exit_count}"
                                self._track_last_cross_ts[track_id] = now

                        self._track_side[track_id] = side

                    conf_sum += conf
                    boxes.append((x1, y1, x2, y2, conf, track_id))

        stale = [
            tid
            for tid, seen_at in self._track_last_seen.items()
            if now - seen_at > self._track_stale_sec
        ]
        for tid in stale:
            self._track_side.pop(tid, None)
            self._track_centroids.pop(tid, None)
            self._track_last_seen.pop(tid, None)
            self._track_last_cross_ts.pop(tid, None)
            self._track_side_pending.pop(tid, None)
            self._track_side_pending_frames.pop(tid, None)

        self.total_detected = len(boxes)
        self.inside_count = self.entry_count
        self.outside_count = self.exit_count
        self.average_confidence = 0.0 if not boxes else conf_sum / len(boxes)
        self._boxes = [
            {
                "x1": a,
                "y1": b,
                "x2": c,
                "y2": d,
                "confidence": round(e, 1),
                "trackId": tid,
            }
            for a, b, c, d, e, tid in boxes
        ]
        return boxes

    def _apply_overlay(
        self,
        frame: np.ndarray,
        boxes: list[tuple[int, int, int, int, float, int]],
    ) -> None:
        h, w = frame.shape[:2]

        if self.show_door_line:
            x1, y1, x2, y2 = self._line_endpoints(w, h)
            cv2.line(frame, (x1, y1), (x2, y2), (0, 220, 255), 2)
            if self.door_line_orientation == "HORIZONTAL":
                cv2.putText(
                    frame,
                    "OUT (top)",
                    (12, max(24, y1 - 12)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 220, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    "IN (bottom) -> ENTRY",
                    (12, min(h - 12, y1 + 28)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 220, 255),
                    2,
                )
            elif self.door_line_orientation == "VERTICAL":
                line_x = int(w * self.zone_divider_percent / 100.0)
                cv2.putText(
                    frame,
                    "OUT (left)",
                    (max(8, line_x - 120), 28),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 220, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    "IN (right) -> ENTRY",
                    (min(w - 180, line_x + 12), 28),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 220, 255),
                    2,
                )
            else:
                cv2.putText(
                    frame,
                    "OUT / IN (corner line)",
                    (12, 28),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 220, 255),
                    2,
                )
            cv2.putText(
                frame,
                f"DOOR LINE ({self.door_line_orientation})",
                (12, 52),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (0, 220, 255),
                1,
            )

        if self.enable_detection:
            for x1, y1, x2, y2, conf, track_id in boxes:
                cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 220, 80), 2)
                if self.show_door_line and track_id >= 0:
                    tx, ty = self._tracking_point(x1, y1, x2, y2)
                    cv2.circle(frame, (int(tx), int(ty)), 4, (255, 180, 0), -1)
                    zone = self._track_side.get(track_id, "?")
                    cv2.putText(
                        frame,
                        zone,
                        (int(tx) + 6, int(ty) - 4),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.45,
                        (255, 180, 0),
                        1,
                    )
                label = f"ID {track_id} {conf:.0f}%" if track_id >= 0 else f"{conf:.0f}%"
                cv2.putText(
                    frame,
                    label,
                    (x1, max(18, y1 - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (0, 220, 80),
                    2,
                )

            if self.show_door_line:
                summary = (
                    f"Now: {self.total_detected} | IN: {self.entry_count} | "
                    f"OUT: {self.exit_count} | Acc: {self.average_confidence:.0f}%"
                )
                if self.last_event:
                    summary += f" | Last: {self.last_event}"
            else:
                summary = (
                    f"Persons: {self.total_detected} | Acc: {self.average_confidence:.0f}%"
                )
            cv2.putText(
                frame,
                summary,
                (12, h - 16),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (255, 255, 255),
                2,
            )

    def _publish_placeholder(self, message: str) -> None:
        img = np.full((360, 640, 3), (36, 28, 24), dtype=np.uint8)
        cv2.putText(img, message[:70], (24, 180), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 2)
        ok, buf = cv2.imencode(".jpg", img, [int(cv2.IMWRITE_JPEG_QUALITY), 70])
        if ok:
            self._frame_jpeg = buf.tobytes()
        self.total_detected = 0
        self._boxes = []


@app.get("/api/health")
def health() -> dict[str, Any]:
    return {
        "success": True,
        "message": "Camera service is running.",
        "version": "1.5.0",
        "device": DEVICE_LABEL,
        "model": MODEL_NAME,
        "imgsz": INFER_IMGSZ,
        "mode": "multi_camera",
        "activeStreams": len(_workers) if _workers else 0,
    }


def _idle_status(camera_id: str) -> dict[str, Any]:
    return {
        "success": True,
        "cameraId": camera_id,
        "connected": False,
        "message": "No active stream.",
        "totalDetected": 0,
        "entryCount": 0,
        "exitCount": 0,
        "insideCount": 0,
        "outsideCount": 0,
        "averageConfidence": 0.0,
        "fps": 0.0,
        "lastEvent": "",
        "boxes": [],
        "detectionEngine": "Off",
        "mode": "line_crossing",
    }


@app.post("/api/stream/start")
def start_stream(req: StartRequest) -> dict[str, Any]:
    camera_id = _normalize_camera_id(req.cameraId)
    purpose = (req.cameraPurpose or "DOOR").strip().upper()
    show_line = _resolve_show_door_line(purpose, req.showDoorLine)
    with _lock:
        existing = _workers.pop(camera_id, None)
        if existing is not None:
            existing.stop()
        orientation = _normalize_door_line_orientation(req.doorLineOrientation)
        worker = CameraWorker(
            rtsp_url=req.rtspUrl.strip(),
            enable_detection=req.enableDetection,
            min_confidence=req.minConfidence,
            zone_divider_percent=req.zoneDividerPercent,
            camera_purpose=purpose,
            show_door_line=show_line,
            door_line_orientation=orientation,
        )
        _workers[camera_id] = worker
        worker.start()
    mode = "occupancy" if not show_line else "line-crossing IN/OUT"
    return {
        "success": True,
        "message": f"Stream started ({mode}).",
        "cameraId": camera_id,
        "cameraPurpose": purpose,
        "showDoorLine": show_line,
        "doorLineOrientation": orientation,
    }


@app.post("/api/stream/stop")
def stop_stream(cameraId: str | None = None) -> dict[str, Any]:
    camera_id = _normalize_camera_id(cameraId) if cameraId else None
    stopped: list[str] = []
    with _lock:
        if camera_id is None:
            for key, worker in list(_workers.items()):
                worker.stop()
                stopped.append(key)
            _workers.clear()
        else:
            worker = _workers.pop(camera_id, None)
            if worker is not None:
                worker.stop()
                stopped.append(camera_id)
    if camera_id is None:
        message = "All streams stopped."
    elif stopped:
        message = f"Stream stopped for camera {camera_id}."
    else:
        message = f"No active stream for camera {camera_id}."
    return {"success": True, "message": message, "stopped": stopped}


@app.get("/api/stream/status")
def stream_status(cameraId: str = "default") -> dict[str, Any]:
    camera_id = _normalize_camera_id(cameraId)
    with _lock:
        worker = _workers.get(camera_id)
        if worker is None:
            return _idle_status(camera_id)
        payload = worker.snapshot()
    payload["cameraId"] = camera_id
    return payload


@app.get("/api/streams/status")
def streams_status() -> dict[str, Any]:
    with _lock:
        items = {
            camera_id: worker.snapshot()
            for camera_id, worker in _workers.items()
        }
    return {
        "success": True,
        "count": len(items),
        "streams": items,
    }


@app.get("/api/stream/frame.jpg")
def stream_frame(cameraId: str = "default") -> Response:
    camera_id = _normalize_camera_id(cameraId)
    with _lock:
        worker = _workers.get(camera_id)
        if worker is None:
            raise HTTPException(status_code=404, detail="No active stream.")
        data = worker.jpeg()
    if not data:
        raise HTTPException(status_code=404, detail="No frame yet.")
    return Response(content=data, media_type="image/jpeg")
