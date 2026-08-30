using DashboardService.Helpers;
using DashboardService.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace DashboardService.Views
{
    public partial class LoginPage : Page
    {
        private readonly LoginService _loginService;

        public LoginPage()
        {
            InitializeComponent();
            _loginService = new LoginService();
            Loaded += (_, _) => UserIdTextBox.Focus();
        }

        private void ChangePasswordLink_Click(object sender, RoutedEventArgs e)
        {
            string userName = UserIdTextBox.Text.Trim();
            var owner = Window.GetWindow(this);

            var window = new ChangePasswordWindow(
                string.IsNullOrWhiteSpace(userName) ? null : userName,
                lockUserId: false)
            {
                Owner = owner
            };

            window.ShowDialog();
        }

        private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ExternalLinks.OpenCodeInq();
            e.Handled = true;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginAsync();
        }

        private async void LoginFields_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await TryLoginAsync();
            }
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool showPassword = VisiblePasswordTextBox.Visibility != Visibility.Visible;

            if (showPassword)
            {
                VisiblePasswordTextBox.Text = PasswordTextBox.Password;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                VisiblePasswordTextBox.Visibility = Visibility.Visible;
                PasswordEyeButton.Content = "🙈";
                PasswordEyeButton.ToolTip = "Hide password";
                VisiblePasswordTextBox.CaretIndex = VisiblePasswordTextBox.Text.Length;
                VisiblePasswordTextBox.Focus();
            }
            else
            {
                PasswordTextBox.Password = VisiblePasswordTextBox.Text;
                VisiblePasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordEyeButton.Content = "👁";
                PasswordEyeButton.ToolTip = "Show password";
                PasswordTextBox.Focus();
            }
        }

        private string GetPassword()
        {
            if (VisiblePasswordTextBox.Visibility == Visibility.Visible)
            {
                return VisiblePasswordTextBox.Text;
            }

            return PasswordTextBox.Password;
        }

        private void ClearPassword()
        {
            PasswordTextBox.Clear();
            VisiblePasswordTextBox.Clear();
        }

        private async Task TryLoginAsync()
        {
            string userName = UserIdTextBox.Text.Trim();
            string password = GetPassword();

            if (string.IsNullOrWhiteSpace(userName))
            {
                MessageBox.Show(
                    "Please enter User ID.",
                    "Login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                UserIdTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Please enter Password.",
                    "Login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                PasswordTextBox.Focus();
                return;
            }

            LoginButton.IsEnabled = false;

            try
            {
                var user = await _loginService.ValidateLoginAsync(userName, password);

                if (user == null)
                {
                    MessageBox.Show(
                        "Invalid User ID or Password.",
                        "Login Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    ClearPassword();
                    if (VisiblePasswordTextBox.Visibility == Visibility.Visible)
                    {
                        VisiblePasswordTextBox.Focus();
                    }
                    else
                    {
                        PasswordTextBox.Focus();
                    }
                    return;
                }

                NavigationService?.Navigate(new DashboardPage(user));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to connect to the database.\n\n{ex.Message}",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }
    }
}
