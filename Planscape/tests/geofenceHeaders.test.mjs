/**
 * createIssue must send geofence coordinates where the server READS them.
 *
 * Regression cover for #632. Mobile put latitude/longitude in the request
 * BODY. The server never looks there: MobileContextMiddleware lifts the
 * X-Latitude / X-Longitude HEADERS into HttpContext.Items, and
 * IssuesController reads Items["Latitude"] for the boundary check.
 *
 * The consequence was not a subtle one. On a geofenced project the server saw
 * no coordinates at all, so EVERY create was refused with
 *   400 "Geofence enforcement is active. Location coordinates are required."
 * including for a user standing in the middle of the site — and the genuine
 * "outside the boundary" 403 was unreachable from mobile entirely.
 *
 * Run: npm run test:geofence
 */

import assert from 'node:assert/strict';
import './register.mjs';

const captured = [];
globalThis.fetch = async (url, init = {}) => {
  const h = {};
  const raw = init.headers ?? {};
  if (typeof raw?.forEach === 'function') raw.forEach((v, k) => { h[String(k).toLowerCase()] = v; });
  else for (const [k, v] of Object.entries(raw)) h[String(k).toLowerCase()] = v;
  captured.push({ url: String(url), headers: h, body: init.body ? JSON.parse(init.body) : undefined });
  return { ok: true, status: 200, headers: new Map(), json: async () => ({ id: 'issue-1' }), text: async () => '{}' };
};

const { createIssue } = await import('../src/api/endpoints.ts');

// ── 1. coordinates present -> headers sent, body unchanged ──────────────────
captured.length = 0;
await createIssue('p1', { title: 'Cracked slab', latitude: 0.3136, longitude: 32.5811 });
const withGps = captured.at(-1);

assert.equal(withGps.headers['x-latitude'], '0.3136',
  'X-Latitude header missing — the server reads coordinates from headers, not the body (#632)');
assert.equal(withGps.headers['x-longitude'], '32.5811',
  'X-Longitude header missing — see #632');

// They must ALSO stay in the body: that is where the stored issue coordinates
// come from. The fix is additive; removing them would break persistence.
assert.equal(withGps.body.latitude, 0.3136, 'latitude must remain in the body for storage');
assert.equal(withGps.body.longitude, 32.5811, 'longitude must remain in the body for storage');

// ── 2. no coordinates -> no coordinate headers, and no crash ────────────────
captured.length = 0;
await createIssue('p1', { title: 'No GPS available' });
const noGps = captured.at(-1);
assert.equal(noGps.headers['x-latitude'], undefined, 'must not invent a coordinate header');
assert.equal(noGps.headers['x-longitude'], undefined, 'must not invent a coordinate header');

// ── 3. a non-finite reading must not be sent as the string "NaN" ────────────
// The server parses these as doubles; "NaN" would either throw or be read as a
// real position. Absent beats wrong.
captured.length = 0;
await createIssue('p1', { title: 'Bad fix', latitude: Number.NaN, longitude: 32.5811 });
const badGps = captured.at(-1);
assert.equal(badGps.headers['x-latitude'], undefined, 'NaN must not be stringified into a header');
assert.equal(badGps.headers['x-longitude'], undefined, 'a partial fix must not be sent as a position');

// ── 4. the idempotency header still works alongside ─────────────────────────
captured.length = 0;
await createIssue('p1', { title: 'Replayed', latitude: 1, longitude: 2, idempotencyKey: 'k-1' });
const both = captured.at(-1);
assert.equal(both.headers['x-idempotency-key'], 'k-1', 'idempotency header regressed');
assert.equal(both.headers['x-latitude'], '1', 'coordinate header lost when idempotencyKey present');
assert.equal(both.body.idempotencyKey, undefined, 'idempotencyKey must not leak into the body');

console.log('geofenceHeaders: 11 assertions passed');
