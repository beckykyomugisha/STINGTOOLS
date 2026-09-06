// Soft-block-at-cap: the single source of truth for plan seat caps and the
// 14-day grace logic. Encoded once here, called by the invite gate and by login.

// TWO AXES, TWO TABLES. There used to be one `PLAN_CAPS` and one `resolveCap()`,
// and that single number independently bounded two unrelated quantities (#693):
//
//   * team ACCOUNTS   — login.ts, tenants/me.ts, tenants/me/invitations.ts
//   * licensed MACHINES — license/issue.ts, license/index.ts, license/present.ts
//
// So a `sting-tools/studio` tenant got 5 accounts AND, separately, 5 machines from
// a figure written to mean one thing. Nothing decided that; it fell out of
// countLicensedSeats() reusing the member helper. The two are genuinely different:
// present.ts notes that most licensed machines have no Planscape login at all, and
// one engineer with a desktop and a laptop is 1 account but 2 machines.
//
// THE NUMBERS BELOW ARE UNCHANGED — machines currently mirror members exactly, so
// this split is behaviour-preserving. Diverging them is a PRICING decision and a
// one-line edit to PLAN_MACHINE_CAPS; it is deliberately not made here.

// Team-account caps per product × tier. Matches marketing-site/pricing.html.
// One unit is an active (non-deleted) member PLUS a pending invitation — a pending
// invite reserves a place so you can't out-invite the cap and only discover it on
// accept.
export const PLAN_MEMBER_CAPS = {
  "sting-tools": { solo: 1, studio: 5, practice: 15, firm: 40, enterprise: Infinity },
  "planscape": { solo: 3, studio: 10, practice: 25, firm: 50, large: 100, enterprise: Infinity },
} as const;

// Licensed-machine caps per product × tier. One unit is a non-revoked, unexpired
// row in `licenses` — see license/_lib/seats.ts. Counted per WORKSTATION, not per
// person, and a machine needs no Planscape account at all.
//
// Identical to PLAN_MEMBER_CAPS today. Kept as its own table so that changing what
// a plan includes in machines does not silently change how many colleagues a firm
// may invite, and vice versa.
export const PLAN_MACHINE_CAPS = {
  "sting-tools": { solo: 1, studio: 5, practice: 15, firm: 40, enterprise: Infinity },
  "planscape": { solo: 3, studio: 10, practice: 25, firm: 50, large: 100, enterprise: Infinity },
} as const;

export type PlanProduct = keyof typeof PLAN_MEMBER_CAPS;
export type PlanTier = "solo" | "studio" | "practice" | "firm" | "large" | "enterprise";

// Before a plan is chosen (trial), apply a generous default so teams can build
// out during the trial. B3 sets plan_product/plan_tier and the real cap kicks in.
// Separate constants for the same reason the tables are separate.
export const TRIAL_MEMBER_CAP = 10;
export const TRIAL_MACHINE_CAP = 10;
export const GRACE_DAYS = 14;

// The status a tenant EFFECTIVELY has right now, as opposed to the string sitting
// in the column.
//
// Trial expiry used to be applied only by expireTrialIfNeeded(), whose sole
// caller is /api/auth/me. Every other path read subscription_status directly and
// saw a stale "trial" — so a tenant whose trial ended months earlier still passed
// entitlement on downloads and licence issuing, and issue.ts derived licence
// expiry from the same stale row and minted an already-expired licence (#677).
//
// Pure and non-mutating on purpose. The write belongs where a write is expected
// (/me); a read path silently UPDATE-ing tenants would be a surprise, and
// read-only callers should not need a transaction to ask a question.
// expireTrialIfNeeded() delegates here so the write rule and the read rule
// cannot drift.
export function effectiveStatus(
  tenant: { subscription_status: string; trial_ends_at: string | null },
  nowMs: number
): string {
  if (tenant.subscription_status !== "trial") return tenant.subscription_status;
  if (!tenant.trial_ends_at) return tenant.subscription_status;
  const ends = Date.parse(tenant.trial_ends_at);
  // An unparseable date is a data problem, not a licence to lock someone out.
  if (Number.isNaN(ends)) return tenant.subscription_status;
  return ends > nowMs ? "trial" : "read_only";
}

function resolve(
  table: Record<string, Record<string, number>>,
  fallback: number,
  planProduct: string | null,
  planTier: string | null
): number {
  if (!planProduct || !planTier) return fallback;
  const product = table[planProduct];
  if (!product) return fallback;
  const cap = product[planTier];
  return typeof cap === "number" ? cap : fallback;
}

// How many team ACCOUNTS this plan includes (members + pending invitations).
// Unknown / unset plan → trial default.
export function resolveMemberCap(
  planProduct: string | null,
  planTier: string | null
): number {
  return resolve(
    PLAN_MEMBER_CAPS as unknown as Record<string, Record<string, number>>,
    TRIAL_MEMBER_CAP,
    planProduct,
    planTier
  );
}

// How many licensed MACHINES this plan includes.
//
// There is deliberately no `resolveCap()` any more. An ambiguous name is what let
// six call sites share one number for two different things (#693); a seventh
// caller added later now has to say which axis it means, and picking wrong is
// visible in the diff rather than invisible in the behaviour.
export function resolveMachineCap(
  planProduct: string | null,
  planTier: string | null
): number {
  return resolve(
    PLAN_MACHINE_CAPS as unknown as Record<string, Record<string, number>>,
    TRIAL_MACHINE_CAP,
    planProduct,
    planTier
  );
}

export interface CapResult {
  cap: number;
  count: number; // committed seats (members + pending invites)
  within: boolean;
  overBy: number;
  gracePeriodEndsAt: string | null; // null unless over cap and cap_exceeded_since known
  graceEnded: boolean;
}

// Pure evaluation: given a committed seat count, the cap, and when the tenant
// first went over (cap_exceeded_since), decide where we stand. `nowMs` is passed
// in so this stays deterministic and testable.
export function evaluateCap(
  count: number,
  cap: number,
  capExceededSince: string | null,
  nowMs: number
): CapResult {
  const within = count <= cap;
  const overBy = within ? 0 : count - cap;
  let gracePeriodEndsAt: string | null = null;
  let graceEnded = false;
  if (!within && capExceededSince) {
    const end = new Date(capExceededSince).getTime() + GRACE_DAYS * 86400_000;
    gracePeriodEndsAt = new Date(end).toISOString();
    graceEnded = nowMs >= end;
  }
  return { cap, count, within, overBy, gracePeriodEndsAt, graceEnded };
}
