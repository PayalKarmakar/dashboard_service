using System.Diagnostics;

namespace DashboardService.Helpers;

public static class ExternalLinks
{
    public const string CodeInqUrl = "https://codeinq.com/";

    public static void OpenCodeInq()
    {
        Process.Start(new ProcessStartInfo(CodeInqUrl)
        {
            UseShellExecute = true
        });
    }
}
