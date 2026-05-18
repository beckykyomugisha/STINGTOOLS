// sync-wwwroot.mjs — Copy the five build artefacts that the per-build
// MSBuild target in Planscape.API.csproj CAN'T sync (because dist/ is
// gitignored and they're not in source) into wwwroot/, so refreshing
// three.js is one command instead of a multi-step manual ritual.
//
// Usage: `npm run sync-wwwroot` from Planscape/assets/viewer/.
// Runs `build` first, then this script.

import { copyFileSync, existsSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const here = dirname(fileURLToPath(import.meta.url));
// Planscape/assets/viewer/ → Planscape.Server/src/Planscape.API/wwwroot/
const wwwroot = join(here, '..', '..', '..', 'Planscape.Server', 'src', 'Planscape.API', 'wwwroot');

if (!existsSync(wwwroot)) {
  console.error(`✗ wwwroot not found at ${wwwroot}`);
  process.exit(1);
}

// These five are build.mjs outputs only — not committed under
// Planscape/assets/viewer/, so the SyncCoordinationViewer MSBuild
// target can't copy them. Everything else (viewer.html / .css / the
// three viewer IIFE files) is already auto-synced on every API build.
const files = [
  'three.min.js',
  'GLTFLoader.js',
  'OrbitControls.js',
  'DRACOLoader.js',
  'MeshoptDecoder.js',
];

for (const f of files) {
  const src = join(here, 'dist', f);
  if (!existsSync(src)) {
    console.error(`✗ ${f} missing — did you run \`npm run build\` first?`);
    process.exit(1);
  }
  copyFileSync(src, join(wwwroot, f));
  console.log(`✓ ${f} → wwwroot/`);
}

console.log('\nSync complete. Commit the updated wwwroot/ files.');
