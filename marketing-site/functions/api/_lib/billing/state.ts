// Subscription state machine + D1 persistence for billing. Keeps the tenant's
// `subscription_status` in lockstep with the provider's view, and owns the
// subscriptions / invoices / webhooks_log tables.

import { uuid } from "../../auth/_lib/tokens";

// ---- row shapes -----------------------------------------------------------

export interface SubscriptionRow {
  id: string;
  tenant_id: string;
  provider: string;
  provider_customer_id: string | null;
  provider_subscription_id: string | null;
  product: string;
  tier: string;
  billing_cycle: string;
  currency: string;
  amount_cents: number;
  current_period_start: string | null;
  current_period_end: string | null;
  cancel_at_period_end: number;
  status: string;
  created_at: string;
  updated_at: string | null;
}

export interface InvoiceRow {
  id: string;
  tenant_id: string;
  subscription_id: string | null;
  provider: string;
  provider_invoice_id: string | null;
  number: string;
  currency: string;
  subtotal_cents: number;
  tax_cents: number;
  total_cents: number;
  tax_label: string | null;
  status: string;
  hosted_url: string | null;
  pdf_url: string | null;
  due_at: string | null;
  paid_at: string | null;
  created_at: string;
}

export interface WebhookLogRow {
  id: string;
  provider: string;
  event_id: string | null;
  event_type: string | null;
  signature_ok: number;
  payload: string;
  status: string;
  error: string | null;
  created_at: string;
  processed_at: string | null;
}

// ---- state mapping --------------------------------------------------------
// Map a Stripe subscription/invoice signal onto our tenant.subscription_status
// vocabulary: trial | active | past_due | read_only | cancelled.

export function mapStripeStatus(stripeStatus: string): string {
  switch (stripeStatus) {
    case "active":
    case "trialing":
      return "active";
    case "past_due":
      return "past_due";
    case "unpaid":
      return "read_only"; // retries exhausted → lock to read-only
    case "canceled":
      return "cancelled";
    case "incomplete":
    case "incomplete_expired":
      return "past_due";
    default:
      return "past_due";
  }
}

// ---- subscriptions --------------------------------------------------------

export interface UpsertSubscriptionInput {
  tenantId: string;
  provider?: string; // defaults to 'stripe'
  providerCustomerId: string | null;
  providerSubscriptionId: string | null;
  product: string;
  tier: string;
  billingCycle: string;
  currency: string;
  amountCents: number;
  currentPeriodStart: string | null;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  status: string;
}

// Insert-or-update keyed on provider_subscription_id (the Stripe id). Webhooks
// fire more than once for the same subscription, so this must be idempotent.
export async function upsertSubscription(
  db: D1Database,
  input: UpsertSubscriptionInput
): Promise<SubscriptionRow> {
  const nowIso = new Date().toISOString();
  const existing = input.providerSubscriptionId
    ? await getSubscriptionByProviderId(db, input.providerSubscriptionId)
    : null;

  if (existing) {
    await db
      .prepare(
        `UPDATE subscriptions
           SET provider_customer_id = ?, product = ?, tier = ?, billing_cycle = ?,
               currency = ?, amount_cents = ?, current_period_start = ?,
               current_period_end = ?, cancel_at_period_end = ?, status = ?,
               updated_at = ?
         WHERE id = ?`
      )
      .bind(
        input.providerCustomerId,
        input.product,
        input.tier,
        input.billingCycle,
        input.currency,
        input.amountCents,
        input.currentPeriodStart,
        input.currentPeriodEnd,
        input.cancelAtPeriodEnd ? 1 : 0,
        input.status,
        nowIso,
        existing.id
      )
      .run();
    return { ...existing, ...rowFromInput(existing.id, existing.created_at, nowIso, input) };
  }

  const id = uuid();
  await db
    .prepare(
      `INSERT INTO subscriptions
         (id, tenant_id, provider, provider_customer_id, provider_subscription_id,
          product, tier, billing_cycle, currency, amount_cents,
          current_period_start, current_period_end, cancel_at_period_end, status,
          created_at)
       VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`
    )
    .bind(
      id,
      input.tenantId,
      input.provider ?? "stripe",
      input.providerCustomerId,
      input.providerSubscriptionId,
      input.product,
      input.tier,
      input.billingCycle,
      input.currency,
      input.amountCents,
      input.currentPeriodStart,
      input.currentPeriodEnd,
      input.cancelAtPeriodEnd ? 1 : 0,
      input.status,
      nowIso
    )
    .run();
  return rowFromInput(id, nowIso, nowIso, input);
}

