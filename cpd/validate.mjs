#!/usr/bin/env node
// Fails the build on drift between cpd/data (source of truth) and the rest of the repo.
// This exists because three hand-written documents once contradicted each other on the
// published-information codes. Run in CI. Exit code 1 = drift found.
// Usage: node cpd/validate.mjs [--quiet]

import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..');
const QUIET = process.argv.includes('--quiet');

const load = f => JSON.parse(readFileSync(join(HERE, 'data', f), 'utf8'));
const C = load('codes.json'), CO = load('course.json'), A = load('assessment.json'), EX = load('exercises.json');

const errors = [], warns = [];
const fail = (rule, msg, where = '') => errors.push({ rule, msg, where });
const warn = (rule, msg, where = '') => warns.push({ rule, msg, where });

/* ── 1. Assessment internal consistency ───────────────────────── */
const sum = A.items.reduce((s, i) => s + i.marks, 0);
if (sum !== A.totalMarks) fail('MARKS-SUM', `Item marks sum to ${sum} but totalMarks is ${A.totalMarks}`, 'assessment.json');
const pct = A.passMark / A.totalMarks;
if (pct < 0.7) fail('PASS-MARK', `Pass mark ${A.passMark}/${A.totalMarks} = ${(pct * 100).toFixed(1)}% is below the stated 70%`, 'assessment.json');
if (pct > 0.8) warn('PASS-MARK', `Pass mark is ${(pct * 100).toFixed(1)}% — higher than the 70% the syllabus advertises`, 'assessment.json');

const ids = new Set();
for (const i of A.items) {
  if (ids.has(i.id)) fail('DUP-ID', `Duplicate item id ${i.id}`, 'assessment.json');
  ids.add(i.id);
  if (i.type === 'mcq') {
    const v0 = { ...i, ...(i.variants?.[0] || {}) };
    const opts = v0.options, ans = v0.answer;
    if (!Array.isArray(opts) || opts.length < 2) fail('MCQ-OPTS', `${i.id} has no option list`, 'assessment.json');
    else if (!(Number.isInteger(ans) && ans >= 0 && ans < opts.length)) fail('MCQ-ANS', `${i.id} answer index ${ans} out of range`, 'assessment.json');
    (i.variants || []).forEach((v, n) => {
      const o = v.options || opts, a = v.answer ?? ans;
      if (!(Number.isInteger(a) && a >= 0 && a < o.length)) fail('MCQ-ANS', `${i.id} variant ${n} answer index ${a} out of range`, 'assessment.json');
    });
  } else if (!i.answer && !i.answerTemplate && !i.answerFrom && !(i.variants || []).some(v => v.answer)) {
    fail('NO-ANSWER', `${i.id} is a written item with no model answer`, 'assessment.json');
  }
  // A variant is spread over its item, so a variant key that collides with a structural
  // field silently rewrites the item. Q9 once carried a `type` key for the container type
  // code, which overwrote type:"written" and dropped the whole question from the paper.
  const RESERVED = ['id', 'type', 'marks', 'lo', 'variants'];
  for (const [n, v] of (i.variants || []).entries())
    for (const k of RESERVED)
      if (k in v) fail('VARIANT-COLLISION', `${i.id} variant ${n} sets reserved key "${k}" - it would overwrite the item's own field`, 'assessment.json');

  if (!CO.outcomes.some(o => o.id === i.lo)) fail('BAD-LO', `${i.id} maps to unknown outcome ${i.lo}`, 'assessment.json');
}

/* ── 2. Every examinable outcome is actually examined ─────────── */
for (const o of CO.outcomes) {
  const covered = A.items.some(i => i.lo === o.id);
  if (!covered && !o.assessedBy) fail('LO-UNCOVERED', `${o.id} is not assessed by any item and has no assessedBy note`, 'course.json');
  if (covered && o.assessedBy) warn('LO-DOUBLE', `${o.id} declares assessedBy but is also examined`, 'course.json');
}

/* ── 3. Variant equivalence — resit papers must be worth the same ── */
const maxV = Math.max(...A.items.map(i => (i.variants || []).length));
for (let v = 0; v < maxV; v++) {
  const t = A.items.reduce((s, i) => s + i.marks, 0);
  if (t !== A.totalMarks) fail('VARIANT-MARKS', `Variant ${v} totals ${t}, not ${A.totalMarks}`, 'assessment.json');
}

