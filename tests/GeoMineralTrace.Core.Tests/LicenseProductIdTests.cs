using FluentAssertions;
using GeoMineralTrace.Core.Licensing;

namespace GeoMineralTrace.Core.Tests;

public class LicenseProductIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unbound-rockhound")]
    [InlineData("Unbound Rockhound")]
    [InlineData("unbound_rockhound")]
    [InlineData("UNBOUND-ROCKHOUND")]
    [InlineData("rockhound")]
    public void IsRockhound_AcceptsAliasesAndMissingMetadata(string? raw) =>
        LicenseProductId.IsRockhound(raw).Should().BeTrue();

    [Theory]
    [InlineData("heirloom")]
    [InlineData("unbound-other")]
    [InlineData("something-else")]
    public void IsRockhound_RejectsOtherProducts(string raw) =>
        LicenseProductId.IsRockhound(raw).Should().BeFalse();

    [Fact]
    public void Normalize_CollapsesSeparatorsToCanonical() =>
        LicenseProductId.Normalize("Unbound Rockhound").Should().Be(LicenseProductId.Canonical);
}