function rowFromInput(
  id: string,
  createdAt: string,
  updatedAt: string,
  input: UpsertSubscriptionInput
): SubscriptionRow {
  return {
    id,
    tenant_id: input.tenantId,
    provider: input.provider ?? "stripe",
    provider_customer_id: input.providerCustomerId,
    provider_subscription_id: input.providerSubscriptionId,
    product: input.product,
    tier: input.tier,
    billing_cycle: input.billingCycle,
    currency: input.currency,
    amount_cents: input.amountCents,
    current_period_start: input.currentPeriodStart,
    current_period_end: input.currentPeriodEnd,
    cancel_at_period_end: input.cancelAtPeriodEnd ? 1 : 0,
    status: input.status,
    created_at: createdAt,
    updated_at: updatedAt,
  };
}

export async function getSubscriptionByProviderId(
  db: D1Database,
  providerSubscriptionId: string
): Promise<SubscriptionRow | null> {
  return db
    .prepare(`SELECT * FROM subscriptions WHERE provider_subscription_id = ?`)
    .bind(providerSubscriptionId)
    .first<SubscriptionRow>();
}

// The tenant's current Stripe subscription (most recent non-cancelled, else
// most recent). Used by cancel / resume / change-plan.
export async function getActiveSubscription(
  db: D1Database,
  tenantId: string
): Promise<SubscriptionRow | null> {
  const live = await db
    .prepare(
      `SELECT * FROM subscriptions
         WHERE tenant_id = ? AND provider = 'stripe' AND status != 'cancelled'
       ORDER BY created_at DESC LIMIT 1`
    )
    .bind(tenantId)
    .first<SubscriptionRow>();
  if (live) return live;
  return db
    .prepare(
      `SELECT * FROM subscriptions WHERE tenant_id = ? AND provider = 'stripe'
       ORDER BY created_at DESC LIMIT 1`
    )
    .bind(tenantId)
    .first<SubscriptionRow>();
}

export async function setSubscriptionCancelAtPeriodEnd(
  db: D1Database,
  id: string,
  cancel: boolean
): Promise<void> {
  await db
    .prepare(
      `UPDATE subscriptions SET cancel_at_period_end = ?, updated_at = ? WHERE id = ?`
    )
    .bind(cancel ? 1 : 0, new Date().toISOString(), id)
    .run();
}

export async function setSubscriptionPlan(
  db: D1Database,
  id: string,
  fields: { product: string; tier: string; billingCycle: string; amountCents: number }
): Promise<void> {
  await db
    .prepare(
      `UPDATE subscriptions
         SET product = ?, tier = ?, billing_cycle = ?, amount_cents = ?, updated_at = ?
       WHERE id = ?`
    )
    .bind(fields.product, fields.tier, fields.billingCycle, fields.amountCents, new Date().toISOString(), id)
    .run();
}

export async function markSubscriptionCancelled(
  db: D1Database,
  providerSubscriptionId: string
): Promise<void> {
  await db
    .prepare(
      `UPDATE subscriptions SET status = 'cancelled', updated_at = ?
       WHERE provider_subscription_id = ?`
    )
    .bind(new Date().toISOString(), providerSubscriptionId)
    .run();
}

// ---- tenant billing sync --------------------------------------------------

export async function setTenantStripeCustomer(
  db: D1Database,
  tenantId: string,
  customerId: string
): Promise<void> {
  await db
    .prepare(`UPDATE tenants SET stripe_customer_id = ?, updated_at = ? WHERE id = ?`)
    .bind(customerId, new Date().toISOString(), tenantId)
    .run();
}

export async function setTenantPlan(
  db: D1Database,
  tenantId: string,
  fields: { product: string; tier: string; status: string }
): Promise<void> {
  await db
    .prepare(
      `UPDATE tenants
         SET plan_product = ?, plan_tier = ?, subscription_status = ?, updated_at = ?
       WHERE id = ?`
    )
    .bind(fields.product, fields.tier, fields.status, new Date().toISOString(), tenantId)
    .run();
}

export async function setTenantSubscriptionStatus(
  db: D1Database,
  tenantId: string,
  status: string
): Promise<void> {
  await db
    .prepare(`UPDATE tenants SET subscription_status = ?, updated_at = ? WHERE id = ?`)
    .bind(status, new Date().toISOString(), tenantId)
    .run();
}

// ---- invoices -------------------------------------------------------------

export async function getInvoiceById(
  db: D1Database,
  tenantId: string,
  id: string
): Promise<InvoiceRow | null> {
  return db
    .prepare(`SELECT * FROM invoices WHERE id = ? AND tenant_id = ?`)
    .bind(id, tenantId)
    .first<InvoiceRow>();
}

