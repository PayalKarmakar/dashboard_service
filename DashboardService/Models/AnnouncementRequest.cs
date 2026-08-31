namespace DashboardService.Models;

public sealed class AnnouncementRequest
{
    public string Message { get; init; } = string.Empty;

    public string? SecondaryMessage { get; init; }

    public string MessageCulture { get; init; } = "en-IN";

    public string? SecondaryCulture { get; init; } = "bn-IN";

    public long AlertId { get; init; }

    public long TransactionId { get; init; }

    public IReadOnlyList<VoiceAnnouncementLine> GetVoiceLines(string? preferredCulture = null)
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

        if (string.IsNullOrWhiteSpace(preferredCulture))
        {
            return lines;
        }

        var preferred = lines
            .Where(line => line.Culture.Equals(preferredCulture, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (preferred.Count > 0)
        {
            return preferred;
        }

        // Fallback: English, then first available line.
        var english = lines
            .Where(line => line.Culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return english.Count > 0 ? english : lines;
    }
}
