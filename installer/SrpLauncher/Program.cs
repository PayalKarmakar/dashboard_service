using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SrpLauncher;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main(string[] args)
    {
        bool backendsOnly = args.Any(a =>
            string.Equals(a, "--backends-only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "/backends-only", StringComparison.OrdinalIgnoreCase));

        string root = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        try
        {
            StartBackends(root);

            if (backendsOnly)
            {
                return;
            }

            Thread.Sleep(1500);

            string dashboardDir = Path.Combine(root, "DashboardService");
            string dashboardExe = Path.Combine(dashboardDir, "DashboardService.exe");
            EnsureExists(dashboardExe);
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

    private static void StartBackends(string root)
    {
        string rfidDir = Path.Combine(root, "RfidService");
        string sensorDir = Path.Combine(root, "SensorService");
        string cameraDir = Path.Combine(root, "CameraService");

        string rfidExe = Path.Combine(rfidDir, "RfidService.exe");
        string sensorExe = Path.Combine(sensorDir, "SensorService.exe");
        string cameraCmd = Path.Combine(cameraDir, "start-camera-service.cmd");

        EnsureExists(rfidExe);

        StartIfNeeded(
            rfidExe,
            rfidDir,
            "RfidService",
            "--background",
            new Dictionary<string, string>
            {
                ["ASPNETCORE_URLS"] = "http://localhost:64312",
                ["ASPNETCORE_ENVIRONMENT"] = "Production"
            });

        if (File.Exists(sensorExe))
        {
            StartIfNeeded(sensorExe, sensorDir, "SensorService", string.Empty);
        }

        if (File.Exists(cameraCmd))
        {
            StartCameraIfNeeded(cameraCmd, cameraDir);
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
        string arguments,
        Dictionary<string, string>? environment = null)
    {
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

        if (environment != null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        Process.Start(startInfo);
    }

    private static void StartCameraIfNeeded(string cameraCmd, string workingDirectory)
    {
        if (IsPortOpen(64316))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cameraCmd,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            // Camera is optional if Python is not installed.
        }
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartOrFocus(
        string exePath,
        string workingDirectory,
        string processName)
    {
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
