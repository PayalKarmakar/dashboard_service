namespace DashboardService.Models;

public sealed class AnnouncementRequest
{
    public string Message { get; init; } = string.Empty;

    public string? SecondaryMessage { get; init; }

    public string MessageCulture { get; init; } = "en-IN";

    public string? SecondaryCulture { get; init; } = "bn-IN";

    public long AlertId { get; init; }

    public long TransactionId { get; init; }

    public IReadOnlyList<VoiceAnnouncementLine> GetVoiceLines()
    {
        var lines = new List<VoiceAnnouncementLine>
        {
            new(Message, MessageCulture)
        };

        if (!string.IsNullOrWhiteSpace(SecondaryMessage))
        {
            lines.Add(new VoiceAnnouncementLine(
                SecondaryMessage,
                SecondaryCulture ?? "bn-IN"));
        }

        return lines;
    }
}
