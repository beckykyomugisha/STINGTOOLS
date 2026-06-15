# C0 — Heavy-BIM server hosting (Oracle Cloud Always Free + Cloudflare Tunnel)

**Status: DRAFT scaffolding.** Nothing here has been deployed. This is the
hosting substrate so the "C" chunks (C1 auth bridge → C5) can deploy when
ready. It does **not** touch application code.

## Architecture recap

```
                 planscape.build/api/*          api-heavy.planscape.build
                 (B1/B2/B3 — auth, billing,     (C — clash, BCF, IFC ingest,
                  tenants)                        file storage, transmittals)
                        │                                  │
              Cloudflare Pages Functions          Cloudflare Tunnel (outbound)
              (TypeScript Workers)                        │
                                                  ┌────────┴─────────┐
                                                  │ Oracle Always    │
                                                  │ Free ARM VM      │
                                                  │  api + worker    │
                                                  │  postgres redis  │
                                                  │  minio cloudflared│
                                                  └──────────────────┘
```

The VM exposes **no inbound ports**. `cloudflared` makes an outbound-only
connection to Cloudflare; the edge terminates TLS for
`api-heavy.planscape.build` and forwards to `api:8080` inside the compose
network. This is why there is no Caddy / Let's Encrypt here (unlike
`docker/docker-compose.prod.yml`, which fronts a public-IP VPS).

## What's in this folder

| File | Role |
|---|---|
| `docker-compose.yml` | Self-contained production stack (api, worker, postgres, redis, minio, cloudflared, pgbackup). Reuses `../docker/Dockerfile`. |
| `.env.example` | Secrets template → copy to `.env` (gitignored) and fill in. |
| `cloudflared/config.yml` | Tunnel ingress: `api-heavy.planscape.build → http://api:8080`. |
| `.gitignore` | Keeps `.env` + `cloudflared/credentials.json` out of git. |

> **No `Dockerfile` here on purpose.** The compose `build:` reuses the existing
> multi-stage `Planscape.Server/docker/Dockerfile`, which must build from the
> repo root because `Planscape.Shared.csproj` links a `StingTools/` source
> file. Duplicating it would diverge and break the build context.

---

## 1. Provision the VM

1. Oracle Cloud → **Compute → Instances → Create**.
2. Shape: **Ampere A1 (arm64)** — Always Free allows up to 4 OCPU / 24 GB RAM.
   Start with 2 OCPU / 12 GB; this stack is comfortable there.
3. Image: **Ubuntu 22.04 (aarch64)**.
4. Boot volume: bump to ~100 GB (IFC/BCF artefacts + Postgres + MinIO).
5. Add your SSH public key. **Do not** add any ingress rules to the security
   list — the tunnel is outbound-only, so the box needs no open inbound ports
   beyond SSH (and even SSH can later move behind Cloudflare Access).

> ⚠️ Everything below is on the **arm64** VM. Pull arm64 image tags and, if you
> enable IfcConvert, an aarch64 IfcOpenShell build.

## 2. Install Docker + Compose plugin

```bash
sudo apt-get update && sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
  sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=arm64 signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo usermod -aG docker $USER && newgrp docker
```

> Oracle Ubuntu images default-DROP forwarded traffic via iptables, which can
> break Docker bridge networking. If containers can't talk to each other:
> `sudo iptables -I FORWARD -j ACCEPT` and persist with `netfilter-persistent`.

## 3. Get the code onto the VM

```bash
git clone https://github.com/beckykyomugisha/STINGTOOLS.git
cd STINGTOOLS/Planscape.Server/deploy
```

## 4. Create the Cloudflare Tunnel

```bash
# Install cloudflared (arm64) for the create/login/route steps (the running
# tunnel itself is the compose service — this is just for setup).
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-arm64 \
  -o /tmp/cloudflared && sudo install /tmp/cloudflared /usr/local/bin/cloudflared

cloudflared tunnel login                       # browser auth → pick planscape.build zone
cloudflared tunnel create planscape-heavy      # prints the tunnel UUID + writes <UUID>.json
cloudflared tunnel route dns planscape-heavy api-heavy.planscape.build
```

Then wire the credentials into this folder:

```bash
cp ~/.cloudflared/<UUID>.json cloudflared/credentials.json   # gitignored
# Edit cloudflared/config.yml → set `tunnel:` to the UUID above.
```

## 5. Configure secrets

```bash
cp .env.example .env
# Edit .env:
#   DB_PASSWORD            openssl rand -base64 24
#   MINIO_ROOT_PASSWORD    openssl rand -base64 24
#   JWT_KEY                **the same value as the Cloudflare Workers JWT_SECRET**
```

The `JWT_KEY ⇒ Workers JWT_SECRET` match is the linchpin: tokens minted by B1
auth at `planscape.build` must validate on this API. C1 formalises this in
`PlanscapeJwtHandler`; for now the server reads it as `Jwt__Key`.

## 6. First boot

```bash
docker compose up -d --build      # arm64 build runs natively on the VM
docker compose logs -f api        # watch schema bootstrap + Healthy
```

On a fresh empty Postgres, `PLANSCAPE_USE_ENSURE_CREATED=true` (default in
`.env.example`) makes the API create the full schema from the EF model on
first boot — **no `dotnet ef` step**. Idempotent patchers fill any gaps. Once
the DB is established you can leave it true or set it false (patchers still
run).

## 7. Verify through the tunnel

```bash
# From anywhere:
curl -fsS https://api-heavy.planscape.build/health     # → Healthy
```

`docker compose ps` should show `api` healthy and `cloudflared` connected
(its logs print "Registered tunnel connection" ×4).

---

## Operations

- **Logs:** `docker compose logs -f api worker cloudflared`
- **Update:** `git pull && docker compose up -d --build`
- **Backups:** the `pgbackup` service runs nightly `pg_dump` into the
  `pgbackups` volume (reuses `../docker/backup/backup.sh`). Ship those off-box
  (e.g. `rclone` to Cloudflare R2) as a follow-up.
- **MinIO console:** loopback-only — reach it via SSH tunnel:
  `ssh -L 9001:127.0.0.1:9001 ubuntu@<vm>` then open `http://localhost:9001`.
- **DB access:** Postgres is loopback-bound (`127.0.0.1:5432`); use an SSH
  tunnel, never expose it.

## Open items for later chunks (NOT C0)

- **C1** — `PlanscapeJwtHandler` + `TenantContext` + middleware (reads JWT
  claims `tid/sub/role/ev/ps/pt/pp`). Reconciles `JWT_KEY` → `JWT_SECRET`
  naming. Sketched but not wired into `Program.cs`.
- Off-box backup shipping (R2/S3) + restore runbook.
- Optional Cloudflare Access in front of SSH so the VM has zero open ports.
- Tunnel resilience: run a second `cloudflared` replica or use a
  remotely-managed (token) tunnel if you'd rather configure ingress in the
  Cloudflare dashboard than in `config.yml`.
