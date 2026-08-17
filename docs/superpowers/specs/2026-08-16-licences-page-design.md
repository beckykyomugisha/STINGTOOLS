# Self-serve licences — design

**Date:** 2026-08-16
**Closes:** #673
**Related:** #677 (mitigated here, not fixed), #651 (deploy gap), #621 (presentation data this finally surfaces)

## Problem

`POST /api/license/issue` is implemented, deployed, and working. **Nothing calls it.** A grep over the
whole marketing-site frontend finds no client, and `requireAuth` is Bearer-only with no cookie
fallback, so a logged-in browser session cannot reach it either. The only way to obtain a `.lic`
today is a hand-rolled authenticated API call.

The production `licenses` table has never held a customer row.

Meanwhile the plugin's activation dialog tells users to email `support@planscape.app` and wait for a
human — the manual bottleneck this removes.

## Scope decisions

| Decision | Choice | Why |
|---|---|---|
| Breadth | Web page **+ plugin dialog fix** | The web form alone is half a loop: the plugin hands users the wrong machine code (below). |
| Pieces | Issue + list. **No revoke.** | Revoke is a destructive billing action needing role gating and a confirm; `issue.ts` already directs users to contact us to move a licence, so a manual path exists. Additive later. |
| Permissions | Any authenticated member; list shows **all tenant machines** | Matches `issue.ts`'s existing behaviour (no role check), so no change to a merged endpoint. Engineers licence their own workstation without an admin relaying files, and the seat count they can see is the one that refuses the 11th machine. |
| Home | **New `/licences` page** | One page, one job. The plugin deep-links to it, so the URL must be stable. `account.html` is already 691 lines carrying subscription + plan picker + invoices. |

## The machine-code defect this depends on

`ActivationDialog.cs:32,40` shows and copies `LicenseGate.MachineCode`, which is
`MachineFingerprint.**Current**` — composed from MachineGuid **plus three WMI factors** (ProcessorId,
BaseBoard serial, BIOS serial). Those fail transiently, which flips the code and silently invalidates
a valid licence. On the development machine the two values are:

```
Stable  (MachineGuid only) : 4681-584E-784F-0868-4E48
Current (all 4 factors)    : 5AAF-6278-748C-2BCB-8810
```

`5AAF` ↔ `ADD3` flipping is a documented cause of past lockouts. `MachineFingerprint.Stable` is
MachineGuid-only and immune.

So if the page shipped alone, users would paste the Current code and receive a fragile licence — the
new flow would manufacture exactly the failure the Stable code exists to prevent.

**`LicenseGate.VerifyEither` already accepts Current OR Stable**, so changing what the dialog *shows*
is backwards compatible: every licence already issued against Current keeps working. No migration,
no reissue.

## Component 1 — `GET /api/license`

New file: `marketing-site/functions/api/license/index.ts`

Bearer auth via `requireAuth`. Tenant-scoped. Returns:

```json
{
  "cap": 10,
  "inUse": 3,
  "licences": [
    {
      "machineCode": "4681-584E-784F-0868-4E48",
      "licensee": "exo",
      "issuedAt": "2026-08-16T17:49:52.000Z",
      "expiresAt": "2036-08-03T09:23:49.531Z",
      "revokedAt": null,
      "lastSeenAt": "2026-08-16T17:54:29.907Z",
      "lastSeenPluginVersion": "2.2.0",
      "lastSeenRevitVersion": "2025"
    }
  ]
}
```

- `cap` is `resolveCap(tenant.plan_product, tenant.plan_tier)`, serialised as `null` for `Infinity` —
  the same convention `present.ts` already uses for `licencesIncluded`.
- `inUse` is `countLicensedSeats(db, tenantId, nowIso)` — **the same helper** `issue.ts` gates on and
  `present.ts` reports. One source of truth, so the page can never contradict the refusal a user gets.
- Ordered `created_at DESC`.
- Revoked and expired rows **are** returned (with their dates) so a user can see why a seat is or is
  not being consumed; they are excluded from `inUse` by `countLicensedSeats` itself.

