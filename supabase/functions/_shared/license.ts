/**
 * Shared license key helpers.
 *
 * license_product_id scheme (store field, Stripe metadata.product_id, product_licenses.product_id):
 *   unbound-rockhound   Unbound Rockhound (aliases rockhound / unboundrockhound; omitted id counts as this)
 *   heirloom-unbound    Heirloom Unbound
 *   viralforge          ViralForge
 *   unbound-astrology   Unbound Astrology
 *   unboundos           UnboundOS
 *
 * license-activate and license-validate accept any id in ALLOWED_PRODUCT_IDS.
 * The requested product must match the row. Omitting productId still means unbound-rockhound,
 * so existing Rockhound clients keep working. Keys stay UR-XXXX-XXXX-XXXX-XXXX.
 * stripe-webhook still mints only unbound-rockhound.
 */

export const PRODUCT_ID = "unbound-rockhound";

/** Product ids license-activate and license-validate will bind. Keep in sync with LicenseProductIds.cs. */
export const ALLOWED_PRODUCT_IDS = [
  "unbound-rockhound",
  "heirloom-unbound",
  "viralforge",
  "unbound-astrology",
  "unboundos",
] as const;

const ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1

/** Collapse Stripe / Base44 aliases to a comparable slug. */
export function normalizeProductId(raw: string | null | undefined): string {
  if (!raw || !raw.trim()) return PRODUCT_ID;
  return raw
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^-|-$/g, "");
}

/**
 * Dedicated webhook: missing metadata counts as this product.
 * Rejects other Unbound SKUs (e.g. heirloom) so they do not mint Rockhound keys.
 */
export function isRockhoundProduct(raw: string | null | undefined): boolean {
  const n = normalizeProductId(raw);
  return n === PRODUCT_ID || n === "rockhound" || n === "unboundrockhound";
}

/**
 * Map a request or row product id onto the allowlist.
 * Blank / missing stays unbound-rockhound so Rockhound clients that omit productId do not change.
 * Returns null when the id is not on the allowlist.
 */
export function canonicalAllowedProductId(raw: string | null | undefined): string | null {
  if (raw == null || !String(raw).trim()) return PRODUCT_ID;
  if (isRockhoundProduct(raw)) return PRODUCT_ID;
  const n = normalizeProductId(raw);
  for (const id of ALLOWED_PRODUCT_IDS) {
    if (n === id) return id;
  }
  return null;
}

export function normalizeLicenseKey(raw: string): string {
  if (!raw || !raw.trim()) {
    throw new Error("Paste a license key shaped like UR-XXXX-XXXX-XXXX-XXXX");
  }
  const cleaned = raw.toUpperCase().replace(/[^A-Z0-9]/g, "");
  // Accept UR + 16 chars with or without dashes
  let body = cleaned;
  if (body.startsWith("UR")) body = body.slice(2);
  if (body.length !== 16) {
    throw new Error("License key must look like UR-XXXX-XXXX-XXXX-XXXX (16 characters after UR)");
  }
  return `UR-${body.slice(0, 4)}-${body.slice(4, 8)}-${body.slice(8, 12)}-${body.slice(12, 16)}`;
}

export function generateLicenseKey(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  let body = "";
  for (let i = 0; i < 16; i++) {
    body += ALPHABET[bytes[i]! % ALPHABET.length];
  }
  return `UR-${body.slice(0, 4)}-${body.slice(4, 8)}-${body.slice(8, 12)}-${body.slice(12, 16)}`;
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
    },
  });
}

export function corsPreflight(): Response {
  return new Response(null, {
    status: 204,
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    },
  });
}

export async function sendLicenseEmail(opts: {
  to: string;
  licenseKey: string;
  downloadUrl?: string;
}): Promise<{ sent: boolean; error?: string }> {
  const apiKey = Deno.env.get("RESEND_API_KEY");
  const from = Deno.env.get("LICENSE_EMAIL_FROM") ?? "Unbound Rockhound <noreply@unboundinfotech.com>";
  if (!apiKey) {
    return { sent: false, error: "RESEND_API_KEY not configured" };
  }

  const download = opts.downloadUrl ??
    Deno.env.get("LICENSE_DOWNLOAD_URL") ??
    "https://unboundinfotech.com/products/unbound-rockhound";

  const html = `
    <p>Thank you for purchasing <strong>Unbound Rockhound</strong>.</p>
    <p>Your license key:</p>
    <p style="font-size:18px;font-family:monospace;letter-spacing:1px"><strong>${opts.licenseKey}</strong></p>
    <p>Download / install: <a href="${download}">${download}</a></p>
    <p>In the app open <strong>Activate</strong> (or About → Activate license), paste the key, and click Activate.</p>
    <p>You may activate on up to 3 devices. Questions: support@unboundinfotech.com</p>
    <p>— Unbound Infotech Corporation</p>
  `;

  try {
    const res = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${apiKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from,
        to: [opts.to],
        subject: "Your Unbound Rockhound license key",
        html,
      }),
    });
    if (!res.ok) {
      const text = await res.text();
      return { sent: false, error: `Resend ${res.status}: ${text}` };
    }
    return { sent: true };
  } catch (e) {
    return { sent: false, error: e instanceof Error ? e.message : String(e) };
  }
}
