using System.Windows.Media.Imaging;
using DashboardService.Models;

namespace DashboardService.Services;

/// <summary>
/// Monitors one camera stream: detection stats, RFID verification, DB logging, and alerts.
/// </summary>
public sealed class CameraMonitorSession : IDisposable
{
    private readonly CameraDoorVerificationService _doorVerificationService = new();
    private readonly CameraOccupancyVerificationService _occupancyVerificationService = new();
    private readonly CameraAccessEventService _cameraAccessEventService = new();
    private readonly CameraLiveStreamService _opencvStreamService = new();
    private readonly CameraPythonLiveService _pythonStreamService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly VoiceAnnouncementService _voiceAnnouncementService;
    private readonly object _sync = new();

    private MasterCameraConfig _camera;
    private RfidReader? _linkedReader;
    private bool _verifyBusy;
    private int _latestDetectedCount;
    private int _lastLoggedEntryCount = -1;
    private int _lastLoggedExitCount = -1;
    private int _frameSubscriberCount;
    private CancellationTokenSource? _verifyCts;
    private Task? _verifyTask;
    private bool _started;
    private bool _usePython;

    public CameraMonitorSession(
        MasterCameraConfig camera,
        RfidReader? linkedReader,
        VoiceAnnouncementService voiceAnnouncementService)
    {
        _camera = camera;
        _linkedReader = linkedReader;
        _voiceAnnouncementService = voiceAnnouncementService;

        _opencvStreamService.FrameReady += StreamService_FrameReady;
        _pythonStreamService.FrameReady += StreamService_FrameReady;
        _doorVerificationService.AlertRaised += HandleAlertRaised;
        _occupancyVerificationService.AlertRaised += HandleAlertRaised;
    }

    public long CameraId => _camera.CameraId;

