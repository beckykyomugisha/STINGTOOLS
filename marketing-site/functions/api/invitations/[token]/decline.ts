// POST /api/invitations/:token/decline — public. Marks the invite declined.
// Returns 204. A subsequent /accept on the same token then 410s (Gone).

import { withHandler, pathParam } from "../../auth/_lib/handler";
import { handlePreflight, corsHeaders } from "../../auth/_lib/cors";
import { notFound, gone } from "../../auth/_lib/errors";
import { sha256Hex } from "../../auth/_lib/tokens";
import {
  getInvitationByTokenHash,
  markInvitationDeclined,
  audit,
} from "../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env, params }) => {
  const token = pathParam(params, "token");

  const invite = await getInvitationByTokenHash(env.WAITLIST_DB, await sha256Hex(token));
  if (!invite) throw notFound("This invitation link is invalid.");
  if (invite.accepted_at) throw gone("This invitation has already been accepted.");

  if (!invite.declined_at) {
    await markInvitationDeclined(env.WAITLIST_DB, invite.id);
    await audit(env.WAITLIST_DB, {
      tenantId: invite.tenant_id,
      actorUserId: null, // declined by the (unauthenticated) invitee
      action: "invitation.declined",
      target: invite.id,
      metadata: { email: invite.email },
      ip: request.headers.get("CF-Connecting-IP"),
      userAgent: request.headers.get("User-Agent"),
    });
  }

  return new Response(null, { status: 204, headers: corsHeaders(request) });
});
