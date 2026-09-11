using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Solar;

/// <summary>
/// Result of inverse solar locus computation: geographic cells consistent
/// with measured solar elevation (and optional azimuth) at a given time.
/// </summary>
public sealed class SolarLocusResult
{
    public required Guid Id { get; init; }
    public required Guid AnalysisSessionId { get; init; }
    public required IReadOnlyList<Guid> MeasurementIds { get; init; }
    public required DateTimeOffset ObservationUtc { get; init; }
    public required double TargetElevationDegrees { get; init; }
    public double? TargetAzimuthDegrees { get; init; }
    public required double ElevationToleranceDegrees { get; init; }
    public double? AzimuthToleranceDegrees { get; init; }
    public required IReadOnlyList<LocusCell> Cells { get; init; }
    public GeoCoordinate? PeakProbabilityCell { get; init; }
    public Confidence OverallConfidence { get; init; } = Confidence.Medium;
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

/// <summary>
/// Discrete geographic cell with relative probability mass for the locus.
/// </summary>
public readonly record struct LocusCell(
    GeoCoordinate Center,
    double RelativeProbability,
    double ComputedElevationDegrees,
    double? ComputedAzimuthDegrees);
