using FluentAssertions;
using GeoMineralTrace.Claims.Import;

namespace GeoMineralTrace.Claims.Tests;

public class ClaimImportSanityTests
{
    [Fact]
    public void ValidateBulkImport_Throws_WhenNationalFileTooSmall()
    {
        var act = () => ClaimImportSanity.ValidateBulkImport(
            @"C:\data\blm\us-active.geojson",
            totalFeatures: 50_000,
            upserted: 50);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Expected at least*");
    }

    [Fact]
    public void ValidateBulkImport_Passes_ForStateExtract()
    {
        var act = () => ClaimImportSanity.ValidateBulkImport(
            @"C:\data\blm\nv-mlrs-claims.geojson",
            totalFeatures: 800,
            upserted: 750);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    [InlineData(50000, false)]
    public void IsLikelyDemoOnlyDatabase(int total, bool expected) =>
        ClaimImportSanity.IsLikelyDemoOnlyDatabase(total).Should().Be(expected);
}
