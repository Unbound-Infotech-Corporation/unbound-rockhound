# GeoMineral Trace ó Overnight Worklog

## Summary (updated 2026-09-02 ~12:30 PT)

| Area | Status |
|------|--------|
| Deep Analysis pass | **Implemented** ó scoring rubric, cluster selection, hypothesis reasoning, Analyze button |
| Unit/integration tests | **Green** ó Evidence(7) + Deep hyp(1) + Deep pipeline(1); full suite ~117+ |
| Real-video verification (Step 6) | **Not done by agent** ó needs you to run Deep Analysis on known YouTube cases |

**New this pass:** Deep Analysis (Evidence scoring + cluster selection ? Hypothesis with Reasoning). Manual button on Analyze after initial pipeline. Thresholds on `DeepAnalysisOptions`.

**Still needs you:** Compare auto hypotheses vs manual judgment on 2ñ3 known videos; if wrong/overconfident, tune weights ó do not treat feature as production-trusted until that check.

---

## 2026-09-02 02:58 PT ó Phase 0 baseline start

- Repo: `F:\Heirloom\GeoMineralTrace`
- Prior session recorded clean build (0 warnings, 0 errors) and 48/48 tests ◊2 with no flakes.

## 2026-09-02 03:05 PT ó Phase 0 clean build

```
dotnet build GeoMineralTrace.sln ? 0 Warning(s), 0 Error(s)
```

## 2026-09-02 03:06 PT ó Phase 0 test suite (post-fixes)

```
dotnet test GeoMineralTrace.sln ? Passed: 108, Failed: 0
Projects: Core(10), Solar(14), Hypothesis(9), Claims(12), Rockhounding(10), Hydrology(4), Pipeline(49)
```

## 2026-09-02 03:07 PT ó Phase 0 manual exercise (ground truth)

**Not executed by agent** (headless environment; WinUI app requires interactive desktop):

| Flow | Agent status | Code-path notes |
|------|--------------|-----------------|
| Analyze page load | Not run | `AnalysisPage.xaml` loads player + timeline on `Loaded` |
| YouTube ingest (Stream & analyze) | Not run | `YouTubeClipPanel` + `YouTubeMediaFetcher` |
| Solar/shadow computation | Not run | `SolarAnalysisPage` manual; now also pipeline/RefuseAsync |
| Evidence Board | Not run | `EvidenceBoardPage` + `EvidenceBoardStore` |
| Hypothesis fusion | Not run | `HypothesisFusionEngine` via pipeline/RefuseAsync |
| Rockhounding DB search | Not run | `LocalityStore` |
| Claims DB search | Not run | `ClaimStore` ó expect ~50 if only demo+NV partial |
| Map render | Not run | Map page |
| Report export | Not run | `ResearchReportExporter` |

**User should verify manually** before trusting overnight changes in production use.

---

## Phase 1 ó Debug verification notes

### 1.1 SOURCE-NER contamination

**Reproduction attempt:** Ran `SourceNerScopingTests` (4 tests) ó all pass.

**Evidence:**
- `RemoteDescriptionSanitizer.SanitizeDescription` strips related-video URLs, PO box, subscribe CTAs
- `BuildNerCorpus` scopes title/description/tags per video
- `PlaceAndMineralExtractor.IsPlausiblePlacePhrase` rejects "Unbelievable Find", "Found Rare Amethyst"
- Pipeline uses sanitizer in `ExtractSourceLeadsAsync` (`AnalysisPipeline.cs`)

**Verdict:** Mitigations present and tested. **Not a live regression** in unit tests. Residual risk: new YouTube description patterns not covered by sanitizer ó monitor Evidence Board for place leads containing `youtu.be` or unrelated video titles.

### 1.2 Solar not wired into automated pipeline

**Before:** `AnalysisPipeline.cs` explicitly skipped SOLAR stage and called `Fuse(..., solarLocus: null)`.

**Fix applied:**
- Added `ShadowMeasurementParser`, `SolarLocusFromEvidence` in `GeoMineralTrace.Solar`
- Pipeline preserves prior-board shadow measurements for matching media paths, computes locus before fusion
- `AnalysisSessionContext.RefuseAsync` auto-computes locus from shadow evidence when `LastSolarLocus` is null
- Tests: `SolarLocusFromEvidenceTests`, `SolarPipelineIntegrationTests`

**Verdict:** **Fixed and regression-tested.** Automated shadow detection (CV) still future work ó locus runs when user saved shadow measurements exist.

