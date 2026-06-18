// POST /api/auth/refresh — opaque refresh token → new JWT + rotated refresh.
// Single-use: the presented refresh token is revoked and a new one minted.

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight, jsonResponse } from "./_lib/cors";
import { unauthorized } from "./_lib/errors";
import { sha256Hex } from "./_lib/tokens";
import {
  getSessionByTokenHash,
  getUserById,
  getTenantById,
  revokeSessionByTokenHash,
  revokeAllUserSessions,
} from "./_lib/db";
import { rotateTokens, refreshCookie, readRefreshCookie } from "./_lib/session";

interface Body {
  refreshToken?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request).catch(() => ({} as Body));
  const presented =
    (typeof body.refreshToken === "string" && body.refreshToken) ||
    readRefreshCookie(request);
  if (!presented) throw unauthorized("Missing refresh token.");

  const tokenHash = await sha256Hex(presented);
  const session = await getSessionByTokenHash(env.WAITLIST_DB, tokenHash);

  if (!session) throw unauthorized("Invalid or expired refresh token.");

  if (session.revoked_at) {
    // Replay detection: a token whose session was revoked because it was
    // already ROTATED means someone is reusing a spent refresh token — treat
    // it as a stolen-token incident and kill every live session for the user.
    if (session.revoked_reason === "rotated") {
      await revokeAllUserSessions(env.WAITLIST_DB, session.user_id, "replay");
    }
    throw unauthorized("Invalid or expired refresh token.");
  }

  if (new Date(session.expires_at).getTime() <= Date.now()) {
    await revokeSessionByTokenHash(env.WAITLIST_DB, tokenHash, "expiry");
    throw unauthorized("Invalid or expired refresh token.");
  }

  const user = await getUserById(env.WAITLIST_DB, session.user_id);
  if (!user) throw unauthorized("Invalid or expired refresh token.");
  const tenant = await getTenantById(env.WAITLIST_DB, user.tenant_id);
  if (!tenant) throw unauthorized("Invalid or expired refresh token.");

  const tokens = await rotateTokens(env, user, tenant, session.id, request);

  return jsonResponse(
    request,
    { token: tokens.token, refreshToken: tokens.refreshToken },
    200,
    { "Set-Cookie": refreshCookie(tokens.refreshToken) }
  );
});
