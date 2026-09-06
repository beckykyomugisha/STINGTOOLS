// Test runner for the marketing-site Pages Functions.
//
// WHY THIS LIVES OUTSIDE marketing-site/. That directory IS the Pages build
// output (`pages_build_output_dir = "."`), so everything in it is uploaded to
// production by `wrangler pages deploy`. When the tests lived in
// marketing-site/tests/ they were publicly downloadable — test sources, and for
// a while an esbuild bundle of issue.ts / present.ts / seats.ts / crypto.ts /
// auth.ts / jwt.ts as one file (#691).
//
// Pages honours neither .gitignore nor dotfile prefixes: the old bundle was
// gitignored AND dot-prefixed and was published anyway. `.assetsignore` is a
// Workers static-assets feature, not a Pages one — the Cloudflare docs describe
// it only for Workers, contrasting it with what "Pages would automatically
// exclude". So being outside the directory is the only reliable exclusion.
//
// The Functions are TypeScript with extensionless imports, which Node cannot
// resolve on its own, so esbuild bundles each test (and everything it pulls in)
// into one ESM file first. miniflare stays external — it provides a REAL D1,
// i.e. the same SQLite engine the deployed Function talks to, so a test that
// passes here is testing the actual SQL and not a hand-written stand-in.
//
//   cd marketing-site && npm test
//
// CWD MATTERS: run from marketing-site, because the bundled code reads
// functions/api/schema.sql by a path relative to cwd, and the bundle is written
// under marketing-site/node_modules so miniflare resolves.

import { spawn } from "node:child_process";
import { readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));

// esbuild is a dependency of marketing-site, not of this directory. Node resolves
// bare imports relative to the IMPORTING FILE, so a plain `import "esbuild"` here
// fails with ERR_MODULE_NOT_FOUND now that the runner sits outside
// marketing-site. Resolve it from marketing-site's own node_modules instead of
// duplicating the dependency.
//
// Anchored to this file rather than to cwd, so it works however npm invokes it.
const requireFromSite = createRequire(
  path.join(here, "..", "marketing-site", "package.json")
);
const esbuild = requireFromSite("esbuild");

// Every *.test.ts beside this file, unless one is named explicitly. Resolved
// relative to THIS file, not cwd, so it does not matter where npm runs it from.
// Previously this defaulted to a single hard-coded file, so a newly added test
// file ran nowhere — present in the repo, absent from CI, indistinguishable from
// coverage. That is the failure CLAUDE.md §3 records for the Clash and Routing
// projects, where 97 test methods were counted for months while compiling nowhere.
const entries = process.argv[2]
  ? [path.resolve(process.argv[2])]
  : readdirSync(here)
      .filter((f) => f.endsWith(".test.ts"))
      .sort()
      .map((f) => path.join(here, f));

if (entries.length === 0) {
  console.error(`No *.test.ts files found in ${here}`);
  process.exit(1);
}

// Inside marketing-site/node_modules specifically: miniflare is left external
// and has to resolve from there, and node_modules is the one directory Pages
// excludes by default.
const outdir = "node_modules/.cache/planscape";

// One bundle + one process per file. Each file owns a Miniflare instance, and
// sharing a process between them would let one file's D1 leak into another's.
let failed = 0;
for (const entry of entries) {
  const outfile = `${outdir}/${path.basename(entry)}.mjs`;

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
      // Bundled code reads functions/api/schema.sql by relative path, so this
      // must stay marketing-site.
      cwd: process.cwd(),
    });
    child.on("exit", (c) => resolve(c ?? 1));
  });

  if (code !== 0) failed++;
}

// Non-zero if ANY file failed — not just the last one, which would let an
// earlier failure pass unnoticed behind a later success.
process.exit(failed === 0 ? 0 : 1);
