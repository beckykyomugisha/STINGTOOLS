// Writes marketing-site/build-info.json so the DEPLOYED commit is discoverable
// from outside — the missing fact behind #651.
//
// marketing-site has no git-connected Pages build, so `main` and production can
// drift arbitrarily with no signal anywhere. Worse, Pages' static fallback makes
// an undeployed Function return 405/200-HTML, which is indistinguishable from a
// route that never existed. Twice in one session that cost hours: a merged
// endpoint was simply absent, and the absence looked like a code fault.
//
// A deployed commit marker fixes the diagnosis cheaply: fetch
// https://planscape.build/build-info.json and you know exactly what is serving,
// with no Cloudflare credentials needed. The deploy workflow asserts against it,
// and so can a human.
//
// Run automatically by `npm run predeploy`, so a manual `npm run deploy` stamps
// it too — otherwise the marker would only ever be right in CI.

import { execSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, "..", "marketing-site", "build-info.json");

function git(args, fallback) {
  try {
    return execSync(`git ${args}`, { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
  } catch {
    return fallback;
  }
}

// GITHUB_SHA is authoritative in CI; git rev-parse covers local deploys. Never
// throw — failing to stamp must not block a deploy someone needs to make.
const sha = process.env.GITHUB_SHA || git("rev-parse HEAD", "unknown");
const branch =
  process.env.GITHUB_REF_NAME || git("rev-parse --abbrev-ref HEAD", "unknown");

const info = {
  sha,
  shortSha: sha.slice(0, 9),
  branch,
  // Whether the tree had uncommitted changes at deploy time. `npm run deploy`
  // passes --commit-dirty=true, so this is the only record that what shipped was
  // not exactly a commit.
  dirty: git("status --porcelain -- ../marketing-site", "") !== "",
  deployedAt: new Date().toISOString(),
  deployedBy: process.env.GITHUB_ACTIONS ? "github-actions" : "manual",
  runId: process.env.GITHUB_RUN_ID || null,
};

writeFileSync(out, JSON.stringify(info, null, 2) + "\n");
console.log(`build-info.json: ${info.shortSha} (${info.branch}) dirty=${info.dirty} by ${info.deployedBy}`);
