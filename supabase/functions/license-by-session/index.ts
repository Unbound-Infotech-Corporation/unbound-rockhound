/**
 * license-by-session — success-page helper: reveal license after Stripe Checkout.
 *
 * Deploy: supabase functions deploy license-by-session --no-verify-jwt
 * Body: { checkoutSessionId: "cs_..." }
 *
 * Success URL example:
 *   https://unboundinfotech.com/activate?session_id={CHECKOUT_SESSION_ID}
 */

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import { corsPreflight, jsonResponse } from "../_shared/license.ts";

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return corsPreflight();
  if (req.method !== "POST") return jsonResponse({ error: "Method not allowed" }, 405);

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!supabaseUrl || !serviceKey) {
    return jsonResponse({ error: "Server misconfigured" }, 500);
  }

  let body: { checkoutSessionId?: string };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ error: "Invalid JSON" }, 400);
  }

  const sessionId = (body.checkoutSessionId ?? "").trim();
  if (!sessionId.startsWith("cs_")) {
    return jsonResponse({ error: "Invalid checkout session id" }, 400);
  }

  const admin = createClient(supabaseUrl, serviceKey);
  const { data, error } = await admin
    .from("product_licenses")
    .select("license_key, email, status, product_id, created_at")
    .eq("stripe_checkout_session_id", sessionId)
    .maybeSingle();

  if (error) return jsonResponse({ error: error.message }, 500);
  if (!data) {
    return jsonResponse({
      ok: false,
      pending: true,
      message: "License not ready yet — webhook may still be processing. Retry in a few seconds.",
    }, 404);
  }

  return jsonResponse({
    ok: true,
    licenseKey: data.license_key,
    email: data.email,
    status: data.status,
    productId: data.product_id,
    createdAt: data.created_at,
  });
});
