#!/usr/bin/env node
// Translation QA helper.
//
// Compares every locale bundle against the canonical en.json and
// reports:
//   - Missing keys (locale doesn't translate something en.json has)
//   - Extra keys  (locale has a key en.json has dropped)
//   - Empty translations (key exists but value is "")
//   - Placeholder mismatches ({n}, {name}, etc.)
//
// Usage:
//   node lint.mjs                # human-readable report on stdout
//   node lint.mjs --json         # machine-readable for CI
//   node lint.mjs --fix-empty    # copy missing/empty values from
//                                  en.json (marks them with the
//                                  '«EN»' prefix so a native-speaker
//                                  reviewer can find them at a glance)
//
// Exit code:
//   0 — all locales clean
//   1 — issues found (CI signal)
//   2 — usage error

import { readFileSync, writeFileSync, readdirSync } from "node:fs";
import { join, dirname, basename } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const localesDir = join(here, "locales");

const args = new Set(process.argv.slice(2));
const wantJson  = args.has("--json");
const wantFix   = args.has("--fix-empty");

const en = JSON.parse(readFileSync(join(localesDir, "en.json"), "utf8"));
const enFlat = flatten(en);
const placeholderRe = /\{[a-zA-Z_][a-zA-Z0-9_]*\}/g;

const report = {};
let exit = 0;

for (const file of readdirSync(localesDir)) {
  if (!file.endsWith(".json") || file === "en.json") continue;
  const code = basename(file, ".json");
  const bundle = JSON.parse(readFileSync(join(localesDir, file), "utf8"));
  const flat = flatten(bundle);
  const missing = [], extra = [], empty = [], placeholderIssues = [];

  for (const k of Object.keys(enFlat)) {
    if (k === "_note") continue;
    if (!(k in flat))             missing.push(k);
    else if (flat[k] === "")      empty.push(k);
    else if (mismatchPlaceholders(enFlat[k], flat[k])) placeholderIssues.push(k);
  }
  for (const k of Object.keys(flat)) {
    if (k === "_note") continue;
    if (!(k in enFlat)) extra.push(k);
  }

  report[code] = { missing, extra, empty, placeholderIssues };
  if (missing.length || empty.length || placeholderIssues.length) exit = 1;

  if (wantFix) {
    let mutated = false;
    for (const k of [...missing, ...empty]) {
      setNested(bundle, k, "«EN» " + enFlat[k]);
      mutated = true;
    }
    if (mutated) {
      writeFileSync(join(localesDir, file), JSON.stringify(bundle, null, 2) + "\n", "utf8");
      console.log(`fixed ${code}: copied ${missing.length + empty.length} strings from en.json with «EN» prefix`);
    }
  }
}

if (wantJson) {
  console.log(JSON.stringify(report, null, 2));
} else {
  for (const [code, r] of Object.entries(report)) {
    console.log(`\n── ${code} ───────────────────────────────`);
    if (!r.missing.length && !r.empty.length && !r.placeholderIssues.length && !r.extra.length) {
      console.log("  ✓ clean");
      continue;
    }
    if (r.missing.length)            console.log(`  missing (${r.missing.length}):            ${r.missing.join(", ")}`);
    if (r.empty.length)              console.log(`  empty (${r.empty.length}):              ${r.empty.join(", ")}`);
    if (r.placeholderIssues.length)  console.log(`  placeholder mismatch (${r.placeholderIssues.length}): ${r.placeholderIssues.join(", ")}`);
    if (r.extra.length)              console.log(`  extra (${r.extra.length}):              ${r.extra.join(", ")}`);
  }
}

process.exit(exit);

// ── helpers ────────────────────────────────────────────────────────────

function flatten(obj, prefix = "", out = {}) {
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object" && !Array.isArray(v)) flatten(v, path, out);
    else out[path] = v;
  }
  return out;
}

function setNested(obj, path, value) {
  const segs = path.split(".");
  let cur = obj;
  for (let i = 0; i < segs.length - 1; i++) {
    cur[segs[i]] ??= {};
    cur = cur[segs[i]];
  }
  cur[segs[segs.length - 1]] = value;
}

function mismatchPlaceholders(en, other) {
  const a = new Set((en.match(placeholderRe) ?? []).sort());
  const b = new Set((other.match(placeholderRe) ?? []).sort());
  if (a.size !== b.size) return true;
  for (const x of a) if (!b.has(x)) return true;
  return false;
}
