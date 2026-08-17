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
import { readdirSync } from "node:fs";

// Every tests/*.test.ts, unless one is named explicitly. Previously this
// defaulted to a single hard-coded file, so a newly added test file ran
// nowhere — present in the repo, absent from CI, and indistinguishable from
// coverage. That is the failure mode CLAUDE.md §3 records for the Clash and
// Routing projects, where 97 test methods were counted for months while
// compiling nowhere.
const entries = process.argv[2]
  ? [process.argv[2]]
  : readdirSync("tests")
      .filter((f) => f.endsWith(".test.ts"))
      .sort()
      .map((f) => `tests/${f}`);

if (entries.length === 0) {
  console.error("No tests/*.test.ts files found.");
  process.exit(1);
}
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
const outdir = "node_modules/.cache/planscape";

// One bundle + one process per file. Each file owns a Miniflare instance, and
// sharing a process between them would let one file's D1 leak into another's.
let failed = 0;
for (const entry of entries) {
  const outfile = `${outdir}/${entry.replace(/[\/\\]/g, "_")}.mjs`;

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
  // anything under node_modules even when named explicitly, and node:test
  // executes its tests on plain import anyway — same reporter, and a failing
  // test still exits non-zero (#691).
  const code = await new Promise((resolve) => {
    const child = spawn(process.execPath, [outfile], {
      stdio: "inherit",
      // Bundled code reads functions/api/schema.sql by relative path.
      cwd: process.cwd(),
    });
    child.on("exit", (c) => resolve(c ?? 1));
  });

  if (code !== 0) failed++;
}

// Non-zero if ANY file failed — not just the last one, which would let an
// earlier failure pass unnoticed behind a later success.
process.exit(failed === 0 ? 0 : 1);
