//using DashboardService.Views;
//using System.Windows;

//namespace DashboardService
//{
//    public partial class MainWindow : Window
//    {
//        public MainWindow()
//        {
//            InitializeComponent();

//            MainFrame.Navigate(new LoginPage());
//        }
//    }
//}

using DashboardService.Models;
using DashboardService.Views;
using System.Windows;
using System.Windows.Controls;

namespace DashboardService
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            GlobalSidebar.SetExpanded(true);
            SidebarToggleButton.IsChecked = false;

            MainFrame.Navigate(new LoginPage());
        }

        public Frame MainNavigationFrame => MainFrame;

        public void ShowSidebar(User currentUser)
        {
            GlobalSidebar.CurrentUser = currentUser;
            GlobalSidebar.Visibility = Visibility.Visible;
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool expanded = SidebarToggleButton.IsChecked == true;

            GlobalSidebar.SetExpanded(expanded);

            SidebarToggleButton.Margin = expanded
                ? new Thickness(218, 14, 0, 0)
                : new Thickness(0, 14, 0, 0);
        }
    }
}