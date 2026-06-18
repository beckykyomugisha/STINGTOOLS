// POST /api/auth/password/reset — set a new password using the email token.
// The token is hashed and matched against the stored hash; expiry is enforced.
// On success: rotate the password, revoke ALL existing sessions (so a leaked
// session can't survive a reset), and return a fresh logged-in token pair.

import { withHandler, readJson } from "../_lib/handler";
import { handlePreflight, jsonResponse } from "../_lib/cors";
import { bad, unauthorized } from "../_lib/errors";
import { validatePassword } from "../_lib/validate";
import { sha256Hex } from "../_lib/tokens";
import {
  getUserByResetTokenHash,
  getUserById,
  getTenantById,
  applyPasswordReset,
  revokeAllUserSessions,
  toPublicUser,
  toPublicTenant,
} from "../_lib/db";
import { hashPassword } from "../_lib/password";
import { issueTokens, refreshCookie } from "../_lib/session";

interface Body {
  token?: string;
  newPassword?: string;
  password?: string; // accepted as an alias for newPassword
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);
  const token = (body.token || "").trim();
  if (!token) throw bad("Missing reset token.");
  const password = validatePassword(body.newPassword ?? body.password);

  const tokenHash = await sha256Hex(token);
  const user = await getUserByResetTokenHash(env.WAITLIST_DB, tokenHash);
  if (!user) throw bad("This reset link is invalid or has already been used.");

  if (
    !user.password_reset_expires_at ||
    new Date(user.password_reset_expires_at).getTime() <= Date.now()
  ) {
    throw bad("This reset link has expired. Request a new one.");
  }

  const passwordHash = await hashPassword(password);
  await applyPasswordReset(env.WAITLIST_DB, user.id, passwordHash);
  await revokeAllUserSessions(env.WAITLIST_DB, user.id, "password_reset");

  // Re-read so the issued JWT reflects the post-reset state, then log them in.
  const fresh = (await getUserById(env.WAITLIST_DB, user.id)) ?? user;
  const tenant = await getTenantById(env.WAITLIST_DB, fresh.tenant_id);
  if (!tenant) throw unauthorized("Account no longer exists.");

  const tokens = await issueTokens(env, fresh, tenant, request);

  return jsonResponse(
    request,
    {
      token: tokens.token,
      refreshToken: tokens.refreshToken,
      user: toPublicUser(fresh),
      tenant: toPublicTenant(tenant),
    },
    200,
    { "Set-Cookie": refreshCookie(tokens.refreshToken) }
  );
});
