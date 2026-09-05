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

public partial class CameraAccessReportPage : Page
{
    private readonly User _currentUser;
    private readonly CameraAccessEventService _eventService = new();
    private readonly ListPager<CameraAccessEventRow> _eventsPager = new();
    private List<CameraAccessEventRow> _rows = [];
    private bool _suppressFilterReload;

    public CameraAccessReportPage(User currentUser)
    {
        InitializeComponent();
        EventsPagerBar.Bind(_eventsPager);
        EventsGrid.ItemsSource = _eventsPager.PageItems;
        _currentUser = currentUser;
        Loaded += CameraAccessReportPage_Loaded;
        Unloaded += CameraAccessReportPage_Unloaded;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void CameraAccessReportPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void CameraAccessReportPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        SidebarMenuHelper.OpenSubMenu(ReportsSubMenuPanel, ReportsArrowText);

        try
        {
            _suppressFilterReload = true;
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            ToDatePicker.SelectedDate = DateTime.Today;
            _suppressFilterReload = false;
            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Camera Access Report", MessageBoxButton.OK, MessageBoxImage.Error);
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
            DateTime from = FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-7);
            DateTime to = ToDatePicker.SelectedDate ?? DateTime.Today;

            // Camera Access Report shows only No-RFID violations.
            _rows = await _eventService.GetReportAsync(from, to, "NO_RFID");
            _eventsPager.SetItems(_rows);

            SummaryEntryText.Text = _rows.Where(r => r.EventType == "NO_RFID").Sum(r => r.PersonCount).ToString();
            SummaryExitText.Text = _rows.Where(r => r.EventType == "NO_RFID_EXIT").Sum(r => r.PersonCount).ToString();
            SummaryUnauthorizedText.Text = _rows.Sum(r => r.PersonCount).ToString();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Camera Access Report", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("No rows to export.", "Camera Access Report", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"camera-access-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("DateTime,Chamber,Camera,Event,Persons,RFID,Message");
        foreach (var row in _rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.OccurredDisplay),
                Csv(row.ChamberName),
                Csv(row.CameraName),
                Csv(row.EventDisplay),
                row.PersonCount,
                Csv(row.RfidDisplay),
                Csv(row.Message)));
        }

        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        MessageBox.Show("Export completed.", "Camera Access Report", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string Csv(string value)
    {
        string v = value?.Replace("\"", "\"\"") ?? string.Empty;
        return $"\"{v}\"";
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

    private void ReportsToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ReportsSubMenuPanel, ReportsArrowText);

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Reports", _currentUser);

    private void CameraAccessReportMenu_Click(object sender, RoutedEventArgs e) { }

    private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);

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

    private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);

    private void ManualRfidMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "ManualRfidTransactions", _currentUser);
}
