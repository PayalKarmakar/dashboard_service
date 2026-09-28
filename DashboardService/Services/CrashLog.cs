using System.IO;

namespace DashboardService.Services;

/// <summary>
/// Appends unexpected errors to %LOCALAPPDATA%\SRP Innovations\logs\dashboard-errors.log
/// (Program Files is not writable for normal users).
/// </summary>
public static class CrashLog
{
    private static readonly object Sync = new();

    public static string LogFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SRP Innovations",
        "logs",
        "dashboard-errors.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(
                    LogFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
