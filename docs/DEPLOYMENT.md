# Planscape — Deployment & Remote-Guest Review

How to put the Planscape server on the internet for a remote reviewer (e.g. a
guest in West Africa) and send them a real, clickable invite — and how the same
build moves from a tunnel to a cloud host by changing **env only**.

Secrets (SMTP creds, tunnel tokens, JWT key) live **only** in
`Planscape.Server/docker/.env`, which is **gitignored**. Never commit them.

---

## 0. Single source of truth: `Planscape__PublicBaseUrl`

Every outward URL a guest receives — invite / password-reset email links,
share + QR links — derives from **one** value:

| Surface | Env (in `docker/.env`) | Config key | Used by |
|---|---|---|---|
| Public base URL | `PUBLIC_BASE_URL` | `Planscape:PublicBaseUrl` | `PublicUrl.Resolve()` → emails, QR, CORS |

`PublicUrl.Resolve(config, request)` returns `Planscape:PublicBaseUrl` when set,
otherwise falls back to `{scheme}://{host}` of the request. Behind a reverse
proxy / Cloudflare tunnel the request host is the **internal** origin
(`localhost:5000`), so **you must set `PUBLIC_BASE_URL`** or guests get unreachable
`localhost` links. The web app + viewer call the API **same-origin** (relative),
so when served from the tunnel they automatically talk to the tunnel API.

`PUBLIC_BASE_URL` is also auto-added to the CORS allow-list.

---

## 1. Run order (THIS ORDER MATTERS)

```
tunnel  →  set PUBLIC_BASE_URL  →  restart api  →  send invite
```

If you send the invite before `PUBLIC_BASE_URL` points at the tunnel, the link in
the email is wrong. Steps:

```bash
# 1. Start the quick tunnel (prints https://<random>.trycloudflare.com)
cloudflared tunnel --url http://localhost:5000

# 2. Put that URL in docker/.env
#    PUBLIC_BASE_URL=https://<random>.trycloudflare.com

# 3. Recreate the api container so it picks up the new env (no rebuild needed)
cd Planscape.Server/docker
JWT_KEY=$(grep '^JWT_KEY=' .env | cut -d= -f2-) docker compose up -d api

# 4. Send the guest a reset/invite link (see §4)
```

> ⚠️ A Cloudflare **quick** tunnel URL **changes every restart**. Re-set
> `PUBLIC_BASE_URL`, re-run `docker compose up -d api`, and re-share each session.
> For a stable URL, use a **named** Cloudflare tunnel (`cloudflared tunnel create`)
> or the cloud/Docker deploy below.

---

## 2. Email — real SMTP (Gmail app password) vs Mailpit capture

The api wires `SmtpEmailService` whenever `Smtp__Host` is set, else
`NullEmailService` (which now logs a **loud warning** so mail never silently drops).
`docker/.env` injects the SMTP config into the container:

| Env | Container config | Notes |
|---|---|---|
| `SMTP_HOST` | `Smtp__Host` | `smtp.gmail.com` or `mailpit` |
| `SMTP_PORT` | `Smtp__Port` | `587` Gmail STARTTLS · `1025` Mailpit |
| `SMTP_USE_SSL` | `Smtp__UseSsl` | `false` for 587/1025; `true` for 465 |
| `SMTP_USERNAME` | `Smtp__Username` | Gmail address (blank ⇒ no AUTH, for Mailpit) |
| `SMTP_PASSWORD` | `Smtp__Password` | 16-char app password |
| `SMTP_FROM_ADDRESS` | `Smtp__FromAddress` | must equal the Gmail address |

The send uses `StartTlsWhenAvailable`, so the **same** build secures Gmail (587
advertises STARTTLS) and still talks to a plain Mailpit catcher.

### 2a. Gmail app-password setup (real delivery)

1. Enable **2-Step Verification** on the sending Google account.
2. Create an app password at <https://myaccount.google.com/apppasswords> for that
   **exact** account (Workspace accounts may need the admin to allow app passwords).
3. Put it in `docker/.env` (16 chars, no spaces) — `SMTP_USERNAME` /
   `SMTP_FROM_ADDRESS` must be that same Gmail address.
4. `docker compose up -d api`. Verify quickly from the host:
   ```bash
   python - <<'PY'
   import smtplib,ssl
   s=smtplib.SMTP("smtp.gmail.com",587,timeout=20); s.starttls(context=ssl.create_default_context())
   s.login("you@gmail.com","apppassword16"); print("AUTH OK"); s.quit()
   PY
   ```
   `535 BadCredentials` ⇒ wrong/expired password or app passwords disabled for that
   account. Fix that before expecting real mail.

### 2b. Mailpit (prove the path with no creds)

Mailpit captures every outbound email so you can prove the send path + verify the
link without real creds. It's a compose service (web UI `http://localhost:8025`):

