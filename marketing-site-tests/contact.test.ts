// POST /api/contact, against a real D1 (miniflare) and the real schema.sql.
//
// THE POINT OF THIS FILE. This endpoint replaced a fetch to a hostname that does
// not resolve, which failed every submission and told prospects the site was
// broken (#705). The property that matters is therefore not "returns 200" — it
// is that a submission SURVIVES. Specifically: the row is written before the
// email is attempted, and a failing notification neither loses the enquiry nor
// reports failure to the visitor.
//
// So the assertions are about the contacts table, not the status code. Resend is
// never reachable here (RESEND_API_KEY is unset), which means every test runs the
// notification-failed path by default — the path that used to lose data.

import test, { after } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Miniflare } from "miniflare";

import { onRequestPost as contact } from "../marketing-site/functions/api/contact";

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

interface Harness {
  db: D1Database;
  env: Record<string, unknown>;
}

let shared: { h: Harness; dispose: () => Promise<void> } | null = null;

async function harness(): Promise<Harness> {
  if (!shared) {
    const mf = new Miniflare({
      modules: true,
      script: "export default { fetch() { return new Response('ok') } }",
      d1Databases: { WAITLIST_DB: ":memory:" },
    });
    const db = (await mf.getD1Database("WAITLIST_DB")) as unknown as D1Database;
    const schema = readFileSync("functions/api/schema.sql", "utf8");
    await db.batch(statements(schema).map((s) => db.prepare(s)));
    // RESEND_API_KEY deliberately absent — see the header comment.
    shared = { h: { db, env: { WAITLIST_DB: db } }, dispose: () => mf.dispose() };
  }
  await shared.h.db.prepare(`DELETE FROM contacts`).run();
  return shared.h;
}

after(async () => {
  await shared?.dispose();
});

const VALID = {
  name: "Sentongo",
  email: "sentongo@example.com",
  firm: "M&E Associates",
  topic: "demo",
  message: "We would like a demo for our MEP team.",
};

function post(
  h: Harness,
  body: unknown,
  headers: Record<string, string> = {}
): Promise<Response> {
  const request = new Request("https://planscape.build/api/contact", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  return (contact as unknown as (ctx: unknown) => Promise<Response>)({
    request,
    env: h.env,
    params: {},
  });
}

const rows = async (h: Harness) =>
  (await h.db.prepare(`SELECT * FROM contacts ORDER BY id`).all<Record<string, unknown>>())
    .results ?? [];

// --- the load-bearing test -------------------------------------------------

test("a submission is stored even though the notification cannot be sent", async (t) => {
  const h = await harness();

  const res = await post(h, VALID, { "CF-Connecting-IP": "203.0.113.10" });

  // The visitor is told it worked, because for them it did — we have it.
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { ok: true });

  const all = await rows(h);
  assert.equal(all.length, 1, "the enquiry must exist regardless of email");
  assert.equal(all[0].name, VALID.name);
  assert.equal(all[0].email, VALID.email);
  assert.equal(all[0].topic, VALID.topic);
  assert.equal(all[0].message, VALID.message);
  assert.equal(all[0].ip, "203.0.113.10");
  assert.equal(all[0].status, "new");

  // And the failed notification is recorded as data, not just a log line, so
  // "stored but nobody was told" is a query.
  assert.equal(all[0].notified_at, null, "no email was sent, so this stays NULL");
});

test("email is lowercased and oversized input is clipped, not rejected", async (t) => {
  const h = await harness();

  await post(h, {
    ...VALID,
    email: "  Sentongo@Example.COM  ",
    message: "x".repeat(6000),
  });

  const all = await rows(h);
  assert.equal(all[0].email, "sentongo@example.com");
  assert.equal((all[0].message as string).length, 5000, "clipped to the cap");
});

// --- validation: nothing malformed reaches the table -----------------------

test("malformed submissions are refused and write nothing", async (t) => {
  const h = await harness();

  const cases: Array<[string, Record<string, unknown>]> = [
    ["no name", { ...VALID, name: "" }],
    ["bad email", { ...VALID, email: "not-an-email" }],
    ["no message", { ...VALID, message: "   " }],
    ["topic not in the select", { ...VALID, topic: "spam-topic" }],
    ["missing topic", { ...VALID, topic: "" }],
  ];

  for (const [label, body] of cases) {
    const res = await post(h, body);
    assert.equal(res.status, 400, `${label} should be refused`);
    const b = (await res.json()) as { error: string };
    assert.ok(b.error && b.error.length > 0, `${label} should say why`);
  }

  assert.equal((await rows(h)).length, 0, "no partial rows from refused input");
});

// --- abuse -----------------------------------------------------------------

test("one IP cannot use the endpoint as a mail relay", async (t) => {
  const h = await harness();
  const ip = { "CF-Connecting-IP": "198.51.100.7" };

  for (let i = 0; i < 5; i++) {
    const res = await post(h, { ...VALID, message: `message ${i}` }, ip);
    assert.equal(res.status, 200, `submission ${i + 1} of 5 should be allowed`);
  }

  const blocked = await post(h, VALID, ip);
  assert.equal(blocked.status, 429, "the 6th within the hour is refused");

  // Refused, but still told where to go — the endpoint never dead-ends someone.
  const body = (await blocked.json()) as { error: string };
  assert.match(body.error, /hello@planscape\.build/);

  assert.equal((await rows(h)).length, 5, "the refused one is not stored");

  // A different IP is unaffected — the cap is per-origin, not global.
  const other = await post(h, VALID, { "CF-Connecting-IP": "198.51.100.8" });
  assert.equal(other.status, 200);
});

test("a submission with no IP header still works", async (t) => {
  const h = await harness();

  // Local dev and some proxies send no CF-Connecting-IP. Treating absent as one
  // shared bucket would rate-limit every such caller against every other.
  for (let i = 0; i < 7; i++) {
    const res = await post(h, { ...VALID, message: `no-ip ${i}` });
    assert.equal(res.status, 200);
  }
  assert.equal((await rows(h)).length, 7);
});
