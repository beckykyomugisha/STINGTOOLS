// Bearer-token auth guard. Pulls the JWT from the Authorization header,
// verifies it, and returns the claims. Throws AuthError(401) on any failure.

import { verifyJwt } from "./jwt";
import { unauthorized } from "./errors";
import type { Env, JwtClaims } from "./types";

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