### 1.3 Claims dataset count (~50 active / 1 expiring)

**Investigation:**
- `DemoClaims.Create()` ? **4** demo claims (Active, ExpiringSoon, Lapsed, MillSite)
- Runtime DB: `%LocalAppData%\GeoMineralTrace\claims.db`
- Repo data: `data/seed/blm-claims-sample.csv`, `data/blm/nv-mlrs-claims.geojson` only
- `BlmClaimsImport --download-national` exists but was **not run** this session (large download)

**Fix applied:**
- `ClaimImportSanity` ó throws if `us-*.geojson` national import upserts < 5,000 rows
- `ClaimsPage` shows prominent warning when total < 1,000
- Tests: `ClaimImportSanityTests`

**Verdict:** ~50 count is **expected without national import**, not a silent truncation bug in the importer logic. Sanity check will fail loudly on incomplete national imports going forward.

### 1.4 Pipeline exception sweep

**Reviewed catch sites:**
- `AnalysisPipeline.cs:654` ó marks session Failed, persists partial evidence, rethrows
- `YouTubeMediaFetcher.cs:467` ó wraps yt-dlp failures with context
- `MediaMetadataExtractor.cs:95` ó logs and continues (metadata optional)
- Per-stage OCR/ASR failures add `EvidenceType` diagnostic rows rather than aborting whole run

**Verdict:** No new fixes required this pass. Long YouTube/Whisper paths degrade gracefully with diagnostic evidence; session status reflects hard failures.

---

## 2026-09-02 03:12 PT ó Phase 1 code changes committed to working tree

Files touched: `SolarLocusFromEvidence.cs`, `ShadowMeasurementParser.cs`, `ClaimImportSanity.cs`, `AnalysisPipeline.cs`, `AnalysisSessionContext.cs`, `BlmMlrsClaimImporter.cs`, `ClaimsPage.xaml.cs`, + test files.

## 2026-09-02 03:14 PT ó Phase 1 exit criteria check

- [x] Full test suite green (108/108)
- [x] Written verification notes for all four Phase 1 items
- [ ] Manual Analyze on real YouTube end-to-end **blocked for agent** ó user verification required

---

## Phase 2+ entries

### 2026-09-02 03:20 PT ó Phase 2 competitive research

Created `RESEARCH.md` with structured notes across OSINT geolocation, NLE timelines, outdoor GIS, BLM MLRS, Rockd/Mindat, and trip-planning apps. Each section lists 2ñ3 concrete UX patterns to adapt.

### 2026-09-02 03:25 PT ó Phase 3 timeline polish

- Increased waveform/scrubber dimensions and opacity (`AnalysisPlayer.xaml`, `Analysis.xaml` tokens)
- Drag-to-scrub with pointer capture on timeline track
- Evidence markers on waveform (color by type: solar/keyframe/GPS/NER)
- Timeline header shows `position / duration` when local video preview loaded
- Refreshed markers after keyframe strip updates

### 2026-09-02 03:28 PT ó Phase 4 Android parity

- Attempted `gradlew test assembleDebug` at `F:\Heirloom\UnboundRockhound`
- **Blocked:** `JAVA_HOME` not set / no `java` on PATH in agent shell
- **Rockpack compatibility:** No shared model changes in Phase 3; Android `RockpackManifest` fields unchanged
- User should run build in Android Studio: `.\gradlew test assembleDebug`

### 2026-09-02 03:30 PT ó Phase 5 final verification (desktop)

```
dotnet build GeoMineralTrace.sln ? 0 Warning(s), 0 Error(s)
dotnet test GeoMineralTrace.sln ? Passed: 108, Failed: 0
```

Manual Analyze YouTube E2E: **not run** (same limitation as Phase 0).

---

## 2026-09-02 12:15 PT ó Deep Analysis pass (Steps 1ñ5)

### Step 1 ó Rubric (documented before/with code)
- Documented in `RESEARCH.md` ß Deep Analysis Scoring Rubric and `EvidenceScoreBreakdown` XML comments.
- Formula: `combined = 0.20∑SR + 0.35∑C + 0.20∑S + 0.15∑Con + 0.10∑P` (tunable via `DeepAnalysisOptions`).

