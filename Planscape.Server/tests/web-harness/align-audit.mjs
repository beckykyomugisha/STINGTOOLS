// Headless verification harness for the Planscape web SPA + 3D viewer fixes
// landed for docs/PLANSCAPE_ALIGNMENT_AUDIT.md.
//
// Playwright/jsdom are not available on this host, so rather than drive a real
// browser this harness (a) syntax-checks every modified wwwroot script and
// (b) extracts the pure functions/literals the findings touch and exercises
// them inside a `vm` sandbox with mocked globals. It tests the SHIPPED source
// (read off disk), not a copy — a regression in any asserted behavior fails CI.
//
// Run: node Planscape.Server/tests/web-harness/align-audit.mjs
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const WWW = path.resolve(__dirname, '../../src/Planscape.API/wwwroot');

let failures = 0;
function ok(name, cond, detail = '') {
  if (cond) { console.log(`  PASS  ${name}`); }
  else { failures++; console.log(`  FAIL  ${name}${detail ? ' — ' + detail : ''}`); }
}
function read(rel) { return fs.readFileSync(path.join(WWW, rel), 'utf8'); }

// base64url-encode a JS object as a fake JWT payload segment.
function fakeJwt(claims) {
  const b64 = Buffer.from(JSON.stringify(claims)).toString('base64')
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `h.${b64}.s`;
}

console.log('— syntax (node --check) —');
for (const f of ['signalr-shim.js', 'js/dashboard.js', 'coordination-viewer.js']) {
  try { execFileSync(process.execPath, ['--check', path.join(WWW, f)]); ok(`syntax ${f}`, true); }
  catch (e) { ok(`syntax ${f}`, false, String(e.stderr || e.message).split('\n')[0]); }
}

console.log('\n— Finding #6: shim relays the server "Notification" event —');
{
  const shim = read('signalr-shim.js');
  // The conn.on registration list must contain 'Notification' and must NOT
  // contain the stale 'NotificationCreated' literal the server never emits.
  ok("shim subscribes to 'Notification'", /['"]Notification['"]\s*,/.test(shim));
  ok("shim no longer subscribes to 'NotificationCreated'", !/['"]NotificationCreated['"]/.test(shim));
}

console.log('\n— Finding #17: dashboard seeds planscape_tenant from the JWT —');
{
  const src = read('js/dashboard.js');
  // Extract the seedTenantFromToken function source and run it in a sandbox.
  const m = src.match(/function seedTenantFromToken\(token\)\s*\{[\s\S]*?\n  \}/);
  ok('seedTenantFromToken present', !!m);
  if (m) {
    function runWith(token) {
      const store = {};
      const ctx = vm.createContext({
        localStorage: { setItem: (k, v) => store[k] = v, getItem: k => store[k] ?? null, removeItem: k => delete store[k] },
        atob: (b64) => Buffer.from(b64, 'base64').toString('binary'),
        JSON, TENANT_KEY: 'planscape_tenant', console, __token: token,
      });
      vm.runInContext(m[0] + '\nseedTenantFromToken(__token);', ctx);
      return store['planscape_tenant'];
    }
    ok('valid JWT → tenant stored', runWith(fakeJwt({ tenant_id: 'tenant-abc' })) === 'tenant-abc');
    ok('JWT with tid claim → tenant stored', runWith(fakeJwt({ tid: 'tenant-xyz' })) === 'tenant-xyz');
    ok('malformed token → no throw + unset', runWith('not-a-jwt') === undefined);
    ok('empty token → no throw + unset', runWith('') === undefined);
  }
  ok('setTokens seeds tenant', /function setTokens[\s\S]*?seedTenantFromToken\(t\)/.test(src));
  ok('clearTokens clears tenant', /function clearTokens[\s\S]*?removeItem\(TENANT_KEY\)/.test(src));
}

console.log('\n— Finding #18: boot() catch splits 401 vs unreachable —');
{
  const src = read('js/dashboard.js');
  ok('top-level catch distinguishes unauthenticated',
    /boot\(\)\.catch\(e\s*=>\s*\(e && e\.unauthenticated\)\s*\?\s*showLogin\(\)\s*:\s*renderServerUnreachable\(e\)\)/.test(src));
}

console.log('\n— Finding #4: coordination-viewer navigations target the hash-routed SPA —');
{
  const cv = read('coordination-viewer.js');
  ok('no bare /app/projects navigation literal', !/['"`][^'"`]*\/app\/projects['"`]/.test(cv)
     && !/\$\{apiBase\}\/app\/projects/.test(cv));
  ok('no bare /projects navigation literal', !/\(apiBase \|\| ''\) \+ '\/projects'/.test(cv));
  ok('uses /app/#models?project= for project crumb', /\/app\/#models\?project=/.test(cv));
  ok('uses /app/#overview for home/list', /\/app\/#overview/.test(cv));
}

console.log('\n— Finding #1: viewer guards non-glTF formats before GLTFLoader —');
{
  const cv = read('coordination-viewer.js');
  ok('format guard references activeModel.format', /activeModel\.format/.test(cv));
  ok('format guard mentions GLB', /\bGLB\b/i.test(cv));
}

console.log('\n— Finding #15: viewer honours element/camera deep-link grammar —');
{
  const cv = read('coordination-viewer.js');
  ok("reads 'element' query param", /params\.get\('element'\)/.test(cv));
  ok("reads 'camera' query param", /params\.get\('camera'\)/.test(cv));
}

console.log(`\n${failures === 0 ? 'ALL WEB HARNESS CHECKS PASSED' : failures + ' WEB HARNESS CHECK(S) FAILED'}`);
process.exit(failures === 0 ? 0 : 1);
