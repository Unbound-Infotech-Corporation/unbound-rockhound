# Data sources & update procedures

## Principles

1. Prefer **public / open scientific** sources with clear attribution.
2. Store **source URLs or citations** on every locality (`Sources` field).
3. Never scrape behind authentication or ignore site terms.
4. Online enrichment is **opt-in** (Settings); when off, Map plots markers without OSM tiles.
5. Flag **closed, claim, permit-only, and restricted** sites; do not silently promote them.
6. Rockhounding module **never initiates network I/O at runtime** — USGS refreshes are offline, periodic import jobs against local files.
7. Map **basemap / overlay tiles** load only when the user enables **Settings → Online Enrichment**. They are not rockhounding-module I/O.

## USGS MRDS (national)

| Item | Value |
|------|--------|
| Portal | https://mrdata.usgs.gov/mrds/ |
| US map | https://mrdata.usgs.gov/mrds/map-us.html |
| **Preferred bulk format** | Flattened CSV — `mrds-csv.zip` (~23 MB compressed, ~137 MB `mrds.csv`) |
| Download URL | https://mrdata.usgs.gov/mrds/mrds-csv.zip |
| Alternate formats | Compact shapefile `mrds-trim.zip`; RDBMS multi-table CSVs `rdbms-tab.zip` / `rdbms-tab-all.zip` |
| Field set used | `dep_id`, `site_name`, `latitude`, `longitude`, `country`, `state`, `county`, `commod1`–`commod3`, `dep_type`, `dev_stat` (legacy samples: `oper_status`), geology (`ore`, `hrock_*`, `arock_*`, `structure`, `tectonic`, `model`, `work_type`), `ref` / `url` |
| Coverage note | Flattened extract is worldwide; importer keeps **United States** rows only for national import |
| Vintage / caveat | Systematic MRDS updates ceased **2011**. Human-activity fields (operating status, ownership) may be outdated — every imported row is tagged and surfaces `[OUTDATED ACTIVITY]` in UI |

### Operator download (offline)

```powershell
$dir = "data/usgs"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Invoke-WebRequest -Uri "https://mrdata.usgs.gov/mrds/mrds-csv.zip" -OutFile "$dir/mrds-csv.zip"
Expand-Archive "$dir/mrds-csv.zip" -DestinationPath "$dir/mrds-csv" -Force
```

Large extracts under `data/usgs/` are gitignored; keep only small samples in `data/seed/`.

## USGS Critical Minerals (supplementary)

| Item | Value |
|------|--------|
| Portal | https://mrdata.usgs.gov/uscritmin/ |
| Publication | Hammarstrom et al., 2023 — https://doi.org/10.5066/P9K1HBNT |
| **Preferred format** | CSV table from ScienceBase item `6464de5bd34ec179a83d9e6c` (`Critical_mineral_deposits_table_v2_csv.csv`) |
| Alternate | File geodatabase / shapefile packages linked from the portal |
| Fields used | `Deposit`, `Lat_WGS84`, `Long_WGS84`, `State`, `CritMin`, `DepType`, `MinSystem`, `FocusArea`, `DepCat`, `Production`, `Resources`, `Source_s`, `Links` |
| Tagging | `source = "USGS Critical Minerals"`, vintage **2023**, same LOW legal-clarity tier + outdated-activity flag |

## Import pipeline

- Implementation: `UsgsMrdsImporter` (extends the existing CSV importer path — no parallel importer).
- Offline CLI: `artifacts/UsgsImport` — writes to `%LocalAppData%\GeoMineralTrace\localities.db` by default.
- App UI: Rockhounding → **Import USGS MRDS…** (local file picker; streams large files).
- Upsert key: `usgs-mrds:{dep_id}` or `usgs-critmin:…`; also merges onto existing seed rows with same name+state within ~0.5 km.
- Ratings: `LegalClarityForUsgsOccurrence = 2.0` (LOW); MRDS recency score uses vintage 2011.
- Invalid coordinates are **logged/counted**, not silently imported.

