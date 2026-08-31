namespace DashboardService.Models;

public class RfidReaderLiveStatus
{
    public long ReaderId { get; set; }

    public string ReaderName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string ReaderPurpose { get; set; } = string.Empty;

    public string PurposeDisplay => ReaderPurpose switch
    {
        "ENTRY" => "Entry",
        "EXIT" => "Exit",
        "EMPLOYEE_REGISTRATION" => "Employee Registration",
        "ENTRY_EXIT" => "Entry / Exit",
        _ => ReaderPurpose
    };

    public string LocationDisplay => $"{IpAddress}:{Port}";
}
