using FluentAssertions;
using GeoMineralTrace.Core.Geo;

namespace GeoMineralTrace.Core.Tests;

public class UsStateCodeTests
{
    [Fact]
    public void States50_HasExactlyFifty_UniqueCodes()
    {
        UsStateCode.States50.Should().HaveCount(50);
        UsStateCode.States50.Select(s => s.Code).Should().OnlyHaveUniqueItems();
        UsStateCode.States50.Should().Contain(s => s.Code == "WA" && s.Display == "Washington (WA)");
        UsStateCode.States50.Should().NotContain(s => s.Code == "DC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("All")]
    [InlineData("All states")]
    [InlineData("All / near home")]
    [InlineData("All ST")]
    [InlineData("ST")]
    public void Normalize_EmptyOrAll_ReturnsNull(string? raw) =>
        UsStateCode.Normalize(raw).Should().BeNull();

    [Theory]
    [InlineData("WA", "WA")]
    [InlineData("wa", "WA")]
    [InlineData("Washington", "WA")]
    [InlineData("Washington (WA)", "WA")]
    [InlineData("oregon", "OR")]
    [InlineData("New York", "NY")]
    [InlineData("DC", "DC")]
    [InlineData("District of Columbia", "DC")]
    public void Normalize_AcceptsCodeNameAndDisplay(string raw, string expected) =>
        UsStateCode.Normalize(raw).Should().Be(expected);

    [Theory]
    [InlineData("XX")]
    [InlineData("Washingtonia")]
    [InlineData("W")]
    [InlineData("WASH")]
    public void Normalize_Unknown_ReturnsNull(string raw) =>
        UsStateCode.Normalize(raw).Should().BeNull();
}
