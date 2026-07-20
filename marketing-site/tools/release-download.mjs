#!/usr/bin/env node
// Release a build to the gated downloads area.
//
// Uploads one or more files to the private planscape-downloads R2 bucket and
// prints the exact `artifacts` block to paste into
// functions/api/_lib/downloads/catalog.ts — sha256 and size are computed from
// the files themselves, so the catalogue can never drift from the objects.
//
// Run from the marketing-site directory (wrangler.toml lives here):
//
//   node tools/release-download.mjs --tool sting-bridge --version 0.1.0-beta.1 \
//     --file "win64:Windows 64-bit:C:/builds/StingBridge_0.1.0-beta.1_win64.zip" \
//     --file "any:Any OS (Python 3.11+):C:/builds/StingBridge_0.1.0-beta.1_any.zip"
//
//   --file LABEL:PLATFORM:PATH   repeatable; LABEL is the URL-safe slug shown
//                                on the button and used as ?artifact=LABEL.
//                                For a single-file tool use one --file and
//                                paste the objectKey/sha256 into the version
//                                fields instead of an artifacts block.
//   --dry-run                    hash + print, skip the R2 upload.
//
// Requires wrangler auth (`npx wrangler whoami` to check). Ship ZIPS ONLY —
// the download endpoint serves everything as application/zip.

import { createHash } from "node:crypto";
import { readFileSync, statSync } from "node:fs";
import { basename } from "node:path";
import { execFileSync } from "node:child_process";

const BUCKET = "planscape-downloads";

function fail(msg) {
  console.error(`error: ${msg}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { files: [], dryRun: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--tool") args.tool = argv[++i];
    else if (a === "--version") args.version = argv[++i];
    else if (a === "--file") args.files.push(argv[++i]);
    else if (a === "--dry-run") args.dryRun = true;
    else fail(`unknown argument: ${a}`);
  }
  if (!args.tool) fail("--tool is required (catalogue tool id, e.g. sting-bridge)");
  if (!args.version) fail("--version is required (e.g. 0.1.0-beta.1)");
  if (!args.files.length) fail("at least one --file LABEL:PLATFORM:PATH is required");
  return args;
}

function parseFileSpec(spec) {
  // LABEL:PLATFORM:PATH — PATH may itself contain ":" (C:/...), so split
  // only the first two separators.
  const first = spec.indexOf(":");
  const second = spec.indexOf(":", first + 1);
  if (first === -1 || second === -1) {
    fail(`--file must be LABEL:PLATFORM:PATH, got: ${spec}`);
  }
  const label = spec.slice(0, first).trim();
  const platform = spec.slice(first + 1, second).trim();
  const path = spec.slice(second + 1).trim();
  if (!/^[a-z0-9][a-z0-9._-]*$/i.test(label)) {
    fail(`label must be URL-safe (letters, digits, . _ -), got: ${label}`);
  }
  return { label, platform, path };
}

const { tool, version, files, dryRun } = parseArgs(process.argv.slice(2));

const artifacts = files.map((spec) => {
  const { label, platform, path } = parseFileSpec(spec);
  let bytes;
  try {
    bytes = readFileSync(path);
  } catch (e) {
    fail(`cannot read ${path}: ${e.message}`);
  }
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  const sizeMb = Math.max(1, Math.round(statSync(path).size / 1048576));
  const objectKey = `${tool}/${version}/${basename(path)}`;
  return { label, platform, path, objectKey, sha256, sizeMb };
});

const labels = artifacts.map((a) => a.label);
if (new Set(labels).size !== labels.length) {
  fail(`artifact labels must be unique within a version: ${labels.join(", ")}`);
}

for (const a of artifacts) {
  console.log(`\n${a.path}`);
  console.log(`  key    ${BUCKET}/${a.objectKey}`);
  console.log(`  sha256 ${a.sha256}`);
  console.log(`  size   ${a.sizeMb} MB`);
  if (dryRun) {
    console.log("  upload skipped (--dry-run)");
    continue;
  }
  execFileSync(
    "npx",
    // --remote is required: wrangler 4 defaults r2 object commands to the LOCAL
    // simulator, which silently uploads to nowhere the production bucket reads.
    ["wrangler", "r2", "object", "put", `${BUCKET}/${a.objectKey}`, "--file", a.path, "--remote"],
    { stdio: "inherit", shell: process.platform === "win32" }
  );
}

const block = artifacts
  .map(
    (a) => `          {
            label: ${JSON.stringify(a.label)},
            platform: ${JSON.stringify(a.platform)},
            objectKey: ${JSON.stringify(a.objectKey)},
            sizeMb: ${a.sizeMb},
            sha256: ${JSON.stringify(a.sha256)},
          },`
  )
  .join("\n");

console.log(`\nPaste into the "${tool}" version "${version}" entry in`);
console.log("functions/api/_lib/downloads/catalog.ts:\n");
console.log(`        artifacts: [\n${block}\n        ],`);
console.log("\nThen deploy: npm run deploy (from marketing-site/).");
