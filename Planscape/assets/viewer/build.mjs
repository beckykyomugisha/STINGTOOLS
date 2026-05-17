// build.mjs — Replaces Vite build for the coordination viewer.
// Uses esbuild directly so each entry point can be an IIFE independently
// (Rollup/Vite reject multi-entry IIFE builds due to code-splitting constraints).
import * as esbuild from 'esbuild';
import { mkdirSync, copyFileSync, writeFileSync } from 'fs';

mkdirSync('dist', { recursive: true });

// ── 1. Bundle three.js + all required addons into a single global ──────
// Use re-export pattern + globalName=THREE so esbuild sets window.THREE
// to an object containing all Three.js exports plus the addon classes.
const threeEntry = `
export * from 'three';
export { GLTFLoader }    from 'three/examples/jsm/loaders/GLTFLoader.js';
export { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
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
  target:     'es2020',
}).then(() => console.log('✓ dist/three.min.js'));

// ── 2. Minify the three IIFE viewer files (no bundling — they have no imports) ──
for (const name of ['coordination-viewer', 'signalr-shim', 'viewer-extras']) {
  await esbuild.build({
    entryPoints: [`${name}.js`],
    bundle:  false,
    outfile: `dist/${name}.js`,
    minify:  true,
    target:  'es2020',
  }).then(() => console.log(`✓ dist/${name}.js`));
}

// ── 3. Copy CSS ────────────────────────────────────────────────────────
copyFileSync('coordination-viewer.css', 'dist/coordination-viewer.css');
console.log('✓ dist/coordination-viewer.css');

// ── 4. Stub files for GLTFLoader.js etc. — already bundled into three.min.js ──
// These stubs prevent 404 errors for the <script onerror> tags in viewer.html.
for (const stub of ['GLTFLoader.js', 'OrbitControls.js', 'DRACOLoader.js', 'MeshoptDecoder.js']) {
  writeFileSync(`dist/${stub}`, `/* bundled into three.min.js */`);
}
console.log('✓ stubs written');

console.log('\nBuild complete → dist/');
