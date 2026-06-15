// POST /api/billing/subscription/resume — owner-only. Clears a pending
// cancellation (cancel_at_period_end=0) so billing continues normally.

import { withHandler } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireRole } from "../../auth/_lib/auth";
import { bad, serverError } from "../../auth/_lib/errors";
import { audit } from "../../auth/_lib/db";
import {
  stripePost,
  safeBillingError,
  type StripeSubscription,
} from "../../_lib/billing/stripe";
import {
  getActiveSubscription,
  setSubscriptionCancelAtPeriodEnd,
} from "../../_lib/billing/state";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "owner");
  if (!env.STRIPE_SECRET_KEY) throw serverError("Billing is not configured.");

  const sub = await getActiveSubscription(env.WAITLIST_DB, auth.tenantId);
  if (!sub || !sub.provider_subscription_id) {
    throw bad("No subscription to resume.");
  }

  try {
    await stripePost<StripeSubscription>(
      `/subscriptions/${sub.provider_subscription_id}`,
      { cancel_at_period_end: false },
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }

  await setSubscriptionCancelAtPeriodEnd(env.WAITLIST_DB, sub.id, false);
  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: "subscription.resumed",
    target: sub.id,
    metadata: { providerSubscriptionId: sub.provider_subscription_id },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return { ok: true, cancelAtPeriodEnd: false };
});
