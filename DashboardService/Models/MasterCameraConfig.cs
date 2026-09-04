namespace DashboardService.Models;

public sealed class MasterCameraConfig
{
    public long CameraId { get; set; }

    public long ChamberId { get; set; }

    public string ChamberName { get; set; } = string.Empty;

    public string CameraName { get; set; } = string.Empty;

    public string CameraPurpose { get; set; } = "ENTRY";

    public string? IpAddress { get; set; }

    public string RtspUrl { get; set; } = string.Empty;

    public long? RfidReaderId { get; set; }

    public string RfidReaderName { get; set; } = string.Empty;

    public bool PersonDetectionEnabled { get; set; } = true;

    public int MatchWindowSeconds { get; set; } = 10;

    public bool AlertOnNoRfid { get; set; } = true;

    public bool AlertOnTailgate { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public string PurposeDisplay => CameraPurpose switch
    {
        "ENTRY" => "Entry",
        "EXIT" => "Exit",
        "MONITORING" => "Monitoring",
        "DOOR" => "Entry", // legacy
        _ => CameraPurpose
    };

    public string DetectionDisplay => PersonDetectionEnabled ? "On" : "Off";

    public string LinkedReaderDisplay =>
        string.IsNullOrWhiteSpace(RfidReaderName) ? "—" : RfidReaderName;

    public string RtspDisplay =>
        RtspUrl.Length > 48 ? RtspUrl[..45] + "..." : RtspUrl;
}
