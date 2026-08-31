namespace DashboardService.Models;

public class RfidReaderStatusSnapshot
{
    public bool ServiceAvailable { get; set; }

    public string? Message { get; set; }

    public IReadOnlyList<RfidReaderLiveStatus> Connected { get; set; } =
        Array.Empty<RfidReaderLiveStatus>();

    public IReadOnlyList<RfidReaderLiveStatus> Disconnected { get; set; } =
        Array.Empty<RfidReaderLiveStatus>();
}
