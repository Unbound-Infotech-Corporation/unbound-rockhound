/**
 * reddit-discover — scheduled ingest for curated rockhounding subreddits.
 *
 * Deploy: supabase functions deploy reddit-discover --no-verify-jwt
 * (or set verify_jwt=false in config). Protect with header `x-cron-secret`
 * matching env CRON_SECRET. Prefer cron secret for scheduled jobs; do not
 * expose CRON_SECRET to clients.
 *
 * Env (Edge secrets):
 *   SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY (required for writes)
 *   CRON_SECRET (required — request rejected without matching header)
 *   REDDIT_CLIENT_ID, REDDIT_CLIENT_SECRET, REDDIT_USER_AGENT (optional OAuth)
 *
 * Graceful degradation: if Reddit OAuth and public JSON both fail, returns
 * HTTP 200 { ok:false, degraded:true, error } — never 500 that breaks clients.
 *
 * Also upserts community_location_mentions with US-state centroid heuristics
 * when a state name/code appears in title/snippet (lat/lon may remain null).
 */

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";

const SUBREDDITS = [
  "Rockhounding",
  "geology",
  "mineralcollecting",
  "fossilid",
] as const;

const US_STATE_CENTROIDS: Record<string, { lat: number; lon: number; name: string }> = {
  AL: { lat: 32.806671, lon: -86.79113, name: "Alabama" },
  AK: { lat: 61.370716, lon: -152.404419, name: "Alaska" },
  AZ: { lat: 33.729759, lon: -111.431221, name: "Arizona" },
  AR: { lat: 34.969704, lon: -92.373123, name: "Arkansas" },
  CA: { lat: 36.116203, lon: -119.681564, name: "California" },
  CO: { lat: 39.059811, lon: -105.311104, name: "Colorado" },
  CT: { lat: 41.597782, lon: -72.755371, name: "Connecticut" },
  DE: { lat: 39.318523, lon: -75.507141, name: "Delaware" },
  FL: { lat: 27.766279, lon: -81.686783, name: "Florida" },
  GA: { lat: 33.040619, lon: -83.643074, name: "Georgia" },
  HI: { lat: 21.094318, lon: -157.498337, name: "Hawaii" },
  ID: { lat: 44.240459, lon: -114.478828, name: "Idaho" },
  IL: { lat: 40.349457, lon: -88.986137, name: "Illinois" },
  IN: { lat: 39.849426, lon: -86.258278, name: "Indiana" },
  IA: { lat: 42.011539, lon: -93.210526, name: "Iowa" },
  KS: { lat: 38.5266, lon: -96.726486, name: "Kansas" },
  KY: { lat: 37.66814, lon: -84.670067, name: "Kentucky" },
  LA: { lat: 31.169546, lon: -91.867805, name: "Louisiana" },
  ME: { lat: 44.693947, lon: -69.381927, name: "Maine" },
  MD: { lat: 39.063946, lon: -76.802101, name: "Maryland" },
  MA: { lat: 42.230171, lon: -71.530106, name: "Massachusetts" },
  MI: { lat: 43.326618, lon: -84.536095, name: "Michigan" },
  MN: { lat: 45.694454, lon: -93.900192, name: "Minnesota" },
  MS: { lat: 32.741646, lon: -89.678696, name: "Mississippi" },
  MO: { lat: 38.456085, lon: -92.288368, name: "Missouri" },
  MT: { lat: 46.921925, lon: -110.454353, name: "Montana" },
  NE: { lat: 41.12537, lon: -98.268082, name: "Nebraska" },
  NV: { lat: 38.313515, lon: -117.055374, name: "Nevada" },
  NH: { lat: 43.452492, lon: -71.563896, name: "New Hampshire" },
  NJ: { lat: 40.298904, lon: -74.521011, name: "New Jersey" },
  NM: { lat: 34.840515, lon: -106.248482, name: "New Mexico" },
  NY: { lat: 42.165726, lon: -74.948051, name: "New York" },
  NC: { lat: 35.630066, lon: -79.806419, name: "North Carolina" },
  ND: { lat: 47.528912, lon: -99.784012, name: "North Dakota" },
  OH: { lat: 40.388783, lon: -82.764915, name: "Ohio" },
  OK: { lat: 35.565342, lon: -96.928917, name: "Oklahoma" },
  OR: { lat: 44.572021, lon: -122.070938, name: "Oregon" },
  PA: { lat: 40.590752, lon: -77.209755, name: "Pennsylvania" },
  RI: { lat: 41.680893, lon: -71.51178, name: "Rhode Island" },
  SC: { lat: 33.856892, lon: -80.945007, name: "South Carolina" },
  SD: { lat: 44.299782, lon: -99.438828, name: "South Dakota" },
  TN: { lat: 35.747845, lon: -86.692345, name: "Tennessee" },
  TX: { lat: 31.054487, lon: -97.563461, name: "Texas" },
  UT: { lat: 40.150032, lon: -111.862434, name: "Utah" },
  VT: { lat: 44.045876, lon: -72.710686, name: "Vermont" },
  VA: { lat: 37.769337, lon: -78.169968, name: "Virginia" },
  WA: { lat: 47.400902, lon: -121.490494, name: "Washington" },
  WV: { lat: 38.491226, lon: -80.954453, name: "West Virginia" },
  WI: { lat: 44.268543, lon: -89.616508, name: "Wisconsin" },
  WY: { lat: 42.755966, lon: -107.30249, name: "Wyoming" },
};

