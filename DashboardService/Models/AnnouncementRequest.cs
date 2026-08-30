namespace DashboardService.Models;

public sealed class AnnouncementRequest
{
    public string Message { get; init; } = string.Empty;

    public long AlertId { get; init; }

    public long TransactionId { get; init; }
}
