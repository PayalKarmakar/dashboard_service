using System.Diagnostics;
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
    private HOGDescriptor? _hog;
    private double _minConfidence = 0.45;
    private int _zoneDividerPercent = 50;

    public event Action<BitmapSource, CameraDetectionStats>? FrameReady;

    public void Start(string streamUrl, bool enableDetection, double minConfidence, int zoneDividerPercent)
    {
        Stop();

        _minConfidence = Math.Clamp(minConfidence, 0.1, 0.95);
        _zoneDividerPercent = Math.Clamp(zoneDividerPercent, 20, 80);
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
            _hog?.Dispose();
            _hog = null;
        }

        if (_workerThread != null && _workerThread.IsAlive)
        {
            _workerThread.Join(TimeSpan.FromSeconds(2));
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

        lock (_sync)
        {
            _capture = new VideoCapture(streamUrl);
        }

        if (_capture == null || !_capture.IsOpened())
        {
            stats.StatusMessage = "Camera not reachable. Check RTSP URL and network.";
            PublishFrame(CreatePlaceholder(stats.StatusMessage), stats);
            return;
        }

        if (enableDetection)
        {
            _hog = new HOGDescriptor();
            _hog.SetSVMDetector(HOGDescriptor.GetDefaultPeopleDetector());
        }

        stats.IsConnected = true;
        stats.StatusMessage = "Live";

        var fpsTimer = Stopwatch.StartNew();
        int frameCount = 0;

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
                stats.IsConnected = false;
                stats.StatusMessage = "Stream interrupted. Retrying...";
                PublishFrame(CreatePlaceholder(stats.StatusMessage), stats);
                Thread.Sleep(500);
                continue;
            }

            stats.IsConnected = true;
            stats.StatusMessage = "Live";

            if (enableDetection && _hog != null)
            {
                ApplyPersonDetection(frame, stats);
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

            using var display = frame.Clone();
            DrawOverlay(display, stats, enableDetection);
            PublishFrame(BitmapSourceConverter.ToBitmapSource(display), stats);
            Thread.Sleep(33);
        }
    }

    private void ApplyPersonDetection(Mat frame, CameraDetectionStats stats)
    {
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

        var rects = _hog!.DetectMultiScale(
            gray,
            out double[] weights,
            hitThreshold: _minConfidence,
            winStride: new Size(8, 8),
            padding: new Size(8, 8),
            scale: 1.05,
            groupThreshold: 2);

        double lineX = frame.Width * _zoneDividerPercent / 100.0;
        int inside = 0;
        int outside = 0;
        double confidenceSum = 0;

        for (int i = 0; i < rects.Length; i++)
        {
            Rect rect = rects[i];
            double centerX = rect.X + rect.Width / 2.0;

            if (centerX < lineX)
            {
                outside++;
            }
            else
            {
                inside++;
            }

            double confidence = NormalizeConfidence(weights.Length > i ? weights[i] : 0);
            confidenceSum += confidence;

            Cv2.Rectangle(frame, rect, new Scalar(0, 220, 80), 2);
            Cv2.PutText(
                frame,
                $"{confidence:F0}%",
                new Point(rect.X, Math.Max(18, rect.Y - 6)),
                HersheyFonts.HersheySimplex,
                0.55,
                new Scalar(0, 220, 80),
                2);
        }

        stats.TotalDetected = rects.Length;
        stats.InsideCount = inside;
        stats.OutsideCount = outside;
        stats.AverageConfidence = rects.Length == 0 ? 0 : confidenceSum / rects.Length;
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

    private static double NormalizeConfidence(double weight)
    {
        // HOG SVM weights are typically around 0-1 for strong hits.
        return Math.Clamp(weight * 100, 0, 100);
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
