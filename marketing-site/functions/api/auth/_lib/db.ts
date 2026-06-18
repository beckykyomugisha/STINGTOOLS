// Typed D1 wrappers for the auth core. All queries are parameterised.
// Functions throw the raw D1 error to the caller, which maps it to a generic
// 500 — callers never surface DB internals to the client.

import type {
  TenantRow,
  UserRow,
  SessionRow,
  InvitationRow,
  PublicUser,
  PublicTenant,
} from "./types";

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

// Single-use rotation: revoke the old session (reason 'rotated') and mint a new
// one atomically. The 'rotated' reason is what replay-detection keys off — a
// presented token whose session is already revoked-as-rotated means reuse.
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
      .prepare(
        `UPDATE sessions SET revoked_at = ?, revoked_reason = 'rotated' WHERE id = ?`
      )
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
  tokenHash: string,
  reason = "manual"
): Promise<void> {
  await db
    .prepare(
      `UPDATE sessions SET revoked_at = ?, revoked_reason = ?
       WHERE refresh_token_hash = ? AND revoked_at IS NULL`
    )
    .bind(new Date().toISOString(), reason, tokenHash)
    .run();
}

// Revoke every still-live session for a user. Used by replay detection
// (reason 'replay') and password reset (reason 'password_reset').
export async function revokeAllUserSessions(
  db: D1Database,
  userId: string,
  reason: string
): Promise<void> {
  await db
    .prepare(
      `UPDATE sessions SET revoked_at = ?, revoked_reason = ?
       WHERE user_id = ? AND revoked_at IS NULL`
    )
    .bind(new Date().toISOString(), reason, userId)
    .run();
}

// ---- B2: members ----------------------------------------------------------

