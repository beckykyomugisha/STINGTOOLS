# Planscape Deployment Runbook

Concrete steps to get **server + mobile** running, from laptop to production. Use this with `docs/PLANSCAPE_GAPS.md` and `docs/ONSITE_SHARING_GAPS.md`.

---

## 1. 15-minute local demo (Expo Go on your phone)

Prerequisites: Docker Desktop, Node 18+, a phone on the **same Wi-Fi** as your laptop, and the Expo Go app.

```bash
# Server
cd Planscape.Server/docker
docker compose up -d
# API on http://<laptop-lan-ip>:5000 ; Swagger at /swagger
# Seeded login: admin@planscape.demo / admin123
# Seeded project has a geofence around central London (51.5045..51.5075 N / 0.124..0.128 W)

# Mobile
cd ../../Planscape
npm install

# Point the app at your laptop on the LAN — emulators and phones cannot reach "localhost"
EXPO_PUBLIC_API_BASE=http://192.168.x.x:5000 \
EXPO_PUBLIC_HUB_URL=http://192.168.x.x:5000/hubs/notifications \
npx expo start
# Scan the QR with Expo Go → log in as admin@planscape.demo
```

What works in this mode:
- Login, list projects, list issues
- Scanner tab opens the real camera and scans QR/PDF417/Code128/EAN13 → element lookup
- Issues create modal: photo capture, GPS, member picker, multipart upload
- Push notifications register **with Expo's service** (Expo Go uses `ExponentPushToken[...]` — the server detects the shape and routes via Expo Push automatically)
- Offline queue drains on app foreground + when connectivity returns

What doesn't work until §3 below:
- HTTPS (ATS will block on TestFlight/production builds)
- Durable / backed-up file storage
- Real FCM/APNs credentials for standalone builds

---

## 2. Production overlay (TLS + MinIO + backups)

```bash
cd Planscape.Server/docker

# 1) Secrets (put in docker/.env, NOT committed)
cat > .env <<EOF
DB_PASSWORD=$(openssl rand -base64 24)
JWT_KEY=$(openssl rand -base64 48)
MINIO_ROOT_PASSWORD=$(openssl rand -base64 24)
PUBLIC_HOST=planscape.yourco.com
EOF

# 2) TLS cert — pick one
#    (a) self-signed for staging / internal:
./nginx/generate-selfsigned.sh planscape.yourco.com
#    (b) Let's Encrypt for public:
#         run certbot --webroot -w /var/www/certbot --deploy-hook "docker compose restart nginx"
#         and copy /etc/letsencrypt/live/<host>/{fullchain,privkey}.pem into ./nginx/certs/server.{crt,key}

# 3) Bring up the full stack (base + prod overlay)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# 4) Apply migrations (the API does this on startup via Database.Migrate(),
#    but you can also run them explicitly from a host with dotnet installed)

# 5) Smoke test
curl -ksS https://localhost/health/live      # => { status: "alive" }
curl -ksS https://localhost/health/ready     # => 200 ready / 503 not-ready
curl -ksS https://localhost/health           # => { status, checks: { database, redis, push } }

# 6) MinIO console: https://planscape.yourco.com:9001 (root: planscape / MINIO_ROOT_PASSWORD)
#    Bucket 'planscape-attachments' is auto-created by the API on first upload.
```

The overlay bundles:

| Service | Purpose |
|---------|---------|
| `nginx` | TLS termination on 443, WebSocket upgrade for `/hubs/*`, HSTS + security headers |
| `minio` | S3-compatible object storage for issue attachments + nightly DB dumps |
| `pgbackup` | Cron sidecar that writes `/backups/planscape-<stamp>.sql.gz` every night (14-file retention) |
| `api` | Same image as dev; switches to `Storage:Provider=S3` + stricter CORS |

Backups also run **in-process** via the Hangfire `DatabaseBackupJob` at 02:15 UTC when `Backup:Enabled=true` in config — useful if you'd rather keep a single container.

---

## 3. Moving to mobile store builds (TestFlight / Play internal)

Requires: Expo account, Apple Developer / Google Play accounts, `eas-cli` installed.

