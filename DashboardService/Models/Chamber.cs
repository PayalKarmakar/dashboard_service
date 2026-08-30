namespace DashboardService.Models;

public class Chamber
{
    public long ChamberId { get; set; }

    public string ChamberCode { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public string ChamberLocation { get; set; } = string.Empty;

    public int? MemberThreshold { get; set; }

    public int? TimeThreshold { get; set; }

    public bool IsActive { get; set; } = true;

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(ChamberCode)
            ? ChamberName
            : $"{ChamberName} ({ChamberCode})";
    }
}
