# Production secrets runbook

> Where every credential the platform needs comes from, where it's
> stored, when to rotate it, and what breaks when it's missing.

## Storage

For a single-Hetzner-box deploy: `/etc/planscape/.env` with `chmod 600`,
owned by the `planscape` user. docker-compose loads it via env-file
binding (`env_file: /etc/planscape/.env`).

For a managed-PaaS deploy (Render, Fly, Railway): use the host's
secret manager. Never commit a populated `.env` to git — the
`.gitignore` blocks `.env` already.

For a future K8s deploy: 1Password Connect or Vault → `ExternalSecret`
CRD → `Secret` mounted into the API pod. Don't roll your own.

## Required minimum (Day 1)

The platform refuses to start without these. Marked **critical**.

| Key | Source | What breaks if missing |
|---|---|---|
| `DB_PASSWORD` | `openssl rand -base64 24` | Postgres can't start |
| `JWT_KEY` | `openssl rand -base64 48` | Auth — every login fails |
| `Smtp__Username` / `Password` | Postmark dashboard | Trial reminders + dunning silent-fail |

Everything else degrades gracefully. The platform starts; specific
features no-op until configured.

## Per-feature

### Billing

| Key | Source | When |
|---|---|---|
| `Billing__Stripe__SecretKey` | <https://dashboard.stripe.com/apikeys> | Before first USD/EUR/GBP buyer |
| `Billing__Stripe__WebhookSecret` | Dashboard → Webhooks → endpoint → Reveal | Same |
| `Billing__Flutterwave__SecretKey` | <https://dashboard.flutterwave.com/dashboard/settings/apis> | Before first EA-currency buyer |
| `Billing__Flutterwave__WebhookHash` | Dashboard → Settings → Webhooks → Secret Hash | Same |

Stripe webhook URL: `https://api.planscape.app/api/v1/billing/webhook/stripe`
Flutterwave webhook URL: `https://api.planscape.app/api/v1/billing/webhook/flutterwave`

Both webhooks need `live` mode enabled before going to production.
Test in Stripe's `test` mode (separate keys) until the first paying
firm signs.

### Plugin update channel

| Key | Source |
|---|---|
| `PluginUpdates__stable__Version` | `assemblyVersion` of the latest plugin .zip |
| `PluginUpdates__stable__DownloadUrl` | Wherever the zip lives (CDN preferred) |
| `PluginUpdates__stable__Sha256` | `sha256sum StingTools-X.Y.Z.zip` |

Bumping these is what triggers an in-plugin upgrade nag. Test against
the `beta` channel first (`PluginUpdates__beta__*`) — friendly
customers can opt in via project_config.json.

### Converter sidecar (S5.2)

```bash
# One-time generation:
openssl rand -hex 32  # → Converter__ApiBearer + CONVERTER_API_BEARER (same value)
openssl rand -hex 32  # → CONVERTER_TOKEN
```

`Converter__ApiBearer` = `CONVERTER_API_BEARER` (the converter sends
the bearer; the API expects the same value). They're separate env
vars only because docker-compose passes them to different services.

### Model converter (Phase: 3D viewing review)

| Key | Source | Notes |
|---|---|---|
| `MODEL_CONVERTER_PROVIDER` | one of `null` / `ifcconvert` / `aps` | Defaults to `null` (no IFC→glTF) |
| `APS_CLIENT_ID` / `APS_CLIENT_SECRET` | <https://aps.autodesk.com/myapps> | Required only if Provider=aps |

### Push notifications

`Push__Firebase__ServiceAccountJson` — Firebase Console → Project
Settings → Service accounts → Generate new private key → paste the
JSON content (the entire JSON object, single-line) into the env var.

## Rotation cadence

| Secret | Cadence | Why |
|---|---|---|
| `JWT_KEY` | Annual | Compromised key invalidates every active token (acceptable annual disruption) |
| `DB_PASSWORD` | Annual | Audit hygiene |
| `Billing__*__SecretKey` / `WebhookSecret` | On suspected leak only | Stripe/Flutterwave bills usage on the key, so rotation is auditable |
| `Converter__ApiBearer` / `CONVERTER_TOKEN` | Quarterly | Internal trust boundary; cheap to rotate, no customer-facing impact |
| `Push__Firebase__ServiceAccountJson` | Annual | Firebase keys don't expire; rotation is a hygiene measure |

Rotation procedure:

1. Generate new value.
2. Update `/etc/planscape/.env` (or the platform secret store).
3. `docker compose up -d --force-recreate api worker converter` to pick up.
4. Verify against the relevant smoke test (Stripe webhook → Stripe CLI
   `stripe trigger payment_intent.succeeded`; Flutterwave → live tx with
   the test card; etc.).
5. Wait one cycle (24 h for daily jobs) before discarding the old value.

## Verifying after a deploy

```bash
# 1. API came up and Postgres is reachable.
curl -fsS https://api.planscape.app/health

# 2. Hangfire is consuming the job queues.
curl -fsS https://api.planscape.app/hangfire/recurring-jobs | grep -E 'trial-state|outbox|dunning'

# 3. Stripe webhook accepts a test event.
stripe trigger checkout.session.completed --add checkout_session:metadata.tenant_id=00000000-0000-0000-0000-000000000001

# 4. Flutterwave verif-hash is correct.
curl -X POST https://api.planscape.app/api/v1/billing/webhook/flutterwave \
  -H "verif-hash: $Billing__Flutterwave__WebhookHash" \
  -H 'content-type: application/json' \
  -d '{"event":"charge.completed","data":{"id":1,"status":"successful","tx_ref":"smoke-test","amount":150,"currency":"USD","meta":{"tenant_id":"00000000-0000-0000-0000-000000000001"}}}'

# 5. Push delivery (replace with a real device token from DevicePushTokens).
curl -X POST https://api.planscape.app/api/v1/notifications/test-push \
  -H "authorization: Bearer $JWT" -d '{"deviceToken":"…"}'
```

## Smell test

If ANY of the following are true, **stop and fix before going live**:

- `JWT_KEY` is shorter than 32 chars
- `Billing__Stripe__SecretKey` starts with `sk_test_`
- `.env` is world-readable (`chmod 600`)
- Webhook URLs are HTTP, not HTTPS
- Same secret reused across `Converter__ApiBearer` AND `JWT_KEY`
- `DB_PASSWORD` is the default `Planscape2026!`
