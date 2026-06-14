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
