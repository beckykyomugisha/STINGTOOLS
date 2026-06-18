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

// Currency support is split by payment provider:
//   USD / EUR / GBP        → Stripe   (FX_FROM_USD)
//   UGX / KES / TZS / RWF  → Pesapal  (FX_FROM_USD_PESAPAL)
//   NGN / ZAR              → deferred (later expansion)
// FX rates are pegged via static tables (refresh quarterly, not at checkout
// time) so users see predictable prices. The router below maps a currency to
// its provider; the checkout route 400s anything unsupported.
export const FX_FROM_USD = { USD: 1.0, EUR: 0.92, GBP: 0.78 } as const;

// Pesapal currencies. Rates are approximate (June 2026) — refresh quarterly
// alongside FX_FROM_USD. UGX/RWF are zero-decimal (see CURRENCY_MINOR_EXP).
export const FX_FROM_USD_PESAPAL = {
  UGX: 3700,
  KES: 129,
  TZS: 2600,
  RWF: 1350,
} as const;

// Minor-unit exponent per currency. 2 = cents (default), 0 = no subunit.
// Drives both our stored *_cents columns and the major-unit amount Pesapal wants.
export const CURRENCY_MINOR_EXP: Record<string, number> = {
  USD: 2, EUR: 2, GBP: 2, KES: 2, TZS: 2, UGX: 0, RWF: 0,
};

export type StripeCurrency = keyof typeof FX_FROM_USD;
export type PesapalCurrency = keyof typeof FX_FROM_USD_PESAPAL;
export type PlanProduct = keyof typeof PLAN_CATALOG;
export type BillingCycle = "monthly" | "annual";

// Combined FX table for any supported currency, used by the generic pricing path.
export const FX_FROM_USD_ALL: Record<string, number> = {
  ...FX_FROM_USD,
  ...FX_FROM_USD_PESAPAL,
};

export type PaymentProvider = "stripe" | "pesapal";

export function isPesapalCurrency(c: string): c is PesapalCurrency {
  return c in FX_FROM_USD_PESAPAL;
}

// Route a currency to its payment provider, or null if unsupported.
export function providerForCurrency(c: string): PaymentProvider | null {
  if (c in FX_FROM_USD) return "stripe";
  if (c in FX_FROM_USD_PESAPAL) return "pesapal";
  return null;
}

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
