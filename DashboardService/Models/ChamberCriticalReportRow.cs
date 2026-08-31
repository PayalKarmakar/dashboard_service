namespace DashboardService.Models;

public class ChamberCriticalReportRow
{
    public long SensorViolationId { get; set; }

    public long ChamberId { get; set; }

    public string ChamberCode { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public string Parameter { get; set; } = string.Empty;

    public string? Unit { get; set; }

    public decimal ActualValueAtStart { get; set; }

    public decimal ThresholdValue { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public bool IsOngoing { get; set; }

    public TimeSpan Duration { get; set; }

    public string Severity { get; set; } = string.Empty;

    public string SeverityDisplay => Severity switch
    {
        "CRITICAL" => "Critical",
        "WARNING" => "Warning",
        _ => Severity
    };

    public string StartedDisplay => StartedAt.ToString("dd MMM yyyy hh:mm:ss tt");

    public string EndedDisplay => IsOngoing
        ? "Ongoing"
        : EndedAt?.ToString("dd MMM yyyy hh:mm:ss tt") ?? "-";

    public string DurationDisplay => ReportDurationFormatter.Format(Duration);

    public string ValueDisplay =>
        string.IsNullOrWhiteSpace(Unit)
            ? ActualValueAtStart.ToString("0.##")
            : $"{ActualValueAtStart:0.##} {Unit}";

    public string ThresholdDisplay =>
        string.IsNullOrWhiteSpace(Unit)
            ? ThresholdValue.ToString("0.##")
            : $"{ThresholdValue:0.##} {Unit}";

    public string StatusDisplay => IsOngoing ? "Ongoing" : "Resolved";
}
