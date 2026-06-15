// POST /api/webhooks/stripe — Stripe sends events here (server-to-server).
// Verifies the Stripe-Signature header (Web Crypto HMAC-SHA256); persists EVERY
// received event to webhooks_log (valid sig or not); rejects a bad signature with
// 400; dedupes on Event.id; processes the 6 subscribed events idempotently.
//
// Subscribed events (case-sensitive):
//   checkout.session.completed
//   customer.subscription.created
//   customer.subscription.updated
//   customer.subscription.deleted
//   invoice.paid
//   invoice.payment_failed

import { withHandler } from "../auth/_lib/handler";
import { jsonResponse } from "../auth/_lib/cors";
import { getTenantById, audit } from "../auth/_lib/db";
import {
  verifyWebhookSignature,
  stripeGet,
  unixToIso,
  type StripeSubscription,
  type StripeCheckoutSession,
  type StripeInvoice,
  type StripeWebhookEvent,
} from "../_lib/billing/stripe";
import {
  mapStripeStatus,
  upsertSubscription,
  getSubscriptionByProviderId,
  markSubscriptionCancelled,
  setTenantStripeCustomer,
  setTenantPlan,
  setTenantSubscriptionStatus,
  upsertInvoice,
  getWebhookByEventId,
  insertWebhookLog,
  updateWebhookStatus,
} from "../_lib/billing/state";
import type { Env } from "../auth/_lib/types";

// CORS preflight is irrelevant for Stripe (no Origin), but keep the contract.
export const onRequestOptions: PagesFunction = async () =>
  new Response(null, { status: 204 });

// ---- plan metadata extraction ---------------------------------------------

interface PlanMeta {
  product: string;
  tier: string;
  cycle: string;
  currency: string;
  amountCents: number;
}

function planMeta(
  metadata: Record<string, string> | undefined,
  fallback?: { product: string; tier: string; billing_cycle: string; currency: string; amount_cents: number }
): PlanMeta {
  const m = metadata ?? {};
  return {
    product: m.product || fallback?.product || "sting-tools",
    tier: m.tier || fallback?.tier || "solo",
    cycle: m.cycle || fallback?.billing_cycle || "monthly",
    currency: (m.currency || fallback?.currency || "USD").toUpperCase(),
    amountCents: m.amount_cents ? parseInt(m.amount_cents, 10) || 0 : fallback?.amount_cents ?? 0,
  };
}

function auditCtx(request: Request) {
  return {
    ip: request.headers.get("CF-Connecting-IP"),
    userAgent: request.headers.get("User-Agent"),
  };
}

// Resolve the tenant for a subscription object via its metadata or our stored row.
async function tenantForSubscription(
  env: Env,
  sub: StripeSubscription
): Promise<{ tenantId: string } | null> {
  const metaTenant = sub.metadata?.tenant_id;
  if (metaTenant) return { tenantId: metaTenant };
  const existing = await getSubscriptionByProviderId(env.WAITLIST_DB, sub.id);
  return existing ? { tenantId: existing.tenant_id } : null;
}

// ---- per-event processing -------------------------------------------------
// Each returns "processed" or "ignored". Throwing bubbles to a 500 so Stripe
// retries (and the webhooks_log row is marked 'error', which re-allows reprocess).

async function handleCheckoutCompleted(
  env: Env,
  request: Request,
  session: StripeCheckoutSession
): Promise<"processed" | "ignored"> {
  const tenantId = session.client_reference_id || session.metadata?.tenant_id || null;
  if (!tenantId) return "ignored";
  const meta = planMeta(session.metadata);

  let status = "active";
  let periodStart: string | null = null;
  let periodEnd: string | null = null;
  let cancelAtPeriodEnd = false;
  if (session.subscription && env.STRIPE_SECRET_KEY) {
    const sub = await stripeGet<StripeSubscription>(
      `/subscriptions/${session.subscription}`,
      { secretKey: env.STRIPE_SECRET_KEY }
    );
    status = mapStripeStatus(sub.status);
    periodStart = unixToIso(sub.current_period_start);
    periodEnd = unixToIso(sub.current_period_end);
    cancelAtPeriodEnd = sub.cancel_at_period_end === true;
  }

  await upsertSubscription(env.WAITLIST_DB, {
    tenantId,
    providerCustomerId: session.customer,
    providerSubscriptionId: session.subscription,
    product: meta.product,
    tier: meta.tier,
    billingCycle: meta.cycle,
    currency: meta.currency,
    amountCents: meta.amountCents,
    currentPeriodStart: periodStart,
    currentPeriodEnd: periodEnd,
    cancelAtPeriodEnd,
    status,
  });
  if (session.customer) {
    await setTenantStripeCustomer(env.WAITLIST_DB, tenantId, session.customer);
  }
  await setTenantPlan(env.WAITLIST_DB, tenantId, {
    product: meta.product,
    tier: meta.tier,
    status,
  });
  await audit(env.WAITLIST_DB, {
    tenantId,
    actorUserId: null,
    action: "subscription.activated",
    target: session.subscription,
    metadata: { product: meta.product, tier: meta.tier, cycle: meta.cycle },
    ...auditCtx(request),
  });
  return "processed";
}

