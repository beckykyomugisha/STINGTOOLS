// POST /api/invitations/:token/preview — public. Returns just enough to render
// the accept screen, without consuming the invite. No auth.

import { withHandler, pathParam } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { bad, notFound, gone } from "../../auth/_lib/errors";
import { sha256Hex } from "../../auth/_lib/tokens";
import {
  getInvitationByTokenHash,
  getTenantById,
  getUserById,
} from "../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env, params }) => {
  const token = pathParam(params, "token");
  if (!token) throw bad("Missing invitation token.");

  const invite = await getInvitationByTokenHash(env.WAITLIST_DB, await sha256Hex(token));
  if (!invite) throw notFound("This invitation link is invalid.");
  if (invite.accepted_at) throw gone("This invitation has already been accepted.");
  if (invite.declined_at) throw gone("This invitation was declined.");
  if (new Date(invite.expires_at).getTime() <= Date.now()) {
    throw gone("This invitation has expired.");
  }

  const tenant = await getTenantById(env.WAITLIST_DB, invite.tenant_id);
  const inviter = await getUserById(env.WAITLIST_DB, invite.invited_by_user_id);

  return {
    email: invite.email,
    role: invite.role,
    tenantName: tenant?.name ?? "a team",
    inviterName: inviter ? `${inviter.first_name} ${inviter.last_name}` : "A teammate",
    expiresAt: invite.expires_at,
  };
});
