using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Solar;

/// <summary>
/// Builds an inverse solar locus / relative probability map over a geographic grid.
/// Methodology aligns with ShadowFinder-style elevation matching and optional
/// azimuth constraints; see knowledge/techniques for assumptions and error sources.
/// </summary>
public sealed class SolarLocusEngine
{
    private readonly SolarPositionCalculator _calculator;

    public SolarLocusEngine(SolarPositionCalculator? calculator = null)
    {
        _calculator = calculator ?? new SolarPositionCalculator(applyRefraction: true);
    }

    public SolarLocusResult ComputeLocus(
        Guid analysisSessionId,
        IReadOnlyList<ShadowMeasurement> measurements,
        LocusSearchBounds bounds,
        double gridStepDegrees = 1.0,
        double elevationToleranceDegrees = 1.5,
        double? azimuthToleranceDegrees = 8.0)
    {
        if (measurements.Count == 0)
            throw new ArgumentException("At least one shadow measurement is required.", nameof(measurements));

        // Use mean elevation / observation time across measurements for single-frame locus.
        // Multi-frame trajectory fusion is handled by ComputeTrajectoryLocus.
        var targetElevation = measurements.Average(m => m.EstimatedSolarElevationDegrees);
        var observationUtc = AverageUtc(measurements.Select(m => m.ObservationUtc));
        var hasAzimuth = measurements.All(m => m.ShadowAzimuthDegrees.HasValue);
        double? targetAzimuth = hasAzimuth
            ? CircularMeanDegrees(measurements.Select(m => m.ShadowAzimuthDegrees!.Value))
            : null;

        var cells = new List<LocusCell>();
        double maxWeight = 0;

        for (var lat = bounds.MinLatitude; lat <= bounds.MaxLatitude; lat += gridStepDegrees)
        {
            for (var lon = bounds.MinLongitude; lon <= bounds.MaxLongitude; lon += gridStepDegrees)
            {
                var location = new GeoCoordinate(lat, lon);
                var pos = _calculator.Calculate(location, observationUtc);

                var elevErr = Math.Abs(pos.ElevationDegrees - targetElevation);
                if (elevErr > elevationToleranceDegrees * 3)
                    continue;

                var elevWeight = Gaussian(elevErr, elevationToleranceDegrees);
                double azWeight = 1.0;
                double? az = null;

                if (targetAzimuth is { } tAz && azimuthToleranceDegrees is { } azTol)
                {
                    // Shadow azimuth points away from the sun → sun azimuth ≈ shadow + 180°
                    var impliedSunAzimuth = SolarPositionCalculator.NormalizeDegrees(tAz + 180.0);
                    var azErr = ShadowGeometry.AngularDifferenceDegrees(pos.AzimuthDegrees, impliedSunAzimuth);
                    if (azErr > azTol * 3)
                        continue;
                    azWeight = Gaussian(azErr, azTol);
                    az = pos.AzimuthDegrees;
                }

                var weight = elevWeight * azWeight;
                if (weight < 1e-6)
                    continue;

                cells.Add(new LocusCell(location, weight, pos.ElevationDegrees, az));
                if (weight > maxWeight)
                    maxWeight = weight;
            }
        }

        // Normalize relative probabilities to [0, 1] by peak
        var normalized = cells
            .Select(c => c with { RelativeProbability = maxWeight > 0 ? c.RelativeProbability / maxWeight : 0 })
            .OrderByDescending(c => c.RelativeProbability)
            .ToList();

        var peak = normalized.FirstOrDefault();
        var meanConfidence = measurements.Average(m => m.MeasurementConfidence.Value);
        var locusConfidence = Confidence.From(meanConfidence * (hasAzimuth ? 0.85 : 0.55));

        return new SolarLocusResult
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = analysisSessionId,
            MeasurementIds = measurements.Select(m => m.Id).ToList(),
            ObservationUtc = observationUtc,
            TargetElevationDegrees = targetElevation,
            TargetAzimuthDegrees = targetAzimuth,
            ElevationToleranceDegrees = elevationToleranceDegrees,
            AzimuthToleranceDegrees = azimuthToleranceDegrees,
            Cells = normalized,
            PeakProbabilityCell = peak.RelativeProbability > 0 ? peak.Center : null,
            OverallConfidence = locusConfidence,
            Assumptions =
            [
                "Level ground unless AssumedTerrainSlopeDegrees is set on measurements.",
                "Object is vertical; shadow length measured on the ground plane.",
                "Observation time is known to within the stated elevation tolerance.",
                hasAzimuth
                    ? "Shadow azimuth measured; sun azimuth inferred as shadow + 180°."
                    : "Elevation-only locus: results are bands/arcs, not point fixes."
            ],
            Limitations =
            [
                "Grid resolution and search bounds truncate the true continuous locus.",
                "Atmospheric refraction model is approximate near the horizon.",
                "Lens distortion and perspective in video frames bias length ratios.",
                "Results are probabilistic hypotheses, not definitive locations."
            ]
        };
    }

    /// <summary>
    /// Multi-observation trajectory: intersect loci by multiplying cell weights
    /// across observation times (ShadowFinder / trajectory paper approach).
    /// </summary>
    public SolarLocusResult ComputeTrajectoryLocus(
        Guid analysisSessionId,
        IReadOnlyList<ShadowMeasurement> measurements,
        LocusSearchBounds bounds,
        double gridStepDegrees = 1.0,
        double elevationToleranceDegrees = 1.5)
    {
        if (measurements.Count < 2)
            throw new ArgumentException("Trajectory fusion requires at least two measurements.", nameof(measurements));

        var perObservation = measurements
            .GroupBy(m => m.ObservationUtc.UtcTicks / TimeSpan.TicksPerMinute) // 1-minute bins
            .Select(g => ComputeLocus(
                analysisSessionId,
                g.ToList(),
                bounds,
                gridStepDegrees,
                elevationToleranceDegrees,
                azimuthToleranceDegrees: null))
            .ToList();

        var combined = new Dictionary<(int LatKey, int LonKey), (GeoCoordinate Center, double Weight, double Elev, double? Az)>();
        var stepKey = (int)Math.Round(1.0 / gridStepDegrees);

        foreach (var locus in perObservation)
        {
            foreach (var cell in locus.Cells)
            {
                var key = (
                    (int)Math.Round(cell.Center.LatitudeDegrees * stepKey),
                    (int)Math.Round(cell.Center.LongitudeDegrees * stepKey));

                if (combined.TryGetValue(key, out var existing))
                    combined[key] = (existing.Center, existing.Weight * Math.Max(cell.RelativeProbability, 1e-9),
                        cell.ComputedElevationDegrees, cell.ComputedAzimuthDegrees);
                else
                    combined[key] = (cell.Center, Math.Max(cell.RelativeProbability, 1e-9),
                        cell.ComputedElevationDegrees, cell.ComputedAzimuthDegrees);
            }
        }

        var max = combined.Values.DefaultIfEmpty().Max(v => v.Weight);
        var cells = combined.Values
            .Select(v => new LocusCell(v.Center, max > 0 ? v.Weight / max : 0, v.Elev, v.Az))
            .OrderByDescending(c => c.RelativeProbability)
            .ToList();

        var peak = cells.FirstOrDefault();
        return new SolarLocusResult
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = analysisSessionId,
            MeasurementIds = measurements.Select(m => m.Id).ToList(),
            ObservationUtc = AverageUtc(measurements.Select(m => m.ObservationUtc)),
            TargetElevationDegrees = measurements.Average(m => m.EstimatedSolarElevationDegrees),
            ElevationToleranceDegrees = elevationToleranceDegrees,
            Cells = cells,
            PeakProbabilityCell = peak.RelativeProbability > 0 ? peak.Center : null,
            OverallConfidence = Confidence.From(Math.Min(0.9, 0.4 + 0.15 * perObservation.Count)),
            Assumptions =
            [
                "Same geographic location for all trajectory observations.",
                "Independent elevation matches multiplied for joint probability (naive independence)."
            ],
            Limitations =
            [
                "Camera/object motion between frames violates the fixed-site assumption.",
                "Independence assumption overstates confidence when errors are correlated."
            ]
        };
    }

    /// <summary>
    /// Forward check: does a candidate location reproduce measured elevation
    /// within tolerance at the observation time?
    /// </summary>
    public ForwardVerificationResult Verify(
        GeoCoordinate candidate,
        ShadowMeasurement measurement,
        double elevationToleranceDegrees = 1.5)
    {
        var pos = _calculator.Calculate(candidate, measurement.ObservationUtc);
        var measured = measurement.EstimatedSolarElevationDegrees;
        var err = Math.Abs(pos.ElevationDegrees - measured);
        var pass = err <= elevationToleranceDegrees;

        double? azErr = null;
        if (measurement.ShadowAzimuthDegrees is { } shadowAz)
        {
            var impliedSun = SolarPositionCalculator.NormalizeDegrees(shadowAz + 180.0);
            azErr = ShadowGeometry.AngularDifferenceDegrees(pos.AzimuthDegrees, impliedSun);
        }

        return new ForwardVerificationResult(
            candidate,
            pos,
            measured,
            err,
            azErr,
            pass,
            Confidence.From(pass ? Math.Exp(-0.5 * Math.Pow(err / elevationToleranceDegrees, 2)) : 0.1));
    }

    private static double Gaussian(double error, double sigma)
    {
        if (sigma <= 0) return error == 0 ? 1 : 0;
        return Math.Exp(-0.5 * Math.Pow(error / sigma, 2));
    }

    private static DateTimeOffset AverageUtc(IEnumerable<DateTimeOffset> times)
    {
        var list = times.Select(t => t.ToUniversalTime()).ToList();
        var avgTicks = (long)list.Average(t => t.UtcTicks);
        return new DateTimeOffset(avgTicks, TimeSpan.Zero);
    }

    private static double CircularMeanDegrees(IEnumerable<double> degrees)
    {
        var sin = 0.0;
        var cos = 0.0;
        var n = 0;
        foreach (var d in degrees)
        {
            var r = GeoCoordinate.DegreesToRadians(d);
            sin += Math.Sin(r);
            cos += Math.Cos(r);
            n++;
        }

        if (n == 0) return 0;
        return SolarPositionCalculator.NormalizeDegrees(
            GeoCoordinate.RadiansToDegrees(Math.Atan2(sin / n, cos / n)));
    }
}

public readonly record struct LocusSearchBounds(
    double MinLatitude,
    double MaxLatitude,
    double MinLongitude,
    double MaxLongitude)
{
    public static LocusSearchBounds ContiguousUnitedStates { get; } =
        new(24.5, 49.5, -125.0, -66.5);

    public static LocusSearchBounds World { get; } =
        new(-60, 70, -180, 180);
}

public readonly record struct ForwardVerificationResult(
    GeoCoordinate Candidate,
    SolarPosition Computed,
    double MeasuredElevationDegrees,
    double ElevationErrorDegrees,
    double? AzimuthErrorDegrees,
    bool PassesElevationTolerance,
    Confidence Confidence);
