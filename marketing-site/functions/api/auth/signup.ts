// POST /api/auth/signup — create tenant + first user (owner), email verify, tokens.

import { withHandler, readJson } from "./_lib/handler";
import { handlePreflight, jsonResponse } from "./_lib/cors";
import { conflict } from "./_lib/errors";
import {
  normEmail,
  validatePassword,
  validateName,
  validateFirmName,
  normCountry,
  slugify,
} from "./_lib/validate";
import { hashPassword } from "./_lib/password";
import { randomToken, uuid } from "./_lib/tokens";
import {
  getUserByEmail,
  createTenant,
  createUser,
  slugExists,
  toPublicUser,
  toPublicTenant,
} from "./_lib/db";
import { issueTokens, refreshCookie } from "./_lib/session";
import { sendVerifyEmail } from "./_lib/email";

const VERIFY_TTL_MS = 24 * 60 * 60 * 1000; // 24h

interface Body {
  email?: string;
  password?: string;
  firstName?: string;
  lastName?: string;
  firmName?: string;
  country?: string;
}

async function uniqueSlug(db: D1Database, base: string): Promise<string> {
  if (!(await slugExists(db, base))) return base;
  for (let i = 0; i < 50; i++) {
    const candidate = `${base}-${randomToken(3).toLowerCase().replace(/[^a-z0-9]/g, "").slice(0, 4)}`;
    if (!(await slugExists(db, candidate))) return candidate;
  }
  // Extremely unlikely; fall back to a uuid fragment.
  return `${base}-${uuid().slice(0, 8)}`;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const body = await readJson<Body>(request);

  const email = normEmail(body.email);
  const password = validatePassword(body.password);
  const firstName = validateName(body.firstName, "first name");
  const lastName = validateName(body.lastName, "last name");
  const firmName = validateFirmName(body.firmName);
  const country = normCountry(body.country);

  // Anti-enumeration is irrelevant on signup (the form is public) — a clear
  // 409 here is the better UX.
  const existing = await getUserByEmail(env.WAITLIST_DB, email);
  if (existing) throw conflict("An account with this email already exists.");

  const passwordHash = await hashPassword(password);
  const slug = await uniqueSlug(env.WAITLIST_DB, slugify(firmName));

  const tenant = await createTenant(env.WAITLIST_DB, {
    id: uuid(),
    name: firmName,
    slug,
    country,
  });

  const verifyToken = randomToken(32);
  const verifyExpires = new Date(Date.now() + VERIFY_TTL_MS).toISOString();

  let user;
  try {
    user = await createUser(env.WAITLIST_DB, {
      id: uuid(),
      tenantId: tenant.id,
      email,
      passwordHash,
      firstName,
      lastName,
      role: "owner",
      emailVerifyToken: verifyToken,
      emailVerifyExpiresAt: verifyExpires,
    });
  } catch (e) {
    // Race: a parallel signup with the same email won the UNIQUE constraint.
    if (await getUserByEmail(env.WAITLIST_DB, email)) {
      throw conflict("An account with this email already exists.");
    }
    throw e;
  }

  const tokens = await issueTokens(env, user, tenant, request);
  await sendVerifyEmail(env, email, firstName, verifyToken);

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
