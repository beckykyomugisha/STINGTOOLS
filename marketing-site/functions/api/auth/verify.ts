// /api/auth/verify — confirm email with the token from the verification email.
// Supports GET (?token=… , the link people click) and POST ({ "token": … }).
// Both return JSON (no redirects, per the API contract).

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight } from "./_lib/cors";
import { bad } from "./_lib/errors";
import {
  getUserByVerifyToken,
  markEmailVerified,
  getTenantById,
  getUserById,
  toPublicUser,
  toPublicTenant,
} from "./_lib/db";
import { sendWelcomeEmail } from "./_lib/email";
import type { Env } from "./_lib/types";

interface Body {
  token?: string;
}

async function consumeVerifyToken(env: Env, token: string) {
  const db = env.WAITLIST_DB;
  const user = await getUserByVerifyToken(db, token);
  if (!user) throw bad("This verification link is invalid or has already been used.");

  const tenant = await getTenantById(db, user.tenant_id);

  if (user.email_verified_at) {
    return { user: toPublicUser(user), tenant: tenant ? toPublicTenant(tenant) : null };
  }

  if (
    !user.email_verify_expires_at ||
    new Date(user.email_verify_expires_at).getTime() <= Date.now()
  ) {
    throw bad("This verification link has expired. Request a new one.");
  }

  await markEmailVerified(db, user.id);
  await sendWelcomeEmail(env, user.email, user.first_name);

  const fresh = (await getUserById(db, user.id)) ?? user;
  return { user: toPublicUser(fresh), tenant: tenant ? toPublicTenant(tenant) : null };
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const token = (new URL(request.url).searchParams.get("token") || "").trim();
  if (!token) throw bad("Missing verification token.");
  const result = await consumeVerifyToken(env, token);
  return { ok: true, emailVerified: true, ...result };
});

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);
  const token = (body.token || "").trim();
  if (!token) throw bad("Missing verification token.");
  const result = await consumeVerifyToken(env, token);
  return { ok: true, emailVerified: true, ...result };
});
