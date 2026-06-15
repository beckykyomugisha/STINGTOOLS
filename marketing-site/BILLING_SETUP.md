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

## Delivered in B3b (see below)

The B3a "deferred" list — **Pesapal**, the **cron Worker**, **tax**, and
**discount codes** — all land in B3b. See the next section.

---

# Planscape Billing (B3b — Pesapal + cron + tax + discounts)

B3b adds the Africa-facing payment rail and the lifecycle automation around it.
A **currency router** sends each checkout to the right provider; a **cron Worker**
handles trial expiry / reminders / dunning / digest / cleanup; a **tax table**
breaks out VAT (or a reverse-charge line) per country; **discount codes** apply at
checkout. The `subscriptions` / `invoices` / `webhooks_log` tables from B3a are
reused with `provider='pesapal'` — no destructive migration.

> **Not Flutterwave.** Uganda self-serve onboarding is broken in Flutterwave's
> dashboard, so B3b uses **Pesapal V3**. NGN/ZAR are deferred (later expansion).

## Currency router

| Currency | Provider | Notes |
|---|---|---|
| USD / EUR / GBP | **Stripe** | Existing B3a route (2-decimal) |
| UGX / KES / TZS / RWF | **Pesapal** | UGX/RWF are zero-decimal (see `CURRENCY_MINOR_EXP`) |
| NGN / ZAR | _deferred_ | `400 "Currency not supported."` until a later phase |

`providerForCurrency()` in `_lib/billing/catalog.ts` does the routing; the
checkout endpoint branches on it. FX pegs (`FX_FROM_USD_PESAPAL`) are approximate
and **refreshed quarterly** alongside `FX_FROM_USD`.

## 1. New environment variables

Add to **Pages → planscape-marketing → Settings → Environment variables**:

| Name | Type | How to get it | Used for |
|---|---|---|---|
| `PESAPAL_CONSUMER_KEY` | Secret | Pesapal dashboard → API | RequestToken |
| `PESAPAL_CONSUMER_SECRET` | Secret | Pesapal dashboard → API | RequestToken |
| `PESAPAL_BASE_URL` | Plain | `https://cybqa.pesapal.com/pesapalv3` (sandbox) / `https://pay.pesapal.com/v3` (prod) | API base |
| `PESAPAL_IPN_ID` | Plain | **RegisterIPN response** — see step 2 below | `notification_id` on SubmitOrder |
| `ADMIN_API_KEY` | Secret | Any 32+ random bytes (`openssl rand -base64 32`) | Guards `/api/admin/*` until B5 |

For the **cron Worker** (`marketing-site-cron`, separate project) set via
`npx wrangler secret put …`:

| Name | Type | Used for |
|---|---|---|
| `RESEND_API_KEY` | Secret | Lifecycle email (reminders, dunning) |
| `SIGNUP_DIGEST_WEBHOOK` | Secret (optional) | 06:00 UTC Slack/Discord digest |
| `ADMIN_API_KEY` | Secret (optional) | Guards the manual `/run?job=` trigger |

> `PESAPAL_IPN_ID` and `ADMIN_API_KEY` are **new required** vars not present in
> B3a. The Pesapal checkout path returns `500 "Mobile-money billing is not
> configured."` if `PESAPAL_CONSUMER_KEY` or `PESAPAL_IPN_ID` is missing, so a
> half-configured deploy never half-creates a Pesapal order.

## 2. Register the Pesapal IPN (one-time → get `PESAPAL_IPN_ID`)

The IPN URL `https://planscape.build/api/webhooks/pesapal` is already registered,
but SubmitOrder needs the **`ipn_id`** that RegisterIPN returned. If you don't
have it, fetch it once:

```bash
BASE=https://cybqa.pesapal.com/pesapalv3   # prod: https://pay.pesapal.com/v3
TOKEN=$(curl -s -X POST $BASE/api/Auth/RequestToken -H "Content-Type: application/json" \
  -d '{"consumer_key":"…","consumer_secret":"…"}' | jq -r .token)

curl -s -X POST $BASE/api/URLSetup/RegisterIPN -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"url":"https://planscape.build/api/webhooks/pesapal","ipn_notification_type":"POST"}'
#   → { "ipn_id":"<uuid>", "url":"…", "ipn_notification_type":"POST", … }
```

