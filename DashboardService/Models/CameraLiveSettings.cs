namespace DashboardService.Models;

public sealed class CameraLiveSettings
{
    public double MinConfidence { get; set; } = 0.45;

    public int ZoneDividerPercent { get; set; } = 50;

    public int RfidRefreshIntervalSeconds { get; set; } = 2;
}
