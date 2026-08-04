# Planscape Production Deploy Runbook

Exact, ordered steps to take Planscape from demo (localhost) to a fully-working
production deployment on the **planscape.build** domain, using the Render
Blueprint in [`/render.yaml`](../render.yaml).

Audience: an operator with access to the Render dashboard, the `planscape.build`
DNS registrar, and a dev machine with the .NET 8 SDK (for the one pre-deploy
migration check).

> **Mental model.** The API Docker image already contains the demo's web
> coordination surface (the `wwwroot/app` dashboard + `coordination-viewer.js` +
> `viewer.html` + LiveKit JS). So deploying the **API** gives you the demo's web
> coordination (meetings, federation viewer, photos) at `api.planscape.build`.
> The separate **`planscape-web`** Next.js app (issues/clashes/single-viewer) is
> an additional, newer client hosted alongside it.

---

## 0. Prerequisites (provision external services first)

These can't be created by the Render Blueprint. Do them first and keep the
credentials handy — you'll paste them into the Render env group in step 3.

| Service | Why | What you need |
|---|---|---|
| **Object storage** | Models, site photos, documents, IFC→GLB output, scene-node federation | **Provisioned by the Blueprint** (`planscape-minio` on a disk) — you only choose a `MINIO_ROOT_USER`/`MINIO_ROOT_PASSWORD`. *Or* switch to Cloudflare R2 / AWS S3 (managed + redundant) — then you need bucket, region, endpoint, access key, secret key. |
| **LiveKit** (LiveKit Cloud free tier, or self-host) | Meeting video/WebRTC | API key, API secret, `wss://…` server URL |
| **Firebase project** | Push notifications (FCM) | Project ID + service-account JSON |
| **Resend** (or SMTP) | Invites / password reset / owner reset email | Resend API key; verify `planscape.build` as a sending domain |
| **Autodesk APS app** *(optional)* | Server-side ACC connector | Client ID + secret; set redirect `https://api.planscape.build/api/acc/oauth/callback` |

Generate the JWT signing key now too:
```bash
openssl rand -base64 48      # → Jwt__Key
```
And choose the owner password (for `davis@planscape.build`).

---

## 1. Schema — no pre-deploy migration step

> **Corrected 2026-07-20.** This section previously said production calls
> `db.Database.Migrate()` and told you to run a pending-model-changes check.
> That is **not** what the committed `render.yaml` does. Skip the old steps.

`render.yaml` sets **`PLANSCAPE_USE_ENSURE_CREATED=true`** on **both**
`planscape-api` and `planscape-worker`. `Program.cs` (~line 1341) therefore
takes the **EnsureCreated** branch, not `Migrate()`:

1. It probes `information_schema` for the `Tenants` table.
2. If absent (fresh DB), `creator.CreateTables()` materialises the whole schema
   from `OnModelCreating`, which always matches the current entity classes.
3. In **both** branches, idempotent patchers run (`PatchDevSchemaAsync`,
   `PlatformSchemaPatcher.ApplyAsync`) with `ADD COLUMN IF NOT EXISTS` /
   `CREATE TABLE IF NOT EXISTS`, so pre-existing DBs pick up later additions.

This is the **official** schema-management mechanism for this codebase — see
[adr/0001-schema-management.md](adr/0001-schema-management.md). The
hand-authored migrations under `Planscape.Infrastructure/Data/Migrations/` are
missing their `.Designer.cs` companions and the model snapshot is stale, so
`Migrate()` cannot apply them in order; that is exactly why the flag exists.
A startup schema-drift self-check fails loudly if an EF entity was never
mirrored into the patcher.

**So: nothing to do here before deploying.** (This also means the HVAC snapshot
tables `HvacLoadSnapshots` / `HvacNcSnapshots` / `HvacRefrigerantSizings`, which
have no `CreateTable` migration, are created correctly by the EnsureCreated
path — the old instruction to generate an `HvacEngineSnapshots` migration is
unnecessary.)

If you ever **remove** `PLANSCAPE_USE_ENSURE_CREATED`, the `Migrate()` branch
takes over and a complete, regenerated migration set becomes a hard
prerequisite — regenerate it before flipping that switch.

