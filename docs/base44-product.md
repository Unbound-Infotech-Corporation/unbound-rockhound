# Base44 Product — Unbound Rockhound (launch pricing)

Paste into **Base44 Dashboard → Data → Product** on [unboundinfotech.com](https://unboundinfotech.com/).

---

## Pricing

| Period | Price | Notes |
|--------|-------|--------|
| **Now → Dec 31, 2026** | **$39.99** | Special launch rate |
| **Jan 1, 2027 onward** | **$79.99** | Regular price |

**Base44 Price field (today):** `39.99`

**Suggested tagline add-on (optional in description):**  
*Launch special — $39.99 through December 31, 2026. Price increases to $79.99 on January 1, 2027.*

On New Year’s, edit the Product record: set **Price** to `79.99` and remove or update the launch-special sentence.

---

## Required fields

| Field | Value |
|-------|--------|
| **Name** | Unbound Rockhound |
| **Price** | `39.99` |
| **Type** | `download` |
| **Status** | `active` *(only after Download URL works)* |
| **Download URL** | `https://github.com/Unbound-Infotech-Corporation/unbound-rockhound/releases/download/v0.6.0/UnboundRockhound-Windows-x64-v0.6.0.zip` |
| **Delivery Instructions** | *(leave empty for download type)* |

> If Base44 prefers its own CDN, upload the same zip from the GitHub Release assets (or `dist\UnboundRockhound-Windows-x64-v0.6.0.zip` after packaging) and paste that URL instead.


---

## Tagline

```
Special launch price $39.99 — forensic video geolocation & rockhounding research for Windows.
```

---

## Description (paste as-is)

```
Unbound Rockhound is desktop research software from Unbound Infotech Corporation. Point it at rockhounding videos or field clips and get ranked location hypotheses — then cross-check claims, rivers, and public localities on an interactive map. Built for serious hobbyists and investigators who want a local-first workflow on Windows.

LAUNCH SPECIAL
Pay $39.99 through December 31, 2026. On January 1, 2027 the regular price becomes $79.99. Same product, same download — lock in the launch rate while it lasts.

WHAT YOU GET
• Analyze YouTube and local media (keyframes, OCR, optional Whisper speech-to-text)
• Evidence Board — accept, reject, and re-weight clues as you work a case
• Deep Analysis — scored corroborating clusters with clear reasoning you can inspect
• Solar / shadow tools and a fused hypothesis map
• Rockhounding, rivers, and claims knowledge stored on your PC
• Optional cloud account for shared profiles with the Android companion
• Portable Windows 10/11 x64 install — no separate .NET install required

HOW IT WORKS
1. Open Analyze and add a video or images (or a YouTube URL with yt-dlp)
2. Review clues on the Evidence Board; measure shadows when you have a clear keyframe
3. Run Deep Analysis for ranked hypotheses with confidence and reasoning
4. Check the map against rivers, localities, and claim context before any field trip

IMPORTANT DISCLAIMERS
Outputs are probabilistic hypotheses — not GPS truth, legal advice, or permission to collect. Always verify land access, claim status, and regulations before visiting a site. Optional tools (ffmpeg, yt-dlp, Whisper) and online map tiles may need a network connection.

Publisher: Unbound Infotech Corporation
Support: support@unboundinfotech.com
Web: https://unboundinfotech.com/
```

---

## Features (one bullet per line)

```
Launch special $39.99 through Dec 31, 2026 (then $79.99)
YouTube & local media analysis with keyframes and OCR
Evidence Board — accept, reject, and re-weight clues
Deep Analysis with scored clusters and inspectable reasoning
Solar / shadow locus and fused map hypotheses
Local rockhounding, rivers, and claims knowledge base
Optional cloud profiles (email sign-in)
Portable Windows x64 — no .NET install required
```

---

## Other recommended fields

| Field | Value |
|-------|--------|
| **Category** | Software |
| **Featured** | `true` |
| **Sort order** | `10` |
| **Image URL** | Upload product artwork to Base44 media, then paste that CDN URL |

---

## Tips for how Base44 lists products

1. **Type = `download`** — After payment, buyers get the file from **Download URL** on the success page. Don’t put license-key text in Delivery Instructions unless Type is `license` or `subscription`. **Preferred launch path:** Type=`download` (zip) **plus** Stripe webhook → Resend email with the `UR-` key (and `/activate?session_id=` as backup). See [licensing-stripe.md](licensing-stripe.md) §9. If Base44 adds a working Type=`license` field, use it only as a pointer (“key arrives by email”) — never a shared static key.

2. **Download URL must be a direct file link** — Prefer the GitHub Release asset above, or upload `UnboundRockhound-Windows-x64-v0.6.0.zip` to Base44 media and paste that public URL. It must start the zip download, not open a marketing HTML page.

3. **Status controls the Buy button** — `coming_soon` hides/disables purchase. Use `active` only when Price, Type, and Download URL are correct.

4. **Tagline vs Description** — Tagline is the one-liner under the name in the grid. Description is the full detail page. Put the **$39.99 launch special** in both so shoppers see it in the grid and on the page.

5. **Features** — Short bullets for scannability. Put “Launch special…” first so the deal is obvious.

6. **Featured + Sort order** — Featured highlights the product; lower sort order usually appears earlier in the grid (confirm in your Base44 UI).

7. **Image URL** — Use a square-ish logo or hero (app icon / StoreLogo). Empty Image URL makes the grid look unfinished.

8. **Price changes** — On Jan 1, 2027 set Price to `79.99` and edit Tagline / Description / Features to remove “launch special” language. Existing buyers who already downloaded keep their zip; this only affects new purchases.

9. **Test purchase** — Do one real or test checkout yourself: success page → download zip → license key email (or `/activate?session_id=`) → unzip → `Launch Unbound Rockhound.bat` → Activate → About shows version **0.6.0** and Licensed.

10. **Heirloom** — Keep as a separate Product row. Only flip it to `active` when its own Download URL points at a real Heirloom build.