async function handleSubscriptionUpsert(
  env: Env,
  request: Request,
  sub: StripeSubscription,
  isCreate: boolean
): Promise<"processed" | "ignored"> {
  const t = await tenantForSubscription(env, sub);
  if (!t) return "ignored";
  const existing = await getSubscriptionByProviderId(env.WAITLIST_DB, sub.id);
  const meta = planMeta(sub.metadata, existing ?? undefined);
  const newStatus = mapStripeStatus(sub.status);

  await upsertSubscription(env.WAITLIST_DB, {
    tenantId: t.tenantId,
    providerCustomerId: sub.customer,
    providerSubscriptionId: sub.id,
    product: meta.product,
    tier: meta.tier,
    billingCycle: meta.cycle,
    currency: meta.currency,
    amountCents: meta.amountCents,
    currentPeriodStart: unixToIso(sub.current_period_start),
    currentPeriodEnd: unixToIso(sub.current_period_end),
    cancelAtPeriodEnd: sub.cancel_at_period_end === true,
    status: newStatus,
  });

  const tenant = await getTenantById(env.WAITLIST_DB, t.tenantId);
  const prevStatus = tenant?.subscription_status ?? null;
  await setTenantPlan(env.WAITLIST_DB, t.tenantId, {
    product: meta.product,
    tier: meta.tier,
    status: newStatus,
  });

  if (isCreate) {
    await audit(env.WAITLIST_DB, {
      tenantId: t.tenantId,
      actorUserId: null,
      action: "subscription.activated",
      target: sub.id,
      metadata: { product: meta.product, tier: meta.tier },
      ...auditCtx(request),
    });
  } else if (prevStatus === "past_due" && newStatus === "active") {
    await audit(env.WAITLIST_DB, {
      tenantId: t.tenantId,
      actorUserId: null,
      action: "payment.recovered",
      target: sub.id,
      metadata: { via: "subscription.updated" },
      ...auditCtx(request),
    });
  }
  return "processed";
}

async function handleSubscriptionDeleted(
  env: Env,
  request: Request,
  sub: StripeSubscription
): Promise<"processed" | "ignored"> {
  const t = await tenantForSubscription(env, sub);
  if (!t) return "ignored";
  await markSubscriptionCancelled(env.WAITLIST_DB, sub.id);
  await setTenantSubscriptionStatus(env.WAITLIST_DB, t.tenantId, "cancelled");
  await audit(env.WAITLIST_DB, {
    tenantId: t.tenantId,
    actorUserId: null,
    action: "subscription.cancelled",
    target: sub.id,
    metadata: { via: "customer.subscription.deleted" },
    ...auditCtx(request),
  });
  return "processed";
}

async function handleInvoice(
  env: Env,
  request: Request,
  inv: StripeInvoice,
  paid: boolean
): Promise<"processed" | "ignored"> {
  if (!inv.subscription) return "ignored";
  const subRow = await getSubscriptionByProviderId(env.WAITLIST_DB, inv.subscription);
  if (!subRow) return "ignored";
  const tenantId = subRow.tenant_id;

  await upsertInvoice(env.WAITLIST_DB, {
    tenantId,
    subscriptionId: subRow.id,
    providerInvoiceId: inv.id,
    currency: inv.currency.toUpperCase(),
    subtotalCents: inv.subtotal,
    taxCents: inv.tax ?? 0,
    totalCents: inv.total,
    status: paid ? "paid" : "open",
    hostedUrl: inv.hosted_invoice_url,
    pdfUrl: inv.invoice_pdf,
    paidAt: paid ? unixToIso(inv.status_transitions?.paid_at ?? inv.created) : null,
    createdAt: unixToIso(inv.created) ?? new Date().toISOString(),
  });

  const tenant = await getTenantById(env.WAITLIST_DB, tenantId);
  const prevStatus = tenant?.subscription_status ?? null;

  if (paid) {
    if (prevStatus !== "cancelled") {
      await setTenantSubscriptionStatus(env.WAITLIST_DB, tenantId, "active");
    }
    if (prevStatus === "past_due" || prevStatus === "read_only") {
      await audit(env.WAITLIST_DB, {
        tenantId,
        actorUserId: null,
        action: "payment.recovered",
        target: inv.id,
        metadata: { via: "invoice.paid" },
        ...auditCtx(request),
      });
    }
  } else {
    await setTenantSubscriptionStatus(env.WAITLIST_DB, tenantId, "past_due");
    await audit(env.WAITLIST_DB, {
      tenantId,
      actorUserId: null,
      action: "payment.failed",
      target: inv.id,
      metadata: { via: "invoice.payment_failed" },
      ...auditCtx(request),
    });
  }
  return "processed";
}

