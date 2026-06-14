// /api/tenants/me — GET tenant detail (+ member count + cap headroom) and
// PATCH tenant settings (owner only).

import { withHandler, readJson } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth, requireRole } from "../auth/_lib/auth";
import { unauthorized, bad } from "../auth/_lib/errors";
import {
  getTenantById,
  countActiveMembers,
  countPendingInvites,
  updateTenantSettings,
  toPublicTenant,
  audit,
} from "../auth/_lib/db";
import { resolveCap, evaluateCap } from "../auth/_lib/limits";
import { clip, normCountry } from "../auth/_lib/validate";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireAuth(request, env);
  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  const members = await countActiveMembers(env.WAITLIST_DB, tenant.id);
  const pending = await countPendingInvites(env.WAITLIST_DB, tenant.id);
  const cap = resolveCap(tenant.plan_product, tenant.plan_tier);
  const capState = evaluateCap(members + pending, cap, tenant.cap_exceeded_since, Date.now());

  return {
    tenant: toPublicTenant(tenant),
    memberCount: members,
    pendingInvites: pending,
    cap: capState.cap === Infinity ? null : capState.cap,
    seatsUsed: capState.count,
    capExceeded: !capState.within,
    capHeadroom: capState.cap === Infinity ? null : Math.max(0, capState.cap - capState.count),
    gracePeriodEndsAt: capState.gracePeriodEndsAt,
    capGraceEnded: capState.graceEnded,
  };
});

interface PatchBody {
  name?: string;
  country?: string;
  currency?: string;
}

const ALLOWED_CURRENCIES = new Set([
  "USD", "EUR", "GBP", "UGX", "KES", "TZS", "RWF", "NGN", "ZAR",
]);

export const onRequestPatch = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "owner");
  const body = await readJson<PatchBody>(request);

  const fields: { name?: string; country?: string | null; currency?: string } = {};
  if (body.name !== undefined) {
    const name = clip(body.name, 160);
    if (!name) throw bad("Tenant name can't be empty.");
    fields.name = name;
  }
  if (body.country !== undefined) {
    fields.country = normCountry(body.country); // unknown → null
  }
  if (body.currency !== undefined) {
    const cur = clip(body.currency, 3).toUpperCase();
    if (!ALLOWED_CURRENCIES.has(cur)) throw bad("Unsupported currency.");
    fields.currency = cur;
  }
  if (Object.keys(fields).length === 0) throw bad("Nothing to update.");

  const tenant = await updateTenantSettings(env.WAITLIST_DB, auth.tenantId, fields);
  await audit(env.WAITLIST_DB, {
    tenantId: auth.tenantId,
    actorUserId: auth.userId,
    action: "tenant.updated",
    target: auth.tenantId,
    metadata: fields,
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return { tenant: toPublicTenant(tenant) };
});
