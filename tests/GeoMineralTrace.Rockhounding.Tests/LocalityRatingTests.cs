using FluentAssertions;
using GeoMineralTrace.Core.Rockhounding;
using GeoMineralTrace.Rockhounding.Scoring;

namespace GeoMineralTrace.Rockhounding.Tests;

public class LocalityRatingTests
{
    [Fact]
    public void Overall_IsWeightedMean_InRange()
    {
        var rating = LocalityRatingCalculator.Create(8, 7, 9, 6, 8, 5, safety: 7);
        rating.Overall.Should().BeInRange(0, 10);
        rating.Breakdown["Overall"].Should().Be(rating.Overall);
    }

    [Fact]
    public void ClosedSite_HasVeryLowLegalClarity_PullsOverallDown()
    {
        var open = LocalityRatingCalculator.Create(8, 8, LocalityRatingCalculator.LegalClarityFromAccess(AccessStatus.Open), 7, 8, 6);
        var closed = LocalityRatingCalculator.Create(8, 8, LocalityRatingCalculator.LegalClarityFromAccess(AccessStatus.Closed), 7, 8, 6);
        closed.LegalClarity.Should().BeLessThan(1);
        closed.Overall.Should().BeLessThan(open.Overall);
    }
}
