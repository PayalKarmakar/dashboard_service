namespace DashboardService.Models;

public sealed class MonitoringCameraCountLine
{
    public string CameraName { get; init; } = string.Empty;

    public string ChamberName { get; init; } = string.Empty;

    public int DetectedCount { get; init; }

    public string DisplayText =>
        string.IsNullOrWhiteSpace(ChamberName)
            ? $"{CameraName}: {DetectedCount}"
            : $"{ChamberName} · {CameraName}: {DetectedCount}";
}
