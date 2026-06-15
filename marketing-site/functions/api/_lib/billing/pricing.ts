// Currency conversion + annual-discount math. Pure functions, no I/O — so the
// same arithmetic runs at checkout, in /plans, and in change-plan comparisons.

import {
  ANNUAL_DISCOUNT,
  FX_FROM_USD,
  FX_FROM_USD_ALL,
  CURRENCY_MINOR_EXP,
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

// Minor-unit exponent for a currency (2 = cents, 0 = no subunit). Defaults to 2.
export function minorExp(currency: string): number {
  return CURRENCY_MINOR_EXP[currency] ?? 2;
}

// Generic version of unitAmountMinor that works for ANY supported currency,
// honouring zero-decimal currencies (UGX/RWF). Used by the Pesapal checkout
// path; the Stripe path keeps using unitAmountMinor above (all 2-decimal).
export function unitAmountMinorFor(
  usdMonthly: number,
  currency: string,
  cycle: BillingCycle
): number {
  const fx = FX_FROM_USD_ALL[currency] ?? 1;
  const factor = Math.pow(10, minorExp(currency));
  const monthlyMinor = Math.round(usdMonthly * fx * factor);
  const cycleMultiplier = cycle === "annual" ? 12 : 1;
  const discount = cycle === "annual" ? 1 - ANNUAL_DISCOUNT : 1;
  return Math.round(monthlyMinor * cycleMultiplier * discount);
}

// Convert our stored minor units back to the major-unit decimal that Pesapal's
// `amount` field expects (e.g. 600000 UGX-minor → 600000, 1290000 KES-minor →
// 12900.00). Zero-decimal currencies pass through unchanged.
export function minorToMajor(minorAmount: number, currency: string): number {
  const factor = Math.pow(10, minorExp(currency));
  return minorAmount / factor;
}
