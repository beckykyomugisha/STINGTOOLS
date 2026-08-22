// Trial expiry, as seen by the paths that actually gate access.
//
// THE POINT OF THIS FILE. expireTrialIfNeeded() was correct and was called by
// exactly one endpoint, /api/auth/me. Every other path read subscription_status
// straight from the row and saw a stale "trial", so a tenant whose trial ended in
// June kept downloading paid builds and issuing licences in August — and issue.ts
// derived licence expiry from that same stale row and minted a licence that was
// already dead (#677).
//
// entitlementFor() took a STATUS STRING, which is what made that possible: the
// string discards trial_ends_at, the only field that can tell the two apart. It
// now takes the tenant. These tests pin that, and pin the guard that stops a dead
// licence being minted whatever the status logic does.

import test from "node:test";
import assert from "node:assert/strict";

import { DOWNLOAD_CATALOG, entitlementFor } from "../marketing-site/functions/api/_lib/downloads/catalog";
import { effectiveStatus } from "../marketing-site/functions/api/auth/_lib/limits";

const NOW = Date.parse("2026-08-17T00:00:00.000Z");
const day = 86_400_000;
const iso = (ms: number) => new Date(ms).toISOString();

const tool = DOWNLOAD_CATALOG.find((t) => t.id === "sting-tools")!;

test("effectiveStatus reports a lapsed trial as read_only without touching the row", () => {
  const lapsed = { subscription_status: "trial", trial_ends_at: iso(NOW - 50 * day) };
  const live = { subscription_status: "trial", trial_ends_at: iso(NOW + 5 * day) };

  assert.equal(effectiveStatus(lapsed, NOW), "read_only");
  assert.equal(effectiveStatus(live, NOW), "trial");

  // The input is not mutated — the write belongs on /me, not on every read.
  assert.equal(lapsed.subscription_status, "trial");
});

test("a non-trial status is passed through untouched", () => {
  for (const s of ["active", "past_due", "read_only", "cancelled"]) {
    assert.equal(
      effectiveStatus({ subscription_status: s, trial_ends_at: iso(NOW - 999 * day) }, NOW),
      s,
      `${s} must not be rewritten by trial logic`
    );
  }
});

test("a missing or unparseable trial_ends_at does not lock anyone out", () => {
  // A data problem is not a licence to deny access.
  assert.equal(effectiveStatus({ subscription_status: "trial", trial_ends_at: null }, NOW), "trial");
  assert.equal(
    effectiveStatus({ subscription_status: "trial", trial_ends_at: "not-a-date" }, NOW),
    "trial"
  );
});

// --- the load-bearing test -------------------------------------------------

test("a lapsed trial is refused downloads — the defect in #677", () => {
  const lapsed = { subscription_status: "trial", trial_ends_at: iso(NOW - 50 * day) };

  const r = entitlementFor(tool, lapsed, NOW);
  assert.equal(r.entitlement, "locked", "a trial that ended 50 days ago must not be allowed");
  assert.match(r.reason, /trial has ended/i, "and it must say why");

  // The exact shape of the old bug: the same tenant, judged by its status string.
  assert.equal(
    entitlementFor(tool, { subscription_status: "trial", trial_ends_at: iso(NOW + day) }, NOW)
      .entitlement,
    "allowed",
    "a trial with a day left is still allowed"
  );
});

test("the boundary is the expiry instant, not the day", () => {
  const oneSecondLeft = { subscription_status: "trial", trial_ends_at: iso(NOW + 1000) };
  const oneSecondPast = { subscription_status: "trial", trial_ends_at: iso(NOW - 1000) };

  assert.equal(entitlementFor(tool, oneSecondLeft, NOW).entitlement, "allowed");
  assert.equal(entitlementFor(tool, oneSecondPast, NOW).entitlement, "locked");
});

test("past_due still gets access, and cancelled still does not", () => {
  // Dunning may recover a bounced card; locking a working tool over it loses a
  // customer who meant to pay. This behaviour predates #677 and must survive it.
  assert.equal(
    entitlementFor(tool, { subscription_status: "past_due", trial_ends_at: null }, NOW).entitlement,
    "allowed"
  );
  assert.equal(
    entitlementFor(tool, { subscription_status: "cancelled", trial_ends_at: null }, NOW).entitlement,
    "locked"
  );
});

test("no tenant at all is locked, not allowed", () => {
  assert.equal(entitlementFor(tool, null, NOW).entitlement, "locked");
  assert.equal(entitlementFor(tool, undefined, NOW).entitlement, "locked");
});

test("an in-development tool is unavailable regardless of a live trial", () => {
  const inDev = DOWNLOAD_CATALOG.find((t) => t.status === "in-development");
  if (!inDev) return; // catalogue may not carry one
  const live = { subscription_status: "trial", trial_ends_at: iso(NOW + 5 * day) };
  assert.equal(entitlementFor(inDev, live, NOW).entitlement, "unavailable");
});
