using GeoMineralTrace.Core.App;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Persisted user preferences (portable-safe local file).</summary>
public static class AppPreferences
{
    public const string OnlineEnrichmentKey = "OnlineEnrichmentConsent";
    public const string GoogleEarthProPathKey = "GoogleEarthProPath";
    public const string GoogleEarthExportsPathKey = "GoogleEarthExportsPath";
    public const string SupportReportEmailKey = "SupportReportEmail";
    public const string CheckForUpdatesKey = "CheckForUpdates";
    public const string UpdateFeedUrlKey = "UpdateFeedUrl";
    public const string DismissedUpdateVersionKey = "DismissedUpdateVersion";

    private static string SettingsPath => AppDataPaths.Sub("ui-settings.ini");

    public static bool OnlineEnrichmentAllowed
    {
        get => string.Equals(ReadValue(OnlineEnrichmentKey), "true", StringComparison.OrdinalIgnoreCase);
        set => WriteValue(OnlineEnrichmentKey, value ? "true" : "false");
    }

    /// <summary>
    /// When true (default), the app may fetch the public update feed once per launch.
    /// Separate from map-tile Online Enrichment.
    /// </summary>
    public static bool CheckForUpdatesAllowed
    {
        get
        {
            var raw = ReadValue(CheckForUpdatesKey);
            // Default ON so paying customers hear about new builds; they can disable.
            if (string.IsNullOrWhiteSpace(raw))
                return true;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
        set => WriteValue(CheckForUpdatesKey, value ? "true" : "false");
    }

    /// <summary>Override update JSON URL; empty uses the app default feed URL.</summary>
    public static string? UpdateFeedUrl
    {
        get
        {
            var value = ReadValue(UpdateFeedUrlKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => WriteValue(UpdateFeedUrlKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    /// <summary>Remote version the user dismissed (“Remind me later”).</summary>
    public static string? DismissedUpdateVersion
    {
        get
        {
            var value = ReadValue(DismissedUpdateVersionKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => WriteValue(DismissedUpdateVersionKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    /// <summary>Full path to googleearth.exe, or empty to auto-detect / use file association.</summary>
    public static string? GoogleEarthProPath
    {
        get
        {
            var value = ReadValue(GoogleEarthProPathKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => WriteValue(GoogleEarthProPathKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    /// <summary>Optional folder for KML/KMZ exports (defaults to LocalAppData\UnboundRockhound\Exports).</summary>
    public static string? GoogleEarthExportsPath
    {
        get
        {
            var value = ReadValue(GoogleEarthExportsPathKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => WriteValue(GoogleEarthExportsPathKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    /// <summary>Email address pre-filled when user sends a diagnostic report.</summary>
    public static string? SupportReportEmail
    {
        get
        {
            var value = ReadValue(SupportReportEmailKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => WriteValue(SupportReportEmailKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    public static string? ReadSetting(string key) => ReadValue(key);

    public static void WriteSetting(string key, string value) => WriteValue(key, value);

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
            // Ignore and use defaults.
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
            // Best-effort persistence.
        }
    }
}
