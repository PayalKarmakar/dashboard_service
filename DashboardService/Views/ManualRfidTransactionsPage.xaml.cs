using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class ManualRfidTransactionsPage : Page
{
    private readonly User _currentUser;
    private readonly RfidTransactionService _transactionService = new();

    public ManualRfidTransactionsPage(User currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        Loaded += ManualRfidTransactionsPage_Loaded;
        Unloaded += ManualRfidTransactionsPage_Unloaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        OpenEntryButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        ApplySciFiChrome(ThemeService.IsDarkMode);
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    private void ManualRfidTransactionsPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void ManualRfidTransactionsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySciFiChrome(ThemeService.IsDarkMode);
        SidebarMenuHelper.OpenSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            OpenGrid.ItemsSource = await _transactionService.GetOpenAsync();
            RecentGrid.ItemsSource = await _transactionService.GetRecentAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RFID Manual", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OpenEntry_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only admin can open RFID transactions.", "RFID Manual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new OpenManualRfidWindow
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only admin can close RFID transactions.", "RFID Manual", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not RfidTransactionRow row)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Close RFID for {row.EmployeeName} in {row.ChamberName}?",
            "Close RFID",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _transactionService.CloseManualAsync(row.TransactionId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RFID Manual", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void LiveCameraMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);

    private void ReportsToggle_Click(object sender, RoutedEventArgs e) =>
        SidebarMenuHelper.ToggleSubMenu(ReportsSubMenuPanel, ReportsArrowText);

    private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "Reports", _currentUser);

    private void CameraAccessReportMenu_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Go(NavigationService, "CameraAccessReport", _currentUser);

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

    private void ManualRfidMenu_Click(object sender, RoutedEventArgs e) { }
}
