// POST /api/auth/password/reset — set a new password using the email token.
// The token is hashed and matched against the stored hash; expiry is enforced.
// Note: existing refresh sessions are NOT revoked here — project-wide session
// invalidation on password reset is a deliberate B2 hardening item.

import { withHandler, readJson } from "../_lib/handler";
import { handlePreflight } from "../_lib/cors";
import { bad } from "../_lib/errors";
import { validatePassword } from "../_lib/validate";
import { sha256Hex } from "../_lib/tokens";
import { getUserByResetTokenHash, applyPasswordReset } from "../_lib/db";
import { hashPassword } from "../_lib/password";

interface Body {
  token?: string;
  password?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);
  const token = (body.token || "").trim();
  if (!token) throw bad("Missing reset token.");
  const password = validatePassword(body.password);

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

  return { ok: true, message: "Your password has been reset. You can now log in." };
});
