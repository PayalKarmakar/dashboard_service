namespace DashboardService.Models;

public sealed class MasterSensorConfig
{
    public long SensorId { get; set; }

    public long ChamberId { get; set; }

    public string SensorType { get; set; } = string.Empty;

    public string SensorModel { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public int DeviceId { get; set; }

    public int BaudRate { get; set; }

    public int DataBits { get; set; }

    public string Parity { get; set; } = string.Empty;

    public string StopBits { get; set; } = string.Empty;

    public int ResponseTimeoutMilliseconds { get; set; }

    public bool Enabled { get; set; }
}
