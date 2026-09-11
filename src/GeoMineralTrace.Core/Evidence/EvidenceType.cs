namespace GeoMineralTrace.Core.Evidence;

/// <summary>
/// Taxonomy of evidence extracted from video and related analysis.
/// </summary>
public enum EvidenceType
{
    Metadata = 0,
    GpsEmbed = 1,
    Keyframe = 2,
    OcrText = 3,
    Landmark = 4,
    Architecture = 5,
    Vegetation = 6,
    Terrain = 7,
    SoilRock = 8,
    Cultural = 9,
    MiningCue = 10,
    AudioTranscript = 11,
    PlaceNameMention = 12,
    MineralNameMention = 13,
    BackgroundSound = 14,
    ShadowMeasurement = 15,
    SolarLocus = 16,
    LanguageDialect = 17,
    LicensePlate = 18,
    UserAnnotation = 19,
    Other = 99
}
