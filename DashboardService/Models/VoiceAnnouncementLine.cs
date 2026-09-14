namespace DashboardService.Models;

public sealed class VoiceAnnouncementLine
{
    public VoiceAnnouncementLine(string message, string culture = "en-IN", bool playEmergencySound = false)
    {
        Message = message;
        Culture = culture;
        PlayEmergencySound = playEmergencySound;
    }

    public string Message { get; }

    public string Culture { get; }

    public bool PlayEmergencySound { get; }
}
