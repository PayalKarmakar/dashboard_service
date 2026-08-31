namespace DashboardService.Models;

public sealed class VoiceAnnouncementLine
{
    public VoiceAnnouncementLine(string message, string culture = "en-IN")
    {
        Message = message;
        Culture = culture;
    }

    public string Message { get; }

    public string Culture { get; }
}
