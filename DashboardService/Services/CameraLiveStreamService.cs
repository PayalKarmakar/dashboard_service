using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using DashboardService.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace DashboardService.Services;

public sealed class CameraLiveStreamService : IDisposable
{
    /// <summary>
    /// Per-run state. The worker thread owns the capture and detector and is the only one that disposes them,
    /// so Stop/Start never free native OpenCV objects that are still in use.
    /// </summary>
    private sealed class RunContext
    {
        public volatile bool Running = true;
        public VideoCapture? Capture;
        public YoloPersonDetector? Detector;
        public Task<IReadOnlyList<PersonDetection>>? DetectTask;
    }

    private Thread? _workerThread;
    private RunContext? _run;
    private double _minConfidence = 0.40;
    private int _zoneDividerPercent = 50;
    private int _detectEveryNFrames = 2;
    private int _inputSize = 320;
    private string _modelPath = string.Empty;
    private bool _showDoorLine = true;

    public event Action<BitmapSource, CameraDetectionStats>? FrameReady;

    public void Start(
        string streamUrl,
        bool enableDetection,
        double minConfidence,
        int zoneDividerPercent,
        int detectEveryNFrames = 2,
        int inputSize = 320,
        string? modelPath = null,
        string? cameraPurpose = null)
    {
        Stop();

        _minConfidence = Math.Clamp(minConfidence, 0.1, 0.95);
        _zoneDividerPercent = Math.Clamp(zoneDividerPercent, 20, 80);
        _detectEveryNFrames = Math.Clamp(detectEveryNFrames, 1, 10);
        _inputSize = inputSize <= 0 ? 320 : inputSize;
        _showDoorLine = !string.Equals(cameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase);
        _modelPath = string.IsNullOrWhiteSpace(modelPath)
            ? Path.Combine(AppContext.BaseDirectory, "Models", "Vision", "yolov5n.onnx")
            : modelPath;

        var run = new RunContext();
        _run = run;
        _workerThread = new Thread(() => RunLoopSafe(run, streamUrl, enableDetection))
        {
            IsBackground = true,
            Name = "CameraLiveStream"
        };
        _workerThread.Start();
    }

    public void Stop()
    {
        RunContext? run = _run;
        _run = null;
        if (run != null)
        {
            run.Running = false;
        }

        if (_workerThread != null && _workerThread.IsAlive)
        {
            _workerThread.Join(TimeSpan.FromSeconds(3));
        }

        _workerThread = null;
    }

    private void RunLoopSafe(RunContext run, string streamUrl, bool enableDetection)
    {
        try
        {
            RunLoop(run, streamUrl, enableDetection);
        }
        catch (Exception ex)
        {
            CrashLog.Write("CameraLiveStream worker", ex);
        }
        finally
        {
            try
            {
                run.Capture?.Release();
                run.Capture?.Dispose();
            }
            catch
            {
                // ignore cleanup errors
            }

            run.Capture = null;
            YoloPersonDetector? detector = run.Detector;
            Task? pending = run.DetectTask;
            run.Detector = null;
            run.DetectTask = null;
            if (pending == null || pending.IsCompleted)
            {
                detector?.Dispose();
            }
            else
            {
                pending.ContinueWith(_ => detector?.Dispose(), TaskScheduler.Default);
            }
        }
    }

    private void RunLoop(RunContext run, string streamUrl, bool enableDetection)
    {
        var stats = new CameraDetectionStats
        {
            IsConnected = false,
            StatusMessage = "Connecting..."
        };
        PublishFrame(run, CreatePlaceholder("Connecting to camera..."), stats);

        if (!TryOpenCapture(run, streamUrl, stats))
        {
            return;
        }

        if (enableDetection && run.Running)
        {
            try
            {
                run.Detector = new YoloPersonDetector(_modelPath, _minConfidence, _inputSize);
                stats.StatusMessage = "Live (YOLO)";
            }
            catch (Exception ex)
            {
                stats.StatusMessage = $"YOLO model load failed: {ex.Message}";
                PublishFrame(run, CreatePlaceholder(stats.StatusMessage), stats);
                enableDetection = false;
            }
        }

        stats.IsConnected = true;
        if (string.IsNullOrWhiteSpace(stats.StatusMessage) || stats.StatusMessage.StartsWith("Connecting"))
        {
            stats.StatusMessage = "Live";
        }

        var fpsTimer = Stopwatch.StartNew();
        int frameCount = 0;
        int frameIndex = 0;
        IReadOnlyList<PersonDetection> lastDetections = [];
        int consecutiveFails = 0;

        while (run.Running)
        {
            using var frame = new Mat();
            bool readOk = run.Capture != null && run.Capture.Read(frame) && !frame.Empty();

            if (!run.Running)
            {
                break;
            }

            if (!readOk)
            {
                consecutiveFails++;
                stats.IsConnected = false;
                stats.StatusMessage = "Stream interrupted. Retrying...";
                PublishFrame(run, CreatePlaceholder(stats.StatusMessage), stats);

                if (consecutiveFails >= 5)
                {
                    TryOpenCapture(run, streamUrl, stats);
                    consecutiveFails = 0;
                }

                Thread.Sleep(200);
                continue;
            }

            consecutiveFails = 0;
            stats.IsConnected = true;
            stats.StatusMessage = enableDetection ? "Live (YOLO)" : "Live";

            frameIndex++;
            if (run.DetectTask is { IsCompleted: true } finished)
            {
                lastDetections = finished.IsCompletedSuccessfully ? finished.Result : [];
                run.DetectTask = null;
            }

            // Inference takes far longer than a frame interval, so it runs off the read loop;
            // blocking reads would let the RTSP buffer grow and the preview fall behind.
            if (enableDetection
                && run.Detector != null
                && run.DetectTask == null
                && frameIndex % _detectEveryNFrames == 0)
            {
                YoloPersonDetector detector = run.Detector;
                Mat detectFrame = frame.Clone();
                run.DetectTask = Task.Run(() =>
                {
                    using (detectFrame)
                    {
                        return detector.Detect(detectFrame);
                    }
                });
            }

            if (enableDetection)
            {
                ApplyDetections(frame, lastDetections, stats);
            }
            else
            {
                stats.TotalDetected = 0;
                stats.InsideCount = 0;
                stats.OutsideCount = 0;
                stats.AverageConfidence = 0;
            }

            frameCount++;
            if (fpsTimer.Elapsed.TotalSeconds >= 1)
            {
                stats.Fps = frameCount / fpsTimer.Elapsed.TotalSeconds;
                frameCount = 0;
                fpsTimer.Restart();
            }

            DrawOverlay(frame, stats, enableDetection);
            PublishFrame(run, BitmapSourceConverter.ToBitmapSource(frame), stats);
        }
    }

