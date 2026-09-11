using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Rockhounding;

/// <summary>
/// Unverified collecting or mining lead from forums, blogs, message boards, or social posts.
/// Always treat as rumoured — verify access and land status in the field.
/// </summary>
public sealed class RumouredSite
{
    public required Guid Id { get; init; }

    /// <summary>Stable import key, e.g. rumour:forum:abc123.</summary>
    public string? ExternalId { get; init; }

    public required string Name { get; init; }
    public required string StateCode { get; init; }
    public GeoCoordinate? Coordinates { get; init; }
    public IReadOnlyList<string> ReportedMinerals { get; init; } = [];

    public RumourSourceType SourceType { get; init; } = RumourSourceType.Unknown;
    public string? SourceUrl { get; init; }
    public string? SourceLabel { get; init; }
    public string? Notes { get; init; }

    /// <summary>0–1 heuristic confidence from import metadata (not field verified).</summary>
    public double Confidence { get; init; }

    public DateTimeOffset? ImportedUtc { get; init; }
}

public enum RumourSourceType
{
    Unknown = 0,
    Forum = 1,
    Blog = 2,
    MessageBoard = 3,
    Social = 4,
    VideoComment = 5,
    FieldReport = 6,
    Other = 99
}
