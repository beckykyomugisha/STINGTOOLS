// Thin, dependency-free Stripe client over fetch(api.stripe.com) — same shape as
// the Resend wrapper in functions/api/auth/_lib/email.ts. The Node `stripe` SDK
// doesn't run cleanly on Workers, so we form-encode requests ourselves and verify
// webhook signatures with Web Crypto HMAC-SHA256.

import { serverError } from "../../auth/_lib/errors";
import { timingSafeEqual } from "../../auth/_lib/tokens";

const STRIPE_API = "https://api.stripe.com/v1";

// Internal: thrown when Stripe returns a non-2xx. Logged server-side; callers
// translate it to a generic message so Stripe internals never reach the client.
export class StripeError extends Error {
  status: number;
  stripeCode: string | null;
  constructor(status: number, message: string, stripeCode: string | null) {
    super(message);
    this.name = "StripeError";
    this.status = status;
    this.stripeCode = stripeCode;
  }
}

// ---- form encoding (Stripe's bracket syntax) ------------------------------
// { line_items: [{ price_data: { currency: 'usd' } }] }
//   → line_items[0][price_data][currency]=usd
export type FormValue =
  | string
  | number
  | boolean
  | null
  | undefined
  | FormObject
  | FormValue[];
export interface FormObject {
  [k: string]: FormValue;
}

