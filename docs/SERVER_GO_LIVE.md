# Planscape Server — Go-Live

Short, ordered path from "nothing deployed" to "`api.planscape.build` answers".
For the long-form reference (per-secret tables, cost notes, optional feature
smoke tests) see **[DEPLOY_RUNBOOK.md](DEPLOY_RUNBOOK.md)**.

> **Status as of 2026-07-20:** `api.planscape.build` does not resolve — the
> server is not deployed. The Cloudflare side (marketing site, auth/billing on
> D1, gated downloads on R2) *is* in production and independent of this.

Secrets live outside the repo and are never committed. The go-live package
(`SECRETS.txt` + `GO-LIVE-CHECKLIST.md`) is generated locally for the owner.

---

## Pre-flight — verified 2026-07-20

| Check | Result |
|---|---|
| Canonical blueprint | `/render.yaml` (repo root). `Planscape.Server/render.yaml` is a stale copy, self-marked SUPERSEDED. |
| API image builds from committed Dockerfile | ✅ `docker build -f Planscape.Server/docker/Dockerfile .` → exit 0 |
| `healthCheckPath: /health` matches a real endpoint | ✅ returns `{"status":"healthy"}` + database/redis/push sub-checks |
| Every `render.yaml` env var is read by the server | ✅ CORS binds as `string[]`; Serilog via `ReadFrom.Configuration`; `Smtp__Username`/`Smtp__Password` via `EmailServiceBase.Cfg` |

## Order of operations

1. **Blueprint** — Render → Blueprints → New Blueprint Instance → this repo,
   branch `main`. Expect 7 services (api, worker, web, converter, redis, minio,
   db). Services fail health checks until secrets are set; that is expected.
2. **Secrets** — set the `sync:false` values (see runbook §3 for the full table).
   Two pairs *must match each other*:
   `Converter__Token` = `CONVERTER_TOKEN`, and
   `Storage__S3__AccessKey`/`SecretKey` = `MINIO_ROOT_USER`/`MINIO_ROOT_PASSWORD`.
3. **Internal URLs** — only knowable after the first deploy:
   `Storage__S3__ServiceUrl` (minio internal URL) and `Converter__BaseUrl`
   (converter internal URL, set on **both** api and worker). Redeploy after.
4. **Handoff secret** — `PLANSCAPE_HANDOFF_SECRET` is shared with the marketing
   site and must be set on **both** sides (see below).
5. **DNS** — `api.planscape.build` → planscape-api, `app.planscape.build` →
   planscape-web, both CNAME to the `.onrender.com` host Render shows. TLS is
   automatic once the CNAME verifies. Both hosts are already in the CORS
   allow-list and `NEXT_PUBLIC_API_BASE` is baked to `https://api.planscape.build`.
6. **Smoke test** — runbook §5.

### The handoff secret spans two providers

`PLANSCAPE_HANDOFF_SECRET` is the shared signing secret between the marketing
site (mints handoff tickets) and the server (redeems them). It must be
**identical** on:

- Render → `planscape-api` **and** `planscape-worker`
- Cloudflare Pages → project `planscape-marketing`:
  `npx wrangler pages secret put PLANSCAPE_HANDOFF_SECRET --project-name planscape-marketing`

As of 2026-07-20 it was **not yet set on the Cloudflare project** (verified via
`wrangler pages secret list`), so first go-live sets a fresh value on both sides.
Rotations must be simultaneous or cloud→server login breaks.

---

## Day 1 after deploy — you do NOT run EF migrations

**The container does not auto-migrate, by design.**

`render.yaml` sets `PLANSCAPE_USE_ENSURE_CREATED=true`, which makes startup
(`Program.cs` ~line 1327) take the **EnsureCreated** branch instead of
`db.Database.Migrate()`:

1. Probe `information_schema` for the `Tenants` table.
2. If absent (fresh DB), `creator.CreateTables()` materialises the entire schema
   from `OnModelCreating` — by construction in sync with the entity classes.
3. In **both** branches, idempotent patchers then run (`PatchDevSchemaAsync`,
   `PlatformSchemaPatcher.ApplyAsync`) using `ADD COLUMN IF NOT EXISTS` /
   `CREATE TABLE IF NOT EXISTS`, so long-lived DBs pick up later entity additions.

This is deliberate, not a workaround-in-waiting: the hand-authored migrations
under `Planscape.Infrastructure/Data/Migrations/` lack `.Designer.cs` companions
and the model snapshot is stale, so `Migrate()` cannot apply them in order. This
is the documented mechanism — see
[adr/0001-schema-management.md](adr/0001-schema-management.md).

A startup **schema-drift self-check** asserts the live schema matches the EF
model, so an entity nobody mirrored into the patcher fails loudly at boot rather
than as a production 500.

**Consequence:** the pre-deploy `dotnet ef migrations has-pending-model-changes`
step is *not* part of go-live while `PLANSCAPE_USE_ENSURE_CREATED=true` is set.
If that flag is ever removed, the `Migrate()` branch takes over and a complete,
regenerated migration set becomes a hard prerequisite first.

## Things that look broken but aren't

| Symptom | Why |
|---|---|
| `/swagger` returns 404 in production | Deliberate — schema disclosure aids attackers. Opt in with `Swagger__Enabled=true`, then remove it again. |
| Services red immediately after Apply | Secrets aren't set yet (step 2). |
| IFC upload rejected / heavy jobs never run | `planscape-worker` or `planscape-converter` suspended — the API degrades gracefully by design. |
| Email never arrives | No `Email__Provider`/`Resend__ApiKey`; mail is logged only. The default sender domain `planscape.build` must be verified with the provider. |

## Production hygiene

- Never set `PLANSCAPE_ALLOW_DEMO_SEED` in production (it would seed the demo
  tenant and `admin@planscape.demo`). `ASPNETCORE_ENVIRONMENT=Production`
  already gates it off.
- Rotate the owner password via change-password after first login, not by
  editing the env var.
- `planscape-minio` is single-node on one disk (no HA) — back it up or move to
  R2/S3 (drop-in `Storage__S3__*` swap) before real data volume.