> **The flag must stay on both services.** The schema block in `Program.cs` is
> not gated by `isWorker`; both roles execute it. Setting the flag on
> `planscape-api` only leaves the worker on the `Migrate()` branch, where it
> collides with the API's EnsureCreated schema on the non-idempotent
> `20260626203153_SustainabilitySnapshots` `CreateTable` and crash-loops.

---

## 2. Apply the Blueprint

1. Render dashboard → **Blueprints → New Blueprint Instance** → connect this repo
   (branch `main`). Render reads `/render.yaml` and proposes:
   - `planscape-api` (web), `planscape-worker` (worker), `planscape-web` (web),
     `planscape-converter` (private), `planscape-redis` (key value),
     `planscape-minio` (private, S3 storage + disk), `planscape-db` (Postgres).
2. Click **Apply**. The first build of the API/worker images is ~5–10 min.
   `planscape-converter` build downloads IfcConvert (non-fatal if the URL is stale).
3. The services will **fail health checks until you set the secrets** (step 3) —
   that's expected.

---

## 3. Set the secrets

All secrets are `sync:false` in the Blueprint, so Render created them empty.

### 3a. Shared env group → `planscape-shared`
Render dashboard → **Env Groups → planscape-shared** → set:

| Key | Value |
|---|---|
| `Jwt__Key` | the `openssl rand -base64 48` output |
| `PLANSCAPE_OWNER_PASSWORD` | the owner login password |
| `Storage__S3__ServiceUrl` | **MinIO default:** the `planscape-minio` internal URL (Render → planscape-minio → Connect → Internal URL, e.g. `http://planscape-minio:9000`). **R2/S3:** their endpoint, or blank for AWS S3. |
| `Storage__S3__AccessKey` | **= `MINIO_ROOT_USER`** (same value as 3d) — or the R2/S3 access key |
| `Storage__S3__SecretKey` | **= `MINIO_ROOT_PASSWORD`** (same value as 3d) — or the R2/S3 secret key |
| `LiveKit__ApiKey` / `LiveKit__ApiSecret` | LiveKit credentials |
| `LiveKit__ServerUrl` / `LiveKit__Url` | your `wss://….livekit.cloud` (set both) |
| `Firebase__ProjectId` | Firebase project id |
| `Firebase__ServiceAccountJson` | service-account JSON, single line |
| `Email__Provider` | `resend` (or `smtp`) |
| `Resend__ApiKey` | Resend key (if `resend`) — or set `Smtp__Host/Username/Password` |
| `Converter__Token` | a strong random string (must match 3c) |
| `Acc__ClientId` / `Acc__ClientSecret` | APS app creds (optional; blank disables ACC) |

`Storage__Provider=S3`, `Storage__S3__BucketName=planscape` (auto-created on
boot), `Storage__S3__Region=us-east-1`, `Storage__S3__ForcePathStyle=true` are
preset for the provisioned MinIO — only change them if you switch to AWS S3
(`ForcePathStyle=false` + real region) or R2.

`Jwt__Issuer/Audience`, `Acc__CallbackUrl`,
`Cors__Origins__*`, `Serilog__*`, `PLANSCAPE_OWNER_EMAIL` are already set to
working defaults in the Blueprint — leave them.

### 3a-bis. `PLANSCAPE_HANDOFF_SECRET` — per-service, NOT in the env group

> **Corrected 2026-07-30.** This key was previously listed in the §3a
> `planscape-shared` table. That is wrong and fails silently.

`render.yaml` declares `PLANSCAPE_HANDOFF_SECRET` as a **per-service**
`sync: false` variable on **`planscape-api`** (line 74) and **`planscape-worker`**
(line 143). It is *not* a member of the `planscape-shared` env group, so setting
it there leaves both services with the value still empty.

Set it **twice**, once per service, to the **same** string:

- Render → **planscape-api** → Environment → `PLANSCAPE_HANDOFF_SECRET`
- Render → **planscape-worker** → Environment → `PLANSCAPE_HANDOFF_SECRET`

```bash
openssl rand -base64 48      # generate ONCE, paste the same value into both
```

The same value goes on Cloudflare Pages in §3e. It is an HMAC shared secret, not
a JWT key — it must be **byte-identical** in all three places. A mismatch (or a
blank on one service) rejects every handoff with nothing obviously wrong in
either system's logs.

