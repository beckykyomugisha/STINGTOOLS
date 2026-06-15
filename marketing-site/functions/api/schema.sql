-- D1 schema for the Planscape waitlist.
-- Apply once with:
--   wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
-- Re-running is safe: every statement is idempotent.

CREATE TABLE IF NOT EXISTS waitlist (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  email         TEXT    NOT NULL UNIQUE,
  first_name    TEXT    NOT NULL,
  last_name     TEXT    NOT NULL,
  firm          TEXT    NOT NULL,
  product       TEXT    NOT NULL,           -- sting-tools | planscape | both
  team_size     TEXT    NOT NULL,           -- 1-3 | 4-10 | 11-25 | 26-50 | 51-100 | 100+
  country       TEXT,
  role          TEXT,
  notes         TEXT,
  referrer      TEXT,
  utm           TEXT,
  ip            TEXT,
  user_agent    TEXT,
  submitted_at  TEXT    NOT NULL,           -- ISO 8601 UTC
  updated_at    TEXT,                       -- set on re-submission
  invited_at    TEXT,                       -- set when you flip them to active trial
  status        TEXT    NOT NULL DEFAULT 'waitlist'  -- waitlist | invited | converted | declined
);

CREATE INDEX IF NOT EXISTS idx_waitlist_submitted_at ON waitlist(submitted_at);
CREATE INDEX IF NOT EXISTS idx_waitlist_country      ON waitlist(country);
CREATE INDEX IF NOT EXISTS idx_waitlist_product      ON waitlist(product);
CREATE INDEX IF NOT EXISTS idx_waitlist_status       ON waitlist(status);

-- ---------------------------------------------------------------------------
-- Auth core (B1) — tenants / users / sessions
-- Sits alongside the waitlist table in the same `planscape-waitlist` D1 DB.
-- Apply with:
--   wrangler d1 execute planscape-waitlist --remote --file=./functions/api/schema.sql
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS tenants (
  id                   TEXT    PRIMARY KEY,            -- uuid v4
  name                 TEXT    NOT NULL,
  slug                 TEXT    NOT NULL UNIQUE,        -- lowercase-hyphenated, derived from name
  country              TEXT,                           -- ISO 3166-1 alpha-2
  currency             TEXT    NOT NULL DEFAULT 'USD',
  plan_product         TEXT,                           -- sting-tools | planscape | null while in trial
  plan_tier            TEXT,                           -- solo | studio | practice | firm | large | enterprise
  subscription_status  TEXT    NOT NULL DEFAULT 'trial', -- trial | active | past_due | read_only | cancelled
  trial_started_at     TEXT    NOT NULL,               -- ISO 8601 UTC
  trial_ends_at        TEXT    NOT NULL,               -- ISO 8601 UTC, +14 days
  cap_exceeded_since   TEXT,                            -- (B2) ISO when seat count first went over plan cap; null when under
  created_at           TEXT    NOT NULL,
  updated_at           TEXT
);

CREATE TABLE IF NOT EXISTS users (
  id                          TEXT    PRIMARY KEY,     -- uuid v4
  tenant_id                   TEXT    NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  email                       TEXT    NOT NULL UNIQUE,
  password_hash               TEXT    NOT NULL,        -- pbkdf2-v1$iterations$salt_b64$hash_b64
  first_name                  TEXT    NOT NULL,
  last_name                   TEXT    NOT NULL,
  role                        TEXT    NOT NULL DEFAULT 'owner',  -- owner | admin | bim_manager | project_lead | coordinator | viewer | client
  email_verified_at           TEXT,
  email_verify_token          TEXT,                    -- random 32-byte url-safe
  email_verify_expires_at     TEXT,
  password_reset_token_hash   TEXT,                    -- SHA-256 of token (token only sent in email, never stored plain)
  password_reset_expires_at   TEXT,
  last_login_at               TEXT,
  deleted_at                  TEXT,                    -- (B2) soft-delete tombstone; contributions preserved
  created_at                  TEXT    NOT NULL,
  updated_at                  TEXT
);

