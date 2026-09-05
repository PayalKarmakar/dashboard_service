using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using Microsoft.Win32;

namespace DashboardService.Views;

public partial class SystemLogsReportPage : Page
{
    private readonly User _currentUser;
    private readonly ReportService _reportService = new();
    private readonly ListPager<SystemLogReportRow> _reportPager = new();
    private List<SystemLogReportRow> _rows = [];
    private bool _suppressFilterReload;

    public SystemLogsReportPage(User currentUser)
    {
        InitializeComponent();
        ReportPagerBar.Bind(_reportPager);
        ReportGrid.ItemsSource = _reportPager.PageItems;
        _currentUser = currentUser;
        Loaded += SystemLogsReportPage_Loaded;
        Unloaded += SystemLogsReportPage_Unloaded;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void SystemLogsReportPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void SystemLogsReportPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);

        try
        {
            _suppressFilterReload = true;
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            ToDatePicker.SelectedDate = DateTime.Today;
            StatusComboBox.SelectedIndex = 0;
            _suppressFilterReload = false;
            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "System Logs", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "System Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string filter = string.Empty;
            if (StatusComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                filter = tag;
            }

            _rows = await _reportService.GetSystemLogsAsync(from, to, filter);
            _reportPager.SetItems(_rows);
            ResultCountText.Text = $"{_rows.Count} log(s)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "System Logs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("No records to export.", "System Logs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"system_logs_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Time,Status,Event,Service,Level,Port,Message");

            foreach (var row in _rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.CreatedDisplay),
                    Csv(row.StatusDisplay),
                    Csv(row.EventType),
                    Csv(row.ServiceName),
                    Csv(row.LogLevel),
                    Csv(row.SourcePort),
                    Csv(row.Message)));
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("Report exported successfully.", "System Logs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "System Logs", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);

    private void ManualRfidMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ManualRfidTransactions", _currentUser);

    private void ReportsToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ReportsSubMenuPanel, ReportsArrowText);

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

    private void SystemLogsReportMenu_Click(object sender, RoutedEventArgs e) { }

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

    private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);
}
