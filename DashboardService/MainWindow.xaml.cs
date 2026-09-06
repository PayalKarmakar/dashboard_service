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

            // Login page should use the complete window
            GlobalSidebar.Visibility = Visibility.Collapsed;
            SidebarToggleButton.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);

            MainFrame.Navigate(new LoginPage());
        }

        public Frame MainNavigationFrame => MainFrame;

        public void ShowSidebar(User currentUser)
        {
            GlobalSidebar.CurrentUser = currentUser;

            // Restore sidebar
            SidebarColumn.Width = new GridLength(230);
            GlobalSidebar.Visibility = Visibility.Visible;

            // Restore toggle
            SidebarToggleButton.Visibility = Visibility.Visible;
            SidebarToggleButton.IsChecked = true;

            GlobalSidebar.SetExpanded(true);
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool expanded = SidebarToggleButton.IsChecked == true;

            GlobalSidebar.SetExpanded(expanded);

            SidebarColumn.Width = expanded
                ? new GridLength(230)
                : new GridLength(76);
        }
    }
}