CREATE TABLE IF NOT EXISTS sessions (
  id                   TEXT    PRIMARY KEY,            -- uuid v4
  user_id              TEXT    NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  refresh_token_hash   TEXT    NOT NULL UNIQUE,        -- SHA-256 of the opaque refresh token
  user_agent           TEXT,
  ip                   TEXT,
  created_at           TEXT    NOT NULL,
  last_used_at         TEXT,
  expires_at           TEXT    NOT NULL,
  revoked_at           TEXT,
  revoked_reason       TEXT                            -- rotated | replay | manual | logout | expiry | password_reset
);

-- Idempotency cache for payment-creating endpoints (B3). The helper lives in
-- functions/api/auth/_lib/idempotency.ts; the table ships now so B3 needs no
-- further migration.
CREATE TABLE IF NOT EXISTS idempotency_keys (
  key          TEXT    PRIMARY KEY,
  tenant_id    TEXT,
  endpoint     TEXT    NOT NULL,
  response     TEXT    NOT NULL,                       -- cached JSON response body
  status_code  INTEGER NOT NULL,
  created_at   TEXT    NOT NULL,
  expires_at   TEXT    NOT NULL                        -- TTL 24h; cleaned by the cron worker
);

CREATE INDEX IF NOT EXISTS idx_users_tenant      ON users(tenant_id);
CREATE INDEX IF NOT EXISTS idx_sessions_user     ON sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_sessions_expires  ON sessions(expires_at);
CREATE INDEX IF NOT EXISTS idx_tenants_status    ON tenants(subscription_status);
CREATE INDEX IF NOT EXISTS idx_idem_expires      ON idempotency_keys(expires_at);

-- ---------------------------------------------------------------------------
-- One-time migration for databases that already had the ORIGINAL B1 schema
-- applied (sessions without revoked_reason). SQLite cannot guard ADD COLUMN
-- with IF NOT EXISTS, so this is split out: run it ONCE on an existing DB and
-- ignore a "duplicate column name" error (it means you already have it). Fresh
-- databases get the column from the CREATE TABLE above and can skip this.
--   wrangler d1 execute planscape-waitlist --remote \
--     --command="ALTER TABLE sessions ADD COLUMN revoked_reason TEXT;"
-- ---------------------------------------------------------------------------


