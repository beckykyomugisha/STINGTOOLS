// POST /api/auth/password/forgot — issue a reset email.
// Always 200 (anti-enumeration): the response is identical whether or not the
// email maps to an account. The reset token is sent only in the email; only its
// SHA-256 hash is stored.

import { withHandler, readJson } from "../_lib/handler";
import { handlePreflight } from "../_lib/cors";
import { normEmail } from "../_lib/validate";
import { randomToken, sha256Hex } from "../_lib/tokens";
import { getUserByEmail, setResetToken } from "../_lib/db";
import { sendResetEmail } from "../_lib/email";

const RESET_TTL_MS = 60 * 60 * 1000; // 1h
const GENERIC = {
  ok: true,
  message: "If an account exists for that email, a reset link is on its way.",
};

interface Body {
  email?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);
  const email = normEmail(body.email);

  const user = await getUserByEmail(env.WAITLIST_DB, email);
  if (user) {
    const token = randomToken(32);
    const tokenHash = await sha256Hex(token);
    const expires = new Date(Date.now() + RESET_TTL_MS).toISOString();
    await setResetToken(env.WAITLIST_DB, user.id, tokenHash, expires);
    await sendResetEmail(env, user.email, user.first_name, token);
  }

  return GENERIC;
});
