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
