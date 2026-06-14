// Issue / rotate access + refresh tokens and persist the session row.
// Centralises the JWT + opaque-refresh dance used by signup, login, refresh.

import { signJwt } from "./jwt";
import { randomToken, sha256Hex, uuid } from "./tokens";
import { claimsFor } from "./auth";
import { createSession, rotateSession } from "./db";
import type { Env, UserRow } from "./types";

const REFRESH_COOKIE = "ps_refresh";
const REFRESH_TTL_SECONDS = 30 * 24 * 60 * 60;

export interface IssuedTokens {
  token: string; // JWT access token
  refreshToken: string; // opaque, returned to client once
}

// Mint a fresh access token for a user without touching sessions.
export async function mintAccessToken(env: Env, user: UserRow): Promise<string> {
  return signJwt(
    claimsFor(user.id, user.tenant_id, user.role, user.email_verified_at != null),
    env.JWT_SECRET
  );
}

// New login/signup: create a brand-new session and return both tokens.
export async function issueTokens(
  env: Env,
  user: UserRow,
  request: Request
): Promise<IssuedTokens> {
  const token = await mintAccessToken(env, user);
  const refreshToken = randomToken(32);
  const refreshTokenHash = await sha256Hex(refreshToken);
  await createSession(env.WAITLIST_DB, {
    id: uuid(),
    userId: user.id,
    refreshTokenHash,
    userAgent: request.headers.get("User-Agent"),
    ip: request.headers.get("CF-Connecting-IP"),
  });
  return { token, refreshToken };
}

// Refresh: single-use rotation. Revokes oldSessionId, mints a new session.
export async function rotateTokens(
  env: Env,
  user: UserRow,
  oldSessionId: string,
  request: Request
): Promise<IssuedTokens> {
  const token = await mintAccessToken(env, user);
  const refreshToken = randomToken(32);
  const refreshTokenHash = await sha256Hex(refreshToken);
  await rotateSession(env.WAITLIST_DB, oldSessionId, {
    id: uuid(),
    userId: user.id,
    refreshTokenHash,
    userAgent: request.headers.get("User-Agent"),
    ip: request.headers.get("CF-Connecting-IP"),
  });
  return { token, refreshToken };
}

// ---- refresh cookie helpers ----------------------------------------------

export function refreshCookie(token: string): string {
  return [
    `${REFRESH_COOKIE}=${token}`,
    "HttpOnly",
    "Secure",
    "SameSite=Strict",
    "Path=/api/auth",
    `Max-Age=${REFRESH_TTL_SECONDS}`,
  ].join("; ");
}

export function clearRefreshCookie(): string {
  return [
    `${REFRESH_COOKIE}=`,
    "HttpOnly",
    "Secure",
    "SameSite=Strict",
    "Path=/api/auth",
    "Max-Age=0",
  ].join("; ");
}

export function readRefreshCookie(request: Request): string | null {
  const cookie = request.headers.get("Cookie") || "";
  for (const part of cookie.split(";")) {
    const [name, ...rest] = part.trim().split("=");
    if (name === REFRESH_COOKIE) return rest.join("=") || null;
  }
  return null;
}
