using System.Windows.Controls;
using System.Windows.Navigation;
using DashboardService.Models;
using DashboardService.Views;

namespace DashboardService.Helpers;

public static class AppNavigation
{
    public static void Go(NavigationService? navigation, string menu, User currentUser)
    {
        if (navigation == null)
        {
            return;
        }

        Page page = menu switch
        {
            "Dashboard" => new DashboardPage(currentUser),
            "Chambers" => new ChambersPage(currentUser),
            "Employees" => new EmployeesPage(currentUser),
            "Readers" => new ReadersPage(currentUser),
            "Reports" => new ReportsPage(currentUser),
            "ChamberEmployeesReport" => new ChamberEmployeesReportPage(currentUser),
            "ChamberCriticalReport" => new ChamberCriticalReportPage(currentUser),
            "ProductionLossReport" => new ProductionLossReportPage(currentUser),
            "SensorReadingsReport" => new SensorReadingsReportPage(currentUser),
            "SystemLogsReport" => new SystemLogsReportPage(currentUser),
            "SensorConfiguration" => new SensorConfigurationPage(currentUser),
            "CameraConfiguration" => new CameraConfigurationPage(currentUser),
            "LiveCamera" => new LiveCameraPage(currentUser),
            _ => throw new InvalidOperationException(menu)
        };

        navigation.Navigate(page);

        while (navigation.CanGoBack)
        {
            navigation.RemoveBackEntry();
        }
    }
}
