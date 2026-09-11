using FluentAssertions;
using GeoMineralTrace.Core.Common;
using GeoMineralTrace.Core.Geo;
using GeoMineralTrace.Core.Solar;
using GeoMineralTrace.Solar;

namespace GeoMineralTrace.Solar.Tests;

public class ShadowGeometryAndLocusTests
{
    [Theory]
    [InlineData(1.0, 1.0, 45.0)]
    [InlineData(1.0, 1.73205080757, 30.0)]
    [InlineData(1.73205080757, 1.0, 60.0)]
    public void ElevationFromHeightAndShadow_MatchesArctan(double h, double l, double expected)
    {
        var elev = ShadowGeometry.ElevationFromHeightAndShadow(h, l);
        elev.Should().BeApproximately(expected, 0.05);
    }

    [Fact]
    public void ShadowLengthFromElevation_RoundTrips()
    {
        const double h = 2.0;
        const double elev = 35.0;
        var length = ShadowGeometry.ShadowLengthFromElevation(h, elev);
        ShadowGeometry.ElevationFromHeightAndShadow(h, length).Should().BeApproximately(elev, 0.01);
    }

    [Fact]
    public void Locus_FindsCellsNearKnownLocation_WhenElevationMatches()
    {
        var calc = new SolarPositionCalculator();
        var engine = new SolarLocusEngine(calc);
        var denver = new GeoCoordinate(39.7392, -104.9903);
        var utc = new DateTimeOffset(2024, 6, 21, 19, 0, 0, TimeSpan.Zero);
        var truth = calc.Calculate(denver, utc);

        // Fabricate a measurement that matches Denver's true elevation
        var ratio = Math.Tan(GeoCoordinate.DegreesToRadians(truth.ElevationDegrees));
        var measurement = new ShadowMeasurement
        {
            Id = Guid.NewGuid(),
            AnalysisSessionId = Guid.NewGuid(),
            EvidenceId = null,
            SourceMediaPath = "test.mp4",
            ObservationUtc = utc,
            ObjectHeight = ratio,
            ShadowLength = 1.0,
            MeasurementConfidence = Confidence.High
        };

        var sessionId = measurement.AnalysisSessionId;
        var locus = engine.ComputeLocus(
            sessionId,
            [measurement],
            new LocusSearchBounds(35, 45, -110, -95),
            gridStepDegrees: 1.0,
            elevationToleranceDegrees: 1.0);

        locus.Cells.Should().NotBeEmpty();
        locus.PeakProbabilityCell.Should().NotBeNull();

        // Peak should be somewhere in the Colorado band (elevation-only → elongated)
        var peak = locus.PeakProbabilityCell!.Value;
        peak.LatitudeDegrees.Should().BeInRange(35, 45);

        var verify = engine.Verify(denver, measurement, elevationToleranceDegrees: 1.0);
        verify.PassesElevationTolerance.Should().BeTrue();
        verify.ElevationErrorDegrees.Should().BeLessThan(0.5);
    }

    [Fact]
    public void TechniquesKnowledgeBase_HasCoreArticles()
    {
        var kb = new Knowledge.TechniquesKnowledgeBase();
        kb.All.Should().NotBeEmpty();
        kb.Search("ShadowFinder").Should().NotBeEmpty();
        kb.GetById("shadow-elevation-basics").Should().NotBeNull();
    }
}
