# Architecture overview

## Layers

```
┌─────────────────────────────────────────────────────────┐
│  GeoMineralTrace.App (WinUI 3)                          │
│  Navigation · Evidence Board · Solar UI · Localities    │
└───────────────────────────┬─────────────────────────────┘
                            │ DI
┌───────────────────────────▼─────────────────────────────┐
│  Infrastructure                                         │
│  Logging · service registration · paths                 │
└───┬─────────────┬──────────────┬────────────┬───────────┘
    │             │              │            │
┌───▼───┐   ┌─────▼────┐  ┌──────▼─────┐ ┌───▼──────────┐
│Solar  │   │Evidence  │  │Hypothesis  │ │Rockhounding  │
│engine │   │board     │  │fusion      │ │SQLite store  │
└───┬───┘   └─────┬────┘  └──────┬─────┘ └───┬──────────┘
    └─────────────┴──────────────┴───────────┘
                            │
                    ┌───────▼────────┐
                    │ Core domain    │
                    │ models / DTOs  │
                    └────────────────┘
```

## Module responsibilities

| Project | Responsibility | Must not |
|---------|----------------|----------|
| **Core** | Shared types: `EvidenceItem`, `LocationHypothesis`, `Locality`, `ShadowMeasurement`, `Confidence` | Depend on UI, SQLite, or network |
| **Solar** | Forward solar position, shadow geometry, inverse locus, techniques KB | Persist localities or parse video |
| **Evidence** | Session evidence store, filter/search | Rank locations |
| **Hypothesis** | Fuse evidence streams into ranked hypotheses | Own media decoding |
| **Rockhounding** | Offline localities DB, ratings, spatial queries | Call the network |
| **App** | Presentation, user measurement entry, navigation | Embed scientific formulas (call libraries) |

## Analysis pipeline (implemented)

1. **Ingest** — single/batch videos → `AnalysisSession`
2. **Metadata** — EXIF/GPS/codecs/timestamps → evidence
3. **Keyframes** — scene-change + quality sampling
4. **OCR** — dense text with language detection
5. **Vision** — landmarks, vegetation, terrain, mining cues
6. **Audio** — transcription, dialect, place/mineral names, ambience
7. **Solar** — interactive shadow measures → locus map
8. **Fusion** — ranked hypotheses with supporting IDs
9. **Rockhounding join** — nearby sites + mineral cross-refs
10. **Report** — exportable audit package

Phase 1 delivers the **domain contracts**, **solar engine**, **Evidence Board shell**, **early fusion**, and **offline locality store** so later extractors plug into stable models.

## Progress & cancellation

`ProgressReport` and `AnalysisSession` carry stage, fraction, and optional `CancellationTokenSource`. Pipeline workers (future) must check cancellation between stages and surface failures without silent partial success.

## Dependency injection

`AddGeoMineralTraceCore()` registers solar, evidence, fusion, techniques KB, and `LocalityStore` (SQLite under `%LocalAppData%\GeoMineralTrace\`).
