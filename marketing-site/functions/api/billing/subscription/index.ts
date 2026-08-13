// GET /api/billing/subscription — admin+. The tenant's current subscription, or
// null while they're still on trial. Read-only, provider-agnostic: the account
// page needs cancelAtPeriodEnd + currentPeriodEnd to decide between Cancel and
// Resume, and neither is exposed by toPublicTenant.

import { withHandler } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireRole } from "../../auth/_lib/auth";
import {
  getCurrentSubscription,
  toPublicSubscription,
} from "../../_lib/billing/state";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "admin");
  const sub = await getCurrentSubscription(env.WAITLIST_DB, auth.tenantId);
  return { subscription: sub ? toPublicSubscription(sub) : null };
});
