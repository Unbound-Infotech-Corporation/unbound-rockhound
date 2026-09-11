# Unbound Rockhound — Social Layer

Shared cloud identity and community features backed by **Supabase**.  
Desktop: [GeoMineralTrace](../) (Unbound Rockhound WinUI).  
Android: companion app at `F:\Heirloom\UnboundRockhound`.

## Locked design decisions (all later phases)

These are fixed for the product; do not reverse casually.

### 1. Location privacy default

Shared map pins **default to fuzzed / approximate** location (randomized within roughly **1–3 miles**, user-configurable radius). Showing the **exact** coordinate requires an **explicit per-post opt-in** toggle. Public rockhounding communities strip sites when exact pins leak by default — exact is never the default.

### 2. News aggregation source

World / USA / local rockhounding & mineral news uses an **RSS aggregator** (geology / mineral-collecting publications, USGS, relevant subreddit RSS, club sites), **not** a generic news API. “Local” is primarily **region/state geotagging** of aggregated items rather than true hyperlocal feeds.

### 3. Moderation baseline

Even a lite social layer needs **report + block from day one of messaging** (Phase 4). Do not ship DMs without them. Report queue / admin polish is Phase 5. Phase 1 does **not** create empty moderation UI; Phase 4 adds `social_reports` / `social_blocks` (or equivalent) **before** Realtime DMs.

## Phase status

| Phase | Scope | Status |
|-------|--------|--------|
| **1** | Supabase auth, profiles, avatar Storage, RLS | **Verified on production (2026-09-11 WORKLOG)** |
| **2** | Shared forum (categories / threads / posts / likes) | **Implemented + RLS verified (2026-09-11)** |
| **2b / C** | Reddit discovery + FB directory + community map mentions | **Schema + edge fn + clients landed; Reddit OAuth secrets operator** |
| 3 | Comments + fuzzed location shares | Not started |
| 4 | DMs + report/block | Not started |
| 5 | Report queue + polish | Not started |

## Phase 1 schema (summary)

- `public.profiles` — `id` = `auth.users.id`, display name, handle, bio, avatar_url, home_region.
- Trigger creates a profile row on signup.
- Bucket `avatars` — write only under `{auth.uid()}/…`; authenticated read.
- RLS: any authenticated user can **read** all profiles; only the owner can **insert/update/delete** their row.

SQL: [`../supabase/migrations/20260903_phase1_profiles.sql`](../supabase/migrations/20260903_phase1_profiles.sql)

## Client configuration

Never commit the **service role** key.

| Key | Where |
|-----|--------|
| `SUPABASE_URL` | Project URL |
| `SUPABASE_ANON_KEY` | Anon (public) key |

**WinUI:** `%LocalAppData%\UnboundRockhound\supabase.json` (or Settings fields):

```json
{
  "url": "https://YOUR_PROJECT.supabase.co",
  "anonKey": "eyJ..."
}
```

**Android:** `local.properties` (repo root) or in-app Settings:

```
SUPABASE_URL=https://YOUR_PROJECT.supabase.co
SUPABASE_ANON_KEY=eyJ...
```

Thin OkHttp clients (Kotlin 1.9–compatible) live in the `:cloud` module — same REST surface as WinUI. Offline local `UserProfile` remains the fallback when signed out.

See [`.env.example`](../.env.example).

## Automated helpers

| Helper | Purpose |
|--------|---------|
| [`scripts/verify-phase1-rls.ps1`](../scripts/verify-phase1-rls.ps1) | Sign in A+B; assert A can SELECT B; assert A cannot PATCH B |
| Android `RlsProbe` | Same checks from the `:cloud` module |
| Core `SocialModelsTests` | Session expiry skew + config validation |

## Phase 1 verification checklist

Complete **before** starting Phase 2:

1. [ ] Apply migration + Storage policies in the Supabase SQL editor (or CLI).
2. [ ] Configure URL + anon key on WinUI and/or Android.
3. [ ] Create test accounts **A** and **B** (email/password).
4. [ ] Each can open the other’s **Profile** page.
5. [ ] Neither can edit the other’s display name / bio / avatar in the app UI.
6. [ ] With **A**’s access token, raw REST fails to mutate B:

```http
PATCH /rest/v1/profiles?id=eq.<B_UUID>
Authorization: Bearer <A_ACCESS_TOKEN>
apikey: <ANON_KEY>
Content-Type: application/json
Prefer: return=representation

{"bio":"hacked"}
```

Expect **0 rows** or RLS denial (not a successful update of B).

7. [ ] Sign out / restart → session restores when signed in; sign out clears session.
8. [ ] Log results in `WORKLOG.md`.

## Out of scope for Phase 1

Forum, RSS news, location shares, DMs, follow graph, OAuth social login, mineral Glossary.