### Step 2 ó Evidence module
- `EvidenceScorer`, `EvidenceClusterSelector`, `ImpliedLocationKey` under `src/GeoMineralTrace.Evidence/Scoring/`
- Non-selected items annotated: `DeepSelected=false`, `DeepSelectionNote="considered, not selected: Ö"`
- Thresholds: `MinimumItemConfidence`, `MinimumClusterConfidence`, `MaxClusters` on options singleton

### Step 3 ó Hypothesis module
- `DeepHypothesisGenerator` ? one hyp per selected cluster, `Label` prefixed `Deep:`, `Reasoning` list with corroboration notes
- Reuses `HypothesisFusionEngine.Fuse`; nearby localities via existing `RunDeepAnalysisAsync` cross-link

### Step 4 ó Wire-up
- `DeepAnalysisPass` (Pipeline) + `AnalysisSessionContext.RunDeepAnalysisAsync`
- Analyze page: **Deep Analysis** button (manual, post-pipeline; progress stages, non-blocking cancel via same Cancel)
- Evidence Board shows deep score / selected flag; Hypotheses list shows Reasoning

### Step 5 ó Tests
- Evidence.Tests: scoring (corroborating high, contradictory low, single-modality moderate, implausible solar, weight config) + cluster selection
- Hypothesis.Tests: deep generator reasoning
- Pipeline.Tests: sidecar image ? pipeline ? deep pass integration
- Full suite green after App rebuild (App was file-locked by running process; killed PID 43152 and rebuilt)

### Step 6 ó Real-video verification
- **Blocked for agent** (needs interactive WinUI + known expected locations).
- Procedure for user:
  1. Run analysis on 2ñ3 known YouTube clips
  2. Click **Deep Analysis**
  3. Compare top Deep: hypothesis vs your manual conclusion
  4. Log pass/fail in this WORKLOG; if overconfident/wrong ? lower `MinimumClusterConfidence` or raise corroboration weight further
- **Feature not marked production-done** until that comparison is logged.

---

## 2026-09-02 13:15 PT ó Yellowstone River short (`p5qNjeU7eXQ`) E2E + over-optimize

**Ground truth:** YouTube Short https://www.youtube.com/shorts/p5qNjeU7eXQ ó title *"Yellowstone River agate hunting 2025Ö #montana #agateÖ"* (Rocky Mountain Rock Hounds).

### Baseline (before fixes)
| Stage | Outcome |
|-------|---------|
| SOURCE-NER | Correctly extracted `Yellowstone River`, `montana`, `agate` |
| Fusion #1 | **Montana state centroid** (46.88, ?110.36) ó tie with river at 31% |
| Fusion #2 | "Yellowstone River" mapped to **NP** (44.60, ?110.50) via substring `yellowstone` |
| Deep Analysis | Not run in CLI; clustering later preferred garbage `place:metadata parse limited` |

### Thought-process audit

**Strengths**
- Title/hashtag NER + geo-cue heuristic (`river`) already surface the right place name without ASR/OCR
- Sidecar `.gmt-source.json` is the critical ingest path for YouTube text
- Deep Analysis design (score ? cluster ? reasoned hyp) is the right place to fuse state + mineral + landmark

**Weaknesses (fixed this pass)**
1. Gazetteer: `Yellowstone River` ? `yellowstone` ? park coords; no river corridor entry
2. Ranking: state vs landmark same confidence; state listed first
3. Clustering: full state names not keyed as `state:MT`; `river` missing from landmark cues
4. Metadata/error strings invented fake `place:` keys and stole satellite merges
5. `Mineral/gem mention: agate` mis-keyed as `place:agate mineral`
6. Place+state same modality (`AsrNer`) ? merged cluster scored below threshold

**Still open**
- Whisper not on PATH ? no ASR corroboration
- CLI OCR is sidecar-only (WinUI OCR is App-only)
- MiningCue spam from heuristic scene tagger (noise, not decisive here)
- River hyp uses Miles City stretch centroid (±120 km corridor) ó not a pin on the exact gravel bar

### After fixes (AnalyzeClip + Deep Analysis)
```
Fusion #1 [71%] Yellowstone River  lat=46.408, lon=-105.840
Deep  #1 [84%] Deep: Ö Yellowstone River  cluster=place:yellowstone river (montana + agate merged)
```

