// The email delivery ledger, against a real D1 (miniflare) and the real schema.
//
// THE POINT OF THIS FILE. EMAIL_FROM was set to a base64 random string, so Resend
// 422'd every send — signup verification, password reset, welcome, invitations —
// and nothing anywhere recorded it. Every sender awaits send() and discards the
// result by design, so the only trace was a Function log, and a total email
// outage stayed invisible for months (#711).
//
// So these tests assert two things that would have caught it:
//   1. a failed send leaves a row saying so;
//   2. the sender VALUE never appears in what gets recorded — because the log
//      line that used to print it wrote a secret-shaped value into Cloudflare's
//      logs on every failure.

import test, { after } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Miniflare } from "miniflare";

import { sendContactNotification } from "../functions/api/auth/_lib/email";
import type { Env } from "../functions/api/auth/_lib/types";

function statements(sql: string): string[] {
  return sql
    .replace(/\r\n/g, "\n")
    .split("\n")
    .map((line) => line.replace(/--.*$/, ""))
    .join("\n")
    .split(";")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

let shared: { db: D1Database; dispose: () => Promise<void> } | null = null;

async function db(): Promise<D1Database> {
  if (!shared) {
    const mf = new Miniflare({
      modules: true,
      script: "export default { fetch() { return new Response('ok') } }",
      d1Databases: { WAITLIST_DB: ":memory:" },
    });
    const d = (await mf.getD1Database("WAITLIST_DB")) as unknown as D1Database;
    await d.batch(statements(readFileSync("functions/api/schema.sql", "utf8")).map((s) => d.prepare(s)));
    shared = { db: d, dispose: () => mf.dispose() };
  }
  await shared.db.prepare(`DELETE FROM email_log`).run();
  return shared.db;
}

after(async () => {
  await shared?.dispose();
});

// The base64 blob EMAIL_FROM actually held. Shortened, but the same shape: not
// an email address, and secret-looking.
const BAD_FROM = "v14gO53fqhK3RyAdzI7b+qUHN8fU8gtcmtoEtHE2M";

const PERSON = {
  name: "Sentongo",
  email: "sentongo@example.com",
  firm: "M&E Associates",
  topic: "demo",
  message: "Hello",
};

const rows = async (d: D1Database) =>
  (await d.prepare(`SELECT * FROM email_log ORDER BY id`).all<Record<string, unknown>>()).results ?? [];

// --- the load-bearing test -------------------------------------------------

test("a send that cannot happen is recorded, not merely logged", async (t) => {
  const d = await db();
  // RESEND_API_KEY absent — the shape of a misconfigured environment.
  const env = { WAITLIST_DB: d } as unknown as Env;

  const ok = await sendContactNotification(env, "hello@planscape.build", PERSON);

  assert.equal(ok, false, "send reports failure to its caller");

  const all = await rows(d);
  assert.equal(all.length, 1, "the attempt left a row");
  assert.equal(all[0].ok, 0);
  assert.equal(all[0].to_address, "hello@planscape.build");
  assert.match(String(all[0].subject), /Contact/);
  assert.match(String(all[0].error), /RESEND_API_KEY unset/);

  // This is the query that would have surfaced the outage on day one.
  const failures = await d
    .prepare(`SELECT COUNT(*) AS n FROM email_log WHERE ok = 0`)
    .first<{ n: number }>();
  assert.equal(failures?.n, 1);
});

test("the recorded failure never contains the sender value", async (t) => {
  const d = await db();
  // A misconfigured EMAIL_FROM, exactly the bug from #711.
  const env = { WAITLIST_DB: d, EMAIL_FROM: BAD_FROM } as unknown as Env;

  await sendContactNotification(env, "hello@planscape.build", PERSON);

  const all = await rows(d);
  assert.equal(all.length, 1);

  // The whole row, not just the error column — nothing may carry the value.
  const serialised = JSON.stringify(all[0]);
  assert.equal(
    serialised.includes(BAD_FROM),
    false,
    "the sender value must never be recorded — that is how it reached the logs"
  );
});

test("a ledger row is written for every attempt, so silence means no attempt", async (t) => {
  const d = await db();
  const env = { WAITLIST_DB: d } as unknown as Env;

  await sendContactNotification(env, "one@example.com", PERSON);
  await sendContactNotification(env, "two@example.com", PERSON);
  await sendContactNotification(env, "three@example.com", PERSON);

  const all = await rows(d);
  assert.equal(all.length, 3);
  assert.deepEqual(
    all.map((r) => r.to_address),
    ["one@example.com", "two@example.com", "three@example.com"]
  );
});

test("a broken ledger does not break sending", async (t) => {
  const d = await db();

  // Drop the table out from under it: record() must swallow the failure, and
  // send() must still return its verdict. An observability failure that breaks
  // signup would be worse than the blindness it replaces.
  await d.prepare(`DROP TABLE email_log`).run();

  const env = { WAITLIST_DB: d } as unknown as Env;
  const ok = await sendContactNotification(env, "hello@planscape.build", PERSON);
  assert.equal(ok, false, "still returns a verdict rather than throwing");

  // Restore for the shared harness.
  await d.batch(
    statements(readFileSync("functions/api/schema.sql", "utf8"))
      .filter((s) => /email_log/.test(s))
      .map((s) => d.prepare(s))
  );
});
