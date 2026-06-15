// Thin, dependency-free Pesapal API 3.0 (V3) client over fetch(). Mirrors the
// shape of stripe.ts. Pesapal's security model is "never trust the IPN payload":
// the callback + IPN carry only an OrderTrackingId, and you re-fetch the
// authoritative status with GetTransactionStatus (authenticated with your own
// bearer token). So there's no inbound HMAC to verify — confirmation == re-query.
//
// Flow:
//   1. RequestToken (consumer_key/secret)            → Bearer token (≤5 min)
//   2. SubmitOrderRequest (amount/currency/IPN id)   → redirect_url + tracking id
//   3. (Pesapal redirects the buyer; pays; fires IPN)
//   4. GetTransactionStatus (tracking id)            → COMPLETED / FAILED / ...

import { serverError } from "../../auth/_lib/errors";

const SANDBOX_BASE = "https://cybqa.pesapal.com/pesapalv3";

export class PesapalError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = "PesapalError";
    this.status = status;
  }
}

export function pesapalBase(env: { PESAPAL_BASE_URL?: string }): string {
  return (env.PESAPAL_BASE_URL || SANDBOX_BASE).replace(/\/+$/, "");
}

interface PesapalEnv {
  PESAPAL_CONSUMER_KEY?: string;
  PESAPAL_CONSUMER_SECRET?: string;
  PESAPAL_BASE_URL?: string;
  PESAPAL_IPN_ID?: string;
}

async function pesapalFetch<T>(
  url: string,
  method: "GET" | "POST",
  token: string | null,
  body?: unknown
): Promise<T> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json",
  };
  if (token) headers.Authorization = `Bearer ${token}`;
  const res = await fetch(url, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json: Record<string, unknown> = {};
  try {
    json = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    json = {};
  }
  if (!res.ok) {
    console.error(`Pesapal ${method} ${url} failed (${res.status}): ${text}`);
    throw new PesapalError(res.status, "Pesapal request failed");
  }
  return json as T;
}

// ---- auth -----------------------------------------------------------------

interface TokenResponse {
  token?: string;
  expiryDate?: string;
  error?: unknown;
  status?: string;
  message?: string;
}

export async function getAccessToken(env: PesapalEnv): Promise<string> {
  if (!env.PESAPAL_CONSUMER_KEY || !env.PESAPAL_CONSUMER_SECRET) {
    throw serverError("Pesapal is not configured.");
  }
  const r = await pesapalFetch<TokenResponse>(
    `${pesapalBase(env)}/api/Auth/RequestToken`,
    "POST",
    null,
    {
      consumer_key: env.PESAPAL_CONSUMER_KEY,
      consumer_secret: env.PESAPAL_CONSUMER_SECRET,
    }
  );
  if (!r.token) {
    console.error(`Pesapal RequestToken returned no token: ${r.message ?? "unknown"}`);
    throw new PesapalError(502, "Pesapal authentication failed");
  }
  return r.token;
}

// ---- IPN registration (one-time setup helper) -----------------------------

interface RegisterIpnResponse {
  ipn_id?: string;
  url?: string;
  error?: unknown;
  status?: string;
}

export async function registerIpn(
  env: PesapalEnv,
  token: string,
  url: string,
  notificationType: "GET" | "POST" = "POST"
): Promise<RegisterIpnResponse> {
  return pesapalFetch<RegisterIpnResponse>(
    `${pesapalBase(env)}/api/URLSetup/RegisterIPN`,
    "POST",
    token,
    { url, ipn_notification_type: notificationType }
  );
}

// ---- submit order ---------------------------------------------------------

export interface SubmitOrderInput {
  merchantReference: string; // our pesapal_orders.id
  currency: string;
  amount: number; // MAJOR units (e.g. 12900.00), not minor
  description: string;
  callbackUrl: string;
  cancellationUrl?: string;
  email: string;
  firstName: string;
  lastName: string;
  countryCode?: string | null; // ISO 3166-1 alpha-2
}

export interface SubmitOrderResult {
  order_tracking_id: string;
  merchant_reference: string;
  redirect_url: string;
}

export async function submitOrder(
  env: PesapalEnv,
  token: string,
  input: SubmitOrderInput
): Promise<SubmitOrderResult> {
  if (!env.PESAPAL_IPN_ID) throw serverError("Pesapal IPN is not configured.");
  const body = {
    id: input.merchantReference,
    currency: input.currency,
    amount: input.amount,
    description: input.description,
    callback_url: input.callbackUrl,
    cancellation_url: input.cancellationUrl,
    notification_id: env.PESAPAL_IPN_ID,
    billing_address: {
      email_address: input.email,
      first_name: input.firstName,
      last_name: input.lastName,
      country_code: input.countryCode || undefined,
    },
  };
  const r = await pesapalFetch<
    SubmitOrderResult & { error?: unknown; status?: string }
  >(`${pesapalBase(env)}/api/Transactions/SubmitOrderRequest`, "POST", token, body);
  if (!r.redirect_url || !r.order_tracking_id) {
    console.error(`Pesapal SubmitOrderRequest incomplete: ${JSON.stringify(r.error ?? r)}`);
    throw new PesapalError(502, "Pesapal could not start checkout");
  }
  return {
    order_tracking_id: r.order_tracking_id,
    merchant_reference: r.merchant_reference,
    redirect_url: r.redirect_url,
  };
}

// ---- transaction status ---------------------------------------------------

export interface TransactionStatus {
  payment_method?: string;
  amount?: number;
  created_date?: string;
  confirmation_code?: string;
  payment_status_description?: string; // COMPLETED | FAILED | REVERSED | INVALID
  description?: string;
  message?: string;
  payment_account?: string;
  status_code?: number; // 0 INVALID | 1 COMPLETED | 2 FAILED | 3 REVERSED
  merchant_reference?: string;
  currency?: string;
  status?: string;
}

export async function getTransactionStatus(
  env: PesapalEnv,
  token: string,
  orderTrackingId: string
): Promise<TransactionStatus> {
  const url = `${pesapalBase(env)}/api/Transactions/GetTransactionStatus?orderTrackingId=${encodeURIComponent(
    orderTrackingId
  )}`;
  return pesapalFetch<TransactionStatus>(url, "GET", token);
}

// status_code 1 (or description COMPLETED) is the only success state.
export function isCompleted(s: TransactionStatus): boolean {
  if (s.status_code === 1) return true;
  return (s.payment_status_description || "").toUpperCase() === "COMPLETED";
}

// Normalise a Pesapal status into our pesapal_orders.status vocabulary.
export function normalizeStatus(s: TransactionStatus): "completed" | "failed" | "invalid" | "reversed" | "pending" {
  switch (s.status_code) {
    case 1:
      return "completed";
    case 2:
      return "failed";
    case 3:
      return "reversed";
    case 0:
      return "invalid";
    default:
      break;
  }
  const d = (s.payment_status_description || "").toUpperCase();
  if (d === "COMPLETED") return "completed";
  if (d === "FAILED") return "failed";
  if (d === "REVERSED") return "reversed";
  if (d === "INVALID") return "invalid";
  return "pending";
}
