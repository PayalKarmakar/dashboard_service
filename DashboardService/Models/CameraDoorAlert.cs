namespace DashboardService.Models;

public sealed class CameraDoorAlert
{
    public DateTime RaisedAt { get; init; } = DateTime.Now;

    public string AlertType { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public int CameraPersonCount { get; init; }

    public int RfidScanCount { get; init; }

    public string Severity => AlertType switch
    {
        "NO_RFID" or "NO_RFID_EXIT" => "CRITICAL",
        "TAILGATE" or "EXIT_TAILGATE" => "WARNING",
        _ => "INFO"
    };

    public string TitleDisplay => AlertType switch
    {
        "NO_RFID" => "No RFID entry scan",
        "NO_RFID_EXIT" => "No RFID exit scan",
        "TAILGATE" => "Tailgating (entry)",
        "EXIT_TAILGATE" => "Tailgating (exit)",
        "MATCHED" => "Entry verified",
        "EXIT_MATCHED" => "Exit verified",
        "OCCUPANCY_NO_RFID" => "Occupancy without RFID",
        "OCCUPANCY_MISMATCH" => "Occupancy mismatch",
        "OCCUPANCY_MATCHED" => "Occupancy matched",
        _ => AlertType
    };
}
