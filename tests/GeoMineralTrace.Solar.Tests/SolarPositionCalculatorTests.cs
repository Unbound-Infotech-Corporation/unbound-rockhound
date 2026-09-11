using FluentAssertions;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Solar;

namespace GeoMineralTrace.Solar.Tests;

public class SolarPositionCalculatorTests
{
    private readonly SolarPositionCalculator _calc = new(applyRefraction: true);

    [Fact]
    public void Equinox_Noon_AtEquator_SunIsNearZenith()
    {
        // Approximate: 2024-03-20 12:00 UTC at 0°N, 0°E — near equinox, local solar noon
        var location = new GeoCoordinate(0, 0);
        var utc = new DateTimeOffset(2024, 3, 20, 12, 0, 0, TimeSpan.Zero);
        var pos = _calc.Calculate(location, utc);

        pos.ElevationDegrees.Should().BeGreaterThan(85);
        pos.ElevationDegrees.Should().BeLessThanOrEqualTo(90.5);
    }

    [Fact]
    public void NorthernSummer_Afternoon_AzimuthIsWesterly()
    {
        // New York City, 2024-06-21 18:00 UTC ≈ 14:00 EDT — sun in SW sky
        var nyc = new GeoCoordinate(40.7128, -74.0060);
        var utc = new DateTimeOffset(2024, 6, 21, 18, 0, 0, TimeSpan.Zero);
        var pos = _calc.Calculate(nyc, utc);

        pos.IsAboveHorizon.Should().BeTrue();
        pos.ElevationDegrees.Should().BeInRange(40, 75);
        // Afternoon: azimuth roughly between south and west (180–270)
        pos.AzimuthDegrees.Should().BeInRange(180, 280);
    }

    [Fact]
    public void Night_InWinter_HighLatitude_BelowHorizon()
    {
        var anchorage = new GeoCoordinate(61.2181, -149.9003);
        var utc = new DateTimeOffset(2024, 12, 21, 10, 0, 0, TimeSpan.Zero); // ~01:00 AKST
        var pos = _calc.Calculate(anchorage, utc);

        pos.ElevationDegrees.Should().BeLessThan(0);
    }

    [Fact]
    public void JulianDay_J2000_IsCorrect()
    {
        // J2000.0 epoch: 2000-01-01 12:00 TT ≈ 2000-01-01 12:00 UTC for this purpose
        var jd = SolarPositionCalculator.ToJulianDay(
            new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero));
        jd.Should().BeApproximately(2451545.0, 0.001);
    }

    [Fact]
    public void InvalidCoordinate_Throws()
    {
        var bad = new GeoCoordinate(100, 0);
        var act = () => _calc.Calculate(bad, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }
}
