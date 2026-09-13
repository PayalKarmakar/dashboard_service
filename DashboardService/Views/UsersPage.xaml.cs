using System.Windows;
using System.Windows.Controls;
using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class UsersPage : Page
{
    private readonly User _currentUser;
    private readonly UserService _userService = new();
    private readonly ListPager<User> _usersPager = new();

    public UsersPage(User currentUser)
    {
        InitializeComponent();
        UsersPagerBar.Bind(_usersPager);
        UsersGrid.ItemsSource = _usersPager.PageItems;
        _currentUser = currentUser;
        Loaded += UsersPage_Loaded;

        bool isAdmin = string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase);
        AddUserButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void UsersPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            _usersPager.SetItems(await _userService.GetAllAsync());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only admin can add users.", "Users", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new AddUserWindow
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() == true)
        {
            await LoadUsersAsync();
        }
    }

    private async void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_currentUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Only admin can change user status.",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is not Button button || button.DataContext is not User user)
        {
            return;
        }

        if (user.UserId == _currentUser.UserId)
        {
            MessageBox.Show(
                "You cannot deactivate your own account.",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool nextActive = !user.IsActive;
        string action = nextActive ? "activate" : "deactivate";

        var confirm = MessageBox.Show(
            $"Do you want to {action} \"{user.UserName}\"?",
            "Users",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _userService.SetActiveAsync(user.UserId, nextActive);
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
