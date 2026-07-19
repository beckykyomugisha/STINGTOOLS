# Planscape identity handoff (Option B)

**Status:** design agreed 2026-07-18, not yet implemented.
**Problem it solves:** a customer signs up on `planscape.build` (Cloudflare D1) but
the cloud app authenticates against the .NET API backed by Postgres. Those are
two separate identity systems, so today a D1 customer cannot open the cloud app
at all.

## Why not the obvious alternatives

**Shared JWT secret / cross-validating tokens.** A D1 token fails against the
.NET API on three independent axes — different signing key, `iss` is `planscape`
vs `Planscape`, and D1 emits no `aud` while the API sets
`ValidateAudience = true`. Aligning all three couples the two systems' token
formats forever, and still leaves the harder half unsolved: the .NET side needs a
`Tenant` row and an `AppUser` row to exist before `ProjectsController` will do
anything (`ProjectsController.cs:112-113` → 404 "Tenant not found").

**Mirroring passwords at signup (Option A).** Simplest to build, but the plaintext
password transits a second service, and password changes then have to stay in
sync forever. That failure is silent for months and then surfaces as "I changed
my password and the cloud app locked me out" — expensive to debug, and it lands
on the customer.

## The design

D1 stays the only place a password lives. The cloud app never sees credentials.

```
planscape.build                     api.planscape.build
(Cloudflare D1)                     (.NET / Postgres)
      |                                     |
 1. user clicks "Open Planscape cloud"      |
      |                                     |
 2. POST /api/cloud/handoff                 |
    -> short-lived signed ticket            |
      |                                     |
 3. redirect to app.planscape.build/handoff?ticket=...
                                            |
                              4. POST /api/auth/handoff/exchange
                                 - verify HMAC + expiry + single-use
                                 - find-or-create Tenant + AppUser
                                 - return a NORMAL .NET session
```

From step 4 onward the cloud app behaves exactly as it does today — same JWT,
same refresh flow, same `AppShell` guard. Nothing downstream needs to know a
handoff happened.

## The ticket

HMAC-SHA256 over the compact JSON payload, using a secret both sides hold as
`PLANSCAPE_HANDOFF_SECRET` (32+ random bytes; set independently on Cloudflare
and Render — it is not either side's JWT key).

Wire format, mirroring the licence format already in use so there is one
convention in the codebase:

```
base64url(utf8(payloadJson)) + "." + base64url(hmacSha256(payloadBytes))
```

Payload:

| field | meaning |
|---|---|
| `jti` | uuid v4 — single-use key, see replay below |
| `email` | normalised, lowercase — the join key between the two systems |
| `tenantSlug` | D1 tenant slug; becomes the Postgres `Tenant.Slug` |
| `tenantName` | display name, used only when creating |
| `firstName` / `lastName` | used only when creating the `AppUser` |
| `role` | D1 role, mapped below |
| `tier` | D1 `plan_tier`, so the API can set `Tenant.Tier` / `MaxProjects` |
| `iat` / `exp` | issued/expiry unix seconds. **TTL 120 seconds.** |

**TTL is deliberately tiny.** The ticket travels in a URL, so it will land in
browser history, referrer headers and any proxy log in between. Two minutes is
enough to redirect and exchange, and short enough that a leaked URL is worthless
by the time anyone reads the log.

**Single use.** The exchange endpoint records `jti` and rejects a repeat. Without
this, anything that replays the URL — a browser prefetch, a shared link, a back
button — mints a second session.

## Role mapping

D1 roles do not match .NET roles one-for-one. Map explicitly rather than passing
the string through, so a rename on one side cannot silently escalate on the
other:

| D1 | .NET |
|---|---|
| `owner` | `Owner` |
| `admin` | `Admin` |
| `project_lead` | `ProjectLead` |
| `coordinator` | `Coordinator` |
| anything else | `Viewer` |

Default to the least privilege on an unrecognised value. Never default upward.

## What each side needs

**D1 — `POST /api/cloud/handoff`** (`marketing-site/functions/api/cloud/handoff.ts`)
- `requireAuth`; load tenant.
- Refuse when `subscription_status` is `read_only` or `cancelled` — the same
  entitlement gate the downloads and licence endpoints use.
- Mint and return `{ ticket, redirectUrl }`.

**.NET — `POST /api/auth/handoff/exchange`** (`AuthController`)
- `[AllowAnonymous]`. Verify HMAC, `exp`, and that `jti` is unseen (a small table
  or the existing Redis instance; Redis with a 5-minute TTL is the natural fit
  since `jti` need not outlive the ticket).
- Find-or-create `Tenant` by slug, then `AppUser` by email. Set a random
  unusable password hash on creation — this account never logs in directly.
- Reuse `GenerateJwt(AppUser)` (`AuthController.cs:939`) and the existing refresh
  path so the response is identical in shape to `/api/auth/login`.

**planscape-web — `/handoff`** (`app/handoff/page.tsx`)
- Read `?ticket=`, POST it to the exchange, store the returned token exactly as
  `lib/auth.tsx` does after a normal login, then `router.replace('/projects')`.
- On failure, send the user to `/login` with a plain message. Never echo the
  ticket back into the page.

## Ordering

Build and test entirely against the local docker stack (`localhost:5000`) with
`planscape-web` on `localhost:3100` before provisioning Render. The stack already
runs healthy with a 134-table schema, so none of this needs paid infrastructure
to prove.

## Deliberately out of scope

- **Reverse sync.** Changes made in the cloud app do not flow back to D1. D1 stays
  authoritative for identity and billing; Postgres holds project data.
- **Deleting a user in D1 does not delete them in Postgres.** Handoff refuses on a
  cancelled subscription, which is the practical control; tidying orphaned
  Postgres accounts is a separate job.
- **One tenant per user.** The .NET side supports multi-tenant switching
  (`/api/auth/tenants`, `/api/auth/switch-tenant`); D1 does not model it, so the
  handoff carries exactly one tenant.
