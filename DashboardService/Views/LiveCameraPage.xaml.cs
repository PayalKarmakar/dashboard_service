using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class LiveCameraPage : Page
{
    private readonly User _currentUser;
    private readonly CameraConfigurationService _cameraService = new();
    private readonly MonitoringService _monitoringService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly CameraLiveStreamService _opencvStreamService = new();
    private readonly CameraPythonLiveService _pythonStreamService = new();
    private readonly CameraDoorVerificationService _doorVerificationService = new();
    private readonly CameraOccupancyVerificationService _occupancyVerificationService = new();
    private readonly CameraAccessEventService _cameraAccessEventService = new();
    private readonly VoiceAnnouncementService _voiceAnnouncementService = new();
    private readonly DispatcherTimer _rfidTimer = new();
    private readonly DispatcherTimer _verifyTimer = new();
    private readonly bool _backgroundMode;

    private List<MasterCameraConfig> _cameras = [];
    private MasterCameraConfig? _selectedCamera;
    private long _selectedChamberId;
    private CameraMonitorSession? _attachedSession;
    private bool _verifyBusy;
    private int _latestDetectedCount;
    private int _lastLoggedEntryCount = -1;
    private int _lastLoggedExitCount = -1;
    private int _unauthorizedSessionCount;

    public LiveCameraPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        _backgroundMode = _configurationService.GetCameraLiveSettings().BackgroundMonitoringEnabled;

        Loaded += LiveCameraPage_Loaded;
        Unloaded += LiveCameraPage_Unloaded;

        _opencvStreamService.FrameReady += LocalStreamService_FrameReady;
        _pythonStreamService.FrameReady += LocalStreamService_FrameReady;
        _doorVerificationService.AlertRaised += LocalDoorVerificationService_AlertRaised;
        _occupancyVerificationService.AlertRaised += LocalDoorVerificationService_AlertRaised;

        var settings = _configurationService.GetCameraLiveSettings();
        _rfidTimer.Interval = TimeSpan.FromSeconds(settings.RfidRefreshIntervalSeconds);
        _rfidTimer.Tick += RfidTimer_Tick;

        _verifyTimer.Interval = TimeSpan.FromSeconds(1);
        _verifyTimer.Tick += VerifyTimer_Tick;

        ApplyBackgroundModeUi();
    }

    private void ApplyBackgroundModeUi()
    {
        if (!_backgroundMode)
        {
            return;
        }

        StartButton.Content = "Refresh View";
        StopButton.Content = "Stop Preview";
        LiveCameraSubtitleText.Text =
            "Background monitoring is active — select a camera for live preview";
    }

    private async void LiveCameraPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_backgroundMode)
        {
            try
            {
                await _cameraAccessEventService.EnsureSchemaAsync();
            }
            catch
            {
                // Persistence will retry on first save.
            }
        }

        await LoadCamerasAsync();
    }

    private void LiveCameraPage_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachFromBackgroundSession();
        if (!_backgroundMode)
        {
            StopLocalStream();
        }
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            _cameras = await _cameraService.GetAllAsync();
            var items = _cameras
                .Where(c => c.IsActive)
                .Select(c => new CameraOption
                {
                    CameraId = c.CameraId,
                    DisplayLabel = $"{c.CameraName} · {c.ChamberName}",
                    Camera = c
                })
                .ToList();

            CameraComboBox.ItemsSource = items;

            if (items.Count > 0)
            {
                CameraComboBox.SelectedIndex = 0;
            }
            else
            {
                StreamStatusText.Text = "No active camera configured. Add one in Configuration → Camera.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Live Camera", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraComboBox.SelectedItem is not CameraOption option)
        {
            return;
        }

        if (_backgroundMode)
        {
            DetachFromBackgroundSession();
        }
        else
        {
            StopLocalStream();
        }

        _selectedCamera = option.Camera;
        _selectedChamberId = option.Camera.ChamberId;
        RfidChamberText.Text = $"Chamber: {option.Camera.ChamberName}";

        ApplyCameraModeUi(option.Camera);

        if (_backgroundMode)
        {
            AttachToBackgroundSession(option.Camera.CameraId);
            await RefreshRfidInsideAsync();
            return;
        }

        await ConfigureLocalDoorVerificationAsync(option.Camera);
    }

    private void AttachToBackgroundSession(long cameraId)
    {
        CameraMonitorSession? session =
            CameraBackgroundMonitoringService.Instance.GetSession(cameraId);

        if (session == null)
        {
            StreamStatusText.Text =
                "Background monitoring not running for this camera. Check camera_service and RTSP URL.";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            return;
        }

        _attachedSession = session;
        _attachedSession.FrameReady += BackgroundSession_FrameReady;
        _attachedSession.AlertRaised += BackgroundSession_AlertRaised;
        _attachedSession.AddFrameSubscriber();

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        CameraComboBox.IsEnabled = true;
        StreamStatusText.Text = session.StatusMessage;
        UpdateStatsFromBackground(session.LastStats);

        if (_selectedCamera != null)
        {
            var status = string.Equals(
                _selectedCamera.CameraPurpose,
                "MONITORING",
                StringComparison.OrdinalIgnoreCase)
                ? session.LastStats.StatusMessage
                : session.StatusMessage;
            DoorVerifyStatusText.Text = status;
        }
    }

    private void DetachFromBackgroundSession()
    {
        if (_attachedSession == null)
        {
            return;
        }

        _attachedSession.FrameReady -= BackgroundSession_FrameReady;
        _attachedSession.AlertRaised -= BackgroundSession_AlertRaised;
        _attachedSession.RemoveFrameSubscriber();
        _attachedSession = null;

        _rfidTimer.Stop();
        StreamImage.Source = null;
        ResetStats();
    }

    private void BackgroundSession_FrameReady(BitmapSource frame, CameraDetectionStats stats)
    {
        Dispatcher.Invoke(() =>
        {
            StreamImage.Source = frame;
            StreamStatusText.Text = stats.StatusMessage;
            UpdateStatsFromBackground(stats);
        });
    }

    private void BackgroundSession_AlertRaised(CameraDoorAlert alert)
    {
        Dispatcher.Invoke(() => HandleAlertUi(alert));
    }

    private void UpdateStatsFromBackground(CameraDetectionStats stats)
    {
        DetectedCountText.Text = stats.TotalDetected.ToString();
        InsideCountText.Text = stats.InsideCount.ToString();
        OutsideCountText.Text = stats.OutsideCount.ToString();
        AccuracyText.Text = stats.AccuracyDisplay;
        FpsText.Text = stats.FpsDisplay;
    }

    private void ApplyCameraModeUi(MasterCameraConfig camera)
    {
        bool monitoring = string.Equals(
            camera.CameraPurpose,
            "MONITORING",
            StringComparison.OrdinalIgnoreCase);

        bool showEntryExitStats = !monitoring
            && _configurationService.GetCameraLiveSettings().ShowEntryExitStats;

        DetectedStatCard.Visibility = monitoring ? Visibility.Visible : Visibility.Collapsed;
        EntryStatCard.Visibility = showEntryExitStats ? Visibility.Visible : Visibility.Collapsed;
        ExitStatCard.Visibility = showEntryExitStats ? Visibility.Visible : Visibility.Collapsed;
        UnauthorizedStatCard.Visibility = showEntryExitStats ? Visibility.Visible : Visibility.Collapsed;
        VerifyStatCard.Visibility = monitoring ? Visibility.Collapsed : Visibility.Visible;
        RfidStatCard.Visibility = Visibility.Collapsed;

        DetectedTitleText.Text = "PERSONS INSIDE";
        DetectedHintText.Text = "People currently detected in the chamber";

        if (_backgroundMode)
        {
            LiveCameraSubtitleText.Text = monitoring
                ? "Background monitoring — occupancy preview"
                : "Background monitoring — entry/exit preview";
        }
        else
        {
            LiveCameraSubtitleText.Text = monitoring
                ? "Monitoring stream — accuracy, FPS, and persons inside"
                : showEntryExitStats
                    ? "Entry/Exit stream — persons, unauthorized, accuracy, FPS"
                    : "Entry/Exit stream — accuracy and FPS";
        }

        VerifyStatusTitleText.Text = monitoring ? "OCCUPANCY MATCH" : "VERIFY STATUS";
        DoorVerifyStatusText.Text = monitoring
            ? $"Compare camera count vs RFID inside · stable {camera.MatchWindowSeconds}s"
            : camera.AlertOnNoRfid || camera.AlertOnTailgate
                ? $"Watching IN/OUT events · match {camera.MatchWindowSeconds}s"
                : "Alerts disabled for this camera";
    }

    private async Task ConfigureLocalDoorVerificationAsync(MasterCameraConfig camera)
    {
        RfidReader? linkedReader = null;
        if (camera.RfidReaderId is > 0)
        {
            var readers = await new RfidReaderService().GetAllAsync();
            linkedReader = readers.FirstOrDefault(r => r.ReaderId == camera.RfidReaderId.Value);
        }

        _doorVerificationService.Configure(camera, linkedReader);
        _occupancyVerificationService.Configure(camera);

        bool monitoring = string.Equals(
            camera.CameraPurpose,
            "MONITORING",
            StringComparison.OrdinalIgnoreCase);

        if (monitoring)
        {
            DoorVerifyStatusText.Text =
                camera.AlertOnNoRfid || camera.AlertOnTailgate
                    ? $"Monitoring occupancy · match after {camera.MatchWindowSeconds}s stable"
                    : "Occupancy alerts disabled for this camera";
            return;
        }

        string readerLabel = linkedReader == null
            ? "chamber RFID (any reader)"
            : linkedReader.ReaderName;

        DoorVerifyStatusText.Text =
            camera.AlertOnNoRfid || camera.AlertOnTailgate
                ? $"Watching IN/OUT events · match {camera.MatchWindowSeconds}s · {readerLabel}"
                : "Alerts disabled for this camera";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundMode)
        {
            if (_selectedCamera != null)
            {
                AttachToBackgroundSession(_selectedCamera.CameraId);
            }

            return;
        }

        await StartLocalStreamAsync();
    }

    private async Task StartLocalStreamAsync()
    {
        if (_selectedCamera == null)
        {
            MessageBox.Show(
                "Please select an active camera first.",
                "Live Camera",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var settings = _configurationService.GetCameraLiveSettings();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        CameraComboBox.IsEnabled = false;
        StreamStatusText.Text = "Connecting...";
        DoorAlertBanner.Visibility = Visibility.Collapsed;
        _doorVerificationService.Reset();
        _occupancyVerificationService.Reset();
        _lastLoggedEntryCount = -1;
        _lastLoggedExitCount = -1;
        _unauthorizedSessionCount = 0;
        UnauthorizedCountText.Text = "0";
        await ConfigureLocalDoorVerificationAsync(_selectedCamera);
        _rfidTimer.Start();
        _verifyTimer.Start();
        _ = RefreshRfidInsideAsync();

        try
        {
            bool preferPython = settings.UsePythonService;
            bool pythonUp = preferPython && await _pythonStreamService.IsAvailableAsync();

            if (preferPython && !pythonUp)
            {
                MessageBox.Show(
                    "Python camera service is not running.\n\n" +
                    "Start: camera_service\\run-camera-service.bat\n" +
                    "Then click Start Stream again.\n\n" +
                    "Falling back to OpenCV stream (detection may be weak).",
                    "Live Camera",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (pythonUp)
            {
                _pythonStreamService.CameraId = "live-preview";
                await _pythonStreamService.StartAsync(
                    _selectedCamera.RtspUrl,
                    _selectedCamera.PersonDetectionEnabled,
                    settings.MinConfidence,
                    settings.ZoneDividerPercent,
                    _selectedCamera.CameraPurpose);
            }
            else
            {
                _opencvStreamService.Start(
                    _selectedCamera.RtspUrl,
                    _selectedCamera.PersonDetectionEnabled,
                    settings.MinConfidence,
                    settings.ZoneDividerPercent,
                    settings.DetectEveryNFrames,
                    settings.InputSize,
                    settings.ModelPath,
                    _selectedCamera.CameraPurpose);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Live Camera", MessageBoxButton.OK, MessageBoxImage.Error);
            StopLocalStream();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundMode)
        {
            DetachFromBackgroundSession();
            StopButton.IsEnabled = false;
            StreamStatusText.Text = "Preview stopped (background monitoring continues)";
            return;
        }

        StopLocalStream();
    }

    private void StopLocalStream()
    {
        _opencvStreamService.Stop();
        _ = _pythonStreamService.StopAsync();
        _rfidTimer.Stop();
        _verifyTimer.Stop();
        _doorVerificationService.Reset();
        _occupancyVerificationService.Reset();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        CameraComboBox.IsEnabled = true;
        StreamStatusText.Text = "Stream stopped";
        DoorVerifyStatusText.Text = "Idle";
        ResetStats();
    }

    private void LocalStreamService_FrameReady(BitmapSource frame, CameraDetectionStats stats)
    {
        Dispatcher.Invoke(() =>
        {
            StreamImage.Source = frame;
            StreamStatusText.Text = stats.StatusMessage;
            DetectedCountText.Text = stats.TotalDetected.ToString();
            InsideCountText.Text = stats.InsideCount.ToString();
            OutsideCountText.Text = stats.OutsideCount.ToString();
            AccuracyText.Text = stats.AccuracyDisplay;
            FpsText.Text = stats.FpsDisplay;
            _latestDetectedCount = stats.TotalDetected;

            _doorVerificationService.ObserveCameraEntryCount(stats.InsideCount);
            _doorVerificationService.ObserveCameraExitCount(stats.OutsideCount);
            PersistCrossingDeltas(stats.InsideCount, stats.OutsideCount);

            if (_selectedCamera != null
                && string.Equals(
                    _selectedCamera.CameraPurpose,
                    "MONITORING",
                    StringComparison.OrdinalIgnoreCase))
            {
                var status = _occupancyVerificationService.GetLastStatus();
                if (!string.IsNullOrWhiteSpace(status.StatusText)
                    && status.StatusText != "Idle"
                    && status.StatusText != "Waiting for detections...")
                {
                    DoorVerifyStatusText.Text = status.StatusText;
                }
            }
        });
    }

    private void PersistCrossingDeltas(int entryCount, int exitCount)
    {
        if (_selectedCamera == null
            || string.Equals(_selectedCamera.CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase))
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

        MasterCameraConfig camera = _selectedCamera;
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
            // Keep live stream running if DB write fails.
        }
    }

    private async void RfidTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshRfidInsideAsync();
    }

    private async void VerifyTimer_Tick(object? sender, EventArgs e)
    {
        if (_verifyBusy)
        {
            return;
        }

        _verifyBusy = true;
        try
        {
            await _doorVerificationService.ProcessDueAsync();

            if (_selectedCamera != null
                && string.Equals(
                    _selectedCamera.CameraPurpose,
                    "MONITORING",
                    StringComparison.OrdinalIgnoreCase))
            {
                await _occupancyVerificationService.EvaluateAsync(_latestDetectedCount);
                var status = _occupancyVerificationService.GetLastStatus();
                DoorVerifyStatusText.Text = status.StatusText;
                if (status.RfidCount >= 0 && status.CameraCount >= 0)
                {
                    RfidInsideText.Text = status.RfidCount.ToString();
                }
            }
        }
        catch
        {
            // Keep stream alive even if verification query fails.
        }
        finally
        {
            _verifyBusy = false;
        }
    }

    private void LocalDoorVerificationService_AlertRaised(CameraDoorAlert alert)
    {
        Dispatcher.Invoke(() =>
        {
            DoorVerifyStatusText.Text =
                $"{alert.TitleDisplay}: camera {alert.CameraPersonCount} / RFID {alert.RfidScanCount}";

            if (alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE")
            {
                _unauthorizedSessionCount += Math.Max(1, alert.CameraPersonCount);
                UnauthorizedCountText.Text = _unauthorizedSessionCount.ToString();
            }

            if (_selectedCamera != null
                && alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE"
                    or "MATCHED" or "EXIT_MATCHED")
            {
                _ = PersistAlertSafeAsync(_selectedCamera, alert);
            }

            HandleAlertUi(alert);
        });
    }

    private void HandleAlertUi(CameraDoorAlert alert)
    {
        DoorVerifyStatusText.Text =
            $"{alert.TitleDisplay}: camera {alert.CameraPersonCount} / RFID {alert.RfidScanCount}";

        if (alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE")
        {
            _unauthorizedSessionCount += Math.Max(1, alert.CameraPersonCount);
            UnauthorizedCountText.Text = _unauthorizedSessionCount.ToString();
        }

        bool isVoiceAlert =
            alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE"
                or "OCCUPANCY_NO_RFID" or "OCCUPANCY_MISMATCH";

        if (!_backgroundMode
            && isVoiceAlert
            && _configurationService.GetCameraLiveSettings().VoiceEnabled)
        {
            _voiceAnnouncementService.AnnounceOnce(alert.Message, "en-IN");
        }

        if (ToastNotificationService.IsEntryExitViolation(alert))
        {
            if (!_backgroundMode)
            {
                ToastNotificationService.ShowCameraViolation(alert);
            }
        }

        if (alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE"
            or "OCCUPANCY_NO_RFID" or "OCCUPANCY_MISMATCH")
        {
            ShowDoorAlertBanner(alert);
        }
        else
        {
            DoorAlertBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task PersistAlertSafeAsync(MasterCameraConfig camera, CameraDoorAlert alert)
    {
        try
        {
            await _cameraAccessEventService.LogAlertAsync(camera, alert);
        }
        catch
        {
            // Keep live stream running if DB write fails.
        }
    }

    private void ShowDoorAlertBanner(CameraDoorAlert alert)
    {
        bool critical = alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "OCCUPANCY_NO_RFID";
        DoorAlertBanner.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#FEF2F2" : "#FFFBEB"));
        DoorAlertBanner.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#FECACA" : "#FDE68A"));
        DoorAlertTitleText.Text = alert.AlertType switch
        {
            "NO_RFID" => "CRITICAL · Camera entry without RFID",
            "NO_RFID_EXIT" => "CRITICAL · Camera exit without RFID",
            "OCCUPANCY_NO_RFID" => "CRITICAL · People in chamber without RFID",
            "OCCUPANCY_MISMATCH" => "WARNING · Camera count > RFID inside",
            "EXIT_TAILGATE" => "WARNING · Possible exit tailgating",
            _ => "WARNING · Possible tailgating"
        };
        DoorAlertTitleText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#7F1D1D" : "#92400E"));
        DoorAlertMessageText.Text = alert.Message;
        DoorAlertMessageText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#991B1B" : "#A16207"));
        DoorAlertBanner.Visibility = Visibility.Visible;
    }

    private void DismissDoorAlert_Click(object sender, RoutedEventArgs e)
    {
        DoorAlertBanner.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshRfidInsideAsync()
    {
        if (_selectedChamberId <= 0)
        {
            return;
        }

        try
        {
            var members = await _monitoringService.GetMembersInsideAsync();
            int count = members.Count(m =>
                string.Equals(m.ChamberName, _selectedCamera?.ChamberName, StringComparison.OrdinalIgnoreCase));

            RfidInsideText.Text = count.ToString();
        }
        catch
        {
            RfidInsideText.Text = "—";
        }
    }

    private void ResetStats()
    {
        DetectedCountText.Text = "0";
        InsideCountText.Text = "0";
        OutsideCountText.Text = "0";
        UnauthorizedCountText.Text = "0";
        _unauthorizedSessionCount = 0;
        _lastLoggedEntryCount = -1;
        _lastLoggedExitCount = -1;
        AccuracyText.Text = "0%";
        FpsText.Text = "0.0 fps";
        RfidInsideText.Text = "0";
        StreamImage.Source = null;
    }

    private sealed class CameraOption
    {
        public long CameraId { get; init; }

        public string DisplayLabel { get; init; } = string.Empty;

        public MasterCameraConfig Camera { get; init; } = new();
    }
}
