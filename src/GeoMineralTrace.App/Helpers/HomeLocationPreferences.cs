using System.Globalization;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace_App.Helpers;

/// <summary>
/// User home / research center and search radius used to scope map and nearby lists.
/// Defaults: 100 miles around a user-set location.
/// </summary>
public static class HomeLocationPreferences
{
    public const string LatitudeKey = "HomeLatitude";
    public const string LongitudeKey = "HomeLongitude";
    public const string LabelKey = "HomeLocationLabel";
    public const string RadiusMilesKey = "SearchRadiusMiles";
    public const string PreferHomeOnMapKey = "PreferHomeLocationOnMap";

    public const double DefaultRadiusMiles = 100;
    public const double MinRadiusMiles = 5;
    public const double MaxRadiusMiles = 500;

    private const double MilesToKm = 1.609344;

    public static double SearchRadiusMiles
    {
        get
        {
            var raw = AppPreferences.ReadSetting(RadiusMilesKey);
            if (string.IsNullOrWhiteSpace(raw)
                || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var miles))
                return DefaultRadiusMiles;

            return Math.Clamp(miles, MinRadiusMiles, MaxRadiusMiles);
        }
        set => AppPreferences.WriteSetting(
            RadiusMilesKey,
            Math.Clamp(value, MinRadiusMiles, MaxRadiusMiles).ToString("0.###", CultureInfo.InvariantCulture));
    }

    public static double SearchRadiusKm => SearchRadiusMiles * MilesToKm;

    /// <summary>When true (default), the map centers catalog layers on the home location when set.</summary>
    public static bool PreferHomeLocationOnMap
    {
        get
        {
            var raw = AppPreferences.ReadSetting(PreferHomeOnMapKey);
            if (string.IsNullOrWhiteSpace(raw))
                return true;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
        set => AppPreferences.WriteSetting(PreferHomeOnMapKey, value ? "true" : "false");
    }

    public static string? HomeLocationLabel
    {
        get
        {
            var value = AppPreferences.ReadSetting(LabelKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        set => AppPreferences.WriteSetting(LabelKey, string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
    }

    public static bool TryGetHomeCoordinate(out GeoCoordinate coordinate)
    {
        coordinate = default;
        var latRaw = AppPreferences.ReadSetting(LatitudeKey);
        var lonRaw = AppPreferences.ReadSetting(LongitudeKey);
        if (string.IsNullOrWhiteSpace(latRaw) || string.IsNullOrWhiteSpace(lonRaw))
            return false;

        if (!double.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return false;

        var c = new GeoCoordinate(lat, lon);
        if (!c.IsValid)
            return false;

        coordinate = c;
        return true;
    }

    public static void SetHomeCoordinate(GeoCoordinate coordinate, string? label = null)
    {
        if (!coordinate.IsValid)
            throw new ArgumentOutOfRangeException(nameof(coordinate), "Latitude/longitude out of range.");

        AppPreferences.WriteSetting(
            LatitudeKey,
            coordinate.LatitudeDegrees.ToString("0.######", CultureInfo.InvariantCulture));
        AppPreferences.WriteSetting(
            LongitudeKey,
            coordinate.LongitudeDegrees.ToString("0.######", CultureInfo.InvariantCulture));

        if (label is not null)
            HomeLocationLabel = label;
    }

    public static void ClearHomeCoordinate()
    {
        AppPreferences.WriteSetting(LatitudeKey, "");
        AppPreferences.WriteSetting(LongitudeKey, "");
        AppPreferences.WriteSetting(LabelKey, "");
    }

    public static string StatusSummary()
    {
        if (!TryGetHomeCoordinate(out var c))
            return $"No home location set — map shows a national sample until you pick one. Default radius {DefaultRadiusMiles:0} mi.";

        var label = HomeLocationLabel;
        var where = string.IsNullOrWhiteSpace(label)
            ? $"{c.LatitudeDegrees:F4}, {c.LongitudeDegrees:F4}"
            : $"{label} ({c.LatitudeDegrees:F4}, {c.LongitudeDegrees:F4})";
        return $"Near {where} · {SearchRadiusMiles:0.#} mi radius"
            + (PreferHomeLocationOnMap ? " · preferred map center" : "");
    }
}
