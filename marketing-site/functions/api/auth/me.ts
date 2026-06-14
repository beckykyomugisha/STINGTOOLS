// GET /api/auth/me — current user + tenant from the JWT bearer token.

import { withHandler } from "./_lib/handler";
import { handlePreflight } from "./_lib/cors";
import { requireAuth } from "./_lib/auth";
import { unauthorized } from "./_lib/errors";
import {
  getUserById,
  getTenantById,
  expireTrialIfNeeded,
  toPublicUser,
  toPublicTenant,
} from "./_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireAuth(request, env);

  const user = await getUserById(env.WAITLIST_DB, auth.userId);
  if (!user) throw unauthorized("Account no longer exists.");
  let tenant = await getTenantById(env.WAITLIST_DB, user.tenant_id);
  if (!tenant) throw unauthorized("Account no longer exists.");

  // Lazy trial expiry: flip an elapsed trial to read_only on read.
  tenant = await expireTrialIfNeeded(env.WAITLIST_DB, tenant);

  return {
    user: toPublicUser(user),
    tenant: toPublicTenant(tenant),
  };
});
