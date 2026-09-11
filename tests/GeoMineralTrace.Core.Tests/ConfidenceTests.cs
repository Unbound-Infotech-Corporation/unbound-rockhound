using FluentAssertions;
using GeoMineralTrace.Core.Common;

namespace GeoMineralTrace.Core.Tests;

public class ConfidenceTests
{
    [Fact]
    public void Clamps_ToUnitInterval()
    {
        new Confidence(1.5).Value.Should().Be(1.0);
        new Confidence(-0.2).Value.Should().Be(0.0);
    }

    [Fact]
    public void CombineIndependent_IncreasesBelief()
    {
        var a = Confidence.Medium;
        var b = Confidence.Medium;
        a.CombineIndependent(b).Value.Should().BeGreaterThan(a.Value);
    }
}
