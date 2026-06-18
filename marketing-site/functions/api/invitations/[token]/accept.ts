// POST /api/invitations/:token/accept — public. Creates the invited user under
// the inviting tenant (email is proven verified by clicking the link) and logs
// them in. Body: { firstName, lastName, password }.

import { withHandler, readJson, pathParam } from "../../auth/_lib/handler";
import { handlePreflight, jsonResponse } from "../../auth/_lib/cors";
import { notFound, gone, conflict, unauthorized } from "../../auth/_lib/errors";
import { validatePassword, validateName } from "../../auth/_lib/validate";
import { hashPassword } from "../../auth/_lib/password";
import { sha256Hex, uuid } from "../../auth/_lib/tokens";
import {
  getInvitationByTokenHash,
  getUserByEmail,
  getTenantById,
  createInvitedUser,
  markInvitationAccepted,
  toPublicUser,
  toPublicTenant,
  audit,
} from "../../auth/_lib/db";
import { issueTokens, refreshCookie } from "../../auth/_lib/session";

interface Body {
  firstName?: string;
  lastName?: string;
  password?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env, params }) => {
  const token = pathParam(params, "token");
  const body = await readJson<Body>(request);

  const invite = await getInvitationByTokenHash(env.WAITLIST_DB, await sha256Hex(token));
  if (!invite) throw notFound("This invitation link is invalid.");
  if (invite.accepted_at) throw gone("This invitation has already been accepted.");
  if (invite.declined_at) throw gone("This invitation was declined.");
  if (new Date(invite.expires_at).getTime() <= Date.now()) {
    throw gone("This invitation has expired.");
  }

  const firstName = validateName(body.firstName, "first name");
  const lastName = validateName(body.lastName, "last name");
  const password = validatePassword(body.password);

  // Single-tenant-per-user model: an email already registered anywhere can't be
  // pulled into a second tenant. Multi-tenant membership is a future change.
  if (await getUserByEmail(env.WAITLIST_DB, invite.email)) {
    throw conflict(
      "An account with this email already exists. Multi-team membership isn't supported yet."
    );
  }

  const tenant = await getTenantById(env.WAITLIST_DB, invite.tenant_id);
  if (!tenant) throw unauthorized("That team no longer exists.");

  const passwordHash = await hashPassword(password);
  const user = await createInvitedUser(env.WAITLIST_DB, {
    id: uuid(),
    tenantId: invite.tenant_id,
    email: invite.email,
    passwordHash,
    firstName,
    lastName,
    role: invite.role,
  });
  await markInvitationAccepted(env.WAITLIST_DB, invite.id);

  await audit(env.WAITLIST_DB, {
    tenantId: invite.tenant_id,
    actorUserId: user.id,
    action: "invitation.accepted",
    target: invite.id,
    metadata: { email: invite.email, role: invite.role },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  const tokens = await issueTokens(env, user, tenant, request);
  return jsonResponse(
    request,
    {
      token: tokens.token,
      refreshToken: tokens.refreshToken,
      user: toPublicUser(user),
      tenant: toPublicTenant(tenant),
    },
    200,
    { "Set-Cookie": refreshCookie(tokens.refreshToken) }
  );
});