async function dispatch(
  env: Env,
  request: Request,
  event: StripeWebhookEvent
): Promise<"processed" | "ignored"> {
  const obj = event.data.object;
  switch (event.type) {
    case "checkout.session.completed":
      return handleCheckoutCompleted(env, request, obj as unknown as StripeCheckoutSession);
    case "customer.subscription.created":
      return handleSubscriptionUpsert(env, request, obj as unknown as StripeSubscription, true);
    case "customer.subscription.updated":
      return handleSubscriptionUpsert(env, request, obj as unknown as StripeSubscription, false);
    case "customer.subscription.deleted":
      return handleSubscriptionDeleted(env, request, obj as unknown as StripeSubscription);
    case "invoice.paid":
      return handleInvoice(env, request, obj as unknown as StripeInvoice, true);
    case "invoice.payment_failed":
      return handleInvoice(env, request, obj as unknown as StripeInvoice, false);
    default:
      return "ignored"; // not one of our 6 — acknowledged but not processed
  }
}

export const onRequestPost = withHandler(async ({ request, env }) => {
  const raw = await request.text();
  const sigHeader = request.headers.get("Stripe-Signature");
  const secret = env.STRIPE_WEBHOOK_SECRET || "";
  const verified = await verifyWebhookSignature(raw, sigHeader, secret);

  // Parse for logging even on bad signature (best-effort).
  let event: StripeWebhookEvent | null = null;
  try {
    event = JSON.parse(raw) as StripeWebhookEvent;
  } catch {
    event = null;
  }

  if (!verified) {
    // Persist with event_id=null (NULLs are distinct → never blocks a real retry).
    await insertWebhookLog(env.WAITLIST_DB, {
      eventId: null,
      eventType: event?.type ?? null,
      signatureOk: false,
      payload: raw,
      status: "bad_signature",
    });
    // Reject silently — no detail to the caller.
    return jsonResponse(request, { error: "Invalid signature." }, 400);
  }

  if (!event || !event.id) {
    await insertWebhookLog(env.WAITLIST_DB, {
      eventId: null,
      eventType: null,
      signatureOk: true,
      payload: raw,
      status: "error",
    });
    return jsonResponse(request, { error: "Malformed event." }, 400);
  }

  // Dedupe: a previously processed/ignored event is acknowledged, not re-run.
  // A prior 'received'/'error' row (in-flight or failed) is reused so reprocess
  // doesn't collide with the UNIQUE(provider, event_id) index.
  const prior = await getWebhookByEventId(env.WAITLIST_DB, "stripe", event.id);
  if (prior && (prior.status === "processed" || prior.status === "ignored")) {
    return jsonResponse(request, { received: true, duplicate: true }, 200);
  }

  let logId: string;
  if (prior) {
    logId = prior.id;
    await updateWebhookStatus(env.WAITLIST_DB, logId, "received");
  } else {
    logId = await insertWebhookLog(env.WAITLIST_DB, {
      eventId: event.id,
      eventType: event.type,
      signatureOk: true,
      payload: raw,
      status: "received",
    });
  }

  try {
    const outcome = await dispatch(env, request, event);
    await updateWebhookStatus(env.WAITLIST_DB, logId, outcome);
    return jsonResponse(request, { received: true }, 200);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    console.error(`Webhook processing failed for ${event.type} (${event.id}): ${msg}`);
    await updateWebhookStatus(env.WAITLIST_DB, logId, "error", msg);
    // 500 → Stripe retries; the 'error' status re-allows reprocessing.
    return jsonResponse(request, { error: "Processing failed." }, 500);
  }
});