```bash
cd Planscape

# Install the CLI
npm install -g eas-cli
eas login
eas init                              # links the project to your Expo org

# Fill the placeholders in eas.json (APPLE_ID, ASC_APP_ID, TEAM_ID,
#  play-service-account.json path) then:
eas build --profile preview --platform all          # internal QA builds
eas build --profile production --platform all       # store-ready builds
eas submit --profile production --platform ios      # TestFlight
eas submit --profile production --platform android  # Play internal track
```

Profiles already defined in `Planscape/eas.json`:
- **development** — local dev client, internal distribution, points at `http://planscape.local:5000`
- **preview** — internal QA build, points at `https://staging.planscape.example`
- **production** — store build, points at `https://api.planscape.example`

Environment variables are per-profile so you never ship a staging URL to the store.

---

## 4. Push notification configuration matrix

| Environment | Mobile token shape | Server config needed |
|-------------|--------------------|----------------------|
| Expo Go (dev on laptop) | `ExponentPushToken[...]` | *Nothing.* Server routes via Expo Push API anonymously. |
| EAS dev build on device | `ExponentPushToken[...]` | Optional: set `Expo:AccessToken` to lift rate limits |
| EAS standalone iOS (TestFlight) | `ExponentPushToken[...]` OR raw APNs if `getDevicePushTokenAsync` is used | `Firebase:ProjectId` + `Firebase:ServiceAccountJson` for APNs via FCM |
| EAS standalone Android (Play) | `ExponentPushToken[...]` OR raw FCM | Same Firebase config |

The server's `FirebasePushService` auto-detects `ExponentPushToken[...]` vs raw tokens and dispatches accordingly (`ExpoPushService` vs FCM HTTP v1). Mobile code already uses `getExpoPushTokenAsync` — switch to `getDevicePushTokenAsync` in `src/services/notificationService.ts` if you need Firebase-only delivery.

---

## 5. Load test

```bash
# Populate one project, copy its id, then:
BASE_URL=https://api.planscape.example \
PLANSCAPE_EMAIL=admin@planscape.demo \
PLANSCAPE_PASSWORD=admin123 \
PROJECT_ID=<guid> \
k6 run Planscape.Server/load/site-sharing.js
```

Thresholds in the script: `p95 < 1.5s` overall, `<2%` error rate, `create p95 < 2s`, `list p95 < 800ms`.

---

## 6. CI/CD

`.github/workflows/` contains:
- `planscape-server.yml` — build, test, and publish Docker image to ghcr.io on push to main
- `planscape-mobile.yml` — `tsc --noEmit` on every push, optional `workflow_dispatch` → EAS build (requires `EXPO_TOKEN` secret)
- `stingtools-plugin.yml` — Revit plugin build

Set these repo secrets for full CI:
- `EXPO_TOKEN` — from `eas whoami --json` or the Expo dashboard
- `GHCR_USER` / `GHCR_TOKEN` — if you're not using `GITHUB_TOKEN`'s default packages scope
- `DOCKER_REGISTRY_URL` — optional override if not using ghcr.io

---

## 7. On-site smoke test (5 minutes)

1. Mobile: install the preview EAS build on a phone, log in as a test user who's a member of the project.
2. Walk onto the project site (or spoof GPS inside the seeded boundary polygon).
3. Open the **Scanner** tab → scan any QR; it should call `/api/tagsync/elements/search` and show results.
4. Open the **Issues** tab → FAB → capture a photo, assign a member, tap Create.
5. Check the assignee's phone — push notification arrives within ~5 s.
6. Open Revit with StingTools plugin → click the **Sync** chip in the dock header → new issue pulls into `STING_BIM_MANAGER/issues.json`, visible in the Document Manager.

If any step fails, check `/health` for `checks.database` / `checks.redis` / `checks.push` status.

---

## 8. Known limitations

- Self-signed cert requires manual trust on the device to work with TestFlight builds; use Let's Encrypt for anything beyond an internal demo.
- The bundled `pgbackup` cron image doesn't include `mc` (MinIO client). To also push dumps to MinIO, either swap the sidecar image to `minio/mc` or use the in-process `DatabaseBackupJob` which stores through `IFileStorageService`.
- No rate limiting yet on the `/api/diagnostics/crash` endpoint except the generic `mobile` policy (120 req/min/device) — fine for real users, add stricter limits if exposing this publicly.
