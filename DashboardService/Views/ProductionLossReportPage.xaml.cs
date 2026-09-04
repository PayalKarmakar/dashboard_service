using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using Microsoft.Win32;

namespace DashboardService.Views;

public partial class ProductionLossReportPage : Page
{
    private readonly User _currentUser;
    private readonly ReportService _reportService = new();
    private readonly ChamberService _chamberService = new();
    private List<ProductionLossReportRow> _rows = new();
    private bool _suppressFilterReload;

    public ProductionLossReportPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += ProductionLossReportPage_Loaded;
        Unloaded += ProductionLossReportPage_Unloaded;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void ProductionLossReportPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void ProductionLossReportPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);

        try
        {
            _suppressFilterReload = true;
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            ToDatePicker.SelectedDate = DateTime.Today;

            var chambers = await _chamberService.GetAllAsync();
            var items = new List<ChamberFilterItem>
            {
                new() { ChamberId = 0, Display = "All Chambers" }
            };

            items.AddRange(
                chambers
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.ChamberName)
                    .Select(c => new ChamberFilterItem
                    {
                        ChamberId = c.ChamberId,
                        Display = c.ToString()
                    }));

            ChamberComboBox.ItemsSource = items;
            ChamberComboBox.SelectedIndex = 0;
            _suppressFilterReload = false;

            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await LoadReportAsync();

    private async void Filter_Changed(object sender, EventArgs e)
    {
        if (_suppressFilterReload || !IsLoaded)
        {
            return;
        }

        await LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        try
        {
            DateTime from = FromDatePicker.SelectedDate ?? DateTime.Today;
            DateTime to = ToDatePicker.SelectedDate ?? DateTime.Today;

            if (to < from)
            {
                MessageBox.Show(
                    "To Date cannot be earlier than From Date.",
                    "Reports",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            long? chamberId = null;
            if (ChamberComboBox.SelectedItem is ChamberFilterItem selected && selected.ChamberId > 0)
            {
                chamberId = selected.ChamberId;
            }

            _rows = await _reportService.GetProductionLossReportAsync(from, to, chamberId);
            ReportGrid.ItemsSource = _rows;

            string totalDuration = ReportService.FormatTotalDuration(_rows);
            int ongoingCount = _rows.Count(r => r.IsOngoing);

            ResultCountText.Text =
                $"{_rows.Count} production loss episode(s) · Total lost time: {totalDuration}" +
                (ongoingCount > 0 ? $" · {ongoingCount} ongoing" : string.Empty);

            SummaryText.Text =
                "Production loss = chamber downtime while WARNING or CRITICAL sensor violations are active. Overlapping events are merged.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("No records to export.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"production_loss_report_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Chamber Code,Chamber,Loss Started,Loss Ended,Duration,Status");

            foreach (var row in _rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.ChamberCode),
                    Csv(row.ChamberName),
                    Csv(row.StartedDisplay),
                    Csv(row.EndedDisplay),
                    Csv(row.DurationDisplay),
                    Csv(row.StatusDisplay)));
            }

            sb.AppendLine();
            sb.AppendLine($"Total Lost Time,{ReportService.FormatTotalDuration(_rows)}");

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("Report exported successfully.", "Reports", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
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

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Reports", _currentUser);

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);

    private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberCriticalReport", _currentUser);

    private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e) { }

    private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorReadingsReport", _currentUser);

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);

    private void ManualRfidMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ManualRfidTransactions", _currentUser);

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);

    private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.OpenCodeInq();
        e.Handled = true;
    }

    private sealed class ChamberFilterItem
    {
        public long ChamberId { get; init; }

        public string Display { get; init; } = string.Empty;
    }
}
