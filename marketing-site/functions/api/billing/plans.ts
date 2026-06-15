// GET /api/billing/plans — public-ish plan catalog with computed prices.
// No auth: the pricing page and signup flow read this. Optional query
// ?currency=USD|EUR|GBP narrows the price matrix to one currency (default: all).

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { bad } from "../auth/_lib/errors";
import {
  PLAN_CATALOG,
  ANNUAL_DISCOUNT,
  FX_FROM_USD,
  isStripeCurrency,
  type PlanProduct,
  type StripeCurrency,
} from "../_lib/billing/catalog";
import { unitAmountMinor } from "../_lib/billing/pricing";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request }) => {
  const url = new URL(request.url);
  const cur = url.searchParams.get("currency");
  let currencies: StripeCurrency[];
  if (cur) {
    const upper = cur.toUpperCase();
    if (!isStripeCurrency(upper)) throw bad("Unsupported currency.");
    currencies = [upper];
  } else {
    currencies = Object.keys(FX_FROM_USD) as StripeCurrency[];
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
                  monthly: unitAmountMinor(entry.usdMonthly, c, "monthly"),
                  annual: unitAmountMinor(entry.usdMonthly, c, "annual"),
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
    annualDiscount: ANNUAL_DISCOUNT,
    fxFromUsd: FX_FROM_USD,
  };
});
