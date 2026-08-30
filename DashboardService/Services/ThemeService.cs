using System.IO;
using System.Text.Json;
using System.Windows;

namespace DashboardService.Services;

public static class ThemeService
{
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string DarkThemePath = "Themes/DarkSciFiTheme.xaml";

    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SRPDashboard",
        "theme.json");

    public static bool IsDarkMode { get; private set; }

    public static event Action<bool>? ThemeChanged;

    public static void Initialize()
    {
        IsDarkMode = LoadPreference();
        ApplyTheme(IsDarkMode, raiseEvent: false);
    }

    public static void SetDarkMode(bool enabled)
    {
        if (IsDarkMode == enabled)
        {
            return;
        }

        IsDarkMode = enabled;
        SavePreference(enabled);
        ApplyTheme(enabled, raiseEvent: true);
    }

    public static void Toggle()
    {
        SetDarkMode(!IsDarkMode);
    }

    private static void ApplyTheme(bool dark, bool raiseEvent)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var uri = new Uri(dark ? DarkThemePath : LightThemePath, UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = uri };

        // Keep non-theme dictionaries; replace theme dictionary at index 0.
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(themeDict);
        }
        else
        {
            merged[0] = themeDict;
        }

        if (app.MainWindow != null)
        {
            app.MainWindow.Background =
                (System.Windows.Media.Brush)app.Resources["AppBgBrush"];
        }

        if (raiseEvent)
        {
            ThemeChanged?.Invoke(dark);
        }
    }

    private static bool LoadPreference()
    {
        try
        {
            if (!File.Exists(PreferencePath))
            {
                return false;
            }

            string json = File.ReadAllText(PreferencePath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("darkMode", out var prop) && prop.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static void SavePreference(bool dark)
    {
        try
        {
            string? dir = Path.GetDirectoryName(PreferencePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(
                PreferencePath,
                JsonSerializer.Serialize(new { darkMode = dark }));
        }
        catch
        {
        }
    }
}