Put the returned `ipn_id` into `PESAPAL_IPN_ID`. (The same call is also exposed
programmatically as `registerIpn()` in `_lib/billing/pesapal.ts`.)

## 3. Apply the schema migration

B3b appends three tables — `pesapal_orders`, `discount_codes`,
`discount_redemptions` — to `schema.sql`. Every `CREATE` is idempotent; **no
ALTERs** (additive only). Re-running is safe.

```bash
cd marketing-site
npm run schema:remote     # wrangler d1 execute … --remote --file=./functions/api/schema.sql
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
#   expect: …, discount_codes, discount_redemptions, pesapal_orders, …
```

## 4. Pesapal security model — no inbound HMAC

Pesapal's callback + IPN carry **only an `OrderTrackingId`, never the payment
status**. The webhook therefore re-fetches the authoritative status with
`GetTransactionStatus` (authenticated with our own token). Confirmation == the
re-query, so there is nothing to HMAC-verify on the inbound call. `webhooks_log`
records each IPN (`provider='pesapal'`, `event_id=OrderTrackingId`), dedupes on
the tracking id, and the handler replies with the JSON ack Pesapal expects
(`{orderNotificationType, orderTrackingId, orderMerchantReference, status:200}`).

## 5. Tax (`_lib/billing/tax.ts`) — hardcoded, **review quarterly**

Prices are treated as **tax-inclusive**: the charged total never changes, we only
break out the embedded component (`subtotal = round(total/(1+rate))`,
`tax = total − subtotal`). Applied to **both** providers at invoice creation,
keyed on `tenants.country`.

| Country | Treatment | Invoice line |
|---|---|---|
| UG | 18% VAT (inclusive) | `UG VAT 18%` |
| KE / TZ / RW / NG / ZA | B2B reverse charge, 0 tax cents | `Reverse charge — declare to KRA/TRA/RRA/FIRS/SARS` |
| GB + EU-27 | VAT MOSS at the country rate (inclusive) | e.g. `DE VAT 19%` |
| US / CA / AU / other | 0% | _(no line)_ |

> **Quarterly review** (next: align with each calendar quarter). Re-check the EU
> standard-rate table + the UG rate against the revenue authorities and update
> `INCLUSIVE_VAT_RATES` / `REVERSE_CHARGE_AUTHORITY` in `tax.ts`. The FX pegs in
> `catalog.ts` get the same quarterly refresh.

## 6. Discount codes

`discount_codes` (`percent_off` XOR `amount_off_cents`+`currency`, `applies_to`,
`max_redemptions`, `redeemed_count`, `expires_at`). Checkout accepts
`?discount=CODE` (or `{"discount":"CODE"}` in the body), validates, and applies it:

- **Stripe** → a one-time `duration:'once'` **coupon** (so renewals bill full price).
- **Pesapal** → the order amount is reduced directly (one payment per period).

Redemption is recorded at **payment success** (not checkout creation) so abandoned
checkouts don't burn a code; `UNIQUE(code, tenant_id)` makes recording idempotent
on webhook retry and bumps `redeemed_count` only on a fresh insert.

**Admin endpoint** (guarded by `X-Admin-Key: $ADMIN_API_KEY` until B5):

```bash
curl -s -X POST $BASE/api/admin/discount-codes -H "X-Admin-Key: $ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"code":"LAUNCH20","percentOff":20,"maxRedemptions":100,"appliesTo":"sting-tools"}'
curl -s $BASE/api/admin/discount-codes -H "X-Admin-Key: $ADMIN_API_KEY"   # list
```

## 7. Cron Worker (`marketing-site-cron`)

A **standalone Worker** (not part of the Pages project) binding the **same
`WAITLIST_DB`**. Deploy:

```bash
cd marketing-site-cron
npm install
npx wrangler secret put RESEND_API_KEY
npx wrangler secret put SIGNUP_DIGEST_WEBHOOK   # optional
npx wrangler secret put ADMIN_API_KEY           # optional (manual trigger)
npx wrangler deploy
```

Schedule (UTC) — dispatched by cron expression in `scheduled()`:

