// GET /api/billing/invoices/:id/pdf — admin+. 302-redirects to the Stripe-hosted
// PDF URL for one of the tenant's invoices. 404 if the invoice isn't this
// tenant's (no cross-tenant leak) or has no PDF yet.

import { withHandler, pathParam } from "../../../auth/_lib/handler";
import { handlePreflight, corsHeaders } from "../../../auth/_lib/cors";
import { requireRole } from "../../../auth/_lib/auth";
import { notFound } from "../../../auth/_lib/errors";
import { getInvoiceById } from "../../../_lib/billing/state";

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env, params }) => {
  const auth = await requireRole(request, env, "admin");
  const id = pathParam(params, "id");

  const invoice = await getInvoiceById(env.WAITLIST_DB, auth.tenantId, id);
  if (!invoice) throw notFound("Invoice not found.");
  if (!invoice.pdf_url) throw notFound("No PDF available for this invoice yet.");

  return new Response(null, {
    status: 302,
    headers: { Location: invoice.pdf_url, ...corsHeaders(request) },
  });
});
