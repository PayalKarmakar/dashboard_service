using System.Windows;
using System.Windows.Controls;
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
    private readonly MonitoringService _monitoringService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly CameraLiveStreamService _opencvStreamService = new();
    private readonly CameraPythonLiveService _pythonStreamService = new();
    private readonly DispatcherTimer _rfidTimer = new();
    private List<MasterCameraConfig> _cameras = [];
    private MasterCameraConfig? _selectedCamera;
    private long _selectedChamberId;

    public LiveCameraPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += LiveCameraPage_Loaded;
        Unloaded += LiveCameraPage_Unloaded;

        _opencvStreamService.FrameReady += StreamService_FrameReady;
        _pythonStreamService.FrameReady += StreamService_FrameReady;

        var settings = _configurationService.GetCameraLiveSettings();
        _rfidTimer.Interval = TimeSpan.FromSeconds(settings.RfidRefreshIntervalSeconds);
        _rfidTimer.Tick += RfidTimer_Tick;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void LiveCameraPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        _rfidTimer.Stop();
        _opencvStreamService.Stop();
        _ = _pythonStreamService.StopAsync();
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

    private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraComboBox.SelectedItem is CameraOption option)
        {
            _selectedCamera = option.Camera;
            _selectedChamberId = option.Camera.ChamberId;
            RfidChamberText.Text = $"Chamber: {option.Camera.ChamberName}";
            DetectionModeText.Text = option.Camera.PersonDetectionEnabled
                ? "Person detection: On (Python YOLOv8)"
                : "Person detection: Off (stream only)";
        }
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
        _rfidTimer.Start();
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
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        CameraComboBox.IsEnabled = true;
        StreamStatusText.Text = "Stream stopped";
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
        });
    }

    private async void RfidTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshRfidInsideAsync();
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