/* ── 4. Course structure ──────────────────────────────────────── */
const exIds = new Set(EX.exercises.map(e => e.id));
for (const r of CO.runsheet) if (r.exercise && !exIds.has(r.exercise)) fail('BAD-EX', `Run sheet ${r.id} references unknown exercise ${r.exercise}`, 'course.json');
const mins = CO.runsheet.reduce((s, r) => s + r.mins, 0);
if (mins !== CO.contactHours * 60) fail('RUNSHEET-MINS', `Run sheet totals ${mins} min but course claims ${CO.contactHours} contact hours (${CO.contactHours * 60} min)`, 'course.json');
if (C.nameFields.length !== 7) fail('NAME-FIELDS', `Container name has ${C.nameFields.length} fields, not 7`, 'codes.json');
for (const [n, f] of C.nameFields.entries()) if (f.pos !== n + 1) fail('NAME-ORDER', `nameFields out of order at ${f.id}`, 'codes.json');

/* ── 5. Code-family integrity — THE rule that caused the drift ── */
const pubStates = C.cdeStates.filter(s => s.codeFamily === 'authorization').map(s => s.id);
if (!pubStates.includes('PUBLISHED')) fail('CODE-FAMILY', 'PUBLISHED must use the authorization code family', 'codes.json');
for (const s of C.cdeStates.filter(x => x.codeFamily === 'suitability'))
  if (!C.codeFamilies.suitability.appliesTo.includes(s.id)) fail('CODE-FAMILY', `${s.id} declares suitability but is not in codeFamilies.suitability.appliesTo`, 'codes.json');

/* ── 6. Repo-wide drift scan ──────────────────────────────────── */
const walk = (dir, out = []) => {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (/^(node_modules|\.git|dist|obj|bin)$/.test(e.name)) continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (/\.(md|html)$/.test(e.name)) out.push(p);
  }
  return out;
};

// Patterns that assert something contradicting the source of truth.
const DRIFT = [
  { rule: 'S-CODE-PUBLISHED',
    re: /\bS[567]\b[^.\n|]{0,60}[|→>\-]\s*Published|Published[^.\n|]{0,40}\bS[567]\b|\bS4\s*[–-]\s*S7\b[^.\n]{0,30}Published/i,
    msg: 'Maps an upper S code to the Published container. S codes describe SHARED suitability; Published carries A/B authorization codes. (Note: "SHARED (S0-S4) -> PUBLISHED (A1/B1)" is correct and does not fire.)' },
  { rule: 'S7-TABLE',
    re: /\bS0\s*[–-]\s*S7\b|\bS5\b[^.\n]{0,60}\bPIM authorization\b/i,
    msg: 'Presents S0–S7 as a single table without distinguishing the authorization family.' },
];

// TIER 1 - the teaching and customer-facing surface. Any drift here is a hard failure:
// these documents TEACH the codes, so being wrong here is being wrong in front of delegates.
const TIER1 = /^(cpd\/|marketing-site\/|GUIDES\/|docs\/CPD_)/;

// TIER 2 - everything else (product docs, changelogs, historical specs). Many of these
// describe SHIPPED software behaviour, so rewriting the prose without changing the code
// would create a worse inconsistency. They are baselined instead: the recorded set may
// shrink, never grow. A new drift hit outside the baseline fails the build.
// Regenerate deliberately with:  node cpd/validate.mjs --update-baseline
const BASELINE_PATH = join(HERE, 'data', 'drift-baseline.json');
const baseline = existsSync(BASELINE_PATH)
  ? JSON.parse(readFileSync(BASELINE_PATH, 'utf8'))
  : { $comment: '', entries: [] };
const known = new Set(baseline.entries.map(e => `${e.file}::${e.rule}`));

// Files that legitimately DISCUSS the drift rather than committing it.
const EXEMPT = /CPD_PACKAGE_GAP_REGISTER|cpd\/README|CPD_FIELD_GUIDE|cpd\/dist\/|cpd\/validate\.mjs|cpd\/data\/drift-baseline/;

