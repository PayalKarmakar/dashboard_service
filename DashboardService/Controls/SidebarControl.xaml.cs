using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DashboardService.Controls;

public partial class SidebarControl : UserControl
{
    public SidebarControl()
    {
        InitializeComponent();

        IsExpanded = true;
        Width = 230;
    }

    // =========================================================
    // EXPANDED / COLLAPSED
    // =========================================================

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(
            nameof(IsExpanded),
            typeof(bool),
            typeof(SidebarControl),
            new PropertyMetadata(true));


    // =========================================================
    // CURRENT USER
    // =========================================================

    public User? CurrentUser
    {
        get => (User?)GetValue(CurrentUserProperty);
        set => SetValue(CurrentUserProperty, value);
    }

    public static readonly DependencyProperty CurrentUserProperty =
        DependencyProperty.Register(
            nameof(CurrentUser),
            typeof(User),
            typeof(SidebarControl),
            new PropertyMetadata(null));


    // =========================================================
    // COLLAPSE / EXPAND
    // =========================================================

  
    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;        
        SidebarContent.Visibility = Visibility.Visible;

        Width = expanded ? 230 : 76;
    }

    // =========================================================
    // MAIN MENU
    // =========================================================

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        Navigate("Dashboard");
    }

    private void Chambers_Click(object sender, RoutedEventArgs e)
    {
        Navigate("Chambers");
    }

    private void Employees_Click(object sender, RoutedEventArgs e)
    {
        Navigate("Employees");
    }

    private void Readers_Click(object sender, RoutedEventArgs e)
    {
        Navigate("Readers");
    }

    private void LiveCamera_Click(object sender, RoutedEventArgs e)
    {
        Navigate("LiveCamera");
    }

    private void ManualRfid_Click(object sender, RoutedEventArgs e)
    {
        Navigate("ManualRfid");
    }


    // =========================================================
    // REPORTS
    // =========================================================

    private void Reports_Click(object sender, RoutedEventArgs e)
    {
        ReportsSubMenu.Visibility =
            ReportsSubMenu.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        ReportsArrow.Text =
            ReportsSubMenu.Visibility == Visibility.Visible
                ? "⌄"
                : "›";
    }

    private void EntryExit_Click(object sender, RoutedEventArgs e)
    {
        Navigate("Reports");
    }

    private void CameraAccess_Click(object sender, RoutedEventArgs e)
    {
        Navigate("CameraAccess");
    }

    private void ChamberEmployees_Click(object sender, RoutedEventArgs e)
    {
        Navigate("ChamberEmployeesReport");
    }

    private void ChamberCritical_Click(object sender, RoutedEventArgs e)
    {
        Navigate("ChamberCritical");
    }

    private void ProductionLoss_Click(object sender, RoutedEventArgs e)
    {
        Navigate("ProductionLoss");
    }

    private void SensorReadings_Click(object sender, RoutedEventArgs e)
    {
        Navigate("SensorReadings");
    }


    // =========================================================
    // CONFIGURATION
    // =========================================================

    private void Configuration_Click(object sender, RoutedEventArgs e)
    {
        ConfigurationSubMenu.Visibility =
            ConfigurationSubMenu.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        ConfigurationArrow.Text =
            ConfigurationSubMenu.Visibility == Visibility.Visible
                ? "⌄"
                : "›";
    }

    private void SensorConfiguration_Click(object sender, RoutedEventArgs e)
    {
        Navigate("SensorConfiguration");
    }

    private void CameraConfiguration_Click(object sender, RoutedEventArgs e)
    {
        Navigate("CameraConfiguration");
    }


    // =========================================================
    // THEME
    // =========================================================

    private void ThemeToggle_Click(object sender, MouseButtonEventArgs e)
    {
        ThemeService.SetDarkMode(!ThemeService.IsDarkMode);
    }


    // =========================================================
    // NAVIGATION
    // =========================================================

    private void Navigate(string page)
    {
        if (CurrentUser == null)
            return;

        var window = Window.GetWindow(this);

        if (window is MainWindow mainWindow)
        {
            AppNavigation.Go(
                mainWindow.MainNavigationFrame.NavigationService,
                page,
                CurrentUser);
        }
    }
}