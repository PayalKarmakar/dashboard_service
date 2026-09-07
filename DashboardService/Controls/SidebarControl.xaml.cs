using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DashboardService.Controls;

public partial class SidebarControl : UserControl
{
    private static readonly HashSet<string> ReportPages =
    [
        "Reports",
        "CameraAccessReport",
        "ChamberEmployeesReport",
        "ChamberCriticalReport",
        "ProductionLossReport",
        "SensorReadingsReport",
        "SystemLogsReport"
    ];

    private static readonly HashSet<string> ConfigurationPages =
    [
        "SensorConfiguration",
        "CameraConfiguration"
    ];

    private readonly Dictionary<string, Button> _menuButtons = new();

    public SidebarControl()
    {
        InitializeComponent();

        IsExpanded = true;
        Width = 230;

        RegisterMenuButton("Dashboard", DashboardButton);
        RegisterMenuButton("Chambers", ChambersButton);
        RegisterMenuButton("Employees", EmployeesButton);
        RegisterMenuButton("Readers", ReadersButton);
        RegisterMenuButton("LiveCamera", LiveCameraButton);
        RegisterMenuButton("ManualRfidTransactions", ManualRfidButton);
        RegisterMenuButton("Reports", ReportsButton);
        RegisterMenuButton("CameraAccessReport", CameraAccessButton);
        RegisterMenuButton("ChamberEmployeesReport", ChamberEmployeesButton);
        RegisterMenuButton("ChamberCriticalReport", ChamberCriticalButton);
        RegisterMenuButton("ProductionLossReport", ProductionLossButton);
        RegisterMenuButton("SensorReadingsReport", SensorReadingsButton);
        RegisterMenuButton("SystemLogsReport", SystemLogsButton);
        RegisterMenuButton("SensorConfiguration", SensorConfigurationButton);
        RegisterMenuButton("CameraConfiguration", CameraConfigurationButton);
        RegisterMenuButton("Configuration", ConfigurationButton);
    }

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

    public string ActivePage
    {
        get => (string)GetValue(ActivePageProperty);
        set => SetValue(ActivePageProperty, value);
    }

    public static readonly DependencyProperty ActivePageProperty =
        DependencyProperty.Register(
            nameof(ActivePage),
            typeof(string),
            typeof(SidebarControl),
            new PropertyMetadata(string.Empty, OnActivePageChanged));

    private static void OnActivePageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SidebarControl sidebar)
        {
            sidebar.ApplyActivePage(e.NewValue as string ?? string.Empty);
        }
    }

    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;
        SidebarContent.Visibility = Visibility.Visible;
        Width = expanded ? 230 : 76;
    }

    public void SetActivePage(string page)
    {
        ActivePage = page;
    }

    private void RegisterMenuButton(string key, Button button)
    {
        _menuButtons[key] = button;
    }

    private void ApplyActivePage(string page)
    {
        ClearActiveStyles();

        if (ReportPages.Contains(page))
        {
            OpenReportsSubMenu();
        }

        if (ConfigurationPages.Contains(page))
        {
            OpenConfigurationSubMenu();
        }

        if (_menuButtons.TryGetValue(page, out Button? button))
        {
            SetActiveStyle(button);
            return;
        }

        if (ReportPages.Contains(page))
        {
            SetActiveStyle(ReportsButton);
        }
        else if (ConfigurationPages.Contains(page))
        {
            SetActiveStyle(ConfigurationButton);
        }
    }

    private void ClearActiveStyles()
    {
        foreach (Button button in _menuButtons.Values)
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(ForegroundProperty);
            button.FontWeight = FontWeights.Normal;
        }
    }

    private void SetActiveStyle(Button button)
    {
        button.Background = (Brush)FindResource("SidebarActiveBgBrush");
        button.Foreground = (Brush)FindResource("SidebarTextActiveBrush");
        button.FontWeight = FontWeights.SemiBold;
    }

    private void OpenReportsSubMenu()
    {
        ReportsSubMenu.Visibility = Visibility.Visible;
        ReportsArrow.Text = "⌄";
    }

    private void OpenConfigurationSubMenu()
    {
        ConfigurationSubMenu.Visibility = Visibility.Visible;
        ConfigurationArrow.Text = "⌄";
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => Navigate("Dashboard");
    private void Chambers_Click(object sender, RoutedEventArgs e) => Navigate("Chambers");
    private void Employees_Click(object sender, RoutedEventArgs e) => Navigate("Employees");
    private void Readers_Click(object sender, RoutedEventArgs e) => Navigate("Readers");
    private void LiveCamera_Click(object sender, RoutedEventArgs e) => Navigate("LiveCamera");
    private void ManualRfid_Click(object sender, RoutedEventArgs e) => Navigate("ManualRfidTransactions");

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

    private void EntryExit_Click(object sender, RoutedEventArgs e) => Navigate("Reports");
    private void CameraAccess_Click(object sender, RoutedEventArgs e) => Navigate("CameraAccessReport");
    private void ChamberEmployees_Click(object sender, RoutedEventArgs e) => Navigate("ChamberEmployeesReport");
    private void ChamberCritical_Click(object sender, RoutedEventArgs e) => Navigate("ChamberCriticalReport");
    private void ProductionLoss_Click(object sender, RoutedEventArgs e) => Navigate("ProductionLossReport");
    private void SensorReadings_Click(object sender, RoutedEventArgs e) => Navigate("SensorReadingsReport");
    private void SystemLogs_Click(object sender, RoutedEventArgs e) => Navigate("SystemLogsReport");

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

    private void SensorConfiguration_Click(object sender, RoutedEventArgs e) => Navigate("SensorConfiguration");
    private void CameraConfiguration_Click(object sender, RoutedEventArgs e) => Navigate("CameraConfiguration");

    private void ThemeToggle_Click(object sender, MouseButtonEventArgs e)
    {
        ThemeService.SetDarkMode(!ThemeService.IsDarkMode);
    }

    private void Navigate(string page)
    {
        if (CurrentUser == null)
        {
            return;
        }

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            AppNavigation.Go(
                mainWindow.MainNavigationFrame.NavigationService,
                page,
                CurrentUser);
        }
    }
}
