using DashboardService.Models;

namespace DashboardService.Services;

/// <summary>
/// Runs entry/exit and occupancy monitoring for all active cameras in the background.
/// </summary>
public sealed class CameraBackgroundMonitoringService : IDisposable
{
    private static readonly Lazy<CameraBackgroundMonitoringService> LazyInstance =
        new(() => new CameraBackgroundMonitoringService());

    private readonly CameraConfigurationService _cameraService = new();
    private readonly RfidReaderService _readerService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly VoiceAnnouncementService _voiceAnnouncementService = new();
    private readonly object _sync = new();
    private readonly Dictionary<long, CameraMonitorSession> _sessions = new();

    private bool _started;

    private CameraBackgroundMonitoringService()
    {
    }

    public static CameraBackgroundMonitoringService Instance => LazyInstance.Value;

    public bool IsEnabled =>
        _configurationService.GetCameraLiveSettings().BackgroundMonitoringEnabled;

    public event Action<long, CameraDoorAlert>? SessionAlertRaised;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        try
        {
            await ReloadAsync(cancellationToken);
        }
        catch
        {
            lock (_sync)
            {
                _started = false;
            }

            throw;
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        var cameras = await _cameraService.GetAllAsync();
        var readers = await _readerService.GetAllAsync();
        var activeCameras = cameras
            .Where(c => c.IsActive && c.PersonDetectionEnabled && !string.IsNullOrWhiteSpace(c.RtspUrl))
            .ToList();

        List<CameraMonitorSession> toStop;
        lock (_sync)
        {
            var keepIds = activeCameras.Select(c => c.CameraId).ToHashSet();
            toStop = _sessions
                .Where(pair => !keepIds.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToList();

            foreach (long cameraId in _sessions.Keys.Where(id => !keepIds.Contains(id)).ToList())
            {
                _sessions.Remove(cameraId);
            }
        }

        foreach (CameraMonitorSession session in toStop)
        {
            await session.StopAsync();
            session.Dispose();
        }

        foreach (MasterCameraConfig camera in activeCameras)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RfidReader? linkedReader = camera.RfidReaderId is > 0
                ? readers.FirstOrDefault(r => r.ReaderId == camera.RfidReaderId.Value)
                : null;

            CameraMonitorSession? session;
            lock (_sync)
            {
                _sessions.TryGetValue(camera.CameraId, out session);
            }

            if (session == null)
            {
                session = new CameraMonitorSession(camera, linkedReader, _voiceAnnouncementService);
                session.AlertRaised += alert => SessionAlertRaised?.Invoke(camera.CameraId, alert);

                lock (_sync)
                {
                    _sessions[camera.CameraId] = session;
                }

                try
                {
                    await session.StartAsync(cancellationToken);
                }
                catch
                {
                    lock (_sync)
                    {
                        _sessions.Remove(camera.CameraId);
                    }

                    session.Dispose();
                }

                continue;
            }

            session.UpdateConfiguration(camera, linkedReader);
        }
    }

    public async Task<CameraMonitorSession?> EnsureSessionAsync(
        long cameraId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        CameraMonitorSession? session;
        lock (_sync)
        {
            _sessions.TryGetValue(cameraId, out session);
        }

        if (session == null || !session.IsRunning)
        {
            await ReloadAsync(cancellationToken);
            lock (_sync)
            {
                _sessions.TryGetValue(cameraId, out session);
            }
        }

        if (session == null)
        {
            return null;
        }

        await session.EnsureLineCrossingModeAsync(cancellationToken);
        return session;
    }

    public CameraMonitorSession? GetSession(long cameraId)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(cameraId, out CameraMonitorSession? session)
                ? session
                : null;
        }
    }

    public IReadOnlyList<CameraMonitorStatus> GetStatuses()
    {
        lock (_sync)
        {
            return _sessions.Values
                .Select(session => new CameraMonitorStatus
                {
                    CameraId = session.CameraId,
                    CameraName = session.Camera.CameraName,
                    ChamberName = session.Camera.ChamberName,
                    Purpose = session.Camera.CameraPurpose,
                    IsRunning = session.IsRunning,
                    StatusMessage = session.StatusMessage,
                    DetectedCount = session.LastStats.TotalDetected,
                    IsConnected = session.LastStats.IsConnected
                })
                .OrderBy(status => status.CameraName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public async Task StopAsync()
    {
        List<CameraMonitorSession> sessions;
        lock (_sync)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
            _started = false;
        }

        foreach (CameraMonitorSession session in sessions)
        {
            await session.StopAsync();
            session.Dispose();
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _voiceAnnouncementService.Dispose();
    }
}

public sealed class CameraMonitorStatus
{
    public long CameraId { get; init; }

    public string CameraName { get; init; } = string.Empty;

    public string ChamberName { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public bool IsRunning { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public int DetectedCount { get; init; }

    public bool IsConnected { get; init; }
}
