using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Hydrology.Geo;

/// <summary>Great-circle distance from a point to a polyline (WGS84).</summary>
public static class PolylineDistance
{
    public static double MinDistanceKm(GeoCoordinate point, IReadOnlyList<GeoCoordinate> vertices)
    {
        if (vertices.Count == 0)
            return double.PositiveInfinity;

        if (vertices.Count == 1)
            return HaversineKm(point, vertices[0]);

        var min = double.PositiveInfinity;
        for (var i = 0; i < vertices.Count - 1; i++)
        {
            var d = DistanceToSegmentKm(point, vertices[i], vertices[i + 1]);
            if (d < min)
                min = d;
        }

        return min;
    }

    public static double HaversineKm(GeoCoordinate a, GeoCoordinate b)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = b.LatitudeRadians - a.LatitudeRadians;
        var dLon = b.LongitudeRadians - a.LongitudeRadians;
        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);
        var h = sinLat * sinLat +
                Math.Cos(a.LatitudeRadians) * Math.Cos(b.LatitudeRadians) * sinLon * sinLon;
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    private static double DistanceToSegmentKm(GeoCoordinate p, GeoCoordinate a, GeoCoordinate b)
    {
        if (a.LatitudeDegrees == b.LatitudeDegrees && a.LongitudeDegrees == b.LongitudeDegrees)
            return HaversineKm(p, a);

        // Project in local equirectangular space around segment midpoint.
        var midLat = (a.LatitudeRadians + b.LatitudeRadians) / 2;
        var cosLat = Math.Cos(midLat);

        static (double x, double y) ToPlane(GeoCoordinate c, double cos)
            => (c.LongitudeRadians * cos, c.LatitudeRadians);

        var (px, py) = ToPlane(p, cosLat);
        var (ax, ay) = ToPlane(a, cosLat);
        var (bx, by) = ToPlane(b, cosLat);

        var dx = bx - ax;
        var dy = by - ay;
        var lenSq = dx * dx + dy * dy;
        var t = lenSq < 1e-18 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lenSq, 0, 1);
        var projLon = (ax + t * dx) / cosLat;
        var projLat = ay + t * dy;
        var proj = new GeoCoordinate(
            GeoCoordinate.RadiansToDegrees(projLat),
            GeoCoordinate.RadiansToDegrees(projLon));
        return HaversineKm(p, proj);
    }
}
