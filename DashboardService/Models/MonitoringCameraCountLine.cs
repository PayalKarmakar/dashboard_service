namespace DashboardService.Models;

public sealed class MonitoringCameraCountLine
{
    public string CameraName { get; init; } = string.Empty;

    public string ChamberName { get; init; } = string.Empty;

    public int DetectedCount { get; init; }

    /// <summary>Per-line label without camera name (count only).</summary>
    public string DisplayText => DetectedCount.ToString();
}
