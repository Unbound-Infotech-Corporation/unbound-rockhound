using GeoMineralTrace.Core.App;
using Microsoft.UI.Xaml;

namespace GeoMineralTrace_App.Helpers;

/// <summary>
/// Theme preferences. Uses a plain file under LocalAppData so unpackaged
/// (portable) builds do not depend on ApplicationData.Current package identity.
/// </summary>
internal static class AppThemeHelper
{
    private const string ThemeKey = "AppTheme";

    private static string SettingsPath => AppDataPaths.Sub("ui-settings.ini");

    public static ApplicationTheme ResolveRequestedTheme()
    {
        return GetSavedThemeTag() switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark
        };
    }

    public static void ApplySavedTheme()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedTheme = ResolveRequestedTheme();
    }

    public static void SaveTheme(string tag)
    {
        WriteValue(ThemeKey, tag);
        if (tag is "Light" or "Dark" && Application.Current is not null)
        {
            Application.Current.RequestedTheme = tag == "Light"
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }
    }

    public static string GetSavedThemeTag() =>
        ReadValue(ThemeKey) ?? "Dark";

    private static string? ReadValue(string key)
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                if (string.Equals(line[..idx].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return line[(idx + 1)..].Trim();
                }
            }
        }
        catch
        {
            // Fall back to default theme.
        }

        return null;
    }

    private static void WriteValue(string key, string value)
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : [];

            var replaced = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var idx = lines[i].IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                if (string.Equals(lines[i][..idx].Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{key}={value}";
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                lines.Add($"{key}={value}");
            }

            File.WriteAllLines(path, lines);
        }
        catch
        {
            // Preference write is best-effort.
        }
    }
}
