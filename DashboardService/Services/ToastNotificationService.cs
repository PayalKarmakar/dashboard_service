using DashboardService.Controls;
using DashboardService.Models;

namespace DashboardService.Services;

public static class ToastNotificationService
{
    private static ToastHostControl? _host;

    public static void Register(ToastHostControl host)
    {
        _host = host;
    }

    public static void ShowCameraViolation(CameraDoorAlert alert)
    {
        if (!IsEntryExitViolation(alert))
        {
            return;
        }

        bool critical = alert.AlertType is "NO_RFID" or "NO_RFID_EXIT";
        string title = alert.AlertType switch
        {
            "NO_RFID" => "Camera entry without RFID",
            "NO_RFID_EXIT" => "Camera exit without RFID",
            "TAILGATE" => "Possible entry tailgating",
            "EXIT_TAILGATE" => "Possible exit tailgating",
            _ => alert.TitleDisplay
        };

        _host?.ShowToast(title, alert.Message, critical);
    }

    public static bool IsEntryExitViolation(CameraDoorAlert alert) =>
        alert.AlertType is "NO_RFID" or "NO_RFID_EXIT" or "TAILGATE" or "EXIT_TAILGATE";
}
