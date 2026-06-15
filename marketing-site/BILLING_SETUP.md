# Planscape Billing (B3a — Stripe core) — setup & smoke test

The Stripe billing core: a tenant owner can subscribe to a plan, upgrade /
downgrade, cancel / resume, and the system stays in sync with Stripe via a
signature-verified webhook. Runs entirely on **Cloudflare Pages Functions + D1**
(Web Crypto only — no Node APIs, no `stripe` SDK). Prices are **inline**
(`price_data` at checkout) — there are no pre-created Stripe Price IDs to manage.

Endpoints live under `functions/api/billing/` + `functions/api/webhooks/stripe.ts`;
shared utilities under `functions/api/_lib/billing/` (`catalog`, `pricing`,
`stripe`, `state`). Auth/CORS/idempotency reuse the existing
`functions/api/auth/_lib/` helpers.

---

## 1. Environment variables

Set these in the Cloudflare dashboard:
**Pages → planscape-marketing → Settings → Environment variables (Production)**.
Both are **encrypted** (Secret). Already configured in this project.

| Name | Type | How to get it | Used for |
|---|---|---|---|
| `STRIPE_SECRET_KEY` | Secret | Stripe dashboard → Developers → API keys (`sk_test_…` / `sk_live_…`) | All `api.stripe.com` calls |
| `STRIPE_WEBHOOK_SECRET` | Secret | Stripe dashboard → Developers → Webhooks → (your endpoint) → Signing secret (`whsec_…`) | HMAC-verifying the webhook |

`APP_ORIGIN` (already set for B1) is reused to build the Checkout `success_url` /
`cancel_url` and the Customer Portal `return_url`.

> If `STRIPE_SECRET_KEY` is unset, the mutating billing endpoints return
> `500 "Billing is not configured."` rather than throwing — so a misconfigured
> preview never half-creates a subscription.

---

## 2. Apply the schema migration

B3a appends three tables — `subscriptions`, `invoices`, `webhooks_log` — to the
existing `schema.sql`. Every `CREATE` is idempotent (`IF NOT EXISTS`), so
re-running is safe.

```bash
cd marketing-site
wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
# or: npm run schema:remote
```

### One-time ALTER for `tenants.stripe_customer_id`

B3a adds one column to the existing `tenants` table. SQLite can't guard
`ADD COLUMN` with `IF NOT EXISTS`, so run this **once** and ignore a
"duplicate column name" error (it means you already have it):

```bash
wrangler d1 execute planscape-waitlist --remote \
  --command="ALTER TABLE tenants ADD COLUMN stripe_customer_id TEXT;"
```

Verify the tables exist:

```bash
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
# expect: …, invoices, subscriptions, webhooks_log, …
```

---

## 3. Stripe webhook endpoint registration (recap — already done)

In the Stripe dashboard: **Developers → Webhooks → Add endpoint**.

- **Endpoint URL:** `https://planscape.build/api/webhooks/stripe`
- **Events to send** (exactly these six, case-sensitive):
  - `checkout.session.completed`
  - `customer.subscription.created`
  - `customer.subscription.updated`
  - `customer.subscription.deleted`
  - `invoice.paid`
  - `invoice.payment_failed`
- Copy the endpoint's **Signing secret** (`whsec_…`) into `STRIPE_WEBHOOK_SECRET`.

