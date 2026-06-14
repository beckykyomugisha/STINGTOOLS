// POST /api/auth/login — email + password → JWT + refresh + HttpOnly cookie.

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight, jsonResponse } from "./_lib/cors";
import { unauthorized, bad } from "./_lib/errors";
import { normEmail } from "./_lib/validate";
import { verifyPassword } from "./_lib/password";
import {
  getUserByEmail,
  getTenantById,
  touchLastLogin,
  countActiveMembers,
  countPendingInvites,
  toPublicUser,
  toPublicTenant,
} from "./_lib/db";
import { issueTokens, refreshCookie } from "./_lib/session";
import { resolveCap, evaluateCap } from "./_lib/limits";

interface Body {
  email?: string;
  password?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);
  const email = normEmail(body.email);
  if (typeof body.password !== "string" || !body.password) {
    throw bad("Please provide your password.");
  }

  const user = await getUserByEmail(env.WAITLIST_DB, email);

  // Anti-enumeration: always do a password verification (against the stored
  // hash, or a throwaway compute when the user is absent) and return the same
  // generic 401 either way.
  const stored = user?.password_hash ?? "pbkdf2-v1$100000$AAAA$AAAA";
  const ok = await verifyPassword(body.password, stored);
  // A soft-deleted (removed) user must not authenticate — same generic 401.
  if (!user || !ok || user.deleted_at) throw unauthorized("Invalid email or password.");

  const tenant = await getTenantById(env.WAITLIST_DB, user.tenant_id);
  if (!tenant) throw unauthorized("Invalid email or password.");

  await touchLastLogin(env.WAITLIST_DB, user.id);
  const tokens = await issueTokens(env, user, tenant, request);

  // Soft-block-at-cap: surface (but never block login on) a read-only state so
  // the UI can show an upgrade banner once the grace period has ended.
  const members = await countActiveMembers(env.WAITLIST_DB, tenant.id);
  const pending = await countPendingInvites(env.WAITLIST_DB, tenant.id);
  const cap = resolveCap(tenant.plan_product, tenant.plan_tier);
  const capState = evaluateCap(members + pending, cap, tenant.cap_exceeded_since, Date.now());
  const capExceeded = !capState.within;
  const readOnlyMode = capExceeded && capState.graceEnded;

  return jsonResponse(
    request,
    {
      token: tokens.token,
      refreshToken: tokens.refreshToken,
      user: toPublicUser(user),
      tenant: toPublicTenant(tenant),
      capExceeded,
      readOnlyMode,
      gracePeriodEndsAt: capState.gracePeriodEndsAt,
    },
    200,
    { "Set-Cookie": refreshCookie(tokens.refreshToken) }
  );
});
