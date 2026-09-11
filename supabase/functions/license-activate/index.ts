/**
 * license-activate — bind a paid license key to this device.
 *
 * Deploy: supabase functions deploy license-activate --no-verify-jwt
 * Body: { licenseKey, deviceFingerprint, deviceLabel? }
 */

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import {
  PRODUCT_ID,
  corsPreflight,
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

  let body: {
    licenseKey?: string;
    deviceFingerprint?: string;
    deviceLabel?: string;
    productId?: string;
  };
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
  if (fingerprint.length < 8 || fingerprint.length > 128) {
    return jsonResponse({ error: "Invalid device fingerprint" }, 400);
  }

  const productId = (body.productId ?? PRODUCT_ID).trim() || PRODUCT_ID;
  const admin = createClient(supabaseUrl, serviceKey);

  const { data: license, error: licErr } = await admin
    .from("product_licenses")
    .select("id, license_key, email, status, max_activations, product_id")
    .eq("license_key", licenseKey)
    .eq("product_id", productId)
    .maybeSingle();

  if (licErr) return jsonResponse({ error: licErr.message }, 500);
  if (!license) return jsonResponse({ error: "License key not found" }, 404);
  if (license.status !== "active") {
    return jsonResponse({
      error: `License is ${license.status}`,
      status: license.status,
    }, 403);
  }

  const { data: existingAct } = await admin
    .from("license_activations")
    .select("id")
    .eq("license_id", license.id)
    .eq("device_fingerprint", fingerprint)
    .is("deactivated_at", null)
    .maybeSingle();

  if (existingAct) {
    await admin
      .from("license_activations")
      .update({ last_seen_at: new Date().toISOString() })
      .eq("id", existingAct.id);

    return jsonResponse({
      ok: true,
      alreadyActivated: true,
      licenseKey: license.license_key,
      email: license.email,
      status: "active",
      productId: license.product_id,
      maxActivations: license.max_activations,
      validatedAtUtc: new Date().toISOString(),
    });
  }

  const { count, error: countErr } = await admin
    .from("license_activations")
    .select("id", { count: "exact", head: true })
    .eq("license_id", license.id)
    .is("deactivated_at", null);

  if (countErr) return jsonResponse({ error: countErr.message }, 500);
  if ((count ?? 0) >= license.max_activations) {
    return jsonResponse({
      error: `Activation limit reached (${license.max_activations} devices). Contact support to reseat a seat.`,
      maxActivations: license.max_activations,
    }, 409);
  }

  const { error: actErr } = await admin.from("license_activations").insert({
    license_id: license.id,
    device_fingerprint: fingerprint,
    device_label: (body.deviceLabel ?? "").slice(0, 120) || null,
  });
  if (actErr) return jsonResponse({ error: actErr.message }, 500);

  return jsonResponse({
    ok: true,
    alreadyActivated: false,
    licenseKey: license.license_key,
    email: license.email,
    status: "active",
    productId: license.product_id,
    maxActivations: license.max_activations,
    validatedAtUtc: new Date().toISOString(),
  });
});
