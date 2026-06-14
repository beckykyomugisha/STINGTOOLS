# Planscape B2 — Tenants, team, invitations, audit, cap enforcement

Builds on B1 (see `AUTH_SETUP.md`). Same D1 (`WAITLIST_DB`), same Web-Crypto-only
runtime, same CORS allow-list. No new env vars.

---

## 1. Apply the schema

B2 adds two tables (`invitations`, `audit_log`) and two columns
(`tenants.cap_exceeded_since`, `users.deleted_at`). The new tables and the
fresh-install columns are in `schema.sql` (idempotent). For a database that
already had B1 applied, run the two one-time `ALTER`s (ignore
"duplicate column name" if you've already run them):

```bash
cd marketing-site
wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql

# existing-DB column adds (run once each):
wrangler d1 execute planscape-waitlist --remote \
  --command="ALTER TABLE tenants ADD COLUMN cap_exceeded_since TEXT;"
wrangler d1 execute planscape-waitlist --remote \
  --command="ALTER TABLE users ADD COLUMN deleted_at TEXT;"
```

Then redeploy: `npm run deploy`.

---

## 2. Endpoints

| Method | Path | Min role | Purpose |
|---|---|---|---|
| GET | `/api/tenants/me` | any member | Tenant detail + member count + cap headroom |
| PATCH | `/api/tenants/me` | **owner** | Update name / country / currency |
| GET | `/api/tenants/me/members` | any member | List active members |
| POST | `/api/tenants/me/members/:userId/role` | admin | Change a member's role |
| DELETE | `/api/tenants/me/members/:userId` | admin | Soft-delete a member |
| POST | `/api/tenants/me/invitations` | admin | Invite a teammate (cap-gated) |
| GET | `/api/tenants/me/invitations` | admin | List pending invitations |
| DELETE | `/api/tenants/me/invitations/:id` | admin | Cancel a pending invitation |
| GET | `/api/tenants/me/audit` | admin | Paginated audit log (`?limit`, `?offset`, `?action`) |
| POST | `/api/invitations/:token/preview` | public | Render the accept screen (doesn't consume) |
| POST | `/api/invitations/:token/accept` | public | Create the user + auto-login |
| POST | `/api/invitations/:token/decline` | public | Decline (204) |

Invite emails link to `/accept-invite?token=…` (the `accept-invite.html` landing
page → preview → accept/decline).

---

## 3. Role authority

Hierarchy: `owner > admin > bim_manager > project_lead > coordinator > viewer > client`.

| Action | Owner | Admin | Others |
|---|---|---|---|
| Invite / change role / remove members | ✓ | ✓ | — |
| Edit tenant settings | ✓ | ✓* | — |
| View audit | ✓ | ✓ | — |
| Change plan / delete tenant | ✓ | — | — |

\* `PATCH /api/tenants/me` is **owner-only** in this build (settings are
owner-controlled). Role changes / removals require the caller to outrank the
target strictly, and a role can only be assigned at or below the caller's level.
`owner` is never assignable via the API — it's minted at signup only.

---

## 4. Soft block at cap (encoded in `_lib/limits.ts`)

A **seat** = active members + pending invitations (a pending invite reserves a
seat). Caps per plan:

```
sting-tools: solo 1 · studio 5 · practice 15 · firm 40 · enterprise ∞
planscape:   solo 3 · studio 10 · practice 25 · firm 50 · large 100 · enterprise ∞
```

While on **trial** (no plan chosen yet), the cap defaults to **10**.

On invite:
- under cap → proceed.
- over cap, within 14-day grace → **proceed + `warning: {code:'over_cap', upgradeBy}`**.
  The grace clock (`tenants.cap_exceeded_since`) starts the first time you go over.
- over cap, past grace → **402** `{error:'cap_exceeded_grace_ended', upgradeUrl:'/upgrade'}`,
  invite NOT created.

On login: never blocked, but the response carries `capExceeded` and
`readOnlyMode` (true once past grace) so the app can show an upgrade banner.

---

## 5. Smoke test

> To exercise caps quickly, set the test tenant to a tiny plan first (trial
> defaults to a cap of 10):
> ```bash
> wrangler d1 execute planscape-waitlist --remote \
>   --command="UPDATE tenants SET plan_product='sting-tools', plan_tier='solo' WHERE slug='test-firm';"
> ```
> (sting-tools/solo cap = 1, so the owner alone is already at cap.)

```bash
BASE=https://planscape.build
OWNER="Authorization: Bearer <owner JWT from B1 login>"

# 1. Owner invites a coordinator → 200; email arrives. Open /accept-invite?token=…
#    in a browser, set name+password → account created under the tenant, auto-login.
curl -s -X POST $BASE/api/tenants/me/invitations -H "$OWNER" \
  -H "Content-Type: application/json" -d '{"email":"coord@example.com","role":"coordinator"}'

# 2. Cap: on sting-tools/solo (cap 1) the invite above already put you over → it
#    returns 200 + "warning":{"code":"over_cap",...}. Now force grace to be over:
wrangler d1 execute planscape-waitlist --remote \
  --command="UPDATE tenants SET cap_exceeded_since='2000-01-01T00:00:00.000Z' WHERE slug='test-firm';"
#    Next invite → 402 cap_exceeded_grace_ended:
curl -s -X POST $BASE/api/tenants/me/invitations -H "$OWNER" \
  -H "Content-Type: application/json" -d '{"email":"two@example.com","role":"viewer"}'

# 3. The coordinator (after accepting) tries to invite → 403.
curl -s -X POST $BASE/api/tenants/me/invitations -H "Authorization: Bearer <coord JWT>" \
  -H "Content-Type: application/json" -d '{"email":"x@example.com","role":"viewer"}'

# 4. Admin promotes coordinator → project_lead → 200; audit row appears.
curl -s -X POST $BASE/api/tenants/me/members/<coordUserId>/role -H "$OWNER" \
  -H "Content-Type: application/json" -d '{"role":"project_lead"}'
curl -s "$BASE/api/tenants/me/audit?action=user.role_changed" -H "$OWNER"

# 5. Owner removes the member → soft-delete; member count drops; they can't log in.
curl -s -X DELETE $BASE/api/tenants/me/members/<coordUserId> -H "$OWNER"
curl -s $BASE/api/tenants/me/members -H "$OWNER"            # member gone from list
curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"coord@example.com","password":"<their pw>"}'  # 401

# 6. Decline flow: invite someone, then decline via the link → 204. Re-accepting
#    the same token → 410 Gone.
curl -s -X POST $BASE/api/invitations/<token>/decline        # 204
curl -s -X POST $BASE/api/invitations/<token>/accept -H "Content-Type: application/json" \
  -d '{"firstName":"A","lastName":"B","password":"a-twelve-char-pass"}'  # 410
```

---

## 6. Known limitation (caveat)

**Single-tenant-per-user.** An invited email that is **already** a registered
Planscape user (in any tenant) gets a **409** on accept — multi-team membership
needs a `memberships` join table and is deliberately deferred. The common case
(inviting a brand-new email) works fully. The `accept` path creates the user
under the inviting tenant with the invited role and a verified email.