### 3b. `planscape-api` and `planscape-worker` (per-service)
On **each** service set:
- `Converter__BaseUrl` = the converter's internal URL (get it after step 2:
  Render → `planscape-converter` → **Connect → Internal URL**, e.g.
  `http://planscape-converter:7700`). Leave blank to disable IFC conversion.

`ConnectionStrings__Default` and `Redis__Connection` are auto-wired by the
Blueprint (fromDatabase / fromService) — nothing to set.

### 3c. `planscape-converter` (per-service)
- `CONVERTER_TOKEN` = **the same string** as `Converter__Token` in 3a.
- `IFCCONVERT_URL` = current linux64 IfcConvert zip if the baked default 404s
  (check the [IfcOpenShell releases](https://github.com/IfcOpenShell/IfcOpenShell/releases)).
- `API_BASE` = `planscape-api` internal URL (only used by the `/chunk` path).
- `API_BEARER` = leave blank unless you use `/chunk`.

### 3d. `planscape-minio` (per-service) — skip if using R2/S3
- `MINIO_ROOT_USER` = an access key string — **set `Storage__S3__AccessKey` (3a) to the same value**.
- `MINIO_ROOT_PASSWORD` = a strong secret — **set `Storage__S3__SecretKey` (3a) to the same value**.

If you chose Cloudflare R2 / AWS S3 instead, suspend or delete `planscape-minio`
(and its disk) and point `Storage__S3__*` at the external store.

After setting secrets, **Manual Deploy → Clear build cache & deploy** (or just
redeploy) each service so it picks them up.

### 3e. Cloudflare Pages side — set these LAST

Two values live on the marketing site (Cloudflare Pages, project
`planscape-marketing`), not on Render. They are what lets the account page hand a
signed-in customer across to the cloud app:

| Key | Value |
|---|---|
| `PLANSCAPE_HANDOFF_SECRET` | **the same string** set on Render in §3a-bis — on *both* `planscape-api` and `planscape-worker`. Both sides verify against it; a mismatch rejects every handoff. |
| `CLOUD_APP_ORIGIN` | `https://app.planscape.build` — where the customer is sent |

```bash
cd marketing-site
npx wrangler pages secret put PLANSCAPE_HANDOFF_SECRET --project-name=planscape-marketing
npx wrangler pages secret put CLOUD_APP_ORIGIN --project-name=planscape-marketing
```

> **Ordering rule — do this only after Render is answering.**
> Setting `CLOUD_APP_ORIGIN` is what activates the cloud button on the customer
> account page. Set it before `app.planscape.build` resolves and serves, and the
> button goes live pointing at an origin that is not there yet — so paying
> customers get sent to a dead host. The consumer of these values is
> `marketing-site/functions/api/cloud/handoff.ts`.
>
> Correct order: Render deployed (§2–§3d) → DNS resolving and TLS issued (§4) →
> first-boot checks passing (§5) → **then** §3e.
>
> Until §3e is done the site is in a safe state: the handoff simply stays
> disabled. An unset secret is a disabled feature; a wrong one is a broken
> customer journey.

---

## 4. DNS + custom domains

For each public service: Render → service → **Settings → Custom Domains → Add**,
then create the matching record at the `planscape.build` registrar.

| Host | Service | Record | Target |
|---|---|---|---|
| `api.planscape.build` | planscape-api | CNAME | `<planscape-api>.onrender.com` (shown by Render) |
| `app.planscape.build` | planscape-web | CNAME | `<planscape-web>.onrender.com` |
| `planscape.build` (apex, if used for marketing) | (marketing/site) | A/ALIAS | per registrar |

`api.planscape.build` and `app.planscape.build` are already in the API CORS
allow-list, and `NEXT_PUBLIC_API_BASE` is baked to `https://api.planscape.build`,
so no code change is needed once DNS resolves. (TLS is issued automatically by
Render once the CNAME verifies.)

---

## 5. First-boot verification

```bash
# API healthy + schema materialised (EnsureCreated path — see §1)
#
# /health/live, not /health. The full diagnostic at /health is gated in
# Production on a private-network caller AND an X-Health-Token header (S11), so
# from your laptop it answers 403 — which looks exactly like a failed deploy and
# is not one. /health/live is the anonymous liveness probe, and is what
# render.yaml points healthCheckPath at for the same reason.
curl -fsS https://api.planscape.build/health/live     # → {"status":"alive"}
curl -fsS https://api.planscape.build/health/ready    # → {"status":"ready"} (DB reachable)

# Owner login works (PlatformOwnerSeeder ran)
curl -fsS -X POST https://api.planscape.build/api/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"davis@planscape.build","password":"<owner password>"}'   # → JWT

# Demo dashboard + viewer shipped in the image
curl -fsS https://api.planscape.build/viewer.html | head -c 80

# Web app loads and points at the API
open https://app.planscape.build       # log in with the owner account
```

Then in the browser console on `app.planscape.build`: confirm **no CORS errors**
and that the real-time **Live** indicator appears on a project page (SignalR +
Redis backplane up).

Optional feature smoke tests:
- **IFC→GLB**: upload a small `.ifc` via the API models endpoint → expect `202`
  → a GLB model row appears shortly (worker + converter + S3 all wired).
- **Meetings**: start a live session → video tiles connect (LiveKit creds good).
- **Photos**: capture/approve from the mobile app → redacted image appears
  (worker `photo-redaction` queue + S3).

---

## 6. Production hygiene

- **Do NOT** set `PLANSCAPE_ALLOW_DEMO_SEED` — production must not seed the demo
  tenant / `admin@planscape.demo`. `ASPNETCORE_ENVIRONMENT=Production` is set by
  the Blueprint, which already gates demo seeding off.
- Rotate the owner password after first login (change-password), not by editing
  the env var.
- `planscape-db` is on the starter plan (1 GB, daily backups) — upgrade before
  real data volume grows.
- `planscape-minio` is **single-node on one disk (no HA)**. Back the disk up, or
  migrate to Cloudflare R2 / AWS S3 (redundant, managed) before serious volume —
  it's a drop-in `Storage__S3__*` swap.

---

## Database connection budget

Render Postgres allows **~97 client connections on every basic tier** (100 minus
10 Render reserves), reaching 200 only at `pro-8gb` and 400 at `pro-16gb`.
Npgsql's own default is **100 connections per pool, per process** — so a single
API container can exhaust the whole database on its own, and api + worker +
Hangfire breaches the ceiling at roughly 30–40 concurrent requests, long before
CPU or RAM are the limit. The symptom is `53300: sorry, too many clients
already` and blanket 500s.

Every pool is therefore capped in `Program.cs` ("Connection budget"):

| Process | EF pool | Hangfire pool | Total |
|---|---|---|---|
| `planscape-api` | 20 | 10 | 30 |
| `planscape-worker` | 15 | 15 | 30 |
| **Sum** | | | **60** — leaves 37 spare |

The spare is for `psql`, migrations, the nightly `pg_dump`, and Render's probes.
Override with `Database__MaxPoolSize` / `Database__HangfireMaxPoolSize`, and
**raise the database plan at the same time** — `PgConnectionStringsTests` asserts
the default budget stays under 97 so a bump can't silently overshoot.

Connections are tagged via `application_name`
(`planscape-api-ef`, `planscape-worker-hangfire`, …), so when it does go wrong:

```bash
psql "$DATABASE_URL" -c "SELECT application_name, count(*) FROM pg_stat_activity GROUP BY 1 ORDER BY 2 DESC;"
```

### Enabling PgBouncer (optional)

Render ships connection pooling free on paid databases, but it is **off by
default** and `connectionPoolString` does not resolve until you turn it on — so
the `ConnectionStrings__Pooled` blocks in `render.yaml` ship commented out.

1. Render → `planscape-db` → Settings → enable **Connection Pooling**
2. Uncomment `ConnectionStrings__Pooled` on **both** `planscape-api` and
   `planscape-worker` in `render.yaml`, then redeploy.

Only EF queries use the pooler. Hangfire (advisory locks, `LISTEN/NOTIFY`) and
`pg_dump` always stay on the direct 5432 connection.

> **Safety gate.** PgBouncer runs in *transaction* pooling mode, handing a server
> connection to a different client after each transaction. `RlsConnectionInterceptor`
> sets `app.current_tenant` at *session* scope, which under transaction pooling
> would leak one tenant's setting to the next — a cross-tenant disclosure. The app
> therefore ignores `ConnectionStrings:Pooled` whenever `Database:RlsEnabled=true`
> and logs a startup warning. To use both, the interceptor must first move to
> `SET LOCAL` inside an explicit transaction.

## Cost / scaling notes

Render bills in **USD**. Frankfurt, all-starter: api $7 + worker $7 + web $7 +
converter $7 + redis $10 + minio $7 + 10 GB disk ~$2.50 + db $6 ≈ **$54/mo**,
plus free-tier LiveKit/Firebase/Resend. Swapping MinIO for R2 (free tier) removes
the storage service + disk (~$9.50) and adds redundancy.

> Any **£12/month** figure in older notes refers to the retired 2-service
> blueprint (api + db only), not this 7-service one.

### Capacity per API tier

Two different numbers, and mixing them up is how you under-buy:

- **Connected** — logged in, WebSocket open, light use. Cheap: idle SignalR
  connections cost tens of KB, and Render enforces no WebSocket cap.
- **Active** — driving issues / markup / CRDT. This is what burns CPU, and it
  is the number to size on.

| Tier | $/mo | Connected | **Active** | Firms | Notes |
|---|---|---|---|---|---|
| Free | 0 | 1–3 | 1 | **0** | Spins down after 15 min, killing every WebSocket; no persistent disk so MinIO can't run. Demo only. |
| Starter | 7 | 20–30 | **10–15** | 3–6 | Current default. |
| Standard | 25 | 60–100 | **30–50** | 10–20 | First honest production tier. Single instance — a deploy drops all WebSockets. |
| Pro | 85 | 150–250 | **80–120** | 30–60 | First tier with autoscaling. **Scale out from here, not up.** |
| Pro Plus | 175 | 300–500 | **150–250** | 60–120 | DB becomes the bottleneck; pair with `pro-8gb`. |
| Pro Max | 225 | ≈ Pro Plus | ≈ Pro Plus | — | **Skip.** 16 GB but still 4 CPU — worse $/CPU than Pro Plus for a CPU-bound app. |
| Pro Ultra | 450 | 600–1000 | 300–500 | 150+ | Prefer 3–4 × Pro: cheaper, no single point of failure. |

Originally derived from ~40 req/s per vCPU. **That estimate was too pessimistic**
— see the measured results below. The table above is left deliberately
conservative because the measurements were taken on developer hardware, which is
faster than a shared cloud vCPU, and because run-to-run variance there is large.

### What has actually been measured

Method: `load/tier-capacity.js` against an API container pinned to Render
Starter limits (0.5 CPU / 512 MB) via `docker/docker-compose.loadtest.yml`, with
Postgres capped at `max_connections=100` to mirror a Render basic tier, and a
project seeded with 5,000 issues so the `.Include()` chains hydrate real rows.

Robust and reproducible:

| Finding | Evidence |
|---|---|
| **The EF pool cap holds.** Peak observed `planscape-api-ef` connections was **exactly 20**, the configured cap, at every load level. | `pg_stat_activity` sampled every 3s across all runs |
| **No connection exhaustion.** Zero `53300` / 5xx at any offered rate. | failure rate 0.00% in every run past the limiter fix |
| **Saturation shows up as latency, not errors.** p95 rose ~25× while failures stayed at 0.00%. | 240 rps → p95 78 ms; 250 rps → p95 935 ms |
| **RAM is not the Starter constraint; CPU is.** | 274 MB of the 512 MB ceiling at ~150 req/s |

Indicative, high variance — **do not quote these as capacity guarantees**:
on a 0.5-CPU container, p95 stayed under ~500 ms up to roughly 120–180 req/s
offered. Four runs at an identical 150 req/s produced p95 of 101, 2527, 424 and
108 ms, so a shared workstation cannot pin a number more precisely than that.
Re-run on an actual Render instance to get figures worth quoting.

Still not measured: SignalR fan-out under concurrent CRDT editing (the
`CrdtHub.Push` write-per-update path), and sustained multi-hour load.

The Redis SignalR backplane is already wired, so horizontal scaling works —
**3 × Pro ($255) beats 1 × Pro Ultra ($450)** on both throughput and resilience.

### Two things that bite before the tier does

1. **Connection ceiling** — see § Database connection budget above. Pool
   exhaustion hits at ~30–40 concurrent *requests* on any tier; buying a bigger
   instance does not fix it.
2. **Bandwidth** — a 500 MB IFC/GLB × 20 coordinators/day is ~300 GB/mo ≈ **$45**
   at $0.15/GB overage, which can exceed the compute bill. Serve models by
   presigned URL from object storage, never proxied through the API.

### Measuring tier capacity yourself

Everything below runs against a local dev stack. Budget ~20 minutes.

**1. Pin the API to the tier you want to measure.**

```bash
cd Planscape.Server
docker compose -f docker/docker-compose.yml -f docker/docker-compose.loadtest.yml \
  --env-file .env.local up -d --build postgres redis api
```

Defaults to Starter (0.5 CPU / 512 MB). For Standard:
`API_CPUS=1 API_MEMORY=2g docker compose ... up -d api`.

**2. Seed users, membership and issues.** Distinct users are not optional — the
`api` policy budgets 100 req/min per user, so a single account measures the rate
limiter rather than the server. 400 users ≈ 666 req/s of headroom.

```bash
docker exec -i docker-postgres-1 psql -U planscape -d planscape < load/seed-loadtest-data.sql
```

**3. Mint tokens.** Bulk login is impossible by design: the `auth` policy allows
5 logins per 5 minutes per IP. Sign tokens with the dev key instead.

```bash
JWT_KEY=$(grep '^JWT_KEY=' .env.local | cut -d= -f2-) \
  python load/mint-loadtest-tokens.py > load/loadtest-tokens.json
```

> `loadtest-tokens.json` holds **valid signed JWTs** and is gitignored. Only ever
> generate it against a dev key.

**4. Run, ramping `PEAK_RPS` to find the knee.**

```bash
docker run --rm --network host -v "$PWD/load:/load" \
  -e BASE_URL=http://localhost:5000 -e PROJECT_ID=<guid> -e PEAK_RPS=150 \
  grafana/k6 run /load/tier-capacity.js
```

The knee is where p95 crosses the budget while **failure rate stays at 0** —
that is CPU saturation. A run that instead shows high failures with a *low* p95
is not saturation: it is 429s, and it means either too few seeded users or the
offered rate exceeds `users × 100 / 60`.

**5. Watch the pools while it runs**, to confirm the cap holds and nothing
approaches the 97-connection ceiling:

```bash
docker exec docker-postgres-1 psql -U planscape -d planscape \
  -c "SELECT application_name, count(*) FROM pg_stat_activity GROUP BY 1 ORDER BY 2 DESC;"
```

**Interpreting the result honestly.** Your CPU core is faster than a shared
cloud vCPU, so treat any figure as an upper bound. Client and server also share
the host, so k6's own CPU competes with the API. Variance on a workstation is
large — repeat each point at least three times and quote the range, not the best
run.

### Suggested progression

| Stage | API | Worker | DB | ≈ $/mo |
|---|---|---|---|---|
| Pilot, 1–2 firms | Starter | Starter | basic-256mb | ~54 (all 7 services) |
| First paying firms (≤50 active) | **Standard** | Starter | **basic-1gb + PgBouncer** | ~90 |
| 10–50 firms | Pro ×2 | Standard | pro-8gb | ~300 |
| 50–150 firms | Pro ×4 | Pro | pro-16gb | ~700 |

To launch leaner, you can **omit `planscape-worker` and `planscape-converter`**
(remove them from `render.yaml` or suspend in Render): the API degrades
gracefully — IFC uploads are rejected with a "convert to GLB first" message and
heavy jobs simply don't run — and add them when you need IFC conversion / photo
redaction. Redis is also optional (the app fails open) but recommended once you
run more than one API instance (SignalR backplane).

---

## Pointing the plugin at a local server (development)

The Revit plugin resolves its API base URL in `PlanscapeServerClient.Settings.cs`,
`ResolveDefaultServerUrl()`, in this order — first hit wins:

| # | Source | Scope |
|---|---|---|
| 1 | `STING_PLANSCAPE_URL` environment variable | Whatever the process inherits |
| 2 | `%APPDATA%\StingTools\planscape_server.json` → `"serverUrl"` | Persists across restarts, per user |
| 3 | `BakedDefaultServerUrl` | Compiled into the assembly |

The resolved value is cached in `_cachedDefaultUrl` **for the lifetime of the process**,
so switching targets always requires restarting Revit. There is no in-session toggle.

### Use the launcher, not the settings file

```powershell
.\tools\Start-RevitLocal.ps1                      # newest Revit -> http://localhost:5000
.\tools\Start-RevitLocal.ps1 -Revit 2025          # a specific version
.\tools\Start-RevitLocal.ps1 -SkipHealthCheck     # launch with the API deliberately down
.\tools\Start-RevitLocal.ps1 -Prod                # no override; use the saved pointer
```

It sets level 1 **for the launched process only**, prints the saved level-2 pointer
alongside the override so the active target is unambiguous, and health-checks the URL
first so a dead stack fails immediately with the `docker start docker-api-1` command
rather than as a confusing in-app error.

**Do not use `setx`, a user-level environment variable, or hand-edit
`planscape_server.json` to switch to local.** All three persist, and a forgotten local
override points a real session at a dev database with no visible sign. Closing Revit
clears the launcher's override; nothing has to be remembered or undone.

`-SkipHealthCheck` is what the site-photo "could not load" verification needs: it starts
Revit pointed at an API that is deliberately down, so the failure states can be observed.

---

## Deploying the plugin, and proving what is deployed

### Why this is not just `cp`

One `StingTools.dll` is shared by **every installed Revit version and every concurrent
session working in this repo**. All three of
`%APPDATA%\Autodesk\Revit\Addins\{2025,2026,2027}\StingTools.addin` resolve to the same
assembly, so a deploy replaces the plugin for everything at once.

On **2026-08-03** that bit. PR #550 was deployed at 23:0x; at **23:22** a sibling session
built `claude/fix-nonmodel-category-bindings` and copied it over the same directory at
**23:26**. Nothing announced it. An entire evening of manual site-photo testing then ran
against a binary that did not contain the code under test — the observed
`"(no detail)"` dialog was a `MergeRecoveryStubs` stub that #550 deletes.

The cost was not the clobber. It was that **nothing said a word.**

### Deploy

```powershell
.\tools\Deploy-StingTools.ps1                 # build Release from the current branch, back up, deploy, stamp
.\tools\Deploy-StingTools.ps1 -SkipBuild      # deploy existing bin\Release output, stamp it
.\tools\Deploy-StingTools.ps1 -Configuration Debug
```

It resolves the target **from the `.addin` manifests every run** rather than hard-coding
`CompiledPlugin` — that path has moved before, and a deploy that writes where Revit is not
loading from looks like a success. It refuses while `Revit.exe` or
`Planscape.Companion.exe` is running (the Companion holds a handle on the directory, so a
copy can half succeed), backs up to `CompiledPlugin.bak-<sha>`, then writes
**`sting-deploy-stamp.json`** beside the DLL:

```json
{
  "branch": "claude/m-pass-deploy",
  "commit": "1a2b3c4",
  "builtAtUtc": "2026-08-04T…Z",
  "assemblySha256": "…"
}
```

### The check that would have caught it

`Start-RevitLocal.ps1` now verifies that stamp **before anything else** — before the
server health check, because which server the plugin talks to does not matter if the
plugin is not the code under test. Every launch prints the branch, commit and deploy time.

| State | Meaning | Behaviour |
|---|---|---|
| `Ok` | DLL hashes to what the stamp recorded | prints branch/commit, launches |
| `NoStamp` | DLL present, no stamp — **something copied over the deploy without using the deploy script** | refuse, **exit 3** |
| `Mismatch` | stamp present but the DLL differs — same cause, and it can name the branch now stale | refuse, **exit 3** |
| `NoAssembly` | no `.addin`, or it points at a missing file | refuse, exit 3 |
| `Unreadable` | stamp will not parse | refuse, exit 3 |

```powershell
.\tools\Start-RevitLocal.ps1 -ExpectBranch claude/m-pass-deploy   # assert the branch too
.\tools\Start-RevitLocal.ps1 -Force                               # launch unverified, deliberately
```

`-Force` downgrades a refusal to a red warning. It is never the default: a silent wrong
binary is the failure this exists to prevent, so the escape hatch has to be typed.

It also warns when the manifests **disagree** on the assembly — that means different Revit
versions load different plugins, which is its own quiet way to lose an evening.