The handler **persists every received event** to `webhooks_log` (valid signature
or not), **rejects a bad signature with `400`** (no detail leaked), **dedupes on
the Stripe `Event.id`** (a re-delivered, already-processed event returns
`200 {received:true,duplicate:true}` without re-running), and returns `500` on a
processing error so Stripe retries (the row's `error` status re-allows reprocess).

> Local testing: `stripe listen --forward-to localhost:8788/api/webhooks/stripe`
> prints a `whsec_…` to drop into `.dev.vars`, then `stripe trigger
> checkout.session.completed` (etc.).

---

## 4. Deploy

```bash
cd marketing-site
wrangler pages deploy . --project-name=planscape-marketing --branch=main --commit-dirty=true
# or: npm run deploy
```

---

## 5. Smoke test (the B3a done-criteria)

Use Stripe **test mode**. Test cards: success `4242 4242 4242 4242`, failure
`4000 0000 0000 0341` (any future expiry, any CVC, any postcode). Sign up + log
in first (see `AUTH_SETUP.md`) to get an **owner** JWT — billing mutations are
owner-only; invoice reads are admin+.

```bash
BASE=https://planscape.build
JWT="<owner access token>"

# 1. SUBSCRIBE — create a Checkout Session, then complete it in the browser.
curl -s -X POST $BASE/api/billing/checkout \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -H "Idempotency-Key: smoke-checkout-1" \
  -d '{"product":"sting-tools","tier":"solo","cycle":"monthly","currency":"USD"}'
#   → 200 { checkoutUrl, sessionId, amountCents:2500, currency:"USD", cycle:"monthly" }
#   Open checkoutUrl, pay with 4242 4242 4242 4242.
#   → Stripe fires checkout.session.completed → /api/webhooks/stripe:
#       subscriptions row created (status=active),
#       tenants.subscription_status='active', tenants.stripe_customer_id populated.
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT tier,status,amount_cents FROM subscriptions; SELECT subscription_status,stripe_customer_id FROM tenants;"

# 2. UPGRADE Solo → Studio (mid-cycle, prorated charge — Stripe does the math).
curl -s -X POST $BASE/api/billing/subscription/change-plan \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d '{"product":"sting-tools","tier":"studio"}'
#   → 200 { tier:"studio", prorated:true, effective:"immediate", amountCents:9000 }
#   subscriptions row updated to studio; an invoice for the proration is raised.

# 3. DOWNGRADE Studio → Solo (deferred — applies next renewal, no mid-cycle credit).
curl -s -X POST $BASE/api/billing/subscription/change-plan \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d '{"tier":"solo"}'
#   → 200 { tier:"solo", prorated:false, effective:"next_period" }
#   subscriptions.cancel_at_period_end stays 0; next invoice reflects Solo pricing.

# 4. CANCEL — schedule cancellation at period end.
curl -s -X POST $BASE/api/billing/subscription/cancel -H "Authorization: Bearer $JWT"
#   → 200 { ok:true, cancelAtPeriodEnd:true, currentPeriodEnd }

# 5. RESUME — clear the pending cancellation.
curl -s -X POST $BASE/api/billing/subscription/resume -H "Authorization: Bearer $JWT"
#   → 200 { ok:true, cancelAtPeriodEnd:false }

# 6. FAILED PAYMENT → past_due (→ read_only after Stripe exhausts retries).
#   In the Customer Portal (POST /api/billing/portal → portalUrl) swap the card to
#   4000 0000 0000 0341, OR `stripe trigger invoice.payment_failed`.
#   → invoice.payment_failed webhook: tenants.subscription_status='past_due',
#     audit 'payment.failed'. When Stripe gives up retrying, the subscription goes
#     'unpaid' → customer.subscription.updated maps it to 'read_only'.
#   Recovery: a later invoice.paid (or subscription back to active) flips the tenant
#     to 'active' and logs 'payment.recovered'.
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT subscription_status FROM tenants; SELECT action,target FROM audit_log ORDER BY id DESC LIMIT 8;"

# Idempotency check — repeat step 1 with the SAME Idempotency-Key:
curl -s -X POST $BASE/api/billing/checkout \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -H "Idempotency-Key: smoke-checkout-1" \
  -d '{"product":"sting-tools","tier":"solo","cycle":"monthly","currency":"USD"}'
#   → SAME checkoutUrl as step 1 (header Idempotency-Replayed:true), no new session.

# Invoices (admin+): list + redirect-to-PDF.
curl -s "$BASE/api/billing/invoices?limit=10" -H "Authorization: Bearer $JWT"
curl -sI "$BASE/api/billing/invoices/<invoiceId>/pdf" -H "Authorization: Bearer $JWT"
#   → 302 Location: https://…stripe…/invoice.pdf
```

When steps 1–6 behave as annotated, **B3a is done**.

---

## Endpoint reference

| Method | Path | Role | Purpose |
|---|---|---|---|
| GET | `/api/billing/plans` | public | Plan catalog + computed prices (USD/EUR/GBP, monthly/annual) |
| POST | `/api/billing/checkout` | owner | Create Checkout Session (inline `price_data`); honours `Idempotency-Key` |
| POST | `/api/billing/portal` | owner | Create Stripe Customer Portal session |
| POST | `/api/billing/subscription/cancel` | owner | `cancel_at_period_end = 1` |
| POST | `/api/billing/subscription/resume` | owner | `cancel_at_period_end = 0` |
| POST | `/api/billing/subscription/change-plan` | owner | Upgrade (prorated, immediate) / downgrade (deferred to next period) |
| GET | `/api/billing/invoices` | admin+ | Paginated invoice list |
| GET | `/api/billing/invoices/:id/pdf` | admin+ | 302-redirect to the Stripe-hosted PDF |
| POST | `/api/webhooks/stripe` | Stripe (HMAC) | Signature-verified, idempotent event sink |

## Pricing model

| | |
|---|---|
| Catalog | `_lib/billing/catalog.ts` — `PLAN_CATALOG` (USD/month + seat caps), single source of truth, mirrors `pricing.html` + `limits.ts` |
| Currencies (B3a) | USD, EUR, GBP via the pegged `FX_FROM_USD` table (refreshed quarterly, **not** at checkout — predictable prices). Others → `400 "currency not yet supported on Stripe"` |
| Annual | `unitAmount = round(usdMonthly · fx · 100) · 12 · (1 − 0.2)` — 20 % off the monthly equivalent |
| Enterprise | `usdMonthly: null` → `400` "contact sales" (no self-serve checkout) |

## State mapping (Stripe → tenant.subscription_status)

`active`/`trialing` → `active` · `past_due` → `past_due` · `unpaid` →
`read_only` · `canceled` → `cancelled` · `incomplete*` → `past_due`. Audit
actions emitted: `subscription.activated` / `.cancelled` / `.resumed` /
`.upgraded` / `.downgraded`, `payment.failed`, `payment.recovered`.

---

## Out of scope for B3a — deferred to B3b

B3a is **Stripe core only**. The following are explicitly **not** in this phase:

- **Pesapal** — the second (Africa-facing) payment rail. The `subscriptions` /
  `invoices` schema already carries a generic `provider` column so B3b adds
  `provider='pesapal'` rows alongside Stripe with no migration.
- **Cron Worker** — nightly `idempotency_keys` / expired-trial sweep and
  proactive `past_due → read_only` enforcement (B3a relies on Stripe webhooks +
  lazy `/me` expiry).
- **Tax** — VAT / sales-tax computation and the `invoices.tax_cents` /
  `tax_label` population (the columns ship now, populated B3b).
- **Discount codes** — promo / coupon handling at checkout.
