/**
 * #645 — WHICH of the four answers does a mobile screen give?
 *
 * SCOPE, STATED HONESTLY. This verifies the state SELECTION, not the
 * rendering. It answers "for this failure, does the screen enter FORBIDDEN or
 * ERROR, and what sentence does the user read?" — it does not paint pixels.
 * The mobile app has no react-test-renderer and no RN testing library, and
 * there is no emulator on this machine, so the visual half of #645 remains
 * unverified and is listed as such.
 *
 * The selection half is worth pinning on its own, because it is where the
 * defect class lives: every one of #624, #625 and #646 was a screen choosing
 * the WRONG state, not a screen painting the right state badly.
 *
 * Loads the real src/utils/forbidden.ts and the real ApiError, with only
 * react-native stubbed.
 *
 * Run: npm run test:forbidden
 */

import assert from 'node:assert/strict';
import './register.mjs';

const { ApiError } = await import('../src/api/client.ts');
const { describeFailure, alertFailure, isForbidden, CAPABILITY_COPY } =
  await import('../src/utils/forbidden.ts');
const rn = await import('./stubs/react-native.mjs');

const OPTS = {
  forbidden: CAPABILITY_COPY.projectAdmin,
  fallback: 'Update failed',
};

const results = [];
const check = (name, fn) => {
  try { fn(); results.push([true, name]); }
  catch (e) { results.push([false, `${name}\n      ${e.message.split('\n')[0]}`]); }
};

// ── The four inputs a screen actually sees ──────────────────────────────────
const CASES = [
  {
    label: 'FORBIDDEN — 403 carrying the server\'s reason',
    err: new ApiError(403, 'Your role does not permit changing naming enforcement.'),
    expectForbidden: true,
    expectMessage: 'Your role does not permit changing naming enforcement.',
    why: 'the server explained itself; that explanation wins over any local copy',
  },
  {
    label: 'FORBIDDEN — 403 with an EMPTY body (ASP.NET Forbid())',
    err: new ApiError(403, 'HTTP 403'),
    expectForbidden: true,
    expectMessage: CAPABILITY_COPY.projectAdmin,
    why: 'nothing to show, so the capability sentence stands in — never "HTTP 403"',
  },
  {
    label: 'ERROR — a 500 is a failure, not a refusal',
    err: new ApiError(500, 'Internal Server Error'),
    expectForbidden: false,
    expectMessage: 'Internal Server Error',
    why: 'retryable; must not be dressed as a permission problem',
  },
  {
    label: 'ERROR — a network drop is not an ApiError at all',
    err: new TypeError('Network request failed'),
    expectForbidden: false,
    expectMessage: 'Network request failed',
    why: 'unknown must never render as denied',
  },
];

console.log('\n  ── which state, and what the user reads ──');
for (const c of CASES) {
  const d = describeFailure(c.err, OPTS);
  console.log(`  ${(d.forbidden ? 'FORBIDDEN' : 'ERROR').padEnd(10)} ${c.label}`);
  console.log(`  ${''.padEnd(10)}   → "${d.message}"`);
  check(`${c.label} → ${c.expectForbidden ? 'FORBIDDEN' : 'ERROR'} (${c.why})`, () => {
    assert.equal(d.forbidden, c.expectForbidden);
    assert.equal(d.message, c.expectMessage);
  });
}

// ── The specific string users must never see ────────────────────────────────
check('a 403 never surfaces the literal "HTTP 403" as if it were a reason', () => {
  for (const body of ['HTTP 403', '', '   ']) {
    const d = describeFailure(new ApiError(403, body || 'HTTP 403'), OPTS);
    assert.ok(!/HTTP\s*\d{3}/.test(d.message),
      `user would read "${d.message}" — a status masquerading as an explanation`);
  }
});

check('a 403 is detected by status, not by prose', () => {
  // The whole defect class in one assertion: a body with no status text in it.
  assert.equal(isForbidden(new ApiError(403, '{"error":"nope"}')), true);
  assert.equal(isForbidden(new ApiError(500, 'HTTP 403 appears in this text')), false,
    'matching the message would misclassify this one');
  assert.equal(isForbidden(new TypeError('Network request failed')), false);
});

// ── The title is part of the answer ─────────────────────────────────────────
// A user decides "retry" vs "go ask someone" from the title before reading the
// body, so a refusal titled "Update failed" is still the wrong answer.
rn.__resetAlerts();
alertFailure(new ApiError(403, 'Your role does not permit this.'),
  { title: 'Update failed', forbidden: CAPABILITY_COPY.projectAdmin, fallback: 'Update failed' });
alertFailure(new ApiError(500, 'Internal Server Error'),
  { title: 'Update failed', forbidden: CAPABILITY_COPY.projectAdmin, fallback: 'Update failed' });
const alerts = rn.__alerts();

console.log('\n  ── alert titles ──');
for (const a of alerts) console.log(`  "${a.title}"  →  "${a.message}"`);

check('a refusal is titled "Permission denied", a failure keeps the caller\'s title', () => {
  assert.equal(alerts.length, 2);
  assert.equal(alerts[0].title, 'Permission denied');
  assert.equal(alerts[1].title, 'Update failed');
  assert.notEqual(alerts[0].title, alerts[1].title);
});

// ── Capability copy names capabilities, not role codes ──────────────────────
check('capability copy never puts an ISO role LETTER in front of a user', () => {
  for (const [key, sentence] of Object.entries(CAPABILITY_COPY)) {
    assert.ok(!/\brole [KC]\b/.test(sentence),
      `${key} leaks an ISO role code to the user: "${sentence}"`);
  }
});

console.log('');
let bad = 0;
for (const [ok, name] of results) {
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) bad++;
}
console.log(`\n  ${results.length - bad}/${results.length} passed, ${bad} failed`);
console.log('  NOTE: state SELECTION verified. Rendering NOT verified — no RN test');
console.log('        renderer and no emulator available. See the PR body.\n');
process.exit(bad ? 1 : 0);
