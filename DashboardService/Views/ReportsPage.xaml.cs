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

public partial class ReportsPage : Page
{
    private readonly User _currentUser;
    private readonly ReportService _reportService = new();
    private List<EntryExitReportRow> _rows = new();
    private bool _suppressFilterReload;

    public ReportsPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += ReportsPage_Loaded;
        Unloaded += ReportsPage_Unloaded;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void ReportsPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void ReportsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);

        _suppressFilterReload = true;
        FromDatePicker.SelectedDate = DateTime.Today;
        ToDatePicker.SelectedDate = DateTime.Today;
        _suppressFilterReload = false;

        await LoadReportAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await LoadReportAsync();
    }

    private async void Filter_Changed(object sender, EventArgs e)
    {
        if (_suppressFilterReload || !IsLoaded)
        {
            return;
        }

        await LoadReportAsync();
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadReportAsync();
        }
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

            string status = string.Empty;
            if (StatusComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                status = tag;
            }

            _rows = await _reportService.GetEntryExitReportAsync(
                from,
                to,
                SearchTextBox.Text,
                status);

            ReportGrid.ItemsSource = _rows;
            ResultCountText.Text = $"{_rows.Count} record(s)";
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
            MessageBox.Show(
                "No records to export.",
                "Reports",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"entry_exit_report_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Emp Code,Employee,Card UID,Chamber,Entry Time,Exit Time,Duration,Status");

            foreach (var row in _rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.EmployeeCode),
                    Csv(row.EmployeeName),
                    Csv(row.CardUid),
                    Csv(row.ChamberName),
                    Csv(row.EntryDisplay),
                    Csv(row.ExitDisplay),
                    Csv(row.DurationDisplay),
                    Csv(row.StatusDisplay)));
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

            MessageBox.Show(
                "Report exported successfully.",
                "Reports",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    private void DashboardMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Dashboard", _currentUser);
    }

    private void ChambersMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Chambers", _currentUser);
    }

    private void EmployeesMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "Employees", _currentUser);
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
        // Already on Entry / Exit report page.
    }

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);
    }

    private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberCriticalReport", _currentUser);

    private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ProductionLossReport", _currentUser);

    private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

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
