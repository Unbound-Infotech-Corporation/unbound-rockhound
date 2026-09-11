using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Solar;

/// <summary>
/// User or automated measurement of an object's height vs. cast shadow length
/// on a keyframe. Angles in degrees; lengths in consistent arbitrary units
/// (ratio is what matters for elevation).
/// </summary>
public sealed class ShadowMeasurement
{
    public required Guid Id { get; init; }
    public required Guid AnalysisSessionId { get; init; }
    public required Guid? EvidenceId { get; init; }
    public required string SourceMediaPath { get; init; }
    public TimeSpan? MediaTimestamp { get; init; }
    public int? FrameIndex { get; init; }
    public required DateTimeOffset ObservationUtc { get; init; }

    /// <summary>Object height in arbitrary image/world units.</summary>
    public required double ObjectHeight { get; init; }

    /// <summary>Shadow length in the same units as <see cref="ObjectHeight"/>.</summary>
    public required double ShadowLength { get; init; }

    /// <summary>
    /// Optional shadow azimuth (degrees clockwise from true north).
    /// When present, enables a much tighter locus than elevation alone.
    /// </summary>
    public double? ShadowAzimuthDegrees { get; init; }

    public double? AssumedTerrainSlopeDegrees { get; init; }
    public Confidence MeasurementConfidence { get; init; } = Confidence.Medium;
    public string? Notes { get; init; }

    /// <summary>
    /// Solar elevation angle from height/shadow ratio:
    /// elevation = arctan(height / shadowLength), adjusted for slope if provided.
    /// </summary>
    public double EstimatedSolarElevationDegrees
    {
        get
        {
            if (ShadowLength <= 0 || ObjectHeight <= 0)
                throw new InvalidOperationException("Object height and shadow length must be positive.");

            var elevationRad = Math.Atan(ObjectHeight / ShadowLength);
            var elevationDeg = GeoCoordinate.RadiansToDegrees(elevationRad);

            if (AssumedTerrainSlopeDegrees is { } slope)
                elevationDeg -= slope;

            return elevationDeg;
        }
    }
}
