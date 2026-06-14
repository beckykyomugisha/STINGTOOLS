// POST /api/auth/refresh — opaque refresh token → new JWT + rotated refresh.
// Single-use: the presented refresh token is revoked and a new one minted.

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight, jsonResponse } from "./_lib/cors";
import { unauthorized } from "./_lib/errors";
import { sha256Hex } from "./_lib/tokens";
import { getSessionByTokenHash, getUserById, revokeSessionByTokenHash } from "./_lib/db";
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

  if (!session || session.revoked_at) {
    // A revoked token being replayed may indicate theft — revoke defensively.
    if (session) await revokeSessionByTokenHash(env.WAITLIST_DB, tokenHash);
    throw unauthorized("Invalid or expired refresh token.");
  }
  if (new Date(session.expires_at).getTime() <= Date.now()) {
    await revokeSessionByTokenHash(env.WAITLIST_DB, tokenHash);
    throw unauthorized("Invalid or expired refresh token.");
  }

  const user = await getUserById(env.WAITLIST_DB, session.user_id);
  if (!user) throw unauthorized("Invalid or expired refresh token.");

  const tokens = await rotateTokens(env, user, session.id, request);

  return jsonResponse(
    request,
    { token: tokens.token, refreshToken: tokens.refreshToken },
    200,
    { "Set-Cookie": refreshCookie(tokens.refreshToken) }
  );
});
