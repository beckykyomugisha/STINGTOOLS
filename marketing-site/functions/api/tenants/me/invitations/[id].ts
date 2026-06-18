// DELETE /api/tenants/me/invitations/:id — cancel a pending invitation (admin+).

import { withHandler, pathParam } from "../../../auth/_lib/handler";
import { handlePreflight } from "../../../auth/_lib/cors";
import { requireRole } from "../../../auth/_lib/auth";
import { bad, notFound } from "../../../auth/_lib/errors";
import { getInvitationById, deleteInvitation, audit } from "../../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestDelete = withHandler(async ({ request, env, params }) => {
  const auth = await requireRole(request, env, "admin");
  const id = pathParam(params, "id");

  const invite = await getInvitationById(env.WAITLIST_DB, auth.tenantId, id);
  if (!invite) throw notFound("Invitation not found.");
  if (invite.accepted_at) throw bad("That invitation was already accepted.");
  if (invite.declined_at) throw bad("That invitation was already declined.");

  await deleteInvitation(env.WAITLIST_DB, id);
  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: "invitation.cancelled",
    target: id,
    metadata: { email: invite.email, role: invite.role },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return { ok: true, cancelled: id };
});