### Code touched
- `PlaceGazetteer` ó `yellowstone river`, Miles City / Glendive / Forsyth
- `HypothesisFusionEngine` ó specificity boost, state/mineral corroboration, tighter ranking
- `ImpliedLocationKey` ó `river`, state-name map, skip Metadata/Keyframe, mineral-first typing
- `EvidenceClusterSelector` ó merge `state:*`, claim-diversity scoring, landmark anchor quality
- `PlaceAndMineralExtractor` ó High confidence for river/creek phrases
- `AnalyzeClip` ó prints Deep Analysis + reasoning
- Tests: gazetteer river?park, fusion ranks river > Montana, cluster merges state+mineral

Hypothesis(14) + Evidence(8) + Deep pipeline(1) tests green.

---

## 2026-09-02 14:05 PT ó In-app update notifications

- Customers get a top banner when hosted `latest.json` version &gt; installed build.
- Settings ? **Software updates** (on by default): Check now, feed URL override, clear dismissed.
- Shared schema for all sellable apps: `docs/app-updates.md` + `updates/geomineral-trace/latest.json`.
- Notify + open download URL (portable-zip friendly). Silent auto-apply (Velopack/Store) left as optional later.
- Core version compare tests green; App Release build OK.

---

## 2026-09-02 16:45 PT ó Hypothesis prominence, map focus, history & trips

### Implemented

| Step | What shipped |
|------|----------------|
| 1 Hero hypothesis | `HypothesesPage` ó dedicated lead card (label, coords, confidence %, reasoning summary, Accept/Reject). Runner-ups in collapsed expander. Full ranked audit list below. |
| 2 View on map | `MapFocusService` + `MainWindow.NavigateTo(..., parameter)`. Map flies to hypothesis; **? lead** (`#E11D48`, r=12) vs runner (`#6366F1`, r=7); popups show confidence + reasoning. Hypothesis pins skip clustering. |
| 3 Analysis history | `AnalysisHistoryStore` ? `%LocalAppData%\UnboundRockhound\analysis-history.json`. Auto-record on `ApplyPipelineResult` when status=Completed. **Analysis History** nav page. |
| 4 Trips | `TripStore` ? `trips.json`. **Trips** nav page. **Add to trip** from hero card. Video-derived waypoints show **From video** badge + **View analysis**. |
| 5 Map polish | River polylines `#0EA5E9`, weight 4.5. Bottom-right legend. Trip waypoints on map (green). |

### Delete behavior (history)

