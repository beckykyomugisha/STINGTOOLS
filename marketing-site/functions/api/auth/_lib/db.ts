// Typed D1 wrappers for the auth core. All queries are parameterised.
// Functions throw the raw D1 error to the caller, which maps it to a generic
// 500 — callers never surface DB internals to the client.

import type { TenantRow, UserRow, SessionRow, PublicUser, PublicTenant } from "./types";

const TRIAL_DAYS = 14;
const REFRESH_TTL_DAYS = 30;

// ---- shaping helpers ------------------------------------------------------

export function toPublicUser(u: UserRow): PublicUser {
  return {
    id: u.id,
    email: u.email,
    firstName: u.first_name,
    lastName: u.last_name,
    role: u.role,
    emailVerified: u.email_verified_at != null,
  };
}

export function toPublicTenant(t: TenantRow): PublicTenant {
  return {
    id: t.id,
    name: t.name,
    slug: t.slug,
    trialEndsAt: t.trial_ends_at,
    subscriptionStatus: t.subscription_status,
  };
}

// ---- tenants --------------------------------------------------------------

export interface CreateTenantInput {
  id: string;
  name: string;
  slug: string;
  country: string | null;
}

export async function createTenant(
  db: D1Database,
  input: CreateTenantInput
): Promise<TenantRow> {
  const now = new Date();
  const trialEnds = new Date(now.getTime() + TRIAL_DAYS * 86400_000);
  const nowIso = now.toISOString();
  await db
    .prepare(
      `INSERT INTO tenants
         (id, name, slug, country, currency, subscription_status,
          trial_started_at, trial_ends_at, created_at)
       VALUES (?,?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.id,
      input.name,
      input.slug,
      input.country,
      "USD",
      "trial",
      nowIso,
      trialEnds.toISOString(),
      nowIso
    )
    .run();
  const row = await getTenantById(db, input.id);
  if (!row) throw new Error("tenant insert vanished");
  return row;
}

export async function getTenantById(
  db: D1Database,
  id: string
): Promise<TenantRow | null> {
  return db.prepare(`SELECT * FROM tenants WHERE id = ?`).bind(id).first<TenantRow>();
}

export async function slugExists(db: D1Database, slug: string): Promise<boolean> {
  const row = await db
    .prepare(`SELECT 1 AS x FROM tenants WHERE slug = ?`)
    .bind(slug)
    .first<{ x: number }>();
  return row != null;
}

// ---- users ----------------------------------------------------------------

export interface CreateUserInput {
  id: string;
  tenantId: string;
  email: string;
  passwordHash: string;
  firstName: string;
  lastName: string;
  role: string;
  emailVerifyToken: string;
  emailVerifyExpiresAt: string;
}

export async function createUser(
  db: D1Database,
  input: CreateUserInput
): Promise<UserRow> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `INSERT INTO users
         (id, tenant_id, email, password_hash, first_name, last_name, role,
          email_verify_token, email_verify_expires_at, created_at)
       VALUES (?,?,?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.id,
      input.tenantId,
      input.email,
      input.passwordHash,
      input.firstName,
      input.lastName,
      input.role,
      input.emailVerifyToken,
      input.emailVerifyExpiresAt,
      nowIso
    )
    .run();
  const row = await getUserById(db, input.id);
  if (!row) throw new Error("user insert vanished");
  return row;
}

export async function getUserByEmail(
  db: D1Database,
  email: string
): Promise<UserRow | null> {
  return db
    .prepare(`SELECT * FROM users WHERE email = ?`)
    .bind(email)
    .first<UserRow>();
}

export async function getUserById(
  db: D1Database,
  id: string
): Promise<UserRow | null> {
  return db.prepare(`SELECT * FROM users WHERE id = ?`).bind(id).first<UserRow>();
}

export async function getUserByVerifyToken(
  db: D1Database,
  token: string
): Promise<UserRow | null> {
  return db
    .prepare(`SELECT * FROM users WHERE email_verify_token = ?`)
    .bind(token)
    .first<UserRow>();
}

export async function getUserByResetTokenHash(
  db: D1Database,
  tokenHash: string
): Promise<UserRow | null> {
  return db
    .prepare(`SELECT * FROM users WHERE password_reset_token_hash = ?`)
    .bind(tokenHash)
    .first<UserRow>();
}

export async function markEmailVerified(db: D1Database, userId: string): Promise<void> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `UPDATE users
         SET email_verified_at = ?, email_verify_token = NULL,
             email_verify_expires_at = NULL, updated_at = ?
       WHERE id = ?`
    )
    .bind(nowIso, nowIso, userId)
    .run();
}

