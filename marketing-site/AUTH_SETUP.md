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

The three new tables (`tenants`, `users`, `sessions`) are appended to the
existing `schema.sql`. Every statement is idempotent (`IF NOT EXISTS`), so
re-running is safe and the existing `waitlist` table is untouched.

```bash
cd marketing-site
wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
```

Verify the tables exist:

```bash
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
# expect: sessions, tenants, users, waitlist
```

---

## 3. Deploy

```bash
cd marketing-site
wrangler pages deploy . --project-name=planscape-marketing --branch=main --commit-dirty=true
```

---

## 4. Rate limiting (configure in the dashboard — do NOT roll your own)

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

## 5. Smoke test

Replace `<JWT>` / `<REFRESH>` with values returned by the previous step.

```bash
BASE=https://planscape.build

# 1. Sign up — expect 200 + token + refreshToken + tenant.subscriptionStatus="trial"
curl -s -X POST $BASE/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery","firstName":"Test","lastName":"User","firmName":"Test Firm","country":"UG"}'

# 2. /me with the JWT — expect 200 + user.emailVerified=false
curl -s $BASE/api/auth/me -H "Authorization: Bearer <JWT>"

# 3. Re-signup, same email — expect 409 "An account with this email already exists."
curl -s -X POST $BASE/api/auth/signup -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery","firstName":"T","lastName":"U","firmName":"X","country":"UG"}'

# 4. Login wrong password — expect 401 "Invalid email or password." (NOT "user not found")
curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"wrong"}'

# 5. Login right password — expect 200 + JWT + refresh
curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"test+b1@planscape.build","password":"correct-horse-battery"}'

# 6. Refresh — expect 200 + new JWT + new refresh. The step-5 refresh is now dead
#    (single-use rotation): repeating step 6 with the SAME token returns 401.
curl -s -X POST $BASE/api/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH from step 5>"}'

# 7. Check the inbox for the verification email (manual).

# 8. Verify the email (paste the token from the email link)
curl -s -X POST $BASE/api/auth/verify -H "Content-Type: application/json" \
  -d '{"token":"<token from email>"}'
#    (Clicking the email link hits GET /api/auth/verify?token=... and returns the same JSON.)

# 9. Logout — revokes the refresh token, clears the cookie
curl -s -X POST $BASE/api/auth/logout -H "Content-Type: application/json" \
  -d '{"refreshToken":"<current REFRESH>"}'

# 10. Inspect D1
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT id,email,first_name,last_name,role,email_verified_at FROM users; SELECT id,name,slug,subscription_status,trial_ends_at FROM tenants;"
```

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

- **Passwords**: PBKDF2-SHA256, 600,000 iterations, 32-byte random salt. Stored as
  `pbkdf2$iterations$salt$hash`.
- **Access token**: HS256 JWT, 1-hour expiry, claims `{ iss, sub, tid, role, ev, iat, exp }`.
- **Refresh token**: opaque 32-byte url-safe random string, **single-use** (rotated on
  every refresh), 30-day expiry. Only its SHA-256 hash is stored in `sessions`.
- **Anti-enumeration**: login returns the same 401 whether the email exists or the
  password is wrong; `password/forgot` and `resend-verify` always return 200.
- **CORS**: only `https://planscape.build` and `https://app.planscape.build` may call
  from a browser. Non-browser callers (no `Origin` header) are allowed.

## Out of scope for B1

Billing, team invitations, the plugin licence endpoint, the Revit/.NET code, and
any admin/user-listing endpoints are later chunks (B2–B4 / C). This is auth only.
