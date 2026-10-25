# Unbound Marketplace widget

Shared desktop catalog for Unbound Infotech apps. The pilot host is **Unbound Rockhound**. The same projects are meant to drop into Heirloom Unbound, ViralForge, Unbound Astrology, and UnboundOS later.

The widget does not collect card numbers. **Buy** asks the public store to create a Stripe Checkout session and opens that link in the default browser. After payment, the page polls the existing `license-by-session` Edge function or accepts a pasted key, then activates through the host app's current license service.

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

- Catalog id `6a9949122f0c8cf48076190a` and the name **Unbound Rockhound** map to license product id `unbound-rockhound`.
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
   - `HostLicenseProductId` — the id your webhook stores (Rockhound uses `unbound-rockhound`).
   - `HostProductDisplayName` — the customer-facing name, for example **Heirloom Unbound**.
   - `LicenseProductIdByCatalogId` / `LicenseProductIdByName` — until Base44 rows include `license_product_id`.
3. Implement three host hooks:
   - `IMarketplaceKeyActivator` — call that app's existing activate function **once**.
   - `IMarketplaceOwnershipSource` — return license product ids this install already holds.
   - `IMarketplaceSupabaseSettings` — URL and anon key for `license-by-session`. Do not embed the service role key.
4. Register `MarketplaceCatalogClient`, `CheckoutLauncher`, `SupabaseLicenseSessionClient`, and a `MarketplaceHost`.
5. Set `MarketplaceHostContext.Current` before the first navigation.
6. Navigate to `Unbound.Marketplace.WinUI.MarketplacePage`, or embed `MarketplacePanel` on an existing page.

A product is activated in-process only when its `license_product_id` equals `HostLicenseProductId`. Other listings still open Stripe Checkout, then tell the buyer to finish activation in that product's app. That keeps one app from binding another product's key.

`license-activate` in this repo still rejects keys that are not for Unbound Rockhound. Other apps need their own activate function (or a shared one that accepts their product id) before Owned can flip for those titles.

## Tests

```powershell
dotnet test tests/Unbound.Marketplace.Tests/Unbound.Marketplace.Tests.csproj
```

The WinUI app project builds on Windows (`net8.0-windows`). The marketplace library and these tests build on any OS with the .NET 8 SDK.

## Base44 and Supabase follow-ups

Nothing in Supabase has to change for the Rockhound pilot. `license-by-session`, `license-activate`, `license-validate`, and `stripe-webhook` stay as they are. Stripe metadata for this listing should remain `product_id=unbound-rockhound`.

Recommended store changes, not required for the pilot:

| Change | Why |
|--------|-----|
| Add `license_product_id` on each Product row (`unbound-rockhound` on listing `6a9949122f0c8cf48076190a`) | Every app can tell Owned from Buy without a hardcoded alias. The live row does not have this field today; Rockhound supplies the alias locally. |
| Return `session_id` next to `url` from `createCheckoutSession` | The widget already parses `cs_…` out of the Stripe URL. An explicit field would be sturdier. |
| Add Product rows for ViralForge, Unbound Astrology, and UnboundOS when those apps ship | The live catalog currently lists Heirloom (coming soon, $79.99), Clore.ai Fleet Optimizer (active, $14.99), and Unbound Rockhound (active, $39.99). The widget shows whatever the store returns. |
| Keep Heirloom `coming_soon` until its download is real | Coming soon rows render a badge and do not open checkout. |
| Decide whether Clore.ai Fleet Optimizer should stay `active` on this storefront | It is a real row, so the marketplace lists it and can open Stripe. This Rockhound install will not activate that license. |

Buyer path that is already deployed: Stripe success URL → `https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}` and the Resend mail with the key. The in-app page is an additional door to that same key, not a replacement.
