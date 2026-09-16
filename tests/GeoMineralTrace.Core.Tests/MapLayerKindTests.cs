using FluentAssertions;
using GeoMineralTrace.Core.Map;

namespace GeoMineralTrace.Core.Tests;

public class MapLayerKindTests
{
    [Fact]
    public void CooperativeNationalGeology_IsDistinctFromSgmc()
    {
        MapLayerKind.CooperativeNationalGeology.Should().NotBe(MapLayerKind.UsgsGeology);
        ((int)MapLayerKind.UsgsGeology).Should().Be(13);
        ((int)MapLayerKind.CooperativeNationalGeology).Should().Be(15);
    }

    [Fact]
    public void OverlayEndpoints_DocumentPublicUsgsServices()
    {
        UsgsMapOverlayEndpoints.SgmcGeologyWms.Should().StartWith("https://www.sciencebase.gov/");
        UsgsMapOverlayEndpoints.SgmcGeologyWms.Should().Contain("WMSServer");

        UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles
            .Should().StartWith("https://energy.usgs.gov/arcgis/rest/services/Hosted/mapunitpolys_esurf_v2/VectorTileServer/");
        UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles.Should().Contain("{z}/{y}/{x}.pbf");
        UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles
            .Should().NotBe(UsgsMapOverlayEndpoints.SgmcGeologyWms);
        UsgsMapOverlayEndpoints.EarthSurfaceIdentifyFeatureLayer
            .Should().StartWith("https://energy.usgs.gov/arcgis/rest/services/Hosted/mapunitpolys_esurf_labels/");
    }
}
