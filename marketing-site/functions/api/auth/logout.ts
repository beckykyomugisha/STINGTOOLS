// POST /api/auth/logout — revoke the refresh token and clear the cookie.
// Idempotent: always returns 200 even if the token is unknown/already revoked.

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight, jsonResponse } from "./_lib/cors";
import { sha256Hex } from "./_lib/tokens";
import { revokeSessionByTokenHash } from "./_lib/db";
import { clearRefreshCookie, readRefreshCookie } from "./_lib/session";

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

  if (presented) {
    const tokenHash = await sha256Hex(presented);
    await revokeSessionByTokenHash(env.WAITLIST_DB, tokenHash, "logout");
  }

  return jsonResponse(request, { ok: true }, 200, {
    "Set-Cookie": clearRefreshCookie(),
  });
});
