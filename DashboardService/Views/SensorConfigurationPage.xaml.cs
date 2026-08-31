using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class SensorConfigurationPage : Page
{
    private readonly User _currentUser;
    private readonly SensorConfigurationService _sensorConfigurationService = new();

    public ObservableCollection<MasterSensorConfig> Sensors { get; } = new();
    public ObservableCollection<SensorViolationThresholdEdit> GasThresholds { get; } = new();
    public ObservableCollection<SensorViolationThresholdEdit> OtherViolations { get; } = new();

    public SensorConfigurationPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;

        SensorsGrid.ItemsSource = Sensors;
        GasThresholdsGrid.ItemsSource = GasThresholds;
        OtherViolationsGrid.ItemsSource = OtherViolations;

        Loaded += SensorConfigurationPage_Loaded;
        Unloaded += SensorConfigurationPage_Unloaded;

        SidebarMenuHelper.OpenSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);
        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void SensorConfigurationPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void SensorConfigurationPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        await LoadConfigurationAsync();
    }

    private async Task LoadConfigurationAsync()
    {
        try
        {
            Sensors.Clear();
            foreach (var sensor in await _sensorConfigurationService.GetMasterSensorsAsync())
            {
                Sensors.Add(sensor);
            }

            GasThresholds.Clear();
            OtherViolations.Clear();

            foreach (var row in await _sensorConfigurationService.GetActiveViolationThresholdsAsync())
            {
                if (row.IsGasParameter)
                {
                    GasThresholds.Add(row);
                }
                else
                {
                    OtherViolations.Add(row);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sensor Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SensorsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            SensorsGrid.CommitEdit();
            GasThresholdsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            GasThresholdsGrid.CommitEdit();

            foreach (var sensor in Sensors)
            {
                await _sensorConfigurationService.UpdateSensorPortAsync(
                    sensor.SensorId,
                    sensor.Port);
            }

            foreach (var threshold in GasThresholds)
            {
                await _sensorConfigurationService.UpdateViolationThresholdAsync(
                    threshold.SensorViolationsId,
                    threshold.ThresholdValue);
            }

            MessageBox.Show(
                "COM port and sensor_violations threshold values saved.",
                "Sensor Configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadConfigurationAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sensor Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);

    private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.OpenCodeInq();
        e.Handled = true;
    }
}
