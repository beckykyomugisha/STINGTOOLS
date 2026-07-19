// POST /api/cloud/handoff — mint a short-lived single-use ticket that lets a
// signed-in planscape.build customer into the Planscape cloud app without a
// second password. Design: docs/PLANSCAPE_IDENTITY_HANDOFF.md.
//
// D1 stays the only place a password lives. The cloud app exchanges this
// ticket at POST /api/auth/handoff/exchange (.NET API) for a normal session;
// the exchange find-or-creates the mirror Tenant/AppUser there.
//
// Wire format (mirrors the licence format so the codebase has one convention):
//   base64url(utf8(payloadJson)) + "." + base64url(hmacSha256(payloadBytes))
//
// TTL is 120 seconds and the jti is single-use at the exchange side. The
// ticket travels in a URL — browser history, referrer headers, proxy logs —
// which is acceptable ONLY because it is dead within two minutes and spent
// after one use.

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth } from "../auth/_lib/auth";
import { forbidden, serverError, unauthorized } from "../auth/_lib/errors";
import { getTenantById, getUserById, audit } from "../auth/_lib/db";
import { uuid } from "../auth/_lib/tokens";
import type { Env } from "../auth/_lib/types";

interface HandoffEnv extends Env {
  // Shared HMAC secret with the .NET API (PLANSCAPE_HANDOFF_SECRET there too).
  // 32+ random bytes; independent of either side's JWT key.
  PLANSCAPE_HANDOFF_SECRET?: string;
  // Where the cloud app lives. Defaults to production.
  CLOUD_APP_ORIGIN?: string;
}

const TICKET_TTL_SECONDS = 120;

function b64url(bytes: Uint8Array): string {
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const e = env as HandoffEnv;
  const auth = await requireAuth(request, e);

  if (!e.PLANSCAPE_HANDOFF_SECRET) {
    console.error("Handoff requested but PLANSCAPE_HANDOFF_SECRET is unset");
    throw serverError("Planscape cloud is not configured yet.");
  }

  const tenant = await getTenantById(e.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");
  const user = await getUserById(e.WAITLIST_DB, auth.userId);
  if (!user) throw unauthorized("Account no longer exists.");

  // Same entitlement gate as downloads and licensing: a lapsed tenant does not
  // get into the cloud app. This is the practical enforcement point — there is
  // deliberately no reverse sync deleting mirror accounts over there.
  if (
    tenant.subscription_status === "read_only" ||
    tenant.subscription_status === "cancelled"
  ) {
    throw forbidden("Your access has ended. Choose a plan to use Planscape cloud.");
  }

  const now = Math.floor(Date.now() / 1000);
  const payload = {
    jti: uuid(),
    email: user.email,
    tenantSlug: tenant.slug,
    tenantName: tenant.name,
    firstName: user.first_name,
    lastName: user.last_name,
    role: user.role,
    tier: tenant.plan_tier,
    iat: now,
    exp: now + TICKET_TTL_SECONDS,
  };

  const data = new TextEncoder().encode(JSON.stringify(payload));
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(e.PLANSCAPE_HANDOFF_SECRET),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, data));
  const ticket = `${b64url(data)}.${b64url(sig)}`;

  await audit(e.WAITLIST_DB, {
    tenantId: tenant.id,
    actorUserId: auth.userId,
    action: "cloud.handoff",
    target: tenant.slug,
    metadata: { jti: payload.jti },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  const origin = (e.CLOUD_APP_ORIGIN || "https://app.planscape.build").replace(/\/+$/, "");
  return {
    ticket,
    expiresInSeconds: TICKET_TTL_SECONDS,
    redirectUrl: `${origin}/handoff?ticket=${encodeURIComponent(ticket)}`,
  };
});
