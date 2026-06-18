// GET /api/tenants/me/members — list active members (any authenticated member).

import { withHandler } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireAuth } from "../../auth/_lib/auth";
import { listMembers, toMemberView } from "../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireAuth(request, env);
  const members = await listMembers(env.WAITLIST_DB, auth.tenantId);
  return { members: members.map(toMemberView) };
});
