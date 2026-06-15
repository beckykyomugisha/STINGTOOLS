// Plan catalog — the single source of truth for what a tenant can buy on Stripe.
// Prices are inline (price_data at checkout), so there are NO pre-created Stripe
// Price IDs to keep in sync. Seat caps mirror functions/api/auth/_lib/limits.ts
// (PLAN_CAPS) and marketing-site/pricing.html.

export const PLAN_CATALOG = {
  "sting-tools": {
    solo: { usdMonthly: 25, capSeats: 1 },
    studio: { usdMonthly: 90, capSeats: 5 },
    practice: { usdMonthly: 200, capSeats: 15 },
    firm: { usdMonthly: 400, capSeats: 40 },
    enterprise: { usdMonthly: null, capSeats: Infinity }, // contact sales
  },
  planscape: {
    solo: { usdMonthly: 60, capCoordinators: 3 },
    studio: { usdMonthly: 130, capCoordinators: 10 },
    practice: { usdMonthly: 280, capCoordinators: 25 },
    firm: { usdMonthly: 560, capCoordinators: 50 },
    large: { usdMonthly: 1000, capCoordinators: 100 },
    enterprise: { usdMonthly: null, capCoordinators: Infinity },
  },
} as const;

export const ANNUAL_DISCOUNT = 0.2; // 20% off the monthly equivalent

// Currency support for B3a (Stripe): USD, EUR, GBP. FX rates are pegged via a
// static table (refresh quarterly, not at checkout time) so users see
// predictable prices. UGX/KES/NGN/ZAR are Flutterwave-only — the checkout route
// rejects them with 400 until B3b.
export const FX_FROM_USD = { USD: 1.0, EUR: 0.92, GBP: 0.78 } as const;

export type StripeCurrency = keyof typeof FX_FROM_USD;
export type PlanProduct = keyof typeof PLAN_CATALOG;
export type BillingCycle = "monthly" | "annual";

// A plan entry as stored above (usdMonthly may be null for enterprise).
export interface PlanEntry {
  usdMonthly: number | null;
  capSeats?: number;
  capCoordinators?: number;
}

export function isStripeCurrency(c: string): c is StripeCurrency {
  return c in FX_FROM_USD;
}

export function isProduct(p: string): p is PlanProduct {
  return p in PLAN_CATALOG;
}

export function isBillingCycle(c: string): c is BillingCycle {
  return c === "monthly" || c === "annual";
}

// Resolve a plan entry, or null if the product/tier pair is unknown.
export function getPlan(product: string, tier: string): PlanEntry | null {
  if (!isProduct(product)) return null;
  const tiers = PLAN_CATALOG[product] as Record<string, PlanEntry>;
  return tiers[tier] ?? null;
}
