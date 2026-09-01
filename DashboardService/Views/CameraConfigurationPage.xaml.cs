using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class CameraConfigurationPage : Page
{
    private readonly User _currentUser;
    private readonly CameraConfigurationService _cameraService = new();

    public CameraConfigurationPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += CameraConfigurationPage_Loaded;
        Unloaded += CameraConfigurationPage_Unloaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        AddCameraButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        SidebarMenuHelper.OpenSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);
        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void CameraConfigurationPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
    }

    private void ThemeService_ThemeChanged(bool isDark)
    {
        Dispatcher.Invoke(() => ApplySciFiChrome(isDark));
    }

    private void ApplySciFiChrome(bool isDark)
    {
        SidebarSciFiOverlay.Opacity = isDark ? 1 : 0;
    }

    private async void CameraConfigurationPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        await LoadCamerasAsync();
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            CamerasGrid.ItemsSource = await _cameraService.GetAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Camera Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddCamera_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdmin())
        {
            return;
        }

        var window = new AddCameraWindow(_currentUser.UserId)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadCamerasAsync();
        }
    }

    private async void EditCamera_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdmin())
        {
            return;
        }

        if (sender is not Button button || button.DataContext is not MasterCameraConfig camera)
        {
            return;
        }

        var window = new AddCameraWindow(_currentUser.UserId, camera)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadCamerasAsync();
        }
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdmin())
        {
            return;
        }

        if (sender is not Button button || button.DataContext is not MasterCameraConfig camera)
        {
            return;
        }

        bool nextActive = !camera.IsActive;
        string action = nextActive ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Do you want to {action} \"{camera.CameraName}\"?",
            "Camera Configuration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _cameraService.SetActiveAsync(camera.CameraId, nextActive, _currentUser.UserId);
            await LoadCamerasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Camera Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsAdmin()
    {
        if (string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        MessageBox.Show(
            "Only admin can change camera configuration.",
            "Camera Configuration",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void DashboardMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Dashboard", _currentUser);

    private void ChambersMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Chambers", _currentUser);

    private void EmployeesMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Employees", _currentUser);

    private void ReadersMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Readers", _currentUser);

    private void ReportsToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ReportsSubMenuPanel, ReportsArrowText);

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Reports", _currentUser);

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);

    private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberCriticalReport", _currentUser);

    private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ProductionLossReport", _currentUser);

    private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorReadingsReport", _currentUser);

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) { }

    private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.OpenCodeInq();
        e.Handled = true;
    }
}
