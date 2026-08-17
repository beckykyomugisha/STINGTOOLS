// Member caps vs machine caps.
//
// THE POINT OF THIS FILE. One `resolveCap()` used to bound two unrelated
// quantities (#693): team ACCOUNTS in login/me/invitations, and licensed MACHINES
// in license/issue|index|present. A `sting-tools/studio` tenant therefore got 5
// accounts AND, separately, 5 machines from a figure written to mean one thing.
// Nobody decided that — countLicensedSeats() reused the member helper.
//
// The two axes measure different things: a machine needs no account (present.ts
// says most licensed machines have no Planscape login), and one engineer with a
// desktop and a laptop is 1 account but 2 machines.
//
// These tests do two jobs. They pin that the axes are now independently
// resolvable, and they pin that the numbers are currently EQUAL — so that the day
// someone diverges pricing, the failing assertion tells them which axis they
// changed instead of leaving it to be discovered in production.

import test from "node:test";
import assert from "node:assert/strict";

import {
  PLAN_MEMBER_CAPS,
  PLAN_MACHINE_CAPS,
  TRIAL_MEMBER_CAP,
  TRIAL_MACHINE_CAP,
  resolveMemberCap,
  resolveMachineCap,
} from "../functions/api/auth/_lib/limits";

test("the two axes are resolved by separate functions", () => {
  // Same inputs, independently answered. Equal today by intent, not by sharing.
  assert.equal(resolveMemberCap("sting-tools", "studio"), 5);
  assert.equal(resolveMachineCap("sting-tools", "studio"), 5);
  assert.equal(resolveMemberCap("planscape", "large"), 100);
  assert.equal(resolveMachineCap("planscape", "large"), 100);
});

test("machine caps mirror member caps EXACTLY today — diverging them is a pricing change", () => {
  // If this fails, someone changed one table and not the other. That may be
  // entirely correct — but it is a pricing decision, so it should be a deliberate
  // edit to this assertion rather than a surprise in production.
  assert.deepEqual(
    JSON.parse(JSON.stringify(PLAN_MACHINE_CAPS)),
    JSON.parse(JSON.stringify(PLAN_MEMBER_CAPS)),
    "PLAN_MACHINE_CAPS and PLAN_MEMBER_CAPS have diverged — intended?"
  );
  assert.equal(TRIAL_MACHINE_CAP, TRIAL_MEMBER_CAP);
});

test("an unset plan falls back to the trial default on both axes", () => {
  assert.equal(resolveMemberCap(null, null), TRIAL_MEMBER_CAP);
  assert.equal(resolveMachineCap(null, null), TRIAL_MACHINE_CAP);
  assert.equal(resolveMemberCap("sting-tools", null), TRIAL_MEMBER_CAP);
  assert.equal(resolveMachineCap(null, "studio"), TRIAL_MACHINE_CAP);
});

test("an unknown product or tier falls back rather than returning undefined", () => {
  // The old implementation guarded this; keep it guarded. Returning undefined
  // here would make `used >= cap` false and silently uncap machine issuing.
  assert.equal(resolveMemberCap("no-such-product", "studio"), TRIAL_MEMBER_CAP);
  assert.equal(resolveMachineCap("sting-tools", "no-such-tier"), TRIAL_MACHINE_CAP);
  assert.equal(resolveMachineCap("no-such-product", "no-such-tier"), TRIAL_MACHINE_CAP);
});

test("enterprise is unlimited on both axes, and stays a number", () => {
  // Infinity, not null and not undefined — callers compare with `!==` and
  // serialise it as null themselves (see license/index.ts).
  assert.equal(resolveMemberCap("sting-tools", "enterprise"), Infinity);
  assert.equal(resolveMachineCap("sting-tools", "enterprise"), Infinity);
  assert.equal(typeof resolveMachineCap("sting-tools", "enterprise"), "number");
});

test("solo means one machine — the case the machine cap exists to enforce", () => {
  // issue.ts's own note: without this, one Solo subscriber could licence
  // unlimited machines. It is the only enforcement point, because an issued
  // licence cannot be revoked remotely.
  assert.equal(resolveMachineCap("sting-tools", "solo"), 1);
});
