/**
 * QR-8 — a commissioning sign-off taken with no signal must survive.
 *
 * Commissioning happens in basement plant rooms, which is exactly where there is
 * no signal. Before this, the scan flow POSTed directly: the operative walked
 * down three flights, scanned the asset, and lost the sign-off.
 *
 * Run: npm run test:commissioning-queue
 */

import assert from 'node:assert/strict';
import './register.mjs';

const q = await import('../src/utils/offlineQueue.ts');
const endpoints = await import('./stubs/endpoints.mjs');
const AsyncStorage = (await import('./stubs/async-storage.mjs')).default;

const QUEUE_KEY = 'planscape_offline_queue';
const FAILED_KEY = 'planscape_offline_failed';

async function reset() {
  await AsyncStorage.removeItem(QUEUE_KEY);
  await AsyncStorage.removeItem(FAILED_KEY);
}

const results = [];
const check = async (name, fn) => {
  try { await fn(); results.push(['PASS', name]); }
  catch (err) { results.push(['FAIL', name, err.message]); }
};

const STEP = {
  elementUniqueId: '3f2504e0-4f89-11d3-9a0c-0305e82c3301-000a1b2c',
  operative: 'A. Fitter',
  elementTag: 'M-BLD1-Z01-L02-HVAC-SUP-AHU-0003',
  source: 'mobile-scan',
  occurredAt: '2026-09-14T09:00:00.000Z',
};

await check('a queued commissioning step replays to advanceCommissioning', async () => {
  await reset();
  let seen = null;
  endpoints.__setBehaviour(async (name, args) => { seen = { name, args }; });

  await q.enqueue('COMMISSIONING_ADVANCE', { projectId: 'P1', payload: STEP });
  const res = await q.syncQueue();

  assert.equal(res.succeeded, 1, 'the step should have drained');
  assert.ok(seen, 'no endpoint was called');
  assert.equal(seen.name, 'advanceCommissioning');
  assert.equal(seen.args[0], 'P1');
  assert.equal(seen.args[1].operative, 'A. Fitter');
  assert.equal(seen.args[1].elementUniqueId, STEP.elementUniqueId);
});

await check('the step keeps the time it actually happened, not the time it drained', async () => {
  await reset();
  let seen = null;
  endpoints.__setBehaviour(async (name, args) => { seen = { name, args }; });

  await q.enqueue('COMMISSIONING_ADVANCE', { projectId: 'P1', payload: STEP });
  await q.syncQueue();

  // A sign-off given at 09:00 in a basement and drained at 17:00 in the car park
  // is evidence about 09:00. The server records its own RecordedAt separately.
  assert.equal(seen.args[1].occurredAt, '2026-09-14T09:00:00.000Z');
});

await check('a witnessed sign-off carries the witness through the queue', async () => {
  await reset();
  let seen = null;
  endpoints.__setBehaviour(async (name, args) => { seen = { name, args }; });

  await q.enqueue('COMMISSIONING_ADVANCE', {
    projectId: 'P1',
    payload: { ...STEP, requestedState: 'COMMISSIONED', witness: 'B. Supervisor' },
  });
  await q.syncQueue();

  // Without this the server refuses the step, and a queued COMMISSIONED would
  // fail on drain — hours after the operative left the plant room.
  assert.equal(seen.args[1].witness, 'B. Supervisor');
  assert.equal(seen.args[1].requestedState, 'COMMISSIONED');
});

await check('every queued step gets an idempotency key', async () => {
  await reset();
  await q.enqueue('COMMISSIONING_ADVANCE', { projectId: 'P1', payload: STEP });
  const queued = await q.loadQueue();

  // A drain that half-succeeds and retries must not record the step twice.
  assert.equal(queued.length, 1);
  assert.ok(queued[0].payload.idempotencyKey, 'no idempotency key was minted');
});

await check('a failed drain keeps the step rather than dropping it', async () => {
  await reset();
  endpoints.__setBehaviour(async () => { throw new Error('Network request failed'); });

  await q.enqueue('COMMISSIONING_ADVANCE', { projectId: 'P1', payload: STEP });
  const res = await q.syncQueue();

  assert.equal(res.succeeded, 0);
  const [queued, failed] = [await q.loadQueue(), await q.loadFailedQueue()];
  assert.ok(
    queued.length + failed.length === 1,
    'the sign-off vanished: it is in neither the queue nor the failed side-queue',
  );
});

const failed = results.filter(r => r[0] === 'FAIL');
for (const r of results) console.log(`  ${r[0]}  ${r[1]}${r[2] ? `\n        ${r[2]}` : ''}`);
console.log(`\ncommissioning queue: ${results.length - failed.length}/${results.length} passed`);
if (failed.length) process.exit(1);
