namespace GeoMineralTrace.Core.Map;

/// <summary>
/// Public USGS overlay endpoints consumed by the Leaflet map (Online Enrichment).
/// These are not secrets — they are documented USGS/NGMDB services.
/// </summary>
public static class UsgsMapOverlayEndpoints
{
    /// <summary>
    /// USGS State Geologic Map Compilation (SGMC) lithology WMS (Lower-48 state compilation).
    /// Layer name: <c>SGMC_Geology</c>.
    /// </summary>
    public const string SgmcGeologyWms =
        "https://www.sciencebase.gov/arcgis/services/Catalog/5888bf4fe4b05ccb964bab9d/MapServer/WMSServer";

    /// <summary>
    /// Cooperative National Geologic Map v2 — Earth's surface map-unit polygons as public vector tiles.
    /// Hosted service: <c>Hosted/mapunitpolys_esurf_v2/VectorTileServer</c> (no token).
    /// Distinct from <see cref="SgmcGeologyWms"/>.
    /// </summary>
    public const string CooperativeNationalGeologyVectorTiles =
        "https://energy.usgs.gov/arcgis/rest/services/Hosted/mapunitpolys_esurf_v2/VectorTileServer/tile/{z}/{y}/{x}.pbf";
}
