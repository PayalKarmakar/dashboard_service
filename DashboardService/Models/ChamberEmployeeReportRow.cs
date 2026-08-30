namespace DashboardService.Models;

public class ChamberEmployeeReportRow
{
    public string ChamberCode { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string CardUid { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public string Mobile { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string StatusDisplay => IsActive ? "Active" : "Inactive";

    public string ChamberDisplay =>
        string.IsNullOrWhiteSpace(ChamberName)
            ? "Unassigned"
            : (string.IsNullOrWhiteSpace(ChamberCode)
                ? ChamberName
                : $"{ChamberName} ({ChamberCode})");
}
