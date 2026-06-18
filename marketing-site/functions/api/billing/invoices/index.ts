// GET /api/billing/invoices — admin+. Paginated list of the tenant's invoices,
// newest first. Query: ?limit (1..100, default 25) &offset (default 0).

import { withHandler } from "../../auth/_lib/handler";
import { handlePreflight } from "../../auth/_lib/cors";
import { requireRole } from "../../auth/_lib/auth";
import { listInvoices, toPublicInvoice } from "../../_lib/billing/state";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const auth = await requireRole(request, env, "admin");
  const url = new URL(request.url);

  const limit = Math.min(100, Math.max(1, parseInt(url.searchParams.get("limit") || "25", 10) || 25));
  const offset = Math.max(0, parseInt(url.searchParams.get("offset") || "0", 10) || 0);

  const rows = await listInvoices(env.WAITLIST_DB, auth.tenantId, { limit, offset });
  return { invoices: rows.map(toPublicInvoice), limit, offset };
});
