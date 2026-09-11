# Unbound Rockhound / GeoMineral Trace — STATUS REPORT

**Audit date:** 2026-09-10  
**Scope:** Full project as it stands today (desktop WinUI + Android companion)  
**Phase:** **0 only** — report, no fixes, no Reddit/community Phase 1 work  
**Product version (desktop badge):** v0.6.0  
**Repos:** `F:\Heirloom\GeoMineralTrace` (desktop) · `F:\Heirloom\UnboundRockhound` (Android)

---

## 1. Build & test status

### Desktop (GeoMineralTrace.sln)

| Check | Result |
|-------|--------|
| `dotnet build GeoMineralTrace.sln -c Debug` | **Succeeded** — 0 errors, 0 warnings in this run |
| `dotnet test GeoMineralTrace.sln -c Debug` | **Passed: 130 · Failed: 0 · Skipped: 0** |

Per-assembly totals from this run:

| Assembly | Passed |
|----------|--------|
| GeoMineralTrace.Pipeline.Tests | 50 |
| GeoMineralTrace.Core.Tests | 18 |
| GeoMineralTrace.Solar.Tests | 14 |
| GeoMineralTrace.Hypothesis.Tests | 14 |
| GeoMineralTrace.Claims.Tests | 12 |
| GeoMineralTrace.Rockhounding.Tests | 10 |
| GeoMineralTrace.Evidence.Tests | 8 |
| GeoMineralTrace.Hydrology.Tests | 4 |
| **Total** | **130** |

**Skipped / disabled tests:** None observed in this run (no `Skip` / `Ignore` attributes hit).

**Not covered by automated tests (notable gaps):**

- WinUI UI / WebView2 Map page  
- Supabase live auth / RLS (helpers exist; not executed here — no local `supabase.json`)  
- Trails importer (CLI smoke-ran earlier in development; no dedicated unit test project)  
- Packaging / Inno installer  
- End-to-end Analyze against real YouTube / Instagram streams  

### Android (`F:\Heirloom\UnboundRockhound`)

| Check | Result |
|-------|--------|
| `./gradlew test` | **Not run** — `JAVA_HOME` unset; no `java` on PATH on this host |
| Unit test sources present | Yes — `core`, `sync`, `rockhounding`, `community`, `cloud`, `rockid` |

**Honest status:** Android compile/test health is **unknown on this machine**. Last documented agent note (WORKLOG 2026-09-02) also could not run Gradle for the same reason.

---

## 2. Feature inventory

Legend: **Done** = usable end-to-end · **Partial** = real code but incomplete / demo / gated · **Stub** = placeholder or intentional no-op · **Untested** = little or no automated coverage relative to risk.

### Desktop modules

