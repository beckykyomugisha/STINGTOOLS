// build.mjs — Replaces Vite build for the coordination viewer.
// Uses esbuild directly so each entry point can be an IIFE independently
// (Rollup/Vite reject multi-entry IIFE builds due to code-splitting constraints).
import * as esbuild from 'esbuild';
import { mkdirSync, copyFileSync, writeFileSync } from 'fs';

mkdirSync('dist', { recursive: true });

// ── 1. Bundle three.js + only the required addons into a single global ──
// Earlier revisions used `export * from 'three'` which forced esbuild to
// keep every Three.js export in the bundle. The coordination viewer
// only touches 24 core classes (Scene, Mesh, Vector3, etc.) plus the
// four addons. Listing them explicitly lets esbuild tree-shake the
// rest of the library (~50 KB saving on a typical 720 KB three.min.js).
// Keep this list in sync with viewer.html / viewer-extras.js — run
//   grep -oh 'THREE\.[A-Z][A-Za-z0-9]*' viewer.html viewer-extras.js \
//     coordination-viewer.js | sort -u
// to refresh.
const threeEntry = `
export {
  AmbientLight, Box3, BufferGeometry, Clock, Color, DirectionalLight,
  Euler, Group, HemisphereLight, Line, LineBasicMaterial, Mesh,
  MeshBasicMaterial, MeshLambertMaterial, MeshStandardMaterial,
  OrthographicCamera, PerspectiveCamera, Plane, PlaneGeometry, Quaternion,
  Raycaster, Scene, SphereGeometry, Sprite, SpriteMaterial, Vector2, Vector3,
  WebGLRenderer,
} from 'three';
export { GLTFLoader }    from 'three/examples/jsm/loaders/GLTFLoader.js';
export { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
export { TransformControls } from 'three/examples/jsm/controls/TransformControls.js';
export { DRACOLoader }   from 'three/examples/jsm/loaders/DRACOLoader.js';
export { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';
`;

await esbuild.build({
  stdin:      { contents: threeEntry, resolveDir: '.', loader: 'js' },
  bundle:     true,
  format:     'iife',
  globalName: 'THREE',
  outfile:    'dist/three.min.js',
  minify:     true,
  sourcemap:  true,
  target:     'es2020',
}).then(() => console.log('✓ dist/three.min.js (+ .map)'));

// ── 2. Minify the three IIFE viewer files (no bundling — they have no imports) ──
// External source maps so prod stack traces map back to the original
// line numbers when uploaded to Sentry. See upload-sourcemaps.mjs.
//
// `format` is DECLARED, not inferred (#309). Without it these three files built
// 13 bytes larger in a repo checkout than in the Docker viewer stage, and the
// gate in .github/workflows/viewer-served-artifact.yml caught it by comparing
// served bytes against a fresh build.
//
// The 13 bytes are a `"use strict";` prologue. esbuild walks UP from each entry
// point looking for a tsconfig.json and applies its compilerOptions to .js files
// too; Planscape/tsconfig.json sets `"strict": true`, which implies TypeScript's
// alwaysStrict, so esbuild emits the directive. The Dockerfile does
// `WORKDIR /viewer` and copies the sources in flat, so in the container there is
// no tsconfig.json above them and no directive is emitted. Same esbuild (0.24.2,
// pinned by the committed lockfile), same sources, same platform — different
// ancestry, different bytes.
//
// Isolated by single-variable test: an out-of-repo replica of the directory
// nesting built at 3108 bytes; adding ONLY Planscape/tsconfig.json to it flipped
// it to 3121, matching the repo.
//
// 'esm' rather than 'cjs' or 'iife':
//   * it reproduces what production already serves byte-for-byte, so declaring it
//     changes no deployed artifact — the alternative would edit a working
//     deployment to satisfy a check, which is backwards;
//   * package.json here already declares "type": "module", so esm is the file's
//     declared type, not a convenient one;
//   * 'iife' would DOUBLE-WRAP — these sources are already hand-written
//     `(function(){…})()` IIFEs, so it builds 11 bytes larger again and matches
//     neither environment. Measured, not assumed.
//
// HAZARD: viewer.html loads these with a plain <script src>, not
// type="module". None of the three has a top-level import/export today, so esm
// output is a classic script. Adding one would silently break the page — the
// served-artifact gate compares real bytes and will catch it.
for (const name of ['coordination-viewer', 'signalr-shim', 'viewer-extras']) {
  await esbuild.build({
    entryPoints: [`${name}.js`],
    bundle:    false,
    format:    'esm',
    outfile:   `dist/${name}.js`,
    minify:    true,
    sourcemap: true,
    target:    'es2020',
  }).then(() => console.log(`✓ dist/${name}.js (+ .map)`));
}

// ── 3. Copy CSS ────────────────────────────────────────────────────────
copyFileSync('coordination-viewer.css', 'dist/coordination-viewer.css');
console.log('✓ dist/coordination-viewer.css');

// ── 4. Stub files for GLTFLoader.js etc. — already bundled into three.min.js ──
// These stubs prevent 404 errors for the <script onerror> tags in viewer.html.
for (const stub of ['GLTFLoader.js', 'OrbitControls.js', 'TransformControls.js', 'DRACOLoader.js', 'MeshoptDecoder.js']) {
  writeFileSync(`dist/${stub}`, `/* bundled into three.min.js */`);
}
console.log('✓ stubs written');

console.log('\nBuild complete → dist/');