**History remove = index row only.** Session evidence/hypotheses remain under `sessions\{id}\` until deleted from **Cases** (full folder delete). Documented in Analysis History page copy.

### Build / tests

```
dotnet build ? 0 Warning(s), 0 Error(s)
dotnet test  ? Passed: 128, Failed: 0 (all projects)
```

### Step 6 ó Manual verification checklist (user)

| Flow | Expected | Agent |
|------|----------|-------|
| Run Deep Analysis on test video | Hero card shows lead label, %, reasoning | **Not run** (needs desktop) |
| View on map (hero) | Map nav, flyTo, red ? pin, popup | **Not run** |
| Runner-up Map button | Centers on alternate, smaller indigo pin | **Not run** |
| Analysis History | Entry appears after completed run | **Not run** |
| History ? View on map | Opens case + focuses map | **Not run** |
| History ? Remove | Row gone; Cases still has session | **Not run** |
| Add to trip | Waypoint on Trips with video badge | **Not run** |
| Trip ? View analysis | Opens Hypotheses for source session | **Not run** |
| Restart app | History + trips persist (`analysis-history.json`, `trips.json`) | **Not run** |
| Map rivers toggle | Layer on/off; cyan lines readable at z?8ñ14 | **Not run** |

**Key new files:** `HypothesisPresentation.cs`, `AnalysisHistoryEntry.cs`, `AnalysisHistoryStore.cs`, `TripStore.cs`, `Trip.cs`, `Waypoint.cs`, `MapFocusService.cs`, `TripActions.cs`, `AnalysisHistoryPage.*`, `TripsPage.*`.

---

## 2026-09-02 ~21:30 PT ó Social Phase 1 (auth + profiles)

### Shipped

| Surface | What |
|---------|------|
| Docs | `docs/social-layer.md` ó locked privacy/news/moderation + Phase 1 checklist |
| SQL | `supabase/migrations/20260903_phase1_profiles.sql` ó profiles, trigger, RLS, avatars Storage |
| WinUI | HttpClient GoTrue/PostgREST/Storage; DPAPI session; SignIn / Profile / ProfileEdit; Settings CLOUD ACCOUNT; session restore on launch |
| Android | `:cloud` module (OkHttp REST); Sign-in UI; Settings account; bridge local `UserProfile` ? cloud on sign-in; avatar upload; handle/UUID lookup |
| RLS helpers | `scripts/verify-phase1-rls.ps1`, Android `RlsProbe`, Core `SocialModelsTests` (2 passed) |

### Build

```
dotnet build GeoMineralTrace.App (x64) ? succeeded (DPAPI CA1416 warnings only)
dotnet test SocialModelsTests ? Passed: 2
Android Gradle ? not run here (no JDK/JAVA_HOME on agent host)
```

### Phase 1 verification gate (must pass before Phase 2)

| Check | Agent | Notes |
|-------|-------|-------|
| Apply SQL migration in Supabase | **User** | Dashboard SQL editor or CLI |
| Configure URL + anon key | **User** | WinUI `supabase.json` / Settings; Android `local.properties` or Settings |
| Create accounts A and B | **User** | Email/password on WinUI and/or Android |
| Each opens the other's Profile | **User** | Lookup by handle or UUID |
| Neither edits the other's profile in UI | **User** | Edit only when owner |
| Raw REST PATCH A?B denied | **User** | Run `scripts/verify-phase1-rls.ps1` with env vars |
| Sign out / restart restores or clears session | **User** | |

**Stop.** Do not start forum/news until Phase 2 command is pasted. Manual two-account + RLS script results should be appended here when done.

---

## 2026-09-03 ó Unbound Infotech store wrap (v0.6.0)

### Packaging / branding

- Publisher set to **Unbound Infotech Corporation** (`AppBranding`, Package.appxmanifest, Inno Setup, EULA, About)
- Support default: `support@unboundinfotech.com` ∑ Web: https://unboundinfotech.com/
- Store listing copy: `docs/store-listing.md`
- Commercial checklist refreshed: `docs/commercial-release.md`
- Update feed stub: `updates/unbound-rockhound/latest.json`
- EULA updated for optional Supabase cloud accounts

### Distribute

```
.\scripts\Package-Release.ps1 -Version 0.6.0 -SkipInstallSync
? dist\UnboundRockhound-Windows-x64-v0.6.0.zip
```

Upload that zip (and optional Setup exe) to the Unbound Infotech store download slot using `docs/store-listing.md`.


---

## 2026-09-10 ù Phase A: RLS verification against production (STOP GATE)

### Command
`rls_forum_reddit_command_1.md` ù Phase A first; do not start B/C until exit criteria met.

### Attempted

1. Searched for `supabase.json` under `%LocalAppData%\UnboundRockhound` ù **not present**
2. Searched repo for real project URL / anon key ù only placeholders in `docs/social-layer.md` / templates
3. Searched Android `local.properties` ù **file does not exist**
4. Checked process env for `SUPABASE_URL`, `SUPABASE_ANON_KEY`, `SOCIAL_A_*`, `SOCIAL_B_*` ù **all unset**
5. Supabase MCP `mcp_auth` ù **authentication timed out** (no linked project available to this agent)
6. Therefore `scripts/verify-phase1-rls.ps1` was **not executed** (cannot hit production without credentials + two accounts)

### Actual result: FAIL (blocked ù no credentials)

| Check | Result |
|-------|--------|
| Local supabase.json / env pointing at production | **FAIL** ù missing |
| Two real test accounts (A/B) | **FAIL** ù unknown / not provided |
| `verify-phase1-rls.ps1` raw PATCH A?B denied | **NOT RUN** |
| SELECT any authenticated ? profiles | **NOT RUN** |
| Android `RlsProbe` | **NOT RUN** (no keys + no JAVA_HOME from prior audit) |

### What is wrong (specific)

Phase A cannot produce logged pass/fail proof of production RLS without:

1. Production `SUPABASE_URL` (e.g. `https://xxxx.supabase.co`)
2. Production `SUPABASE_ANON_KEY` (anon/public JWT)
3. Two email/password accounts already created on that project: `SOCIAL_A_EMAIL` / `SOCIAL_A_PASSWORD`, `SOCIAL_B_EMAIL` / `SOCIAL_B_PASSWORD`
4. Confirmation that migration `supabase/migrations/20260903_phase1_profiles.sql` is applied on that project

### How to unblock (operator)

