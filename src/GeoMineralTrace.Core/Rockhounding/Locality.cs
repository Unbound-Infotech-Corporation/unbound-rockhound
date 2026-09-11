using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Rockhounding;

/// <summary>
/// U.S. gem/mineral collecting locality with access and enrichment metadata.
/// </summary>
public sealed class Locality
{
    public required Guid Id { get; init; }

    /// <summary>Stable external key for upsert (e.g. usgs-mrds:12345).</summary>
    public string? ExternalId { get; init; }

    public required string Name { get; init; }
    public required string StateCode { get; init; }
    public string? County { get; init; }
    public GeoCoordinate? Coordinates { get; init; }
    public IReadOnlyList<string> ReportedMinerals { get; init; } = [];
    public LandType LandType { get; init; } = LandType.Unknown;
    public string? AccessNotes { get; init; }
    public string? TypicalFinds { get; init; }
    public DifficultyLevel Difficulty { get; init; } = DifficultyLevel.Unknown;
    public string? SeasonalityNotes { get; init; }
    public string? CollectingLimits { get; init; }
    public AccessStatus AccessStatus { get; init; } = AccessStatus.Unknown;
    public string? HazardNotes { get; init; }
    public string? NearestServices { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public string? EnrichmentSummary { get; init; }
    public LocalityRating? SystemRating { get; set; }
    public double? UserAverageRating { get; set; }
    public int UserRatingCount { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; init; }

    /// <summary>Dataset label, e.g. "USGS MRDS" or "USGS Critical Minerals".</summary>
    public string? SourceDataset { get; init; }

    /// <summary>Publication / systematic-update vintage (e.g. "2011").</summary>
    public string? SourceVintage { get; init; }

    /// <summary>
    /// When true, oper_status / ownership / access fields must not be treated as current —
    /// surface wherever human-activity status would normally appear.
    /// </summary>
    public bool HumanActivityDataMayBeOutdated { get; init; }
}

public enum LandType
{
    Unknown = 0,
    PublicBlm = 1,
    PublicUsfs = 2,
    StatePark = 3,
    StateLand = 4,
    Private = 5,
    Claim = 6,
    Mixed = 7,
    Other = 99
}

public enum DifficultyLevel
{
    Unknown = 0,
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3,
    Expert = 4
}

public enum AccessStatus
{
    Unknown = 0,
    Open = 1,
    PermitRequired = 2,
    Restricted = 3,
    Closed = 4,
    Seasonal = 5
}
