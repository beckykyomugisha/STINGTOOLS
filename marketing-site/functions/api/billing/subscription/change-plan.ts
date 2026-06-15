// POST /api/billing/subscription/change-plan — owner-only. Switches the active
// subscription to a new tier. Upgrades charge a prorated amount immediately
// (proration_behavior=always_invoice); downgrades apply at the next renewal with
// no mid-cycle credit/charge (proration_behavior=none). Stripe does the math.
// Body: { tier, product?, cycle? } — product/cycle default to the current plan.

import { withHandler, readJson } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireRole } from "../../auth/_lib/auth";
import { bad, serverError } from "../../auth/_lib/errors";
import { audit } from "../../auth/_lib/db";
import {
  getPlan,
  isProduct,
  isBillingCycle,
  isStripeCurrency,
} from "../../_lib/billing/catalog";
import { unitAmountMinor, stripeInterval } from "../../_lib/billing/pricing";
import {
  stripeGet,
  stripePost,
  safeBillingError,
  type StripeSubscription,
} from "../../_lib/billing/stripe";
import {
  getActiveSubscription,
  setSubscriptionPlan,
  setTenantPlan,
} from "../../_lib/billing/state";

interface Body {
  product?: string;
  tier?: string;
  cycle?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "owner");
  if (!env.STRIPE_SECRET_KEY) throw serverError("Billing is not configured.");

  const sub = await getActiveSubscription(env.WAITLIST_DB, auth.tenantId);
  if (!sub || !sub.provider_subscription_id) {
    throw bad("No active subscription to change.");
  }

  const body = await readJson<Body>(request);
  const product = (body.product || sub.product).trim();
  const tier = (body.tier || "").trim().toLowerCase();
  const cycle = (body.cycle || sub.billing_cycle).trim().toLowerCase();
  const currency = sub.currency.toUpperCase();

  if (!isProduct(product)) throw bad("Unknown product.");
  if (!tier) throw bad("A target tier is required.");
  if (!isBillingCycle(cycle)) throw bad("Cycle must be 'monthly' or 'annual'.");
  if (!isStripeCurrency(currency)) throw bad("Currency not supported on Stripe.");
  const plan = getPlan(product, tier);
  if (!plan) throw bad("Unknown plan tier.");
  if (plan.usdMonthly === null) {
    throw bad("That plan is enterprise — please contact sales.");
  }
  if (product === sub.product && tier === sub.tier && cycle === sub.billing_cycle) {
    throw bad("That's already your current plan.");
  }

  const newAmount = unitAmountMinor(plan.usdMonthly, currency, cycle);
  const isUpgrade = newAmount > sub.amount_cents;

  // Fetch the live subscription to get its item id for the price swap.
  let live: StripeSubscription;
  try {
    live = await stripeGet<StripeSubscription>(
      `/subscriptions/${sub.provider_subscription_id}`,
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }
  const itemId = live!.items?.data?.[0]?.id;
  if (!itemId) throw serverError("Could not read the current subscription.");

  try {
    await stripePost<StripeSubscription>(
      `/subscriptions/${sub.provider_subscription_id}`,
      {
        proration_behavior: isUpgrade ? "always_invoice" : "none",
        items: [
          {
            id: itemId,
            price_data: {
              currency: currency.toLowerCase(),
              product_data: { name: `Planscape ${product} ${tier} (${cycle})` },
              recurring: { interval: stripeInterval(cycle) },
              unit_amount: newAmount,
            },
          },
        ],
        "metadata[tenant_id]": auth.tenantId,
        "metadata[product]": product,
        "metadata[tier]": tier,
        "metadata[cycle]": cycle,
        "metadata[currency]": currency,
        "metadata[amount_cents]": String(newAmount),
      },
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }

  await setSubscriptionPlan(env.WAITLIST_DB, sub.id, {
    product,
    tier,
    billingCycle: cycle,
    amountCents: newAmount,
  });
  // Keep the tenant's plan claims in step; subscription.updated webhook reconciles
  // status. Soft-cap grace (14 days) cushions any seat overage from a downgrade.
  await setTenantPlan(env.WAITLIST_DB, auth.tenantId, {
    product,
    tier,
    status: sub.status === "cancelled" ? "active" : sub.status,
  });

  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: isUpgrade ? "subscription.upgraded" : "subscription.downgraded",
    target: sub.id,
    metadata: {
      from: { product: sub.product, tier: sub.tier, cycle: sub.billing_cycle },
      to: { product, tier, cycle },
      amountCents: newAmount,
      prorated: isUpgrade,
    },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return {
    ok: true,
    product,
    tier,
    billingCycle: cycle,
    amountCents: newAmount,
    currency,
    prorated: isUpgrade,
    effective: isUpgrade ? "immediate" : "next_period",
  };
});