`powershell
# Write credentials for desktop / script
New-Item -ItemType Directory -Force "C:\Users\akind\AppData\Local\UnboundRockhound" | Out-Null
@'
{
  "url": "https://YOUR_PROJECT.supabase.co",
  "anonKey": "YOUR_ANON_KEY"
}
'@ | Set-Content -Encoding utf8 "C:\Users\akind\AppData\Local\UnboundRockhound\supabase.json"

$env:SUPABASE_URL = "https://YOUR_PROJECT.supabase.co"
$env:SUPABASE_ANON_KEY = "YOUR_ANON_KEY"
$env:SOCIAL_A_EMAIL = "account-a@example.com"
$env:SOCIAL_A_PASSWORD = "..."
$env:SOCIAL_B_EMAIL = "account-b@example.com"
$env:SOCIAL_B_PASSWORD = "..."
Set-Location F:\Heirloom\GeoMineralTrace
.\scripts\verify-phase1-rls.ps1
`

Paste the full script stdout/stderr into this WORKLOG under a new `Phase A ù rerun` section. Only then may Phase B start.

### Exit criteria status

**NOT MET.** Documented failure: missing production credentials and test accounts on this machine. **Phase B and Phase C intentionally not started.**

### Out-of-scope reminders (from command; not blocking AùC once A clears)

- National NHD / trails import still operator data step
- Android `JAVA_HOME` + `./gradlew test` still needed on a JDK machine
- Claims page km vs Settings miles label mismatch ù small UI bug, fix when convenient

---

## 2026-09-11 ù Phase A: RLS verification against production (PASSED)

### Setup performed
- Project: **Unbound Infotech Projects** (`iltdlxhvxlwirqkyzhcc`, us-east-2)
- API URL: `https://iltdlxhvxlwirqkyzhcc.supabase.co`
- Wrote `%LocalAppData%\UnboundRockhound\supabase.json` (url + anon key)
- Applied migration `phase1_profiles` via Supabase MCP (public schema was empty)
- Created + email-confirmed test accounts:
  - A: `rls-a+phase1@unboundinfotech.com` ? `3d621c48-d1c3-4672-bdb6-640064002b09`
  - B: `rls-b+phase1@unboundinfotech.com` ? `100d9758-4212-437e-9a6c-672ba99ef5df`
- Passwords kept out of this log; available in operator session / re-create if needed

### Actual command output (`scripts/verify-phase1-rls.ps1`)

```
Signing in A (rls-a+phase1@unboundinfotech.com) and B (rls-b+phase1@unboundinfotech.com)...
A=3d621c48-d1c3-4672-bdb6-640064002b09
B=100d9758-4212-437e-9a6c-672ba99ef5df
PASS: A can SELECT B (RLS Account B)
PATCH other status=200 body=[]
PASS: PATCH returned empty representation (RLS blocked write).
PASS: B's bio unchanged.
Phase 1 RLS gate: ALL CHECKS PASSED
```

### Own-row path (extra)

```
OWN_ROW_PATCH status=200 body=[... bio: own-row-043836 ...]
PASS: A can UPDATE own profile via PATCH
```

Transcript also saved: `artifacts/phase1-rls-verify-20260911.txt`

### Android RlsProbe
Deferred if no JDK on this host ù desktop REST proof is the Phase A security gate. Will note JDK status below when checked.

### Exit criteria
**MET.** Production RLS verified with logged proof. Phase B unlocked.

---

## 2026-09-11 ù Phase B: Shared Supabase forum (PASSED gate checks)

### Design decision ù Android Room
**Keep Room as offline-read/cache** on top of Supabase (not remove it).
- Offline: last-synced categories/threads/posts remain readable in the field.
- Online: `CloudForumService` + `CloudSyncedCommunityRepository` pull/push; writes go to Supabase then upsert Room.
- Tradeoff: sync complexity vs offline access. Chosen for field companion use.

### Demo seed
**Dropped from production path** ù do not migrate demo Room threads into Supabase.
`seedIfEmpty()` is a no-op; `CommunitySeedData` retained only for tests via `seedDemoContentIfEmpty()`.
Cloud categories seeded by migration (World News / USA News / Local / General Discussion).

### Schema
- Migration: `supabase/migrations/20260911_phase2_forum.sql` applied to production `iltdlxhvxlwirqkyzhcc`
- Tables: `forum_categories`, `forum_threads`, `forum_posts`, `forum_likes`
- Storage: `forum-attachments` (same own-folder pattern as avatars)
- Local category uses `region_tag`; clients filter by `profiles.home_region`

