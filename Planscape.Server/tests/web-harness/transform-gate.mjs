// TRACK B / P1 — headless verification of the viewer's transform gate.
//
// THE DEFECT
// ----------
// `applyModelTransform` in viewer.html decided whether to move a model with:
//
//     const confirmed = (t.isConfirmed !== false);
//     if (!confirmed || isIdentity) return;
//
// Both automatic writers — the IFC ingest path and AutoAlignService — store
// `IsConfirmed = false`. So a correct, survey-derived alignment was computed,
// persisted, sent to the viewer, and then discarded by this one line, every
// time, until a human confirmed it by hand. That is the whole "same-site models
// from different tools don't line up on their own" symptom, and it lives in four
// tokens of JavaScript.
//
// WHAT THIS HARNESS DOES
// ----------------------
// Same approach as align-audit.mjs: Playwright/jsdom are not available on this
// host, so rather than drive a browser it extracts `applyModelTransform` from
// the SHIPPED viewer.html (read off disk, not a copy) and runs it in a `vm`
// sandbox against a fake three.js root that records what was mutated. A
// regression in the gate fails here.
//
// Run: node Planscape.Server/tests/web-harness/transform-gate.mjs
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const WWW = path.resolve(__dirname, '../../src/Planscape.API/wwwroot');

let failures = 0;
function ok(name, cond, detail = '') {
  if (cond) { console.log(`  PASS  ${name}`); }
  else { failures++; console.log(`  FAIL  ${name}${detail ? ' — ' + detail : ''}`); }
}

const html = fs.readFileSync(path.join(WWW, 'viewer.html'), 'utf8');

// ── extract the shipped function ────────────────────────────────────────────
const m = html.match(/function applyModelTransform\(root, t\)\s*\{[\s\S]*?\n  \}/);
if (!m) {
  console.log('  FAIL  could not extract applyModelTransform from viewer.html');
  process.exit(1);
}

/** A minimal stand-in for a three.js Object3D that records mutations. */
function fakeRoot() {
  return {
    moved: false,
    scale: { set(x, y, z) { this._v = [x, y, z]; } },
    rotation: { z: 0 },
    position: { set(x, y, z) { this._v = [x, y, z]; } },
    updateMatrixWorld() { this.moved = true; },
  };
}

/** Run the shipped gate against one transform payload; returns the root. */
function apply(transform) {
  const root = fakeRoot();
  const ctx = vm.createContext({ Math, console, __root: root, __t: transform });
  vm.runInContext(m[0] + '\napplyModelTransform(__root, __t);', ctx);
  return root;
}

const REAL = { translationX: 500000, translationY: 250000, translationZ: 0, rotationDeg: 12, scaleFactor: 1 };

console.log('— B1: an auto-applied transform actually moves the model —');
{
  // The regression case. This is exactly what both automatic writers produce.
  const r = apply({ ...REAL, isConfirmed: false, appliedAutomatically: true });
  ok('appliedAutomatically=true + isConfirmed=false → APPLIED', r.moved === true,
     'this is the original defect: a survey-derived transform that never rendered');
  ok('translation converted mm → metres', r.position._v && r.position._v[0] === 500,
     JSON.stringify(r.position._v));
  ok('rotation applied about Z in radians',
     Math.abs(r.rotation.z - (12 * Math.PI / 180)) < 1e-12);
}

console.log('\n— manual confirmation still applies (unchanged behaviour) —');
{
  const r = apply({ ...REAL, isConfirmed: true, appliedAutomatically: false });
  ok('isConfirmed=true → APPLIED', r.moved === true);
}
{
  // Older/partial payloads that omit the field must behave as before.
  const r = apply({ ...REAL });
  ok('isConfirmed absent → APPLIED (back-compat default)', r.moved === true);
}

console.log('\n— a low-confidence suggestion must NOT move the model —');
{
  // The other half of the contract. A model with no usable georeference stays at
  // the origin, where it is obviously un-placed, rather than being scattered
  // across the site by a guess.
  const r = apply({ ...REAL, isConfirmed: false, appliedAutomatically: false });
  ok('isConfirmed=false + appliedAutomatically=false → NOT applied', r.moved === false);
}
{
  const r = apply({ ...REAL, isConfirmed: false, appliedAutomatically: 'yes' });
  ok('appliedAutomatically must be strictly true (no truthy coercion)', r.moved === false);
}

console.log('\n— identity and absent transforms remain no-ops —');
{
  const r = apply({ translationX: 0, translationY: 0, translationZ: 0, rotationDeg: 0,
                    scaleFactor: 1, isConfirmed: true, appliedAutomatically: true });
  ok('identity transform → NOT applied', r.moved === false);
}
{
  const r = fakeRoot();
  const ctx = vm.createContext({ Math, console, __root: r, __t: null });
  vm.runInContext(m[0] + '\napplyModelTransform(__root, __t);', ctx);
  ok('null transform → NOT applied, no throw', r.moved === false);
}

console.log('\n— the gate reads the field the server actually sends —');
{
  // Guards against the two halves drifting: the server projects
  // `appliedAutomatically` (camelCase) in ModelTransformController.
  ok('viewer.html references appliedAutomatically', /appliedAutomatically/.test(html));
  const ctrl = fs.readFileSync(
    path.resolve(__dirname, '../../src/Planscape.API/Controllers/ModelTransformController.cs'), 'utf8');
  ok('ModelTransformController projects appliedAutomatically',
     /appliedAutomatically\s*=/.test(ctrl));
}

console.log(failures === 0 ? '\nALL PASS' : `\n${failures} FAILURE(S)`);
process.exit(failures === 0 ? 0 : 1);
