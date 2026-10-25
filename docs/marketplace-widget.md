# Unbound Marketplace widget

Shared desktop catalog for Unbound Infotech apps. The pilot host is **Unbound Rockhound**. The same projects are meant to drop into Heirloom Unbound, ViralForge, Unbound Astrology, and UnboundOS later.

The widget does not collect card numbers. **Buy** is only for a product this install can activate. It asks the public store to create a Stripe Checkout session and opens that link in the default browser. After payment, the page polls the existing `license-by-session` Edge function or accepts a pasted key, then activates through the host app's current license service. Every other active listing shows **Get it**, which opens that product's download URL or store page and does not start checkout.

## Projects

| Project | What it is |
|---------|------------|
| `src/Unbound.Marketplace` | .NET 8 library. Catalog client, checkout launcher, activation state machine, Supabase session lookup. No Rockhound types. |
| `src/Unbound.Marketplace.WinUI` | WinUI 3 page and panel (`MarketplacePage`, `MarketplacePanel`) plus a self-contained theme that matches the Rockhound charcoal / blue chrome. |
| `tests/Unbound.Marketplace.Tests` | Catalog parsing and the activation state machine. |

Rockhound references both projects. Navigation tag `marketplace` is available without a license, same as Activate.

## What is live

These calls are the same ones the public site uses. No API keys are compiled into the apps.

| Step | Endpoint |
|------|----------|
| Catalog | `GET https://unboundinfotech.com/api/apps/69d5f18a091f6012136d4f5a/entities/Product?sort=sort_order&limit=50` |
| Checkout | `POST https://unboundinfotech.com/api/apps/69d5f18a091f6012136d4f5a/functions/createCheckoutSession` with `{ "product_id": "<Base44 id>" }` |
| Session → key | `POST {SUPABASE_URL}/functions/v1/license-by-session` with `{ "checkoutSessionId": "cs_…" }` |
| Activate | The host's existing activate path. In Rockhound that is `ILicenseService.ActivateAsync`, which posts to `license-activate` once. |

The checkout function returns `{ "url": "https://checkout.stripe.com/c/pay/cs_…" }`. The widget reads the `cs_live_` / `cs_test_` id from that URL and polls. It only opens `https://*.stripe.com` links.

Catalog JSON may be a Base44 array or `{ "products": [ … ] }`. Recognized fields include `id`, `name`, `price`, `tagline`, `description`, `image_url`, `status`, `sort_order`, and optional `license_product_id` / `licenseProductId`. `is_sample: true` rows are hidden. The short description is the tagline, otherwise the first collapsed line of the description.

If the store cannot be reached, the panel shows an offline notice and the embedded file `src/Unbound.Marketplace/Catalog/fallback-catalog.json`.

## Rockhound wiring

`GeoMineralTrace.App/Marketplace/RockhoundMarketplace.cs` registers the host:

- Catalog id `6a9949122f0c8cf48076190a` and the name **Unbound Rockhound** map to license product id `unbound-rockhound`. That is the only id in `ActivatableLicenseProductIds`.
- `HideComingSoon` and `HideInactive` are on, so Heirloom (`coming_soon`) and archived rows stay off the panel.
- Clore.ai Fleet Optimizer stays visible while its store row is `active`, but it has no `license_product_id` and Rockhound does not alias it. The card says **Get it** and opens the store page. It is not an in-app Buy unless a host maps a license product id and that id is activatable here.
- Owned state is the existing local entitlement: status `active` and a saved key. Trial is not Owned.
- Pasted keys and keys returned by `license-by-session` go through `ILicenseService.ActivateAsync` only. The marketplace does not call `license-activate` a second time, so it cannot burn an extra device seat.
- Supabase URL and anon key still come from Sign in (`supabase.json` or `SUPABASE_URL` / `SUPABASE_ANON_KEY`). Nothing new is stored.

Optional environment overrides (public URLs only):

- `UNBOUND_MARKETPLACE_CATALOG_URL`
- `UNBOUND_MARKETPLACE_CHECKOUT_URL`

The Activate page is unchanged.

## Drop the widget into another app

1. Reference `Unbound.Marketplace` and `Unbound.Marketplace.WinUI`.
2. Build a `MarketplaceOptions`:
   - `HostLicenseProductId` — the id your webhook stores (see the table below).
   - `ActivatableLicenseProductIds` — ids this install may Buy and activate. Empty means only `HostLicenseProductId`.
   - `HideComingSoon` / `HideInactive` — default true. Set either to false to show badges instead of hiding those rows.
   - `HostProductDisplayName` — the customer-facing name, for example **Heirloom Unbound**.
   - `LicenseProductIdByCatalogId` / `LicenseProductIdByName` — until Base44 rows include `license_product_id`. Do not map a title you do not want offered as Buy.
3. Implement three host hooks:
   - `IMarketplaceKeyActivator` — call that app's existing activate function **once**.
   - `IMarketplaceOwnershipSource` — return license product ids this install already holds.
   - `IMarketplaceSupabaseSettings` — URL and anon key for `license-by-session`. Do not embed the service role key.
4. Register `MarketplaceCatalogClient`, `CheckoutLauncher`, `SupabaseLicenseSessionClient`, and a `MarketplaceHost`.
5. Set `MarketplaceHostContext.Current` before the first navigation.
6. Navigate to `Unbound.Marketplace.WinUI.MarketplacePage`, or embed `MarketplacePanel` on an existing page.

A product is **Buy** only when its resolved `license_product_id` is in `ActivatableLicenseProductIds` (or equals `HostLicenseProductId` when that set is empty). Anything else that is still listed is **Get it**: the panel opens `download_url` when it is https, otherwise the store page. No Stripe session is created, and the host activator is not called.

## License product ids

