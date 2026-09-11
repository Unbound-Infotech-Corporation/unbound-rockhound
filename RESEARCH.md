# GeoMineral Trace — Competitive Research Notes

*Phase 2 overnight pass — structured notes for Phase 3 inspiration. Not a feature spec.*

---

## 1. Forensic video geolocation / OSINT

**Sources:** [OSINTBench video guide](https://osintbench.com/guides/video-osint-investigation/), [EBU chronolocation](https://spotlight.ebu.ch/p/mastering-video-verification-and), [Bellingcat ShadowFinder](https://github.com/bellingcat/ShadowFinder)

### Worth stealing

1. **Preserve → extract → geolocate → chronolocate → verify loop** — Each stage produces artifacts (hash, EXIF, keyframes, shadow math, satellite cross-check). GeoMineral Trace already mirrors this; surface stage completion visibly on Analyze (progress + per-stage evidence counts).

2. **Shadow bands on map, not point fixes** — ShadowFinder shows a *bright band* of possible latitudes. Our `SolarLocusEngine` already does this; UX should never imply a single pin unless azimuth + multiple frames constrain it.

3. **Frame-by-frame landmark anchoring** — Investigators scrub to a frame with a fixed feature (sign, peak, intersection), then match in satellite imagery. **Adapt:** click keyframe thumbnail → seek video to that timestamp + open map centered on fused hypothesis (desktop already has keyframe strip; tie seek + map).

---

## 2. NLE timeline / scrubber (DaVinci Resolve, Premiere)

**Sources:** [Resolve Timeline View Options](https://www.steakunderwater.com/VFXPedia/__man/Resolve18-6/DaVinciResolve18_Manual_files/part842.htm), [PremiumBeat Resolve 16 changes](https://www.premiumbeat.com/blog/changes-edit-page-davinci-resolve-16/)

### Worth stealing

1. **Full-height waveform with playhead-attached “active” coloring** — Bars before playhead use accent color; after use muted fill. We had partial implementation; extend with evidence tick marks at timestamp positions.

2. **Drag-to-scrub with optional audio scrub** — Resolve’s tape-style scrub (Shift-S) helps find spoken place names. **Adapt:** pointer-drag on scrubber track (not just click); optional short audio burst while dragging when local preview loaded.

3. **Fixed playhead vs moving timeline** — For long clips, keeping playhead centered while timeline pans reduces disorientation. **Defer** full implementation; short-term: show `HH:MM:SS.mmm / duration` readout on scrub (already have timecode elsewhere — unify on timeline header).

---

## 3. GIS / outdoor mapping (Gaia, onX, CalTopo)

**Sources:** [DroidLore 2026 comparison](https://droidlore.com/hiking/hiking-apps-backpacking), [Granite Alpine Lab CalTopo vs onX](https://granitealpinelab.com/apps-caltopo-vs-onx-backcountry/), [PMags 2026 workflow](https://pmags.com/digital-mapping-tools-my-use-in-2026)

### Worth stealing

1. **Desktop plan → mobile field package** — CalTopo/Gaia split: heavy planning on desktop, offline region download for field. **Direct fit:** `.rockpack` trip export with offline map bounds + waypoints (already on Android roadmap).

2. **Layer opacity sliders + parcel/land overlay** — onX land-ownership tint at a glance. **Adapt:** claims layer on map with Active vs ExpiringSoon color coding (desktop Claims + Map cross-link).

3. **Waypoint typed categories** — Gaia distinguishes campsite, water, hazard. **Adapt:** Unbound Rockhound waypoint `kind` enum (camp, dig site, parking) in trip UI filters.

---

## 4. Mining claim lookup (BLM MLRS / LR2000)

**Sources:** [reports.blm.gov MLRS](https://reports.blm.gov/reports/MLRS/), [BLM MLRS overview](https://www.blm.gov/services/land-records/mlrs), [Western Mining History how-to](https://westernmininghistory.com/3729/researching-mining-claims-with-the-blm-mlrs/), NV MLRS PDF disposition defs

### Worth stealing

1. **Geographic Report by PLSS (MTRS)** — BLM’s primary public search: meridian, township, range, section + Active/Closed disposition. **Adapt:** Claims search by PLSS fields (we have legal description parsed) not just state/mineral filter.

2. **Serial Register Page (SRP) detail view** — Single claim “source of truth” panel with disposition, maintenance fee history, case actions. **Adapt:** Claims detail drawer with `BlmCaseDisposition`, maintenance deadline advisory, link to MLRS SRP URL template.

3. **Explicit “demo vs production dataset” banner** — MLRS web UI is slow but authoritative; third-party tools must show data vintage. **Implemented this pass:** `ClaimImportSanity` + ClaimsPage warning when total &lt; 1,000.

---

## 5. Rock / mineral ID & collecting (Rockd, Mindat)

**Sources:** [Rockd](https://rockd.org/), [Macrostrat/Rockd GSA abstract](https://gsa.confex.com/gsa/2018AM/webprogram/Paper316620.html)

### Worth stealing

1. **Location dashboard** — Rockd distills “what am I standing on?” into one card (unit, age, minerals). **Adapt:** locality detail sheet on Android: county, access status, reported minerals, rating — one scrollable card after map pin tap.

2. **Check-in / find log with share URL** — Field observations get permanent links. **Adapt:** personal find record with photo + optional public share (community module stub → Pro feature).

3. **Offline-tolerant observation capture** — Rockd tags strat names offline, syncs later. **Adapt:** Android Room queue for finds/waypoints when sync module enabled.

---

## 6. Trip planning / send-to-mobile (AllTrails pattern)

**Sources:** [DroidLore AllTrails vs Gaia](https://droidlore.com/hiking/hiking-apps-backpacking)

### Worth stealing

1. **Trip card with distance/elevation/mineral tags** — AllTrails cards show difficulty, length, conditions at a glance. **Adapt:** desktop trip package summary before export: N waypoints, M localities, claims overlap warning count.

2. **One-tap “send to phone”** — QR or file share for `.rockpack`. **Adapt:** export dialog with “Save” + “Reveal in Explorer” + copy path for ADB push / cloud drop.

3. **Conditions / recency badge** — “Updated 3 days ago” on trail intel. **Adapt:** `SourceVintage` + `HumanActivityDataMayBeOutdated` flags on locality hits (Core model exists — show in UI).

---

## Phase 3 picks (implemented or queued)

| Priority | Feature | Module | Status this pass |
|----------|---------|--------|------------------|
| P0 | Solar locus in pipeline + RefuseAsync | Solar/Pipeline | **Done** |
| P0 | Claims import sanity + demo warning | Claims/App | **Done** |
| P1 | Timeline drag-scrub + evidence markers | App (UI only) | **Done** |
| P1 | Timecode on timeline header during video preview | App | **Done** |
| P2 | PLSS/MTRS claims search field | Claims/App | Deferred |
| P2 | Keyframe click → seek local preview | App | Partial (existing thumbnail click) |
| P3 | Audio scrub while dragging | App | Deferred |

---

## Deep Analysis Scoring Rubric (inspectable / tunable)

Implemented in `GeoMineralTrace.Evidence.Scoring` + `DeepAnalysisOptions` (Core).  
**Do not treat this as a black box** — weights live on `DeepAnalysisOptions` and can be changed without a rebuild via DI.

### Dimensions (each 0–1)

| Dimension | Meaning | How derived |
|-----------|---------|-------------|
| **SourceReliability (SR)** | Extractor quality | `EvidenceItem.Confidence.Value`, boosted for Accepted / softened for Downweighted |
| **Corroboration (C)** | Cross-modal agreement | Distinct modalities sharing the same implied-location key: 1→0.25, 2→0.55, 3→0.80, 4+→1.0 |
| **Specificity (S)** | How pinpoint the cue is | GPS/address≈1.0 → landmark≈0.85 → city≈0.65 → highway≈0.55 → mineral≈0.40 → state≈0.35 → terrain≈0.18 |
| **Consistency (Con)** | Agreement with majority context | Same key as majority high-conf evidence →1.0; conflicting places →0.15–0.45; mineral/solar soft →0.70 |
| **Plausibility (P)** | Solar temporal/spatial sanity | Non-solar default 0.75; solar elev 5–75° →1.0; elev &lt;2° or &gt;88° →0.05 |

### Combined formula

```
combined =
  wSR  * SourceReliability +
  wC   * Corroboration +
  wS   * Specificity +
  wCon * Consistency +
  wP   * Plausibility
```

**Default weights:** `wSR=0.20`, `wC=0.35`, `wS=0.20`, `wCon=0.15`, `wP=0.10` (renormalized if they don't sum to 1).

Corroboration is weighted highest on purpose — multi-modal agreement is the strongest geolocation signal we have.

### Selection (not top-N)

1. Drop items below `MinimumItemConfidence` (default **0.25**).
2. Cluster by implied-location key (`place:…`, `gps:…`, `state:…`, `mineral:…`, `solar:locus`).
3. Merge mineral/solar satellite clusters into the strongest place/gps anchor.
4. Cluster score ≈ `avg(item) × √(modalities)` with a multi-modal boost.
5. Keep up to `MaxClusters` (default **3**) above `MinimumClusterConfidence` (default **0.35**).
6. Non-selected items stay on the board as **“considered, not selected”** with score + reason.

### Hypothesis generation

`DeepHypothesisGenerator` (Hypothesis module) runs `HypothesisFusionEngine.Fuse` per selected cluster, labels hypotheses `Deep: …`, and attaches `Reasoning` lines explaining which evidence and why. Nearby rockhounding cross-link reuses the existing session path.

### UX

Manual **Deep Analysis** button on Analyze (enabled after initial pipeline). Progress stages: `deep-score` → `deep-select` → `deep-fuse` → `done`. Does not silently block the UI mid-pipeline.