export function encodeForm(params: FormObject): string {
  const pairs: string[] = [];
  const add = (key: string, val: FormValue): void => {
    if (val === undefined || val === null) return;
    if (Array.isArray(val)) {
      val.forEach((v, i) => add(`${key}[${i}]`, v));
    } else if (typeof val === "object") {
      for (const k of Object.keys(val)) add(`${key}[${k}]`, (val as FormObject)[k]);
    } else {
      pairs.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(val))}`);
    }
  };
  for (const k of Object.keys(params)) add(k, params[k]);
  return pairs.join("&");
}

// ---- request helpers ------------------------------------------------------

interface StripeRequestOpts {
  secretKey: string;
  idempotencyKey?: string | null;
}

async function request<T>(
  method: "GET" | "POST",
  path: string,
  opts: StripeRequestOpts,
  body?: FormObject
): Promise<T> {
  const headers: Record<string, string> = {
    Authorization: `Bearer ${opts.secretKey}`,
  };
  let url = `${STRIPE_API}${path}`;
  let payload: string | undefined;
  if (method === "POST") {
    headers["Content-Type"] = "application/x-www-form-urlencoded";
    payload = body ? encodeForm(body) : "";
    if (opts.idempotencyKey) headers["Idempotency-Key"] = opts.idempotencyKey;
  } else if (body) {
    const qs = encodeForm(body);
    if (qs) url += `?${qs}`;
  }

  const res = await fetch(url, { method, headers, body: payload });
  const text = await res.text();
  let json: Record<string, unknown> = {};
  try {
    json = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    json = {};
  }
  if (!res.ok) {
    const err = (json.error ?? {}) as { message?: string; code?: string };
    console.error(`Stripe ${method} ${path} failed (${res.status}): ${err.message ?? text}`);
    throw new StripeError(res.status, err.message ?? "Stripe request failed", err.code ?? null);
  }
  return json as T;
}

export function stripePost<T>(
  path: string,
  body: FormObject,
  opts: StripeRequestOpts
): Promise<T> {
  return request<T>("POST", path, opts, body);
}

export function stripeGet<T>(
  path: string,
  opts: StripeRequestOpts,
  query?: FormObject
): Promise<T> {
  return request<T>("GET", path, opts, query);
}

// Translate any thrown error into a safe AuthError for the client. Stripe
// messages are logged but never returned verbatim.
export function safeBillingError(e: unknown): never {
  if (e instanceof StripeError) {
    throw serverError("Payment service error. Please try again.");
  }
  throw e;
}

// ---- minimal typed Stripe object shapes (only the fields we read) ----------

export interface StripeCustomer {
  id: string;
}

export interface StripeCheckoutSession {
  id: string;
  url: string | null;
  customer: string | null;
  subscription: string | null;
  client_reference_id: string | null;
  metadata?: Record<string, string>;
}

export interface StripePortalSession {
  id: string;
  url: string;
}

export interface StripeCoupon {
  id: string;
}

// Create a one-time (duration: 'once') coupon so a discount code only applies to
// the first invoice — renewals bill at full price. percent_off XOR amount_off
// (amount_off needs currency, in the smallest unit). Used by the checkout route.
export function stripeCreateCoupon(
  opts: { secretKey: string; idempotencyKey?: string | null },
  spec:
    | { percentOff: number; name?: string }
    | { amountOff: number; currency: string; name?: string }
): Promise<StripeCoupon> {
  const body: FormObject = { duration: "once", max_redemptions: 1 };
  if ("percentOff" in spec) {
    body.percent_off = spec.percentOff;
  } else {
    body.amount_off = spec.amountOff;
    body.currency = spec.currency.toLowerCase();
  }
  if (spec.name) body.name = spec.name;
  return stripePost<StripeCoupon>("/coupons", body, opts);
}

export interface StripeSubscriptionItem {
  id: string;
}

export interface StripeSubscription {
  id: string;
  status: string;
  customer: string;
  cancel_at_period_end: boolean;
  current_period_start: number;
  current_period_end: number;
  items: { data: StripeSubscriptionItem[] };
  metadata?: Record<string, string>;
}

export interface StripeInvoiceLine {
  parent?: {
    subscription_item_details?: { subscription?: string | null } | null;
  } | null;
}

export interface StripeInvoice {
  id: string;
  number: string | null;
  customer: string;
  // The top-level `subscription` field existed before the 2025-03-31.basil API,
  // which removed it and moved the id to parent.subscription_details.subscription
  // (and the per-line parent.subscription_item_details.subscription). We read all
  // three so the webhook works regardless of the account's API version.
  subscription?: string | null;
  parent?: {
    subscription_details?: { subscription?: string | null } | null;
  } | null;
  lines?: { data?: StripeInvoiceLine[] };
  currency: string;
  subtotal: number;
  tax: number | null;
  total: number;
  status: string;
  hosted_invoice_url: string | null;
  invoice_pdf: string | null;
  created: number;
  status_transitions?: { paid_at: number | null };
}

// Resolve an invoice's subscription id across Stripe API versions: pre-Basil
// top-level `subscription`, Basil `parent.subscription_details.subscription`, or
// the first line item's `parent.subscription_item_details.subscription`.
export function invoiceSubscriptionId(inv: StripeInvoice): string | null {
  if (inv.subscription) return inv.subscription;
  const fromParent = inv.parent?.subscription_details?.subscription;
  if (fromParent) return fromParent;
  for (const line of inv.lines?.data ?? []) {
    const sub = line.parent?.subscription_item_details?.subscription;
    if (sub) return sub;
  }
  return null;
}

export interface StripeWebhookEvent {
  id: string;
  type: string;
  data: { object: Record<string, unknown> };
}

// ---- webhook signature verification ---------------------------------------
// Stripe-Signature: t=<unix>,v1=<hex hmac>[,v0=...]. signed_payload = `${t}.${body}`.
// expected = HMAC-SHA256(signed_payload, whsec). Default 5-minute tolerance.

const SIG_TOLERANCE_SECONDS = 5 * 60;

async function hmacHex(secret: string, message: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  const view = new Uint8Array(sig);
  let hex = "";
  for (let i = 0; i < view.length; i++) hex += view[i].toString(16).padStart(2, "0");
  return hex;
}

export async function verifyWebhookSignature(
  rawBody: string,
  sigHeader: string | null,
  secret: string,
  nowSeconds: number = Math.floor(Date.now() / 1000)
): Promise<boolean> {
  if (!sigHeader || !secret) return false;
  let t: number | null = null;
  const v1s: string[] = [];
  for (const part of sigHeader.split(",")) {
    const [k, v] = part.split("=");
    if (k === "t") t = parseInt(v, 10);
    else if (k === "v1") v1s.push(v);
  }
  if (t === null || Number.isNaN(t) || v1s.length === 0) return false;
  if (Math.abs(nowSeconds - t) > SIG_TOLERANCE_SECONDS) return false;

  const expected = await hmacHex(secret, `${t}.${rawBody}`);
  // A signed payload can carry multiple v1s during secret rotation; match any.
  return v1s.some((sig) => timingSafeEqual(sig, expected));
}

// Unix seconds → ISO 8601 UTC (or null).
export function unixToIso(secs: number | null | undefined): string | null {
  if (secs === null || secs === undefined) return null;
  return new Date(secs * 1000).toISOString();
}
