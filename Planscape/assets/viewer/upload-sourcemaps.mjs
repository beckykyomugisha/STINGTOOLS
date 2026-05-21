// upload-sourcemaps.mjs — Upload dist/*.map to Sentry so production
// stack traces from the coordination viewer resolve back to the
// unminified source. Sentry is the default target because it's what
// Planscape already wires into the .NET API via OpenTelemetry; other
// backends (Honeycomb, Datadog, custom) can use the same release-based
// HTTP shape with a different host.
//
// Required env vars:
//   SENTRY_AUTH_TOKEN  — project-scoped token with `project:releases` scope
//   SENTRY_ORG         — Sentry organisation slug (e.g. "planscape")
//   SENTRY_PROJECT     — Sentry project slug (e.g. "planscape-viewer")
//   SENTRY_RELEASE     — release identifier; defaults to short git SHA
//                        (or "viewer-<timestamp>" if git isn't available)
// Optional:
//   SENTRY_HOST        — defaults to "sentry.io" (use your self-hosted host
//                        like "sentry.acme.com" if you're not on SaaS)
//   SENTRY_URL_PREFIX  — defaults to "~/" so the maps match any host;
//                        set to e.g. "https://planscape.app/" for hard pinning
//
// Behaviour: when SENTRY_AUTH_TOKEN is missing the script prints a
// helpful no-op message and exits 0 so CI pipelines that haven't
// configured Sentry yet keep passing. When the token IS set, missing
// SENTRY_ORG/SENTRY_PROJECT fails fast with exit 1.
//
// Usage: `npm run upload-sourcemaps` (runs after `npm run build`).

import { readFileSync, readdirSync, existsSync, statSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import { execSync } from 'child_process';

const here = dirname(fileURLToPath(import.meta.url));
const distDir = join(here, 'dist');

const token   = process.env.SENTRY_AUTH_TOKEN;
const org     = process.env.SENTRY_ORG;
const project = process.env.SENTRY_PROJECT;
const host    = process.env.SENTRY_HOST || 'sentry.io';
const urlPrefix = process.env.SENTRY_URL_PREFIX || '~/';

if (!token) {
  console.log('ℹ SENTRY_AUTH_TOKEN not set — skipping source-map upload.');
  console.log('  Set SENTRY_AUTH_TOKEN + SENTRY_ORG + SENTRY_PROJECT in CI');
  console.log('  to enable production stack-trace symbolication.');
  process.exit(0);
}

if (!org || !project) {
  console.error('✗ SENTRY_AUTH_TOKEN is set but SENTRY_ORG / SENTRY_PROJECT are missing.');
  console.error('  Set both to upload, or unset SENTRY_AUTH_TOKEN to skip.');
  process.exit(1);
}

if (!existsSync(distDir) || !statSync(distDir).isDirectory()) {
  console.error(`✗ ${distDir} not found — run \`npm run build\` first.`);
  process.exit(1);
}

// Resolve release identifier. Prefer a short git SHA so each commit
// uploads against its own bucket; fall back to a timestamp when the
// working tree isn't a git checkout (CI snapshots, docker contexts).
function resolveRelease() {
  if (process.env.SENTRY_RELEASE) return process.env.SENTRY_RELEASE;
  try {
    return 'viewer-' + execSync('git rev-parse --short=12 HEAD', {
      cwd: here, stdio: ['ignore', 'pipe', 'ignore'],
    }).toString().trim();
  } catch (_) {
    return 'viewer-' + new Date().toISOString().replace(/[:.]/g, '-');
  }
}
const release = resolveRelease();

const apiBase = `https://${host}/api/0/projects/${encodeURIComponent(org)}/${encodeURIComponent(project)}/releases`;

async function ensureRelease() {
  const res = await fetch(`${apiBase}/`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type':  'application/json',
    },
    body: JSON.stringify({ version: release }),
  });
  // 201 = created, 208/409 = already exists (also OK), anything else = fatal.
  if (res.status >= 200 && res.status < 300) return;
  if (res.status === 208 || res.status === 409) return;
  const body = await res.text();
  throw new Error(`Create release failed: ${res.status} ${body}`);
}

async function uploadFile(filePath, relName) {
  // multipart/form-data — use the built-in FormData (Node 18+).
  const buf = readFileSync(filePath);
  const form = new FormData();
  form.append('name', `${urlPrefix}${relName}`);
  form.append('file', new Blob([buf]), relName);
  const res = await fetch(
    `${apiBase}/${encodeURIComponent(release)}/files/`,
    {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` },
      body: form,
    });
  if (res.status === 409) return 'exists';   // dedupe — already uploaded
  if (res.status >= 200 && res.status < 300) return 'uploaded';
  const body = await res.text();
  throw new Error(`Upload ${relName} failed: ${res.status} ${body}`);
}

// Discover every .js + .map pair under dist/. Skip the addon stubs —
// they're 1-liner comment files that don't need source-map coverage.
function collectArtefacts() {
  const all = readdirSync(distDir);
  return all.filter(name => {
    if (!/\.(js|map)$/.test(name)) return false;
    if (/^(GLTFLoader|OrbitControls|DRACOLoader|MeshoptDecoder)\.js$/.test(name)) return false;
    return true;
  });
}

console.log(`Uploading source maps to ${host}/${org}/${project} (release: ${release})`);
await ensureRelease();
const files = collectArtefacts();
let uploaded = 0, existed = 0;
for (const name of files) {
  const result = await uploadFile(join(distDir, name), name);
  if (result === 'uploaded') { console.log(`✓ ${name}`); uploaded++; }
  else                       { console.log(`· ${name} (already uploaded)`); existed++; }
}
console.log(`\nDone — ${uploaded} new, ${existed} already on Sentry. Release: ${release}`);