```bash
docker compose up -d mailpit
# .env:  SMTP_HOST=mailpit  SMTP_PORT=1025  SMTP_USERNAME=  SMTP_PASSWORD=  SMTP_USE_SSL=false
docker compose up -d api
```

Trigger a send, then read it back:

```bash
curl -s http://localhost:8025/api/v1/messages | jq '.messages[0] | {To,Subject}'
# open the captured HTML to confirm the link points at the tunnel
```

**One-line swap to real Gmail:** change the four `SMTP_*` lines in `.env` back to
the Gmail block (§2a) and `docker compose up -d api`. No rebuild.

---

## 3. Reset / set-password page

Invite + forgot-password emails link to `/{PublicBaseUrl}/reset-password.html?token=…&email=…`.
`wwwroot/reset-password.html` reads the token+email from the query, takes a new
password, and POSTs same-origin to `/api/auth/reset-password`. Works over the tunnel
unchanged (same-origin fetch).

---

## 4. Review with a remote guest

1. Ensure the guest is a project member (invite once via the BIM Coordination
   Center, or `POST /api/projects/{id}/members/invite`).
2. Run §1 (tunnel → PUBLIC_BASE_URL → restart).
3. Send them a set-password link:
   ```bash
   curl -X POST "$PUBLIC_BASE_URL/api/auth/forgot-password" \
        -H 'Content-Type: application/json' \
        -d '{"email":"guest@example.com"}'
   ```
   They receive an email with a clickable `…/reset-password.html?token=…` link.
4. Guest, on a **second device** over the tunnel URL: open the link → set a
   password → sign in at `$PUBLIC_BASE_URL/` → project visible → open the model in
   the viewer → issues/clashes load.

### Perf note (slow links)
The GLB can be large over a tunnel. The streaming loader shows real progress
(no stuck-at-0%). For a first review on a slow link, publish a **smaller** model.

---

## 5. Long-term home — same image, env only

The tunnel is for ad-hoc reviews. The stable path is the existing Docker deploy
(`docker compose up -d` on a cloud VM, or push the image to your registry). Moving
there is **env-only**: set `PUBLIC_BASE_URL` to the cloud hostname, set the real
`SMTP_*` Gmail/Postmark values, set a strong `JWT_KEY` + `DB_PASSWORD`. No code or
image change — `PublicUrl` + the SMTP wiring already read everything from env.

---

## 6. LiveKit media (ICE) — fixing "could not establish pc connection"

Meeting A/V (camera/mic/screen-share) runs through the `livekit` SFU. Signaling is
WebSocket on `:7880`; the actual media flows over **UDP 50000–50019** (host
candidates) with **TCP 7881** as a fallback — all published by compose.

**The failure mode.** In Docker **bridge** networking LiveKit auto-detects its IP
as the container's `172.x` bridge address and advertises that as the ICE *host
candidate*. A browser on the host can't route to `172.x`, so the peer connection
never completes — `LiveKit could not establish pc connection` — even though the
ports are published and the container is healthy.

**The fix — one variable: `LIVEKIT_NODE_IP`.** It feeds the LiveKit server's
`--node-ip` (`$NODE_IP`) flag, which is the IP LiveKit advertises to clients.
Compose passes `NODE_IP=${LIVEKIT_NODE_IP:-127.0.0.1}` into the `livekit` service.

| Environment | `LIVEKIT_NODE_IP` | Why |
|---|---|---|
| **Local / dev** | unset → `127.0.0.1` (default) | Host browsers reach the published `50000-50019/udp` + `7881/tcp` at loopback. |
| **Production (VPS)** | the VPS **public IP** | Clients reach the server's real address; deterministic (no bridge auto-detect). |

`use_external_ip` stays `false` in `livekit.yaml` — `NODE_IP` supplies the
advertised IP explicitly in both modes, which is what avoids the bad `172.x`
candidate. Setting `node_ip` = public IP is equivalent to `use_external_ip: true`
but doesn't depend on the metadata/STUN auto-detect path.

### Apply / flip
```bash
# local (default — nothing to set), or force it:
echo 'LIVEKIT_NODE_IP=127.0.0.1' >> .env

# production:
echo 'LIVEKIT_NODE_IP=203.0.113.10' >> .env     # ← this VPS's public IP

docker compose up -d --force-recreate livekit
```
No image rebuild — it's a runtime env var.

### Confirm the advertised IP
```bash
docker compose logs livekit | grep -iE "node ?ip|using external|candidate"
# expect the configured IP (127.0.0.1 locally; the public IP in prod), NOT 172.x
```

### Verify the call
Open the same meeting in **two browser tabs/windows on the host**, join A/V in
both → both peer connections reach `connected` and audio is exchanged. If a
client is on a restrictive/symmetric-NAT network and even TCP 7881 is blocked,
front the UDP range with a **TURN server on TCP/TLS 443** (LiveKit's built-in TURN
or coturn) and point `rtc.turn_servers` / the LiveKit TURN block at your
`$DOMAIN` cert — that's the production path for locked-down corporate networks.