| Cron | Job | Action |
|---|---|---|
| `0 * * * *` | `expireTrials` | `trial` + `trial_ends_at < now` → `read_only` + email |
| `0 9 * * *` | `daily09` | T-7/T-3/T-1 trial reminders · cap-exceeded reminders · dunning (past_due > 7d → read_only) |
| `0 6 * * *` | `nightlyDigest` | 24h signups / subs / invoices / waitlist → `SIGNUP_DIGEST_WEBHOOK` |
| `0 3 * * *` | `cleanup` | prune expired sessions + idempotency_keys; archive `webhooks_log` > 90d |

Manual trigger for testing (needs `ADMIN_API_KEY`):

```bash
curl -s "https://planscape-cron.<account>.workers.dev/run?job=cleanup" -H "X-Admin-Key: $ADMIN_API_KEY"
curl -s "https://planscape-cron.<account>.workers.dev/run?job=expireTrials" -H "X-Admin-Key: $ADMIN_API_KEY"
# jobs: expireTrials | daily09 | trialReminders | capReminders | dunning | nightlyDigest | cleanup
```

## 8. Smoke test (B3b done-criteria)

```bash
BASE=https://planscape.build
JWT="<owner access token>"

# 1. Pesapal checkout (UGX) → returns a Pesapal redirect_url + a pending order.
curl -s -X POST $BASE/api/billing/checkout \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d '{"product":"sting-tools","tier":"solo","cycle":"monthly","currency":"UGX"}'
#   → 200 { provider:"pesapal", checkoutUrl:"https://…pesapal…", orderTrackingId, amountCents, currency:"UGX" }
#   Open checkoutUrl, pay with a sandbox mobile-money/card. Pesapal fires the IPN:
#   → /api/webhooks/pesapal re-queries GetTransactionStatus → COMPLETED →
#     subscriptions(provider=pesapal,status=active) + invoices(status=paid,tax_label set) +
#     tenants.subscription_status='active'.
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT provider,status,currency FROM subscriptions; SELECT status,tax_label,total_cents FROM invoices ORDER BY created_at DESC LIMIT 3;"

# 2. Discount: create a code, then check it reduces the charged amount.
curl -s -X POST $BASE/api/admin/discount-codes -H "X-Admin-Key: $ADMIN_API_KEY" \
  -H "Content-Type: application/json" -d '{"code":"SAVE10","percentOff":10}'
curl -s -X POST $BASE/api/billing/checkout -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json" \
  -d '{"product":"sting-tools","tier":"solo","cycle":"monthly","currency":"UGX","discount":"SAVE10"}'
#   → amountCents 10% lower than step 1. On payment success, discount_redemptions
#     gets a row and discount_codes.redeemed_count increments.

# 3. Tax line on a UG invoice (set the tenant country to UG first if needed):
wrangler d1 execute planscape-waitlist --remote \
  --command="SELECT number,subtotal_cents,tax_cents,tax_label,total_cents FROM invoices ORDER BY created_at DESC LIMIT 1;"
#   → tax_label='UG VAT 18%', subtotal+tax == total.

# 4. Cron: trigger cleanup + expireTrials manually and confirm counts.
curl -s "https://planscape-cron.<account>.workers.dev/run?job=cleanup" -H "X-Admin-Key: $ADMIN_API_KEY"
```

When 1–4 behave as annotated, **B3b is done**.

## B3b endpoint / file reference

| Method | Path | Role | Purpose |
|---|---|---|---|
| POST | `/api/billing/checkout` | owner | Routed by currency (Stripe **or** Pesapal); accepts `discount` |
| POST/GET | `/api/webhooks/pesapal` | Pesapal (re-query) | IPN sink — confirms via GetTransactionStatus |
| POST | `/api/admin/discount-codes` | `X-Admin-Key` | Create a discount code |
| GET | `/api/admin/discount-codes` | `X-Admin-Key` | List discount codes |

| File | Role |
|---|---|
| `_lib/billing/pesapal.ts` | Pesapal V3 client (token, RegisterIPN, SubmitOrder, GetTransactionStatus) |
| `_lib/billing/tax.ts` | Country tax table (inclusive VAT / reverse charge) |
| `_lib/billing/discounts.ts` | Validate / apply / record-redemption |
| `marketing-site-cron/` | Standalone cron Worker (separate `wrangler.toml`, same D1) |
