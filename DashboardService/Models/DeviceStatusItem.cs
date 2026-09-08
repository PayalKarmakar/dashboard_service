namespace DashboardService.Models;

public sealed class DeviceStatusItem
{
    public string Name { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public bool IsConnected { get; set; }

    public string StatusLabel => IsConnected ? "ON" : "OFF";
}
