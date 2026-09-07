using System.Windows;
using DashboardService.Services;

namespace DashboardService;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CameraBackgroundMonitoringService.Instance.Dispose();
        base.OnExit(e);
    }
}
