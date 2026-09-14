using DashboardService.Models;
using System.Windows;
using System.Windows.Threading;

namespace DashboardService.Services;

/// <summary>
/// Keeps employee and sensor alerts running after leaving the dashboard page.
/// Started on login, stopped on logout.
/// </summary>
public sealed class LiveAlertHost
{
    private static readonly Lazy<LiveAlertHost> LazyInstance = new(() => new LiveAlertHost());

    private readonly MonitoringService _monitoringService = new();
    private readonly AlertMessageService _alertMessageService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly SystemLogStatusService _systemLogStatusService = new();
    private readonly HashSet<long> _announcementInFlight = new();
    private readonly HashSet<long> _enqueuedAlertIds = new();
    private readonly SemaphoreSlim _announcementProcessLock = new(1, 1);
    private readonly object _sync = new();

    private DispatcherTimer? _dueTimer;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _sensorTimer;
    private List<Employee> _members = new();
    private bool _anySensorConnected;
    private bool _started;

    private LiveAlertHost()
    {
        Voice = new VoiceAnnouncementService(alertId =>
            _monitoringService.MarkAnnouncementPlayedAsync(alertId));
    }

    public static LiveAlertHost Instance => LazyInstance.Value;

    public VoiceAnnouncementService Voice { get; }

    public HashSet<long> UserStoppedVoice { get; } = new();

    public bool SensorVoiceEnabled { get; set; } = true;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            var sensorSettings = _configurationService.GetSensorAlertSettings();

            _dueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _dueTimer.Tick += async (_, _) => await TickDueAnnouncementsAsync();
            _dueTimer.Start();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _refreshTimer.Tick += async (_, _) => await TickRefreshAsync();
            _refreshTimer.Start();

