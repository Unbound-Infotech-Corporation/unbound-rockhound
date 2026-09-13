/**
 * license-validate — refresh entitlement for an already-activated device.
 *
 * Deploy: supabase functions deploy license-validate --no-verify-jwt
 * Body: { licenseKey, deviceFingerprint }
 */

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import {
  PRODUCT_ID,
  corsPreflight,
  isRockhoundProduct,
  jsonResponse,
  normalizeLicenseKey,
} from "../_shared/license.ts";

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return corsPreflight();
  if (req.method !== "POST") return jsonResponse({ error: "Method not allowed" }, 405);

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!supabaseUrl || !serviceKey) {
    return jsonResponse({ error: "Server misconfigured" }, 500);
  }

  let body: { licenseKey?: string; deviceFingerprint?: string; productId?: string };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ error: "Invalid JSON" }, 400);
  }

  let licenseKey: string;
  try {
    licenseKey = normalizeLicenseKey(body.licenseKey ?? "");
  } catch (e) {
    return jsonResponse({ error: e instanceof Error ? e.message : "Bad key" }, 400);
  }

  const fingerprint = (body.deviceFingerprint ?? "").trim();
  if (fingerprint.length < 8) {
    return jsonResponse({ error: "Invalid device fingerprint" }, 400);
  }

  const requestedProduct = (body.productId ?? PRODUCT_ID).trim() || PRODUCT_ID;
  if (!isRockhoundProduct(requestedProduct)) {
    return jsonResponse({ ok: false, error: "wrong_product", status: "missing" }, 400);
  }
  const admin = createClient(supabaseUrl, serviceKey);

  const { data: byKey, error: licErr } = await admin
    .from("product_licenses")
    .select("id, license_key, email, status, max_activations, product_id")
    .eq("license_key", licenseKey)
    .maybeSingle();

  if (licErr) return jsonResponse({ error: licErr.message }, 500);
  if (!byKey) return jsonResponse({ ok: false, error: "not_found", status: "missing" }, 404);
  if (!isRockhoundProduct(byKey.product_id)) {
    return jsonResponse({ ok: false, error: "wrong_product", status: "missing" }, 404);
  }
  const license = byKey;
  if (license.status !== "active") {
    return jsonResponse({
      ok: false,
      error: "inactive",
      status: license.status,
    }, 403);
  }

  const { data: act, error: actErr } = await admin
    .from("license_activations")
    .select("id")
    .eq("license_id", license.id)
    .eq("device_fingerprint", fingerprint)
    .is("deactivated_at", null)
    .maybeSingle();

  if (actErr) return jsonResponse({ error: actErr.message }, 500);
  if (!act) {
    return jsonResponse({
      ok: false,
      error: "device_not_activated",
      status: "unbound",
    }, 403);
  }

  await admin
    .from("license_activations")
    .update({ last_seen_at: new Date().toISOString() })
    .eq("id", act.id);

  return jsonResponse({
    ok: true,
    licenseKey: license.license_key,
    email: license.email,
    status: "active",
    productId: license.product_id,
    maxActivations: license.max_activations,
    validatedAtUtc: new Date().toISOString(),
  });
});
