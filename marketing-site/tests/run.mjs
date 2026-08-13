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
// resolve from node_modules. Gitignored.
const outfile = "tests/.bundle.test.mjs";

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

const child = spawn(process.execPath, ["--test", outfile], {
  stdio: "inherit",
  // Bundled code reads functions/api/schema.sql by relative path.
  cwd: process.cwd(),
});
child.on("exit", (code) => process.exit(code ?? 1));
