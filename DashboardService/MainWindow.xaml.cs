using DashboardService.Views;
using System.Windows;

namespace DashboardService
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            MainFrame.Navigate(new LoginPage());
        }
    }
}