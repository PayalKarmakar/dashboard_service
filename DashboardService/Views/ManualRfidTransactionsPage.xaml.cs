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
    private readonly ListPager<RfidTransactionRow> _openPager = new();
    private readonly ListPager<RfidTransactionRow> _recentPager = new();

    public ManualRfidTransactionsPage(User currentUser)
    {
        InitializeComponent();
        OpenPagerBar.Bind(_openPager);
        OpenGrid.ItemsSource = _openPager.PageItems;
        RecentPagerBar.Bind(_recentPager);
        RecentGrid.ItemsSource = _recentPager.PageItems;
        _currentUser = currentUser;
        Loaded += ManualRfidTransactionsPage_Loaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        OpenEntryButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

    }

    private async void ManualRfidTransactionsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _openPager.SetItems(await _transactionService.GetOpenAsync());
            _recentPager.SetItems(await _transactionService.GetRecentAsync());
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

        var window = new OpenManualRfidWindow(_currentUser.UserId)
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

        var window = new CloseManualRfidWindow(row)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _transactionService.CloseManualAsync(
                row.TransactionId,
                _currentUser.UserId,
                window.Remarks);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RFID Manual", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
