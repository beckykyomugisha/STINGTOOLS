# Self-host Planscape

If your firm needs to keep all data on your own infrastructure — for data-residency, security, or cost reasons — you can run the Planscape server stack on hardware you control. Self-hosting is included in the Enterprise plan and is also available as a standalone licence option.

This guide walks you through a baseline single-host deployment using Docker Compose. For multi-host or HA deployments, contact us and we'll scope a deployment plan.

## Prerequisites

- **Docker 24+** and **Docker Compose v2** on the host
- **4 GB RAM minimum** (8 GB recommended for >50 users)
- **40 GB disk** (more if you'll host many large models — plan for ~3× the size of your `.rvt` files combined)
- A domain name pointing to the host (TLS termination is handled by your reverse proxy)
- Outbound access to Docker Hub for image pulls
- An SMTP server (or a transactional email service like SendGrid, Postmark, Mailgun)
- Optional: a Firebase project for push notifications to mobile users

## Get the package

The server package — Docker Compose configuration plus an example `.env` — is in the `Planscape.Server/docker/` directory of the repository, or available as a tarball from your Enterprise dashboard.

Layout:

```
Planscape.Server/
├── docker/
│   ├── docker-compose.yml
│   ├── Dockerfile
│   └── .env.example
└── src/                  (built into images by docker compose build)
```

Copy the `docker/` directory to your server, rename `.env.example` to `.env`, and fill in the variables.

## Required environment variables

| Variable | Description |
|---|---|
| `DATABASE_URL` | PostgreSQL connection string. The compose file ships a Postgres 16 service; point this at it (`postgres://planscape:CHANGE_ME@db:5432/planscape`). |
| `REDIS_URL` | Redis 7 connection string. Compose ships Redis (`redis://redis:6379`). |
| `JWT_SECRET` | A 64-character random string used to sign access tokens. Generate with `openssl rand -hex 32`. |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | MinIO credentials for object storage. Use long random values. |
| `MINIO_ENDPOINT` | Internal MinIO address (`http://minio:9000` if using the bundled service). |
| `SMTP_HOST` / `SMTP_PORT` / `SMTP_USERNAME` / `SMTP_PASSWORD` / `SMTP_FROM` | SMTP credentials for email — transmittals, invitations, password resets. |
| `FCM_SERVICE_ACCOUNT_JSON` | Path to a Firebase service account JSON, mounted into the container. Optional — push notifications no-op without it. |
| `PUBLIC_BASE_URL` | The HTTPS URL where the API is reachable, e.g. `https://api.planscape.your-firm.com`. Used in email links. |

The full list — including optional variables for clustering, logging, and feature flags — is documented in `.env.example` with comments.

## Bring it up

```bash
cd Planscape.Server/docker
docker compose pull
docker compose up -d
```

The first start pulls images, runs Postgres migrations, seeds a demo tenant, and brings up the API on `http://localhost:5000`. Watch the logs:

```bash
docker compose logs -f api
```

When the API logs `Application started. Press Ctrl+C to shut down.`, you're up.

## First login

The seed data creates a demo admin: `admin@planscape.demo` / `admin123`.

**Change this password immediately.** Open `http://localhost:5000/swagger`, log in, hit `/api/auth/change-password`, and pick a real password. Or, easier, point your browser at the configured public URL and use the dashboard.

Then create your real admin user from **Admin → Users → Invite** and delete the demo account.

## Reverse proxy and TLS

The API listens on plain HTTP inside Docker. Terminate TLS at a reverse proxy. Caddy is the simplest option:

```caddy
api.planscape.your-firm.com {
  reverse_proxy localhost:5000
  encode gzip
}
```

Caddy obtains and renews Let's Encrypt certificates automatically. For nginx or HAProxy, see the example configurations in `docker/proxy-examples/`.

## Updates

```bash
cd Planscape.Server/docker
docker compose pull
docker compose up -d
```

Compose performs rolling restarts service-by-service, so the API is briefly unavailable (~5 s) during the API container restart. For zero-downtime updates, run two replica API containers behind your reverse proxy.

Database migrations are applied automatically on API startup. To skip auto-migration (e.g. for change-controlled environments), set `RUN_MIGRATIONS=false` and run them manually.

## Monitoring

- **Health endpoint** — `GET /health` returns 200 with a JSON status of dependencies (database, Redis, MinIO). Wire this into your uptime monitor.
- **Hangfire dashboard** — `/hangfire` shows background-job queues, recurring jobs, and failures. Restricted to admin users.
- **Logs** — structured JSON logs to stdout. Forward to your log aggregator (Loki, ELK, Datadog) using a Docker logging driver.
- **Metrics** — Prometheus metrics on `/metrics` (admin token required). Default scrape configs in `docker/prometheus/`.

## Backup

Backup the Postgres database, the MinIO bucket, and the `.env` file. The Compose file ships a sample backup sidecar that snapshots Postgres nightly to a configurable target (S3, B2, local volume).

For Enterprise plans we provide a fully-managed backup-and-restore service; ask if you'd rather not run this yourself.

## Hardening

- Run behind a firewall — only the reverse proxy should be reachable from the internet.
- Use Docker secrets (or a vault) for `JWT_SECRET`, `MINIO_ROOT_PASSWORD`, and `SMTP_PASSWORD` rather than plain `.env` in production.
- Enable Docker's userns-remap feature so containers don't run as host root.
- Set up a regular CVE scan against the running images (Trivy, Grype, or Docker Scout).

## Need help?

Self-hosting on Enterprise includes setup support — book a deployment session and we'll walk through it on a video call. Email <hello@planscape.app>.
