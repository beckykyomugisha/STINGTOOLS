#!/usr/bin/env node
// meetings-core-parity.js — CI guard (option Z) that fails on drift between the two
// lockstep meeting modules:
//   web   : Planscape.Server/src/Planscape.API/wwwroot/js/meetings-core.js  (UMD, window.MeetingsCore)
//   mobile: Planscape/src/api/meetingsCore.ts                               (Metro import)
// They can't be one physical file (no monorepo / Metro can't reach wwwroot), so this asserts
// their CONTRACT stays identical: the role→capability matrix (CAPS) and the API method names.
// Exit 0 = in lockstep; exit 1 = drift (prints what differs). No build/deploy change.
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const WEB = path.join(ROOT, "Planscape.Server/src/Planscape.API/wwwroot/js/meetings-core.js");
const MOBILE = path.join(ROOT, "Planscape/src/api/meetingsCore.ts");

function read(p) {
  if (!fs.existsSync(p)) { console.error(`[parity] FILE MISSING: ${p}`); process.exit(1); }
  return fs.readFileSync(p, "utf8");
}

// Slice the `CAPS = { ... };` object literal text.
function capsBlock(src) {
  const i = src.indexOf("CAPS");
  if (i < 0) return "";
  const open = src.indexOf("{", i);
  const end = src.indexOf("};", open);
  return open >= 0 && end >= 0 ? src.slice(open, end) : "";
}

// role -> sorted cap list, from a CAPS block.
function parseCaps(src) {
  const block = capsBlock(src);
  const out = {};
  const re = /(["']?[\w-]+["']?)\s*:\s*\[([^\]]*)\]/g;
  let m;
  while ((m = re.exec(block)) !== null) {
    const role = m[1].replace(/["']/g, "");
    const caps = m[2].split(",").map((s) => s.replace(/["']/g, "").trim()).filter(Boolean).sort();
    out[role] = caps;
  }
  return out;
}

// API method-name set. Web: `name: function (`; mobile (meetingsCore object): `name: (`.
function parseMethods(src, kind) {
  const set = new Set();
  const re = kind === "web"
    ? /^\s+([a-zA-Z]\w*)\s*:\s*function\s*\(/gm
    : /^\s+([a-zA-Z]\w*)\s*:\s*(?:async\s*)?\(/gm;
  let m;
  while ((m = re.exec(src)) !== null) set.add(m[1]);
  return set;
}

function diff(a, b) { return [...a].filter((x) => !b.has(x)).sort(); }

const web = read(WEB), mob = read(MOBILE);
const problems = [];

// 1) CAPS parity (role keys + per-role capability sets).
const wc = parseCaps(web), mc = parseCaps(mob);
const wRoles = new Set(Object.keys(wc)), mRoles = new Set(Object.keys(mc));
const onlyWebRoles = diff(wRoles, mRoles), onlyMobRoles = diff(mRoles, wRoles);
if (onlyWebRoles.length) problems.push(`CAPS roles only in web: ${onlyWebRoles.join(", ")}`);
if (onlyMobRoles.length) problems.push(`CAPS roles only in mobile: ${onlyMobRoles.join(", ")}`);
for (const r of Object.keys(wc)) {
  if (!mc[r]) continue;
  if (wc[r].join("|") !== mc[r].join("|"))
    problems.push(`CAPS caps differ for role "${r}":\n    web   = [${wc[r].join(", ")}]\n    mobile= [${mc[r].join(", ")}]`);
}
if (Object.keys(wc).length === 0) problems.push("CAPS not found in web file");
if (Object.keys(mc).length === 0) problems.push("CAPS not found in mobile file");

// 2) API method-name parity.
const wm = parseMethods(web, "web"), mm = parseMethods(mob, "mobile");
const onlyWebM = diff(wm, mm), onlyMobM = diff(mm, wm);
if (onlyWebM.length) problems.push(`API methods only in web: ${onlyWebM.join(", ")}`);
if (onlyMobM.length) problems.push(`API methods only in mobile: ${onlyMobM.join(", ")}`);
if (wm.size === 0 || mm.size === 0) problems.push("API methods not parsed from one of the files");

if (problems.length) {
  console.error("[parity] DRIFT between meetings-core.js (web) and meetingsCore.ts (mobile):\n  - " + problems.join("\n  - "));
  console.error("\nKeep the two LOCKSTEP — add the missing capability/method to BOTH (see DEPLOY.md option Z).");
  process.exit(1);
}
console.log(`[parity] OK — CAPS (${Object.keys(wc).length} roles) + ${wm.size} API methods match across web + mobile.`);
process.exit(0);
