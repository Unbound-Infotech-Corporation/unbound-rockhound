using FluentAssertions;
using GeoMineralTrace.Core.Geology;
using GeoMineralTrace.Core.Map;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GeoMineralTrace.Core.Tests;

public class LeafletOnlineTileScriptTests
{
    /// <summary>
    /// Mid-line splice of a three-quote raw string inside a JS single-quoted URL.
    /// This is the pattern that produced CS8997 in MapPage.xaml.cs.
    /// </summary>
    private static readonly string ForbiddenRawStringSplice = "'" + new string('"', 3) + " +";

    [Fact]
    public void Build_WiresUsgsGeologyLidarAndContourEndpoints()
    {
        var js = LeafletOnlineTileScript.Build(
            lidarOverlayOn: true,
            geologyOn: true,
            cngmOn: true,
            contoursOn: true,
            hillshadeOpacity: 0.55);

        js.Should().Contain(UsgsMapOverlayEndpoints.SgmcGeologyWms);
        js.Should().Contain("layers:'SGMC_Geology'");
        js.Should().Contain("geology=L.tileLayer.wms(");
        js.Should().Contain("\"USGS State Geology (SGMC)\":geology");

        js.Should().Contain(UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles);
        js.Should().Contain("L.vectorGrid.protobuf(");
        js.Should().Contain("mapunitpolys_esurf");
        js.Should().Contain("USGS Cooperative National Geologic Map (NGMDB)");
        js.Should().Contain("window.__cngmMode='synthesis'");
        js.Should().Contain("window.__setCngmOpacity=");
        js.Should().Contain("opacity:0.42,");

        js.Should().Contain("contours=L.tileLayer.wms(");
        js.Should().Contain("carto.nationalmap.gov/arcgis/services/contours/MapServer/WMSServer");
        js.Should().Contain("\"Elevation contours (USGS)\":contours");

        js.Should().Contain("hillshade=L.tileLayer(");
        js.Should().Contain("Elevation/World_Hillshade");
        js.Should().Contain("USGSShadedReliefOnly");
        js.Should().Contain("var hillOpacity=0.55;");

        js.Should().NotContain("__SGMC_WMS__");
        js.Should().NotContain("__CNGM_PBF__");
        js.Should().NotContain("__HILL_OPACITY__");
        js.Should().NotContain("__CNGM_SOURCE__");
        js.Should().NotContain("__CNGM_OPACITY__");
        js.Should().NotContain("__CNGM_MODE__");
        js.Should().NotContain("__CNGM_TITLE__");
        js.Should().NotContain("__CNGM_ATTR__");
        js.Should().NotContain("UsgsMapOverlayEndpoints");
        js.Should().NotContain(ForbiddenRawStringSplice);
    }

    [Fact]
    public void Build_AppliesCngmThemeSymbologyAndOpacity()
    {
        var js = LeafletOnlineTileScript.Build(
            lidarOverlayOn: false,
            geologyOn: false,
            cngmOn: true,
            contoursOn: false,
            hillshadeOpacity: 0.4,
            theme: CngmTheme.Quaternary,
            symbology: CngmSymbology.Age,
            cngmOpacity: 0.7);

        js.Should().Contain(UsgsMapOverlayEndpoints.VectorTileUrl(CngmTheme.Quaternary));
        js.Should().Contain("cngmStyles['mapunitpolys_quat']");
        js.Should().Contain("window.__cngmMode='age'");
        js.Should().Contain("USGS CNGM (Quaternary)");
        js.Should().Contain("opacity:0.7,");
        js.Should().Contain(UsgsMapOverlayEndpoints.Attribution);
        js.Should().NotContain("__CNGM_PBF__");
        js.Should().NotContain(ForbiddenRawStringSplice);
    }

    [Fact]
    public void Build_AddToMapHonorsLayerToggles()
    {
        var off = LeafletOnlineTileScript.Build(false, false, false, false, 0.4);
        off.Should().NotContain("hillshade.addTo(map);");
        off.Should().NotContain("geology.addTo(map);");
        off.Should().NotContain("if(cngm)cngm.addTo(map);");
        off.Should().NotContain("contours.addTo(map);");
        off.Should().Contain("var hillOpacity=0.4;");

        var on = LeafletOnlineTileScript.Build(true, true, true, true, 0.8);
        on.Should().Contain("hillshade.addTo(map);");
        on.Should().Contain("geology.addTo(map);");
        on.Should().Contain("if(cngm)cngm.addTo(map);");
        on.Should().Contain("contours.addTo(map);");
    }

    [Fact]
    public void Build_RejectsNonFiniteOpacity()
    {
        var act = () => LeafletOnlineTileScript.Build(false, false, false, false, double.NaN);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("hillshadeOpacity");

        var cngm = () => LeafletOnlineTileScript.Build(
            false, false, false, false, 0.5, cngmOpacity: double.PositiveInfinity);
        cngm.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("cngmOpacity");
    }

    [Theory]
    [InlineData("src/GeoMineralTrace.App/Pages/MapPage.xaml.cs")]
    [InlineData("src/GeoMineralTrace.Core/Map/LeafletOnlineTileScript.cs")]
    public void LeafletSources_DoNotSpliceRawStringsInsideJsQuotes(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            ForbiddenRawStringSplice,
            "splicing a C# raw string closed by three quotes inside a JS URL is CS8997");
    }

    [Theory]
    [InlineData("src/GeoMineralTrace.App/Pages/MapPage.xaml.cs")]
    [InlineData("src/GeoMineralTrace.Core/Map/LeafletOnlineTileScript.cs")]
    public void LeafletSources_HaveNoCSharpSyntaxErrors(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue(path);
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void MapPage_DelegatesOnlineTilesToCoreHelper()
    {
        var path = Path.Combine(FindRepoRoot(), "src/GeoMineralTrace.App/Pages/MapPage.xaml.cs");
        var text = File.ReadAllText(path);
        text.Should().Contain("LeafletOnlineTileScript.Build(");
        text.Should().Contain("leaflet.vectorgrid");
        text.Should().Contain("LayerCngmGeology");
        text.Should().Contain("MapLayerKind.CooperativeNationalGeology");
        text.Should().Contain("MapLayerKind.UsgsGeology");
        text.Should().Contain("MapLayerKind.ElevationContours");
        text.Should().Contain("MapLayerKind.LidarTerrain");
    }

    [Fact]
    public void MapPage_DoesNotConfigureAwaitWinRtExecuteScriptAsync()
    {
        var path = Path.Combine(FindRepoRoot(), "src/GeoMineralTrace.App/Pages/MapPage.xaml.cs");
        var text = File.ReadAllText(path);
        text.Should().Contain("ExecuteScriptAsync(");
        text.Should().NotMatchRegex(
            @"ExecuteScriptAsync[\s\S]{0,240}?\)\.ConfigureAwait",
            "WinRT IAsyncOperation has no ConfigureAwait (CS1929)");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GeoMineralTrace.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find GeoMineralTrace.sln walking up from {AppContext.BaseDirectory}.");
    }
}
