using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DashboardService.Services;

namespace DashboardService;

public partial class App : Application
{
    private const string MutexName = "Global\\SRP.DashboardService.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ThemeService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Dispose off the UI thread so StopAsync cannot deadlock the dispatcher.
            Task.Run(() =>
            {
                try
                {
                    CameraBackgroundMonitoringService.Instance.Dispose();
                }
                catch
                {
                    // ignore shutdown errors
                }
            }).Wait(TimeSpan.FromSeconds(4));
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_ownsMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
        }
        catch
        {
            // ignore
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            var existing = Process.GetProcessesByName("DashboardService")
                .FirstOrDefault(p =>
                    p.Id != Environment.ProcessId && p.MainWindowHandle != IntPtr.Zero);

            if (existing != null)
            {
                NativeMethods.ShowWindow(existing.MainWindowHandle, 9); // SW_RESTORE
                NativeMethods.SetForegroundWindow(existing.MainWindowHandle);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