```powershell
dotnet run --project artifacts/UsgsImport -- `
  --mrds data/usgs/mrds-csv/mrds.csv `
  --critmin data/usgs/Critical_mineral_deposits_table_v2_csv.csv
```

## BLM mining claims (MLRS)

| Item | Value |
|------|--------|
| Platform | **MLRS** replaced LR2000 (reports still at https://reports.blm.gov/reports/mlrs/) |
| Geospatial bulk | ArcGIS FeatureServer “BLM Natl MLRS Mining Claims” — Not Closed + Closed layers |
| Not Closed | https://gis.blm.gov/nlsdb/rest/services/HUB/BLM_Natl_MLRS_Mining_Claims_Not_Closed/FeatureServer/0 |
| Closed | https://gis.blm.gov/nlsdb/rest/services/HUB/BLM_Natl_MLRS_Mining_Claims_Closed/FeatureServer/0 |
| Hub dataset | https://gbp-blm-egis.hub.arcgis.com/datasets/BLM-EGIS::blm-natl-mlrs-mining-claims-not-closed |
| Export formats | GeoJSON, CSV, shapefile, file GDB (FeatureServer `supportedExportFormats`) |
| Attribute fields | `CSE_NAME`, `CSE_NR`, `LEG_CSE_NR`, `BLM_PROD` (lode/placer/mill/tunnel), `CSE_DISP`, `CSE_META` (PLSS legal), `RCRD_ACRS`, `Created`/`Modified` |
| Enrichment CSV | Geographic Index / Customer reports from https://reports.blm.gov/reports/mlrs/ can add claimant + assessment year when exported locally |
| Module | `GeoMineralTrace.Claims` — SQLite `%LocalAppData%\GeoMineralTrace\claims.db`; **no runtime network** |
| Status model | **Active** (informational / private transfer only) · **Expiring Soon** (fee evidence + Sept 1 window) · **Lapsed/Reopenable** (closed — possible new stake) — never “for sale” |
| Import CLI | `dotnet run --project artifacts/BlmClaimsImport -- --file …` · `--import-pattern us-*.geojson` · `--download-state NV` · `--download-national` |
| Historical verify extract | NV-only with `--pages 3` (~10k rows) — **not** full national |
| National extract (2026-08-27) | BLM MLRS Not Closed + Closed via `--download-national`; ~667k claims in `claims.db` |

### Operator download (offline extract)

```powershell
# Full Nevada (page until short response)
dotnet run --project artifacts/BlmClaimsImport -- --download-state NV --pages 0

# Full national Not Closed + Closed (large / long-running; resumes existing us-*.geojson pages)
dotnet run --project artifacts/BlmClaimsImport -- --download-national --pages 0

# Re-import cached pages without re-download
dotnet run --project artifacts/BlmClaimsImport -- --import-pattern "us-*.geojson"

# Small verify sample (legacy)
dotnet run --project artifacts/BlmClaimsImport -- --download-state NV --pages 3
```

Or export GeoJSON/CSV from the FeatureServer / Hub UI into `data/blm/` and import via the Claims page **Import BLM…** button.

---

## Mine & locality registry (public / private)

| Store | Path | Contents |
|-------|------|----------|
| **Localities / mines** | `%LocalAppData%\GeoMineralTrace\localities.db` | ~267k U.S. USGS MRDS + Critical Minerals occurrence points, curated fee-dig / public collecting sites |
| **BLM claims** | `%LocalAppData%\GeoMineralTrace\claims.db` | ~667k federal mining claims (tenure), used to tag claim vicinity |

**Honest coverage:** There is no free national database of every private parcel mine. USGS MRDS is an **occurrence inventory** (not ownership). Ownership labels in the app are best-effort:

1. **Curated CSV** — `data/seed/known-mines-public-private.csv` (fee digs, state parks, known private tourist mines)
2. **Text heuristics** — name/notes mentioning private, BLM, USFS, state park, etc.
3. **Claim proximity** — sites within ~2.5 km of an active BLM claim → `Claim` land type

```powershell
dotnet run --project artifacts/MineOwnershipEnrich --
# or in-app: Rockhounding → Classify ownership…
```

UI: **Mine & Locality Registry** (Rockhounding nav) — filter by Public / Private / Claim / Unknown.

---

## Hydrology — named rivers & minerals (UnboundRockhound)

| Item | Value |
|------|--------|
| Portal | https://www.usgs.gov/national-hydrography/national-hydrography-dataset |
| **Preferred bulk format** | NHD GeoJSON flowlines (state or HUC extracts) with `gnis_name` / `GNIS_Name` |
| Local staging | `data/nhd/` (gitignored for bulk); sample in `data/seed/rivers-sample.geojson` |
| Curated finds | `data/seed/river-finds.csv` — `river_external_id,mineral,citation,notes,typical_habitat` |
| Module | `GeoMineralTrace.Hydrology` — SQLite `%LocalAppData%\GeoMineralTrace\rivers.db`; **no runtime network** |
| Import filter | Named watercourses only (GNIS name required); geometry simplified to ~200 vertices |
| Mineral linkage | **Hybrid** — curated CSV citations + proximity-inferred links from `localities.db` (default 3 km corridor) |
| Import CLI | `dotnet run --project artifacts/RiversImport -- --nhd … --curated … --enrich-proximity --radius-km 3` |
| In-app UI | **Rivers** nav — search, curated/inferred badges, NHD import, proximity enrich |

### Operator download (offline)

```powershell
# Place state/HUC NHD named flowline GeoJSON under data/nhd/, then:
dotnet run --project artifacts/RiversImport -- `
  --nhd data/nhd/co-named-flowlines.geojson `
  --curated data/seed/river-finds.csv `
  --enrich-proximity --radius-km 3
```

Or use in-app **Rivers → Import NHD…** and **Enrich proximity** (uses local `localities.db` only).

---

## Trails — access & hiking corridors

| Item | Value |
|------|--------|
| Preferred sources | [USGS The National Map — Trails](https://apps.nationalmap.gov/downloader/), USFS trail extracts, or curated rockhound-access GeoJSON |
| **Preferred format** | GeoJSON FeatureCollection of `LineString` / `MultiLineString` with a name property |
| Recognized name fields | `name`, `NAME`, `TRAIL_NAME`, `OfficialTrailName`, `FEATURE_NAME`, `gnis_name` |
| Recognized id fields | `permanent_identifier`, `TRAIL_ID`, `TRAIL_NO`, `FEATURE_ID`, `OBJECTID` |
| Kind hints | `trail_type` / `RTE_TYPE` / `FEATURE_TYPE` → Hiking, AccessRoad, OHV, Pack |
| Local staging | `data/trails/` (gitignored for bulk); sample in `data/seed/trails-sample.geojson` |
| Module | `GeoMineralTrace.Hydrology` — SQLite `%LocalAppData%\UnboundRockhound\trails.db`; **no runtime network** |
| Upsert keys | `seed:…`, `tnm:…`, `fs:…`, or `trail:…` |
| Import CLI | `dotnet run --project artifacts/TrailsImport -- --geojson path\to\trails.geojson` |
| In-app UI | **Map → Import trails…** (also seeds on first launch) |

### Operator download (offline)

```powershell
# Place state/region trail GeoJSON under data/trails/, then:
dotnet run --project artifacts/TrailsImport -- `
  --geojson data/trails/co-trails.geojson
```

Or use in-app **Map → Import trails…**. Trails appear when the **Access & hiking trails** layer is on, scoped to your home search radius.

---

## Map basemaps & overlays (opt-in Online Enrichment)

These are **public** tile/WMS services with required attribution. They are not raw LAS/LiDAR point clouds. Hillshade is **DEM / LiDAR-derived shaded relief**.

| Layer | Kind | Endpoint (summary) | Attribution / terms |
|-------|------|--------------------|---------------------|
| Streets (Esri) | Basemap | ArcGIS `World_Street_Map` tiles | Tiles © Esri |
| Streets (Carto / OSM) | Basemap | CARTO Voyager | © OpenStreetMap © CARTO — do **not** hit `tile.openstreetmap.org` (blocks desktop WebView) |
| Imagery (Esri) | Basemap | ArcGIS `World_Imagery` | Tiles © Esri |
| Imagery + hillshade | Basemap group | Imagery + Esri `World_Hillshade` | Esri |
| USGS Imagery | Basemap | The National Map `USGSImageryOnly` | USGS TNM (public domain; attribute USGS) |
| USGS Topo | Basemap | TNM `USGSTopo` | USGS The National Map |
| OpenTopoMap | Basemap | `tile.opentopomap.org` | © OSM, SRTM — © OpenTopoMap (CC-BY-SA) |
| Esri Topo | Basemap | ArcGIS `World_Topo_Map` | Tiles © Esri |
| DEM hillshade (USGS 3DEP) | Basemap | TNM `USGSShadedReliefOnly` | USGS 3DEP shaded relief (LiDAR/DEM-derived) |
| Hillshade overlay | Overlay | Esri `Elevation/World_Hillshade` | Esri World Hillshade — opacity slider in the layer panel |
| USGS State Geology (SGMC) | Overlay | ScienceBase WMS `5888bf4fe4b05ccb964bab9d` layer `SGMC_Geology` | USGS SGMC (Horton et al., [doi:10.5066/F7WH2N65](https://doi.org/10.5066/F7WH2N65)); older seamless **state compilation** (Lower 48). [state geology](https://mrdata.usgs.gov/geology/state) |
| USGS Cooperative National Geologic Map (v2) | Overlay | Public ArcGIS vector tiles `Hosted/mapunitpolys_esurf_v2` — `https://energy.usgs.gov/arcgis/rest/services/Hosted/mapunitpolys_esurf_v2/VectorTileServer/tile/{z}/{y}/{x}.pbf` (Earth’s surface / National Geology; 50 states + most U.S. territories) | USGS National Cooperative Geologic Mapping Program / NGMDB — public domain. Product: [DR-1210](https://ngmdb.usgs.gov/Prodesc/proddesc_118545.htm); viewer: [National Geology](https://ngmdb.usgs.gov/nationalgeology/); Earth’s surface data [doi:10.5066/P146VGVM](https://doi.org/10.5066/P146VGVM). **Not a replacement for SGMC** — keep both toggles. |
| Elevation contours | Overlay | TNM `contours` WMS | USGS The National Map — Contours |

**Honesty:** “LiDAR terrain” in the UI means hillshade from elevation models that include airborne LiDAR where 3DEP collected it. The app does not stream LAS/LAZ point clouds.

Layer panel groups: **Terrain / Geology / Claims / Localities / Hypotheses**. Claims, localities, rivers, trails, and hypotheses are local SQLite markers — they work offline.

---

## Update procedure (operators)

1. Download current USGS zips/CSVs into `data/usgs/` and/or BLM GeoJSON into `data/blm/` and/or trail GeoJSON into `data/trails/`.
2. Run `UsgsImport` / `BlmClaimsImport` / `TrailsImport` (or in-app import) — upserts by stable `ExternalId`.
3. Confirm log: upsert counts, counts by state/status, invalid-coordinate samples.
4. Spot-check serials in MLRS reports / live claim lookup.
5. Record changelog note under `docs/` with source versions.

## User contributions

User ratings/notes improve the **local** DB only unless the user explicitly exports/shares. Never upload without consent.
