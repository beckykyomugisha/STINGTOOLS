# Email — Resend provider + deliverability

Planscape sends transactional email (invites, password resets, notifications)
through a single interface, `IEmailService`, with two interchangeable transports
selected by config:

| `Email__Provider` | Transport | When to use |
|---|---|---|
| `smtp` (default) | MailKit SMTP | Local dev (Mailpit), Gmail app-password, or Resend's SMTP endpoint |
| `resend` | Resend HTTP API | Production — best deliverability, webhooks-ready |

Both providers share **one** message-composition path (`EmailServiceBase` +
the `{{Body}}` template renderer), so the HTML + plain-text body is byte-identical
regardless of which transport sends it. Flipping the provider never changes what
the recipient sees — only the wire changes.

---

## 1. The from-address rule (read this first)

With `Email__Provider=resend`, the **from-address must be on a domain you have
verified in Resend**. Resend rejects any send whose `from` is on an unverified
domain. Set it via `SMTP_FROM_ADDRESS` (shared by both providers):

```
SMTP_FROM_ADDRESS=no-reply@yourdomain.com   # yourdomain.com must be Resend-verified
SMTP_FROM_NAME=Planscape
```

In **sandbox mode** (a brand-new Resend account, before domain verification) you
can only send to **your own account email** and only from `onboarding@resend.dev`.
That's enough to smoke-test the wiring; production needs a verified domain.

---

## 2. Add + verify your domain in Resend

1. Resend dashboard → **Domains** → **Add Domain** → enter `yourdomain.com`.
2. Resend issues a set of DNS records (SPF, DKIM, and a recommended DMARC). They
   look like the table below — **copy the exact values Resend shows you**, don't
   reuse these samples.

| Type | Name / Host | Value (sample — use Resend's) | Purpose |
|---|---|---|---|
| `TXT` | `send.yourdomain.com` | `v=spf1 include:amazonses.com ~all` | **SPF** — authorises Resend's MTA |
| `MX`  | `send.yourdomain.com` | `feedback-smtp.<region>.amazonses.com` (priority 10) | bounce/complaint return path |
| `TXT` | `resend._domainkey.yourdomain.com` | `p=MIGfMA0GCSq...` (long key) | **DKIM** — signs the message |
| `TXT` | `_dmarc.yourdomain.com` | `v=DMARC1; p=none;` | **DMARC** — alignment policy (start at `p=none`) |

> Resend currently signs from a `send.` subdomain — keep the host names exactly as
> the dashboard lists them. The DKIM record is the long `p=...` public key.

### Where these go in Cloudflare DNS

1. Cloudflare dashboard → your zone → **DNS** → **Records** → **Add record**.
2. For each row above:
   - **Type** = the record type (TXT / MX).
   - **Name** = the host Resend shows. In Cloudflare you enter the **subdomain
     part only** (e.g. `send`, `resend._domainkey`, `_dmarc`) — Cloudflare appends
     the zone automatically. Entering the FQDN also works (Cloudflare flattens it).
   - **Content** = the exact value from Resend.
   - **TTL** = Auto.
   - **Proxy status** = **DNS only** (grey cloud). DKIM/SPF/MX must NOT be
     proxied — the orange cloud only applies to A/AAAA/CNAME web traffic.
3. Save all records, then back in Resend click **Verify**. Propagation is usually
   minutes; Resend re-checks until all records are green.

---

## 3. Flip the provider

Everything is config — no rebuild needed to switch providers, only a container
recreate so the new env is read.

### Production (Resend HTTP API)
```
# .env
Email__Provider=resend
RESEND_API_KEY=re_xxxxxxxxxxxxxxxxxxxx
SMTP_FROM_ADDRESS=no-reply@yourdomain.com    # on the verified domain
SMTP_FROM_NAME=Planscape
PUBLIC_BASE_URL=https://yourdomain.com
```
```
docker compose up -d --force-recreate api
```

### Gmail (SMTP)
```
Email__Provider=smtp
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USE_SSL=false                 # 587 = STARTTLS
SMTP_USERNAME=you@gmail.com
SMTP_PASSWORD=<16-char app password>
SMTP_FROM_ADDRESS=you@gmail.com    # Gmail forbids arbitrary From
```

### Resend via SMTP (zero-code-change Resend)
Uses the same MailKit path, no HTTP adapter:
```
Email__Provider=smtp
SMTP_HOST=smtp.resend.com
SMTP_PORT=465
SMTP_USE_SSL=true                  # 465 = implicit TLS (use false + 587 for STARTTLS)
SMTP_USERNAME=resend
SMTP_PASSWORD=<RESEND_API_KEY>
SMTP_FROM_ADDRESS=no-reply@yourdomain.com    # still must be verified
```

### Local dev render check (Mailpit)
```
Email__Provider=smtp
SMTP_HOST=mailpit
SMTP_PORT=1025
SMTP_USERNAME=                     # blank — Mailpit takes no auth
SMTP_PASSWORD=
```
Send via the BCC **"Send test email"** button (or `POST /api/notifications/test-email`)
and open Mailpit at http://localhost:8025 to confirm the rendered layout + clickable
button.

---

## 4. Verifying the send path

- **Build**: `dotnet build` the API — clean.
- **Deploy server changes**: `docker compose build api && docker compose up -d --force-recreate api`
  (a recreate alone does NOT pick up new server code).
- **Provider selected?** On startup the API registers exactly one `IEmailService`.
  With `Email__Provider=resend` and no key, `IEmailService.IsConfigured` is `false`
  and `POST /api/notifications/test-email` returns `{ sent:false, message:"SMTP/Resend
  not configured…" }` rather than crashing.
- **Resend logs**: each send writes `[email] resend id={id} to={addr} -> {status}` to
  the API log; SMTP writes `[email] smtp sent to {addr}`.
- **Auth failure** (bad/dummy key): the adapter surfaces a clear
  `Resend send failed (401): …` error to the caller (the test-email endpoint returns
  it as `{ sent:false, message }`) — it never crashes the process.

---

## 5. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Resend send failed (403): … domain is not verified` | from-address on an unverified domain | verify the domain (§2) or use `onboarding@resend.dev` in sandbox |
| `Resend send failed (401)` | bad/empty API key | check `RESEND_API_KEY`; recreate api |
| Emails land in spam | missing/incorrect SPF/DKIM/DMARC | re-check the DNS records are present, **DNS-only** (grey cloud), and verified in Resend |
| `IsConfigured=false`, nothing sent | provider not wired | `Email__Provider=resend` + `RESEND_API_KEY` set; recreate api |
| Raw `<h2>` tags in inbox | (already fixed) renderer double-escape | ensure you're on the build with the `{{Body}}` RawHtmlKeys fix |
