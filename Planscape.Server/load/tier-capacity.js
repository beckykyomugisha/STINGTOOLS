// CAPACITY-01 — measure what a Render instance type actually holds, so the
// tier table in docs/DEPLOY_RUNBOOK.md is measured rather than modelled.
//
// This answers one question: at a given CPU/RAM limit, how many requests per
// second can the API sustain while staying inside an acceptable p95 — and how
// many ACTIVE coordinators does that convert to?
//
// It deliberately uses a ramping-arrival-rate executor, not ramping-vus.
// VU-based load self-throttles: as the server slows, VUs send fewer requests
// and the server never actually saturates, so you measure the client rather
// than the server. Arrival-rate holds the offered load regardless of how the
// server feels about it, which is what finds the knee.
//
// ── Run ────────────────────────────────────────────────────────────────────
// Bring up an API pinned to the tier you want to measure (see
// docker/docker-compose.loadtest.yml), then:
//
//   1. Seed users + mint tokens (once):
//        see docs/DEPLOY_RUNBOOK.md § Measuring tier capacity
//   2. docker run --rm --network host -v "$PWD/load:/load" \
//        -e BASE_URL=http://localhost:5000 \
//        -e PROJECT_ID=<guid> \
//        grafana/k6 run /load/tier-capacity.js
//
// Override the ramp ceiling for bigger tiers (seed more users first — see the
// rate-limit ceiling note on TOKENS below):
//   -e PEAK_RPS=400
//
// ── Reading the result ─────────────────────────────────────────────────────
// The summary prints "sustained RPS" — the highest offered rate where
// http_req_failed stayed at 0 and p95 stayed under P95_BUDGET_MS. Convert:
//
//   active coordinators = sustained_rps * 60 / REQ_PER_COORDINATOR_MIN
//
// ── Honest limits of this measurement ──────────────────────────────────────
//   - Your CPU core is faster than a shared cloud vCPU. Treat the result as an
//     UPPER BOUND for the equivalent Render tier and derate.
//   - The seeded dev database is small. Real projects have far more issues per
//     project, and these endpoints have deep .Include() chains, so per-request
//     cost grows with data volume. Re-measure against production-sized data.
//   - Client and server share a host, so k6's own CPU competes with the API.
//     At high RPS this understates server capacity.

import http from 'k6/http';
import { check } from 'k6';
import { SharedArray } from 'k6/data';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const PROJECT_ID = __ENV.PROJECT_ID;
const TOKEN_FILE = __ENV.TOKEN_FILE || '/load/loadtest-tokens.json';
const RESULT_FILE = __ENV.RESULT_FILE || '/load/tier-capacity-result.json';

// The planning assumption under test. docs/DEPLOY_RUNBOOK.md sizes tiers on
// ~10 requests per minute for a coordinator actively working issues/markup.
const REQ_PER_COORDINATOR_MIN = Number(__ENV.REQ_PER_COORDINATOR_MIN || 10);

// Above this p95 the UI feels sluggish; that is the definition of "sustained".
const P95_BUDGET_MS = Number(__ENV.P95_BUDGET_MS || 800);
const PEAK_RPS = Number(__ENV.PEAK_RPS || 200);

if (!PROJECT_ID) throw new Error('PROJECT_ID env var is required');

const listLatency = new Trend('coordinator_mix_ms', true);
const okRate = new Rate('coordinator_mix_ok');
const reqs = new Counter('coordinator_mix_reqs');

export const options = {
  scenarios: {
    // Step the offered rate up in stages. Each plateau is long enough to see
    // whether the server holds it or falls behind.
    find_the_knee: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 400,
      stages: [
        { duration: '20s', target: Math.round(PEAK_RPS * 0.10) },
        { duration: '30s', target: Math.round(PEAK_RPS * 0.25) },
        { duration: '30s', target: Math.round(PEAK_RPS * 0.50) },
        { duration: '30s', target: Math.round(PEAK_RPS * 0.75) },
        { duration: '30s', target: PEAK_RPS },
        { duration: '20s', target: 0 },
      ],
    },
  },
  thresholds: {
    // Not pass/fail gates for CI — these mark where the knee is in the output.
    'http_req_failed': ['rate<0.01'],
    'coordinator_mix_ms': [`p(95)<${P95_BUDGET_MS}`],
  },
};

