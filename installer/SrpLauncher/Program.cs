using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SrpLauncher;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main()
    {
        string root = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        string rfidServiceDir = Path.Combine(root, "RfidService");
        string dashboardDir = Path.Combine(root, "DashboardService");

        string rfidServiceExe = Path.Combine(rfidServiceDir, "RfidService.exe");
        string dashboardExe = Path.Combine(dashboardDir, "DashboardService.exe");

        try
        {
            EnsureExists(rfidServiceExe);
            EnsureExists(dashboardExe);

            StartIfNeeded(rfidServiceExe, rfidServiceDir, "RfidService", "--background");

            // Give backend a moment to bind ports / open DB.
            Thread.Sleep(1500);

            StartOrFocus(dashboardExe, dashboardDir, "DashboardService");
        }
        catch (Exception ex)
        {
            MessageBoxNative.Show(
                "Unable to start SRP applications.\n\n" + ex.Message +
                "\n\nInstall folder:\n" + root,
                "SRP Launcher");
        }
    }

    private static void EnsureExists(string exePath)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Missing application:\n{exePath}");
        }
    }

    private static void StartIfNeeded(
        string exePath,
        string workingDirectory,
        string processName,
        string arguments)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Missing application: {exePath}");
        }

        if (Process.GetProcessesByName(processName).Length > 0)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(startInfo);
    }

    private static void StartOrFocus(
        string exePath,
        string workingDirectory,
        string processName)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Missing application: {exePath}");
        }

        var existing = Process.GetProcessesByName(processName)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (existing != null)
        {
            SetForegroundWindow(existing.MainWindowHandle);
            return;
        }

        if (Process.GetProcessesByName(processName).Length > 0)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }
}

internal static class MessageBoxNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string text, string caption)
    {
        MessageBox(IntPtr.Zero, text, caption, 0x10);
    }
}
