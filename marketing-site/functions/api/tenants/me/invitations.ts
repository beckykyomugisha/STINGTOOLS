// /api/tenants/me/invitations
//   POST — invite a teammate (admin+). Enforces the soft-block-at-cap rule.
//   GET  — list pending invitations (admin+).

import { withHandler, readJson } from "../../auth/_lib/handler";
import { handlePreflight, jsonResponse } from "../../auth/_lib/cors";
import { requireRole, roleLevel, ASSIGNABLE_ROLES } from "../../auth/_lib/auth";
import { bad, conflict, unauthorized } from "../../auth/_lib/errors";
import { normEmail } from "../../auth/_lib/validate";
import { randomToken, sha256Hex, uuid } from "../../auth/_lib/tokens";
import { resolveCap, GRACE_DAYS } from "../../auth/_lib/limits";
import {
  getTenantById,
  getUserById,
  getActiveMemberByEmail,
  getPendingInviteForEmail,
  countActiveMembers,
  countPendingInvites,
  createInvitation,
  listPendingInvitations,
  setCapExceededSince,
  audit,
} from "../../auth/_lib/db";
import { sendInviteEmail } from "../../auth/_lib/email";
import type { InvitationRow } from "../../auth/_lib/types";

const INVITE_TTL_MS = 7 * 24 * 60 * 60 * 1000;

interface Body {
  email?: string;
  role?: string;
}

function publicInvite(i: InvitationRow) {
  return {
    id: i.id,
    email: i.email,
    role: i.role,
    invitedByUserId: i.invited_by_user_id,
    expiresAt: i.expires_at,
    createdAt: i.created_at,
  };
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "admin");
  const rows = await listPendingInvitations(env.WAITLIST_DB, auth.tenantId);
  return { invitations: rows.map(publicInvite) };
});

export const onRequestPost = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "admin");
  const body = await readJson<Body>(request);

  const email = normEmail(body.email);
  const role = (body.role || "").trim().toLowerCase();
  if (!(ASSIGNABLE_ROLES as readonly string[]).includes(role)) {
    throw bad("Invalid role.");
  }
  if (roleLevel(role) > roleLevel(auth.role)) {
    throw bad("You can't invite someone at a role above your own.");
  }

  // Already a member of this tenant? Already invited?
  if (await getActiveMemberByEmail(env.WAITLIST_DB, auth.tenantId, email)) {
    throw conflict("That person is already a member of this team.");
  }
  if (await getPendingInviteForEmail(env.WAITLIST_DB, auth.tenantId, email)) {
    throw conflict("There's already a pending invitation for that email.");
  }

  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  // ---- soft-block-at-cap ---------------------------------------------------
  const members = await countActiveMembers(env.WAITLIST_DB, tenant.id);
  const pending = await countPendingInvites(env.WAITLIST_DB, tenant.id);
  const committed = members + pending;
  const wouldBe = committed + 1; // this invite reserves a seat
  const cap = resolveCap(tenant.plan_product, tenant.plan_tier);
  const now = Date.now();

  let warning: { code: string; upgradeBy: string } | null = null;

  if (wouldBe > cap) {
    // Over cap. Record when we first went over (if not already), then check grace.
    let since = tenant.cap_exceeded_since;
    if (!since) {
      since = new Date(now).toISOString();
      await setCapExceededSince(env.WAITLIST_DB, tenant.id, since);
    }
    const graceEnd = new Date(since).getTime() + GRACE_DAYS * 86400_000;
    if (now >= graceEnd) {
      // Past grace — hard block (do NOT create the invite).
      return jsonResponse(
        request,
        {
          error: "cap_exceeded_grace_ended",
          message: "You've reached your plan's team limit. Upgrade to add more people.",
          upgradeUrl: "/upgrade",
          cap,
          seatsUsed: committed,
        },
        402
      );
    }
    warning = { code: "over_cap", upgradeBy: new Date(graceEnd).toISOString() };
  } else if (tenant.cap_exceeded_since) {
    // Back under cap — clear the marker.
    await setCapExceededSince(env.WAITLIST_DB, tenant.id, null);
  }

  // ---- create + email ------------------------------------------------------
  const token = randomToken(32);
  const tokenHash = await sha256Hex(token);
  const invite = await createInvitation(env.WAITLIST_DB, {
    id: uuid(),
    tenantId: tenant.id,
    email,
    role,
    tokenHash,
    invitedByUserId: auth.userId,
    expiresAt: new Date(now + INVITE_TTL_MS).toISOString(),
  });

  const inviter = await getUserById(env.WAITLIST_DB, auth.userId);
  const inviterName = inviter ? `${inviter.first_name} ${inviter.last_name}` : "A teammate";
  await sendInviteEmail(env, email, inviterName, tenant.name, role, token);

  await audit(env.WAITLIST_DB, {
    tenantId: tenant.id,
    actorUserId: auth.userId,
    action: "user.invited",
    target: invite.id,
    metadata: { email, role, overCap: warning != null },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return jsonResponse(
    request,
    { invitation: publicInvite(invite), ...(warning ? { warning } : {}) },
    200
  );
});
