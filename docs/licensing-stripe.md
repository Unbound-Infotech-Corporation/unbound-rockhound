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
| `LICENSE_DOWNLOAD_URL` | optional | link in email body |

## 4. Stripe product setup

1. Create Product **Unbound Rockhound** with Price **$39.99** (launch) one-time.
2. Create a **Payment Link** or Checkout Session with:
   - `metadata.product_id` = `unbound-rockhound`
   - Collect email
   - Success URL (optional):  
     `https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}`  
     (page can call `license-by-session` to show the key)
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
- [ ] Test Checkout creates row in `product_licenses`
- [ ] Email arrives (or `license-by-session` returns key)
- [ ] App Activate succeeds; Analyze unlocks
- [ ] Second/third device OK; fourth returns 409
- [ ] Refund webhook sets status `refunded`; next validate clears entitlement
- [ ] Publish feed; Settings → Check now shows banner
