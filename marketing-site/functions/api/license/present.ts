// POST /api/license/present — a running plugin tells us which licence it holds.
//
// REPORTING ONLY. Nothing here gates anything, and nothing the plugin does with
// the response may gate anything either. A STING licence is verified entirely
// offline against a public key compiled into the assembly; it must keep working
// when this endpoint is unreachable, refuses, or is mid-deploy. Presentation
// answers questions we currently cannot answer at all — which of the machines
// we licensed are actually running, on which Revit, on which plugin build, and
// whether the .lic file in the field still matches what we issued.
//
// AUTHENTICATION IS THE LICENCE ITSELF. The caller presents the signed licence
// text; we verify the signature with the same key that minted it. There is
// deliberately no Bearer token:
//
//   * The licence already binds to a tenant. issue.ts wrote tenant_id and
//     user_id against this machine_code, so a validly-signed licence names its
//     own tenant. No user identity has to travel with it.
//   * Most licensed machines have no Planscape login at all. STING Tools sells
//     standalone, and requiring an account would mean the number only ever
//     moved for the subset of customers who also use the coordination server —
//     which is precisely the blind spot this endpoint exists to remove.
//
// PRESENTATION NEVER INSERTS. A licence we have no record of is reported as
// unknown and dropped. If presentation could create rows it would manufacture
// seats, and the seat count would stop meaning "licences we issued".

import { withHandler, readJson } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { bad, notFound, serverError, unauthorized } from "../auth/_lib/errors";
import { getTenantById, audit } from "../auth/_lib/db";
import { resolveCap } from "../auth/_lib/limits";
import { verifyLicense } from "./_lib/crypto";
import { countLicensedSeats } from "./_lib/seats";
import type { Env } from "../auth/_lib/types";

interface LicenseEnv extends Env {
  LICENSE_PRIVATE_KEY?: string;
}

interface Body {
  license?: string;
  pluginVersion?: string;
  revitVersion?: string;
}

interface LicenseRow {
  id: string;
  tenant_id: string;
  licensee: string;
  expires_at: string;
  revoked_at: string | null;
  last_seen_at: string | null;
}

// Keep free-text client-supplied fields short and boring — they are display
// values, and they land in a column we later group by.
function clean(value: string | undefined, max = 40): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim().slice(0, max);
  return trimmed.length > 0 ? trimmed : null;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const e = env as LicenseEnv;

  if (!e.LICENSE_PRIVATE_KEY) {
    console.error("License presentation attempted with LICENSE_PRIVATE_KEY unset");
    throw serverError("Licensing is not configured.");
  }

  const body = await readJson<Body>(request);
  const licenseText = (body.license || "").trim();
  if (!licenseText) throw bad("No licence supplied.");

  const payload = await verifyLicense(e.LICENSE_PRIVATE_KEY, licenseText);
  if (!payload) throw unauthorized("That licence could not be verified.");
  // Forward compatibility: a future schema is not something this version can
  // interpret, and guessing at it would record misleading data.
  if (payload.schema !== 1) {
    throw bad(`Unsupported licence schema ${payload.schema}.`);
  }

  const db = e.WAITLIST_DB;

  // Match on BOTH machine code and licence id. machine_code alone is unique
  // only per tenant (idx_licenses_tenant_machine), so the same workstation
  // licensed by two tenants would otherwise be ambiguous. The payload carries
  // the row id with its dashes stripped (issue.ts), hence the REPLACE.
  //
  // Revoked and expired rows are deliberately still matched: a revoked licence
  // that is still running is exactly the thing worth knowing about, and hiding
  // it would make presentation useless for the case it best serves.
  const row = await db
    .prepare(
      `SELECT id, tenant_id, licensee, expires_at, revoked_at, last_seen_at
         FROM licenses
        WHERE machine_code = ? AND REPLACE(id, '-', '') = ?`
    )
    .bind(payload.machineCode, payload.licenseId)
    .first<LicenseRow>();

  if (!row) {
    // Validly signed but unrecorded — a licence hand-issued before this table
    // existed, or against a database that has since been restored. Worth a log
    // line; not worth inventing a seat for. audit() needs a tenant_id we do
    // not have, so console is the only honest place for this.
    console.warn(
      `Unrecorded licence presented: machine=${payload.machineCode} id=${payload.licenseId}`
    );
    throw notFound("We have no record of that licence.");
  }

  const now = new Date();
  const nowIso = now.toISOString();
  const firstPresentation = row.last_seen_at == null;

  // updated_at is deliberately NOT touched. It means "when the licence record
  // last changed"; being observed is not a change to the licence.
  await db
    .prepare(
      `UPDATE licenses
          SET last_seen_at = ?,
              last_seen_plugin_version = ?,
              last_seen_revit_version = ?
        WHERE id = ?`
    )
    .bind(
      nowIso,
      clean(body.pluginVersion),
      clean(body.revitVersion),
      row.id
    )
    .run();

  // Does the .lic file on that machine still say what our record says? Compared
  // at second granularity because the payload stores unix seconds while the row
  // stores an ISO string with milliseconds.
  const matchesRecord =
    Math.floor(Date.parse(row.expires_at) / 1000) === payload.expiryUnix;

  const revoked = row.revoked_at != null;
  const expired = Date.parse(row.expires_at) <= now.getTime();

  // Routine heartbeats do not belong in audit_log — every licensed machine
  // would write a row a day. Audit only the things a human would want to find
  // later: the first sighting of a machine, a licence still running after
  // revocation, and a file that disagrees with our record.
  if (firstPresentation || revoked || !matchesRecord) {
    await audit(db, {
      tenantId: row.tenant_id,
      actorUserId: null,
      action: firstPresentation ? "license.first_seen" : "license.presented",
      target: payload.machineCode,
      metadata: {
        pluginVersion: clean(body.pluginVersion),
        revitVersion: clean(body.revitVersion),
        revoked,
        matchesRecord,
      },
      ip: request.headers.get("CF-Connecting-IP"),
      userAgent: request.headers.get("User-Agent"),
    });
  }

  const tenant = await getTenantById(db, row.tenant_id);
  const cap = resolveCap(tenant?.plan_product ?? null, tenant?.plan_tier ?? null);
  const inUse = await countLicensedSeats(db, row.tenant_id, nowIso);

  // Everything below is information, not instruction. The plugin logs it and
  // carries on regardless of what it says.
  return {
    recorded: true,
    licensee: row.licensee,
    machineCode: payload.machineCode,
    expiresAt: row.expires_at,
    revoked,
    expired,
    matchesRecord,
    licencesIncluded: cap === Infinity ? null : cap,
    licencesInUse: inUse,
  };
});