export interface MemberView {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  emailVerified: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export function toMemberView(u: UserRow): MemberView {
  return {
    id: u.id,
    email: u.email,
    firstName: u.first_name,
    lastName: u.last_name,
    role: u.role,
    emailVerified: u.email_verified_at != null,
    lastLoginAt: u.last_login_at,
    createdAt: u.created_at,
  };
}

export async function countActiveMembers(
  db: D1Database,
  tenantId: string
): Promise<number> {
  const row = await db
    .prepare(
      `SELECT COUNT(*) AS n FROM users WHERE tenant_id = ? AND deleted_at IS NULL`
    )
    .bind(tenantId)
    .first<{ n: number }>();
  return row?.n ?? 0;
}

export async function countPendingInvites(
  db: D1Database,
  tenantId: string
): Promise<number> {
  const row = await db
    .prepare(
      `SELECT COUNT(*) AS n FROM invitations
         WHERE tenant_id = ? AND accepted_at IS NULL AND declined_at IS NULL
           AND expires_at > ?`
    )
    .bind(tenantId, new Date().toISOString())
    .first<{ n: number }>();
  return row?.n ?? 0;
}

export async function listMembers(
  db: D1Database,
  tenantId: string
): Promise<UserRow[]> {
  const res = await db
    .prepare(
      `SELECT * FROM users WHERE tenant_id = ? AND deleted_at IS NULL
       ORDER BY created_at ASC`
    )
    .bind(tenantId)
    .all<UserRow>();
  return res.results ?? [];
}

export async function getActiveMemberById(
  db: D1Database,
  tenantId: string,
  userId: string
): Promise<UserRow | null> {
  return db
    .prepare(
      `SELECT * FROM users WHERE id = ? AND tenant_id = ? AND deleted_at IS NULL`
    )
    .bind(userId, tenantId)
    .first<UserRow>();
}

export async function getActiveMemberByEmail(
  db: D1Database,
  tenantId: string,
  email: string
): Promise<UserRow | null> {
  return db
    .prepare(
      `SELECT * FROM users WHERE email = ? AND tenant_id = ? AND deleted_at IS NULL`
    )
    .bind(email, tenantId)
    .first<UserRow>();
}

export async function countTenantOwners(
  db: D1Database,
  tenantId: string
): Promise<number> {
  const row = await db
    .prepare(
      `SELECT COUNT(*) AS n FROM users
         WHERE tenant_id = ? AND role = 'owner' AND deleted_at IS NULL`
    )
    .bind(tenantId)
    .first<{ n: number }>();
  return row?.n ?? 0;
}

export async function updateUserRole(
  db: D1Database,
  userId: string,
  role: string
): Promise<void> {
  await db
    .prepare(`UPDATE users SET role = ?, updated_at = ? WHERE id = ?`)
    .bind(role, new Date().toISOString(), userId)
    .run();
}

export async function softDeleteUser(db: D1Database, userId: string): Promise<void> {
  const nowIso = new Date().toISOString();
  // Tombstone the user and kill their live sessions in one batch.
  await db.batch([
    db
      .prepare(`UPDATE users SET deleted_at = ?, updated_at = ? WHERE id = ?`)
      .bind(nowIso, nowIso, userId),
    db
      .prepare(
        `UPDATE sessions SET revoked_at = ?, revoked_reason = 'user_removed'
         WHERE user_id = ? AND revoked_at IS NULL`
      )
      .bind(nowIso, userId),
  ]);
}

// ---- B2: tenant settings + cap -------------------------------------------

export async function updateTenantSettings(
  db: D1Database,
  tenantId: string,
  fields: { name?: string; country?: string | null; currency?: string }
): Promise<TenantRow> {
  const sets: string[] = [];
  const binds: (string | null)[] = [];
  if (fields.name !== undefined) {
    sets.push("name = ?");
    binds.push(fields.name);
  }
  if (fields.country !== undefined) {
    sets.push("country = ?");
    binds.push(fields.country);
  }
  if (fields.currency !== undefined) {
    sets.push("currency = ?");
    binds.push(fields.currency);
  }
  sets.push("updated_at = ?");
  binds.push(new Date().toISOString());
  binds.push(tenantId);
  await db
    .prepare(`UPDATE tenants SET ${sets.join(", ")} WHERE id = ?`)
    .bind(...binds)
    .run();
  const row = await getTenantById(db, tenantId);
  if (!row) throw new Error("tenant vanished");
  return row;
}

// Set or clear cap_exceeded_since. Pass null to clear (tenant back under cap).
export async function setCapExceededSince(
  db: D1Database,
  tenantId: string,
  iso: string | null
): Promise<void> {
  await db
    .prepare(
      `UPDATE tenants SET cap_exceeded_since = ?, updated_at = ? WHERE id = ?`
    )
    .bind(iso, new Date().toISOString(), tenantId)
    .run();
}

// ---- B2: invitations ------------------------------------------------------

export interface CreateInvitationInput {
  id: string;
  tenantId: string;
  email: string;
  role: string;
  tokenHash: string;
  invitedByUserId: string;
  expiresAt: string;
}

export async function createInvitation(
  db: D1Database,
  input: CreateInvitationInput
): Promise<InvitationRow> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `INSERT INTO invitations
         (id, tenant_id, email, role, token_hash, invited_by_user_id,
          expires_at, created_at)
       VALUES (?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.id,
      input.tenantId,
      input.email,
      input.role,
      input.tokenHash,
      input.invitedByUserId,
      input.expiresAt,
      nowIso
    )
    .run();
  const row = await db
    .prepare(`SELECT * FROM invitations WHERE id = ?`)
    .bind(input.id)
    .first<InvitationRow>();
  if (!row) throw new Error("invitation insert vanished");
  return row;
}

export async function getInvitationByTokenHash(
  db: D1Database,
  tokenHash: string
): Promise<InvitationRow | null> {
  return db
    .prepare(`SELECT * FROM invitations WHERE token_hash = ?`)
    .bind(tokenHash)
    .first<InvitationRow>();
}

export async function getInvitationById(
  db: D1Database,
  tenantId: string,
  id: string
): Promise<InvitationRow | null> {
  return db
    .prepare(`SELECT * FROM invitations WHERE id = ? AND tenant_id = ?`)
    .bind(id, tenantId)
    .first<InvitationRow>();
}

// An open invite for this email in this tenant (not accepted/declined/expired).
export async function getPendingInviteForEmail(
  db: D1Database,
  tenantId: string,
  email: string
): Promise<InvitationRow | null> {
  return db
    .prepare(
      `SELECT * FROM invitations
         WHERE tenant_id = ? AND email = ? AND accepted_at IS NULL
           AND declined_at IS NULL AND expires_at > ?`
    )
    .bind(tenantId, email, new Date().toISOString())
    .first<InvitationRow>();
}

export async function listPendingInvitations(
  db: D1Database,
  tenantId: string
): Promise<InvitationRow[]> {
  const res = await db
    .prepare(
      `SELECT * FROM invitations
         WHERE tenant_id = ? AND accepted_at IS NULL AND declined_at IS NULL
           AND expires_at > ?
       ORDER BY created_at DESC`
    )
    .bind(tenantId, new Date().toISOString())
    .all<InvitationRow>();
  return res.results ?? [];
}

export async function deleteInvitation(db: D1Database, id: string): Promise<void> {
  await db.prepare(`DELETE FROM invitations WHERE id = ?`).bind(id).run();
}

export async function markInvitationAccepted(
  db: D1Database,
  id: string
): Promise<void> {
  await db
    .prepare(`UPDATE invitations SET accepted_at = ? WHERE id = ?`)
    .bind(new Date().toISOString(), id)
    .run();
}

export async function markInvitationDeclined(
  db: D1Database,
  id: string
): Promise<void> {
  await db
    .prepare(`UPDATE invitations SET declined_at = ? WHERE id = ?`)
    .bind(new Date().toISOString(), id)
    .run();
}

// Create a user who is joining via an accepted invite: role from the invite,
// email already proven (verified) by clicking the link, no verify token.
export interface CreateInvitedUserInput {
  id: string;
  tenantId: string;
  email: string;
  passwordHash: string;
  firstName: string;
  lastName: string;
  role: string;
}

export async function createInvitedUser(
  db: D1Database,
  input: CreateInvitedUserInput
): Promise<UserRow> {
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `INSERT INTO users
         (id, tenant_id, email, password_hash, first_name, last_name, role,
          email_verified_at, last_login_at, created_at)
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
      nowIso,
      nowIso,
      nowIso
    )
    .run();
  const row = await getUserById(db, input.id);
  if (!row) throw new Error("invited user insert vanished");
  return row;
}

