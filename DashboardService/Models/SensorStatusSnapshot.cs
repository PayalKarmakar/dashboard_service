namespace DashboardService.Models;

public sealed class SensorStatusSnapshot
{
    public bool ServiceAvailable { get; set; }

    public string? Message { get; set; }

    public IReadOnlyList<SensorLiveStatus> Connected { get; set; } =
        Array.Empty<SensorLiveStatus>();

    public IReadOnlyList<SensorLiveStatus> Disconnected { get; set; } =
        Array.Empty<SensorLiveStatus>();
}
