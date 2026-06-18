// POST /api/billing/checkout — owner-only. Starts a subscription checkout,
// routing by currency: USD/EUR/GBP → Stripe Checkout (inline price_data),
// UGX/KES/TZS/RWF → Pesapal SubmitOrderRequest. Body: { product, tier, cycle,
// currency, discount? }. Returns { checkoutUrl }. Honours the Idempotency-Key
// header: a replay returns the same checkoutUrl.

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
  isProduct,
  isBillingCycle,
  isStripeCurrency,
  providerForCurrency,
  type StripeCurrency,
} from "../_lib/billing/catalog";
import {
  unitAmountMinor,
  unitAmountMinorFor,
  minorToMajor,
  stripeInterval,
} from "../_lib/billing/pricing";
import {
  stripePost,
  stripeCreateCoupon,
  safeBillingError,
  type StripeCustomer,
  type StripeCheckoutSession,
} from "../_lib/billing/stripe";
import { getAccessToken, submitOrder } from "../_lib/billing/pesapal";
import { setTenantStripeCustomer, createPesapalOrder, setPesapalOrderTracking } from "../_lib/billing/state";
import { validateDiscount, applyDiscountMinor, normalizeCode, type DiscountRow } from "../_lib/billing/discounts";
import { uuid } from "../auth/_lib/tokens";
import type { Env, TenantRow } from "../auth/_lib/types";

const ENDPOINT = "billing/checkout";

interface Body {
  product?: string;
  tier?: string;
  cycle?: string;
  currency?: string;
  discount?: string;
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

  // Idempotency: replay returns the first call's cached response verbatim.
  const idemKey = idempotencyKey(request);
  if (idemKey) {
    const cached = await getCachedResponse(env, idemKey, ENDPOINT);
    if (cached) return cached;
  }

  const body = await readJson<Body>(request);
  const url = new URL(request.url);
  const product = (body.product || "").trim();
  const tier = (body.tier || "").trim().toLowerCase();
  const cycle = (body.cycle || "monthly").trim().toLowerCase();
  const currency = (body.currency || "USD").trim().toUpperCase();
  const discountInput = (body.discount || url.searchParams.get("discount") || "").trim();

  if (!isProduct(product)) throw bad("Unknown product.");
  if (!isBillingCycle(cycle)) throw bad("Cycle must be 'monthly' or 'annual'.");
  const provider = providerForCurrency(currency);
  if (!provider) throw bad("Currency not supported.");
  const plan = getPlan(product, tier);
  if (!plan) throw bad("Unknown plan tier.");
  if (plan.usdMonthly === null) {
    throw bad("That plan is enterprise — please contact sales.");
  }

  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");
  const owner = await getUserById(env.WAITLIST_DB, auth.userId);
  if (!owner) throw unauthorized("Account no longer exists.");

  // Validate the discount (if any) before we touch the provider.
  let discount: DiscountRow | null = null;
  if (discountInput) {
    const v = await validateDiscount(env.WAITLIST_DB, discountInput, {
      product,
      tier,
      currency,
      tenantId: tenant.id,
    });
    if (!v.ok) throw bad(v.error);
    discount = v.discount;
  }

  const responseBody =
    provider === "stripe"
      ? await stripeCheckout(env, tenant, owner, { product, tier, cycle, currency, plan, discount }, idemKey)
      : await pesapalCheckout(env, tenant, owner, { product, tier, cycle, currency, plan, discount });

  if (idemKey) {
    await saveResponse(env, idemKey, ENDPOINT, tenant.id, 200, JSON.stringify(responseBody));
  }
  return jsonResponse(request, responseBody, 200);
});

interface CheckoutArgs {
  product: string;
  tier: string;
  cycle: string;
  currency: string;
  plan: { usdMonthly: number | null };
  discount: DiscountRow | null;
}

