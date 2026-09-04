namespace DashboardService.Models;

public class EntryExitReportRow
{
    public long TransactionId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string CardUid { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public DateTime EntryTime { get; set; }

    public DateTime? ExitTime { get; set; }

    public string Status { get; set; } = string.Empty;

    public string EntryDisplay => EntryTime.ToString("dd MMM yyyy hh:mm:ss tt");

    public string ExitDisplay => ExitTime.HasValue
        ? ExitTime.Value.ToString("dd MMM yyyy hh:mm:ss tt")
        : "-";

    public string DurationDisplay
    {
        get
        {
            var end = ExitTime ?? DateTime.Now;
            var span = end - EntryTime;
            if (span.TotalSeconds < 0)
            {
                return "00:00:00";
            }

            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    public string StatusDisplay => string.Equals(Status, "OPEN", StringComparison.OrdinalIgnoreCase)
        ? "Inside"
        : string.Equals(Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "CLOSED", StringComparison.OrdinalIgnoreCase)
            ? "Exited"
            : Status;
}