// ---- B2: audit log --------------------------------------------------------

export interface AuditInput {
  tenantId: string | null;
  actorUserId: string | null;
  action: string;
  target?: string | null;
  metadata?: unknown;
  ip?: string | null;
  userAgent?: string | null;
}

export async function audit(db: D1Database, input: AuditInput): Promise<void> {
  await db
    .prepare(
      `INSERT INTO audit_log
         (tenant_id, actor_user_id, action, target, metadata, ip, user_agent, created_at)
       VALUES (?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.tenantId,
      input.actorUserId,
      input.action,
      input.target ?? null,
      input.metadata != null ? JSON.stringify(input.metadata) : null,
      input.ip ?? null,
      input.userAgent ?? null,
      new Date().toISOString()
    )
    .run();
}

export interface AuditRowView {
  id: number;
  action: string;
  target: string | null;
  metadata: unknown;
  actorUserId: string | null;
  ip: string | null;
  createdAt: string;
}

export async function listAudit(
  db: D1Database,
  tenantId: string,
  opts: { limit: number; offset: number; action?: string | null }
): Promise<AuditRowView[]> {
  const where = ["tenant_id = ?"];
  const binds: (string | number)[] = [tenantId];
  if (opts.action) {
    where.push("action = ?");
    binds.push(opts.action);
  }
  binds.push(opts.limit, opts.offset);
  const res = await db
    .prepare(
      `SELECT id, action, target, metadata, actor_user_id, ip, created_at
         FROM audit_log WHERE ${where.join(" AND ")}
       ORDER BY id DESC LIMIT ? OFFSET ?`
    )
    .bind(...binds)
    .all<{
      id: number;
      action: string;
      target: string | null;
      metadata: string | null;
      actor_user_id: string | null;
      ip: string | null;
      created_at: string;
    }>();
  return (res.results ?? []).map((r) => ({
    id: r.id,
    action: r.action,
    target: r.target,
    metadata: r.metadata ? safeJson(r.metadata) : null,
    actorUserId: r.actor_user_id,
    ip: r.ip,
    createdAt: r.created_at,
  }));
}

function safeJson(s: string): unknown {
  try {
    return JSON.parse(s);
  } catch {
    return s;
  }
}

// Lazy trial expiry: if the tenant is still 'trial' but the trial window has
// closed, flip it to 'read_only' and return the updated row. Called on /me.
export async function expireTrialIfNeeded(
  db: D1Database,
  tenant: TenantRow
): Promise<TenantRow> {
  if (
    tenant.subscription_status !== "trial" ||
    new Date(tenant.trial_ends_at).getTime() > Date.now()
  ) {
    return tenant;
  }
  const nowIso = new Date().toISOString();
  await db
    .prepare(
      `UPDATE tenants SET subscription_status = 'read_only', updated_at = ?
       WHERE id = ? AND subscription_status = 'trial'`
    )
    .bind(nowIso, tenant.id)
    .run();
  return { ...tenant, subscription_status: "read_only", updated_at: nowIso };
}
