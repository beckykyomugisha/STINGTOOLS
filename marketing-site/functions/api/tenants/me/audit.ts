// GET /api/tenants/me/audit — paginated, filterable audit log (admin+).
// Query: ?limit (1..200, default 50) &offset (default 0) &action (exact match)

import { withHandler } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireRole } from "../../auth/_lib/auth";
import { listAudit } from "../../auth/_lib/db";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "admin");
  const url = new URL(request.url);

  const limit = Math.min(200, Math.max(1, parseInt(url.searchParams.get("limit") || "50", 10) || 50));
  const offset = Math.max(0, parseInt(url.searchParams.get("offset") || "0", 10) || 0);
  const action = url.searchParams.get("action");

  const entries = await listAudit(env.WAITLIST_DB, auth.tenantId, { limit, offset, action });
  return { entries, limit, offset };
});
