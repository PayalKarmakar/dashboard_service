using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using DashboardService.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace DashboardService.Services;

public sealed class CameraLiveStreamService : IDisposable
{
    private readonly object _sync = new();
    private Thread? _workerThread;
    private volatile bool _running;
    private VideoCapture? _capture;
    private YoloPersonDetector? _detector;
    private double _minConfidence = 0.40;
    private int _zoneDividerPercent = 50;
    private int _detectEveryNFrames = 2;
    private int _inputSize = 320;
    private string _modelPath = string.Empty;

    public event Action<BitmapSource, CameraDetectionStats>? FrameReady;

    public void Start(
        string streamUrl,
        bool enableDetection,
        double minConfidence,
        int zoneDividerPercent,
        int detectEveryNFrames = 2,
        int inputSize = 320,
        string? modelPath = null)
    {
        Stop();

        _minConfidence = Math.Clamp(minConfidence, 0.1, 0.95);
        _zoneDividerPercent = Math.Clamp(zoneDividerPercent, 20, 80);
        _detectEveryNFrames = Math.Clamp(detectEveryNFrames, 1, 10);
        _inputSize = inputSize <= 0 ? 320 : inputSize;
        _modelPath = string.IsNullOrWhiteSpace(modelPath)
            ? Path.Combine(AppContext.BaseDirectory, "Models", "Vision", "yolov5n.onnx")
            : modelPath;
        _running = true;

        _workerThread = new Thread(() => RunLoop(streamUrl, enableDetection))
        {
            IsBackground = true,
            Name = "CameraLiveStream"
        };
        _workerThread.Start();
    }

    public void Stop()
    {
        _running = false;

        lock (_sync)
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            _detector?.Dispose();
            _detector = null;
        }

        if (_workerThread != null && _workerThread.IsAlive)
        {
            _workerThread.Join(TimeSpan.FromSeconds(3));
        }

        _workerThread = null;
    }

    private void RunLoop(string streamUrl, bool enableDetection)
    {
        var stats = new CameraDetectionStats
        {
            IsConnected = false,
            StatusMessage = "Connecting..."
        };
        PublishFrame(CreatePlaceholder("Connecting to camera..."), stats);

        if (!TryOpenCapture(streamUrl, stats))
        {
            return;
        }

        if (enableDetection)
        {
            try
            {
                _detector = new YoloPersonDetector(_modelPath, _minConfidence, _inputSize);
                stats.StatusMessage = "Live (YOLO)";
            }
            catch (Exception ex)
            {
                stats.StatusMessage = $"YOLO model load failed: {ex.Message}";
                PublishFrame(CreatePlaceholder(stats.StatusMessage), stats);
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

        while (_running)
        {
            using var frame = new Mat();
            bool readOk;

            lock (_sync)
            {
                readOk = _capture != null && _capture.Read(frame) && !frame.Empty();
            }

            if (!readOk)
            {
                consecutiveFails++;
                stats.IsConnected = false;
                stats.StatusMessage = "Stream interrupted. Retrying...";
                PublishFrame(CreatePlaceholder(stats.StatusMessage), stats);

                if (consecutiveFails >= 5)
                {
                    TryOpenCapture(streamUrl, stats);
                    consecutiveFails = 0;
                }

                Thread.Sleep(200);
                continue;
            }

            consecutiveFails = 0;
            stats.IsConnected = true;
            stats.StatusMessage = enableDetection ? "Live (YOLO)" : "Live";

            frameIndex++;
            if (enableDetection && _detector != null && frameIndex % _detectEveryNFrames == 0)
            {
                try
                {
                    lastDetections = _detector.Detect(frame);
                }
                catch
                {
                    // Keep last good detections; never crash the UI thread/worker.
                    lastDetections = [];
                }
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
            PublishFrame(BitmapSourceConverter.ToBitmapSource(frame), stats);
        }
    }

    private bool TryOpenCapture(string streamUrl, CameraDetectionStats stats)
    {
        lock (_sync)
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = new VideoCapture(streamUrl, VideoCaptureAPIs.FFMPEG);
            _capture.Set(VideoCaptureProperties.BufferSize, 1);
            _capture.Set(VideoCaptureProperties.Fps, 15);
        }

        if (_capture == null || !_capture.IsOpened())
        {
            stats.IsConnected = false;
            stats.StatusMessage = "Camera not reachable. Check RTSP URL and network.";
            PublishFrame(CreatePlaceholder(stats.StatusMessage), stats);
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
        if (!detectionEnabled)
        {
            return;
        }

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

        string summary =
            $"Detected: {stats.TotalDetected} | In: {stats.InsideCount} | Out: {stats.OutsideCount} | Acc: {stats.AccuracyDisplay}";
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

    private void PublishFrame(BitmapSource frame, CameraDetectionStats stats)
    {
        frame.Freeze();
        FrameReady?.Invoke(frame, stats);
    }

    public void Dispose()
    {
        Stop();
    }
}