            _sensorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(sensorSettings.SensorViolationDbCheckIntervalSeconds)
            };
            _sensorTimer.Tick += async (_, _) => await TickSensorAlertsAsync();
            _sensorTimer.Start();
        });

        _ = TickRefreshAsync();
        _ = TickSensorAlertsAsync();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _started = false;
        }

        void StopTimer(ref DispatcherTimer? timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Stop();
            timer = null;
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            StopTimer(ref _dueTimer);
            StopTimer(ref _refreshTimer);
            StopTimer(ref _sensorTimer);
        }
        else
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                StopTimer(ref _dueTimer);
                StopTimer(ref _refreshTimer);
                StopTimer(ref _sensorTimer);
            });
        }

        Voice.StopAll();
        UserStoppedVoice.Clear();
        _enqueuedAlertIds.Clear();
        _announcementInFlight.Clear();
        _members = new();
        SensorVoiceEnabled = true;
    }

    public void DisposeVoice()
    {
        Stop();
        Voice.Dispose();
    }

    public void StartVoiceLoop(AnnouncementRequest announcement)
    {
        if (announcement.TransactionId <= 0 ||
            string.IsNullOrWhiteSpace(announcement.Message))
        {
            return;
        }

        var settings = _configurationService.GetAlertSettings();
        if (!settings.VoiceEnabled)
        {
            if (announcement.AlertId > 0)
            {
                _ = _monitoringService.MarkAnnouncementPlayedAsync(announcement.AlertId);
            }

            return;
        }

        _enqueuedAlertIds.Add(announcement.AlertId);
        UserStoppedVoice.Remove(announcement.TransactionId);

        bool limitedLoop =
            string.Equals(announcement.AlertType, MonitoringService.WarningType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(announcement.AlertType, MonitoringService.HalfTimeType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(announcement.AlertType, MonitoringService.AttentionType, StringComparison.OrdinalIgnoreCase);

        Voice.StartLooping(
            announcement.TransactionId,
            announcement.GetVoiceLines(AlertMessageService.CultureEnglishIndia),
            announcement.AlertId,
            maxSpeakCount: limitedLoop ? 2 : null);
    }

    private async Task TickRefreshAsync()
    {
        try
        {
            var members = await _monitoringService.GetMembersInsideAsync(MemberFilter.Current);
            var insideIds = members.Select(x => x.TransactionId).ToHashSet();

            foreach (var previous in _members)
            {
                if (!insideIds.Contains(previous.TransactionId))
                {
                    Voice.Stop(previous.TransactionId);
                    UserStoppedVoice.Remove(previous.TransactionId);
                }
            }

            _members = members;
            await EnqueueUnplayedAnnouncementsAsync();
            await ProcessDueAnnouncementsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live alert refresh failed: {ex.Message}");
        }
    }

    private async Task TickDueAnnouncementsAsync()
    {
        try
        {
            foreach (var employee in _members)
            {
                DateTime allowedExitTime = employee.EntryTime.AddMinutes(employee.TimeThresholdMinutes);
                employee.RemainingTime = allowedExitTime - DateTime.Now;
            }

            await ProcessDueAnnouncementsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live alert due-check failed: {ex.Message}");
        }
    }

    private async Task TickSensorAlertsAsync()
    {
        try
        {
            var statuses = await _systemLogStatusService.GetLatestSensorStatusesAsync();
            _anySensorConnected = statuses.Any(x => x.IsConnected);

            if (!_anySensorConnected || !SensorVoiceEnabled)
            {
                return;
            }

            var violations = await _monitoringService.GetActiveSensorViolationsAsync(1);
            await ProcessSensorAnnouncementsAsync(violations);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live sensor alert failed: {ex.Message}");
        }
    }

    private async Task EnqueueUnplayedAnnouncementsAsync()
    {
        var pending = await _monitoringService.GetUnplayedAnnouncementsAsync();
        foreach (var announcement in pending)
        {
            StartVoiceLoop(announcement);
        }
    }

    private async Task ProcessDueAnnouncementsAsync()
    {
        if (!await _announcementProcessLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            foreach (var employee in _members.ToList())
            {
                if (employee.TransactionId <= 0)
                {
                    continue;
                }

                if (Voice.IsPlaying(employee.TransactionId))
                {
                    continue;
                }

                if (!_announcementInFlight.Add(employee.TransactionId))
                {
                    continue;
                }

                try
                {
                    if (ShouldKeepContinueVoice(employee))
                    {
                        AnnouncementRequest? live = await _monitoringService.BuildLiveAnnouncementAsync(
                            employee,
                            MonitoringService.ViolationType);

                        if (live != null && !string.IsNullOrWhiteSpace(live.Message))
                        {
                            StartVoiceLoop(live);
                        }

                        continue;
                    }

                    AnnouncementRequest? announcement =
                        await _monitoringService.TryCreateDueAnnouncementAsync(employee);

                    if (announcement != null &&
                        !string.IsNullOrWhiteSpace(announcement.Message))
                    {
                        StartVoiceLoop(announcement);
                    }
                }
                catch
                {
                }
                finally
                {
                    _announcementInFlight.Remove(employee.TransactionId);
                }
            }
        }
        finally
        {
            _announcementProcessLock.Release();
        }
    }

    private bool ShouldKeepContinueVoice(Employee employee)
    {
        if (!employee.ViolationAudioEnabled ||
            !ChamberAlertRule.IsContinue(employee.ViolationMaxPlayCount) ||
            UserStoppedVoice.Contains(employee.TransactionId))
        {
            return false;
        }

        return string.Equals(employee.Status, "Violation", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessSensorAnnouncementsAsync(List<SensorViolation> violations)
    {
        var settings = _configurationService.GetSensorAlertSettings();
        if (!settings.VoiceEnabled || !SensorVoiceEnabled)
        {
            return;
        }

        foreach (var violation in violations)
        {
            string currentSeverity = violation.Status.ToUpperInvariant();

            bool severityChanged = !string.Equals(
                violation.LastAnnouncedSeverity,
                currentSeverity,
                StringComparison.OrdinalIgnoreCase);

            bool repeatDue =
                violation.LastAnnouncedAt.HasValue &&
                (DateTime.Now - ToLocalTime(violation.LastAnnouncedAt.Value))
                    >= TimeSpan.FromMinutes(settings.RepeatAfterMinutes);

            bool neverAnnounced = !violation.LastAnnouncedAt.HasValue;

            if (!neverAnnounced && !severityChanged && !repeatDue)
            {
                continue;
            }

            var templates = await _alertMessageService.GetTemplatesAsync(
                AlertMessageService.CategorySensor,
                currentSeverity);

            string chamberName = $"Chamber {violation.ChamberId}";
            var voiceLines = new List<VoiceAnnouncementLine>();
            string englishCulture = settings.EnglishVoiceCulture;

            if (templates.TryGetValue(englishCulture, out string? englishTemplate) &&
                !string.IsNullOrWhiteSpace(englishTemplate))
            {
                voiceLines.Add(new VoiceAnnouncementLine(
                    MonitoringService.FormatSensorMessage(
                        englishTemplate,
                        violation.Parameter,
                        chamberName),
                    englishCulture,
                    playEmergencySound: true));
            }

            if (voiceLines.Count == 0)
            {
                continue;
            }

            Voice.AnnounceOnce(voiceLines);
            await _monitoringService.MarkSensorViolationAnnouncedAsync(
                violation.SensorViolationsId,
                currentSeverity);
        }
    }

    private static DateTime ToLocalTime(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };
}
