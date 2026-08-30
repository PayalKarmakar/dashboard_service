namespace DashboardService.Models;

public class EmployeeRecord
{
    public long EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string CardUid { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public string Mobile { get; set; } = string.Empty;

    public long? ChamberId { get; set; }

    public string ChamberName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string Status => IsActive ? "Active" : "Inactive";

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";
}