| Module | Status | Notes |
|--------|--------|-------|
| **Core** | Done | Geo models, branding, app data paths, deep-analysis options, social profile models. Tests: 18. |
| **Solar** | Done | Position calculator + locus engine; Solar / Shadow page with measure canvas. Tests: 14. |
| **Evidence** | Done | Evidence board store + scoring / cluster selection for Deep Analysis. UI: Evidence Board page. Tests: 8. |
| **Hypothesis** | Done | Fusion engine + presentation helpers; Hypotheses page with map focus / trip actions. Tests: 14. |
| **Pipeline (Analyze)** | Done / Partial | Keyframes (ffmpeg), OCR (Windows Media / sidecar), ASR (Whisper CLI / sidecar), scene tags, place+mineral NER (`PlaceAndMineralExtractor`), YouTube/Instagram ingest (yt-dlp + optional signed-in cookies). Depends on host tooling (ffmpeg / yt-dlp / whisper). Tests: 50. Deep Analysis is a **manual second pass** after initial analyze — implemented, not automatic. |
| **Deep Analysis pass** | Done | `DeepAnalysisPass` + button on Analyze; cluster → hypothesis with reasoning. No dedicated UI “settings” for thresholds beyond code defaults. |
| **Rockhounding DB** | Done | Localities SQLite, CSV/USGS importers, ratings, ownership enrich, Public Mines + Rockhounding pages. Home-radius filtering when State empty. **This machine:** ~267,299 localities. |
| **Claims DB** | Done | BLM MLRS import, nearby cross-links, Claims page. Demo-only sanity warnings when DB tiny. **This machine:** ~666,937 `mining_claims`. Freshness: file mtime ~2026-08-27. |
| **Hydrology (Rivers)** | Partial | Store + NHD GeoJSON import + mineral curated/proximity enrich + Rivers page. **This machine:** only **5** watercourses / 6 mineral links — national NHD not imported here (seed-scale). |
| **Trails** | Partial | Model, store, GeoJSON importer, Map layer, CLI `TrailsImport`, seed sample. **This machine:** **8** seed trails. No dedicated Trails nav page (import via Map). KML export comment still says trails reserved — **stale**. |
| **Rumoured sites** | Partial | Store + importer + Map layer. **This machine:** **0** rows (table empty). |
| **Personal finds** | Partial | Store + Map layer. **This machine:** **3** finds. |
| **Map** | Done / Partial | Leaflet WebView2; layers for hypotheses, solar, curated/USGS localities, claims, rumoured, rivers, trails, personal finds; home pin + searchable radius prefs; Google Earth / Maps actions; trail import button. Online tiles require Settings consent. Campgrounds not a live layer. |
| **Trip creator** | Done | `TripStore` + Trips page; waypoints from hypotheses; map overlay. Persists `trips.json`. |
| **Analysis history** | Done | History page + focus map / open case. Persists under LocalAppData. |
| **Cases / Reports / Techniques KB** | Done | Cases list, report export, techniques knowledge page. |
| **Auth / Profiles (Supabase Phase 1)** | Partial | WinUI GoTrue/PostgREST/Storage clients; SignIn / Profile / ProfileEdit; Settings cloud card; DPAPI session. **Migration SQL in repo.** Live project config **not present** on this host (`supabase.json` missing). **Two-account RLS gate still unchecked** in WORKLOG. |
| **Forum (desktop)** | Stub / Not started | No Forum nav item. Social Phase 2 explicitly “Not started” in `docs/social-layer.md`. |
| **Home location & radius** | Done | Settings: state centroids / lat-lon / miles radius (default 100). Wired into Map + Rivers/Claims/Rockhounding list scoping. |
| **Updates / packaging** | Partial | Update feed check + banner; `Package-Release.ps1`; GitHub release used for Base44 download. Feed URL is productized stub. |

### Android modules (from tree + QUALITY_REPORT.md; build not re-verified)

| Module | Status | Notes |
|--------|--------|-------|
| **core** | Done | Domain models including forum types; serialization tests present. |
| **rockhounding** | Done | Room trips / waypoints / localities / campgrounds; migrations. |
| **community** | Done (local-only) | Offline Room forum: categories, threads, replies, likes; demo seed. **Not** Supabase-backed. |
| **cloud** | Partial | Supabase auth/profile OkHttp clients + `RlsProbe`; Settings config. Gradle not run here. |
| **sync** | Done | Rockpack importer. |
| **maps** | Partial | MapLibre trip map + land/geology chips — not the desktop national claims/rivers stack. |
| **rockid** | Partial | On-device identifier tests; TFLite may be absent; `CloudVisionRockIdentifier` unwired per QUALITY_REPORT. |
| **terrain / landlayer / geology** | Partial | Feature modules; terrain segmenter stub returns null per QUALITY_REPORT. |
| **app shell** | Done | Bottom nav: Trips · Community · Identify · Profile (+ settings/cloud). |

---

## 3. Known issues / fragile spots

Logged only — **not fixed in this phase**.

