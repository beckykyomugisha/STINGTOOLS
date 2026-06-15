// POST /api/billing/portal — owner-only. Opens a Stripe Customer Portal session
// where the tenant can manage cards / view invoices. Returns { portalUrl }.

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireRole } from "../auth/_lib/auth";
import { bad, unauthorized, serverError } from "../auth/_lib/errors";
import { getTenantById } from "../auth/_lib/db";
import {
  stripePost,
  safeBillingError,
  type StripePortalSession,
} from "../_lib/billing/stripe";
import type { Env } from "../auth/_lib/types";

function appOrigin(env: Env): string {
  return env.APP_ORIGIN || "https://planscape.build";
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "owner");
  if (!env.STRIPE_SECRET_KEY) throw serverError("Billing is not configured.");

  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");
  if (!tenant.stripe_customer_id) {
    throw bad("No billing account yet. Start a subscription first.");
  }

  let session: StripePortalSession;
  try {
    session = await stripePost<StripePortalSession>(
      "/billing_portal/sessions",
      {
        customer: tenant.stripe_customer_id,
        return_url: `${appOrigin(env)}/account/billing`,
      },
      { secretKey: env.STRIPE_SECRET_KEY }
    );
  } catch (e) {
    safeBillingError(e);
  }

  return { portalUrl: session!.url };
});
