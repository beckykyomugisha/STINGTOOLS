// GET /api/license — what this tenant has licensed, and how much of its cap
// that uses.
//
// The numbers come from resolveMachineCap + countLicensedSeats, the SAME pair issue.ts
// consults before refusing at cap and present.ts reports back. A second query
// here would be a second definition of "in use", and two definitions in two
// files is exactly how the server-side seat meter drifted (see _lib/seats.ts).
//
// Revoked and expired rows ARE returned, with their dates, so a user can see
// why a seat is or is not being consumed. countLicensedSeats excludes them from
// inUse on its own — this endpoint does not re-implement that rule.
//
// The signed licence text is deliberately absent: issue.ts persists the row but
// never the text, so a .lic exists exactly once, in the response that mints it.
// Recovery is re-issue, which reuses the seat.

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth } from "../auth/_lib/auth";
import { unauthorized } from "../auth/_lib/errors";
import { getTenantById } from "../auth/_lib/db";
import { resolveMachineCap } from "../auth/_lib/limits";
import { countLicensedSeats } from "./_lib/seats";
import type { Env } from "../auth/_lib/types";

interface LicenseRow {
  machine_code: string;
  licensee: string;
  issued_at: string;
  expires_at: string;
  revoked_at: string | null;
  last_seen_at: string | null;
  last_seen_plugin_version: string | null;
  last_seen_revit_version: string | null;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const e = env as Env;
  const auth = await requireAuth(request, e);

  const tenant = await getTenantById(e.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  const res = await e.WAITLIST_DB.prepare(
    `SELECT machine_code, licensee, issued_at, expires_at, revoked_at,
            last_seen_at, last_seen_plugin_version, last_seen_revit_version
       FROM licenses
      WHERE tenant_id = ?
      ORDER BY created_at DESC`
  )
    .bind(auth.tenantId)
    .all<LicenseRow>();

  const cap = resolveMachineCap(tenant.plan_product, tenant.plan_tier);

  return {
    // Infinity is not JSON. null means unlimited — the same convention
    // present.ts uses for licencesIncluded.
    cap: cap === Infinity ? null : cap,
    inUse: await countLicensedSeats(
      e.WAITLIST_DB,
      auth.tenantId,
      new Date().toISOString()
    ),
    licences: (res.results ?? []).map((r) => ({
      machineCode: r.machine_code,
      licensee: r.licensee,
      issuedAt: r.issued_at,
      expiresAt: r.expires_at,
      revokedAt: r.revoked_at,
      lastSeenAt: r.last_seen_at,
      lastSeenPluginVersion: r.last_seen_plugin_version,
      lastSeenRevitVersion: r.last_seen_revit_version,
    })),
  };
});
