using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Solar;

namespace GeoMineralTrace.Solar;

/// <summary>
/// Geometric helpers for shadow ↔ solar elevation relationships.
/// </summary>
public static class ShadowGeometry
{
    /// <summary>
    /// Solar elevation from object height and shadow length (same units).
    /// elevation = arctan(h / L). Optional terrain slope is subtracted
    /// (downslope in the shadow direction shortens the apparent shadow).
    /// </summary>
    public static double ElevationFromHeightAndShadow(
        double objectHeight,
        double shadowLength,
        double terrainSlopeDegrees = 0)
    {
        if (objectHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectHeight), "Must be positive.");
        if (shadowLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(shadowLength), "Must be positive.");

        var elevation = GeoCoordinate.RadiansToDegrees(Math.Atan(objectHeight / shadowLength));
        return elevation - terrainSlopeDegrees;
    }

    /// <summary>
    /// Expected shadow length for a known elevation and object height.
    /// Used for forward verification of candidate locations.
    /// </summary>
    public static double ShadowLengthFromElevation(double objectHeight, double elevationDegrees)
    {
        if (objectHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectHeight), "Must be positive.");
        if (elevationDegrees is <= 0 or >= 90)
            throw new ArgumentOutOfRangeException(nameof(elevationDegrees), "Must be in (0, 90).");

        return objectHeight / Math.Tan(GeoCoordinate.DegreesToRadians(elevationDegrees));
    }

    /// <summary>
    /// Smallest absolute angular difference on a circle (degrees).
    /// </summary>
    public static double AngularDifferenceDegrees(double a, double b)
    {
        var d = Math.Abs(SolarPositionCalculator.NormalizeDegrees(a) - SolarPositionCalculator.NormalizeDegrees(b));
        return d > 180 ? 360 - d : d;
    }

    public static double ElevationFromMeasurement(ShadowMeasurement measurement) =>
        measurement.EstimatedSolarElevationDegrees;
}
