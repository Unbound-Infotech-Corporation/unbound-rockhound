using FluentAssertions;
using GeoMineralTrace.Core.Updates;

namespace GeoMineralTrace.Core.Tests;

public class AppVersionComparerTests
{
    [Theory]
    [InlineData("0.5.1", "0.5.0", true)]
    [InlineData("0.5.0", "0.5.0", false)]
    [InlineData("0.4.9", "0.5.0", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("v0.5.2", "0.5.1", true)]
    public void IsNewer_ComparesDottedVersions(string remote, string local, bool expected) =>
        AppVersionComparer.IsNewer(remote, local).Should().Be(expected);

    [Fact]
    public void FormatDisplay_OmitsZeroRevision() =>
        AppVersionComparer.FormatDisplay(new Version(0, 5, 0, 0)).Should().Be("0.5.0");
}
