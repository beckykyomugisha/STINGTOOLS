/**
 * #625 verification harness.
 *
 * Runs the REAL `src/utils/issueCreateMessage.ts` (transpiled to
 * `.verify-out/`) against responses CAPTURED FROM A RUNNING PLANSCAPE API.
 * Every `wire:` line below is a verbatim status + body observed on 2026-08-06
 * against `docker compose` at http://localhost:5000; the capture commands are
 * in the PR body.
 *
 *   node scripts/verify-issue-create-messages.cjs
 *
 * Exits non-zero if any assertion fails.
 */
const { describeIssueCreateFailure } = require('../.verify-out/utils/issueCreateMessage.js');

// Exactly src/api/client.ts:109-114 and :157-159.
class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; this.name = 'ApiError'; }
}
const wire = (status, body) => new ApiError(status, body || `HTTP ${status}`);

/** The logic on origin/main (issues.tsx:544-554), for the before/after. */
function originMain(err) {
  const msg = err instanceof Error ? err.message : 'Failed to create issue';
  if (msg.includes('HTTP 403') || msg.toLowerCase().includes('geofence')
      || msg.toLowerCase().includes('outside the project')) {
    return 'Outside project geofence — move on site or ask your BIM manager to widen the boundary.';
  }
  if (msg.includes('HTTP 400') && msg.toLowerCase().includes('latitude')) {
    return 'Invalid GPS reading — try again in a moment.';
  }
  if (msg.includes('HTTP 400') && msg.toLowerCase().includes('assignee')) {
    return 'Chosen assignee is not a member of this project.';
  }
  return msg;
}

const CASES = [
  {
    id: 'geofence-403',
    label: 'REAL geofence violation — X-Latitude/X-Longitude outside BoundaryPolygon',
    status: 403,
    body: '{"error":"Device location is outside the project geofence boundary"}',
  },
  {
    id: 'capability-403',
    label: 'CAPABILITY refusal — RequireProjectMemberAsync, project with no geofence configured',
    status: 403,
    body: '{"error":"You are not a member of this project"}',
  },
  {
    id: 'empty-403',
    label: 'Empty-body 403 — ASP.NET Forbid(); the shape the bare "HTTP 403" test was written for',
    status: 403,
    body: '',
  },
  {
    id: 'coords-required-400',
    label: 'Geofenced project, no coordinates sent — this is what mobile createIssue produces today',
    status: 400,
    body: '{"error":"Geofence enforcement is active. Location coordinates are required."}',
  },
  {
    id: 'lat-range-400',
    label: 'Out-of-range latitude header',
    status: 400,
    body: '{"error":"Invalid latitude/longitude range"}',
  },
  {
    id: 'assignee-400',
    label: 'Assignee is not a project member',
    status: 400,
    body: '{"error":"Assignee is not an active member of this project"}',
  },
  {
    id: 'network',
    label: 'Network failure — no status at all',
    raw: new TypeError('Network request failed'),
  },
];

const byId = {};
for (const c of CASES) {
  const err = c.raw ?? wire(c.status, c.body);
  byId[c.id] = describeIssueCreateFailure(err);
  console.log('─'.repeat(104));
  console.log(c.label);
  console.log(`  wire: status=${c.raw ? '(none)' : c.status}  body=${c.raw ? '(none)' : JSON.stringify(c.body)}`);
  console.log(`  origin/main  │ ${originMain(err)}`);
  console.log(`  this branch  │ [${byId[c.id].kind}] ${byId[c.id].message}`);
  console.log();
}

const GEOFENCE = 'Outside project geofence — move on site or ask your BIM manager to widen the boundary.';
const mentionsLocation = (s) => /geofence|move on site|boundary|outside/i.test(s);

const failures = [];
const check = (name, ok) => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failures.push(name);
};

console.log('═'.repeat(104));

// The case that already worked must not regress — verbatim, not paraphrased.
check('a real geofence 403 still produces the EXACT original wording',
  byId['geofence-403'].message === GEOFENCE && byId['geofence-403'].kind === 'geofence');

// The defect.
check('a capability 403 no longer mentions location at all',
  !mentionsLocation(byId['capability-403'].message));
check('a capability 403 is classified forbidden, not geofence',
  byId['capability-403'].kind === 'forbidden');
check('a capability 403 names why, from the server',
  byId['capability-403'].message === 'You are not a member of this project');
check('an empty-body 403 no longer mentions location',
  !mentionsLocation(byId['empty-403'].message));
check('an empty-body 403 is forbidden, and admits no reason was given',
  byId['empty-403'].kind === 'forbidden' && /gave no reason/.test(byId['empty-403'].message));

// The 400 that origin/main also mis-reported as "you are outside the boundary".
check('"coordinates are required" is NOT reported as being outside the boundary',
  byId['coords-required-400'].message !== GEOFENCE &&
  !/move on site/i.test(byId['coords-required-400'].message));
check('"coordinates are required" reports the server reason verbatim',
  byId['coords-required-400'].message === 'Geofence enforcement is active. Location coordinates are required.');

// Two arms that existed but could never fire.
check('the GPS-range arm now fires',
  byId['lat-range-400'].message === 'Invalid GPS reading — try again in a moment.');
check('the assignee arm now fires',
  byId['assignee-400'].message === 'Chosen assignee is not a member of this project.');

check('a network failure is not forbidden and not geofence',
  byId['network'].kind === 'error' && byId['network'].message === 'Network request failed');

// Regression witnesses — reproduce the origin/main behaviour, so this harness
// cannot pass while describing a fix that is not there.
check('witness: origin/main sent an empty-body 403 to the geofence message',
  originMain(wire(403, '')) === GEOFENCE);
check('witness: origin/main sent a CAPABILITY 403 to a raw JSON dump (neither geofence nor honest)',
  originMain(wire(403, CASES[1].body)) === CASES[1].body);
check('witness: origin/main sent "coordinates are required" to the geofence message',
  originMain(wire(400, CASES[3].body)) === GEOFENCE);
check('witness: origin/main GPS-range arm was dead (fell through to a raw JSON dump)',
  originMain(wire(400, CASES[4].body)) === CASES[4].body);
check('witness: origin/main assignee arm was dead (fell through to a raw JSON dump)',
  originMain(wire(400, CASES[5].body)) === CASES[5].body);

console.log('═'.repeat(104));
console.log(failures.length === 0 ? 'ALL ASSERTIONS PASSED' : `FAILED: ${failures.join(' | ')}`);
process.exit(failures.length === 0 ? 0 : 1);
