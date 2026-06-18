// Discount codes — validation, application, and redemption tracking. Codes
// reduce the CHARGED amount directly (inline Stripe price / Pesapal order), so
// there are no Stripe Coupon objects to keep in sync. Redemption is recorded at
// payment success (not at checkout creation) so abandoned checkouts don't burn a
// code; the UNIQUE(code, tenant_id) index makes recording idempotent on retry.

import { uuid } from "../../auth/_lib/tokens";

export interface DiscountRow {
  id: string;
  code: string;
  percent_off: number | null;
  amount_off_cents: number | null;
  currency: string | null;
  applies_to: string | null; // null = any | 'product' | 'product:tier'
  max_redemptions: number | null;
  redeemed_count: number;
  expires_at: string | null;
  active: number;
  created_at: string;
  created_by: string | null;
}

export function normalizeCode(code: string): string {
  return code.trim().toUpperCase().slice(0, 64);
}

export async function getDiscountByCode(
  db: D1Database,
  code: string
): Promise<DiscountRow | null> {
  return db
    .prepare(`SELECT * FROM discount_codes WHERE code = ?`)
    .bind(normalizeCode(code))
    .first<DiscountRow>();
}

export interface DiscountContext {
  product: string;
  tier: string;
  currency: string;
  tenantId: string;
}

export type DiscountValidation =
  | { ok: true; discount: DiscountRow }
  | { ok: false; error: string };

// Validate a code against a checkout context. Pure read — the authoritative
// double-redemption guard is the UNIQUE index applied in recordRedemption.
export async function validateDiscount(
  db: D1Database,
  code: string,
  ctx: DiscountContext
): Promise<DiscountValidation> {
  const d = await getDiscountByCode(db, code);
  if (!d || d.active !== 1) return { ok: false, error: "Unknown or inactive discount code." };

  if (d.expires_at && new Date(d.expires_at).getTime() <= Date.now()) {
    return { ok: false, error: "This discount code has expired." };
  }
  if (d.max_redemptions !== null && d.redeemed_count >= d.max_redemptions) {
    return { ok: false, error: "This discount code has reached its redemption limit." };
  }
  if (d.applies_to) {
    const matchProduct = d.applies_to === ctx.product;
    const matchTier = d.applies_to === `${ctx.product}:${ctx.tier}`;
    if (!matchProduct && !matchTier) {
      return { ok: false, error: "This discount code doesn't apply to the selected plan." };
    }
  }
  if (d.amount_off_cents !== null) {
    if (!d.currency || d.currency.toUpperCase() !== ctx.currency.toUpperCase()) {
      return { ok: false, error: "This discount code is for a different currency." };
    }
  }

  // Already redeemed by this tenant?
  const prior = await db
    .prepare(`SELECT 1 AS n FROM discount_redemptions WHERE code = ? AND tenant_id = ?`)
    .bind(d.code, ctx.tenantId)
    .first<{ n: number }>();
  if (prior) return { ok: false, error: "You've already used this discount code." };

  return { ok: true, discount: d };
}

// Apply a validated discount to a charged amount (minor units). Never returns a
// negative; the caller rejects a result < 1 (a code can't make the order free).
export function applyDiscountMinor(amountMinor: number, d: DiscountRow): number {
  if (d.percent_off !== null) {
    const pct = Math.max(0, Math.min(100, d.percent_off));
    return Math.round(amountMinor * (1 - pct / 100));
  }
  if (d.amount_off_cents !== null) {
    return Math.max(0, amountMinor - d.amount_off_cents);
  }
  return amountMinor;
}

// Record a redemption at payment success. Idempotent: the UNIQUE(code, tenant)
// index makes the INSERT a no-op on retry, and redeemed_count is only bumped
// when a fresh row was actually written.
export async function recordRedemption(
  db: D1Database,
  code: string,
  tenantId: string,
  provider: string,
  reference: string | null
): Promise<void> {
  const normalized = normalizeCode(code);
  const res = await db
    .prepare(
      `INSERT OR IGNORE INTO discount_redemptions
         (id, code, tenant_id, provider, reference, created_at)
       VALUES (?,?,?,?,?,?)`
    )
    .bind(uuid(), normalized, tenantId, provider, reference, new Date().toISOString())
    .run();
  if (res.meta && res.meta.changes && res.meta.changes > 0) {
    await db
      .prepare(`UPDATE discount_codes SET redeemed_count = redeemed_count + 1 WHERE code = ?`)
      .bind(normalized)
      .run();
  }
}
