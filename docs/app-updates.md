# In-app updates (multi-product pattern)

GeoMineral Trace (and your other sellable apps) can notify users when a newer build is published.

## How it works

1. You host a tiny JSON file per product (the **update feed**).
2. On launch (and via **Settings → Software updates → Check now**), the app downloads that JSON.
3. If `version` is newer than the installed build, a banner appears: **Download update**.
4. Download opens the browser to your zip / sales page. The user’s `%LocalAppData%` cases are not touched.

This is **notify + link**, not silent auto-patch. That fits portable zip distribution and keeps trust high.

## Feed schema (shared across apps)

```json
{
  "productId": "geomineral-trace",
  "version": "0.5.1",
  "releasedAtUtc": "2026-09-10T18:00:00Z",
  "title": "GeoMineral Trace 0.5.1",
  "notes": "Short plain-text summary for the banner.",
  "downloadUrl": "https://example.com/GeoMineralTrace-Windows-x64-v0.5.1.zip",
  "releaseNotesUrl": "https://example.com/changelog",
  "mandatory": false
}
```

| Field | Required | Notes |
|-------|----------|--------|
| `productId` | yes | Must match the app (`geomineral-trace`) |
| `version` | yes | Compared as `System.Version` (`0.5.1` → `0.5.1.0`) |
| `downloadUrl` | strongly yes | Banner button opens this |
| `releaseNotesUrl` | optional | Changelog page |
| `mandatory` | optional | Uses warning severity in the banner |

## Hosting options

| Host | Example |
|------|---------|
| Your CDN / site | `https://updates.geomineraltrace.app/geomineral-trace/latest.json` |
| GitHub raw | `https://raw.githubusercontent.com/YOU/releases-feed/main/geomineral-trace/latest.json` |
| Cloudflare R2 / S3 public object | Same URL pattern |
| Local test | Set Settings feed URL to `C:\path\to\latest.json` |

Default URL in code: `AppUpdateService.DefaultFeedUrl`.

Template in repo: [`updates/geomineral-trace/latest.json`](../updates/geomineral-trace/latest.json).

## Release workflow (you)

1. Bump app version in `GeoMineralTrace.App.csproj`.
2. Run `scripts/Package-Release.ps1 -Version X.Y.Z -BuildSetup`.
3. Upload the Setup.exe / zip to your download URL.
4. Run `scripts/Publish-UpdateFeed.ps1 -Version X.Y.Z -DownloadUrl <https-url> -Notes "..."`.
5. Upload `updates/unbound-rockhound/latest.json` to the public feed host.
6. Existing customers see the banner within a few seconds of next launch (if update checks are on).

Default URL in code: `AppBranding.DefaultUpdateFeedUrl` / `AppUpdateService.DefaultFeedUrl`.

Until `updates.unboundrockhound.app` DNS exists, host the same JSON on unboundinfotech.com (or R2/GitHub raw) and either change the constant or set Settings → Feed URL.

## Other apps you sell

Copy the same JSON schema and the same UX pattern:

- One folder per product under `updates/`
- One `productId` constant in that app
- One `DefaultFeedUrl`

Customers can disable checks per app in Settings. Dismissing a banner stores that version as “remind me later” until you clear it or ship a newer version.

## Future (optional)

When you want **one-click apply** inside the folder (not just download):

- [Velopack](https://velopack.io/) or similar for WinUI portable updates
- Or Microsoft Store packaging for Store-managed updates

Start with the feed + banner; add silent apply once sales volume justifies signing + update channels.