type RedditListingChild = {
  data?: {
    id?: string;
    name?: string;
    title?: string;
    selftext?: string;
    permalink?: string;
    url?: string;
    score?: number;
    created_utc?: number;
    subreddit?: string;
  };
};

type RedditListing = {
  data?: { children?: RedditListingChild[] };
};

export type DegradedResponse = {
  ok: false;
  degraded: true;
  error: string;
  upserted?: number;
  mentions?: number;
};

export type OkResponse = {
  ok: true;
  degraded: false;
  upserted: number;
  mentions: number;
  source: "oauth" | "public_json";
  subreddits: string[];
};

/** Pure helper — unit-testable degraded shape for Reddit total failure. */
export function buildDegradedResponse(error: string): DegradedResponse {
  return { ok: false, degraded: true, error };
}

function truncateSnippet(text: string, max = 600): string {
  const t = (text ?? "").replace(/\s+/g, " ").trim();
  if (t.length <= max) return t;
  return t.slice(0, max - 1) + "…";
}

function resolveStateHint(text: string): {
  code: string;
  name: string;
  lat: number;
  lon: number;
} | null {
  const hay = text ?? "";
  for (const [code, info] of Object.entries(US_STATE_CENTROIDS)) {
    const nameRe = new RegExp(`\\b${info.name}\\b`, "i");
    const codeRe = new RegExp(`\\b${code}\\b`);
    if (nameRe.test(hay) || codeRe.test(hay)) {
      return { code, name: info.name, lat: info.lat, lon: info.lon };
    }
  }
  return null;
}

async function getRedditOAuthToken(
  clientId: string,
  clientSecret: string,
  userAgent: string,
): Promise<string | null> {
  try {
    const basic = btoa(`${clientId}:${clientSecret}`);
    const body = new URLSearchParams({
      grant_type: "client_credentials",
    });
    const resp = await fetch("https://www.reddit.com/api/v1/access_token", {
      method: "POST",
      headers: {
        Authorization: `Basic ${basic}`,
        "Content-Type": "application/x-www-form-urlencoded",
        "User-Agent": userAgent,
      },
      body,
    });
    if (!resp.ok) return null;
    const json = await resp.json() as { access_token?: string };
    return json.access_token ?? null;
  } catch {
    return null;
  }
}

