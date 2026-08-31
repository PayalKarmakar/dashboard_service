namespace DashboardService.Models;

public static class ReportDurationFormatter
{
    public static string Format(TimeSpan span)
    {
        if (span.TotalSeconds < 0)
        {
            return "00:00:00";
        }

        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
