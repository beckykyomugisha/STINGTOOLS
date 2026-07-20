// GET /api/billing/plans — public-ish plan catalog with computed prices.
// No auth: the pricing page and signup flow read this. Optional query
// ?currency=<one of the 7 supported> narrows the price matrix to one currency
// (default: all). Every advertised currency routes to a provider (Stripe or
// Pesapal); NGN/ZAR are deliberately NOT advertised (deferred).

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { bad } from "../auth/_lib/errors";
import {
  PLAN_CATALOG,
  ANNUAL_DISCOUNT,
  FX_FROM_USD_ALL,
  providerForCurrency,
  type PlanProduct,
} from "../_lib/billing/catalog";
import { unitAmountMinorFor } from "../_lib/billing/pricing";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request }) => {
  const url = new URL(request.url);
  const cur = url.searchParams.get("currency");
  let currencies: string[];
  if (cur) {
    const upper = cur.toUpperCase();
    // providerForCurrency covers exactly the 7 payable currencies; unsupported
    // (incl. deferred NGN/ZAR) → 400.
    if (!providerForCurrency(upper)) throw bad("Unsupported currency.");
    currencies = [upper];
  } else {
    currencies = Object.keys(FX_FROM_USD_ALL);
  }

  // Payment provider per advertised currency, so the front-end can branch on
  // Stripe (redirect to Checkout) vs Pesapal (redirect to the mobile-money URL).
  const providers: Record<string, string> = {};
  for (const c of currencies) {
    const p = providerForCurrency(c);
    if (p) providers[c] = p;
  }

  const products = (Object.keys(PLAN_CATALOG) as PlanProduct[]).map((product) => {
    const tiers = PLAN_CATALOG[product] as Record<
      string,
      { usdMonthly: number | null; capSeats?: number; capCoordinators?: number }
    >;
    return {
      product,
      tiers: Object.keys(tiers).map((tier) => {
        const entry = tiers[tier];
        const cap = entry.capSeats ?? entry.capCoordinators ?? null;
        const prices: Record<string, { monthly: number; annual: number } | null> = {};
        for (const c of currencies) {
          prices[c] =
            entry.usdMonthly === null
              ? null // enterprise — contact sales
              : {
                  // unitAmountMinorFor honours zero-decimal currencies (UGX/RWF)
                  // and every FX rate — unlike the Stripe-only pricing helper,
                  // which returns NaN for Pesapal currencies.
                  monthly: unitAmountMinorFor(entry.usdMonthly, c, "monthly"),
                  annual: unitAmountMinorFor(entry.usdMonthly, c, "annual"),
                };
        }
        return {
          tier,
          usdMonthly: entry.usdMonthly,
          cap: cap === Infinity ? null : cap,
          contactSales: entry.usdMonthly === null,
          prices,
        };
      }),
    };
  });

  return {
    products,
    currencies,
    providers,
    annualDiscount: ANNUAL_DISCOUNT,
    fxFromUsd: FX_FROM_USD_ALL,
  };
});
