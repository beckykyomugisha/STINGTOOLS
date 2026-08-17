// Soft-block-at-cap: the single source of truth for plan seat caps and the
// 14-day grace logic. Encoded once here, called by the invite gate and by login.

// Seat caps per product × tier. Matches marketing-site/pricing.html. A "seat"
// is an active (non-deleted) member PLUS a pending invitation — a pending invite
// reserves a seat so you can't out-invite the cap and only discover it on accept.
export const PLAN_CAPS = {
  "sting-tools": { solo: 1, studio: 5, practice: 15, firm: 40, enterprise: Infinity },
  "planscape": { solo: 3, studio: 10, practice: 25, firm: 50, large: 100, enterprise: Infinity },
} as const;

export type PlanProduct = keyof typeof PLAN_CAPS;
export type PlanTier = "solo" | "studio" | "practice" | "firm" | "large" | "enterprise";

// Before a plan is chosen (trial), apply a generous default so teams can build
// out during the trial. B3 sets plan_product/plan_tier and the real cap kicks in.
export const TRIAL_SEAT_CAP = 10;
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

// Resolve the seat cap for a tenant's plan. Unknown / unset plan → trial default.
export function resolveCap(
  planProduct: string | null,
  planTier: string | null
): number {
  if (!planProduct || !planTier) return TRIAL_SEAT_CAP;
  const product = (PLAN_CAPS as Record<string, Record<string, number>>)[planProduct];
  if (!product) return TRIAL_SEAT_CAP;
  const cap = product[planTier];
  return typeof cap === "number" ? cap : TRIAL_SEAT_CAP;
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
