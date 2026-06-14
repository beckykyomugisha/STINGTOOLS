# Planscape Auth Core (B1) — setup & smoke test

The auth/tenant/user core that lets a real human sign up, verify their email,
log in, and receive a JWT downstream services trust. Runs entirely on
**Cloudflare Pages Functions + D1** (Web Crypto only — no Node APIs). Reuses the
existing `planscape-waitlist` D1 database via the `WAITLIST_DB` binding.

Endpoints live under `functions/api/auth/`; shared utilities under
`functions/api/auth/_lib/`.

---

## 1. Environment variables

Set these in the Cloudflare dashboard:
**Pages → planscape-marketing → Settings → Environment variables (Production)**.
Mark `JWT_SECRET` and `RESEND_API_KEY` as **encrypted** (Secret).

| Name | Type | How to get it | Used for |
|---|---|---|---|
| `JWT_SECRET` | Secret | `openssl rand -base64 32` | Signs HS256 access tokens |
| `RESEND_API_KEY` | Secret | Resend dashboard → API Keys (`re_...`) | Verification / reset / welcome email |
| `APP_ORIGIN` | Plain | `https://planscape.build` | Building email links |
| `EMAIL_FROM` | Plain (optional) | e.g. `Planscape <noreply@planscape.build>` | From: address override |

> If `RESEND_API_KEY` is unset (e.g. preview), email sends are skipped with a
> `console.error` and the auth flow still works — useful for local testing.

Generate a secret:

```bash
openssl rand -base64 32
```

---

## 2. Apply the schema migration

The four new tables (`tenants`, `users`, `sessions`, `idempotency_keys`) are
appended to the existing `schema.sql`. Every `CREATE` is idempotent
(`IF NOT EXISTS`), so re-running is safe and the existing `waitlist` table is
untouched.

```bash
cd marketing-site
wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
# or: npm run schema:remote
```

Verify the tables exist:

```bash
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
# expect: idempotency_keys, sessions, tenants, users, waitlist
```

### If you applied the ORIGINAL B1 schema before this hardening pass

The hardening adds `sessions.revoked_reason`. SQLite can't guard `ADD COLUMN`
with `IF NOT EXISTS`, so on a database that already had the first B1 schema
applied, run this **once** (ignore a "duplicate column name" error — it means
you already have it):

```bash
wrangler d1 execute planscape-waitlist --remote \
  --command="ALTER TABLE sessions ADD COLUMN revoked_reason TEXT;"
```

A fresh database gets the column from the `CREATE TABLE` and can skip this.

---

## 3. Local development

```bash
cd marketing-site
npm install                 # @cloudflare/workers-types + wrangler + typescript
npm run typecheck           # tsc against the Functions — must be clean
npm run schema:local        # apply schema.sql to the LOCAL D1 (.wrangler/)
npm run dev                 # wrangler pages dev — serves the site + Functions
# Set local secrets in marketing-site/.dev.vars (gitignored):
#   JWT_SECRET="local-dev-secret-at-least-32-bytes-long-xxxxx"
#   APP_ORIGIN="http://localhost:8788"
#   RESEND_API_KEY=""      # leave empty locally — email sends are skipped, flow still works
```

With `RESEND_API_KEY` unset, verification/reset emails are skipped (logged via
`console.error`) so the whole flow is exercisable offline; grab the token
straight from D1:

```bash
wrangler d1 execute planscape-waitlist --local \
  --command="SELECT email, email_verify_token FROM users ORDER BY created_at DESC LIMIT 1;"
```

---

## 4. Deploy

```bash
cd marketing-site
wrangler pages deploy . --project-name=planscape-marketing --branch=main --commit-dirty=true
# or: npm run deploy
```

---

## 5. Rate limiting (configure in the dashboard — do NOT roll your own)

Use Cloudflare's built-in rate-limiting rules. In the dashboard:
**Security → WAF → Rate limiting rules** (or the per-Pages-project equivalent).
Recommended rules, keyed on client IP:

| Path | Limit | Action |
|---|---|---|
| `/api/auth/signup` | **5 / hour** per IP | Block (then 429) |
| `/api/auth/login` | **10 / minute** per IP | Block |
| `/api/auth/resend-verify` | **3 / hour** per IP | Block |
| `/api/auth/password/forgot` | **3 / hour** per IP | Block |
| `/api/auth/refresh` | **60 / minute** per IP | Managed challenge |

The Functions themselves do not implement rate limiting — that's intentional.

---

## 6. Smoke test (the B1 done-criteria)

Replace `<JWT>` / `<REFRESH>` with values returned by the previous step.

