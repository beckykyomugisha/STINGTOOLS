// /api/admin/discount-codes — create + list discount codes. Platform-level, not
// tenant-scoped, so it's guarded by the X-Admin-Key header matching ADMIN_API_KEY
// (a stopgap until B5 ships a real platform-admin model). If ADMIN_API_KEY is
// unset the endpoint is closed.
//   POST  { code, percentOff? | amountOffCents?+currency?, appliesTo?, maxRedemptions?, expiresAt? }
//   GET   → { codes: [...] }

import { withHandler, readJson } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { bad, conflict, forbidden } from "../auth/_lib/errors";
import { uuid } from "../auth/_lib/tokens";
import { normalizeCode, getDiscountByCode, type DiscountRow } from "../_lib/billing/discounts";
import { isStripeCurrency, isPesapalCurrency } from "../_lib/billing/catalog";
import type { Env } from "../auth/_lib/types";

function requireAdmin(request: Request, env: Env): void {
  if (!env.ADMIN_API_KEY) throw forbidden("Admin API is not configured.");
  const key = request.headers.get("X-Admin-Key") || "";
  if (key !== env.ADMIN_API_KEY) throw forbidden("Invalid admin key.");
}

interface CreateBody {
  code?: string;
  percentOff?: number;
  amountOffCents?: number;
  currency?: string;
  appliesTo?: string;
  maxRedemptions?: number;
  expiresAt?: string;
  createdBy?: string;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestPost = withHandler(async ({ request, env }) => {
  requireAdmin(request, env);
  const body = await readJson<CreateBody>(request);

  const code = normalizeCode(body.code || "");
  if (!code) throw bad("A code is required.");

  const hasPercent = typeof body.percentOff === "number";
  const hasAmount = typeof body.amountOffCents === "number";
  if (hasPercent === hasAmount) {
    throw bad("Provide exactly one of percentOff or amountOffCents.");
  }

  let percentOff: number | null = null;
  let amountOffCents: number | null = null;
  let currency: string | null = null;

  if (hasPercent) {
    const p = Math.round(body.percentOff as number);
    if (p < 1 || p > 100) throw bad("percentOff must be between 1 and 100.");
    percentOff = p;
  } else {
    const a = Math.round(body.amountOffCents as number);
    if (a < 1) throw bad("amountOffCents must be a positive integer.");
    const cur = (body.currency || "").trim().toUpperCase();
    if (!cur || (!isStripeCurrency(cur) && !isPesapalCurrency(cur))) {
      throw bad("amountOffCents requires a supported currency.");
    }
    amountOffCents = a;
    currency = cur;
  }

  const appliesTo = body.appliesTo ? body.appliesTo.trim() : null;
  const maxRedemptions =
    typeof body.maxRedemptions === "number" && body.maxRedemptions > 0
      ? Math.round(body.maxRedemptions)
      : null;
  const expiresAt = body.expiresAt ? new Date(body.expiresAt).toISOString() : null;

  const existing = await getDiscountByCode(env.WAITLIST_DB, code);
  if (existing) throw conflict("A discount code with that name already exists.");

  const id = uuid();
  await env.WAITLIST_DB.prepare(
    `INSERT INTO discount_codes
       (id, code, percent_off, amount_off_cents, currency, applies_to,
        max_redemptions, redeemed_count, expires_at, active, created_at, created_by)
     VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`
  )
    .bind(
      id,
      code,
      percentOff,
      amountOffCents,
      currency,
      appliesTo,
      maxRedemptions,
      0,
      expiresAt,
      1,
      new Date().toISOString(),
      body.createdBy || "admin"
    )
    .run();

  return { id, code, percentOff, amountOffCents, currency, appliesTo, maxRedemptions, expiresAt };
});

export const onRequestGet = withHandler(async ({ request, env }) => {
  requireAdmin(request, env);
  const res = await env.WAITLIST_DB.prepare(
    `SELECT * FROM discount_codes ORDER BY created_at DESC LIMIT 200`
  ).all<DiscountRow>();
  return { codes: res.results ?? [] };
});