    private bool TryOpenCapture(RunContext run, string streamUrl, CameraDetectionStats stats)
    {
        run.Capture?.Release();
        run.Capture?.Dispose();
        run.Capture = new VideoCapture(streamUrl, VideoCaptureAPIs.FFMPEG);
        run.Capture.Set(VideoCaptureProperties.BufferSize, 1);
        run.Capture.Set(VideoCaptureProperties.Fps, 15);

        if (!run.Capture.IsOpened())
        {
            stats.IsConnected = false;
            stats.StatusMessage = "Camera not reachable. Check RTSP URL and network.";
            PublishFrame(run, CreatePlaceholder(stats.StatusMessage), stats);
            return false;
        }

        return true;
    }

    private void ApplyDetections(
        Mat frame,
        IReadOnlyList<PersonDetection> detections,
        CameraDetectionStats stats)
    {
        double lineX = frame.Width * _zoneDividerPercent / 100.0;
        int inside = 0;
        int outside = 0;
        double confidenceSum = 0;

        foreach (var detection in detections)
        {
            Rect rect = detection.Box;
            double centerX = rect.X + rect.Width / 2.0;

            if (centerX < lineX)
            {
                outside++;
            }
            else
            {
                inside++;
            }

            confidenceSum += detection.Confidence;

            Cv2.Rectangle(frame, rect, new Scalar(0, 220, 80), 2);
            Cv2.PutText(
                frame,
                $"{detection.Confidence:F0}%",
                new Point(rect.X, Math.Max(18, rect.Y - 6)),
                HersheyFonts.HersheySimplex,
                0.55,
                new Scalar(0, 220, 80),
                2);
        }

        stats.TotalDetected = detections.Count;
        stats.InsideCount = inside;
        stats.OutsideCount = outside;
        stats.AverageConfidence = detections.Count == 0 ? 0 : confidenceSum / detections.Count;
    }

    private void DrawOverlay(Mat frame, CameraDetectionStats stats, bool detectionEnabled)
    {
        if (_showDoorLine)
        {
            double lineX = frame.Width * _zoneDividerPercent / 100.0;
            Cv2.Line(
                frame,
                new Point(lineX, 0),
                new Point(lineX, frame.Height),
                new Scalar(0, 220, 255),
                2);

            Cv2.PutText(
                frame,
                "OUTSIDE",
                new Point(12, 28),
                HersheyFonts.HersheySimplex,
                0.8,
                new Scalar(0, 220, 255),
                2);

            Cv2.PutText(
                frame,
                "INSIDE",
                new Point(lineX + 12, 28),
                HersheyFonts.HersheySimplex,
                0.8,
                new Scalar(0, 220, 255),
                2);
        }

        string summary = _showDoorLine
            ? $"Detected: {stats.TotalDetected} | In: {stats.InsideCount} | Out: {stats.OutsideCount} | Acc: {stats.AccuracyDisplay}"
            : $"Persons: {stats.TotalDetected} | Acc: {stats.AccuracyDisplay}";
        Cv2.PutText(
            frame,
            summary,
            new Point(12, frame.Height - 16),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(255, 255, 255),
            2);
    }

    private static BitmapSource CreatePlaceholder(string message)
    {
        using var mat = new Mat(360, 640, MatType.CV_8UC3, new Scalar(24, 28, 36));
        Cv2.PutText(
            mat,
            message,
            new Point(24, 180),
            HersheyFonts.HersheySimplex,
            0.7,
            new Scalar(200, 200, 200),
            2);

        var bitmap = BitmapSourceConverter.ToBitmapSource(mat);
        bitmap.Freeze();
        return bitmap;
    }

    private void PublishFrame(RunContext run, BitmapSource frame, CameraDetectionStats stats)
    {
        if (!run.Running)
        {
            return;
        }

        frame.Freeze();
        FrameReady?.Invoke(frame, stats);
    }

    public void Dispose()
    {
        Stop();
    }
}