### It cannot offer re-download — and must say so

`issue.ts` persists the row but **never the signed licence text**. The `.lic` exists exactly once, in
the response that mints it. The page must not imply a download that cannot exist. Recovery is
re-issue, which `issue.ts` already performs without consuming another seat
(`ON CONFLICT(tenant_id, machine_code) DO UPDATE`).

## Component 2 — `marketing-site/licences.html`

Served at `/licences`. **Do not add a `_redirects` rule** — Cloudflare Pages auto-canonicalises
`.html`, and a rule for `/licences` creates a redirect loop (see the note in `_redirects`, commit
`d75f094a0`).

Bootstrap is identical to `downloads.html`: `POST /api/auth/refresh` → access token held **in memory
only**, never `localStorage`/`sessionStorage`.

**Layout**

- Header line: *"3 of 10 machines licensed"* (or *"3 machines licensed"* when `cap` is null).
- Issue form: single machine-code input, auto-uppercased on input, validated client-side against the
  exact regex `issue.ts` enforces — `^[0-9A-F]{4}(-[0-9A-F]{4}){4}$` — plus a submit button.
- On success: browser saves **`StingTools.lic`** (Blob + `<a download>`), plus a copy button and the
  literal install path `C:\ProgramData\Planscape\StingTools\StingTools.lic`.
- Table: Machine · Expires · Last seen · Plugin · Revit · Status.
  Status is a pill: `Revoked` if `revokedAt`, else `Expired` if `expiresAt` is past, else `Active`.
  Never-presented rows show *"never"* for Last seen, not a blank cell or a fabricated date.
- Empty state: *"No machines licensed yet."*

**Expiry guard (#677 mitigation)**

If the returned `expiresAt` is in the past, show a red warning and **do not auto-download**. A lapsed
trial still passes entitlement and mints an already-dead licence; installing it would replace a
working licence with a broken one. This is a guard, not the fix — #677 is the fix.

## Component 3 — `StingTools/UI/ActivationDialog.cs`

- Line 32 (`codeBox.Text`) and line 40 (copy button) → `MachineFingerprint.Stable`.
- Line 28 instruction text → *"Get your licence at https://planscape.build/licences"* instead of
  emailing `support@planscape.app`.
- The paste-and-Apply half is unchanged.

## Error handling

| Case | Behaviour |
|---|---|
| 401 from refresh or any call | Redirect to `/login`, as `account.html` and `downloads.html` do |
| 403 (locked tenant) | Render `body.error` — `entitlementFor` already returns a user-facing reason |
| 403 (seat cap) | Render `body.error` — `issue.ts`'s message already names the cap and the count |
| 400 (bad machine code) | Render `body.error` inline against the field |
| Network / parse failure | Inline error. **Never** an empty table standing in for an error |

## Testing

Extend `marketing-site/tests/license.test.ts`. It runs under miniflare with a **real D1**, so these
exercise actual SQL rather than a stand-in:

1. `GET /api/license` returns only the caller's tenant's rows — seed two tenants, assert isolation.
2. `cap` and `inUse` equal what `issue.ts` gates on and `present.ts` reports for the same tenant.
3. A revoked licence is present in `licences` with `revokedAt` set, and is **excluded** from `inUse`.
4. An unauthenticated `GET` is refused 401.

No UI harness exists for these static pages; `licences.html` is verified by hand against production.

## Acceptance

- A paying customer can obtain a working `.lic` for their machine without anyone running `curl`.
- The code the plugin shows is the code that yields a durable licence.
- The account holder can see which machines are licensed and when each was last seen.

## Out of scope

- **Revoke.** Follow-up; `licenses.revoked_at` is currently written nowhere in the codebase.
- **#677 itself.** The expiry guard is a UI mitigation; the defect is server-side.
- **Deploy.** Per #651 `marketing-site` has no git-connected build — merging this ships nothing until
  someone runs `npm run deploy`.
