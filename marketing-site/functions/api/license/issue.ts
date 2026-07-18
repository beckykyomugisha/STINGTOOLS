// POST /api/license/issue — mint a signed STING Tools licence for one machine.
//
// The plugin verifies licences entirely OFFLINE: a signed payload bound to a
// machine fingerprint, checked against a public key compiled into the assembly
// (StingTools/Core/Licensing/LicenseVerifier.cs). There is no call-home, so an
// issued licence CANNOT be revoked remotely — it only expires. Two consequences
// drive the design here:
//
//   1. The seat check at issue time is the only enforcement point that exists.
//      Without it, one Solo subscriber could licence unlimited machines.
//   2. Expiry has to be chosen up front. A trial licence dies with the trial; a
//      paid one runs a year, matching the existing hand-issued licences.
//
// Wire format, byte-for-byte compatible with LicenseCrypto.VerifyAndExtract:
//   base64(utf8(payloadJson)) + "." + base64(RSASSA-PKCS1-v1_5(SHA-256, jsonBytes))

import { withHandler, readJson } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth } from "../auth/_lib/auth";
import { bad, forbidden, serverError, unauthorized } from "../auth/_lib/errors";
import { getTenantById, audit } from "../auth/_lib/db";
import { uuid } from "../auth/_lib/tokens";
import { resolveCap } from "../auth/_lib/limits";
import { DOWNLOAD_CATALOG, entitlementFor } from "../_lib/downloads/catalog";
import type { Env } from "../auth/_lib/types";

interface LicenseEnv extends Env {
  // PKCS#8 PEM of the RSA key whose public half is compiled into the plugin.
  LICENSE_PRIVATE_KEY?: string;
}

interface Body {
  machineCode?: string;
}

// Matches the format the plugin shows the user: five 4-hex-char groups.
const MACHINE_CODE = /^[0-9A-F]{4}(-[0-9A-F]{4}){4}$/i;

const PAID_LICENCE_DAYS = 365;
// A little past the trial so a licence issued on the last day still works while
// the customer is deciding.
const TRIAL_GRACE_DAYS = 2;

function pemToBinary(pem: string): ArrayBuffer {
  const body = pem
    .replace(/-----BEGIN [A-Z ]+-----/g, "")
    .replace(/-----END [A-Z ]+-----/g, "")
    .replace(/\s+/g, "");
  const raw = atob(body);
  const buf = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) buf[i] = raw.charCodeAt(i);
  return buf.buffer;
}

function b64(bytes: Uint8Array): string {
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

async function signLicense(pem: string, payloadJson: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "pkcs8",
    pemToBinary(pem),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const data = new TextEncoder().encode(payloadJson);
  const sig = new Uint8Array(
    await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, data)
  );
  return `${b64(data)}.${b64(sig)}`;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  const e = env as LicenseEnv;
  const auth = await requireAuth(request, e);

  if (!e.LICENSE_PRIVATE_KEY) {
    // Fail loudly rather than half-issuing something unsigned.
    console.error("License issue attempted with LICENSE_PRIVATE_KEY unset");
    throw serverError("Licensing is not configured.");
  }

  const body = await readJson<Body>(request);
  const machineCode = (body.machineCode || "").trim().toUpperCase();
  if (!MACHINE_CODE.test(machineCode)) {
    throw bad(
      "That machine code doesn't look right. It appears in the plugin as five groups, like ADD3-E01C-3412-14C8-175E."
    );
  }

  const tenant = await getTenantById(e.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  // Same entitlement gate the downloads use — a locked tenant gets no licence.
  const tool = DOWNLOAD_CATALOG.find((t) => t.id === "sting-tools")!;
  const { entitlement, reason } = entitlementFor(tool, tenant.subscription_status);
  if (entitlement !== "allowed") throw forbidden(reason);

  const now = new Date();
  const db = e.WAITLIST_DB;

  // Already licensed this machine? Re-issue without consuming another seat —
  // reinstalls and lost .lic files are normal, not abuse.
  const existing = await db
    .prepare(
      `SELECT id FROM licenses
        WHERE tenant_id = ? AND machine_code = ? AND revoked_at IS NULL`
    )
    .bind(tenant.id, machineCode)
    .first<{ id: string }>();

  if (!existing) {
    const cap = resolveCap(tenant.plan_product, tenant.plan_tier);
    if (cap !== Infinity) {
      const row = await db
        .prepare(
          `SELECT COUNT(*) AS n FROM licenses
            WHERE tenant_id = ? AND revoked_at IS NULL AND expires_at > ?`
        )
        .bind(tenant.id, now.toISOString())
        .first<{ n: number }>();
      const used = row?.n ?? 0;
      if (used >= cap) {
        throw forbidden(
          `Your plan covers ${cap} machine${cap === 1 ? "" : "s"} and ${used} ${
            used === 1 ? "is" : "are"
          } already licensed. Upgrade your plan, or contact us to move a licence to a different machine.`
        );
      }
    }
  }

  // Trial licences die with the trial; paid ones run a year. The plugin cannot
  // phone home to re-check, so this is a one-shot decision per issue.
  const isTrial = tenant.subscription_status === "trial";
  let expires: Date;
  if (isTrial && tenant.trial_ends_at) {
    expires = new Date(
      new Date(tenant.trial_ends_at).getTime() + TRIAL_GRACE_DAYS * 86400_000
    );
  } else {
    expires = new Date(now.getTime() + PAID_LICENCE_DAYS * 86400_000);
  }

  const licenseId = existing?.id ?? uuid();
  const payload = {
    licenseId: licenseId.replace(/-/g, ""),
    machineCode,
    licensee: tenant.name || "Planscape Licensed User",
    issuedUnix: Math.floor(now.getTime() / 1000),
    expiryUnix: Math.floor(expires.getTime() / 1000),
    schema: 1,
  };

  let licenseText: string;
  try {
    licenseText = await signLicense(e.LICENSE_PRIVATE_KEY, JSON.stringify(payload));
  } catch (err) {
    console.error("License signing failed", err);
    throw serverError("Could not issue a licence. Please contact support.");
  }

  const nowIso = now.toISOString();
  await db
    .prepare(
      `INSERT INTO licenses
         (id, tenant_id, user_id, machine_code, licensee, issued_at, expires_at, created_at, updated_at)
       VALUES (?,?,?,?,?,?,?,?,?)
       ON CONFLICT(tenant_id, machine_code) DO UPDATE SET
         licensee   = excluded.licensee,
         issued_at  = excluded.issued_at,
         expires_at = excluded.expires_at,
         revoked_at = NULL,
         updated_at = excluded.updated_at`
    )
    .bind(
      licenseId,
      tenant.id,
      auth.userId,
      machineCode,
      payload.licensee,
      nowIso,
      expires.toISOString(),
      nowIso,
      nowIso
    )
    .run();

  await audit(db, {
    tenantId: tenant.id,
    actorUserId: auth.userId,
    action: existing ? "license.reissued" : "license.issued",
    target: machineCode,
    metadata: { expiresAt: expires.toISOString(), trial: isTrial },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });

  return {
    license: licenseText,
    machineCode,
    licensee: payload.licensee,
    expiresAt: expires.toISOString(),
    reissued: Boolean(existing),
  };
});
