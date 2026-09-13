# Unbound Rockhound — Stripe licensing + updates

One-time purchase via Stripe → license key email → Activate in the app.
Updates ship via a public JSON feed (in-app banner → download Setup/zip).

## Architecture

```
Stripe Checkout / Payment Link
        │ webhook (checkout.session.completed)
        ▼
Supabase Edge: stripe-webhook ──► product_licenses row + Resend email
        │
        ▼
Desktop Activate page ──► license-activate (binds device)
        │
        ▼
Periodic license-validate (refund/revoke)
```

Updates (independent of Stripe):

```
Package-Release.ps1 → dist zip/setup
Publish-UpdateFeed.ps1 → updates/unbound-rockhound/latest.json
Upload feed + binaries to HTTPS host
AppUpdateService polls feed on launch / Settings
```

## 1. Apply database migration

Run `supabase/migrations/20260911_phase4_licenses.sql` in the Supabase SQL editor
(project used for Unbound Rockhound — same URL/anon key as cloud sign-in).

## 2. Deploy Edge functions

```bash
supabase functions deploy stripe-webhook --no-verify-jwt
supabase functions deploy license-activate --no-verify-jwt
supabase functions deploy license-validate --no-verify-jwt
supabase functions deploy license-by-session --no-verify-jwt
```

## 3. Secrets (Supabase → Edge Functions → Secrets)

| Secret | Required | Notes |
|--------|----------|--------|
| `STRIPE_WEBHOOK_SECRET` | yes | `whsec_...` from Stripe webhook endpoint |
| `SUPABASE_SERVICE_ROLE_KEY` | auto | usually already present |
| `RESEND_API_KEY` | recommended | emails license key after purchase |
| `LICENSE_EMAIL_FROM` | optional | default `Unbound Rockhound <noreply@unboundinfotech.com>` |
| `LICENSE_DOWNLOAD_URL` | optional | link in email body (zip / product page) |

Do **not** put live secrets in this repo. Dashboard / CI secrets only.

## 4. Stripe product setup

1. Create Product **Unbound Rockhound** with Price **$39.99** (launch) one-time.
2. Create a **Payment Link** or Checkout Session with:
   - `metadata.product_id` = `unbound-rockhound` (aliases like `Unbound Rockhound` are accepted; the row is always stored as `unbound-rockhound`)
   - Collect email (required for Resend; Success URL still works without it)
   - Success URL:  
     `https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}`  
     Host `web/activate/index.html` there. The page calls `license-by-session` and shows the key if the purchase email is late.
3. Webhook endpoint:  
   `https://<project-ref>.supabase.co/functions/v1/stripe-webhook`  
   Events: `checkout.session.completed`, `charge.refunded`, `charge.dispute.created`

## 5. Desktop behavior

- **14-day trial** starts on first launch (stored DPAPI-protected under `%LocalAppData%\UnboundRockhound\license.bin`).
- After trial: Analyze / Map / research tools require activation.
- Free without key: Home, About, Activate, Sign in, Techniques, Settings.
- Up to **3 devices** per key.
- Online re-check about every **7 days** (offline grace keeps last good activation until then).

Configure the same Supabase URL + anon key under **Sign in** (already used for cloud profiles).

## 6. Shipping updates to customers

1. Bump version / run `.\scripts\Package-Release.ps1 -Version X.Y.Z -BuildSetup`
2. Upload `UnboundRockhound-Setup-X.Y.Z.exe` (and/or zip) to your HTTPS download host
3. Run:

```powershell
.\scripts\Publish-UpdateFeed.ps1 `
  -Version X.Y.Z `
  -DownloadUrl "https://unboundinfotech.com/downloads/UnboundRockhound-Setup-X.Y.Z.exe" `
  -Notes "Bug fixes and glossary updates"
```

4. Upload `updates/unbound-rockhound/latest.json` to the public feed URL  
   (default in app: `https://updates.unboundrockhound.app/unbound-rockhound/latest.json`).

Until that host exists, set **Settings → Software updates → Feed URL** to a working HTTPS (or local file) path, or host the JSON on GitHub raw / Cloudflare R2 / your site.

Customers see an in-app banner and click **Download update**. Cases under LocalAppData are not overwritten.

## 7. Manual / support license

```sql
insert into public.product_licenses (product_id, license_key, email, status, max_activations, notes)
values (
  'unbound-rockhound',
  'UR-TEST-AAAA-BBBB-CCCC',  -- must match key format
  'customer@unboundinfotech.com',
  'active',
  3,
  'manual comps'
);
```

Generate a real key with the Edge helper alphabet (or activate after inserting a key produced by a one-off script). Easiest support path: run a test Stripe Checkout in test mode.

## 8. Test checklist

- [ ] Migration applied
- [ ] Functions deployed; webhook receives Stripe CLI `stripe listen --forward-to ...`
- [ ] Test Checkout creates row in `product_licenses` with `product_id=unbound-rockhound` and key `UR-xxxx-xxxx-xxxx-xxxx`
- [ ] Email arrives (or `license-by-session` / `/activate?session_id=` returns key)
- [ ] App Activate succeeds; Analyze unlocks
- [ ] Second/third device OK; fourth returns 409
- [ ] Refund webhook sets status `refunded`; next validate clears entitlement
- [ ] Publish feed; Settings → Check now shows banner

## 9. Operator smoke checklist (buyers must not get stuck)

Live this list **before** flipping Base44 Status to `active`. Secrets stay in Stripe / Supabase / Resend dashboards — never in git.

| Must be live | Why buyers fail without it |
|--------------|----------------------------|
| Stripe Product / Payment Link `metadata.product_id=unbound-rockhound` | Webhook ignores other products; missing id is OK on this dedicated endpoint |
| Stripe webhook → `…/functions/v1/stripe-webhook` (`checkout.session.completed`, `charge.refunded`, `charge.dispute.created`) | No `product_licenses` row → Activate says key not found |
| Edge functions deployed (`stripe-webhook`, `license-activate`, `license-by-session`, `license-validate`) + migration applied | App and Success URL have nothing to call |
| `RESEND_API_KEY` (+ verified `LICENSE_EMAIL_FROM` domain) | No inbox key; buyer still OK if `/activate?session_id=` is hosted |
| `LICENSE_DOWNLOAD_URL` or Base44 **Download URL** | Email / store must give the zip **and** a path to the key |
| Base44 Type=`download`, Status=`active`, working zip URL | Store delivers the binary; license key is **parallel** via Stripe+Resend |
| Hosted `web/activate/index.html` (project ref filled in) | Fallback when Resend fails (webhook writes a notes row) |
| Desktop Sign in → same Supabase URL + **anon** key | Activate cannot reach `license-activate` otherwise |
| Support mailbox `support@unboundinfotech.com` | Manual comps / 4th-device reseat |

**Base44 Type=`download` vs `license`:** prefer `download` so Checkout hands the zip immediately. Do **not** put a static key in Delivery Instructions. If Base44 later exposes Type=`license` as a second SKU, keep Stripe webhook as the source of truth for `UR-` keys (one key per paid session, max 3 devices, 14-day trial in-app). Do not invent a second key namespace.

**Silent-failure checks:** after a test purchase, open `product_licenses` — if `notes` mentions Resend, the key still exists; use `/activate` or SQL. If Activate fails with HTTP body text, the app now surfaces it (wrong URL, bad key, 409 seat limit) instead of a blank error.
