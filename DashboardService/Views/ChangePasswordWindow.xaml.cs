using DashboardService.Models;
using DashboardService.Services;
using System.Windows;

namespace DashboardService.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly LoginService _loginService = new();
    private readonly bool _lockUserId;

    public ChangePasswordWindow(string? userName = null, bool lockUserId = false)
    {
        InitializeComponent();

        _lockUserId = lockUserId;

        if (!string.IsNullOrWhiteSpace(userName))
        {
            UserIdTextBox.Text = userName.Trim();
        }

        if (_lockUserId)
        {
            UserIdTextBox.IsReadOnly = true;
            UserIdTextBox.Background = System.Windows.Media.Brushes.WhiteSmoke;
            CurrentPasswordBox.Focus();
        }
        else
        {
            UserIdTextBox.Focus();
        }
    }

    public static bool? ShowDialogForUser(Window? owner, User? user = null)
    {
        var window = new ChangePasswordWindow(
            user?.UserName,
            lockUserId: user != null)
        {
            Owner = owner
        };

        return window.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string userName = UserIdTextBox.Text.Trim();
        string currentPassword = CurrentPasswordBox.Password;
        string newPassword = NewPasswordBox.Password;
        string confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            MessageBox.Show(
                "Please enter User ID.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UserIdTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            MessageBox.Show(
                "Please enter your current password.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CurrentPasswordBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            MessageBox.Show(
                "New password must be at least 6 characters.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            NewPasswordBox.Focus();
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "New password and confirm password do not match.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ConfirmPasswordBox.Focus();
            return;
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "New password must be different from the current password.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            NewPasswordBox.Focus();
            return;
        }

        SaveButton.IsEnabled = false;

        try
        {
            var result = await _loginService.ChangePasswordAsync(
                userName,
                currentPassword,
                newPassword);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "Unable to change password.",
                    "Change Password",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                "Password updated successfully.",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to update password.\n\n{ex.Message}",
                "Change Password",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