> **Egress note.** With `LIVEKIT_NODE_IP=127.0.0.1`, the in-compose Egress
> recorder (a separate container) can't reach LiveKit media at loopback, so
> recording is disabled in the loopback dev profile. Browser A/V (the priority)
> works. In production, `LIVEKIT_NODE_IP=<public IP>` is reachable by both
> browsers and the egress container, so recording works there.

---

## 7. Phase 1 — VPS production (Caddy + Let's Encrypt)

§1–6 cover **Phase 0**: a laptop + a named/quick cloudflared tunnel for ad-hoc
remote review. **Phase 1** is the durable home: a VPS with a real domain, HTTPS,
and persistent state. Everything is **DOMAIN-driven** — no hardcoded hosts.

### What the prod overlay adds (`docker-compose.prod.yml`)
Run both files together:
```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```
- **Caddy** fronts the API and obtains/renews a **Let's Encrypt** cert for
  `app.${DOMAIN}` automatically (HTTP-01 / TLS-ALPN on :80/:443). The API stops
  binding a host port — only Caddy is exposed. LE certs persist on the
  `caddy_data` named volume.
- **`app.${DOMAIN}` is proxied** through Caddy; **`livekit.${DOMAIN}` is DNS-only**
  — an A record straight to the VPS, LiveKit terminates its own TLS/TURN on :443.
- **Persistent named volumes** for every piece of state, so a redeploy keeps it:
  `pgdata` (Postgres), `miniodata` (recordings/attachments), `caddy_data`
  (certs), and `dpkeys` (the **ASP.NET DataProtection key ring**).
- The MinIO console binds to **loopback only** (`127.0.0.1:9001`) — never
  world-exposed.

### DataProtection keys — fixing "keys not persisted"
The API now persists its DataProtection key ring when `DataProtection:KeysPath`
is set (the overlay sets `DataProtection__KeysPath=/app/keys`, mounted on the
`dpkeys` volume). Without this, ASP.NET regenerates an **ephemeral** key ring on
every boot — logging *"No XML encryptor … keys not persisted"* and invalidating
anything protected by the prior key (auth cookies, antiforgery tokens, share
links) on each restart. Unset (dev) keeps the default ephemeral behaviour.

### Secrets — env-only, nothing in the image
```bash
cp .env.production.template .env      # gitignored; never commit a populated .env
# fill: DOMAIN, DB_PASSWORD, JWT_KEY, MINIO_ROOT_PASSWORD,
#       LIVEKIT_API_KEY, LIVEKIT_API_SECRET, LIVEKIT_NODE_IP (VPS public IP),
#       RESEND_API_KEY (or SMTP_*), PUSH_FIREBASE_SERVICE_ACCOUNT_JSON
```
The dev LiveKit `devkey:secret` pair is **only a `${…:-default}` fallback** in
`docker-compose.yml`. Setting `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` in `.env`
**rotates it out** — the api mints tokens with, and LiveKit + egress validate
against, the env values. Generate strong ones:
```bash
echo "LIVEKIT_API_KEY=$(openssl rand -hex 16)"    >> .env
echo "LIVEKIT_API_SECRET=$(openssl rand -base64 32)" >> .env
```

### DNS + firewall
| Record | Points at | Proxied? |
|---|---|---|
| `app.${DOMAIN}` (A) | VPS public IP | Caddy (80/443) |
| `livekit.${DOMAIN}` (A) | VPS public IP | **DNS-only** (LiveKit owns 443) |
| `recordings.${DOMAIN}` (A, optional) | MinIO/S3/CDN host | as needed |

Open **80/443 TCP** (Caddy), **443/tcp + 3478/udp** (LiveKit TURN), and the
**50000–50019/udp** media range.

### LiveKit media (ICE + TURN/TLS-443)
Set `LIVEKIT_NODE_IP` to the **VPS public IP** (§6). For clients behind
restrictive/symmetric NAT where even TCP 7881 is blocked, enable LiveKit's
built-in **TURN over TLS on :443** in `livekit.yaml`:
```yaml
turn:
  enabled: true
  domain: livekit.${DOMAIN}
  tls_port: 443
  # cert/key: point at an LE cert for livekit.${DOMAIN} (certbot / a Caddy
  # sidecar issuing to a shared volume), or terminate TLS at LiveKit directly.
```
`livekit.${DOMAIN}` stays DNS-only precisely so LiveKit can own :443 for TURN/TLS.

### Drift check (no hardcoded hosts)
Every outward URL derives from `DOMAIN` / `PUBLIC_BASE_URL`. The prod overlay and
Caddyfile contain **no** hardcoded `localhost` / `trycloudflare` / quick-tunnel
hosts (the only `127.0.0.1` is the deliberate MinIO-console loopback bind). The
base compose's `localhost` values are dev `${VAR:-default}` fallbacks, all
overridden by `.env` in prod.
