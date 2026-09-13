# Unbound Rockhound

Professional Windows research instrument for **multi-modal forensic video geolocation** tightly integrated with a **local-first U.S. rockhounding / gem & mineral knowledge base**.

> **Results are probabilistic hypotheses.** Solar loci, OCR, audio, landmarks, and mineral cues can corroborate each other; none alone proves a location. Always review evidence, reject weak clues, and verify land access independently.

## Status

| Area | State |
|------|--------|
| WinUI research shell + design system | **v0.6.0 — Unbound Rockhound branding + setup wizard** |
| Deep Analysis (scored clusters + reasoning) | Done |
| Video/image analysis pipeline | Done |
| Evidence Board / Hypotheses / Map / Reports | Done |
| Solar + rockhounding + claims + rivers | Done |
| Portable Windows x64 zip + Inno Setup | `scripts/Package-Release.ps1` / `scripts/Build-Installer.ps1` |

See [docs/commercial-release.md](docs/commercial-release.md) for the sell / distribute checklist.

## Solution layout

```
GeoMineralTrace/
  src/
    GeoMineralTrace.App/             WinUI 3 shell
    GeoMineralTrace.Core/            Domain models
    GeoMineralTrace.Solar/           Solar math, locus, techniques KB
    GeoMineralTrace.Pipeline/        Ingest → metadata → keyframes → OCR/ASR/NER/vision
    GeoMineralTrace.Evidence/        Evidence Board store
    GeoMineralTrace.Hypothesis/      Fusion engine + place gazetteer
    GeoMineralTrace.Rockhounding/    SQLite localities, ratings, CSV/USGS import
    GeoMineralTrace.Reporting/       Markdown/JSON research reports
    GeoMineralTrace.Infrastructure/  DI / logging / session context
  tests/                             xUnit + FluentAssertions
  docs/                              Architecture, data sources, scoring, limitations, roadmap
  web/activate/                      Stripe success page (host at unboundinfotech.com/activate)
  knowledge/techniques/              Markdown technique articles
  data/seed/                         CSV seed payloads
```

## Requirements

- Windows 10/11 (17763+)
- .NET 8 SDK
- Windows App SDK (restored via NuGet)

## Build & test

```powershell
cd $env:USERPROFILE\Documents\GeoMineralTrace
dotnet restore GeoMineralTrace.sln
dotnet test GeoMineralTrace.sln --filter "FullyQualifiedName!~GeoMineralTrace.App"
dotnet build src\GeoMineralTrace.App\GeoMineralTrace.App.csproj -c Debug -p:Platform=x64
```

### Run the app (single install)

**Do not** use `bin\Debug\...\UnboundRockhound.exe` for day-to-day use — that is a developer build.

| Role | Path |
|------|------|
| **Launch the app** | `F:\UnboundRockhound` → `Launch Unbound Rockhound.bat` |
| **Source code** | `F:\Heirloom\GeoMineralTrace` (repo; internal project names unchanged) |
| **Your data** | `%LocalAppData%\UnboundRockhound\` (migrated from `GeoMineralTrace` on first run) |

After pulling or building changes, refresh the install:

```powershell
powershell -File scripts\Package-Release.ps1
```

See [INSTALL-LOCATION.txt](INSTALL-LOCATION.txt) for details.

### Optional tools

```powershell
winget install ffmpeg    # keyframe bitmaps + shadow measurement images
winget install yt-dlp    # YouTube ingest on Analyze page
pip install openai-whisper   # optional ASR (or place media.whisper.srt beside videos)
```

### YouTube ingest

Paste a youtube.com / youtu.be URL on **Analyze**:

- **Stream & analyze** (primary) — temporary ≤720p working copy + local pipeline.
- **Preview only** — in-app embed (ads may appear).
- **Sign in to YouTube…** — optional; use the Premium account holder's Google login so downloads can use that session (Settings also has Sign in / Sign out). Cookies stay under `%LocalAppData%\GeoMineralTrace\youtube-cookies.txt`.
- **Archival download…** — optional full-quality fetch.

Requires `yt-dlp` (and ideally `ffmpeg`). Respect YouTube terms and copyright; only sign in with an account you are allowed to operate.

## Design principles

1. **Modular boundaries** — Core has no UI; Solar has no SQLite; Rockhounding never initiates network I/O.
2. **Local-first** — Optional online map tiles require explicit consent (Settings → Online enrichment).
3. **Auditability** — Every evidence item stores type, provenance, confidence, notes, and media linkage.
4. **Honest uncertainty** — Confidence scores, assumptions, and limitations travel with solar and hypothesis outputs.
5. **Legal clarity** — Closed/restricted localities are flagged; rating formula heavily weights legal clarity.

## Documentation

- [Architecture](docs/architecture.md)
- [In-app updates](docs/app-updates.md)
- [Commercial release checklist](docs/commercial-release.md)
- [Solar techniques & formulas](docs/solar-techniques.md)
- [Data sources & update procedures](docs/data-sources.md)
- [Scoring methodology](docs/scoring-methodology.md)
- [Limitations](docs/limitations.md)
- [Roadmap](docs/roadmap.md)

## License & ethics

This project is intended for legitimate research, education, and personal hobby planning. Respect private property, mining claims, park rules, and collecting limits. The demo database includes a deliberately **closed** site to exercise restriction UX — it is not a visit recommendation.