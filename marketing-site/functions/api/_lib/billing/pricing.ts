// Currency conversion + annual-discount math. Pure functions, no I/O — so the
// same arithmetic runs at checkout, in /plans, and in change-plan comparisons.

import {
  ANNUAL_DISCOUNT,
  FX_FROM_USD,
  type BillingCycle,
  type StripeCurrency,
} from "./catalog";

// Minor units (cents) charged for one billing cycle of a plan priced at
// `usdMonthly` US dollars/month, converted to `currency` and with the annual
// discount applied when `cycle === 'annual'`.
//
//   monthlyMinor   = round(usdMonthly * fx * 100)
//   annual         = monthlyMinor * 12 * (1 - ANNUAL_DISCOUNT)
//   monthly        = monthlyMinor
export function unitAmountMinor(
  usdMonthly: number,
  currency: StripeCurrency,
  cycle: BillingCycle
): number {
  const fx = FX_FROM_USD[currency];
  const monthlyMinor = Math.round(usdMonthly * fx * 100);
  const cycleMultiplier = cycle === "annual" ? 12 : 1;
  const discount = cycle === "annual" ? 1 - ANNUAL_DISCOUNT : 1;
  return Math.round(monthlyMinor * cycleMultiplier * discount);
}

// Stripe's recurring interval for a billing cycle.
export function stripeInterval(cycle: BillingCycle): "month" | "year" {
  return cycle === "annual" ? "year" : "month";
}