export async function setVerifyToken(
  db: D1Database,
  userId: string,
  token: string,
  expiresAt: string
): Promise<void> {
  await db
    .prepare(
      `UPDATE users
         SET email_verify_token = ?, email_verify_expires_at = ?, updated_at = ?
       WHERE id = ?`
    )
    .bind(token, expiresAt, new Date().toISOString(), userId)
    .run();
}

export async function setResetToken(
  db: D1Database,
  userId: string,
  tokenHash: string,
  expiresAt: string
): Promise<void> {
  await db
    .prepare(
      `UPDATE users
         SET password_reset_token_hash = ?, password_reset_expires_at = ?, updated_at = ?
       WHERE id = ?`
    )
    .bind(tokenHash, expiresAt, new Date().toISOString(), userId)
    .run();
}

export async function applyPasswordReset(
  db: D1Database,
  userId: string,
  passwordHash: string
): Promise<void> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `UPDATE users
         SET password_hash = ?, password_reset_token_hash = NULL,
             password_reset_expires_at = NULL, updated_at = ?
       WHERE id = ?`
    )
    .bind(passwordHash, nowIso, userId)
    .run();
}

export async function touchLastLogin(db: D1Database, userId: string): Promise<void> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(`UPDATE users SET last_login_at = ?, updated_at = ? WHERE id = ?`)
    .bind(nowIso, nowIso, userId)
    .run();
}

// ---- sessions -------------------------------------------------------------

export interface CreateSessionInput {
  id: string;
  userId: string;
  refreshTokenHash: string;
  userAgent: string | null;
  ip: string | null;
}

export async function createSession(
  db: D1Database,
  input: CreateSessionInput
): Promise<void> {
  const now = new Date();
  const expires = new Date(now.getTime() + REFRESH_TTL_DAYS * 86400_000);
  const nowIso = now.toISOString();
  await db
    .prepare(
      `INSERT INTO sessions
         (id, user_id, refresh_token_hash, user_agent, ip, created_at,
          last_used_at, expires_at)
       VALUES (?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.id,
      input.userId,
      input.refreshTokenHash,
      input.userAgent,
      input.ip,
      nowIso,
      nowIso,
      expires.toISOString()
    )
    .run();
}

export async function getSessionByTokenHash(
  db: D1Database,
  tokenHash: string
): Promise<SessionRow | null> {
  return db
    .prepare(`SELECT * FROM sessions WHERE refresh_token_hash = ?`)
    .bind(tokenHash)
    .first<SessionRow>();
}

// Single-use rotation: revoke the old session and mint a new one atomically.
export async function rotateSession(
  db: D1Database,
  oldSessionId: string,
  next: CreateSessionInput
): Promise<void> {
  const now = new Date();
  const expires = new Date(now.getTime() + REFRESH_TTL_DAYS * 86400_000);
  const nowIso = now.toISOString();
  await db.batch([
    db
      .prepare(`UPDATE sessions SET revoked_at = ? WHERE id = ?`)
      .bind(nowIso, oldSessionId),
    db
      .prepare(
        `INSERT INTO sessions
           (id, user_id, refresh_token_hash, user_agent, ip, created_at,
            last_used_at, expires_at)
         VALUES (?,?,?,?,?,?,?,?)`
      )
      .bind(
        next.id,
        next.userId,
        next.refreshTokenHash,
        next.userAgent,
        next.ip,
        nowIso,
        nowIso,
        expires.toISOString()
      ),
  ]);
}

export async function revokeSessionByTokenHash(
  db: D1Database,
  tokenHash: string
): Promise<void> {
  await db
    .prepare(
      `UPDATE sessions SET revoked_at = ? WHERE refresh_token_hash = ? AND revoked_at IS NULL`
    )
    .bind(new Date().toISOString(), tokenHash)
    .run();
}
