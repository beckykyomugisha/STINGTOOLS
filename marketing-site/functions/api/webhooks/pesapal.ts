// Pesapal IPN (Instant Payment Notification). Pesapal calls this with only an
// OrderTrackingId — never the payment status — so we re-fetch the authoritative
// status with GetTransactionStatus (Pesapal's documented security model; there
// is no inbound HMAC). Supports GET and POST registration types. Always replies
// with the JSON acknowledgement Pesapal expects:
//   { orderNotificationType, orderTrackingId, orderMerchantReference, status }
// where status is 200 (handled) or 500 (retry me).

import {
  getAccessToken,
  getTransactionStatus,
  normalizeStatus,
} from "../_lib/billing/pesapal";
import { resolveTax } from "../_lib/billing/tax";
import { recordRedemption } from "../_lib/billing/discounts";
import {
  getWebhookByEventId,
  insertWebhookLog,
  updateWebhookStatus,
  getPesapalOrderByTracking,
  setPesapalOrderStatus,
  upsertSubscription,
  getSubscriptionByProviderId,
  upsertInvoice,
  setTenantPlan,
  setTenantSubscriptionStatus,
} from "../_lib/billing/state";
import { getTenantById, audit } from "../auth/_lib/db";
import type { Env } from "../auth/_lib/types";

interface IpnParams {
  orderTrackingId: string | null;
  merchantReference: string | null;
  notificationType: string;
}

function ack(p: IpnParams, statusCode: 200 | 500): Response {
  return new Response(
    JSON.stringify({
      orderNotificationType: p.notificationType,
      orderTrackingId: p.orderTrackingId,
      orderMerchantReference: p.merchantReference,
      status: statusCode,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } }
  );
}

// Pull the IPN fields from either the query string (GET) or a JSON body (POST).
async function readParams(request: Request): Promise<IpnParams> {
  const url = new URL(request.url);
  const q = url.searchParams;
  let orderTrackingId = q.get("OrderTrackingId") || q.get("orderTrackingId");
  let merchantReference = q.get("OrderMerchantReference") || q.get("orderMerchantReference");
  let notificationType = q.get("OrderNotificationType") || q.get("orderNotificationType") || "IPNCHANGE";

  if ((!orderTrackingId || !merchantReference) && request.method === "POST") {
    try {
      const body = (await request.json()) as Record<string, string>;
      orderTrackingId = orderTrackingId || body.OrderTrackingId || body.orderTrackingId || null;
      merchantReference =
        merchantReference || body.OrderMerchantReference || body.orderMerchantReference || null;
      notificationType =
        body.OrderNotificationType || body.orderNotificationType || notificationType;
    } catch {
      // ignore — handled by the missing-id guard below
    }
  }
  return { orderTrackingId, merchantReference, notificationType };
}

function addMonths(from: Date, months: number): string {
  const d = new Date(from.getTime());
  d.setUTCMonth(d.getUTCMonth() + months);
  return d.toISOString();
}

async function processCompleted(
  env: Env,
  request: Request,
  trackingId: string
): Promise<void> {
  const order = await getPesapalOrderByTracking(env.WAITLIST_DB, trackingId);
  if (!order) {
    console.error(`Pesapal IPN: no pesapal_orders row for tracking ${trackingId}`);
    return;
  }
  if (order.status === "completed") return; // already applied

  const token = await getAccessToken(env);
  const status = await getTransactionStatus(env, token, trackingId);
  const norm = normalizeStatus(status);

  if (norm !== "completed") {
    await setPesapalOrderStatus(env.WAITLIST_DB, order.id, norm);
    return;
  }

  // One subscription row per (tenant, product) for Pesapal; renewals update it.
  const providerSubId = `pesapal:${order.tenant_id}:${order.product}`;
  const now = new Date();
  const periodEnd = addMonths(now, order.billing_cycle === "annual" ? 12 : 1);
  await upsertSubscription(env.WAITLIST_DB, {
    tenantId: order.tenant_id,
    provider: "pesapal",
    providerCustomerId: null,
    providerSubscriptionId: providerSubId,
    product: order.product,
    tier: order.tier,
    billingCycle: order.billing_cycle,
    currency: order.currency,
    amountCents: order.amount_minor,
    currentPeriodStart: now.toISOString(),
    currentPeriodEnd: periodEnd,
    cancelAtPeriodEnd: false,
    status: "active",
  });
  const subRow = await getSubscriptionByProviderId(env.WAITLIST_DB, providerSubId);

  const tenant = await getTenantById(env.WAITLIST_DB, order.tenant_id);
  const tax = resolveTax(tenant?.country ?? null, order.amount_minor);
  await upsertInvoice(env.WAITLIST_DB, {
    tenantId: order.tenant_id,
    provider: "pesapal",
    subscriptionId: subRow?.id ?? null,
    providerInvoiceId: trackingId,
    currency: order.currency,
    subtotalCents: tax.subtotalCents,
    taxCents: tax.taxCents,
    totalCents: order.amount_minor,
    taxLabel: tax.taxLabel,
    status: "paid",
    hostedUrl: null,
    pdfUrl: null,
    paidAt: now.toISOString(),
    createdAt: now.toISOString(),
  });

  await setTenantPlan(env.WAITLIST_DB, order.tenant_id, {
    product: order.product,
    tier: order.tier,
    status: "active",
  });
  if (order.discount_code) {
    await recordRedemption(env.WAITLIST_DB, order.discount_code, order.tenant_id, "pesapal", trackingId);
  }
  await setPesapalOrderStatus(env.WAITLIST_DB, order.id, "completed");
  await audit(env.WAITLIST_DB, {
    tenantId: order.tenant_id,
    actorUserId: null,
    action: "subscription.activated",
    target: trackingId,
    metadata: { provider: "pesapal", product: order.product, tier: order.tier, cycle: order.billing_cycle },
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  });
}

async function handle(request: Request, env: Env): Promise<Response> {
  const p = await readParams(request);
  if (!p.orderTrackingId) {
    // Nothing to act on; acknowledge so Pesapal doesn't hammer us.
    return ack(p, 200);
  }

  // Dedupe on the tracking id (Pesapal can fire the IPN more than once).
  const prior = await getWebhookByEventId(env.WAITLIST_DB, "pesapal", p.orderTrackingId);
  if (prior && (prior.status === "processed" || prior.status === "ignored")) {
    return ack(p, 200);
  }

  const logId =
    prior?.id ??
    (await insertWebhookLog(env.WAITLIST_DB, {
      provider: "pesapal",
      eventId: p.orderTrackingId,
      eventType: p.notificationType,
      signatureOk: true, // verified by re-querying GetTransactionStatus
      payload: JSON.stringify(p),
      status: "received",
    }));
  if (prior) await updateWebhookStatus(env.WAITLIST_DB, logId, "received");

  try {
    await processCompleted(env, request, p.orderTrackingId);
    await updateWebhookStatus(env.WAITLIST_DB, logId, "processed");
    return ack(p, 200);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    console.error(`Pesapal IPN failed for ${p.orderTrackingId}: ${msg}`);
    await updateWebhookStatus(env.WAITLIST_DB, logId, "error", msg);
    // status 500 → Pesapal retries; the 'error' row re-allows reprocessing.
    return ack(p, 500);
  }
}

export const onRequestGet: PagesFunction<Env> = ({ request, env }) => handle(request, env);
export const onRequestPost: PagesFunction<Env> = ({ request, env }) => handle(request, env);