export async function getInvoiceByProviderId(
  db: D1Database,
  providerInvoiceId: string
): Promise<InvoiceRow | null> {
  return db
    .prepare(`SELECT * FROM invoices WHERE provider_invoice_id = ?`)
    .bind(providerInvoiceId)
    .first<InvoiceRow>();
}

export async function listInvoices(
  db: D1Database,
  tenantId: string,
  opts: { limit: number; offset: number }
): Promise<InvoiceRow[]> {
  const res = await db
    .prepare(
      `SELECT * FROM invoices WHERE tenant_id = ?
       ORDER BY created_at DESC LIMIT ? OFFSET ?`
    )
    .bind(tenantId, opts.limit, opts.offset)
    .all<InvoiceRow>();
  return res.results ?? [];
}

// Next PS-INV-YYYY-NNNN for the given year (4-digit, count-based). Low volume,
// so a count+1 is fine; numbers are only minted once per provider invoice
// (upsertInvoice reuses an existing row's number on replay).
export async function nextInvoiceNumber(db: D1Database, year: number): Promise<string> {
  const prefix = `PS-INV-${year}-`;
  const row = await db
    .prepare(`SELECT COUNT(*) AS n FROM invoices WHERE number LIKE ?`)
    .bind(`${prefix}%`)
    .first<{ n: number }>();
  const next = (row?.n ?? 0) + 1;
  return `${prefix}${String(next).padStart(4, "0")}`;
}

export interface UpsertInvoiceInput {
  tenantId: string;
  provider?: string; // defaults to 'stripe'
  subscriptionId: string | null;
  providerInvoiceId: string;
  currency: string;
  subtotalCents: number;
  taxCents: number;
  totalCents: number;
  taxLabel?: string | null;
  status: string;
  hostedUrl: string | null;
  pdfUrl: string | null;
  paidAt: string | null;
  createdAt: string;
}

// Insert-or-update keyed on provider_invoice_id; mints our PS-INV number only on
// first sight so replays keep the same number.
export async function upsertInvoice(
  db: D1Database,
  input: UpsertInvoiceInput
): Promise<InvoiceRow> {
  const existing = await getInvoiceByProviderId(db, input.providerInvoiceId);
  if (existing) {
    const taxLabel = input.taxLabel !== undefined ? input.taxLabel : existing.tax_label;
    await db
      .prepare(
        `UPDATE invoices
           SET subscription_id = ?, currency = ?, subtotal_cents = ?, tax_cents = ?,
               total_cents = ?, tax_label = ?, status = ?, hosted_url = ?, pdf_url = ?,
               paid_at = ?
         WHERE id = ?`
      )
      .bind(
        input.subscriptionId,
        input.currency,
        input.subtotalCents,
        input.taxCents,
        input.totalCents,
        taxLabel,
        input.status,
        input.hostedUrl,
        input.pdfUrl,
        input.paidAt,
        existing.id
      )
      .run();
    return {
      ...existing,
      subscription_id: input.subscriptionId,
      currency: input.currency,
      subtotal_cents: input.subtotalCents,
      tax_cents: input.taxCents,
      total_cents: input.totalCents,
      tax_label: taxLabel,
      status: input.status,
      hosted_url: input.hostedUrl,
      pdf_url: input.pdfUrl,
      paid_at: input.paidAt,
    };
  }

  const id = uuid();
  const year = new Date(input.createdAt).getUTCFullYear();
  const number = await nextInvoiceNumber(db, year);
  const provider = input.provider ?? "stripe";
  const taxLabel = input.taxLabel ?? null;
  await db
    .prepare(
      `INSERT INTO invoices
         (id, tenant_id, subscription_id, provider, provider_invoice_id, number,
          currency, subtotal_cents, tax_cents, total_cents, tax_label, status,
          hosted_url, pdf_url, paid_at, created_at)
       VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`
    )
    .bind(
      id,
      input.tenantId,
      input.subscriptionId,
      provider,
      input.providerInvoiceId,
      number,
      input.currency,
      input.subtotalCents,
      input.taxCents,
      input.totalCents,
      taxLabel,
      input.status,
      input.hostedUrl,
      input.pdfUrl,
      input.paidAt,
      input.createdAt
    )
    .run();
  return {
    id,
    tenant_id: input.tenantId,
    subscription_id: input.subscriptionId,
    provider,
    provider_invoice_id: input.providerInvoiceId,
    number,
    currency: input.currency,
    subtotal_cents: input.subtotalCents,
    tax_cents: input.taxCents,
    total_cents: input.totalCents,
    tax_label: taxLabel,
    status: input.status,
    hosted_url: input.hostedUrl,
    pdf_url: input.pdfUrl,
    due_at: null,
    paid_at: input.paidAt,
    created_at: input.createdAt,
  };
}

