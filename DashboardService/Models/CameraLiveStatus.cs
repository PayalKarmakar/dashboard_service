namespace DashboardService.Models;

public sealed class CameraLiveStatus
{
    public long CameraId { get; set; }

    public string CameraName { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public bool IsConnected { get; set; }
}