-- ---------------------------------------------------------------------------
-- Tenants + team (B2) — invitations / audit log
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS invitations (
  id                   TEXT    PRIMARY KEY,            -- uuid v4
  tenant_id            TEXT    NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  email                TEXT    NOT NULL,
  role                 TEXT    NOT NULL,               -- admin | bim_manager | project_lead | coordinator | viewer | client
  token_hash           TEXT    NOT NULL UNIQUE,        -- SHA-256 of the opaque invite token (token only sent in email)
  invited_by_user_id   TEXT    NOT NULL REFERENCES users(id),
  expires_at           TEXT    NOT NULL,               -- +7 days
  accepted_at          TEXT,
  declined_at          TEXT,
  created_at           TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_log (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  tenant_id     TEXT,
  actor_user_id TEXT,
  action        TEXT    NOT NULL,                      -- user.invited | invitation.accepted | user.role_changed | user.removed | tenant.updated | ...
  target        TEXT,                                  -- user_id | invitation_id
  metadata      TEXT,                                  -- JSON
  ip            TEXT,
  user_agent    TEXT,
  created_at    TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_invites_tenant ON invitations(tenant_id);
CREATE INDEX IF NOT EXISTS idx_invites_email  ON invitations(email);
CREATE INDEX IF NOT EXISTS idx_audit_tenant   ON audit_log(tenant_id);
CREATE INDEX IF NOT EXISTS idx_audit_action   ON audit_log(action);

-- ---------------------------------------------------------------------------
-- One-time migration for databases that already had B1 applied (before B2 added
-- tenants.cap_exceeded_since and users.deleted_at). SQLite can't guard ADD
-- COLUMN with IF NOT EXISTS — run these ONCE and ignore "duplicate column name"
-- errors. Fresh databases get the columns from the CREATE TABLEs above.
--   wrangler d1 execute planscape-waitlist --remote \
--     --command="ALTER TABLE tenants ADD COLUMN cap_exceeded_since TEXT;"
--   wrangler d1 execute planscape-waitlist --remote \
--     --command="ALTER TABLE users ADD COLUMN deleted_at TEXT;"
-- ---------------------------------------------------------------------------


-- ---------------------------------------------------------------------------
-- Billing (B3a — Stripe core) — subscriptions / invoices / webhook log
-- The subscriptions + invoices model is provider-agnostic: B3a writes
-- provider='stripe'; B3b adds provider='flutterwave' rows alongside.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS subscriptions (
  id                       TEXT PRIMARY KEY,            -- uuid v4 (our row id)
  tenant_id                TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  provider                 TEXT NOT NULL,               -- 'stripe' for B3a (B3b adds 'flutterwave')
  provider_customer_id     TEXT,
  provider_subscription_id TEXT,
  product                  TEXT NOT NULL,               -- sting-tools | planscape
  tier                     TEXT NOT NULL,               -- solo | studio | practice | firm | large | enterprise
  billing_cycle            TEXT NOT NULL,               -- 'monthly' | 'annual'
  currency                 TEXT NOT NULL,
  amount_cents             INTEGER NOT NULL,            -- unit amount in minor units for the cycle
  current_period_start     TEXT,
  current_period_end       TEXT,
  cancel_at_period_end     INTEGER NOT NULL DEFAULT 0,
  status                   TEXT NOT NULL,               -- mirrors tenant.subscription_status
  created_at               TEXT NOT NULL,
  updated_at               TEXT
);

CREATE TABLE IF NOT EXISTS invoices (
  id                  TEXT PRIMARY KEY,                 -- uuid v4 (our row id)
  tenant_id           TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  subscription_id     TEXT REFERENCES subscriptions(id),
  provider            TEXT NOT NULL,
  provider_invoice_id TEXT,
  number              TEXT NOT NULL,                    -- PS-INV-YYYY-NNNN
  currency            TEXT NOT NULL,
  subtotal_cents      INTEGER NOT NULL,
  tax_cents           INTEGER NOT NULL DEFAULT 0,
  total_cents         INTEGER NOT NULL,
  tax_label           TEXT,                             -- e.g. "UG VAT 18%" (B3b populates)
  status              TEXT NOT NULL,                    -- 'open' | 'paid' | 'void' | 'uncollectible'
  hosted_url          TEXT,
  pdf_url             TEXT,
  due_at              TEXT,
  paid_at             TEXT,
  created_at          TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS webhooks_log (
  id            TEXT    PRIMARY KEY,                    -- uuid v4 (our row id)
  provider      TEXT    NOT NULL,                       -- 'stripe'
  event_id      TEXT,                                   -- provider Event.id; null if unparseable / bad sig
  event_type    TEXT,
  signature_ok  INTEGER NOT NULL DEFAULT 0,             -- 1 = HMAC verified
  payload       TEXT    NOT NULL,                       -- raw body, persisted even on bad sig
  status        TEXT    NOT NULL,                       -- received | processed | ignored | bad_signature | error
  error         TEXT,
  created_at    TEXT    NOT NULL,
  processed_at  TEXT
);

CREATE INDEX        IF NOT EXISTS idx_subs_tenant      ON subscriptions(tenant_id);
CREATE INDEX        IF NOT EXISTS idx_subs_provider    ON subscriptions(provider_subscription_id);
CREATE INDEX        IF NOT EXISTS idx_invoices_tenant  ON invoices(tenant_id);
CREATE INDEX        IF NOT EXISTS idx_invoices_number  ON invoices(number);
-- SQLite treats NULLs as distinct, so bad-signature events (event_id NULL) all
-- coexist as separate rows; dedupe only kicks in for events with a real event_id.
CREATE UNIQUE INDEX IF NOT EXISTS idx_webhooks_event   ON webhooks_log(provider, event_id);

-- ---------------------------------------------------------------------------
-- One-time migration for databases that already had B1/B2 applied (before B3a
-- added tenants.stripe_customer_id). SQLite can't guard ADD COLUMN with IF NOT
-- EXISTS — run this ONCE and ignore a "duplicate column name" error. Fresh
-- databases get the column from... (there is no CREATE TABLE for tenants here;
-- the column is added to the existing tenants table, so on a brand-new DB you
-- still run this ALTER once after the B1 CREATE TABLE tenants above.)
--   wrangler d1 execute planscape-waitlist --remote \
--     --command="ALTER TABLE tenants ADD COLUMN stripe_customer_id TEXT;"
-- ---------------------------------------------------------------------------


-- ---------------------------------------------------------------------------
-- Billing (B3b — Pesapal + discounts) — pesapal_orders / discount_codes /
-- discount_redemptions. The subscriptions + invoices tables from B3a are
-- reused with provider='pesapal'; webhooks_log carries provider='pesapal'
-- rows (event_id = Pesapal OrderTrackingId). No ALTERs — additive only.
-- ---------------------------------------------------------------------------

-- Pending Pesapal order context, written at checkout and resolved when the IPN
-- fires. Holds everything needed to mint the subscription + invoice once the
-- authoritative GetTransactionStatus comes back COMPLETED.
CREATE TABLE IF NOT EXISTS pesapal_orders (
  id                  TEXT PRIMARY KEY,            -- uuid v4; sent to Pesapal as the merchant reference
  tenant_id           TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  order_tracking_id   TEXT,                        -- Pesapal's id, set after SubmitOrderRequest
  merchant_reference  TEXT NOT NULL,               -- == id (our reference echoed back on the IPN)
  product             TEXT NOT NULL,               -- sting-tools | planscape
  tier                TEXT NOT NULL,               -- solo | studio | ...
  billing_cycle       TEXT NOT NULL,               -- monthly | annual
  currency            TEXT NOT NULL,               -- UGX | KES | TZS | RWF
  amount_minor        INTEGER NOT NULL,            -- charged amount in minor units (discount already applied)
  discount_code       TEXT,                        -- uppercased code if one was applied
  status              TEXT NOT NULL,               -- pending | completed | failed | invalid | reversed
  created_at          TEXT NOT NULL,
  updated_at          TEXT
);

CREATE INDEX IF NOT EXISTS idx_pesapal_tracking ON pesapal_orders(order_tracking_id);
CREATE INDEX IF NOT EXISTS idx_pesapal_tenant   ON pesapal_orders(tenant_id);

-- Discount codes. percent_off XOR amount_off_cents (amount needs currency).
-- applies_to: null = any plan | 'product' | 'product:tier'. Provider-agnostic —
-- the checkout reduces the charged amount directly (inline price for Stripe,
-- order amount for Pesapal), so there are no Stripe Coupon objects to sync.
CREATE TABLE IF NOT EXISTS discount_codes (
  id                TEXT PRIMARY KEY,              -- uuid v4
  code              TEXT NOT NULL UNIQUE,          -- stored UPPER-cased; matched case-insensitively
  percent_off       INTEGER,                       -- 1..100 (null when amount_off used)
  amount_off_cents  INTEGER,                       -- minor units in `currency` (null when percent used)
  currency          TEXT,                          -- required iff amount_off_cents set
  applies_to        TEXT,                          -- null=any | 'sting-tools' | 'planscape:solo' ...
  max_redemptions   INTEGER,                       -- null = unlimited
  redeemed_count    INTEGER NOT NULL DEFAULT 0,
  expires_at        TEXT,                          -- ISO 8601 UTC; null = never expires
  active            INTEGER NOT NULL DEFAULT 1,
  created_at        TEXT NOT NULL,
  created_by        TEXT                           -- admin identifier (free text for B3b)
);

-- One redemption per (code, tenant). The UNIQUE index makes recording idempotent
-- (INSERT OR IGNORE), so a webhook retry never double-increments redeemed_count.
CREATE TABLE IF NOT EXISTS discount_redemptions (
  id          TEXT PRIMARY KEY,                    -- uuid v4
  code        TEXT NOT NULL,
  tenant_id   TEXT NOT NULL,
  provider    TEXT NOT NULL,                       -- stripe | pesapal
  reference   TEXT,                                -- checkout session id / order tracking id
  created_at  TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_redemption_unique ON discount_redemptions(code, tenant_id);
CREATE INDEX        IF NOT EXISTS idx_discount_active   ON discount_codes(active);
