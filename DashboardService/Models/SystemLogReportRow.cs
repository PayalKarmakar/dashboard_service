namespace DashboardService.Models;

public sealed class SystemLogReportRow
{
    public long LogId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string LogLevel { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string SourcePort { get; set; } = string.Empty;

    public string CreatedDisplay => CreatedAt.ToString("dd MMM yyyy hh:mm:ss tt");

    public string StatusDisplay
    {
        get
        {
            string eventType = EventType.ToUpperInvariant();
            if (eventType.Contains("DISCONNECTED"))
            {
                return "Disconnected";
            }

            if (eventType.Contains("RECONNECTED"))
            {
                return "Reconnected";
            }

            if (eventType.Contains("CONNECTED"))
            {
                return "Connected";
            }

            return EventType;
        }
    }
}
