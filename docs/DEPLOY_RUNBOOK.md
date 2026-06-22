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

## 1. Pre-deploy migration check (one-time, on a dev machine)

Production **auto-applies committed EF migrations on boot** (`Program.cs` calls
`db.Database.Migrate()` in non-Development). So you don't run `database update`
by hand — but you MUST make sure every mapped entity has a migration, or those
tables won't exist and their endpoints will 500.

```bash
cd Planscape.Server
# EF 8: detect entities mapped in the model but missing from migrations.
dotnet ef migrations has-pending-model-changes \
  --project src/Planscape.Infrastructure --startup-project src/Planscape.API
```

- If it reports **no pending model changes** → you're done; skip to step 2.
- If it reports **pending changes** (known gap: the **HVAC snapshot** tables
  `HvacLoadSnapshots` / `HvacNcSnapshots` / `HvacRefrigerantSizings` are in the
  model snapshot but have no `CreateTable` migration), generate and commit it:

```bash
dotnet ef migrations add HvacEngineSnapshots \
  --project src/Planscape.Infrastructure --startup-project src/Planscape.API
git add src/Planscape.Infrastructure/Data/Migrations/*
git commit -m "Add HvacEngineSnapshots migration"
git push
```

Already present (no action): `HealthcarePack` (`20260515…`),
`IfcIngestSubstrate` (`20260519…`, incl. `ExternalElementMappings`), and ACC
token columns (on `PlatformConnections`, base schema). The `#345` ACC issue
sync needs **no** migration (it reuses `ConfigJson`).

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
# API healthy + migrations applied
curl -fsS https://api.planscape.build/health         # → 200

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

## Cost / scaling notes

Frankfurt starter tiers: api £6 + worker £6 + web £6 + converter £6 + redis ~£6 +
minio £6 + 10 GB disk ~£2 + db £6 ≈ **£44/mo**, plus free-tier LiveKit/Firebase/Resend.
Swapping MinIO for R2 (free tier) removes the storage service + disk (~£8) and adds redundancy.

To launch leaner, you can **omit `planscape-worker` and `planscape-converter`**
(remove them from `render.yaml` or suspend in Render): the API degrades
gracefully — IFC uploads are rejected with a "convert to GLB first" message and
heavy jobs simply don't run — and add them when you need IFC conversion / photo
redaction. Redis is also optional (the app fails open) but recommended once you
run more than one API instance (SignalR backplane).