async function fetchSubredditListing(
  subreddit: string,
  opts: { accessToken?: string; userAgent: string },
): Promise<RedditListingChild[]> {
  const path = `/r/${encodeURIComponent(subreddit)}/new.json?limit=25`;
  if (opts.accessToken) {
    const resp = await fetch(`https://oauth.reddit.com${path}`, {
      headers: {
        Authorization: `Bearer ${opts.accessToken}`,
        "User-Agent": opts.userAgent,
      },
    });
    if (!resp.ok) throw new Error(`oauth listing ${subreddit} HTTP ${resp.status}`);
    const json = await resp.json() as RedditListing;
    return json.data?.children ?? [];
  }

  const resp = await fetch(`https://www.reddit.com${path}`, {
    headers: { "User-Agent": opts.userAgent },
  });
  if (!resp.ok) throw new Error(`public listing ${subreddit} HTTP ${resp.status}`);
  const json = await resp.json() as RedditListing;
  return json.data?.children ?? [];
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

Deno.serve(async (req) => {
  try {
    if (req.method === "OPTIONS") {
      return new Response(null, {
        status: 204,
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type, x-cron-secret",
        },
      });
    }

    if (req.method !== "POST" && req.method !== "GET") {
      return jsonResponse({ ok: false, degraded: true, error: "Method not allowed" }, 200);
    }

    const cronSecret = Deno.env.get("CRON_SECRET") ?? "";
    const headerSecret = req.headers.get("x-cron-secret") ?? "";
    if (!cronSecret || headerSecret !== cronSecret) {
      // Still 200 so schedulers/clients don't treat auth miss as hard outage shape,
      // but mark degraded — operators must set CRON_SECRET.
      return jsonResponse(
        buildDegradedResponse("Unauthorized: x-cron-secret missing or does not match CRON_SECRET"),
      );
    }

    const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? "";
    const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
    if (!supabaseUrl || !serviceKey) {
      return jsonResponse(
        buildDegradedResponse("Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY"),
      );
    }

    const userAgent =
      Deno.env.get("REDDIT_USER_AGENT") ??
      "UnboundRockhound/1.0 (geo-mineral-trace; contact: ops@local)";
    const clientId = Deno.env.get("REDDIT_CLIENT_ID") ?? "";
    const clientSecret = Deno.env.get("REDDIT_CLIENT_SECRET") ?? "";

    let accessToken: string | null = null;
    let source: "oauth" | "public_json" = "public_json";

    if (clientId && clientSecret) {
      accessToken = await getRedditOAuthToken(clientId, clientSecret, userAgent);
      if (accessToken) source = "oauth";
    }

    const errors: string[] = [];
    const posts: Array<{
      reddit_fullname_id: string;
      subreddit: string;
      title: string;
      snippet: string;
      url: string;
      score: number;
      posted_at: string | null;
    }> = [];

    for (const sub of SUBREDDITS) {
      try {
        let children: RedditListingChild[];
        try {
          children = await fetchSubredditListing(sub, {
            accessToken: accessToken ?? undefined,
            userAgent,
          });
        } catch (oauthErr) {
          if (accessToken) {
            // Fall back to public JSON for this subreddit
            children = await fetchSubredditListing(sub, { userAgent });
            source = "public_json";
          } else {
            throw oauthErr;
          }
        }

        for (const child of children) {
          const d = child.data;
          if (!d?.id || !d.title) continue;
          const permalink = d.permalink
            ? `https://www.reddit.com${d.permalink}`
            : (d.url ?? "");
          if (!permalink) continue;
          const thingId = d.name?.startsWith("t3_") ? d.name : `t3_${d.id}`;
          posts.push({
            reddit_fullname_id: thingId,
            subreddit: d.subreddit ?? sub,
            title: truncateSnippet(d.title, 500),
            snippet: truncateSnippet(d.selftext ?? "", 600),
            url: permalink,
            score: typeof d.score === "number" ? d.score : 0,
            posted_at: d.created_utc
              ? new Date(d.created_utc * 1000).toISOString()
              : null,
          });
        }
      } catch (e) {
        errors.push(`${sub}: ${e instanceof Error ? e.message : String(e)}`);
      }
    }

    if (posts.length === 0) {
      return jsonResponse(
        buildDegradedResponse(
          errors.length > 0
            ? `Reddit fetch failed for all subreddits: ${errors.join("; ")}`
            : "Reddit returned no posts",
        ),
      );
    }

    const supabase = createClient(supabaseUrl, serviceKey, {
      auth: { persistSession: false, autoRefreshToken: false },
    });

    const { data: upsertedRows, error: upsertErr } = await supabase
      .from("reddit_discoveries")
      .upsert(posts, { onConflict: "reddit_fullname_id" })
      .select("id, reddit_thing_id, title, snippet, url, subreddit");

    if (upsertErr) {
      return jsonResponse(
        buildDegradedResponse(`Upsert failed: ${upsertErr.message}`),
      );
    }

    const rows = upsertedRows ?? [];
    let mentions = 0;

    for (const row of rows) {
      const text = `${row.title ?? ""} ${row.snippet ?? ""}`;
      const hint = resolveStateHint(text);
      const mentionText = truncateSnippet(row.title ?? "Reddit mention", 1000);
      const payload = {
        reddit_discovery_id: row.id,
        source_url: row.url,
        source_label: `r/${row.subreddit}`,
        mention_text: mentionText,
        lat: hint?.lat ?? null,
        lon: hint?.lon ?? null,
        place_hint: hint ? `${hint.name} (${hint.code})` : null,
        confidence: hint ? 0.35 : 0.2,
      };

      // Idempotent-ish: delete prior mention for this discovery then insert one
      await supabase
        .from("community_location_mentions")
        .delete()
        .eq("reddit_discovery_id", row.id);

      const { error: menErr } = await supabase
        .from("community_location_mentions")
        .insert(payload);

      if (!menErr) mentions += 1;
    }

    const body: OkResponse = {
      ok: true,
      degraded: false,
      upserted: rows.length,
      mentions,
      source,
      subreddits: [...SUBREDDITS],
    };
    if (errors.length > 0) {
      return jsonResponse({ ...body, warnings: errors });
    }
    return jsonResponse(body);
  } catch (e) {
    return jsonResponse(
      buildDegradedResponse(e instanceof Error ? e.message : String(e)),
    );
  }
});
