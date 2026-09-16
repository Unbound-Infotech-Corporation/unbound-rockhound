using FluentAssertions;
using GeoMineralTrace.Core.Geology;
using GeoMineralTrace.Core.Map;

namespace GeoMineralTrace.Core.Tests;

public class CngmIdentifyJsonParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_FeatureServerQuery_JohnDayFormation()
    {
        var json = Fixture("cngm-identify-oregon-john-day.json");
        var unit = CngmIdentifyJsonParser.Parse(json, CngmTheme.EarthSurface);

        unit.Should().NotBeNull();
        unit!.MapUnit.Should().Be("Tsfj");
        unit.Name.Should().Be("John Day Formation of east-central Oregon");
        unit.GeoMaterial.Should().Be("Sandstone and mudstone");
        unit.GeoMaterialConfidence.Should().Be("High");
        unit.SynthesisMapUnitName.Should().Be("Sedimentary rocks");
        unit.Age.Should().Be("lower Miocene to uppermost Eocene");
        unit.MinAge.Should().Be("Burdigalian");
        unit.MapCitation.Should().Contain("Geologic map of Oregon");
        unit.NgmdbUrl.Should().StartWith("https://ngmdb.usgs.gov/");
        unit.DisplayTitle.Should().Contain("John Day");
        unit.ThemeLabel.Should().Be("Earth Surface");
    }

    [Fact]
    public void Parses_IdentifyResults_Array()
    {
        var json = Fixture("cngm-identify-results-pegmatite.json");
        var unit = CngmIdentifyJsonParser.Parse(json, CngmTheme.EarthSurface);

        unit.Should().NotBeNull();
        unit!.Name.Should().Be("Tertiary intrusive rocks");
        unit.Description.Should().Contain("pegmatite");
        unit.GeoMaterial.Should().Be("Intrusive igneous rock");
    }

    [Fact]
    public void Empty_Or_Error_ReturnsNull()
    {
        CngmIdentifyJsonParser.Parse(null).Should().BeNull();
        CngmIdentifyJsonParser.Parse("").Should().BeNull();
        CngmIdentifyJsonParser.Parse("{not json").Should().BeNull();
        CngmIdentifyJsonParser.Parse("""{"error":{"code":500,"message":"fail"}}""").Should().BeNull();
        CngmIdentifyJsonParser.Parse("""{"features":[]}""").Should().BeNull();
    }

    [Fact]
    public void IdentifyQueryUrl_IsPublicUsgsPointQuery()
    {
        var url = UsgsMapOverlayEndpoints.BuildIdentifyQueryUrl(CngmTheme.EarthSurface, 47.4, -121.5);
        url.Should().StartWith(UsgsMapOverlayEndpoints.EarthSurfaceIdentifyFeatureLayer);
        url.Should().Contain("/query?");
        url.Should().Contain("geometryType=esriGeometryPoint");
        url.Should().Contain("inSR=4326");
        url.Should().Contain("-121.5");
        url.Should().Contain("47.4");
        url.Should().Contain("geomaterial");
    }
}