let scanned = 0;
const found = [];
for (const file of walk(ROOT)) {
  const rel = relative(ROOT, file).split(sep).join('/');
  if (EXEMPT.test(rel)) continue;
  scanned++;
  const text = readFileSync(file, 'utf8');
  for (const d of DRIFT) {
    const m = text.match(d.re);
    if (!m) continue;
    const line = text.slice(0, m.index).split('\n').length;
    const key = `${rel}::${d.rule}`;
    found.push({ file: rel, rule: d.rule, line });
    if (TIER1.test(rel)) fail(d.rule, `[tier 1 — teaching surface] ${d.msg}`, `${rel}:${line}`);
    else if (!known.has(key)) fail(d.rule, `[new drift outside baseline] ${d.msg}`, `${rel}:${line}`);
    else warn(d.rule, `[baselined legacy] ${rel}:${line}`);
  }
}

// Baseline hygiene: an entry that no longer drifts should be removed, so the list shrinks.
const foundKeys = new Set(found.map(f => `${f.file}::${f.rule}`));
for (const e of baseline.entries)
  if (!foundKeys.has(`${e.file}::${e.rule}`))
    warn('BASELINE-STALE', `${e.file} (${e.rule}) no longer drifts — remove it from drift-baseline.json`);

if (process.argv.includes('--update-baseline')) {
  const entries = found.filter(f => !TIER1.test(f.file))
    .map(f => ({ file: f.file, rule: f.rule }))
    .sort((a, b) => (a.file + a.rule).localeCompare(b.file + b.rule));
  writeFileSync(BASELINE_PATH, JSON.stringify({
    $comment: 'Legacy S-code drift accepted at baseline. These files describe shipped behaviour or are historical records; they are not the teaching surface. THIS LIST MAY SHRINK, NEVER GROW. A new hit outside it fails the build. Tier 1 paths (cpd/, marketing-site/, GUIDES/, docs/CPD_*) are never baselined.',
    updated: new Date().toISOString().slice(0, 10),
    codesVersion: C.version,
    entries,
  }, null, 2) + '\n');
  console.log(`Baseline updated: ${entries.length} legacy entries recorded.`);
  process.exit(0);
}

/* ── 7. Generated pack is present and current ─────────────────── */
const dist = join(HERE, 'dist');
if (!existsSync(dist)) warn('NO-DIST', 'cpd/dist not built — run `node cpd/build.mjs`');
else {
  for (const f of ['field-guide.html', 'assessment-paper.html', 'assessment-marking.html', 'workbook.html', 'workbook-answers.html', 'syllabus.html', 'BEP_TEMPLATE.md', 'MIDP_TEMPLATE.csv', 'index.html'])
    if (!existsSync(join(dist, f))) warn('MISSING-DOC', `${f} not in cpd/dist — rebuild`);
  const fg = existsSync(join(dist, 'field-guide.html')) ? readFileSync(join(dist, 'field-guide.html'), 'utf8') : '';
  if (fg && !fg.includes(`codes.json v${C.version}`)) warn('STALE-DIST', `field-guide.html was built from a different codes.json version — rebuild`);
}

/* ── report ───────────────────────────────────────────────────── */
const pad = s => String(s).padEnd(18);
if (!QUIET) {
  console.log(`\nCPD validate · ${A.items.length} items · ${sum} marks · pass ${A.passMark} (${(pct * 100).toFixed(0)}%) · ${scanned} repo files scanned\n`);
  for (const w of warns) console.log(`  WARN  ${pad(w.rule)} ${w.msg}${w.where ? `\n        ${w.where}` : ''}`);
  for (const e of errors) console.log(`  FAIL  ${pad(e.rule)} ${e.msg}${e.where ? `\n        ${e.where}` : ''}`);
}
if (errors.length) { console.log(`\n✗ ${errors.length} error${errors.length > 1 ? 's' : ''}, ${warns.length} warning${warns.length === 1 ? '' : 's'}\n`); process.exit(1); }
console.log(`\n✓ no drift · ${warns.length} warning${warns.length === 1 ? '' : 's'}\n`);
