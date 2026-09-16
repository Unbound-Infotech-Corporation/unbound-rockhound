namespace GeoMineralTrace.Core.Geology;

/// <summary>
/// Cooperative National Geologic Map (NGMDB) horizons shown in the National Geology viewer.
/// </summary>
public enum CngmTheme
{
    EarthSurface = 0,
    Quaternary = 1,
    PreQuaternary = 2,
    Precambrian = 3
}

/// <summary>
/// Overlay coloring that matches the National Geology viewer symbology radios.
/// </summary>
public enum CngmSymbology
{
    NationalSynthesis = 0,
    GeoMaterial = 1,
    Age = 2,
    SourceGeology = 3
}
