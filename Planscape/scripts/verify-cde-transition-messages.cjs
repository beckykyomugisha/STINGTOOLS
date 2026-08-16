/**
 * #624 verification harness.
 *
 * Runs the REAL `src/utils/cdeTransitionMessage.ts` (transpiled to
 * `.verify-out/`, see the npm script) against responses CAPTURED FROM A
 * RUNNING PLANSCAPE API. Nothing below is invented; each `wire:` line is a
 * verbatim status + body observed on 2026-08-06 against
 * `docker compose` at http://localhost:5000. The capture commands are in the
 * PR body so they can be re-run.
 *
 *   node scripts/verify-cde-transition-messages.cjs
 *
 * Exits non-zero if any assertion fails.
 */
const { describeTransitionFailure } = require('../.verify-out/utils/cdeTransitionMessage.js');
const { ApiError } = require('../.verify-out/api/apiError.js');

// Exactly src/api/client.ts — `body || \`HTTP ${status}\``.
const wire = (status, body) => new ApiError(status, body || `HTTP ${status}`);

/** The logic on origin/main (documents.tsx:169-179), for the before/after. */
function originMain(err) {
  const msg = err instanceof Error ? err.message : 'Transition failed';
  if (msg.includes('HTTP 403')) {
    return {
      title: 'Approval required',
      body: 'This transition needs BIM Coordinator sign-off. The request has been sent — check back when it is approved.',
    };
  }
  return { title: 'CDE Transition Failed', body: msg };
}

const CASES = [
  {
    label: 'BRANCH 1 — requiresApproval FALSE. POST .../transition {"newStatus":"WIP"} as a Contributor',
    attempt: 'transition',
    status: 403,
    body: '{"message":"Insufficient role for SHARED->WIP transition. Required: Coordinator, Current: Contributor"}',
  },
  {
    label: 'BRANCH 2 — requiresApproval TRUE. POST .../approval-request {"targetState":"PUBLISHED"} as a project author with no active member row',
    attempt: 'approval-request',
    status: 403,
    body: '{"error":"You are not a member of this project"}',
  },
  {
    label: 'BRANCH 2 as origin/main actually calls it — POST .../approvals (no such route)',
    attempt: 'approval-request',
    status: 404,
    body: '',
  },
  {
    label: 'Empty-body 403 — ASP.NET Forbid(), e.g. ProjectMembersController.AddMember',
    attempt: 'transition',
    status: 403,
    body: '',
  },
  {
    label: 'Non-403 refusal — POST .../approval-request {"targetState":"SHARED"}; server does not gate WIP->SHARED',
    attempt: 'approval-request',
    status: 400,
    body: 'Transition WIP->SHARED does not require approval',
  },
  {
    label: 'Network failure — not an ApiError at all',
    attempt: 'transition',
    raw: new TypeError('Network request failed'),
  },
];

const quote = (s) => s.split('\n').map((l) => '        │ ' + l).join('\n');

for (const c of CASES) {
  const err = c.raw ?? wire(c.status, c.body);
  console.log('─'.repeat(104));
  console.log(c.label);
  console.log(`  wire: status=${c.raw ? '(none)' : c.status}  body=${c.raw ? '(none)' : JSON.stringify(c.body)}`);
  const before = originMain(err);
  const after = describeTransitionFailure(err, c.attempt);
  console.log(`\n  origin/main   [${before.title}]`);
  console.log(quote(before.body));
  console.log(`\n  this branch   kind=${after.kind}  [${after.title}]`);
  console.log(quote(after.body));
  console.log();
}

const failures = [];
const check = (name, ok) => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failures.push(name);
};

console.log('═'.repeat(104));

const b1 = describeTransitionFailure(wire(403, CASES[0].body), 'transition');
const b2 = describeTransitionFailure(wire(403, CASES[1].body), 'approval-request');
const empty403 = describeTransitionFailure(wire(403, ''), 'transition');

check('branch 1 no longer claims a request was sent',
  !/request has been sent|check back/i.test(b1.body));
check('branch 1 states explicitly that NOTHING was submitted',
  /Nothing was submitted/.test(b1.body));
check('branch 2 says the request was REFUSED, not sent',
  /refused/i.test(b2.body) && !/has been sent/i.test(b2.body));
check('the two branches do not share a title', b1.title !== b2.title);
check('the two branches do not share a body', b1.body !== b2.body);
check('branch 1 classified forbidden (not error)', b1.kind === 'forbidden');
check('branch 2 classified forbidden (not error)', b2.kind === 'forbidden');
check('both surface the server reason verbatim',
  b1.body.includes('Required: Coordinator, Current: Contributor') &&
  b2.body.includes('You are not a member of this project'));
check('404 is NOT forbidden',
  describeTransitionFailure(wire(404, ''), 'approval-request').kind === 'error');
check('network failure is NOT forbidden',
  describeTransitionFailure(new TypeError('Network request failed'), 'transition').kind === 'error');
check('empty-body 403 is still forbidden, and admits no reason was given',
  empty403.kind === 'forbidden' && empty403.body.includes('gave no reason'));
check('regression witness: origin/main DID emit the false claim on an empty-body 403',
  /request has been sent/.test(originMain(wire(403, '')).body));
check('regression witness: origin/main missed every 403 carrying a reason, and dumped raw JSON',
  originMain(wire(403, CASES[0].body)).title === 'CDE Transition Failed' &&
  originMain(wire(403, CASES[0].body)).body.startsWith('{"message"'));

console.log('═'.repeat(104));
console.log(failures.length === 0 ? 'ALL ASSERTIONS PASSED' : `FAILED: ${failures.join(' | ')}`);
process.exit(failures.length === 0 ? 0 : 1);
