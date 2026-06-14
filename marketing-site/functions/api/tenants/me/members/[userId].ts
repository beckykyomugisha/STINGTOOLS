// DELETE /api/tenants/me/members/:userId — soft-delete a member.
// Contributions are preserved (tombstone only). Authority: admin+, may only
// remove a member strictly below their own level; can't remove self or an owner.

import { withHandler, pathParam } from "../../../auth/_lib/handler";
import { handlePreflight } from "../../../auth/_lib/cors";
import { requireRole, roleLevel } from "../../../auth/_lib/auth";
import { bad, forbidden, notFound } from "../../../auth/_lib/errors";
import { getActiveMemberById, softDeleteUser, audit } from "../../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestDelete = withHandler(async ({ request, env, params }) => {
  const auth = await requireRole(request, env, "admin");
  const targetId = pathParam(params, "userId");
  if (targetId === auth.userId) throw bad("You can't remove yourself.");

  const target = await getActiveMemberById(env.WAITLIST_DB, auth.tenantId, targetId);
  if (!target) throw notFound("Member not found.");
  if (roleLevel(target.role) >= roleLevel(auth.role)) {
    throw forbidden("You can't remove a member at or above your own role.");
  }

  await softDeleteUser(env.WAITLIST_DB, targetId);
  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: "user.removed",
    target: targetId,
    metadata: { email: target.email, role: target.role },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return { ok: true, removed: targetId };
});
