// LOAD-01 — k6 scenario simulating 50 concurrent site users sharing updates.
//
// Run against a populated dev server:
//   BASE_URL=http://localhost:5000 \
//   PLANSCAPE_EMAIL=admin@planscape.demo \
//   PLANSCAPE_PASSWORD=admin123 \
//   PROJECT_ID=<guid> \
//   k6 run load/site-sharing.js
//
// Two scenarios run in parallel:
//   - create_issue : 1 issue / 30s per VU  (mirrors site snag rate)
//   - list_issues  : 1 list / 5s  per VU  (dashboards + pull-to-refresh)

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { Counter, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const EMAIL = __ENV.PLANSCAPE_EMAIL || 'admin@planscape.demo';
const PASSWORD = __ENV.PLANSCAPE_PASSWORD || 'admin123';
const PROJECT_ID = __ENV.PROJECT_ID; // required — no default

if (!PROJECT_ID) {
  throw new Error('PROJECT_ID env var is required');
}

const createdCounter = new Counter('issues_created');
const createLatency = new Trend('create_ms', true);
const listLatency = new Trend('list_ms', true);

export const options = {
  scenarios: {
    create_issue: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 10 },
        { duration: '1m',  target: 50 },
        { duration: '1m',  target: 50 },
        { duration: '30s', target: 0 },
      ],
      exec: 'createIssue',
    },
    list_issues: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 20 },
        { duration: '2m',  target: 100 },
        { duration: '30s', target: 0 },
      ],
      exec: 'listIssues',
    },
  },
  thresholds: {
    'http_req_failed':   ['rate<0.02'],   // <2% errors
    'http_req_duration': ['p(95)<1500'],  // p95 under 1.5s
    'create_ms':         ['p(95)<2000'],
    'list_ms':           ['p(95)<800'],
  },
};

function login() {
  const res = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({ email: EMAIL, password: PASSWORD }), {
    headers: { 'Content-Type': 'application/json' },
  });
  check(res, { 'login 200': r => r.status === 200 });
  return res.json('token');
}

export function setup() {
  const token = login();
  return { token };
}

export function createIssue(data) {
  const res = http.post(
    `${BASE_URL}/api/projects/${PROJECT_ID}/issues`,
    JSON.stringify({
      type: 'RFI',
      title: `k6 site update ${__VU}-${__ITER}`,
      description: 'Automated load test issue',
      priority: 'MEDIUM',
      latitude: 51.507 + Math.random() * 0.01,
      longitude: -0.127 + Math.random() * 0.01,
      locationAccuracy: 15,
      deviceId: `k6-vu-${__VU}`,
      source: 'mobile',
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${data.token}`,
        'X-Device-Id': `k6-vu-${__VU}`,
      },
      tags: { endpoint: 'create_issue' },
    },
  );
  createLatency.add(res.timings.duration);
  if (check(res, { 'issue created': r => r.status === 200 || r.status === 201 })) {
    createdCounter.add(1);
  }
  sleep(30 + Math.random() * 5);
}

export function listIssues(data) {
  const res = http.get(
    `${BASE_URL}/api/projects/${PROJECT_ID}/issues?page=1&pageSize=50`,
    {
      headers: { 'Authorization': `Bearer ${data.token}` },
      tags: { endpoint: 'list_issues' },
    },
  );
  listLatency.add(res.timings.duration);
  check(res, { 'list 200': r => r.status === 200 });
  sleep(5 + Math.random());
}
