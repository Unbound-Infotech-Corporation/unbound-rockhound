using FluentAssertions;
using GeoMineralTrace.Core.Geology;
using GeoMineralTrace.Core.Map;

namespace GeoMineralTrace.Core.Tests;

public class GeoMaterialAssociationAndProspectGuessTests
{
    [Fact]
    public void Pegmatite_MapsTo_TourmalineBerylMica()
    {
        var hits = GeoMaterialMineralCatalog.Match("granite and local alaskite-aplite-pegmatite dikes");
        hits.Should().Contain(r => r.Id == "pegmatite-granitic");
        var minerals = hits.SelectMany(h => h.Minerals).ToHashSet(StringComparer.OrdinalIgnoreCase);
        minerals.Should().Contain(["tourmaline", "beryl", "mica"]);
    }

    [Fact]
    public void Volcanic_MapsTo_AgateOpalObsidian()
    {
        var hits = GeoMaterialMineralCatalog.Match("Miocene basalt flows and rhyolite tuff");
        hits.Should().Contain(r => r.Id == "volcanic-silica");
        var minerals = hits.SelectMany(h => h.Minerals).ToHashSet(StringComparer.OrdinalIgnoreCase);
        minerals.Should().Contain(["agate", "opal", "obsidian"]);
    }

    [Fact]
    public void Ultramafic_MapsTo_JadeiteChromite()
    {
        var minerals = GeoMaterialMineralCatalog.Match("serpentinized peridotite and ultramafic melange")
            .SelectMany(h => h.Minerals)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        minerals.Should().Contain(["jadeite", "chromite"]);
    }

    [Fact]
    public void Limestone_MapsTo_CalciteFluorsparFamily()
    {
        var minerals = GeoMaterialMineralCatalog.Match("karst limestone and dolomite")
            .SelectMany(h => h.Minerals)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        minerals.Should().Contain(["calcite", "fluorite"]);
    }

    [Fact]
    public void Alluvium_MapsTo_PlacerMinerals_Speculative()
    {
        var rule = GeoMaterialMineralCatalog.Match("alluvial gravel terrace").Should().ContainSingle(r => r.Id == "placer-alluvium").Subject;
        rule.GeologyOnlyBand.Should().Be(ProspectGuessBand.Speculative);
        rule.Minerals.Should().Contain(["gold", "garnet", "sapphire"]);
    }

    [Fact]
    public void GeologyAlone_Never_Stronger()
    {
        var unit = CngmIdentifyJsonParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cngm-identify-results-pegmatite.json")));
        var result = ProspectGuessScorer.Score(unit, [], usedOnlineGeology: true);

        result.Hints.Should().NotBeEmpty();
        result.Hints.Should().OnlyContain(h => h.Band != ProspectGuessBand.Stronger);
        result.Hints.Select(h => h.Mineral).Should().Contain(m =>
            m.Equals("tourmaline", StringComparison.OrdinalIgnoreCase)
            || m.Equals("beryl", StringComparison.OrdinalIgnoreCase));
        result.Disclaimer.Should().Contain("not a collecting permit");
        result.UsedOnlineGeology.Should().BeTrue();
    }

    [Fact]
    public void NearbyMatchingMineral_PromotesTo_Stronger()
    {
        var unit = new GeologicMapUnit(
            "Ti", "Tertiary intrusive rocks", "granite and pegmatite",
            "Intrusive igneous rock", "High", "Tertiary", null, null,
            "Ti", "Intrusive rocks", null, "Source map citation",
            "https://ngmdb.usgs.gov/example", null, null, CngmTheme.EarthSurface);

        var nearby = new NearbyMineralOccurrence(
            "Sample pegmatite prospect",
            "USGS MRDS",
            ["tourmaline", "mica"],
            DistanceKm: 4.2,
            IsCurated: false);

        var result = ProspectGuessScorer.Score(unit, [nearby], usedOnlineGeology: true);
        var tourmaline = result.Hints.Should().ContainSingle(h =>
            h.Mineral.Equals("tourmaline", StringComparison.OrdinalIgnoreCase)).Subject;
        tourmaline.Band.Should().Be(ProspectGuessBand.Stronger);
        tourmaline.Reason.Should().Contain("4.2");
        tourmaline.Citations.Should().Contain(c => c.Contains("CNGM", StringComparison.OrdinalIgnoreCase));
        tourmaline.Citations.Should().Contain(c => c.Contains("Sample pegmatite"));
    }

    [Fact]
    public void WaterOrUnmapped_Yields_NoHints()
    {
        var unit = new GeologicMapUnit(
            "H2O", "Water or ice", null, "Water or ice", "High",
            null, null, null, null, "Water or ice", null, null, null, null, null,
            CngmTheme.EarthSurface);

        var result = ProspectGuessScorer.Score(unit, [], usedOnlineGeology: true);
        result.Hints.Should().BeEmpty();
        result.StatusNote.Should().Contain("water");
    }

    [Fact]
    public void FormatPanel_Includes_Unit_And_Disclaimer()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cngm-identify-oregon-john-day.json"));
        var unit = CngmIdentifyJsonParser.Parse(json);
        var result = ProspectGuessScorer.Score(unit, [], usedOnlineGeology: true);
        var panel = ProspectGuessScorer.FormatPanel(result);

        panel.Should().Contain("John Day Formation");
        panel.Should().Contain("GeoMaterial: Sandstone and mudstone");
        panel.Should().Contain("Prospect guess");
        panel.Should().Contain("not a collecting permit");
        panel.Should().Contain("Walker");
    }

    [Fact]
    public void OverlayEndpoints_IncludeIdentifyAndThemes()
    {
        UsgsMapOverlayEndpoints.VectorTileUrl(CngmTheme.EarthSurface)
            .Should().Be(UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles);
        UsgsMapOverlayEndpoints.VectorTileUrl(CngmTheme.Quaternary)
            .Should().Contain("mapunitpolys_quat_v2");
        UsgsMapOverlayEndpoints.IdentifyFeatureLayer(CngmTheme.Precambrian)
            .Should().Contain("mapunitpolys_precamb_labels");
        UsgsMapOverlayEndpoints.VectorTileSourceLayer(CngmTheme.PreQuaternary)
            .Should().Be("mapunitpolys_prequat");
        UsgsMapOverlayEndpoints.Attribution.Should().Contain("NGMDB");
    }
}
