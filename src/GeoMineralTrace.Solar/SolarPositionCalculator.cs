using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Solar;

/// <summary>
/// Solar topocentric position at a given UTC instant and geographic location.
/// Angles in degrees. Elevation is geometric (no refraction) unless noted.
/// </summary>
public readonly record struct SolarPosition(
    DateTimeOffset Utc,
    GeoCoordinate Location,
    double ElevationDegrees,
    double ZenithDegrees,
    double AzimuthDegrees,
    double DeclinationDegrees,
    double EquationOfTimeMinutes,
    bool IncludesRefraction)
{
    public bool IsAboveHorizon => ElevationDegrees > 0;
}

/// <summary>
/// High-accuracy solar position calculator based on NOAA Solar Calculator
/// algorithms (derived from Astronomical Algorithms / Jean Meeus), suitable
/// for shadow-forensic elevation/azimuth work. Typical angular error is well
/// under 0.1° for modern dates; see knowledge/techniques for full SPA notes
/// and when to prefer NREL SPA (Reda &amp; Andreas).
/// </summary>
public sealed class SolarPositionCalculator
{
    private readonly bool _applyRefraction;

    public SolarPositionCalculator(bool applyRefraction = true)
    {
        _applyRefraction = applyRefraction;
    }

    public SolarPosition Calculate(GeoCoordinate location, DateTimeOffset utc)
    {
        if (!location.IsValid)
            throw new ArgumentException("Location coordinates are out of range.", nameof(location));

        var utcTime = utc.ToUniversalTime();
        var jd = ToJulianDay(utcTime);
        var jc = (jd - 2451545.0) / 36525.0;

        // Geometric mean longitude of the Sun (degrees)
        var l0 = NormalizeDegrees(280.46646 + jc * (36000.76983 + 0.0003032 * jc));

        // Geometric mean anomaly (degrees)
        var m = 357.52911 + jc * (35999.05029 - 0.0001537 * jc);
        var mRad = GeoCoordinate.DegreesToRadians(m);

        // Eccentricity of Earth's orbit
        var e = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);

