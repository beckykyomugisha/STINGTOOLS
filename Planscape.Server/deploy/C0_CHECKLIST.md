# C0 operator checklist — heavy-BIM server bring-up

**Status: DRAFT.** Run top-to-bottom on the Oracle Always Free ARM VM. Full
prose in [`oracle-vm-setup.md`](./oracle-vm-setup.md). Nothing here deploys
automatically — these are the manual steps an operator performs.

## The 11 steps

| # | Step | Command / action | Done when |
|---|------|------------------|-----------|
| 1 | Provision VM | Oracle Cloud → Ampere A1 (arm64), Ubuntu 22.04 aarch64, ~100 GB boot, SSH key added, **no inbound rules** | `ssh ubuntu@<vm>` works |
| 2 | Install Docker + Compose plugin | apt repo + `docker-ce docker-compose-plugin`, add user to `docker` group | `docker compose version` prints |
| 3 | Clone repo | `git clone … && cd STINGTOOLS/Planscape.Server/deploy` | `docker-compose.yml` present |
| 4 | Install cloudflared (setup only) | download arm64 binary to `/usr/local/bin/cloudflared` | `cloudflared --version` prints |
| 5 | Authenticate tunnel | `cloudflared tunnel login` → pick the `planscape.build` zone | cert.pem written to `~/.cloudflared` |
| 6 | Create tunnel | `cloudflared tunnel create planscape-heavy` | UUID printed; `<UUID>.json` written |
| 7 | Route DNS | `cloudflared tunnel route dns planscape-heavy api-heavy.planscape.build` | CNAME created in Cloudflare DNS |
| 8 | Wire tunnel creds | `cp ~/.cloudflared/<UUID>.json cloudflared/credentials.json`; set `tunnel:` UUID in `cloudflared/config.yml` | creds file present (gitignored) |
| 9 | Fill secrets | `cp .env.example .env`; set `DB_PASSWORD`, `MINIO_ROOT_PASSWORD`, **`JWT_KEY` = Workers `JWT_SECRET`** | `.env` complete (gitignored) |
| 10 | First boot | `docker compose up -d --build` (arm64 native build) | `api` Healthy; `cloudflared` shows 4 connections |
| 11 | Verify through tunnel | `curl -fsS https://api-heavy.planscape.build/health` | returns `Healthy` |

> **Schema note (step 10):** on a fresh empty DB, `PLANSCAPE_USE_ENSURE_CREATED=true`
> (default) makes the API build the full schema from the EF model on first boot.
> There is **no manual `dotnet ef` step** — the migration set is intentionally
> incomplete and idempotent patchers fill any gaps.

## JWT_SECRET cross-stack handshake

The auth boundary spans two stacks that must share **one** signing secret:

```
Cloudflare Workers (B1 auth)                Heavy-BIM API (.NET, this VM)
─────────────────────────────              ──────────────────────────────
signs JWT with JWT_SECRET            ──►    validates JWT with the same secret
  iss = "planscape"                          ValidIssuer  = "planscape"
  aud ∈ {planscape, planscape-heavy}         ValidAudiences = {planscape-heavy, planscape}
  alg = HS256                                HS256, ClockSkew = 60s
  claims: tid sub role ev pp pt ps           → projected onto TenantContext (C1)
```

- The Workers side reads it as **`JWT_SECRET`** (already set in Cloudflare
  Pages → planscape-marketing → Variables and secrets).
- This VM currently reads it as **`Jwt__Key`** in `docker-compose.yml`, fed
  from `.env`'s `JWT_KEY`. **Set `JWT_KEY` to the exact same string as the
  Workers `JWT_SECRET`.**
- The C1 auth stubs (`Auth/PlanscapeJwtHandler.cs`) already standardise on the
  `JWT_SECRET` name; the C1 commit renames the server-side `Jwt__Key`
  references to match, so the two names converge.
- **Rotation:** rotate on the Workers side and in `.env` together, then
  `docker compose up -d` to restart with the new secret. A 60-second clock
  skew is the only tolerance — there is no dual-key overlap window yet
  (add one in C1 if zero-downtime rotation is needed).

## Planned backup verification flow (follow-up, not C0 bring-up)

The `pgbackup` service runs nightly `pg_dump` into the `pgbackups` volume
(reusing `../docker/backup/backup.sh`). The verification loop to stand up next:

1. **Confirm a dump exists:** `docker compose exec pgbackup ls -lh /backups`
   — expect a dated `.sql`/`.dump` from the last run.
2. **Off-box copy:** push the latest dump to Cloudflare R2 (or S3) via
   `rclone` on a cron — a backup that only lives on the VM is not a backup.
3. **Restore drill (quarterly):** spin a throwaway Postgres, restore the
   newest dump, boot the API against it with `PLANSCAPE_USE_ENSURE_CREATED=false`,
   and hit `/health` + a couple of read endpoints. Record the restore time.
4. **Alert on staleness:** if no new dump in 36 h, page the operator (wire to
   the same channel C-chunk health checks use once they land).

> None of steps 1–4 are automated yet — this section is the spec for the
> backup-hardening task that follows C0.
