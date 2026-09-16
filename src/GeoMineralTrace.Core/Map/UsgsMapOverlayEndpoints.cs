using System.Globalization;
using System.Net;
using GeoMineralTrace.Core.Geology;

namespace GeoMineralTrace.Core.Map;

/// <summary>
/// Public USGS overlay and identify endpoints consumed by the Leaflet map (Online Enrichment).
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

    public const string EnergyArcGisHosted =
        "https://energy.usgs.gov/arcgis/rest/services/Hosted";

    /// <summary>
    /// Cooperative National Geologic Map v2 — Earth's surface map-unit polygons as public vector tiles.
    /// Hosted service: <c>Hosted/mapunitpolys_esurf_v2/VectorTileServer</c> (no token).
    /// Distinct from <see cref="SgmcGeologyWms"/>.
    /// </summary>
    public const string CooperativeNationalGeologyVectorTiles =
        EnergyArcGisHosted + "/mapunitpolys_esurf_v2/VectorTileServer/tile/{z}/{y}/{x}.pbf";

    public const string EarthSurfaceIdentifyFeatureLayer =
        EnergyArcGisHosted + "/mapunitpolys_esurf_labels/FeatureServer/0";

    public const string NationalGeologyViewerUrl = "https://ngmdb.usgs.gov/nationalgeology/";
    public const string NgmdbHomeUrl = "https://ngmdb.usgs.gov/";
    public const string EarthSurfaceDataReleaseUrl = "https://doi.org/10.5066/P146VGVM";
    public const string ProductDescriptionUrl = "https://ngmdb.usgs.gov/Prodesc/proddesc_118545.htm";

    public const string Attribution =
        "USGS / AASG — National Cooperative Geologic Mapping Program; Cooperative National Geologic Map (NGMDB). Public domain.";

    public const string IdentifyOutFields =
        "mapunit,name,description,geomaterial,geomaterialconfidence,age,min_age,max_age," +
        "synthesis_mapunit,synthesis_mapunitname,synthesis_description,map_citation,ngmdb_url," +
        "synthesis_citation,synthesis_url";

    public static string VectorTileUrl(CngmTheme theme) =>
        $"{EnergyArcGisHosted}/mapunitpolys_{ThemeSlug(theme)}_v2/VectorTileServer/tile/{{z}}/{{y}}/{{x}}.pbf";

    public static string VectorTileSourceLayer(CngmTheme theme) =>
        "mapunitpolys_" + ThemeSlug(theme);

    public static string IdentifyFeatureLayer(CngmTheme theme) =>
        $"{EnergyArcGisHosted}/mapunitpolys_{ThemeSlug(theme)}_labels/FeatureServer/0";

    public static string ThemeSlug(CngmTheme theme) => theme switch
    {
        CngmTheme.Quaternary => "quat",
        CngmTheme.PreQuaternary => "prequat",
        CngmTheme.Precambrian => "precamb",
        _ => "esurf"
    };

    public static string BuildIdentifyQueryUrl(CngmTheme theme, double latitude, double longitude)
    {
        var lat = latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.######", CultureInfo.InvariantCulture);
        var geometry = $"{{\"x\":{lon},\"y\":{lat},\"spatialReference\":{{\"wkid\":4326}}}}";
        return IdentifyFeatureLayer(theme)
            + "/query?f=json"
            + "&geometry=" + WebUtility.UrlEncode(geometry)
            + "&geometryType=esriGeometryPoint"
            + "&inSR=4326"
            + "&spatialRel=esriSpatialRelIntersects"
            + "&outFields=" + WebUtility.UrlEncode(IdentifyOutFields)
            + "&returnGeometry=false"
            + "&resultRecordCount=1";
    }
}
