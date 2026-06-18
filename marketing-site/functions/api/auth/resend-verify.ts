// POST /api/auth/resend-verify — re-issue the verification email.
// Anti-enumeration: always 200, even if the email is unknown or already verified.
// IP rate-limiting is enforced by a Cloudflare dashboard rule (see README).

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight } from "./_lib/cors";
import { normEmail } from "./_lib/validate";
import { randomToken } from "./_lib/tokens";
import { getUserByEmail, setVerifyToken } from "./_lib/db";
import { sendVerifyEmail } from "./_lib/email";

const VERIFY_TTL_MS = 24 * 60 * 60 * 1000;
const GENERIC = {
  ok: true,
  message: "If that account exists and is unverified, a new email is on its way.",
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
  if (user && !user.email_verified_at) {
    const token = randomToken(32);
    const expires = new Date(Date.now() + VERIFY_TTL_MS).toISOString();
    await setVerifyToken(env.WAITLIST_DB, user.id, token, expires);
    await sendVerifyEmail(env, user.email, user.first_name, token);
  }

  return GENERIC;
});
