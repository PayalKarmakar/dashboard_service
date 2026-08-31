using System.Windows;
using System.Windows.Controls;

namespace DashboardService.Helpers;

public static class SidebarMenuHelper
{
    public static void ToggleSubMenu(StackPanel panel, TextBlock arrow)
    {
        bool open = panel.Visibility != Visibility.Visible;
        panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        arrow.Text = open ? "▲" : "▼";
    }

    public static void OpenSubMenu(StackPanel panel, TextBlock arrow)
    {
        panel.Visibility = Visibility.Visible;
        arrow.Text = "▲";
    }
}
