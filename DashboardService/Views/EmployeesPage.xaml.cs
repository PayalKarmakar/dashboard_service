using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class EmployeesPage : Page
{
    private readonly User _currentUser;
    private readonly EmployeeService _employeeService = new();

    public EmployeesPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += EmployeesPage_Loaded;
        Unloaded += EmployeesPage_Unloaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        AddEmployeeButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void EmployeesPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void EmployeesPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        await LoadEmployeesAsync();
    }

    private async Task LoadEmployeesAsync()
    {
        try
        {
            EmployeesGrid.ItemsSource = await _employeeService.GetAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Employees", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only admin can add employees.", "Employees", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new AddEmployeeWindow(_currentUser.UserId)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadEmployeesAsync();
        }
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can change employee status.",
                "Employees",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not EmployeeRecord employee)
        {
            return;
        }

        bool nextActive = !employee.IsActive;
        string action = nextActive ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Do you want to {action} \"{employee.EmployeeName}\"?",
            "Employees",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _employeeService.SetActiveAsync(employee.EmployeeId, nextActive, _currentUser.UserId);
            await LoadEmployeesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Employees", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DashboardMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Dashboard", _currentUser);
    }

    private void ChambersMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Chambers", _currentUser);
    }

    private void ReadersMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Readers", _currentUser);
    }

    private void ReportsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool open = ReportsSubMenuPanel.Visibility != Visibility.Visible;
        ReportsSubMenuPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ReportsArrowText.Text = open ? "▲" : "▼";
    }

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Reports", _currentUser);
    }

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);
    }

    private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberCriticalReport", _currentUser);

    private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ProductionLossReport", _currentUser);
    private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorReadingsReport", _currentUser);

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);

    private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.OpenCodeInq();
        e.Handled = true;
    }
}
