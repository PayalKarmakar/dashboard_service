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

    public bool IsLost { get; set; }

    public long? LostCreatedBy { get; set; }

    public DateTime? LostUpdatedAt { get; set; }

    public string Status =>
        IsLost ? "Lost" : (IsActive ? "Active" : "Inactive");

    public string ToggleActionText => IsActive ? "Deactivate" : "Activate";

    public string LostActionText => IsLost ? "Clear Lost" : "Mark Lost";
}
