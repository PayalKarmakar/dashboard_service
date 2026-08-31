namespace DashboardService.Models;

public sealed class VoiceAlertMessage
{
    public long MessageId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string AlertType { get; set; } = string.Empty;

    public string Culture { get; set; } = string.Empty;

    public string MessageTemplate { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
