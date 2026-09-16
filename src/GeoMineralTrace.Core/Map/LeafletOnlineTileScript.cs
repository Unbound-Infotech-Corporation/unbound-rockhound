using System.Globalization;

namespace GeoMineralTrace.Core.Map;

/// <summary>
/// Leaflet JS emitted into the WinUI map WebView for online basemaps and USGS overlays.
/// Kept out of <c>MapPage.xaml.cs</c> so USGS URL injection cannot terminate
/// a three-quote C# raw string (CS8997).
/// </summary>
public static class LeafletOnlineTileScript
{
    private const string HillOpacityToken = "__HILL_OPACITY__";
    private const string SgmcWmsToken = "__SGMC_WMS__";
    private const string CngmPbfToken = "__CNGM_PBF__";

    /// <summary>
    /// Builds the online tile/overlay bootstrap script, including SGMC WMS, CNGM vector tiles,
    /// DEM hillshade, and USGS contours. Layer <c>.addTo(map)</c> calls follow the visibility flags.
    /// </summary>
    public static string Build(
        bool lidarOverlayOn,
        bool geologyOn,
        bool cngmOn,
        bool contoursOn,
        double hillshadeOpacity)
    {
        if (double.IsNaN(hillshadeOpacity) || double.IsInfinity(hillshadeOpacity)
            || hillshadeOpacity < 0 || hillshadeOpacity > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hillshadeOpacity),
                hillshadeOpacity,
                "Hillshade opacity must be a finite value between 0 and 1.");
        }

        var opacity = hillshadeOpacity.ToString("0.##", CultureInfo.InvariantCulture);

        // Placeholders (not C# interpolation) so Leaflet {z}/{y}/{x} templates stay literal
        // and USGS URLs never appear as raw-string delimiters inside this blob.
        var js = """
              var hillshade=null;
              var geology=null;
              var cngm=null;
              var contours=null;
              var hillOpacity=__HILL_OPACITY__;
              var street=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Street Map'});
              var carto=L.tileLayer('https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',{
                maxZoom:20,subdomains:'abcd',attribution:'© OpenStreetMap © CARTO'});
              var sat=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Imagery'});
              var usgsLidar=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSShadedReliefOnly/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — 3DEP shaded relief (DEM/LiDAR-derived, not raw LAS)'});
              hillshade=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Elevation/World_Hillshade/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,opacity:hillOpacity,attribution:'Esri World Hillshade (DEM/LiDAR-derived)'});
              window.__hillshade=hillshade;
              window.__setHillshadeOpacity=function(o){if(window.__hillshade){window.__hillshade.setOpacity(o);}};
              var topo=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:19,attribution:'Tiles © Esri — World Topo'});
              var usgsTopo=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSTopo/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — Topo'});
              var usgsImagery=L.tileLayer('https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}',{
                maxZoom:16,attribution:'USGS The National Map — Imagery'});
              var openTopo=L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png',{
                maxZoom:17,attribution:'© OpenStreetMap, SRTM — © OpenTopoMap (CC-BY-SA)'});
              var satRelief=L.layerGroup([
                L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',{maxZoom:19,attribution:'Tiles © Esri'}),
                L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Elevation/World_Hillshade/MapServer/tile/{z}/{y}/{x}',{
                  maxZoom:16,opacity:0.45,attribution:'Esri World Hillshade'})
              ]);
              geology=L.tileLayer.wms('__SGMC_WMS__',{
                layers:'SGMC_Geology',format:'image/png',transparent:true,opacity:0.42,version:'1.3.0',
                attribution:'USGS SGMC geologic units (Horton et al.) — public WMS'});
              contours=L.tileLayer.wms('https://carto.nationalmap.gov/arcgis/services/contours/MapServer/WMSServer',{
                layers:'0',format:'image/png',transparent:true,opacity:0.65,version:'1.3.0',
                attribution:'USGS The National Map — Contours'});
              function cngmAgeColor(a,fallback){
                a=(a||'').toLowerCase();
                if(/holocene|greenlandian|meghalayan|quaternary|pleistocene|calabrian|chibanian|gelasian/.test(a)) return '#fde047';
                if(/pliocene|zanclean|piacenzian|neogene|miocene|messinian|serravallian|langhian|burdigalian/.test(a)) return '#facc15';
                if(/oligocene|chattian|rupelian|eocene|priabonian|bartonian|lutetian|paleogene|paleocene/.test(a)) return '#fb923c';
                if(/tertiary/.test(a)) return '#fdba74';
                if(/cretaceous|maastrichtian|campanian|santonian|coniacian|turonian|cenomanian|albian|aptian|hauterivian|valanginian/.test(a)) return '#4ade80';
                if(/jurassic|tithonian|kimmeridgian|callovian/.test(a)) return '#22d3ee';
                if(/triassic|norian|carnian/.test(a)) return '#c084fc';
                if(/permian|lopingian|guadalupian|cisuralian|kungurian|artinskian/.test(a)) return '#f87171';
                if(/pennsylvanian|moscovian|bashkirian|kasimovian|carboniferous/.test(a)) return '#60a5fa';
                if(/mississippian|tournaisian|visean|serpukhovian/.test(a)) return '#93c5fd';
                if(/devonian|famennian|frasnian|givetian|emsian/.test(a)) return '#a3e635';
                if(/silurian|pridoli|ludlow|wenlock|llandovery|aeronian/.test(a)) return '#86efac';
                if(/ordovician|hirnantian|katian|sandbian|darriwilian/.test(a)) return '#5eead4';
                if(/cambrian|furongian|miaolingian|terreneuvian|series 2/.test(a)) return '#34d399';
                if(/paleozoic/.test(a)) return '#38bdf8';
                if(/mesozoic/.test(a)) return '#4ade80';
                if(/archean|eoarchean|paleoarchean|mesoarchean|neoarchean/.test(a)) return '#db2777';
                if(/proterozoic|paleoproterozoic|mesoproterozoic|neoproterozoic|precambrian/.test(a)) return '#e879f9';
                return fallback;
              }
              function cngmPolyStyle(p){
                p=p||{};
                var g=(p.geomaterial||'').toLowerCase();
                var s=(p.synthesis_mapunitname||'').toLowerCase();
                var a=(p.min_age||p.max_age||'');
                var t=g+' '+s;
                function sty(c){return {fill:true,fillColor:c,fillOpacity:0.42,color:'#27272a',weight:0.25,opacity:0.35};}
                if(t.indexOf('unmapped')>=0) return {fill:true,fillColor:'#d4d4d8',fillOpacity:0.12,color:'#a1a1aa',weight:0.2,opacity:0.2};
                if(/water or ice|water and ice/.test(t)) return {fill:true,fillColor:'#93c5fd',fillOpacity:0.28,color:'#3b82f6',weight:0.15,opacity:0.25};
                if(t.indexOf('artificial')>=0||t.indexOf('human-engineered')>=0||t.indexOf('"made"')>=0) return sty('#a8a29e');
                if(/limestone|dolomite|carbonate|marble/.test(t)) return sty('#86efac');
                if(/ultramafic/.test(t)) return sty('#166534');
                if(/granitic|felsic/.test(t)) return sty('#f9a8d4');
                if(/mafic|gabbro/.test(t)) return sty('#b91c1c');
                if(/volcanic|lava|pyroclastic|tephra|extrusive/.test(t)) return sty('#ef4444');
                if(/intrusive|igneous/.test(t)) return sty('#e11d48');
                if(/schist|gneiss|quartzite|phyllite|slate|metamorphic|meta-/.test(t)) return sty('#c084fc');
                if(/glacial|till|ice-contact/.test(t)) return sty('#e5e7eb');
                if(/alluvial|colluvium|eolian|loess|dune|playa|lacustrine|coastal|marine sediment|peat/.test(t)) return sty(cngmAgeColor(a,'#fde68a'));
                return sty(cngmAgeColor(a,'#d6d3d1'));
              }
              try{
                if(L.vectorGrid&&L.vectorGrid.protobuf){
                  cngm=L.vectorGrid.protobuf('__CNGM_PBF__',{
                    rendererFactory:L.canvas.tile,
                    interactive:false,
                    maxNativeZoom:13,
                    minZoom:6,
                    tileSize:512,
                    opacity:0.85,
                    attribution:'USGS Cooperative National Geologic Map v2 (NCGMP / NGMDB) — public domain',
                    vectorTileLayerStyles:{
                      'mapunitpolys_esurf':function(props){return cngmPolyStyle(props);},
                      'mapunitpolys_esurf/label':{fill:false,stroke:false,weight:0,opacity:0}
                    }
                  });
                  cngm.on('tileerror',function(){});
                }
              }catch(ex){cngm=null;}
              street.addTo(map);
              var overlayLayers={
                "Hillshade overlay (DEM/LiDAR-derived)":hillshade,
                "USGS State Geology (SGMC)":geology,
                "Elevation contours (USGS)":contours
              };
              if(cngm) overlayLayers["USGS Cooperative National Geologic Map (v2)"]=cngm;
              L.control.layers({
                "Streets (Esri)":street,
                "Streets (Carto / OSM)":carto,
                "Imagery (Esri)":sat,
                "Imagery + hillshade":satRelief,
                "USGS Imagery":usgsImagery,
                "USGS Topo (The National Map)":usgsTopo,
                "OpenTopoMap":openTopo,
                "Esri Topo":topo,
                "DEM hillshade (USGS 3DEP)":usgsLidar
              }, overlayLayers, {collapsed:true,position:'topright'}).addTo(map);
              """
            .Replace(HillOpacityToken, opacity, StringComparison.Ordinal)
            .Replace(SgmcWmsToken, UsgsMapOverlayEndpoints.SgmcGeologyWms, StringComparison.Ordinal)
            .Replace(CngmPbfToken, UsgsMapOverlayEndpoints.CooperativeNationalGeologyVectorTiles, StringComparison.Ordinal);

        if (lidarOverlayOn)
            js += "hillshade.addTo(map);";
        if (geologyOn)
            js += "geology.addTo(map);";
        if (cngmOn)
            js += "if(cngm)cngm.addTo(map);";
        if (contoursOn)
            js += "contours.addTo(map);";
        return js;
    }
}