Use these slugs in Base44 `license_product_id`, Stripe `metadata.product_id`, and `product_licenses.product_id`. The same list is `LicenseProductIds` in `src/Unbound.Marketplace/LicenseProductIds.cs` and `ALLOWED_PRODUCT_IDS` in `supabase/functions/_shared/license.ts`.

| App | `license_product_id` |
|-----|----------------------|
| Unbound Rockhound | `unbound-rockhound` |
| Heirloom Unbound | `heirloom-unbound` |
| ViralForge | `viralforge` |
| Unbound Astrology | `unbound-astrology` |
| UnboundOS | `unboundos` |

Rockhound aliases still collapse to `unbound-rockhound`: `rockhound`, `unboundrockhound`, and a missing id on the dedicated webhook. Other apps must send their slug. `normalizeProductId` lowercases and turns spaces into hyphens, so `Heirloom Unbound` becomes `heirloom-unbound`.

### What `license-activate` accepts

`POST {SUPABASE_URL}/functions/v1/license-activate` body:

```json
{ "licenseKey": "UR-XXXX-XXXX-XXXX-XXXX", "deviceFingerprint": "<8-128 chars>", "deviceLabel": "optional", "productId": "heirloom-unbound" }
```

- `productId` omitted or blank still means `unbound-rockhound`. Rockhound's `ILicenseService` keeps sending `unbound-rockhound` and is unchanged.
- `productId` must be on the allowlist. Anything else returns 400 `This app activates Unbound Rockhound keys only.`
- The row's `product_id` must canonicalize to the same id. A Rockhound client cannot activate an Heirloom key, and an Heirloom client cannot activate a Rockhound key (404).
- Keys are still `UR-XXXX-XXXX-XXXX-XXXX`. This change does not add a second key alphabet. An app that mints a different prefix needs its own normalizer before these functions can bind that key.
- `license-validate` uses the same allowlist and the same default. A mismatch returns `{ "ok": false, "error": "wrong_product", "status": "missing" }`.
- `stripe-webhook` still ignores non-Rockhound metadata and still stores `product_id=unbound-rockhound`. Sibling apps do not get keys until that webhook is extended and redeployed. Missing metadata on this webhook remains Rockhound.

Deploy the updated functions before another app calls them:

```bash
supabase functions deploy license-activate --no-verify-jwt
supabase functions deploy license-validate --no-verify-jwt
```

## Tests

```powershell
dotnet test tests/Unbound.Marketplace.Tests/Unbound.Marketplace.Tests.csproj -c Release
```

The marketplace library and these tests build on any OS with the .NET 8 SDK. The WinUI app and `Unbound.Marketplace.WinUI` compile on Windows. `XamlCompiler.exe` is a Windows binary, so a Linux build of those projects stops at the XAML compiler.

## Windows x64 Release build

On Windows 10 (17763) or Windows 11, with the .NET 8 SDK installed, from the repo root:

```powershell
dotnet build GeoMineralTrace.sln -c Release
```

`GeoMineralTrace.sln` only defines `Debug|Any CPU` and `Release|Any CPU`. Both map **GeoMineralTrace.App** and **Unbound.Marketplace.WinUI** to **x64** (`win-x64`). That command is the Windows x64 Release build. Leave Platform unset. The solution has no `Release|x64` configuration; the Any CPU mapping already selects x64.

The WinUI projects target `net8.0-windows10.0.19041.0`, minimum `10.0.17763.0`, `UseWinUI` true, `WindowsPackageType` None, platforms `x86;x64;ARM64`, and default runtime identifier `win-x64`. Packages match the app: Windows App SDK `1.7.260224002`, Windows SDK Build Tools `10.0.28000.2526`, CommunityToolkit.Mvvm `8.4.2`.

## Base44 and Supabase follow-ups

Rockhound Buy/Activate still works against the functions that are deployed today, because omitted and `unbound-rockhound` requests behave as before. The allowlist in this repo is source only until the Edge functions are redeployed.

| Change | Why |
|--------|-----|
| Redeploy `license-activate` and `license-validate` | The allowlist is in git. Supabase keeps serving the previous Rockhound-only functions until deploy. |
| Add `license_product_id` on each Product row (`unbound-rockhound` on listing `6a9949122f0c8cf48076190a`) | Every app can tell Buy from Get it without a hardcoded alias. The live Rockhound row does not have this field today; Rockhound supplies the alias locally. |
| Leave Clore.ai Fleet Optimizer unmapped unless it should be an in-app Buy | No `license_product_id` means **Get it**, not Stripe. Map it only in the host that can activate that id. |
| Return `session_id` next to `url` from `createCheckoutSession` | The widget already parses `cs_…` out of the Stripe URL. An explicit field would be sturdier. |
| Add Product rows for ViralForge, Unbound Astrology, and UnboundOS when those apps ship | The live catalog lists Heirloom (coming soon, $79.99), Clore.ai Fleet Optimizer (active, $14.99), and Unbound Rockhound (active, $39.99). |
| Keep Heirloom `coming_soon` until its download is real | Rockhound hides `coming_soon`. A host that sets `HideComingSoon` false shows a badge and does not open checkout. |
| Extend `stripe-webhook` for the other allowlisted ids, then redeploy it | It still refuses non-Rockhound checkouts and still writes `unbound-rockhound`. Set Stripe `metadata.product_id` to the slug when that ships. Missing metadata on this webhook must stay Rockhound. |
| Decide key prefixes and email copy per app | `normalizeLicenseKey` / `generateLicenseKey` / `sendLicenseEmail` are still Unbound Rockhound `UR-` keys and Rockhound mail. |

Buyer path that is already deployed: Stripe success URL → `https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}` and the Resend mail with the key. The in-app page is an additional door to that same key, not a replacement.
