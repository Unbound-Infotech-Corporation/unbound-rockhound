using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Trails;

/// <summary>Named hiking / access trail segment useful for rockhounding approach.</summary>
public sealed class Trail
{
    public required Guid Id { get; init; }

    /// <summary>Stable external key for upsert (e.g. seed:crystal-park-co).</summary>
    public string? ExternalId { get; init; }

    public required string Name { get; init; }
    public required string StateCode { get; init; }
    public string? County { get; init; }
    public TrailKind Kind { get; init; } = TrailKind.Hiking;
    public double? LengthKm { get; init; }
    public IReadOnlyList<GeoCoordinate> Vertices { get; init; } = [];
    public GeoCoordinate? Centroid { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public string? SourceDataset { get; init; }
    public string? Notes { get; init; }
}

public enum TrailKind
{
    Unknown = 0,
    Hiking = 1,
    AccessRoad = 2,
    OHV = 3,
    Pack = 4,
    Other = 99
}
