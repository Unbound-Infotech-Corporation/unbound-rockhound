# reddit-discover edge function

## Auth
- Deploy with **verify_jwt = false** (scheduled cron / operator invoke).
- Every request must send header **`x-cron-secret`** equal to Edge secret **`CRON_SECRET`**.
- Do not put `CRON_SECRET` in client apps.

## Secrets
| Name | Required | Purpose |
|------|----------|---------|
| `SUPABASE_URL` | yes | REST writes |
| `SUPABASE_SERVICE_ROLE_KEY` | yes | Bypass RLS for upserts |
| `CRON_SECRET` | yes | Gate invokes |
| `REDDIT_CLIENT_ID` | no | OAuth client-credentials |
| `REDDIT_CLIENT_SECRET` | no | OAuth client-credentials |
| `REDDIT_USER_AGENT` | no | Reddit ToS User-Agent (set a unique string) |

## Graceful degradation
If OAuth and public `.json` listings all fail (or secrets missing), the function returns **HTTP 200** with:

```json
{ "ok": false, "degraded": true, "error": "..." }
```

Never rely on HTTP 500 for client/scheduler health checks.

## Schedule
Example (Supabase cron / external scheduler):

```http
POST /functions/v1/reddit-discover
x-cron-secret: <CRON_SECRET>
```