    public MasterCameraConfig Camera
    {
        get
        {
            lock (_sync)
            {
                return _camera;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    public CameraDetectionStats LastStats { get; private set; } = new();

    public string StatusMessage { get; private set; } = "Idle";

    public event Action<BitmapSource, CameraDetectionStats>? FrameReady;

    public event Action<CameraDoorAlert>? AlertRaised;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _cameraAccessEventService.EnsureSchemaAsync();

        var settings = _configurationService.GetCameraLiveSettings();
        _doorVerificationService.Configure(_camera, _linkedReader);
        _occupancyVerificationService.Configure(_camera);

        _pythonStreamService.CameraId = _camera.CameraId.ToString();
        _pythonStreamService.PollFrames = false;

        bool preferPython = settings.UsePythonService;
        bool pythonUp = preferPython && await _pythonStreamService.IsAvailableAsync(cancellationToken);
        _usePython = pythonUp;

        _lastLoggedEntryCount = -1;
        _lastLoggedExitCount = -1;

        if (pythonUp)
        {
            await _pythonStreamService.StartAsync(
                _camera.RtspUrl,
                _camera.PersonDetectionEnabled,
                settings.MinConfidence,
                settings.ZoneDividerPercent,
                _camera.CameraPurpose,
                cancellationToken);
        }
        else
        {
            _opencvStreamService.Start(
                _camera.RtspUrl,
                _camera.PersonDetectionEnabled,
                settings.MinConfidence,
                settings.ZoneDividerPercent,
                settings.DetectEveryNFrames,
                settings.InputSize,
                settings.ModelPath,
                _camera.CameraPurpose);
        }

        _verifyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _verifyTask = Task.Run(() => VerifyLoopAsync(_verifyCts.Token), _verifyCts.Token);

        lock (_sync)
        {
            _started = true;
            StatusMessage = pythonUp
                ? "Background monitoring (Python)"
                : "Background monitoring (OpenCV)";
        }
    }

    public async Task StopAsync()
    {
        lock (_sync)
        {
            _started = false;
            StatusMessage = "Stopped";
        }

        if (_verifyCts != null)
        {
            _verifyCts.Cancel();
            try
            {
                if (_verifyTask != null)
                {
                    await Task.WhenAny(_verifyTask, Task.Delay(1500));
                }
            }
            catch
            {
                // ignored
            }

            _verifyCts.Dispose();
            _verifyCts = null;
            _verifyTask = null;
        }

        _opencvStreamService.Stop();
        await _pythonStreamService.StopAsync();
        _doorVerificationService.Reset();
        _occupancyVerificationService.Reset();
        _lastLoggedEntryCount = -1;
        _lastLoggedExitCount = -1;
    }

    public void UpdateConfiguration(MasterCameraConfig camera, RfidReader? linkedReader)
    {
        lock (_sync)
        {
            _camera = camera;
            _linkedReader = linkedReader;
        }

        _doorVerificationService.Configure(camera, linkedReader);
        _occupancyVerificationService.Configure(camera);
    }

    public void AddFrameSubscriber()
    {
        Interlocked.Increment(ref _frameSubscriberCount);
        _pythonStreamService.PollFrames = true;
    }

    public void RemoveFrameSubscriber()
    {
        int count = Interlocked.Decrement(ref _frameSubscriberCount);
        if (count <= 0)
        {
            Interlocked.Exchange(ref _frameSubscriberCount, 0);
            _pythonStreamService.PollFrames = false;
        }
    }

    private async Task VerifyLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_verifyBusy)
            {
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            _verifyBusy = true;
            try
            {
                await _doorVerificationService.ProcessDueAsync(cancellationToken);

                MasterCameraConfig camera;
                lock (_sync)
                {
                    camera = _camera;
                }

                if (string.Equals(
                        camera.CameraPurpose,
                        "MONITORING",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await _occupancyVerificationService.EvaluateAsync(
                        _latestDetectedCount,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep monitoring alive even if verification query fails.
            }
            finally
            {
                _verifyBusy = false;
            }

            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void StreamService_FrameReady(BitmapSource frame, CameraDetectionStats stats)
    {
        LastStats = stats;
        StatusMessage = stats.StatusMessage;

        _doorVerificationService.ObserveCameraEntryCount(stats.InsideCount);
        _doorVerificationService.ObserveCameraExitCount(stats.OutsideCount);
        PersistCrossingDeltas(stats.InsideCount, stats.OutsideCount);
        _latestDetectedCount = stats.TotalDetected;

        FrameReady?.Invoke(frame, stats);
    }

    private void PersistCrossingDeltas(int entryCount, int exitCount)
    {
        MasterCameraConfig camera;
        lock (_sync)
        {
            camera = _camera;
        }

        if (string.Equals(camera.CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_lastLoggedEntryCount < 0)
        {
            _lastLoggedEntryCount = entryCount;
            _lastLoggedExitCount = exitCount;
            return;
        }

        int entryDelta = entryCount - _lastLoggedEntryCount;
        int exitDelta = exitCount - _lastLoggedExitCount;
        _lastLoggedEntryCount = entryCount;
        _lastLoggedExitCount = exitCount;

        if (entryDelta > 0)
        {
            _ = PersistCrossingSafeAsync(camera, "ENTRY", entryDelta);
        }

        if (exitDelta > 0)
        {
            _ = PersistCrossingSafeAsync(camera, "EXIT", exitDelta);
        }
    }

    private async Task PersistCrossingSafeAsync(MasterCameraConfig camera, string eventType, int delta)
    {
        try
        {
            await _cameraAccessEventService.LogCrossingAsync(camera, eventType, delta);
        }
        catch
        {
            // Keep monitoring if DB write fails.
        }
    }

    private void HandleAlertRaised(CameraDoorAlert alert)
    {
        MasterCameraConfig camera;
        lock (_sync)
        {
            camera = _camera;
        }

        if (alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE"
            or "MATCHED" or "EXIT_MATCHED")
        {
            _ = PersistAlertSafeAsync(camera, alert);
        }

        if (alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE"
            or "OCCUPANCY_NO_RFID" or "OCCUPANCY_MISMATCH")
        {
            if (_configurationService.GetCameraLiveSettings().VoiceEnabled)
            {
                _voiceAnnouncementService.AnnounceOnce(alert.Message, "en-IN");
            }
        }

        AlertRaised?.Invoke(alert);
    }

    private async Task PersistAlertSafeAsync(MasterCameraConfig camera, CameraDoorAlert alert)
    {
        try
        {
            await _cameraAccessEventService.LogAlertAsync(camera, alert);
        }
        catch
        {
            // Keep monitoring if DB write fails.
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _opencvStreamService.Dispose();
        _pythonStreamService.Dispose();
    }
}