1. **Social Phase 1 manual gate incomplete** — WORKLOG still lists Apply SQL / two accounts / raw PATCH denial as **User** / not run. Until that passes, treat cloud profiles as **unverified in production**.
2. **No `TODO`/`FIXME`/`HACK` markers** in desktop `src/` from ripgrep — issues are mostly structural, not annotated.
3. **Android cannot be built on this audit host** (no JDK) — regression risk unknown.
4. **Rivers DB on this machine is seed-scale (5 rows)** while localities/claims are national — Map “near me” rivers will look empty for most U.S. homes until NHD import.
5. **Rumoured DB empty** on this machine — rumoured layer will show nothing until seed/import.
6. **Trails national coverage missing** — only demo/seed corridors; importer ready but operator still must download TNM/USFS GeoJSON.
7. **KML catalog stale** — `KmlCatalogBuilder` still documents Trails/Campgrounds as reserved; Campgrounds folder empty; trails not exported despite Map support.
8. **Pipeline external tool dependency** — Analyze quality collapses without ffmpeg / yt-dlp / optional Whisper; Settings surfaces this but users can miss it.
9. **Home-radius Rockhounding behavior** — empty State uses near-home; default State box was changed to empty — users who expect Oregon default may be surprised.
10. **Claims NumberBox still labeled km** while Settings radius is miles — nearby-claims control is auto-seeded from miles→km but UX is mixed units.
11. **DPAPI CA1416 warnings** appear on some builds (Windows-only APIs in multi-TFM Infrastructure) — previously seen; this solution build was clean but risk remains.
12. **Forum split-brain** — Android has offline local forum; desktop has none; cloud Phase 2 forum not started. Easy to confuse “Community” on Android with future Supabase forum.
13. **Update feed / download URL** — commercial packaging points at product URLs; ensure production feed matches GitHub release reality.
14. **Fragile Map catalog limits** — FindNear with 100 mi can return large claim/locality sets (capped in Map helpers); performance on low-end machines not profiled.

---

## 4. Data / schema state

### Supabase (repo migration only)

Single migration: `supabase/migrations/20260903_phase1_profiles.sql`

| Object | Detail |
|--------|--------|
| `public.profiles` | `id` → `auth.users`, display_name, handle (unique regex), bio, avatar_url, home_region, timestamps |
| Trigger | `on_auth_user_created` → insert profile |
| RLS | SELECT all authenticated; INSERT/UPDATE/DELETE own row only (`auth.uid() = id`) |
| Storage | Bucket `avatars` public read for authenticated; write only under `{uid}/…` |

**No later migrations** in repo (no forum tables, no reddit tables, no location shares).

**Live project:** This audit host has **no** `%LocalAppData%\UnboundRockhound\supabase.json` — cannot confirm remote schema was applied or matches file.

### Local SQLite (this host, `%LocalAppData%\UnboundRockhound`)

| DB | Approx size | Row counts (this host) | Notes |
|----|-------------|------------------------|-------|
| `localities.db` | ~589 MB | **267,299** localities · 0 user_ratings | USGS/national import present |
| `claims.db` | ~554 MB | **666,937** mining_claims | Large BLM import; mtime 2026-08-27 |
| `rivers.db` | ~36 KB | **5** watercourses · 6 river_minerals | Seed only |
| `trails.db` | ~24 KB | **8** trails | Seed / TrailsImport sample |
| `rumoured.db` | ~20 KB | **0** | Empty |
| `finds.db` | ~12 KB | **3** | Personal finds |
| `trips.json` | tiny | present | Trip store |

Claims “freshness”: local file age ~2 weeks as of audit date; BLM source vintage is whatever was imported Aug 27 — not re-fetched during audit.

---

## 5. Security check (Phase 1 RLS)

### What was reviewed

- Migration SQL: UPDATE policy uses `using (auth.uid() = id)` **and** `with check (auth.uid() = id)` — correct pattern to block cross-user PATCH.
- Client: `SocialAuthService.PatchProfileAsync` / `RawPatchProfileAsync` hit PostgREST with user JWT (anon key + bearer) — no service-role key in client path.
- Helper script `scripts/verify-phase1-rls.ps1` and Android `RlsProbe` implement the raw PATCH A→B check.

