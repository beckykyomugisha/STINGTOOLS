// TRACK B / P3 — mesh-unit reconciliation, checked against the SHIPPED sources.
//
// Two things are verified here that no C# unit test can reach:
//
//  1. The viewer applies the mesh-unit scale UNCONDITIONALLY — a millimetre
//     model needs rescaling whether or not it is georeferenced, so it must not
//     sit behind the "is this transform live" gate.
//
//  2. The two Revit GLB writers agree on the unit. They disagreed by 1000x
//     (GlbSerializer: feet → metres; RevitGltfExporter: feet → millimetres),
//     both feeding the same endpoint and the same viewer. A model viewed alone
//     hides it completely, because the camera fits to whatever bounds it finds;
//     it only surfaces when you federate, and then it reads as "the models
//     don't line up" rather than as a unit bug. A grep-level guard is crude but
//     it is the only thing that fails when someone edits one writer and not the
//     other.
//
// Run: node Planscape.Server/tests/web-harness/mesh-units.mjs
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const WWW  = path.resolve(__dirname, '../../src/Planscape.API/wwwroot');
// …/Planscape.Server/tests/web-harness → repo root.
const REPO = path.resolve(__dirname, '../../..');

let failures = 0;
function ok(name, cond, detail = '') {
  if (cond) { console.log(`  PASS  ${name}`); }
  else { failures++; console.log(`  FAIL  ${name}${detail ? ' — ' + detail : ''}`); }
}

const html = fs.readFileSync(path.join(WWW, 'viewer.html'), 'utf8');
const m = html.match(/function applyModelTransform\(root, t\)\s*\{[\s\S]*?\n  \}/);
if (!m) { console.log('  FAIL  could not extract applyModelTransform'); process.exit(1); }

function fakeRoot() {
  return {
    scaled: null, positioned: null, moved: false,
    scale: { set(x) { this._owner.scaled = x; } },
    rotation: { z: 0 },
    position: { set(x, y, z) { this._owner.positioned = [x, y, z]; } },
    updateMatrixWorld() { this.moved = true; },
  };
}
function apply(t) {
  const root = fakeRoot();
  root.scale._owner = root; root.position._owner = root;
  const ctx = vm.createContext({ Math, console, __root: root, __t: t });
  vm.runInContext(m[0] + '\napplyModelTransform(__root, __t);', ctx);
  return root;
}

const PLACED = {
  translationX: 500000, translationY: 250000, translationZ: 0,
  rotationDeg: 0, scaleFactor: 1, isConfirmed: true,
};

console.log('— a millimetre mesh is rescaled even with NO transform —');
{
  // The case that makes this unconditional: an ungeoreferenced mm model.
  const r = apply({ hasTransform: false, translationX: 0, translationY: 0, translationZ: 0,
                    rotationDeg: 0, scaleFactor: 1, isConfirmed: false,
                    appliedAutomatically: false, meshUnitScale: 0.001 });
  ok('scale applied without a live transform', r.scaled === 0.001, String(r.scaled));
  ok('no position applied (there is no placement)', r.positioned === null);
}

console.log('\n— a metre mesh is left alone —');
{
  const r = apply({ hasTransform: false, translationX: 0, translationY: 0, translationZ: 0,
                    rotationDeg: 0, scaleFactor: 1, isConfirmed: false,
                    appliedAutomatically: false, meshUnitScale: 1 });
  ok('identity + metres → untouched', r.moved === false && r.scaled === null);
}

console.log('\n— mesh unit and georef scale multiply, they do not replace —');
{
  const r = apply({ ...PLACED, scaleFactor: 2, meshUnitScale: 0.001 });
  ok('scaleFactor 2 x meshUnitScale 0.001 = 0.002', Math.abs(r.scaled - 0.002) < 1e-12, String(r.scaled));
  ok('placement still applied', r.positioned && r.positioned[0] === 500);
}

console.log('\n— an absent meshUnitScale defaults to metres —');
{
  const r = apply({ ...PLACED });
  ok('no meshUnitScale → scale 1 (unchanged behaviour)',
     r.scaled === null || r.scaled === 1, String(r.scaled));
  ok('placement unaffected', r.positioned && r.positioned[0] === 500);
}

console.log('\n— the two Revit GLB writers agree on the unit —');
{
  const exporter = fs.readFileSync(path.join(REPO, 'StingTools/BIMManager/RevitGltfExporter.cs'), 'utf8');
  const serializer = fs.readFileSync(path.join(REPO, 'StingTools/Commands/IFC/GlbSerializer.cs'), 'utf8');

  ok('GlbSerializer writes metres', /FeetToMetres\s*=\s*0\.3048f?/.test(serializer));
  ok('RevitGltfExporter writes metres', /FeetToMetres\s*=\s*0\.3048/.test(exporter));
  ok('RevitGltfExporter no longer scales vertices by 304.8',
     !/Positions\.Add\(\(float\)\(w\.[XYZ] \* FeetToMm\)\)/.test(exporter),
     'vertices are still being written in millimetres');

  // The bounds describe the same geometry, so they must use the same unit or
  // the manifest AABB disagrees with what is rendered.
  const publish = fs.readFileSync(path.join(REPO, 'StingTools/BIMManager/PublishModelCommand.cs'), 'utf8');
  ok('published bounds are metres', /feetToMetres\s*=\s*0\.3048/.test(publish));
  ok('published units are reported as "m"', /units:\s*"m"/.test(publish));
}

console.log(failures === 0 ? '\nALL PASS' : `\n${failures} FAILURE(S)`);
process.exit(failures === 0 ? 0 : 1);
