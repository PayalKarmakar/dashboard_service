namespace DashboardService.Models;

public class ProductionLossReportRow
{
    public long ChamberId { get; set; }

    public string ChamberCode { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public DateTime LossStartedAt { get; set; }

    public DateTime? LossEndedAt { get; set; }

    public bool IsOngoing { get; set; }

    public TimeSpan Duration { get; set; }

    public string StartedDisplay => LossStartedAt.ToString("dd MMM yyyy hh:mm:ss tt");

    public string EndedDisplay => IsOngoing
        ? "Ongoing"
        : LossEndedAt?.ToString("dd MMM yyyy hh:mm:ss tt") ?? "-";

    public string DurationDisplay => ReportDurationFormatter.Format(Duration);

    public string StatusDisplay => IsOngoing ? "Production Lost (Live)" : "Production Lost";
}
