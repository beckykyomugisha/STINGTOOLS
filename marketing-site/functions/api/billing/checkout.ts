// POST /api/billing/checkout — owner-only. Creates a Stripe Checkout Session for
// a subscription using INLINE price_data (no pre-created Stripe Price IDs).
// Body: { product, tier, cycle, currency }. Returns { checkoutUrl }.
// Honours the Idempotency-Key header: a replay returns the same checkoutUrl.

import { withHandler, readJson } from "../auth/_lib/handler";
import { handlePreflight, jsonResponse } from "../auth/_lib/cors";
import { requireRole } from "../auth/_lib/auth";
import { bad, unauthorized, serverError } from "../auth/_lib/errors";
import { getTenantById, getUserById } from "../auth/_lib/db";
import {
  idempotencyKey,
  getCachedResponse,
  saveResponse,
} from "../auth/_lib/idempotency";
import {
  getPlan,
  isStripeCurrency,
  isProduct,
  isBillingCycle,
} from "../_lib/billing/catalog";
import { unitAmountMinor, stripeInterval } from "../_lib/billing/pricing";
import {
  stripePost,
  safeBillingError,
  type StripeCustomer,
  type StripeCheckoutSession,
} from "../_lib/billing/stripe";
import { setTenantStripeCustomer } from "../_lib/billing/state";
import type { Env, TenantRow } from "../auth/_lib/types";

const ENDPOINT = "billing/checkout";

interface Body {
  product?: string;
  tier?: string;
  cycle?: string;
  currency?: string;
}

function appOrigin(env: Env): string {
  return env.APP_ORIGIN || "https://planscape.build";
}

// Ensure the tenant has a Stripe customer; create + persist on first checkout.
async function ensureCustomer(
  env: Env,
  tenant: TenantRow,
  email: string,
  name: string
): Promise<string> {
  if (tenant.stripe_customer_id) return tenant.stripe_customer_id;
  const customer = await stripePost<StripeCustomer>(
    "/customers",
    { email, name, "metadata[tenant_id]": tenant.id },
    { secretKey: env.STRIPE_SECRET_KEY as string }
  );
  await setTenantStripeCustomer(env.WAITLIST_DB, tenant.id, customer.id);
  return customer.id;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "owner");
  if (!env.STRIPE_SECRET_KEY) throw serverError("Billing is not configured.");

  // Idempotency: replay returns the first call's cached response verbatim.
  const idemKey = idempotencyKey(request);
  if (idemKey) {
    const cached = await getCachedResponse(env, idemKey, ENDPOINT);
    if (cached) return cached;
  }

  const body = await readJson<Body>(request);
  const product = (body.product || "").trim();
  const tier = (body.tier || "").trim().toLowerCase();
  const cycle = (body.cycle || "monthly").trim().toLowerCase();
  const currency = (body.currency || "USD").trim().toUpperCase();

  if (!isProduct(product)) throw bad("Unknown product.");
  if (!isBillingCycle(cycle)) throw bad("Cycle must be 'monthly' or 'annual'.");
  if (!isStripeCurrency(currency)) {
    throw bad("Currency not yet supported on Stripe.");
  }
  const plan = getPlan(product, tier);
  if (!plan) throw bad("Unknown plan tier.");
  if (plan.usdMonthly === null) {
    throw bad("That plan is enterprise — please contact sales.");
  }

  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");
  const owner = await getUserById(env.WAITLIST_DB, auth.userId);
  if (!owner) throw unauthorized("Account no longer exists.");

  const unitAmount = unitAmountMinor(plan.usdMonthly, currency, cycle);
  const customerId = await ensureCustomer(
    env,
    tenant,
    owner.email,
    `${owner.first_name} ${owner.last_name}`
  );

  const meta = {
    tenant_id: tenant.id,
    product,
    tier,
    cycle,
    currency,
    amount_cents: String(unitAmount),
  };

  let session: StripeCheckoutSession;
  try {
    session = await stripePost<StripeCheckoutSession>(
      "/checkout/sessions",
      {
        mode: "subscription",
        customer: customerId,
        client_reference_id: tenant.id,
        success_url: `${appOrigin(env)}/account/billing?checkout=success&session_id={CHECKOUT_SESSION_ID}`,
        cancel_url: `${appOrigin(env)}/account/billing?checkout=cancelled`,
        line_items: [
          {
            quantity: 1,
            price_data: {
              currency: currency.toLowerCase(),
              product_data: { name: `Planscape ${product} ${tier} (${cycle})` },
              recurring: { interval: stripeInterval(cycle) },
              unit_amount: unitAmount,
            },
          },
        ],
        metadata: meta,
        subscription_data: { metadata: meta },
      },
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }

  if (!session!.url) throw serverError("Could not start checkout. Please try again.");

  const responseBody = {
    checkoutUrl: session!.url,
    sessionId: session!.id,
    amountCents: unitAmount,
    currency,
    cycle,
  };
  if (idemKey) {
    await saveResponse(env, idemKey, ENDPOINT, tenant.id, 200, JSON.stringify(responseBody));
  }
  return jsonResponse(request, responseBody, 200);
});