// A POOL of tokens, one per seeded user — not a single login.
//
// This is load-bearing, not a convenience. MapControllers().RequireRateLimiting("api")
// partitions a 100 req/min sliding window by the `sub` claim, so driving every
// request through one account measures the rate limiter, not the server: an
// early run of this script reported 91 req/s with a 97.95% failure rate and a
// 2 ms p95, which is the signature of instant 429s, not of capacity.
//
// The token count therefore sets a hard ceiling on offered load:
//   max measurable RPS = users * 100 / 60
// 60 users ≈ 100 req/s. Seed more users if you need to push a bigger tier.
const TOKENS = new SharedArray('tokens', function () {
  return JSON.parse(open(TOKEN_FILE));
});

export function setup() {
  if (TOKENS.length === 0) throw new Error(`no tokens in ${TOKEN_FILE}`);
  const ceiling = Math.floor((TOKENS.length * 100) / 60);
  console.log(`${TOKENS.length} tokens loaded — rate-limit ceiling ≈ ${ceiling} req/s`);
  if (PEAK_RPS > ceiling) {
    console.warn(
      `PEAK_RPS=${PEAK_RPS} exceeds the ${ceiling} req/s rate-limit ceiling. ` +
      `Results above that rate measure 429s, not capacity. Seed more users.`);
  }
  return {};
}

export default function () {
  // Deterministic round-robin, NOT Math.random(). Random selection clusters:
  // some users land well over their 100 req/min budget while others idle, so
  // you start collecting 429s at maybe 60% of the theoretical ceiling and
  // mistake rate-limiting for saturation. Round-robin spreads evenly.
  const token = TOKENS[(__VU + __ITER) % TOKENS.length];
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };

  // Weighted to mirror a coordinator's real read pattern: the issue list is
  // the screen they live on, project detail is the dashboard, members is
  // occasional. All four are GETs with .Include() chains — the endpoints that
  // dominate CPU on this app (IssuesController has 8, MeetingsController 12).
  const roll = Math.random();
  let url;
  if (roll < 0.50)      url = `${BASE_URL}/api/projects/${PROJECT_ID}/issues`;
  else if (roll < 0.80) url = `${BASE_URL}/api/projects/${PROJECT_ID}`;
  else if (roll < 0.95) url = `${BASE_URL}/api/projects`;
  else                  url = `${BASE_URL}/api/projects/${PROJECT_ID}/members`;

  const res = http.get(url, { headers });

  listLatency.add(res.timings.duration);
  okRate.add(res.status === 200);
  reqs.add(1);

  check(res, {
    'status 200': (r) => r.status === 200,
    // 500s here are the tell for connection-pool exhaustion:
    // Postgres 53300 "sorry, too many clients already".
    'not a server error': (r) => r.status < 500,
  });
}

export function handleSummary(data) {
  const m = data.metrics;
  const p95 = m.coordinator_mix_ms ? m.coordinator_mix_ms.values['p(95)'] : NaN;
  const rps = m.http_reqs ? m.http_reqs.values.rate : NaN;
  const failRate = m.http_req_failed ? m.http_req_failed.values.rate : NaN;
  const total = m.http_reqs ? m.http_reqs.values.count : 0;

  const activeCoordinators = Math.floor((rps * 60) / REQ_PER_COORDINATOR_MIN);

  const lines = [
    '',
    '══════════════════════════════════════════════════════════════',
    '  TIER CAPACITY RESULT',
    '══════════════════════════════════════════════════════════════',
    `  requests total        ${total}`,
    `  mean throughput       ${rps.toFixed(1)} req/s`,
    `  p95 latency           ${p95.toFixed(0)} ms  (budget ${P95_BUDGET_MS} ms)`,
    `  failure rate          ${(failRate * 100).toFixed(2)} %`,
    '  ──────────────────────────────────────────────────────────',
    `  → active coordinators ~${activeCoordinators}`,
    `    (at ${REQ_PER_COORDINATOR_MIN} req/min each)`,
    '══════════════════════════════════════════════════════════════',
    p95 > P95_BUDGET_MS
      ? '  NOTE: p95 over budget — the offered peak exceeded capacity.'
      : '  NOTE: p95 within budget at peak — raise PEAK_RPS and re-run to',
    p95 > P95_BUDGET_MS
      ? '  Re-run with a lower PEAK_RPS to find the sustainable rate.'
      : '  find the actual ceiling; this run did not reach it.',
    '',
  ];

  const out = {};
  out.stdout = lines.join('\n');
  // Written next to the mounted script. A relative path here resolves against
  // k6's cwd, which inside the container is "/", not the repo — so the write
  // silently failed with ENOENT on every run.
  out[RESULT_FILE] = JSON.stringify(data, null, 2);
  return out;
}