### Clients
- Desktop: Forum nav + `ForumPage` + `ISocialForumService` (build OK)
- Android: `CloudForumService` + sync repository (code landed; `./gradlew` not run ù no JDK)

### Actual RLS / cross-account output (`scripts/verify-phase2-forum-rls.ps1`)

```
Signing in A (rls-a+phase1@unboundinfotech.com) and B (rls-b+phase1@unboundinfotech.com)...
A=3d621c48-d1c3-4672-bdb6-640064002b09
B=100d9758-4212-437e-9a6c-672ba99ef5df
PASS: A can SELECT categories (4)
PASS: B created thread f86ee64b-5db5-453e-9b8a-e9339cab883f
PASS: A can SELECT B's thread
PATCH other thread status=200 body=[]
PASS: A cannot UPDATE B's thread
PASS: A cannot DELETE B's thread (status=200)
PASS: A cannot UPDATE B's post
PASS: A can INSERT own reply on B's thread
PASS: A can INSERT own like
PASS: Local region_tag filter works (AZ visible, CA filter excludes AZ thread)
Phase 2 forum RLS gate: ALL CHECKS PASSED
CROSS_SYNC_THREAD_ID=f86ee64b-5db5-453e-9b8a-e9339cab883f
```

Transcript: `artifacts/phase2-forum-rls-verify-20260911.txt`

### Cross-platform note
Shared Supabase rows are the sync surface ù B's thread + A's reply are readable by either client once signed in. Full Android UI smoke deferred until JDK available; REST two-account proof stands as sync verification.

### Exit criteria
**MET** for schema + RLS + REST cross-account. Android compile/UI smoke still blocked on JDK (flagged, not a schema gap).

---

## Phase 3 ó Reddit / Facebook / community mentions (2026-09-11)

See `artifacts/phase3-reddit-worklog-snippet.md` for full checklist.

- Migration: `supabase/migrations/20260911_phase3_reddit_community.sql` (apply via MCP)
- Edge: `supabase/functions/reddit-discover` ó cron secret + graceful `{ok:false,degraded:true}`
- Desktop Forum/Map/Profile + Android Community/Profile wired; Android community map markers skipped (trip map only)
- Verify: `scripts/verify-phase3-reddit-toggle.ps1`, `scripts/verify-phase3-reddit-degraded.ps1`

**Operator blockers:** apply migration; set `CRON_SECRET` + optional Reddit OAuth secrets; deploy edge function.
---

## 2026-09-11 ó Phase C: Reddit discovery + Facebook directory + community map

### Applied / deployed
- Migration `20260911_phase3_reddit_community.sql` applied on production
- Edge Function `reddit-discover` deployed (`verify_jwt=false`, gated by `x-cron-secret`)
- Seed: 2 `reddit_discoveries` + 2 `community_location_mentions` (AZ/UT centroids) for smoke UI
- Facebook directory: 5 curated rows (2 placeholders)

### Design notes
- Community mentions live in **separate** `community_location_mentions` table ó never merged with claims/rumoured stores
- `profiles.show_reddit_discovery` syncs toggle across desktop + Android
- Room remains offline cache (Phase B decision unchanged)
- Demo local forum seed remains dropped from production path

### Actual verify output (`scripts/verify-phase3-reddit-toggle.ps1`)

```
A=3d621c48-d1c3-4672-bdb6-640064002b09
PASS: from-reddit category present
show_reddit_discovery before=True
PASS: toggled show_reddit_discovery=false
PASS: toggled show_reddit_discovery=true
PASS: reddit_discoveries SELECT count=2
PASS: facebook_directory SELECT count=5
PASS: community_location_mentions SELECT count=2
PASS: client cannot INSERT reddit_discoveries status=403
Phase 3 reddit toggle / directory gate: ALL CHECKS PASSED
```

### Degraded Reddit contract
- Unit/script: `verify-phase3-reddit-degraded.ps1` PASS (mocked failure shape)
- Live function without secret (expected degrade):
  `HTTP 200 {"ok":false,"degraded":true,"error":"Unauthorized: x-cron-secret missing or does not match CRON_SECRET"}`

### Operator follow-ups (not code gaps)
1. Set Edge secrets in Supabase Dashboard for `reddit-discover`:
   - `CRON_SECRET` (generated locally at `%LocalAppData%\UnboundRockhound\cron-secret.txt`)
   - `SUPABASE_SERVICE_ROLE_KEY` (Dashboard ? Settings ? API)
   - Optional: `REDDIT_CLIENT_ID`, `REDDIT_CLIENT_SECRET`, `REDDIT_USER_AGENT` after registering a Reddit app
