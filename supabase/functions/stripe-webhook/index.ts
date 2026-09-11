/**
 * stripe-webhook — creates / revokes Unbound Rockhound licenses from Stripe events.
 *
 * Deploy: supabase functions deploy stripe-webhook --no-verify-jwt
 *
 * Secrets:
 *   STRIPE_WEBHOOK_SECRET   (whsec_...)
 *   STRIPE_SECRET_KEY       (optional; only if you expand API lookups)
 *   SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY
 *   RESEND_API_KEY, LICENSE_EMAIL_FROM, LICENSE_DOWNLOAD_URL (optional email)
 *
 * Stripe Dashboard → Webhooks → endpoint:
 *   https://<project>.supabase.co/functions/v1/stripe-webhook
 * Events: checkout.session.completed, charge.refunded, charge.dispute.created
 */

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import {
  PRODUCT_ID,
  generateLicenseKey,
  jsonResponse,
  sendLicenseEmail,
} from "../_shared/license.ts";

const encoder = new TextEncoder();

async function hmacSha256Hex(secret: string, payload: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const sig = await crypto.subtle.sign("HMAC", key, encoder.encode(payload));
  return [...new Uint8Array(sig)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let out = 0;
  for (let i = 0; i < a.length; i++) out |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return out === 0;
}

async function verifyStripeSignature(
  payload: string,
  header: string | null,
  secret: string,
): Promise<boolean> {
  if (!header) return false;
  const parts = Object.fromEntries(
    header.split(",").map((p) => {
      const [k, v] = p.split("=");
      return [k?.trim() ?? "", v?.trim() ?? ""];
    }),
  );
  const timestamp = parts["t"];
  const v1 = parts["v1"];
  if (!timestamp || !v1) return false;

  const ageSec = Math.abs(Math.floor(Date.now() / 1000) - Number(timestamp));
  if (!Number.isFinite(ageSec) || ageSec > 300) return false;

  const expected = await hmacSha256Hex(secret, `${timestamp}.${payload}`);
  return timingSafeEqual(expected, v1);
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return jsonResponse({ error: "Method not allowed" }, 405);
  }

  const webhookSecret = Deno.env.get("STRIPE_WEBHOOK_SECRET");
  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!webhookSecret || !supabaseUrl || !serviceKey) {
    console.error("Missing STRIPE_WEBHOOK_SECRET or Supabase env");
    return jsonResponse({ error: "Server misconfigured" }, 500);
  }

  const payload = await req.text();
  const sigHeader = req.headers.get("stripe-signature");
  const ok = await verifyStripeSignature(payload, sigHeader, webhookSecret);
  if (!ok) {
    return jsonResponse({ error: "Invalid signature" }, 400);
  }

  let event: { id: string; type: string; data: { object: Record<string, unknown> } };
  try {
    event = JSON.parse(payload);
  } catch {
    return jsonResponse({ error: "Invalid JSON" }, 400);
  }

  const admin = createClient(supabaseUrl, serviceKey);

  const { data: existing } = await admin
    .from("stripe_webhook_events")
    .select("event_id")
    .eq("event_id", event.id)
    .maybeSingle();
  if (existing) {
    return jsonResponse({ ok: true, duplicate: true });
  }

  try {
    if (event.type === "checkout.session.completed") {
      await handleCheckoutCompleted(admin, event.data.object);
    } else if (event.type === "charge.refunded" || event.type === "charge.dispute.created") {
      await handleRevokeByCharge(admin, event.data.object, event.type);
    }

    await admin.from("stripe_webhook_events").insert({
      event_id: event.id,
      event_type: event.type,
      payload_summary: event.type,
    });

    return jsonResponse({ ok: true });
  } catch (e) {
    console.error("Webhook handler error", e);
    return jsonResponse({
      error: e instanceof Error ? e.message : "Handler failed",
    }, 500);
  }
});

async function handleCheckoutCompleted(
  admin: ReturnType<typeof createClient>,
  session: Record<string, unknown>,
) {
  const paymentStatus = String(session["payment_status"] ?? "");
  if (paymentStatus && paymentStatus !== "paid" && paymentStatus !== "no_payment_required") {
    console.log("Skipping unpaid session", session["id"]);
    return;
  }

  const sessionId = String(session["id"] ?? "");
  if (!sessionId) throw new Error("Missing checkout session id");

  const { data: already } = await admin
    .from("product_licenses")
    .select("id, license_key")
    .eq("stripe_checkout_session_id", sessionId)
    .maybeSingle();
  if (already) {
    console.log("License already exists for session", sessionId);
    return;
  }

  const meta = (session["metadata"] ?? {}) as Record<string, string>;
  const productId = meta["product_id"] || PRODUCT_ID;
  if (productId !== PRODUCT_ID) {
    console.log("Ignoring non-rockhound product", productId);
    return;
  }

  const email =
    (typeof session["customer_details"] === "object" &&
        session["customer_details"] !== null &&
        typeof (session["customer_details"] as Record<string, unknown>)["email"] === "string"
      ? String((session["customer_details"] as Record<string, unknown>)["email"])
      : null) ??
    (typeof session["customer_email"] === "string" ? session["customer_email"] : null) ??
    meta["email"] ??
    null;

  let licenseKey = generateLicenseKey();
  for (let attempt = 0; attempt < 5; attempt++) {
    const { error } = await admin.from("product_licenses").insert({
      product_id: productId,
      license_key: licenseKey,
      email,
      stripe_checkout_session_id: sessionId,
      stripe_payment_intent_id: session["payment_intent"]
        ? String(session["payment_intent"])
        : null,
      stripe_customer_id: session["customer"] ? String(session["customer"]) : null,
      status: "active",
      max_activations: 3,
    });
    if (!error) break;
    if (error.code === "23505") {
      licenseKey = generateLicenseKey();
      continue;
    }
    throw new Error(error.message);
  }

  if (email) {
    const mail = await sendLicenseEmail({ to: email, licenseKey });
    if (!mail.sent) {
      console.warn("License email not sent:", mail.error);
    }
  } else {
    console.warn("No email on checkout session; license created without delivery mail");
  }
}

async function handleRevokeByCharge(
  admin: ReturnType<typeof createClient>,
  charge: Record<string, unknown>,
  reason: string,
) {
  const paymentIntent = charge["payment_intent"]
    ? String(charge["payment_intent"])
    : null;
  const chargeId = charge["id"] ? String(charge["id"]) : null;

  let query = admin.from("product_licenses").update({
    status: reason.includes("dispute") ? "revoked" : "refunded",
    revoked_at: new Date().toISOString(),
    notes: `Auto-revoked via ${reason}`,
    stripe_charge_id: chargeId,
  });

  if (paymentIntent) {
    query = query.eq("stripe_payment_intent_id", paymentIntent);
  } else if (chargeId) {
    query = query.eq("stripe_charge_id", chargeId);
  } else {
    console.warn("Refund/dispute without payment_intent or charge id");
    return;
  }

  const { error } = await query;
  if (error) throw new Error(error.message);
}
