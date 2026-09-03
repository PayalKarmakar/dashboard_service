namespace DashboardService.Models;

public sealed class SystemLogConnectionStatus
{
    public string DeviceName { get; set; } = string.Empty;

    public string LocationDisplay { get; set; } = string.Empty;

    public string DetailDisplay { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool IsConnected { get; set; }

    public string StatusLabel => IsConnected ? "Connected" : "Disconnected";
}