// ---- Stripe path ----------------------------------------------------------
// Full price stays in the inline price; a one-time Stripe coupon carries the
// discount so only the first invoice is reduced.
async function stripeCheckout(
  env: Env,
  tenant: TenantRow,
  owner: { email: string; first_name: string; last_name: string },
  args: CheckoutArgs,
  idemKey: string | null
) {
  if (!env.STRIPE_SECRET_KEY) throw serverError("Billing is not configured.");
  const currency = args.currency as StripeCurrency;
  if (!isStripeCurrency(currency)) throw bad("Currency not supported on Stripe.");

  const unitAmount = unitAmountMinor(args.plan.usdMonthly as number, currency, args.cycle as "monthly" | "annual");
  const customerId = await ensureCustomer(env, tenant, owner.email, `${owner.first_name} ${owner.last_name}`);

  const meta: Record<string, string> = {
    tenant_id: tenant.id,
    product: args.product,
    tier: args.tier,
    cycle: args.cycle,
    currency,
    amount_cents: String(unitAmount),
  };

  // One-time coupon so renewals stay at full price (matches discount semantics).
  let discounts: Array<{ coupon: string }> | undefined;
  if (args.discount) {
    meta.discount = args.discount.code;
    const coupon = await stripeCreateCoupon(
      { secretKey: env.STRIPE_SECRET_KEY, idempotencyKey: idemKey ? `${idemKey}-coupon` : null },
      args.discount.percent_off !== null
        ? { percentOff: args.discount.percent_off, name: args.discount.code }
        : { amountOff: args.discount.amount_off_cents as number, currency, name: args.discount.code }
    );
    discounts = [{ coupon: coupon.id }];
  }

  let session: StripeCheckoutSession;
  try {
    session = await stripePost<StripeCheckoutSession>(
      "/checkout/sessions",
      {
        mode: "subscription",
        customer: customerId,
        client_reference_id: tenant.id,
        success_url: `${appOrigin(env)}/billing-success?checkout=success&session_id={CHECKOUT_SESSION_ID}&tier=${encodeURIComponent(args.tier)}&product=${encodeURIComponent(args.product)}`,
        cancel_url: `${appOrigin(env)}/pricing.html?checkout=cancelled`,
        line_items: [
          {
            quantity: 1,
            price_data: {
              currency: currency.toLowerCase(),
              product_data: { name: `Planscape ${args.product} ${args.tier} (${args.cycle})` },
              recurring: { interval: stripeInterval(args.cycle as "monthly" | "annual") },
              unit_amount: unitAmount,
            },
          },
        ],
        discounts,
        metadata: meta,
        subscription_data: { metadata: meta },
      },
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }

  if (!session!.url) throw serverError("Could not start checkout. Please try again.");
  return {
    provider: "stripe",
    checkoutUrl: session!.url,
    sessionId: session!.id,
    amountCents: unitAmount,
    currency,
    cycle: args.cycle,
  };
}

// ---- Pesapal path ---------------------------------------------------------
// One payment per period; the discount reduces the order amount directly. The
// pending order is persisted so the IPN can mint the subscription + invoice.
async function pesapalCheckout(
  env: Env,
  tenant: TenantRow,
  owner: { email: string; first_name: string; last_name: string },
  args: CheckoutArgs
) {
  if (!env.PESAPAL_CONSUMER_KEY || !env.PESAPAL_IPN_ID) {
    throw serverError("Mobile-money billing is not configured.");
  }
  const baseMinor = unitAmountMinorFor(args.plan.usdMonthly as number, args.currency, args.cycle as "monthly" | "annual");
  const chargedMinor = args.discount ? applyDiscountMinor(baseMinor, args.discount) : baseMinor;
  if (chargedMinor < 1) throw bad("Discount cannot reduce the total to zero.");

  const orderId = uuid();
  await createPesapalOrder(env.WAITLIST_DB, {
    id: orderId,
    tenantId: tenant.id,
    merchantReference: orderId,
    product: args.product,
    tier: args.tier,
    billingCycle: args.cycle,
    currency: args.currency,
    amountMinor: chargedMinor,
    discountCode: args.discount ? normalizeCode(args.discount.code) : null,
  });

  let result;
  try {
    const token = await getAccessToken(env);
    result = await submitOrder(env, token, {
      merchantReference: orderId,
      currency: args.currency,
      amount: minorToMajor(chargedMinor, args.currency),
      description: `Planscape ${args.product} ${args.tier} (${args.cycle})`,
      callbackUrl: `${appOrigin(env)}/billing-success?checkout=success&tier=${encodeURIComponent(args.tier)}&product=${encodeURIComponent(args.product)}`,
      cancellationUrl: `${appOrigin(env)}/pricing.html?checkout=cancelled`,
      email: owner.email,
      firstName: owner.first_name,
      lastName: owner.last_name,
      countryCode: tenant.country,
    });
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    console.error(`Pesapal checkout failed: ${msg}`);
    throw serverError("Could not start mobile-money checkout. Please try again.");
  }

  await setPesapalOrderTracking(env.WAITLIST_DB, orderId, result.order_tracking_id);
  return {
    provider: "pesapal",
    checkoutUrl: result.redirect_url,
    orderTrackingId: result.order_tracking_id,
    amountCents: chargedMinor,
    currency: args.currency,
    cycle: args.cycle,
  };
}
