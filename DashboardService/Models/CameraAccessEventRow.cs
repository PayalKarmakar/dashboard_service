namespace DashboardService.Models;

public sealed class CameraAccessEventRow
{
    public long EventId { get; set; }

    public DateTime OccurredAt { get; set; }

    public long CameraId { get; set; }

    public long ChamberId { get; set; }

    public string CameraName { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public int PersonCount { get; set; }

    public int? RfidScanCount { get; set; }

    public string Message { get; set; } = string.Empty;

    public string OccurredDisplay => OccurredAt.ToString("dd-MMM-yyyy HH:mm:ss");

    public string EventDisplay => EventType switch
    {
        "ENTRY" => "Entry",
        "EXIT" => "Exit",
        "NO_RFID" => "Unauthorized (No RFID)",
        "TAILGATE" => "Unauthorized (Tailgate)",
        "MATCHED" => "Verified",
        _ => EventType
    };

    public string RfidDisplay => RfidScanCount?.ToString() ?? "—";
}
