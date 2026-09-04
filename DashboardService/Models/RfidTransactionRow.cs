namespace DashboardService.Models;

public sealed class RfidTransactionRow
{
    public long TransactionId { get; set; }

    public long EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string CardUid { get; set; } = string.Empty;

    public long ChamberId { get; set; }

    public string ChamberName { get; set; } = string.Empty;

    public DateTime EntryTime { get; set; }

    public DateTime? ExitTime { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;

    public bool IsManuallyCorrected { get; set; }

    public long? CorrectedBy { get; set; }

    public DateTime? CorrectedAt { get; set; }

    public string EntryDisplay => EntryTime.ToString("dd-MMM-yyyy HH:mm:ss");

    public string ExitDisplay => ExitTime?.ToString("dd-MMM-yyyy HH:mm:ss") ?? "—";

    public string StatusDisplay =>
        string.Equals(Status, "OPEN", StringComparison.OrdinalIgnoreCase) ? "Open (Inside)" : "Closed (Exited)";

    public string ManualDisplay => IsManuallyCorrected ? "Yes" : "No";

    public string CorrectedAtDisplay => CorrectedAt?.ToString("dd-MMM-yyyy HH:mm:ss") ?? "—";

    public bool CanClose =>
        string.Equals(Status, "OPEN", StringComparison.OrdinalIgnoreCase) && ExitTime == null;
}
