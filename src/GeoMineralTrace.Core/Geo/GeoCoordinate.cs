namespace GeoMineralTrace.Core.Geo;

/// <summary>
/// WGS84 geographic coordinate. Latitude/longitude in decimal degrees;
/// altitude in meters above mean sea level when known.
/// </summary>
public readonly record struct GeoCoordinate(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double? AltitudeMeters = null)
{
    public bool IsValid =>
        LatitudeDegrees is >= -90 and <= 90 &&
        LongitudeDegrees is >= -180 and <= 180;

    public double LatitudeRadians => DegreesToRadians(LatitudeDegrees);
    public double LongitudeRadians => DegreesToRadians(LongitudeDegrees);

    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    public override string ToString() =>
        AltitudeMeters is { } alt
            ? $"{LatitudeDegrees:F6}, {LongitudeDegrees:F6} ({alt:F1} m)"
            : $"{LatitudeDegrees:F6}, {LongitudeDegrees:F6}";
}
