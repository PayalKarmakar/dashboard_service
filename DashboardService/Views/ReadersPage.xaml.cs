using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class ReadersPage : Page
{
    private readonly User _currentUser;
    private readonly RfidReaderService _readerService = new();
    private readonly ListPager<RfidReader> _readersPager = new();

    public ReadersPage(User currentUser)
    {
        InitializeComponent();
        ReadersPagerBar.Bind(_readersPager);
        ReadersGrid.ItemsSource = _readersPager.PageItems;
        _currentUser = currentUser;
        Loaded += ReadersPage_Loaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        AddReaderButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

    }

    private async void ReadersPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadReadersAsync();
    }

    private async Task LoadReadersAsync()
    {
        try
        {
            _readersPager.SetItems(await _readerService.GetAllAsync());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RFID Readers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddReader_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can add RFID readers.",
                "RFID Readers",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = new AddReaderWindow(_currentUser.UserId)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadReadersAsync();
        }
    }

    private async void EditReader_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can edit RFID readers.",
                "RFID Readers",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not RfidReader reader)
        {
            return;
        }

        var window = new AddReaderWindow(_currentUser.UserId, reader)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadReadersAsync();
        }
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can change reader status.",
                "RFID Readers",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not RfidReader reader)
        {
            return;
        }

        bool nextActive = !reader.IsActive;
        string action = nextActive ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Do you want to {action} \"{reader.ReaderName}\"?",
            "RFID Readers",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _readerService.SetActiveAsync(reader.ReaderId, nextActive, _currentUser.UserId);
            await LoadReadersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RFID Readers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
