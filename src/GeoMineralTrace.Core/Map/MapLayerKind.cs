namespace GeoMineralTrace.Core.Map;

/// <summary>Toggleable map marker categories (source / provenance).</summary>
public enum MapLayerKind
{
    Hypothesis = 0,
    SolarLocus = 1,
    LocalityCurated = 2,
    LocalityUsgs = 3,
    ClaimActive = 4,
    ClaimExpiring = 5,
    ClaimOther = 6,
    Rumoured = 7,
    PersonalFind = 8,
    River = 9,
    Trail = 10,
    /// <summary>Unverified community mentions from Reddit (not claims/rumoured).</summary>
    CommunityReddit = 11,
    /// <summary>
    /// DEM / LiDAR-derived hillshade imagery overlay (USGS 3DEP / Esri World Hillshade).
    /// Not raw point clouds — shaded relief from elevation models that include airborne LiDAR where collected.
    /// </summary>
    LidarTerrain = 12,
    /// <summary>USGS State Geologic Map Compilation (SGMC) lithology overlay — public WMS.</summary>
    UsgsGeology = 13,
    /// <summary>USGS The National Map elevation contours overlay — public WMS.</summary>
    ElevationContours = 14
}