export function toPublicInvoice(i: InvoiceRow) {
  return {
    id: i.id,
    number: i.number,
    currency: i.currency,
    subtotalCents: i.subtotal_cents,
    taxCents: i.tax_cents,
    totalCents: i.total_cents,
    taxLabel: i.tax_label,
    status: i.status,
    hostedUrl: i.hosted_url,
    pdfUrl: i.pdf_url,
    paidAt: i.paid_at,
    createdAt: i.created_at,
  };
}

// ---- webhook log ----------------------------------------------------------

export async function getWebhookByEventId(
  db: D1Database,
  provider: string,
  eventId: string
): Promise<WebhookLogRow | null> {
  return db
    .prepare(`SELECT * FROM webhooks_log WHERE provider = ? AND event_id = ?`)
    .bind(provider, eventId)
    .first<WebhookLogRow>();
}

export interface InsertWebhookInput {
  provider?: string; // defaults to 'stripe'
  eventId: string | null;
  eventType: string | null;
  signatureOk: boolean;
  payload: string;
  status: string;
}

export async function insertWebhookLog(
  db: D1Database,
  input: InsertWebhookInput
): Promise<string> {
  const id = uuid();
  await db
    .prepare(
      `INSERT INTO webhooks_log
         (id, provider, event_id, event_type, signature_ok, payload, status, created_at)
       VALUES (?,?,?,?,?,?,?,?)`
    )
    .bind(
      id,
      input.provider ?? "stripe",
      input.eventId,
      input.eventType,
      input.signatureOk ? 1 : 0,
      input.payload,
      input.status,
      new Date().toISOString()
    )
    .run();
  return id;
}

export async function updateWebhookStatus(
  db: D1Database,
  id: string,
  status: string,
  error: string | null = null
): Promise<void> {
  await db
    .prepare(`UPDATE webhooks_log SET status = ?, error = ?, processed_at = ? WHERE id = ?`)
    .bind(status, error, new Date().toISOString(), id)
    .run();
}

// ---- pesapal orders -------------------------------------------------------
// Pending Pesapal checkout context. Written at checkout, resolved on the IPN.

export interface PesapalOrderRow {
  id: string;
  tenant_id: string;
  order_tracking_id: string | null;
  merchant_reference: string;
  product: string;
  tier: string;
  billing_cycle: string;
  currency: string;
  amount_minor: number;
  discount_code: string | null;
  status: string;
  created_at: string;
  updated_at: string | null;
}

export interface CreatePesapalOrderInput {
  id: string;
  tenantId: string;
  merchantReference: string;
  product: string;
  tier: string;
  billingCycle: string;
  currency: string;
  amountMinor: number;
  discountCode: string | null;
}

export async function createPesapalOrder(
  db: D1Database,
  input: CreatePesapalOrderInput
): Promise<void> {
  await db
    .prepare(
      `INSERT INTO pesapal_orders
         (id, tenant_id, order_tracking_id, merchant_reference, product, tier,
          billing_cycle, currency, amount_minor, discount_code, status, created_at)
       VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`
    )
    .bind(
      input.id,
      input.tenantId,
      null,
      input.merchantReference,
      input.product,
      input.tier,
      input.billingCycle,
      input.currency,
      input.amountMinor,
      input.discountCode,
      "pending",
      new Date().toISOString()
    )
    .run();
}

export async function setPesapalOrderTracking(
  db: D1Database,
  id: string,
  orderTrackingId: string
): Promise<void> {
  await db
    .prepare(`UPDATE pesapal_orders SET order_tracking_id = ?, updated_at = ? WHERE id = ?`)
    .bind(orderTrackingId, new Date().toISOString(), id)
    .run();
}

export async function getPesapalOrderByTracking(
  db: D1Database,
  orderTrackingId: string
): Promise<PesapalOrderRow | null> {
  return db
    .prepare(`SELECT * FROM pesapal_orders WHERE order_tracking_id = ?`)
    .bind(orderTrackingId)
    .first<PesapalOrderRow>();
}

export async function setPesapalOrderStatus(
  db: D1Database,
  id: string,
  status: string
): Promise<void> {
  await db
    .prepare(`UPDATE pesapal_orders SET status = ?, updated_at = ? WHERE id = ?`)
    .bind(status, new Date().toISOString(), id)
    .run();
}
