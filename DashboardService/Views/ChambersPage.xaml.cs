using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class ChambersPage : Page
{
    private readonly User _currentUser;
    private readonly ChamberService _chamberService = new();
    private readonly ListPager<Chamber> _chambersPager = new();

    public ChambersPage(User currentUser)
    {
        InitializeComponent();
        ChambersPagerBar.Bind(_chambersPager);
        ChambersGrid.ItemsSource = _chambersPager.PageItems;
        _currentUser = currentUser;
        Loaded += ChambersPage_Loaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        AddChamberButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

    }

    private async void ChambersPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadChambersAsync();
    }

    private async Task LoadChambersAsync()
    {
        try
        {
            _chambersPager.SetItems(await _chamberService.GetAllAsync());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Chambers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddChamber_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only admin can add chambers.", "Chambers", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new AddChamberWindow(_currentUser.UserId)
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadChambersAsync();
        }
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can change chamber status.",
                "Chambers",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not Chamber chamber)
        {
            return;
        }

        bool nextActive = !chamber.IsActive;
        string action = nextActive ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Do you want to {action} \"{chamber.ChamberName}\"?",
            "Chambers",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _chamberService.SetActiveAsync(chamber.ChamberId, nextActive, _currentUser.UserId);
            await LoadChambersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Chambers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
