using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Hypothesis;

/// <summary>
/// Ranked location or region hypothesis produced by the fusion engine.
/// </summary>
public sealed class LocationHypothesis
{
    public required Guid Id { get; init; }
    public required Guid AnalysisSessionId { get; init; }
    public required string Label { get; init; }
    public GeoCoordinate? Center { get; init; }
    public double? RadiusKm { get; init; }
    public string? RegionDescription { get; init; }
    public required Confidence Confidence { get; init; }
    public int Rank { get; set; }
    public required IReadOnlyList<Guid> SupportingEvidenceIds { get; init; }
    public IReadOnlyList<string>? UncertaintyNotes { get; init; }

    /// <summary>
    /// Deep Analysis corroboration / selection reasoning (inspectable — not just a confidence number).
    /// </summary>
    public IReadOnlyList<string>? Reasoning { get; init; }

    public HypothesisDisposition Disposition { get; set; } = HypothesisDisposition.Open;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum HypothesisDisposition
{
    Open = 0,
    Accepted = 1,
    Rejected = 2,
    Deferred = 3
}
