using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using DashboardService.Models;

namespace DashboardService.Services;

/// <summary>
/// Client for Python camera_service (YOLOv8 person detection over RTSP).
/// </summary>
public sealed class CameraPythonLiveService : IDisposable
{
    private readonly ConfigurationService _configurationService = new();
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event Action<BitmapSource, CameraDetectionStats>? FrameReady;

    public CameraPythonLiveService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string baseUrl = _configurationService.GetCameraServiceBaseUrl();
            using var response = await _http.GetAsync($"{baseUrl}/api/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task StartAsync(
        string rtspUrl,
        bool enableDetection,
        double minConfidence,
        int zoneDividerPercent,
        string? cameraPurpose = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync();

        string baseUrl = _configurationService.GetCameraServiceBaseUrl();
        string purpose = string.IsNullOrWhiteSpace(cameraPurpose)
            ? "DOOR"
            : cameraPurpose.Trim().ToUpperInvariant();
        bool showDoorLine = purpose is "ENTRY" or "EXIT" or "DOOR";

        var payload = new StartStreamRequest
        {
            RtspUrl = rtspUrl,
            EnableDetection = enableDetection,
            MinConfidence = minConfidence,
            ZoneDividerPercent = zoneDividerPercent,
            CameraPurpose = purpose,
            ShowDoorLine = showDoorLine
        };

        using var response = await _http.PostAsJsonAsync(
            $"{baseUrl}/api/stream/start",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Camera service start failed ({(int)response.StatusCode}): {body}");
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                if (_loopTask != null)
                {
                    await Task.WhenAny(_loopTask, Task.Delay(1500));
                }
            }
            catch
            {
                // ignored
            }

            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }

        try
        {
            string baseUrl = _configurationService.GetCameraServiceBaseUrl();
            using var response = await _http.PostAsync($"{baseUrl}/api/stream/stop", null);
        }
        catch
        {
            // ignored
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        string baseUrl = _configurationService.GetCameraServiceBaseUrl();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await _http.GetFromJsonAsync<StatusResponse>(
                    $"{baseUrl}/api/stream/status",
                    cancellationToken);

                BitmapSource? frame = null;
                try
                {
                    byte[] bytes = await _http.GetByteArrayAsync(
                        $"{baseUrl}/api/stream/frame.jpg",
                        cancellationToken);
                    frame = BytesToBitmap(bytes);
                }
                catch
                {
                    // Status can still update without a frame.
                }

                var stats = new CameraDetectionStats
                {
                    IsConnected = status?.Connected == true,
                    StatusMessage = status?.Message ?? "Waiting...",
                    TotalDetected = status?.TotalDetected ?? 0,
                    InsideCount = status?.InsideCount ?? 0,
                    OutsideCount = status?.OutsideCount ?? 0,
                    AverageConfidence = status?.AverageConfidence ?? 0,
                    Fps = status?.Fps ?? 0
                };

                frame ??= CreatePlaceholder(stats.StatusMessage);
                FrameReady?.Invoke(frame, stats);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var stats = new CameraDetectionStats
                {
                    IsConnected = false,
                    StatusMessage = $"Camera service error: {ex.Message}"
                };
                FrameReady?.Invoke(CreatePlaceholder(stats.StatusMessage), stats);
            }

            try
            {
                await Task.Delay(120, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static BitmapSource BytesToBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource CreatePlaceholder(string message)
    {
        // 1x1 dark pixel fallback when OpenCV placeholder is unavailable on this path.
        var pixels = new byte[] { 24, 28, 36, 255 };
        var source = BitmapSource.Create(
            1,
            1,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            4);
        source.Freeze();
        return source;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _http.Dispose();
    }

    private sealed class StartStreamRequest
    {
        [JsonPropertyName("rtspUrl")]
        public string RtspUrl { get; set; } = string.Empty;

        [JsonPropertyName("enableDetection")]
        public bool EnableDetection { get; set; }

        [JsonPropertyName("minConfidence")]
        public double MinConfidence { get; set; }

        [JsonPropertyName("zoneDividerPercent")]
        public int ZoneDividerPercent { get; set; }

        [JsonPropertyName("cameraPurpose")]
        public string CameraPurpose { get; set; } = "DOOR";

        [JsonPropertyName("showDoorLine")]
        public bool ShowDoorLine { get; set; }
    }

    private sealed class StatusResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("connected")]
        public bool Connected { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("totalDetected")]
        public int TotalDetected { get; set; }

        [JsonPropertyName("insideCount")]
        public int InsideCount { get; set; }

        [JsonPropertyName("outsideCount")]
        public int OutsideCount { get; set; }

        [JsonPropertyName("averageConfidence")]
        public double AverageConfidence { get; set; }

        [JsonPropertyName("fps")]
        public double Fps { get; set; }
    }
}
