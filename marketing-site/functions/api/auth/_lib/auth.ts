// Bearer-token auth guard. Pulls the JWT from the Authorization header,
// verifies it, and returns the claims. Throws AuthError(401) on any failure.

import { verifyJwt } from "./jwt";
import { unauthorized, forbidden } from "./errors";
import type { Env, JwtClaims } from "./types";

// Role hierarchy. Higher number = more authority.
// owner > admin > bim_manager > project_lead > coordinator > viewer > client
export const ROLE_LEVEL: Record<string, number> = {
  owner: 6,
  admin: 5,
  bim_manager: 4,
  project_lead: 3,
  coordinator: 2,
  viewer: 1,
  client: 0,
};

export type Role = keyof typeof ROLE_LEVEL;

export function roleLevel(role: string): number {
  return ROLE_LEVEL[role] ?? -1;
}

// Roles that may be assigned to an invited / managed member (owner is minted
// only at signup and transferred via a dedicated flow, never granted here).
export const ASSIGNABLE_ROLES = [
  "admin",
  "bim_manager",
  "project_lead",
  "coordinator",
  "viewer",
  "client",
] as const;

export interface AuthContext {
  userId: string;
  tenantId: string;
  role: string;
  emailVerified: boolean;
  subscriptionStatus: string;
  planTier: string | null;
  planProduct: string | null;
}

export async function requireAuth(
  request: Request,
  env: Env
): Promise<AuthContext> {
  const header = request.headers.get("Authorization") || "";
  const match = /^Bearer\s+(.+)$/i.exec(header.trim());
  if (!match) throw unauthorized("Missing or malformed Authorization header");

  const claims = await verifyJwt(match[1], env.JWT_SECRET);
  if (!claims) throw unauthorized("Invalid or expired token");

  return {
    userId: claims.sub,
    tenantId: claims.tid,
    role: claims.role,
    emailVerified: claims.ev === true,
    subscriptionStatus: claims.ps,
    planTier: claims.pt ?? null,
    planProduct: claims.pp ?? null,
  };
}

// Authenticate AND require at least `minRole`. Throws 401 if no/invalid token,
// 403 if the caller's role sits below the threshold.
export async function requireRole(
  request: Request,
  env: Env,
  minRole: Role
): Promise<AuthContext> {
  const auth = await requireAuth(request, env);
  if (roleLevel(auth.role) < roleLevel(minRole)) {
    throw forbidden("You don't have permission to do that.");
  }
  return auth;
}

// Build the signable claim set from a user + their tenant. Subscription status,
// plan tier, and plan product travel in the JWT so downstream services
// (and later phases) can authorize without a DB round-trip.
export function claimsFor(args: {
  userId: string;
  tenantId: string;
  role: string;
  emailVerified: boolean;
  subscriptionStatus: string;
  planTier: string | null;
  planProduct: string | null;
}): Omit<JwtClaims, "iat" | "exp" | "iss"> {
  return {
    sub: args.userId,
    tid: args.tenantId,
    role: args.role,
    ev: args.emailVerified,
    ps: args.subscriptionStatus,
    pt: args.planTier,
    pp: args.planProduct,
  };
}
