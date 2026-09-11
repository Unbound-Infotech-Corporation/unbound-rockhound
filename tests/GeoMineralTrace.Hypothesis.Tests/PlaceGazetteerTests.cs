using FluentAssertions;
using GeoMineralTrace.Hypothesis.Fusion;

namespace GeoMineralTrace.Hypothesis.Tests;

public class PlaceGazetteerTests
{
    [Theory]
    [InlineData("Yosemite", 37.865)]
    [InlineData("Maury Mountain", 44.076)]
    [InlineData("Place mention: Sedona", 34.870)]
    [InlineData("44.1, -120.5", 44.1)]
    [InlineData("Yellowstone River", 46.408)]
    [InlineData("Place-like phrase: Yellowstone River", 46.408)]
    public void TryResolve_KnownPlaces(string text, double expectedLat)
    {
        var coord = PlaceGazetteer.TryResolve(text);
        coord.Should().NotBeNull();
        coord!.Value.LatitudeDegrees.Should().BeApproximately(expectedLat, 0.05);
    }

    [Fact]
    public void TryResolve_YellowstoneRiver_IsNotNationalParkCentroid()
    {
        var river = PlaceGazetteer.TryResolve("Yellowstone River");
        var park = PlaceGazetteer.TryResolve("Yellowstone");
        river.Should().NotBeNull();
        park.Should().NotBeNull();
        river!.Value.LatitudeDegrees.Should().BeApproximately(46.408, 0.05);
        park!.Value.LatitudeDegrees.Should().BeApproximately(44.600, 0.05);
        river.Value.LatitudeDegrees.Should().NotBeApproximately(park.Value.LatitudeDegrees, 0.5);
    }

    [Fact]
    public void EntryCount_IsSubstantial()
    {
        PlaceGazetteer.EntryCount.Should().BeGreaterThan(100);
    }

    [Fact]
    public void TryResolve_Unknown_ReturnsNull()
    {
        PlaceGazetteer.TryResolve("zzzz-not-a-real-place-xyz").Should().BeNull();
    }
}
