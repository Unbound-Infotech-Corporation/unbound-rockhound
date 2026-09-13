# Commercial release checklist (v0.6.0)

Use this before listing on the **Unbound Infotech** store or distributing a customer build.

## Done in this packaging pass

- [x] Version aligned: app `0.6.0`, Package.appxmanifest `0.6.0.0`, Package-Release.ps1 default
- [x] Publisher branded **Unbound Infotech Corporation** (About / EULA / installer / AppBranding)
- [x] Customer-facing Home + About copy
- [x] EULA / disclaimer shipped (`EULA-DISCLAIMER.md`) including optional cloud-account privacy
- [x] Portable self-contained publish via `scripts/Package-Release.ps1`
- [x] Branded Setup.exe via Inno Setup (`-BuildSetup`) — product icon on Setup + shortcuts
- [ ] Authenticode sign Setup.exe + UnboundRockhound.exe (SmartScreen)
- [x] Store listing copy: [store-listing.md](store-listing.md)
- [x] Social Phase 1 (optional Supabase auth + profiles) — see [social-layer.md](social-layer.md)

## Required before serious paid sales (owner action)

- [ ] Attorney-reviewed EULA / Terms of Sale (replace summary disclaimer)
- [ ] Public privacy policy URL (cloud email/auth requires this for many payment processors)
- [ ] Code-signing certificate (Authenticode) — unsigned WinUI exes trigger SmartScreen
- [ ] Support mailbox live (`support@unboundinfotech.com`)
- [ ] Payment + license delivery (Stripe → Supabase webhook → email key + `/activate` → desktop Activate) — see [licensing-stripe.md](licensing-stripe.md) §9 operator smoke checklist
- [ ] Host update feed JSON at public HTTPS URL — see [app-updates.md](app-updates.md) + `scripts/Publish-UpdateFeed.ps1`
- [ ] Refund / update policy for portable zip customers
- [ ] Optional: Microsoft Store listing (Partner Center + MSIX signing)

## Recommended polish before marketing push

- [ ] Host `updates/unbound-rockhound/latest.json` on a public HTTPS URL (see [app-updates.md](app-updates.md))
- [ ] Point `AppUpdateService.DefaultFeedUrl` at that URL
- [ ] Screenshots + 60s demo video for store page
- [ ] Customer smoke test: Analyze known YouTube short → Deep Analysis → Map
- [ ] Supabase production project + migration applied (if offering cloud sign-in at launch)

## Known product limitations (disclose on sales page)

- Hypotheses are probabilistic, not GPS truth
- Whisper ASR optional (not bundled)
- Map tiles need Online Enrichment + network
- Claim / MRDS / locality data can be stale — verify before visiting
- Cloud forum / news / DMs are **not** in 0.6.0 (Phase 1 = profiles only)
