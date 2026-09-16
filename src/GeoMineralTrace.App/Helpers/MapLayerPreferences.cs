using GeoMineralTrace.Core.Map;

namespace GeoMineralTrace_App.Helpers;

/// <summary>Persisted map layer visibility toggles.</summary>
public static class MapLayerPreferences
{
    private static readonly Dictionary<MapLayerKind, string> Keys = new()
    {
        [MapLayerKind.Hypothesis] = "MapLayerHypothesis",
        [MapLayerKind.SolarLocus] = "MapLayerSolarLocus",
        [MapLayerKind.LocalityCurated] = "MapLayerLocalityCurated",
        [MapLayerKind.LocalityUsgs] = "MapLayerLocalityUsgs",
        [MapLayerKind.ClaimActive] = "MapLayerClaimActive",
        [MapLayerKind.ClaimExpiring] = "MapLayerClaimExpiring",
        [MapLayerKind.ClaimOther] = "MapLayerClaimOther",
        [MapLayerKind.Rumoured] = "MapLayerRumoured",
        [MapLayerKind.PersonalFind] = "MapLayerPersonalFind",
        [MapLayerKind.River] = "MapLayerRivers",
        [MapLayerKind.Trail] = "MapLayerTrails",
        [MapLayerKind.CommunityReddit] = "MapLayerCommunityReddit",
        // Default off — user enables to blend terrain relief over satellite/street.
        [MapLayerKind.LidarTerrain] = "MapLayerLidarTerrain",
        [MapLayerKind.UsgsGeology] = "MapLayerUsgsGeology",
        [MapLayerKind.ElevationContours] = "MapLayerElevationContours",
        [MapLayerKind.CooperativeNationalGeology] = "MapLayerCooperativeNationalGeology"
    };

    public const string LidarOpacityKey = "MapLayerLidarOpacity";
    public const double DefaultLidarOpacity = 0.55;

    public static bool IsVisible(MapLayerKind kind)
    {
        if (!Keys.TryGetValue(kind, out var key))
            return true;

        var raw = AppPreferences.ReadSetting(key);
        if (kind is MapLayerKind.LidarTerrain
            or MapLayerKind.UsgsGeology
            or MapLayerKind.ElevationContours
            or MapLayerKind.CooperativeNationalGeology)
        {
            // Explicit opt-in for online terrain / geology overlays (default off).
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetVisible(MapLayerKind kind, bool visible)
    {
        if (Keys.TryGetValue(kind, out var key))
            AppPreferences.WriteSetting(key, visible ? "true" : "false");
    }

    /// <summary>Hillshade overlay opacity in 0.15–0.80. Default 0.55.</summary>
    public static double LidarOpacity
    {
        get
        {
            var raw = AppPreferences.ReadSetting(LidarOpacityKey);
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return Math.Clamp(value, 0.15, 0.80);
            }

            return DefaultLidarOpacity;
        }
        set => AppPreferences.WriteSetting(
            LidarOpacityKey,
            Math.Clamp(value, 0.15, 0.80).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static bool MatchesMarkerKind(string markerKind)
    {
        var layer = MarkerKindToLayer(markerKind);
        return layer is null || IsVisible(layer.Value);
    }

    public static MapLayerKind? MarkerKindToLayer(string markerKind) => markerKind switch
    {
        "hypothesis" or "hypothesis_lead" or "hypothesis_runner" => MapLayerKind.Hypothesis,
        "locus" or "peak" => MapLayerKind.SolarLocus,
        "locality_curated" => MapLayerKind.LocalityCurated,
        "locality_usgs" => MapLayerKind.LocalityUsgs,
        "locality" => MapLayerKind.LocalityCurated,
        "claim_active" => MapLayerKind.ClaimActive,
        "claim_expiring" => MapLayerKind.ClaimExpiring,
        "claim_other" or "claim" => MapLayerKind.ClaimOther,
        "rumoured" => MapLayerKind.Rumoured,
        "personal_find" => MapLayerKind.PersonalFind,
        "river" => MapLayerKind.River,
        "trail" => MapLayerKind.Trail,
        "community_reddit" => MapLayerKind.CommunityReddit,
        _ => null
    };
}
