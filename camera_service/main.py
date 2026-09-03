"""
Camera person-detection service (YOLOv8 + movement line-crossing).
Tracks persons and counts ENTRY / EXIT when they cross the door line.
"""

from __future__ import annotations

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

app = FastAPI(title="SRP Camera Detection Service", version="1.1.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

_lock = threading.Lock()
_worker: "CameraWorker | None" = None
_model: YOLO | None = None
_model_lock = threading.Lock()


def get_model() -> YOLO:
    global _model
    with _model_lock:
        if _model is None:
            _model = YOLO("yolov8n.pt")
        return _model


class StartRequest(BaseModel):
    rtspUrl: str = Field(min_length=3)
    enableDetection: bool = True
    minConfidence: float = 0.40
    zoneDividerPercent: int = 50
    cameraPurpose: str = "DOOR"


class CameraWorker:
    def __init__(
        self,
        rtsp_url: str,
        enable_detection: bool,
        min_confidence: float,
        zone_divider_percent: int,
        camera_purpose: str = "DOOR",
    ) -> None:
        self.rtsp_url = rtsp_url
        self.enable_detection = enable_detection
        self.min_confidence = max(0.1, min(0.95, min_confidence))
        self.zone_divider_percent = max(20, min(80, zone_divider_percent))
        self.camera_purpose = (camera_purpose or "DOOR").strip().upper()
        self.show_door_line = self.camera_purpose != "MONITORING"
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
        self._cross_cooldown_sec = 1.5

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
        detect_every = 2
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
                self.status_message = "Live (YOLO track)" if self.enable_detection else "Live"
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
            self.status_message = "Live (YOLO track)" if self.enable_detection else "Live"
            frame_index += 1

            if self.enable_detection and frame_index % detect_every == 0:
                last_boxes = self._track_and_count(frame)

            self._apply_overlay(frame, last_boxes)

            ok_jpg, buf = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), 75])
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

    def _side_of(self, cx: float, line_x: float) -> str:
        # Left of door line = OUTSIDE, right = INSIDE
        return "OUT" if cx < line_x else "IN"

    def _track_and_count(
        self, frame: np.ndarray
    ) -> list[tuple[int, int, int, int, float, int]]:
        model = get_model()
        h, w = frame.shape[:2]
        line_x = w * self.zone_divider_percent / 100.0
        now = time.time()

        # ByteTrack gives stable IDs for line-crossing.
        results = model.track(
            source=frame,
            conf=self.min_confidence,
            classes=[0],
            verbose=False,
            imgsz=320,
            persist=True,
            tracker="bytetrack.yaml",
        )

        boxes: list[tuple[int, int, int, int, float, int]] = []
        conf_sum = 0.0
        seen_ids: set[int] = set()

        if results:
            r0 = results[0]
            if r0.boxes is not None and len(r0.boxes) > 0:
                ids = r0.boxes.id
                for i, box in enumerate(r0.boxes):
                    xyxy = box.xyxy[0].tolist()
                    conf = float(box.conf[0].item()) * 100.0
                    x1, y1, x2, y2 = map(int, xyxy)
                    cx = (x1 + x2) / 2.0
                    track_id = -1
                    if ids is not None:
                        track_id = int(ids[i].item())
                        seen_ids.add(track_id)
                        if self.show_door_line:
                            side = self._side_of(cx, line_x)
                            prev = self._track_side.get(track_id)
                            last_cross = self._track_last_cross_ts.get(track_id, 0.0)

                            if prev is not None and prev != side and (now - last_cross) >= self._cross_cooldown_sec:
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

        # Drop stale track sides for IDs not seen recently
        stale = [tid for tid in self._track_side if tid not in seen_ids]
        for tid in stale:
            # keep briefly; remove if missing long — simple cleanup
            if now - self._track_last_cross_ts.get(tid, now) > 5.0 and tid not in seen_ids:
                self._track_side.pop(tid, None)

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
        line_x = int(w * self.zone_divider_percent / 100.0)

        if self.enable_detection:
            if self.show_door_line:
                cv2.line(frame, (line_x, 0), (line_x, h), (0, 220, 255), 2)
                cv2.putText(
                    frame,
                    "OUT  -->",
                    (max(8, line_x - 110), 28),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.7,
                    (0, 220, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    "<--  IN",
                    (line_x + 12, 28),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.7,
                    (0, 220, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    "DOOR LINE",
                    (max(8, line_x - 55), 54),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (0, 220, 255),
                    1,
                )

            for x1, y1, x2, y2, conf, track_id in boxes:
                cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 220, 80), 2)
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
    return {"success": True, "message": "Camera service is running.", "mode": "line_crossing"}


@app.post("/api/stream/start")
def start_stream(req: StartRequest) -> dict[str, Any]:
    global _worker
    with _lock:
        if _worker is not None:
            _worker.stop()
        _worker = CameraWorker(
            rtsp_url=req.rtspUrl.strip(),
            enable_detection=req.enableDetection,
            min_confidence=req.minConfidence,
            zone_divider_percent=req.zoneDividerPercent,
            camera_purpose=req.cameraPurpose,
        )
        _worker.start()
    mode = "occupancy" if _worker.show_door_line is False else "line-crossing IN/OUT"
    return {"success": True, "message": f"Stream started ({mode})."}


@app.post("/api/stream/stop")
def stop_stream() -> dict[str, Any]:
    global _worker
    with _lock:
        if _worker is not None:
            _worker.stop()
            _worker = None
    return {"success": True, "message": "Stream stopped."}


@app.get("/api/stream/status")
def stream_status() -> dict[str, Any]:
    with _lock:
        if _worker is None:
            return {
                "success": True,
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
        return _worker.snapshot()


@app.get("/api/stream/frame.jpg")
def stream_frame() -> Response:
    with _lock:
        if _worker is None:
            raise HTTPException(status_code=404, detail="No active stream.")
        data = _worker.jpeg()
    if not data:
        raise HTTPException(status_code=404, detail="No frame yet.")
    return Response(content=data, media_type="image/jpeg")