```bash
BASE=https://planscape.build

# 1. Sign up — 200 + token + refreshToken + tenant.subscriptionStatus="trial"
curl -s -X POST $BASE/api/auth/signup -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery","firstName":"Test","lastName":"User","firmName":"Test Firm","country":"UG"}'

# 2. /me with the JWT — 200 + user.emailVerified=false
curl -s $BASE/api/auth/me -H "Authorization: Bearer <JWT>"

# 3. Re-signup, same email — 409 "An account with this email already exists."
curl -s -X POST $BASE/api/auth/signup -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery","firstName":"T","lastName":"U","firmName":"X","country":"UG"}'

# 4. Login wrong password — 401 "Invalid email or password." (NOT "user not found")
curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"wrong"}'

# 5. Login right password — 200 + JWT + refresh. SAVE this refresh as REFRESH_5.
curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery"}'

# 6. Refresh with REFRESH_5 — 200 + new JWT + new refresh (REFRESH_6).
curl -s -X POST $BASE/api/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH_5>"}'

# 7. REPLAY DETECTION: refresh again with the now-spent REFRESH_5 — 401, AND this
#    revokes ALL of the user's sessions (so REFRESH_6 is now dead too). Confirm by
#    trying REFRESH_6 next → also 401.
curl -s -X POST $BASE/api/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH_5>"}'
curl -s -X POST $BASE/api/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH_6>"}'   # expect 401 — killed by the replay above

# 8. Verify email — click the link in the inbox (lands on /verify-email) OR POST directly:
curl -s -X POST $BASE/api/auth/verify -H "Content-Type: application/json" \
  -d '{"token":"<token from email>"}'

# 9. /me again — user.emailVerified=true, and the JWT now carries ev:true.
curl -s $BASE/api/auth/me -H "Authorization: Bearer <fresh JWT from a new login>"

# 10. Forgot password, NONEXISTENT email — 200 {ok:true} (anti-enumeration, no email).
curl -s -X POST $BASE/api/auth/password/forgot -H "Content-Type: application/json" \
  -d '{"email":"nobody@nowhere.test"}'

# 11. Forgot password, REAL email — 200 {ok:true} + reset email arrives.
curl -s -X POST $BASE/api/auth/password/forgot -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build"}'

# 12. Reset password — 200 + fresh login tokens. All prior sessions are revoked.
#     (Click the email link → lands on /reset-password, OR POST directly:)
curl -s -X POST $BASE/api/auth/password/reset -H "Content-Type: application/json" \
  -d '{"token":"<token from reset email>","newPassword":"a-brand-new-passphrase"}'

# 13. Trial lazy-expiry — backdate the trial, then GET /me flips it to read_only:
wrangler d1 execute planscape-waitlist --remote \
  --command="UPDATE tenants SET trial_ends_at='2000-01-01T00:00:00.000Z' WHERE slug='test-firm';"
curl -s $BASE/api/auth/me -H "Authorization: Bearer <JWT>"   # tenant.subscriptionStatus="read_only"

# 14. Inspect D1
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT id,email,first_name,last_name,role,email_verified_at FROM users; SELECT id,name,slug,subscription_status,trial_ends_at FROM tenants; SELECT id,revoked_at,revoked_reason FROM sessions ORDER BY created_at DESC LIMIT 5;"
```

When steps 1–14 behave as annotated, **B1 is done** — reply `B1 smoke passed`.

---

## Endpoint reference

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/auth/signup` | Create tenant + owner user, send verify email, return JWT + refresh |
| POST/GET | `/api/auth/verify` | Verify email (POST body `{token}` or GET `?token=`) |
| POST | `/api/auth/resend-verify` | Resend verification email (always 200) |
| POST | `/api/auth/login` | Email + password → JWT + refresh + HttpOnly cookie |
| POST | `/api/auth/refresh` | Refresh token (body or `ps_refresh` cookie) → rotated JWT + refresh |
| POST | `/api/auth/logout` | Revoke refresh token, clear cookie |
| GET | `/api/auth/me` | Current user + tenant from the JWT bearer |
| POST | `/api/auth/password/forgot` | Issue reset email (always 200, anti-enumeration) |
| POST | `/api/auth/password/reset` | Reset password with token from email |

## Security notes

- **Passwords**: PBKDF2-SHA256, **100,000 iterations**, 32-byte random salt. Stored as
  `pbkdf2-v1$iterations$salt$hash` (versioned prefix). Cloudflare Workers' Web Crypto
  **caps PBKDF2 at 100,000 iterations** — requesting more (OWASP recommends 600k)
  throws `NotSupportedError` at runtime, so 100k is the platform ceiling, not a
  choice. The upgrade path for stronger hashing is **Argon2id** via
  [`@noble/hashes/argon2`](https://github.com/paulmillr/noble-hashes) (a pure-JS impl
  that runs in Workers), shipped under a new prefix (e.g. `argon2id-v1`) so old hashes
  keep verifying during a rolling rehash-on-login migration.
  Ref: <https://developers.cloudflare.com/workers/runtime-apis/web-crypto/>
- **Access token**: HS256 JWT, 1-hour expiry, claims
  `{ iss, sub, tid, role, ev, ps, pt, pp, iat, exp }` — `ps`=subscription status,
  `pt`=plan tier, `pp`=plan product. These plan claims are refreshed on every
  login / refresh so B2–B4 can authorize without a DB round-trip.
- **Refresh token**: opaque 32-byte url-safe random string, **single-use** (rotated on
  every refresh), 30-day expiry. Only its SHA-256 hash is stored in `sessions`.
- **Replay detection**: reusing an already-rotated refresh token revokes **all** of
  the user's live sessions (`sessions.revoked_reason='replay'`) and returns 401 —
  defends against a stolen refresh token being replayed after rotation.
- **Trial lazy-expiry**: `GET /me` flips a `trial` tenant whose window has elapsed to
  `read_only`. The B3 cron worker will do the same proactively.
- **Idempotency**: the `idempotency_keys` table + `_lib/idempotency.ts` ship now,
  unused by B1, so B3's payment endpoints can dedupe on `Idempotency-Key` with no
  further migration.
- **Anti-enumeration**: login returns the same 401 whether the email exists or the
  password is wrong; `password/forgot` and `resend-verify` always return 200.
- **CORS**: only `https://planscape.build` and `https://app.planscape.build` may call
  from a browser. Non-browser callers (no `Origin` header) are allowed.

## Out of scope for B1

Billing, team invitations, the plugin licence endpoint, the Revit/.NET code, and
any admin/user-listing endpoints are later chunks (B2–B4 / C). This is auth only.
