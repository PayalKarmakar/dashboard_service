using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class LiveCameraPage : Page
{
    private readonly User _currentUser;
    private readonly CameraConfigurationService _cameraService = new();
    private readonly RfidReaderService _readerService = new();
    private readonly MonitoringService _monitoringService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly CameraLiveStreamService _opencvStreamService = new();
    private readonly CameraPythonLiveService _pythonStreamService = new();
    private readonly CameraDoorVerificationService _doorVerificationService = new();
    private readonly CameraOccupancyVerificationService _occupancyVerificationService = new();
    private readonly VoiceAnnouncementService _voiceAnnouncementService = new();
    private readonly DispatcherTimer _rfidTimer = new();
    private readonly DispatcherTimer _verifyTimer = new();
    private List<MasterCameraConfig> _cameras = [];
    private MasterCameraConfig? _selectedCamera;
    private long _selectedChamberId;
    private bool _verifyBusy;
    private int _latestDetectedCount;

    public LiveCameraPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += LiveCameraPage_Loaded;
        Unloaded += LiveCameraPage_Unloaded;

        _opencvStreamService.FrameReady += StreamService_FrameReady;
        _pythonStreamService.FrameReady += StreamService_FrameReady;
        _doorVerificationService.AlertRaised += DoorVerificationService_AlertRaised;
        _occupancyVerificationService.AlertRaised += DoorVerificationService_AlertRaised;

        var settings = _configurationService.GetCameraLiveSettings();
        _rfidTimer.Interval = TimeSpan.FromSeconds(settings.RfidRefreshIntervalSeconds);
        _rfidTimer.Tick += RfidTimer_Tick;

        _verifyTimer.Interval = TimeSpan.FromSeconds(1);
        _verifyTimer.Tick += VerifyTimer_Tick;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void LiveCameraPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        _doorVerificationService.AlertRaised -= DoorVerificationService_AlertRaised;
        _occupancyVerificationService.AlertRaised -= DoorVerificationService_AlertRaised;
        _rfidTimer.Stop();
        _verifyTimer.Stop();
        _opencvStreamService.Stop();
        _ = _pythonStreamService.StopAsync();
        _voiceAnnouncementService.Dispose();
    }

    private void ThemeService_ThemeChanged(bool isDark)
    {
        Dispatcher.Invoke(() => ApplySciFiChrome(isDark));
    }

    private void ApplySciFiChrome(bool isDark)
    {
        SidebarSciFiOverlay.Opacity = isDark ? 1 : 0;
    }

    private async void LiveCameraPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        await LoadCamerasAsync();
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
        if (CameraComboBox.SelectedItem is CameraOption option)
        {
            _selectedCamera = option.Camera;
            _selectedChamberId = option.Camera.ChamberId;
            RfidChamberText.Text = $"Chamber: {option.Camera.ChamberName}";
            DetectionModeText.Text = option.Camera.PersonDetectionEnabled
                ? "Person detection: On (Python YOLOv8)"
                : "Person detection: Off (stream only)";

            ApplyCameraModeUi(option.Camera);
            await ConfigureDoorVerificationAsync(option.Camera);
        }
    }

    private void ApplyCameraModeUi(MasterCameraConfig camera)
    {
        bool monitoring = string.Equals(
            camera.CameraPurpose,
            "MONITORING",
            StringComparison.OrdinalIgnoreCase);

        ExitStatCard.Visibility = monitoring ? Visibility.Collapsed : Visibility.Visible;
        EntryStatCard.Visibility = monitoring ? Visibility.Collapsed : Visibility.Visible;

        DetectedTitleText.Text = monitoring ? "CAMERA IN CHAMBER" : "PERSONS NOW";
        DetectedHintText.Text = monitoring
            ? "People currently detected by monitoring camera"
            : "Currently visible in frame";

        VerifyStatusTitleText.Text = monitoring ? "OCCUPANCY MATCH" : "DOOR VERIFY STATUS";
        DoorVerifyStatusText.Text = monitoring
            ? $"Compare camera count vs RFID inside · stable {camera.MatchWindowSeconds}s"
            : camera.AlertOnNoRfid || camera.AlertOnTailgate
                ? $"Watching IN events · match {camera.MatchWindowSeconds}s"
                : "Alerts disabled for this camera";
    }

    private async Task ConfigureDoorVerificationAsync(MasterCameraConfig camera)
    {
        RfidReader? linkedReader = null;
        if (camera.RfidReaderId is > 0)
        {
            var readers = await _readerService.GetAllAsync();
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
                ? $"Watching IN events · match {camera.MatchWindowSeconds}s · {readerLabel}"
                : "Alerts disabled for this camera";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
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
        await ConfigureDoorVerificationAsync(_selectedCamera);
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
                DetectionModeText.Text = "Person detection: On (Python YOLOv8)";
                await _pythonStreamService.StartAsync(
                    _selectedCamera.RtspUrl,
                    _selectedCamera.PersonDetectionEnabled,
                    settings.MinConfidence,
                    settings.ZoneDividerPercent);
            }
            else
            {
                DetectionModeText.Text = _selectedCamera.PersonDetectionEnabled
                    ? "Person detection: On (OpenCV fallback)"
                    : "Person detection: Off (stream only)";
                _opencvStreamService.Start(
                    _selectedCamera.RtspUrl,
                    _selectedCamera.PersonDetectionEnabled,
                    settings.MinConfidence,
                    settings.ZoneDividerPercent,
                    settings.DetectEveryNFrames,
                    settings.InputSize,
                    settings.ModelPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Live Camera", MessageBoxButton.OK, MessageBoxImage.Error);
            StopStream();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopStream();
    }

    private void StopStream()
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

    private void StreamService_FrameReady(BitmapSource frame, CameraDetectionStats stats)
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

            // InsideCount is mapped from camera service ENTRY (IN) cumulative count.
            _doorVerificationService.ObserveCameraEntryCount(stats.InsideCount);

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

    private void DoorVerificationService_AlertRaised(CameraDoorAlert alert)
    {
        Dispatcher.Invoke(() =>
        {
            DoorVerifyStatusText.Text = $"{alert.TitleDisplay}: camera {alert.CameraPersonCount} / RFID {alert.RfidScanCount}";

            if (alert.AlertType is "NO_RFID" or "TAILGATE" or "OCCUPANCY_NO_RFID" or "OCCUPANCY_MISMATCH")
            {
                ShowDoorAlertBanner(alert);
                _voiceAnnouncementService.AnnounceOnce(alert.Message, "en-IN");
            }
            else
            {
                DoorAlertBanner.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void ShowDoorAlertBanner(CameraDoorAlert alert)
    {
        bool critical = alert.AlertType is "NO_RFID" or "OCCUPANCY_NO_RFID";
        DoorAlertBanner.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#FEF2F2" : "#FFFBEB"));
        DoorAlertBanner.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(critical ? "#FECACA" : "#FDE68A"));
        DoorAlertTitleText.Text = alert.AlertType switch
        {
            "NO_RFID" => "CRITICAL · Camera entry without RFID",
            "OCCUPANCY_NO_RFID" => "CRITICAL · People in chamber without RFID",
            "OCCUPANCY_MISMATCH" => "WARNING · Camera count > RFID inside",
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
        AccuracyText.Text = "0%";
        FpsText.Text = "0.0 fps";
        RfidInsideText.Text = "0";
        StreamImage.Source = null;
    }

    private void DashboardMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("Dashboard");

    private void ChambersMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("Chambers");

    private void EmployeesMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("Employees");

    private void ReadersMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("Readers");

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) { }

    private void ReportsToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ReportsSubMenuPanel, ReportsArrowText);

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("Reports");

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("ChamberEmployeesReport");

    private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("ChamberCriticalReport");

    private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("ProductionLossReport");

    private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("SensorReadingsReport");

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("SensorConfiguration");

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        NavigateAway("CameraConfiguration");

    private void NavigateAway(string menu)
    {
        StopStream();
        AppNavigation.Go(NavigationService, menu, _currentUser);
    }

    private sealed class CameraOption
    {
        public long CameraId { get; init; }

        public string DisplayLabel { get; init; } = string.Empty;

        public MasterCameraConfig Camera { get; init; } = new();
    }
}