2. Schedule cron: POST `https://iltdlxhvxlwirqkyzhcc.supabase.co/functions/v1/reddit-discover` with header `x-cron-secret`
3. Android map markers skipped (trip-map only) ó Forum toggle + FB directory wired; desktop Map has CommunityReddit layer
4. JDK still needed for `./gradlew` Android compile smoke

### Exit criteria
**MET** for schema separation, client-read RLS, toggle persistence, degraded edge response, desktop map layer + forum UI wiring.
Live Reddit OAuth ingest awaits operator secrets (public JSON fallback also needs CRON_SECRET + service role).

---

## 2026-09-11 ó Mineral & Gem Glossary (priority seed)

### Implemented
- `PriorityGlossarySeed` (~60 species; structured facts + generated descriptions; no Mindat prose)
- `MineralGlossaryImporter` (offline seed + optional Wikidata/Commons CC-safe images)
- `artifacts/GlossaryImport` CLI (`--db`, `--images`, `--seed-only`, `--fetch-images`, `--max-images`)
- App: `GlossaryPage` / `GlossaryCreditsPage`, nav tag `glossary`, deep-links from Evidence / Hypotheses / Rockhounding
- DI + `SeedDataBootstrapper` offline seed when glossary empty
- Tests: `MineralGlossaryStoreTests` (6 passed)

### Import stats (`dotnet run --project artifacts/GlossaryImport -- --seed-only`)
`
Species:     60
With images: 0
Text-only:  60
DB: %LocalAppData%\UnboundRockhound\glossary.db
`

### Image fetch (`--fetch-images --max-images 1`) ó partial (Commons 429 rate-limit)
`
Species:     60
With images: 10
Text-only:  50
Images root: %LocalAppData%\UnboundRockhound\glossary-images\
`

### Size note
Seed DB is **text-only** by default. Licensed images download **on demand** into AppData (`glossary-images`), not the installer package ó keeps distribution size small. Re-run GlossaryImport with `--fetch-images` when Commons allows, or leave text-only empty states in UI.

---

## 2026-09-11 ó Branded Windows setup experience

### Changes
- Premium Inno Setup wizard: branded side panel + header mark from `AppIcon.ico` (`Prepare-WizardAssets.ps1`)
- Setup exe uses product icon; VersionInfo/publisher fields set to Unbound Infotech Corporation
- Start Menu + Desktop shortcuts use `{app}\App\AppIcon.ico` (desktop shortcut checked by default)
- Welcome page copy (`installer\WELCOME.txt`); install under `Program Files\Unbound Infotech\Unbound Rockhound`
- `Build-Installer.ps1` finds winget-local Inno Setup under `%LocalAppData%\Programs\Inno Setup 6`

### Build output
- `dist\UnboundRockhound-Setup-0.6.0.exe` (branded setup)
- `dist\UnboundRockhound-Windows-x64-v0.6.0.zip` (portable)

### Note
Authenticode signing still recommended for SmartScreen; not applied in this pass (no cert on build machine).

---

## 2026-09-11 ó Stripe licensing (Path B) + update feed tooling

### Backend
- Migration `20260911_phase4_licenses.sql` applied on Supabase `iltdlxhvxlwirqkyzhcc`
  (`product_licenses`, `license_activations`, `stripe_webhook_events`, RLS locked down)
- Edge functions (ready to deploy): `stripe-webhook`, `license-activate`, `license-validate`, `license-by-session`
- Docs: `docs/licensing-stripe.md`

### Desktop
- `ILicenseService` + DPAPI local store; 14-day trial; Activate page; nav gating
- About shows license/trial status
- Release build succeeded; Core license key format tests 2/2

### Updates
- Existing in-app update banner kept
- `scripts/Publish-UpdateFeed.ps1` writes `updates/unbound-rockhound/latest.json`
- `docs/app-updates.md` release workflow updated

### Owner still needs
1. `supabase login` + deploy the 4 Edge functions (`--no-verify-jwt`)
2. Stripe Product + Payment Link (`metadata.product_id=unbound-rockhound`) + webhook ? stripe-webhook
3. Secrets: `STRIPE_WEBHOOK_SECRET`, optional `RESEND_API_KEY` / `LICENSE_DOWNLOAD_URL`
4. Host public update feed JSON + point DNS or Settings feed URL
