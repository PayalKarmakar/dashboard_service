namespace DashboardService.Models;

public class RfidReader
{
    public long ReaderId { get; set; }

    public string ReaderName { get; set; } = string.Empty;

    public string ReaderSerialNo { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string ReaderPurpose { get; set; } = "ENTRY";

    public bool IsActive { get; set; } = true;

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public string PurposeDisplay => ReaderPurpose switch
    {
        "ENTRY" => "Entry",
        "EXIT" => "Exit",
        "EMPLOYEE_REGISTRATION" => "Employee Registration",
        "ENTRY_EXIT" => "Entry / Exit",
        _ => ReaderPurpose
    };

    public string DisplayLabel =>
        $"{ReaderName}  ·  {IpAddress}:{Port}";
}
