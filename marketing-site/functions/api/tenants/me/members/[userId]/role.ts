// POST /api/tenants/me/members/:userId/role — change a member's role.
// Authority (mirrors the B2 matrix): caller must be admin+, may only modify a
// member strictly below their own level, and may only assign a role at or below
// their own level. 'owner' is never assignable here (ownership transfer is a
// separate flow).

import { withHandler, readJson, pathParam } from "../../../../auth/_lib/handler";
import { handlePreflight } from "../../../../auth/_lib/cors";
import { requireRole, roleLevel, ASSIGNABLE_ROLES } from "../../../../auth/_lib/auth";
import { bad, forbidden, notFound } from "../../../../auth/_lib/errors";
import {
  getActiveMemberById,
  updateUserRole,
  toMemberView,
  audit,
} from "../../../../auth/_lib/db";
import { clip } from "../../../../auth/_lib/validate";

interface Body {
  role?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env, params }) => {
  const auth = await requireRole(request, env, "admin");
  const targetId = pathParam(params, "userId");
  const body = await readJson<Body>(request);

  const newRole = clip(body.role, 32).toLowerCase();
  if (!(ASSIGNABLE_ROLES as readonly string[]).includes(newRole)) {
    throw bad("Invalid role.");
  }
  if (targetId === auth.userId) throw bad("You can't change your own role.");

  const target = await getActiveMemberById(env.WAITLIST_DB, auth.tenantId, targetId);
  if (!target) throw notFound("Member not found.");

  const callerLvl = roleLevel(auth.role);
  if (roleLevel(target.role) >= callerLvl) {
    throw forbidden("You can't change a member at or above your own role.");
  }
  if (roleLevel(newRole) > callerLvl) {
    throw forbidden("You can't assign a role above your own.");
  }
  if (target.role === newRole) {
    return { member: toMemberView(target) }; // no-op
  }

  await updateUserRole(env.WAITLIST_DB, targetId, newRole);
  // The member's existing JWT keeps the old role until it expires (≤1h) or they
  // refresh — claims are re-minted on /refresh. We don't force-logout on a role
  // change in B2.
  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: "user.role_changed",
    target: targetId,
    metadata: { from: target.role, to: newRole },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return { member: toMemberView({ ...target, role: newRole }) };
});