        // Equation of center
        var c = Math.Sin(mRad) * (1.914602 - jc * (0.004817 + 0.000014 * jc))
              + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * jc)
              + Math.Sin(3 * mRad) * 0.000289;

        var sunTrueLong = l0 + c;
        var sunTrueAnom = m + c;
        var sunRadVector = (1.000001018 * (1 - e * e)) /
                           (1 + e * Math.Cos(GeoCoordinate.DegreesToRadians(sunTrueAnom)));

        // Apparent longitude (aberration + nutation approximation)
        var omega = 125.04 - 1934.136 * jc;
        var lambda = sunTrueLong - 0.00569 - 0.00478 * Math.Sin(GeoCoordinate.DegreesToRadians(omega));

        // Mean obliquity of the ecliptic + correction
        var seconds = 21.448 - jc * (46.8150 + jc * (0.00059 - jc * 0.001813));
        var epsilon0 = 23.0 + (26.0 + seconds / 60.0) / 60.0;
        var epsilon = epsilon0 + 0.00256 * Math.Cos(GeoCoordinate.DegreesToRadians(omega));
        var epsilonRad = GeoCoordinate.DegreesToRadians(epsilon);
        var lambdaRad = GeoCoordinate.DegreesToRadians(lambda);

        // Solar declination
        var sinDec = Math.Sin(epsilonRad) * Math.Sin(lambdaRad);
        var declination = GeoCoordinate.RadiansToDegrees(Math.Asin(sinDec));

        // Equation of time (minutes)
        var y = Math.Tan(epsilonRad / 2.0);
        y *= y;
        var l0Rad = GeoCoordinate.DegreesToRadians(l0);
        var eqTime = 4.0 * GeoCoordinate.RadiansToDegrees(
            y * Math.Sin(2 * l0Rad)
            - 2 * e * Math.Sin(mRad)
            + 4 * e * y * Math.Sin(mRad) * Math.Cos(2 * l0Rad)
            - 0.5 * y * y * Math.Sin(4 * l0Rad)
            - 1.25 * e * e * Math.Sin(2 * mRad));

        var minutes = utcTime.Hour * 60.0 + utcTime.Minute + utcTime.Second / 60.0
                      + utcTime.Millisecond / 60000.0;
        var trueSolarTime = (minutes + eqTime + 4.0 * location.LongitudeDegrees) % 1440.0;
        if (trueSolarTime < 0)
            trueSolarTime += 1440.0;

        var hourAngle = trueSolarTime / 4.0 - 180.0;
        if (hourAngle < -180)
            hourAngle += 360.0;

        var latRad = location.LatitudeRadians;
        var decRad = GeoCoordinate.DegreesToRadians(declination);
        var haRad = GeoCoordinate.DegreesToRadians(hourAngle);

        var cosZenith = Math.Sin(latRad) * Math.Sin(decRad)
                        + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(haRad);
        cosZenith = Math.Clamp(cosZenith, -1.0, 1.0);
        var zenith = GeoCoordinate.RadiansToDegrees(Math.Acos(cosZenith));

        // Azimuth (degrees clockwise from north) — NOAA Solar Calculator formulation
        var zenithRad = GeoCoordinate.DegreesToRadians(zenith);
        double azimuth;
        var azDenom = Math.Cos(latRad) * Math.Sin(zenithRad);
        if (Math.Abs(azDenom) > 0.001)
        {
            var cosAz = ((Math.Sin(latRad) * Math.Cos(zenithRad)) - Math.Sin(decRad)) / azDenom;
            cosAz = Math.Clamp(cosAz, -1.0, 1.0);
            var azRad = Math.PI - Math.Acos(cosAz);
            if (hourAngle > 0)
                azRad = -azRad;
            azimuth = NormalizeDegrees(GeoCoordinate.RadiansToDegrees(azRad));
        }
        else
        {
            azimuth = latRad > 0 ? 180.0 : 0.0;
        }

        var elevation = 90.0 - zenith;
        var includesRefraction = false;

        if (_applyRefraction && elevation > -0.575)
        {
            // Approximate atmospheric refraction (NOAA), degrees
            var te = elevation;
            var refraction = te > 85
                ? 0.0
                : te > 5
                    ? 58.1 / Math.Tan(GeoCoordinate.DegreesToRadians(te))
                      - 0.07 / Math.Pow(Math.Tan(GeoCoordinate.DegreesToRadians(te)), 3)
                      + 0.000086 / Math.Pow(Math.Tan(GeoCoordinate.DegreesToRadians(te)), 5)
                    : te > -0.575
                        ? 1735.0 + te * (-518.2 + te * (103.4 + te * (-12.79 + te * 0.711)))
                        : -20.774 / Math.Tan(GeoCoordinate.DegreesToRadians(te));

            elevation += refraction / 3600.0;
            zenith = 90.0 - elevation;
            includesRefraction = true;
        }

        // Suppress unused warning — retained for documentation / future SPA parity checks
        _ = sunRadVector;

        return new SolarPosition(
            utcTime,
            location,
            elevation,
            zenith,
            NormalizeDegrees(azimuth),
            declination,
            eqTime,
            includesRefraction);
    }

    /// <summary>
    /// Julian Day for a UTC DateTimeOffset (fractional day).
    /// </summary>
    public static double ToJulianDay(DateTimeOffset utc)
    {
        utc = utc.ToUniversalTime();
        var y = utc.Year;
        var m = utc.Month;
        var d = utc.Day
                + (utc.Hour + (utc.Minute + (utc.Second + utc.Millisecond / 1000.0) / 60.0) / 60.0) / 24.0;

        if (m <= 2)
        {
            y -= 1;
            m += 12;
        }

        var a = Math.Floor(y / 100.0);
        var b = 2 - a + Math.Floor(a / 4.0);
        return Math.Floor(365.25 * (y + 4716))
               + Math.Floor(30.6001 * (m + 1))
               + d + b - 1524.5;
    }

    public static double NormalizeDegrees(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }
}
