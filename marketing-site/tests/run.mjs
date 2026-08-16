// Test runner for the Pages Functions.
//
// The Functions are TypeScript with extensionless imports, which Node cannot
// resolve on its own, so esbuild bundles the test (and everything it pulls in)
// into one ESM file first. miniflare stays external — it provides a REAL D1,
// i.e. the same SQLite engine the deployed Function talks to, so a test that
// passes here is testing the actual SQL and not a hand-written stand-in.
//
//   npm test

import * as esbuild from "esbuild";
import { spawn } from "node:child_process";

const entry = process.argv[2] ?? "tests/license.test.ts";
// Inside the project, not a temp dir: miniflare is left external and has to
// resolve from node_modules.
//
// Inside node_modules SPECIFICALLY, and not in tests/, because this directory is
// also the Pages build output — `wrangler pages deploy .` uploads whatever is
// sitting here. It does not honour .gitignore, and it does not skip dotfiles:
// the old `tests/.bundle.test.mjs` was gitignored, dot-prefixed, and published
// to production anyway, where it served a bundle of issue.ts / present.ts /
// seats.ts / crypto.ts / auth.ts / jwt.ts as one downloadable file (#691).
// node_modules IS excluded by Pages, and module resolution still finds
// miniflare from here — marketing-site/node_modules is an ancestor.
const outfile = "node_modules/.cache/planscape/bundle.test.mjs";

await esbuild.build({
  entryPoints: [entry],
  bundle: true,
  outfile,
  format: "esm",
  platform: "node",
  target: "node22",
  external: ["miniflare"],
  logLevel: "info",
});

// Run the bundle directly rather than via `--test`. Node's test runner skips
// anything under node_modules even when named explicitly, and node:test executes
// its tests on plain import anyway — same reporter, and a failing test still
// exits non-zero (asserted by tests/run.mjs's own failure path; see #691).
const child = spawn(process.execPath, [outfile], {
  stdio: "inherit",
  // Bundled code reads functions/api/schema.sql by relative path.
  cwd: process.cwd(),
});
child.on("exit", (code) => process.exit(code ?? 1));
