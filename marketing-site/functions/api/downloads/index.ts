// GET /api/downloads — the download catalogue, with per-tool entitlement
// resolved against the caller's tenant. Any signed-in member can read it;
// whether they may actually download is what `entitlement` reports.
//
// Requires auth rather than being public: the catalogue tells you what a
// subscription is worth, and the entitlement reasons quote the tenant's own
// billing state back at them.

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth } from "../auth/_lib/auth";
import { unauthorized } from "../auth/_lib/errors";
import { getTenantById } from "../auth/_lib/db";
import { DOWNLOAD_CATALOG, entitlementFor } from "../_lib/downloads/catalog";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireAuth(request, env);
  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  const status = tenant.subscription_status;

  const tools = DOWNLOAD_CATALOG.map((tool) => {
    const { entitlement, reason } = entitlementFor(tool, status);
    return {
      id: tool.id,
      name: tool.name,
      tagline: tool.tagline,
      kind: tool.kind,
      status: tool.status,
      platform: tool.platform ?? null,
      docsUrl: tool.docsUrl ?? null,
      entitlement,
      entitlementReason: reason,
      // Only hand out file URLs to a tenant actually entitled to them. A locked
      // tenant still sees the catalogue — they just don't get the link.
      versions:
        entitlement === "allowed"
          ? tool.versions.map((v) => ({
              version: v.version,
              hosts: v.hosts ?? null,
              sizeMb: v.sizeMb ?? null,
              releasedAt: v.releasedAt ?? null,
              notes: v.notes ?? null,
              url: v.url ?? null,
              sha256: v.sha256 ?? null,
            }))
          : [],
    };
  });

  return {
    tools,
    subscriptionStatus: status,
    trialEndsAt: tenant.trial_ends_at ?? null,
  };
});
