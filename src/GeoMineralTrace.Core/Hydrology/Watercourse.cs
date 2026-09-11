using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Hydrology;

/// <summary>Named river, creek, or stream segment (USGS NHD / curated).</summary>
public sealed class Watercourse
{
    public required Guid Id { get; init; }

    /// <summary>Stable external key for upsert (e.g. nhd:12345, seed:arkansas-river-co).</summary>
    public string? ExternalId { get; init; }

    public required string Name { get; init; }
    public required string StateCode { get; init; }
    public string? County { get; init; }
    public WatercourseKind Kind { get; init; } = WatercourseKind.Unknown;
    public string? GnisId { get; init; }
    public double? LengthKm { get; init; }
    public IReadOnlyList<GeoCoordinate> Vertices { get; init; } = [];
    public GeoCoordinate? Centroid { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public string? SourceDataset { get; init; }
    public string? SourceVintage { get; init; }
    public IReadOnlyList<RiverMineralAssociation> Minerals { get; init; } = [];
}

public enum WatercourseKind
{
    Unknown = 0,
    River = 1,
    Creek = 2,
    Stream = 3,
    Canal = 4,
    Other = 99
}
