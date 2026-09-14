namespace DashboardService.Models;

public class ChamberAlertRule
{
    public long RuleId { get; set; }

    public long ChamberId { get; set; }

    public string AlertType { get; set; } = string.Empty;

    public int AlertTimeMinutes { get; set; }

    public string AnnouncementMessage { get; set; } = string.Empty;

    public bool IsAnnouncementEnabled { get; set; }

    public bool IsActive { get; set; } = true;

    public int MaxPlayCount { get; set; } = 1;

    public int RepeatAfterMinutes { get; set; } = 5;

    /// <summary>0 means keep creating violation sessions until the member exits.</summary>
    public const int ContinuePlayCount = 0;

    public static bool IsContinue(int playCount) => playCount <= 0;

    public static string FormatPlayCount(int playCount, bool allowContinue)
    {
        if (allowContinue && IsContinue(playCount))
        {
            return "continue";
        }

        return Math.Max(1, playCount).ToString();
    }

    public static bool TryParsePlayCount(string text, bool allowContinue, out int playCount)
    {
        playCount = 1;
        string value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (allowContinue &&
            (value.Equals("continue", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("unlimited", StringComparison.OrdinalIgnoreCase) ||
             value == "0"))
        {
            playCount = ContinuePlayCount;
            return true;
        }

        return int.TryParse(value, out playCount) && playCount >= 1;
    }
}
