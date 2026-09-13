# Web Activate page (`/activate`)

Static success page for Stripe Checkout:

`https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}`

It calls the public `license-by-session` Edge function on project `iltdlxhvxlwirqkyzhcc` (session id required; no secrets in this file). Buyer-facing copy only — do not add operator footnotes to `index.html`.

## Host

1. Upload `index.html` to the Unbound Infotech site as `/activate` (or `/activate/index.html`). GitHub Pages also serves this folder at `/activate/`.
2. The default functions URL is already set. Optional override: `?fn=https://<ref>.supabase.co/functions/v1`.
3. Set Stripe Checkout / Payment Link Success URL to the page above.
4. Never put service-role keys or Stripe secrets in this HTML.

Buyers still get the Windows zip from Base44 **Type = download**. This page is the fallback when Resend is late.
