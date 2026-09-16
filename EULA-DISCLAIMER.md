# Unbound Rockhound — End-User License Summary & Disclaimer

**Version:** 0.7.0  
**Product:** Unbound Rockhound (Windows desktop)  
**Publisher:** Unbound Infotech Corporation ([unboundinfotech.com](https://unboundinfotech.com/))

This document ships with the portable install and setup wizard. It is a plain-language summary for customers; replace with your attorney-reviewed EULA before high-volume commercial distribution.

## License grant (summary)

You may install and use Unbound Rockhound on Windows PCs you own or control, for legitimate research, education, hobby planning, and professional investigation workflows consistent with applicable law.

You may not reverse-engineer the product solely to create a competing geolocation product, redistribute cracked builds, or strip copyright notices from redistributed documentation included in the install folder.

## No warranty

THE SOFTWARE IS PROVIDED “AS IS,” WITHOUT WARRANTY OF ANY KIND. Location hypotheses, mineral locality data, geologic-map synthesis, Prospect Guess hints, claim status, river finds, and solar loci may be incomplete, outdated, or wrong.

## Critical use restrictions

1. **Not definitive geolocation.** Outputs are ranked probabilistic hypotheses. Do not treat them as courtroom-ready proof without independent corroboration.
2. **Not legal advice.** Claim status, land ownership, and collecting rules must be verified with BLM, state agencies, landowners, and current regulations.
3. **No visit endorsement.** Closed, restricted, or private sites flagged in the database must never be treated as recommendations to trespass or collect. Cooperative National Geologic Map units and Prospect Guess bands are research hints only — they do not grant permission to collect and are not a substitute for BLM / USFS / state / private-land checks.
4. **Media rights.** You are solely responsible for complying with YouTube, Instagram, and copyright law when downloading or analyzing third-party media.
5. **Safety.** Field travel is at your own risk. Weather, terrain, wildlife, and access conditions are outside the scope of this software.

## Data privacy

### Local-first (default)

- Case evidence, cookies (optional YouTube/Instagram sign-in), and diagnostic reports are stored on the local machine under `%LocalAppData%\UnboundRockhound\`.
- The app does not automatically upload crash reports. Optional “Send email…” opens your mail client with a local report attached only when you choose it.
- Online map tiles and media stream/download use the network only with your consent (Settings and/or confirmations).

### Optional cloud account (Phase 1 social identity)

- Creating a cloud account is **optional**. All core analysis, trips, and maps work offline without signing in.
- If you sign up / sign in, your email and password are processed by the configured **Supabase** Auth project; profile fields (display name, handle, bio, home region, avatar) are stored in that project under row-level security.
- Session tokens are stored locally (DPAPI-protected on Windows). Sign out clears the local session.
- Do not put the Supabase **service role** key in the app. Only the public anon key is used client-side.

## Support

**Unbound Infotech Corporation** — `support@unboundinfotech.com`  
Configure or override the support email in **Settings → Diagnostics**. Include the app version from **About** when contacting support.

## Third-party tools (optional)

ffmpeg, yt-dlp, and Whisper are separate programs you may install. Their licenses and terms apply independently.