### What was **not** re-executed live

- No Supabase credentials on this host → **did not** run two-account SELECT/PATCH proof against a real project.
- WORKLOG still marks the manual gate incomplete.

### Assessment given code since Phase 1

- **No new Supabase tables/policies** appear in-repo since Phase 1 migration — nothing in-tree obviously weakens profiles RLS.
- Risk remains if the **deployed** project diverged (extra policies, disabled RLS, or service role leaked into a client). That cannot be confirmed from filesystem alone.
- **Verdict:** Policy *as coded* still looks sound; **production verification is still open**. Do not treat “RLS still holds” as confirmed until `verify-phase1-rls.ps1` (or equivalent) is run against the live project.

---

## 6. Mobile / desktop parity

| Capability | Desktop (WinUI) | Android |
|------------|-----------------|---------|
| Video analyze pipeline | Yes | No (field app focus) |
| National localities / claims / rivers DBs | Yes (offline SQLite) | Localities via rockpack import; no national claims/rivers stack |
| Map | Leaflet + many layers | MapLibre trip-scoped |
| Trips / waypoints | Yes | Yes (Room) |
| Offline forum | No | Yes (Room + seed) |
| Cloud auth / profiles | Yes | Yes (`:cloud`) |
| Cloud forum / Reddit | No | No |
| Rock ID / terrain ML | N/A | Partial / stubs |
| Home location + mi radius prefs | Yes | Not mirrored in Settings (cloud region string only) |
| Trails import | Yes | No |
| Reports / Deep Analysis / Solar | Yes | No |

**Bottom line:** Desktop is the research/forensics + national GIS workstation. Android is the field companion (trips, offline community, ID experiments, cloud profile). They share Supabase profile identity when configured; they do **not** share forum or map data models.

---

## 7. Open questions

1. Has the Phase 1 SQL migration actually been applied to the production Supabase project used by UnboundInfotech.com buyers?
2. Did anyone complete the two-account RLS script and log results after WORKLOG’s “Stop” note?
3. Should Android Community stay offline-only until desktop/cloud forum (social Phase 2), or should Reddit Phase 1 (per the attached command) land first on one platform only?
4. Is the intended primary trails source USGS TNM, USFS, or curated rockhound access corridors? (Importer accepts all; product messaging is vague.)
5. Should rivers/trails national extracts ship inside the store zip, or remain operator-import only? (Current zip size already constrained Base44 → GitHub Releases.)
6. For upcoming Reddit integration: is desktop Forum UI in scope, Android Community tab only, or both?
7. KML Campgrounds — keep reserved forever, or wire Android rockpack campgrounds into desktop export?
8. Home location: should Profile `home_region` text auto-geocode into Settings lat/lon, or remain separate on purpose?

---

## Audit constraints / method notes

- Build + tests run on 2026-09-10 from `F:\Heirloom\GeoMineralTrace`.
- Android Gradle blocked by missing JDK on audit host.
- Live Supabase RLS not executed (no local credentials).
- DB counts are from **this developer machine’s** LocalAppData — other installs may differ.
- Phase 1 (Reddit / Facebook directory / community map layer) from `audit_and_reddit_command.md` was **not started**, per instructions.

---

## Recommended next actions (for humans reviewing this report)

1. Run `scripts/verify-phase1-rls.ps1` against production and paste results into WORKLOG.  
2. Decide forum host platform before Reddit Phase 1.  
3. Import NHD + trails GeoJSON for any demo machine that should look “full” within 100 mi.  
4. Set `JAVA_HOME` and re-run Android `./gradlew test` before calling mobile “green.”  
5. Only after review, kick off **Phase 1 — Reddit discovery integration** as a separate command.
