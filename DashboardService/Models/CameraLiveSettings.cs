namespace DashboardService.Models;

public sealed class CameraLiveSettings
{
    public double MinConfidence { get; set; } = 0.40;

    public int ZoneDividerPercent { get; set; } = 50;

    public int RfidRefreshIntervalSeconds { get; set; } = 2;

    public int DetectEveryNFrames { get; set; } = 2;

    public int InputSize { get; set; } = 320;

    public string ModelPath { get; set; } = "Models/Vision/yolov5n.onnx";

    public bool UsePythonService { get; set; } = true;

    /// <summary>
    /// When true, Entry/Exit/Unauthorized stat cards show for ENTRY/EXIT cameras.
    /// </summary>
    public bool ShowEntryExitStats { get; set; } = true;

    /// <summary>
    /// When true, all active cameras are monitored automatically after login.
    /// </summary>
    public bool BackgroundMonitoringEnabled { get; set; } = true;

    /// <summary>
    /// When true, speak voice alerts for camera entry/exit violations.
    /// </summary>
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>Max active MONITORING cameras allowed per chamber.</summary>
    public int MaxMonitoringCamerasPerChamber { get; set; } = 6;

    /// <summary>Show each MONITORING camera count on the dashboard.</summary>
    public bool ShowPerMonitoringCameraCounts { get; set; } = true;

    /// <summary>Sum | Max | PerCamera — how MONITORING counts roll up per chamber.</summary>
    public string ChamberOccupancyAggregation { get; set; } = "Sum";

    public bool UsesChamberLevelMonitoringAggregation =>
        !string.Equals(
            ChamberOccupancyAggregation,
            "PerCamera",
            StringComparison.OrdinalIgnoreCase);

    public int AggregateMonitoringCounts(IEnumerable<int> counts)
    {
        var list = counts.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        if (string.Equals(ChamberOccupancyAggregation, "Max", StringComparison.OrdinalIgnoreCase))
        {
            return list.Max();
        }

        if (string.Equals(ChamberOccupancyAggregation, "PerCamera", StringComparison.OrdinalIgnoreCase))
        {
            return list.Max();
        }

        return list.Sum();
    }
}
