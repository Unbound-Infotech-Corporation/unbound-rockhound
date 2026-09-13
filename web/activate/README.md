# Web Activate page (`/activate`)

Static success page for Stripe Checkout:

`https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}`

It calls the public `license-by-session` Edge function (session id required; no secrets in this file).

## Host

1. Upload `index.html` to the Unbound Infotech site as `/activate` (or `/activate/index.html`).
2. Replace `YOUR_PROJECT` in the script with the live Supabase project ref, **or** keep the placeholder and append `?fn=https://<ref>.supabase.co/functions/v1` on the Stripe Success URL.
3. Set Stripe Checkout / Payment Link Success URL to the page above.
4. Never put `SUPABASE_SERVICE_ROLE_KEY` or Stripe secrets in this HTML.

Buyers still get the Windows zip from Base44 **Type = download**. This page is the fallback when Resend is late.
