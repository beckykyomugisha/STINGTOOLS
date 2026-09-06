// Licence issue + presentation, against a real D1 (miniflare) and the real
// schema.sql.
//
// THE POINT OF THIS FILE. The seat meter on the Planscape server counted
// ProjectMembers carrying a role string that nothing wrote, while the paths
// that sold a seat wrote AppUser rows. Every endpoint returned 200. Every test
// that asserted "200" passed. The number still never moved, and two PRs died on
// it.
//
// So the assertions here are deliberately not about status codes. They are
// about countLicensedSeats() — the exact function issue.ts consults before it
// decides a tenant is at cap — and about whether a presentation lands on a row
// that function actually counts. A test that checked `response.status === 200`
// would pass against a present.ts that wrote to a table nobody reads.

import test, { after } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Miniflare } from "miniflare";

import { onRequestPost as issueLicense } from "../marketing-site/functions/api/license/issue";
import { onRequestPost as presentLicense } from "../marketing-site/functions/api/license/present";
import { onRequestGet as listLicenses } from "../marketing-site/functions/api/license/index";
import { countLicensedSeats } from "../marketing-site/functions/api/license/_lib/seats";
import { signLicense } from "../marketing-site/functions/api/license/_lib/crypto";
import { signJwt } from "../marketing-site/functions/api/auth/_lib/jwt";

const JWT_SECRET = "test-secret-at-least-32-bytes-long-for-hs256";
const TENANT_ID = "tenant-test-0001";
const USER_ID = "user-test-0001";
// PLAN_CAPS["sting-tools"].studio === 5
const PLAN_PRODUCT = "sting-tools";
const PLAN_TIER = "studio";
const CAP = 5;

// --- harness ---------------------------------------------------------------

// schema.sql is checked out CRLF on Windows. Normalise first: JS `.` does not
// match \r, so a /--.*$/ strip silently does nothing on CRLF input, leaving the
// example `ALTER TABLE ...;` lines inside comments to split real statements in
// half. The failure looks like "incomplete input" from SQLite, a long way from
// the cause.
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

async function generatePrivateKeyPem(): Promise<string> {
  const pair = await crypto.subtle.generateKey(
    {
      name: "RSASSA-PKCS1-v1_5",
      modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]),
      hash: "SHA-256",
    },
    true,
    ["sign", "verify"]
  );
  const pkcs8 = new Uint8Array(
    await crypto.subtle.exportKey("pkcs8", pair.privateKey)
  );
  let b = "";
  for (const byte of pkcs8) b += String.fromCharCode(byte);
  const body = btoa(b).replace(/(.{64})/g, "$1\n");
  return `-----BEGIN PRIVATE KEY-----\n${body}\n-----END PRIVATE KEY-----`;
}

interface Harness {
  db: D1Database;
  env: Record<string, unknown>;
  token: string;
}

// One Miniflare (one workerd process) for the whole file. Applying the 63
// statements of schema.sql costs a round-trip each, so doing it per test made
// the suite take minutes. Tests get isolation from reset() instead.
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

    const token = await signJwt(
      {
        sub: USER_ID,
        tid: TENANT_ID,
        role: "owner",
        ev: true,
        ps: "active",
        pt: PLAN_TIER,
        pp: PLAN_PRODUCT,
      },
      JWT_SECRET
    );

    shared = {
      h: {
        db,
        token,
        env: {
          WAITLIST_DB: db,
          JWT_SECRET,
          LICENSE_PRIVATE_KEY: await generatePrivateKeyPem(),
        },
      },
      dispose: () => mf.dispose(),
    };
  }
  await reset(shared.h);
  return shared.h;
}

async function reset(h: Harness): Promise<void> {
  const now = new Date().toISOString();
  await h.db.batch([
    h.db.prepare(`DELETE FROM licenses`),
    h.db.prepare(`DELETE FROM audit_log`),
    h.db.prepare(`DELETE FROM tenants`),
    h.db
      .prepare(
        `INSERT INTO tenants
           (id, name, slug, country, currency, plan_product, plan_tier,
            subscription_status, trial_started_at, trial_ends_at, created_at)
         VALUES (?,?,?,?,?,?,?,?,?,?,?)`
      )
      .bind(
        TENANT_ID,
        "Test Practice",
        "test-practice",
        "UG",
        "USD",
        PLAN_PRODUCT,
        PLAN_TIER,
        "active",
        now,
        new Date(Date.now() + 30 * 86400_000).toISOString(),
        now
      ),
  ]);
}

after(async () => {
  await shared?.dispose();
});

