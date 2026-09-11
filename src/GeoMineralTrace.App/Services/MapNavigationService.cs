using System.Diagnostics;
using System.Globalization;
using GeoMineralTrace.Reporting.Kml;
using GeoMineralTrace_App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Services;

/// <summary>Opens Google Earth and Google Maps for map pins (external apps / browser).</summary>
public static class MapNavigationService
{
    public static async Task OpenInGoogleEarthAsync(
        double latitude,
        double longitude,
        string label,
        XamlRoot xamlRoot)
    {
        var doc = new KmlDocument
        {
            Name = label
        };
        var folder = new KmlFolder { Name = "Selected location", Visible = true };
        folder.Placemarks.Add(new KmlPlacemark
        {
            Name = label,
            Latitude = latitude,
            Longitude = longitude,
            DescriptionHtml = $"<p>{System.Net.WebUtility.HtmlEncode(label)}</p>",
            StyleId = "default"
        });
        doc.Folders.Add(folder);
        await GoogleEarthLauncher.OpenInGoogleEarthAsync(doc, xamlRoot, "map-pin").ConfigureAwait(true);
    }

    public static void OpenGoogleMapsLocation(double latitude, double longitude, string? label = null)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        var url = string.IsNullOrWhiteSpace(label)
            ? $"https://www.google.com/maps/search/?api=1&query={lat},{lon}"
            : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(label)}@{lat},{lon}";
        LaunchUrl(url);
    }

    public static void OpenGoogleMapsDirections(double latitude, double longitude)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=driving";
        LaunchUrl(url);
    }

    private static void LaunchUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
