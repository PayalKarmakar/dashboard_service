namespace DashboardService.Models;

public sealed class SensorLiveStatus
{
    public long SensorId { get; set; }

    public string SensorName { get; set; } = string.Empty;

    public string LocationDisplay { get; set; } = string.Empty;

    public string DetailDisplay { get; set; } = string.Empty;
}
