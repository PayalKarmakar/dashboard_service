using System.Windows.Media;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace DashboardService.Views;

public partial class SensorConfigurationPage : Page
{
    private readonly User _currentUser;
    private readonly SensorConfigurationService _sensorConfigurationService = new();

    public ObservableCollection<MasterSensorConfig> Sensors { get; } = new();
    public ObservableCollection<SensorThresholdConfig> MasterThresholds { get; } = new();
    //public ObservableCollection<SensorViolationThresholdEdit> GasThresholds { get; } = new();
    //public ObservableCollection<SensorViolationThresholdEdit> OtherViolations { get; } = new();
    public SensorConfigurationPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;

        SensorsGrid.ItemsSource = Sensors;
        MasterThresholdsGrid.ItemsSource = MasterThresholds;
        //GasThresholdsGrid.ItemsSource = GasThresholds;
        //OtherViolationsGrid.ItemsSource = OtherViolations;

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

            MasterThresholds.Clear();
            foreach (var threshold in await _sensorConfigurationService.GetSensorThresholdsAsync())
            {
                MasterThresholds.Add(threshold);
            }

            //GasThresholds.Clear();
            //OtherViolations.Clear();

            //foreach (var row in await _sensorConfigurationService.GetActiveViolationThresholdsAsync())
            //{
            //    if (row.IsGasParameter)
            //    {
            //        GasThresholds.Add(row);
            //    }
            //    else
            //    {
            //        OtherViolations.Add(row);
            //    }
            //}
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
            MasterThresholdsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MasterThresholdsGrid.CommitEdit();
            //GasThresholdsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            //GasThresholdsGrid.CommitEdit();

            foreach (var sensor in Sensors)
            {
                await _sensorConfigurationService.UpdateSensorPortAsync(
                    sensor.SensorId,
                    sensor.Port);
            }

            foreach (var threshold in MasterThresholds)
            {
                await _sensorConfigurationService.UpdateSensorThresholdAsync(threshold);
            }

            //foreach (var threshold in GasThresholds)
            //{
            //    await _sensorConfigurationService.UpdateViolationThresholdAsync(
            //        threshold.SensorViolationsId,
            //        threshold.ThresholdValue);
            //}

            MessageBox.Show(
                "COM port, warning/critical limits, and active violation thresholds saved.",
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
    private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorReadingsReport", _currentUser);

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

    private void ViewThresholdRanges_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not SensorThresholdConfig threshold)
        {
            return;
        }

        // Build the ranges from the existing database threshold values
        BuildStatusRanges(threshold);

        // Find the row containing this button
        var row = FindVisualParent<DataGridRow>(button);

        if (row == null)
            return;

        row.DetailsVisibility =
            row.DetailsVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        button.Content =
            row.DetailsVisibility == Visibility.Visible
                ? "Hide ranges  ↑"
                : "View ranges  ›";
    }

    private static T? FindVisualParent<T>(DependencyObject child)
    where T : DependencyObject
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(child);

        while (parent != null)
        {
            if (parent is T result)
                return result;

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void BuildStatusRanges(SensorThresholdConfig threshold)
    {
        threshold.StatusRanges.Clear();

        string unit = threshold.Unit ?? string.Empty;

        decimal? warningLow = ParseThreshold(threshold.WarningLow);
        decimal? warningHigh = ParseThreshold(threshold.WarningHigh);
        decimal? criticalLow = ParseThreshold(threshold.CriticalLow);
        decimal? criticalHigh = ParseThreshold(threshold.CriticalHigh);

        // CRITICAL LOW
        if (criticalLow.HasValue)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "CRITICAL",
                Range = $"Below {criticalLow.Value:0.00} {unit}"
            });
        }

        // WARNING LOW
        if (warningLow.HasValue)
        {
            string range = criticalLow.HasValue
                ? $"{criticalLow.Value:0.00} – below {warningLow.Value:0.00} {unit}"
                : $"Below {warningLow.Value:0.00} {unit}";

            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "WARNING",
                Range = range
            });
        }

        // NORMAL
        if (warningLow.HasValue && warningHigh.HasValue)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "NORMAL",
                Range =
                    $"{warningLow.Value:0.00} – {warningHigh.Value:0.00} {unit}"
            });
        }
        else if (warningLow.HasValue)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "NORMAL",
                Range =
                    $"{warningLow.Value:0.00} {unit} and above"
            });
        }
        else if (warningHigh.HasValue)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "NORMAL",
                Range =
                    $"{warningHigh.Value:0.00} {unit} and below"
            });
        }

        // WARNING HIGH
        if (warningHigh.HasValue)
        {
            string range = criticalHigh.HasValue
                ? $"Above {warningHigh.Value:0.00} – {criticalHigh.Value:0.00} {unit}"
                : $"Above {warningHigh.Value:0.00} {unit}";

            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "WARNING",
                Range = range
            });
        }

        // CRITICAL HIGH
        if (criticalHigh.HasValue)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "CRITICAL",
                Range = $"Above {criticalHigh.Value:0.00} {unit}"
            });
        }

        if (threshold.StatusRanges.Count == 0)
        {
            threshold.StatusRanges.Add(new ThresholdStatusRange
            {
                Status = "NORMAL",
                Range = "No limits configured"
            });
        }
    }

    private static decimal? ParseThreshold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal result)
            ? result
            : null;
    }


}
