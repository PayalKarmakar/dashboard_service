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

    /// <summary>HORIZONTAL, VERTICAL, or DIAGONAL (corner-wise door line).</summary>
    public string DoorLineMode { get; set; } = DoorLineOrientation.Horizontal;

    /// <summary>Door line position 20–80 (horizontal: height %, vertical: width %).</summary>
    public int? ZoneDividerPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public int GetEffectiveZoneDividerPercent(int fallback = 50) =>
        ZoneDividerPercent is >= 20 and <= 80
            ? ZoneDividerPercent.Value
            : Math.Clamp(fallback, 20, 80);

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public bool ShowsDoorLine =>
        !string.Equals(CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase);

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

    public string DoorLineOrientationDisplay =>
        ShowsDoorLine
            ? DoorLineOrientation.Normalize(DoorLineMode) switch
            {
                DoorLineOrientation.Vertical => "Vertical",
                DoorLineOrientation.Diagonal => "Diagonal",
                _ => "Horizontal"
            }
            : "—";

    public string DoorLinePositionDisplay =>
        ShowsDoorLine ? $"{GetEffectiveZoneDividerPercent(50)}%" : "—";
}
