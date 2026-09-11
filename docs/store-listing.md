# Unbound Rockhound — Unbound Infotech store listing

**Base44 (unboundinfotech.com):** use the ready-to-paste Product fields in **[base44-product.md](base44-product.md)**.

Copy/paste for the Unbound Infotech product store / sales page.  
**SKU version:** 0.6.0 · **Publisher:** Unbound Infotech Corporation

## Short blurb (1–2 sentences)

**Unbound Rockhound** turns rockhounding and field videos into ranked location hypotheses, then cross-checks claims, rivers, and public localities on an interactive map — local-first Windows research software from Unbound Infotech.

## Feature bullets

- YouTube / local media analysis (keyframes, OCR, optional Whisper ASR)
- Evidence Board with accept / reject / re-weight
- Deep Analysis — scored corroborating clusters with inspectable reasoning
- Solar / shadow locus tools + hypothesis fusion map
- Rockhounding, rivers, and claims knowledge — local-first under `%LocalAppData%`
- Optional cloud account (email/password) for shared profiles with the Android companion
- Portable zip + Windows setup wizard (x64)

## What’s included in the download

| Artifact | Use |
|----------|-----|
| `UnboundRockhound-Windows-x64-v0.6.0.zip` | Portable install (no .NET required) |
| `UnboundRockhound-Setup-0.6.0.exe` | **Recommended** branded Inno Setup wizard (product icon, Start Menu + Desktop shortcuts with AppIcon, welcome + EULA) |
| `EULA-DISCLAIMER.md` | License summary + privacy |
| `INSTALL.txt` | Quick start |

## Requirements

- Windows 10 (1809+) or Windows 11, **64-bit**
- Optional: ffmpeg, yt-dlp, Whisper for full media features
- Internet only for map tiles (Online Enrichment), media ingest, and optional cloud sign-in

## Disclosures (must show on sales page)

- Hypotheses are **probabilistic**, not GPS truth or legal advice
- Whisper ASR is **optional** (not bundled)
- Claim / MRDS / locality data can be **stale** — verify before visiting
- Cloud account is **optional**; core workflows work offline

## Support

- Email: `support@unboundinfotech.com`
- Web: [unboundinfotech.com](https://unboundinfotech.com/)

## Store operator checklist

- [ ] Upload zip (and setup exe if available) to the Unbound Infotech store CDN / download slot
- [ ] Paste short blurb + feature bullets + disclosures
- [ ] Link EULA / this privacy summary
- [ ] Set price / license delivery (Gumroad, Lemon Squeezy, or internal checkout)
- [ ] Smoke-test download → unzip → launch → Analyze sample → About shows 0.6.0
- [ ] (Optional cloud) Apply Supabase migration; configure customer-facing project URL + anon key docs — never ship service role
- [ ] Point update feed `updates/unbound-rockhound/latest.json` (or host equivalent) when auto-update URL is live

## Build command (maintainers)

```powershell
cd F:\Heirloom\GeoMineralTrace
.\scripts\Package-Release.ps1 -Version 0.6.0 -SkipInstallSync
# Optional setup wizard (requires Inno Setup 6+):
.\scripts\Package-Release.ps1 -Version 0.6.0 -SkipInstallSync -BuildSetup
```

Output: `dist\UnboundRockhound-Windows-x64-v0.6.0.zip`