function call(
  handler: unknown,
  h: Harness,
  body: unknown,
  headers: Record<string, string> = {}
): Promise<Response> {
  const request = new Request("https://planscape.build/api/license/x", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  return (handler as (ctx: unknown) => Promise<Response>)({
    request,
    env: h.env,
    params: {},
  });
}

const issue = (h: Harness, machineCode: string) =>
  call(issueLicense, h, { machineCode }, { Authorization: `Bearer ${h.token}` });

const present = (h: Harness, body: unknown) => call(presentLicense, h, body);

// call() is POST-only; the list endpoint is a GET with no body.
function callGet(
  handler: unknown,
  h: Harness,
  headers: Record<string, string> = {}
): Promise<Response> {
  const request = new Request("https://planscape.build/api/license", {
    method: "GET",
    headers,
  });
  return (handler as (ctx: unknown) => Promise<Response>)({
    request,
    env: h.env,
    params: {},
  });
}

interface ListBody {
  cap: number | null;
  inUse: number;
  licences: Array<{
    machineCode: string;
    licensee: string;
    issuedAt: string;
    expiresAt: string;
    revokedAt: string | null;
    lastSeenAt: string | null;
    lastSeenPluginVersion: string | null;
    lastSeenRevitVersion: string | null;
  }>;
}

const list = (h: Harness) =>
  callGet(listLicenses, h, { Authorization: `Bearer ${h.token}` });

const seats = (h: Harness) =>
  countLicensedSeats(h.db, TENANT_ID, new Date().toISOString());

const totalRows = async (h: Harness) =>
  (
    await h.db
      .prepare(`SELECT COUNT(*) AS n FROM licenses`)
      .first<{ n: number }>()
  )?.n ?? 0;

// Select by the SAME predicate the cap is checked against, so what comes back
// is by construction a row that counts. Fetching by id would prove nothing —
// that is the mistake #606/#607 made.
async function countedRows(h: Harness) {
  const res = await h.db
    .prepare(
      `SELECT machine_code, last_seen_at, last_seen_plugin_version,
              last_seen_revit_version
         FROM licenses
        WHERE tenant_id = ? AND revoked_at IS NULL AND expires_at > ?`
    )
    .bind(TENANT_ID, new Date().toISOString())
    .all<{
      machine_code: string;
      last_seen_at: string | null;
      last_seen_plugin_version: string | null;
      last_seen_revit_version: string | null;
    }>();
  return res.results ?? [];
}

// --- the load-bearing test -------------------------------------------------

test("an issued licence moves the number the seat cap is checked against, and presenting it lands on that same row", async (t) => {
  const h = await harness();

  assert.equal(await seats(h), 0, "no seats used before anything is issued");

  const issued = await issue(h, "ADD3-E01C-3412-14C8-175E");
  assert.equal(issued.status, 200);
  const { license } = (await issued.json()) as { license: string };

  // (1) Issuing moved the counter — writer and counter are the same table.
  assert.equal(await seats(h), 1, "issuing a licence consumed a seat");

  const presented = await present(h, {
    license,
    pluginVersion: "2.2.0",
    revitVersion: "2025",
  });
  assert.equal(presented.status, 200);

  // (2) Presenting did NOT invent a seat.
  assert.equal(await seats(h), 1, "presentation must not manufacture a seat");
  assert.equal(await totalRows(h), 1, "presentation must not insert a row");

  // (3) THE ASSERTION. Among the rows the cap counts, the presented machine is
  //     there and carries what it reported. If present.ts wrote anywhere else —
  //     another table, another row, a no-op UPDATE that matched nothing — this
  //     fails while the endpoint still returns 200.
  const counted = await countedRows(h);
  assert.equal(counted.length, 1);
  assert.equal(counted[0].machine_code, "ADD3-E01C-3412-14C8-175E");
  assert.notEqual(
    counted[0].last_seen_at,
    null,
    "the row the cap counts must be the row presentation stamped"
  );
  assert.equal(counted[0].last_seen_plugin_version, "2.2.0");
  assert.equal(counted[0].last_seen_revit_version, "2025");

  // (4) And the endpoint reports that same number back, from the same helper.
  const bodyJson = (await presented.json()) as {
    licencesInUse: number;
    licencesIncluded: number;
    matchesRecord: boolean;
  };
  assert.equal(bodyJson.licencesInUse, await seats(h));
  assert.equal(bodyJson.licencesIncluded, CAP);
  assert.equal(bodyJson.matchesRecord, true, "the .lic matches our record");
});

// --- presentation must never create seats ----------------------------------

test("a licence signed by a different key is rejected and moves nothing", async (t) => {
  const h = await harness();

  await issue(h, "ADD3-E01C-3412-14C8-175E");
  const before = await seats(h);

  // Same payload shape, minted with a key we do not trust.
  const foreignKey = await generatePrivateKeyPem();
  const forged = await signLicense(
    foreignKey,
    JSON.stringify({
      licenseId: "ffffffffffffffffffffffffffffffff",
      machineCode: "BEEF-BEEF-BEEF-BEEF-BEEF",
      licensee: "Not Us",
      issuedUnix: Math.floor(Date.now() / 1000),
      expiryUnix: Math.floor(Date.now() / 1000) + 86400,
      schema: 1,
    })
  );

  const res = await present(h, { license: forged });
  assert.equal(res.status, 401);
  assert.equal(await seats(h), before);
  assert.equal(await totalRows(h), 1, "a forged licence must not insert a row");
});

test("a validly signed licence we have no record of is reported, not created", async (t) => {
  const h = await harness();

  const before = await seats(h);

  // Signed with OUR key, but never issued — e.g. a licence hand-issued before
  // the licences table existed.
  const orphan = await signLicense(
    h.env.LICENSE_PRIVATE_KEY as string,
    JSON.stringify({
      licenseId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      machineCode: "CAFE-CAFE-CAFE-CAFE-CAFE",
      licensee: "Ghost",
      issuedUnix: Math.floor(Date.now() / 1000),
      expiryUnix: Math.floor(Date.now() / 1000) + 86400,
      schema: 1,
    })
  );

  const res = await present(h, { license: orphan });
  assert.equal(res.status, 404);
  assert.equal(await seats(h), before);
  assert.equal(await totalRows(h), 0, "an unknown licence must not be created");
});

// --- the cap still behaves, and still uses the same number -----------------

test("the seat cap gates on the same count presentation reports", async (t) => {
  const h = await harness();

  for (let i = 0; i < CAP; i++) {
    const code = `AAAA-BBBB-CCCC-DDDD-${String(i).padStart(4, "0")}`;
    assert.equal((await issue(h, code)).status, 200, `machine ${i} should issue`);
  }
  assert.equal(await seats(h), CAP);

  const overCap = await issue(h, "FFFF-FFFF-FFFF-FFFF-FFFF");
  assert.equal(overCap.status, 403, "the machine past the cap is refused");
  assert.equal(await seats(h), CAP, "a refused issue consumes nothing");
});

test("re-issuing for a licensed machine reuses its seat and keeps its history", async (t) => {
  const h = await harness();

  const code = "ADD3-E01C-3412-14C8-175E";
  const first = await issue(h, code);
  const { license } = (await first.json()) as { license: string };
  await present(h, { license, pluginVersion: "2.2.0", revitVersion: "2025" });

  const seen = (await countedRows(h))[0].last_seen_at;
  assert.notEqual(seen, null);

  // A reinstall asks for the same machine again.
  const again = await issue(h, code);
  assert.equal(again.status, 200);
  const { reissued } = (await again.json()) as { reissued: boolean };
  assert.equal(reissued, true);

  assert.equal(await seats(h), 1, "a reinstall must not spend a second seat");
  assert.equal(
    (await countedRows(h))[0].last_seen_at,
    seen,
    "re-issuing must not wipe what we know about the machine"
  );
});

// --- reporting reflects reality, without gating on it ----------------------

test("a revoked licence still running is recorded and reported as revoked", async (t) => {
  const h = await harness();

  const code = "ADD3-E01C-3412-14C8-175E";
  const { license } = (await (await issue(h, code)).json()) as {
    license: string;
  };

  await h.db
    .prepare(`UPDATE licenses SET revoked_at = ? WHERE machine_code = ?`)
    .bind(new Date().toISOString(), code)
    .run();
  assert.equal(await seats(h), 0, "revoking frees the seat");

  const res = await present(h, { license, pluginVersion: "2.2.0" });
  assert.equal(res.status, 200, "we still want to hear from a revoked machine");
  const body = (await res.json()) as { revoked: boolean; recorded: boolean };
  assert.equal(body.revoked, true);
  assert.equal(body.recorded, true);

  // Recorded, but the seat stays free — presentation never resurrects a licence.
  assert.equal(await seats(h), 0);
  const row = await h.db
    .prepare(`SELECT last_seen_at, revoked_at FROM licenses WHERE machine_code = ?`)
    .bind(code)
    .first<{ last_seen_at: string | null; revoked_at: string | null }>();
  assert.notEqual(row?.last_seen_at, null, "the sighting was recorded");
  assert.notEqual(row?.revoked_at, null, "presentation must not clear revocation");
});

test("a .lic that disagrees with our record is flagged, not corrected", async (t) => {
  const h = await harness();

  const code = "ADD3-E01C-3412-14C8-175E";
  const { license } = (await (await issue(h, code)).json()) as {
    license: string;
  };

  // Someone changed the record after the .lic was minted (a support fix, a
  // restore) — the file in the field now says something different.
  const moved = new Date(Date.now() + 900 * 86400_000).toISOString();
  await h.db
    .prepare(`UPDATE licenses SET expires_at = ? WHERE machine_code = ?`)
    .bind(moved, code)
    .run();

  const res = await present(h, { license });
  assert.equal(res.status, 200);
  const body = (await res.json()) as { matchesRecord: boolean; expiresAt: string };
  assert.equal(body.matchesRecord, false, "the divergence is surfaced");
  assert.equal(body.expiresAt, moved, "we report OUR record, not the file's");
});

// --- the list endpoint -----------------------------------------------------

test("the list reports the same seat numbers the cap is checked against", async (t) => {
  const h = await harness();

  const issued = await issue(h, "ADD3-E01C-3412-14C8-175E");
  assert.equal(issued.status, 200);
  const { license } = (await issued.json()) as { license: string };
  await present(h, { license, pluginVersion: "2.2.0", revitVersion: "2025" });

  const res = await list(h);
  assert.equal(res.status, 200);
  const body = (await res.json()) as ListBody;

  assert.equal(body.licences.length, 1);
  assert.equal(body.licences[0].machineCode, "ADD3-E01C-3412-14C8-175E");
  assert.equal(body.licences[0].lastSeenPluginVersion, "2.2.0");
  assert.equal(body.licences[0].lastSeenRevitVersion, "2025");
  assert.notEqual(body.licences[0].lastSeenAt, null);
  assert.equal(body.licences[0].revokedAt, null);

  // The numbers must come from the same helper issue.ts gates on. If the
  // endpoint grew its own query, this drifts silently — which is the exact
  // failure seats.ts exists to prevent.
  assert.equal(body.cap, CAP);
  assert.equal(body.inUse, await seats(h));
  assert.equal(body.inUse, 1);
});

test("the list returns only the caller's tenant, never another tenant's machines", async (t) => {
  const h = await harness();

  await issue(h, "ADD3-E01C-3412-14C8-175E");

  const now = new Date().toISOString();
  const other = "tenant-test-0002";
  await h.db.batch([
    h.db
      .prepare(
        `INSERT INTO tenants
           (id, name, slug, country, currency, plan_product, plan_tier,
            subscription_status, trial_started_at, trial_ends_at, created_at)
         VALUES (?,?,?,?,?,?,?,?,?,?,?)`
      )
      .bind(other, "Other Firm", "other-firm", "UG", "USD", PLAN_PRODUCT,
            PLAN_TIER, "active", now,
            new Date(Date.now() + 30 * 86400_000).toISOString(), now),
    h.db
      .prepare(
        `INSERT INTO licenses
           (id, tenant_id, user_id, machine_code, licensee, issued_at,
            expires_at, created_at, updated_at)
         VALUES (?,?,?,?,?,?,?,?,?)`
      )
      .bind("lic-other-0001", other, "user-other-0001",
            "BEEF-BEEF-BEEF-BEEF-BEEF", "Other Firm", now,
            new Date(Date.now() + 365 * 86400_000).toISOString(), now, now),
  ]);

  const body = (await (await list(h)).json()) as ListBody;

  assert.equal(body.licences.length, 1, "only this tenant's machines");
  assert.equal(body.licences[0].machineCode, "ADD3-E01C-3412-14C8-175E");
  assert.equal(
    body.licences.some((l) => l.machineCode === "BEEF-BEEF-BEEF-BEEF-BEEF"),
    false,
    "another tenant's machine must never appear"
  );
  assert.equal(body.inUse, 1, "another tenant's licence must not count here");
});

test("a revoked licence is listed as revoked and stops consuming a seat", async (t) => {
  const h = await harness();

  await issue(h, "ADD3-E01C-3412-14C8-175E");
  assert.equal(await seats(h), 1);

  await h.db
    .prepare(`UPDATE licenses SET revoked_at = ? WHERE tenant_id = ?`)
    .bind(new Date().toISOString(), TENANT_ID)
    .run();

  const body = (await (await list(h)).json()) as ListBody;

  // Visible, so a user can see WHY the seat came back.
  assert.equal(body.licences.length, 1);
  assert.notEqual(body.licences[0].revokedAt, null);

  // But not counted — and counted by the same helper, not by this endpoint.
  assert.equal(body.inUse, 0);
  assert.equal(body.inUse, await seats(h));
});

test("the list refuses a caller with no token", async (t) => {
  const h = await harness();
  await issue(h, "ADD3-E01C-3412-14C8-175E");

  const res = await callGet(listLicenses, h); // no Authorization header
  assert.equal(res.status, 401);

  const body = (await res.json()) as { error: string };
  assert.match(body.error, /Authorization header/i);
});
