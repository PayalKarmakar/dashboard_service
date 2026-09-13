using System.Windows;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddUserWindow : Window
{
    private readonly UserService _userService = new();

    public AddUserWindow()
    {
        InitializeComponent();
        Loaded += AddUserWindow_Loaded;
        UserNameTextBox.Focus();
    }

    private async void AddUserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var roles = await _userService.GetRolesAsync();
            RoleComboBox.ItemsSource = roles;
            if (roles.Count > 0)
            {
                RoleComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Add User", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string userName = UserNameTextBox.Text.Trim();
        string fullName = FullNameTextBox.Text.Trim();
        string password = PasswordBox.Password;
        string confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(fullName))
        {
            MessageBox.Show(
                "Username and full name are required.",
                "Add User",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (RoleComboBox.SelectedItem is not Role role)
        {
            MessageBox.Show(
                "Select a role.",
                "Add User",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            MessageBox.Show(
                "Password must be at least 6 characters.",
                "Add User",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "Password and confirm password do not match.",
                "Add User",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var user = new User
        {
            UserName = userName,
            FullName = fullName,
            Email = EmailTextBox.Text.Trim(),
            RoleId = role.RoleId,
            IsActive = ActiveCheckBox.IsChecked == true
        };

        try
        {
            await _userService.AddAsync(user, password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Add User",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
