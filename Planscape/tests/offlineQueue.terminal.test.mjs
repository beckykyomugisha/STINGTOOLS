/**
 * offlineQueue terminal-failure classification.
 *
 * Regression cover for #646. The queue used to decide whether a failure was
 * permanent by running a regex over the error *message*. ApiError.message is
 * the response BODY (falling back to `HTTP <status>` only when the body is
 * empty), so the regex only ever matched empty-body responses — and a refused
 * mutation that came back with a body was classified transient and retried.
 *
 * These assertions are written against `err.status`, which is what the class
 * has always carried. They exercise the real syncQueue and the real ApiError.
 *
 * Run: npm run test:queue
 */

import assert from 'node:assert/strict';
import './register.mjs';

const { ApiError } = await import('../src/api/client.ts');
const queue = await import('../src/utils/offlineQueue.ts');
const endpoints = await import('./stubs/endpoints.mjs');
const AsyncStorage = (await import('./stubs/async-storage.mjs')).default;

const QUEUE_KEY = 'planscape_offline_queue';
const FAILED_KEY = 'planscape_offline_failed';

// A real refusal body, as the server actually sends it. This is the case the
// old regex could not see.
const FORBIDDEN_BODY = JSON.stringify({
  error: 'forbidden',
  reason: 'Your role does not permit transitioning documents to PUBLISHED.',
});

async function reset() {
  await AsyncStorage.removeItem(QUEUE_KEY);
  await AsyncStorage.removeItem(FAILED_KEY);
}

/** Simulate the backoff window elapsing so the next drain is not gated. */
async function advancePastBackoff() {
  const raw = await AsyncStorage.getItem(QUEUE_KEY);
  if (!raw) return;
  const q = JSON.parse(raw).map((a) => ({ ...a, nextRetryAt: undefined }));
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(q));
}

async function counts() {
  const live = JSON.parse((await AsyncStorage.getItem(QUEUE_KEY)) ?? '[]');
  const failed = JSON.parse((await AsyncStorage.getItem(FAILED_KEY)) ?? '[]');
  return { live: live.length, failed: failed.length };
}

/**
 * Queue one action, make the endpoint reject with `status`/`body`, drain once,
 * and report whether the action was retained for retry or moved to the failed
 * side-queue.
 */
async function drainOnce(status, body) {
  await reset();
  await queue.enqueue('TRANSITION_CDE', { projectId: 'p', docId: 'd', newStatus: 'PUBLISHED' });
  endpoints.__setBehaviour(async () => { throw new ApiError(status, body || `HTTP ${status}`); });
  const result = await queue.syncQueue();
  return { result, ...(await counts()) };
}

const results = [];
function check(name, fn) {
  try { fn(); results.push([true, name]); }
  catch (e) { results.push([false, `${name}\n      ${e.message.split('\n')[0]}`]); }
}

// ── The class of bug, stated directly ────────────────────────────────────────
{
  const err = new ApiError(403, FORBIDDEN_BODY);
  check('ApiError carries the status as a field', () => assert.equal(err.status, 403));
  check('ApiError.message is the body, NOT "HTTP 403"', () => {
    assert.equal(err.message, FORBIDDEN_BODY);
    assert.ok(!/HTTP \d{3}/.test(err.message),
      'the message contains no status text — any regex over it cannot classify this error');
  });
}

// ── Terminal statuses: must move to the failed side-queue on attempt 1 ───────
for (const [status, body, label] of [
  [403, FORBIDDEN_BODY, '403 WITH a response body (#646)'],
  [403, '', '403 with an empty body'],
  [400, JSON.stringify({ error: 'bad latitude' }), '400 with a body'],
  [401, '', '401'],
  [404, '', '404'],
  [409, JSON.stringify({ error: 'conflict' }), '409 with a body'],
  [412, '', '412 Precondition Failed'],
  [422, '', '422 Unprocessable Entity'],
]) {
  const r = await drainOnce(status, body);
  check(`terminal: ${label} moves to failed queue on the first attempt`, () => {
    assert.equal(r.result.moved, 1, `expected moved=1, got ${r.result.moved}`);
    assert.equal(r.failed, 1, `expected 1 action in the failed side-queue, got ${r.failed}`);
    assert.equal(r.live, 0, `expected the live queue drained, got ${r.live}`);
  });
}

// ── Retryable statuses: must stay in the live queue ──────────────────────────
for (const [status, label] of [[408, '408 Request Timeout'], [429, '429 Too Many Requests'], [500, '500'], [503, '503']]) {
  const r = await drainOnce(status, '');
  check(`retryable: ${label} stays in the live queue`, () => {
    assert.equal(r.result.moved, 0, `expected moved=0, got ${r.result.moved}`);
    assert.equal(r.live, 1, `expected the action retained for retry, got live=${r.live}`);
    assert.equal(r.failed, 0, `expected nothing in the failed side-queue, got ${r.failed}`);
  });
}

// ── A non-ApiError (genuine network drop) must stay retryable ────────────────
{
  await reset();
  await queue.enqueue('TRANSITION_CDE', { projectId: 'p', docId: 'd', newStatus: 'SHARED' });
  endpoints.__setBehaviour(async () => { throw new TypeError('Network request failed'); });
  const result = await queue.syncQueue();
  const c = await counts();
  check('retryable: a network TypeError stays in the live queue', () => {
    assert.equal(result.moved, 0);
    assert.equal(c.live, 1);
  });
}

// ── Measure the cost of the misclassification ───────────────────────────────
// How many drains does a refused-with-a-body action survive? Every drain it
// survives is a wasted request AND a drain in which it blocks the FIFO head.
{
  await reset();
  await queue.enqueue('TRANSITION_CDE', { projectId: 'p', docId: 'd', newStatus: 'PUBLISHED' });
  endpoints.__setBehaviour(async () => { throw new ApiError(403, FORBIDDEN_BODY); });
  let drains = 0;
  for (let i = 0; i < 12; i++) {
    const r = await queue.syncQueue();
    if (r.total === 0) break;
    drains++;
    if (r.moved > 0) break;
    await advancePastBackoff();
  }
  console.log(`\n  measured: a 403-with-a-body survives ${drains} drain(s) before reaching the failed queue`);
  check('a refused action costs exactly one request', () =>
    assert.equal(drains, 1, `it took ${drains} drains — ${drains - 1} wasted request(s), and the FIFO head was blocked for ${drains - 1} extra drain(s)`));
}

// ── Report ──────────────────────────────────────────────────────────────────
console.log('');
let bad = 0;
for (const [ok, name] of results) {
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) bad++;
}
console.log(`\n  ${results.length - bad}/${results.length} passed, ${bad} failed\n`);
process.exit(bad ? 1 : 0);
