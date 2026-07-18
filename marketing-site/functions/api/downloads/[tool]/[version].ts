// GET /api/downloads/:tool/:version — stream a build from R2, but only to a
// tenant entitled to it.
//
// The bucket is private and has no public URL. Every byte goes through this
// Function, which re-checks entitlement on each request. That is the point: a
// direct link cannot be forwarded to someone who has not paid, and revoking a
// subscription revokes downloads immediately rather than at the next cache
// expiry.
//
// The object key comes from the catalogue, never from the URL, so a caller
// cannot walk the bucket by editing the path.

import { handlePreflight } from "../../auth/_lib/cors";
import { requireAuth } from "../../auth/_lib/auth";
import { getTenantById, getSessionByTokenHash, getUserById } from "../../auth/_lib/db";
import { readRefreshCookie } from "../../auth/_lib/session";
import { sha256Hex } from "../../auth/_lib/tokens";
import { DOWNLOAD_CATALOG, entitlementFor } from "../../_lib/downloads/catalog";
import type { Env } from "../../auth/_lib/types";

interface DownloadsEnv extends Env {
  DOWNLOADS: R2Bucket;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

function deny(status: number, message: string): Response {
  return new Response(JSON.stringify({ error: message }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

export const onRequestGet: PagesFunction<DownloadsEnv> = async ({
  request,
  env,
  params,
}) => {
  // A download is triggered by a plain browser navigation (<a href>), which
  // sends NO Authorization header — the access token lives in memory in the
  // page, not in a cookie. So Bearer auth alone always failed here with "Sign
  // in to download" for a user who was very much signed in.
  //
  // Fall back to the HttpOnly ps_refresh cookie, which the browser DOES send on
  // a same-origin navigation (SameSite=Strict). Bearer is still accepted first
  // so scripted clients keep working.
  let auth: { userId: string; tenantId: string } | null = null;
  try {
    auth = await requireAuth(request, env);
  } catch {
    const presented = readRefreshCookie(request);
    if (presented) {
      const session = await getSessionByTokenHash(env.WAITLIST_DB, await sha256Hex(presented));
      if (session && new Date(session.expires_at).getTime() > Date.now() && !session.revoked_at) {
        const user = await getUserById(env.WAITLIST_DB, session.user_id);
        if (user) auth = { userId: user.id, tenantId: user.tenant_id };
      }
    }
  }
  if (!auth) return deny(401, "Sign in to download.");

  const tenant = await getTenantById(env.WAITLIST_DB, auth.tenantId);
  if (!tenant) return deny(401, "Account no longer exists.");

  const toolId = String(params.tool);
  const versionId = String(params.version);

  const tool = DOWNLOAD_CATALOG.find((t) => t.id === toolId);
  if (!tool) return deny(404, "Unknown download.");

  const { entitlement, reason } = entitlementFor(tool, tenant.subscription_status);
  if (entitlement !== "allowed") return deny(403, reason);

  const version = tool.versions.find((v) => v.version === versionId);
  // objectKey is what makes path traversal a non-issue: the key is whatever the
  // catalogue says, and the URL only ever selects a catalogue entry.
  if (!version || !version.objectKey) return deny(404, "That version is not available.");

  const object = await env.DOWNLOADS.get(version.objectKey);
  if (!object) {
    console.error(
      `R2 object missing for ${toolId}/${versionId}: ${version.objectKey}`
    );
    return deny(404, "That file is temporarily unavailable. Please contact support.");
  }

  const filename = version.objectKey.split("/").pop() || `${toolId}.zip`;
  const headers = new Headers();
  object.writeHttpMetadata(headers);
  headers.set("Content-Type", "application/zip");
  headers.set("Content-Disposition", `attachment; filename="${filename}"`);
  headers.set("etag", object.httpEtag);
  // Never cache at the edge: caching would serve the file to a later request
  // without re-running the entitlement check above.
  headers.set("Cache-Control", "private, no-store");

  return new Response(object.body, { headers });
};
