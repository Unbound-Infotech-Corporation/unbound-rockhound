using System.Diagnostics;
using GeoMineralTrace.Core.App;
using GeoMineralTrace_App.Helpers;
using GeoMineralTrace.Reporting.Kml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Services;

/// <summary>
/// Writes KML/KMZ and launches Google Earth Pro (custom path, auto-detect, or OS .kml association).
/// </summary>
public static class GoogleEarthLauncher
{
    private static readonly string[] DefaultEarthCandidates =
    [
        @"C:\Program Files\Google\Google Earth Pro\client\googleearth.exe",
        @"C:\Program Files (x86)\Google\Google Earth Pro\client\googleearth.exe",
    ];

    public static string ExportsDirectory
    {
        get
        {
            var custom = AppPreferences.GoogleEarthExportsPath;
            if (!string.IsNullOrWhiteSpace(custom))
            {
                Directory.CreateDirectory(custom);
                return custom;
            }

            var dir = AppDataPaths.Sub("Exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string? ResolveGoogleEarthExecutable()
    {
        var saved = AppPreferences.GoogleEarthProPath;
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            return saved;

        foreach (var candidate in DefaultEarthCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static async Task<string> SaveKmlAsync(KmlDocument document, string? fileNameHint = null)
    {
        var safe = SanitizeFileName(fileNameHint ?? document.Name ?? "export");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(ExportsDirectory, $"{safe}-{stamp}.kml");
        await KmlWriter.WriteToFileAsync(document, path).ConfigureAwait(false);

        var kmzPath = Path.Combine(ExportsDirectory, $"{safe}-{stamp}.kmz");
        try
        {
            await KmlCatalogBuilder.WriteKmzAsync(document, kmzPath).ConfigureAwait(false);
        }
        catch
        {
            // KML alone is enough to open.
        }

        return path;
    }

    public static async Task OpenInGoogleEarthAsync(KmlDocument document, XamlRoot xamlRoot, string? fileNameHint = null)
    {
        string path;
        try
        {
            path = await SaveKmlAsync(document, fileNameHint).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowAsync(xamlRoot, "Could not write KML", ex.Message).ConfigureAwait(true);
            return;
        }

        await LaunchKmlAsync(path, xamlRoot).ConfigureAwait(true);
    }

    public static async Task LaunchKmlAsync(string kmlPath, XamlRoot xamlRoot)
    {
        var earthExe = ResolveGoogleEarthExecutable();
        try
        {
            if (!string.IsNullOrWhiteSpace(earthExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = earthExe,
                    Arguments = $"\"{kmlPath}\"",
                    UseShellExecute = false
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = kmlPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowMissingHandlerAsync(xamlRoot, kmlPath, ex.Message).ConfigureAwait(true);
        }
    }

    public static void OpenExportsFolder()
    {
        Directory.CreateDirectory(ExportsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = ExportsDirectory,
            UseShellExecute = true
        });
    }

    public static async Task ShowMissingHandlerAsync(XamlRoot xamlRoot, string savedPath, string? detail = null)
    {
        var earthExe = ResolveGoogleEarthExecutable();
        var content =
            "Could not open the KML in Google Earth.\n\n" +
            $"Your file was saved to:\n{savedPath}\n\n";
        if (earthExe is null)
        {
            content +=
                "Set the path to Google Earth Pro in Settings → Google Earth, or install it from:\n" +
                $"{KmlCatalogBuilder.GoogleEarthDownloadUrl}\n\n" +
                $"You can also import the file in Google Earth Web:\n{KmlCatalogBuilder.GoogleEarthWebUrl}";
        }
        else
        {
            content += $"Configured Earth path:\n{earthExe}";
        }

        if (!string.IsNullOrWhiteSpace(detail))
            content += "\n\nDetail: " + detail;

        await ShowAsync(xamlRoot, "Open in Google Earth", content).ConfigureAwait(true);
    }

    private static async Task ShowAsync(XamlRoot xamlRoot, string title, string content)
    {
        await new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = xamlRoot
        }.ShowAsync();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned.Trim();
    }
}
