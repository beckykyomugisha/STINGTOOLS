/**
 * Proves the `no-restricted-syntax` guard against status-from-message actually
 * fires — on every banned form, and on none of the allowed ones.
 *
 * A lint rule nobody has tested is a lint rule nobody knows the shape of. The
 * whole point of adding it is that this defect class is invisible to the
 * compiler, the type-checker and CI; if the selectors are subtly wrong it is
 * invisible to the linter too, and we are back where we started.
 *
 * Run: npm run test:lint-rule
 */

import assert from 'node:assert/strict';
import { ESLint } from 'eslint';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, '..');
const fixtureSrc = path.join(here, 'fixtures', 'status-from-message.fixture.ts.txt');

// The fixture is kept as .ts.txt so the project gates never compile or lint it
// as part of the app. Materialise it as a real .ts next to the app so eslint
// picks up the project's own .eslintrc.json — the rule under test must be the
// SHIPPED config, not a copy of it.
// NB: no leading dot in the filename — ESLint silently ignores dotfiles, which
// would make every assertion below pass vacuously by reporting nothing.
const tmp = path.join(projectRoot, 'lint-rule-fixture.tmp.ts');
fs.writeFileSync(tmp, fs.readFileSync(fixtureSrc, 'utf8'));

let messages;
try {
  // `overrides` in .eslintrc.json turns the rule off under tests/, so the
  // fixture is linted from the project root where the rule is error-level.
  const eslint = new ESLint({ cwd: projectRoot });
  const [result] = await eslint.lintFiles([tmp]);
  messages = result.messages.filter((m) => m.ruleId === 'no-restricted-syntax');
} finally {
  fs.unlinkSync(tmp);
}

const flagged = new Set(messages.map((m) => m.line));
const source = fs.readFileSync(fixtureSrc, 'utf8').split(/\r?\n/);

// Derive expectations from the fixture itself: a line is expected to be
// flagged iff the comment two lines above it starts with "// BAD".
const expected = new Set();
const allowed = new Set();
source.forEach((line, i) => {
  const marker = /^\s*\/\/ (BAD|GOOD) (\d+)/.exec(line);
  if (!marker) return;
  // The statement follows the comment block; find the next non-comment,
  // non-blank line.
  for (let j = i + 1; j < source.length; j++) {
    const l = source[j].trim();
    if (!l || l.startsWith('//')) continue;
    (marker[1] === 'BAD' ? expected : allowed).add(j + 1);
    break;
  }
});

const results = [];
const check = (name, fn) => {
  try { fn(); results.push([true, name]); }
  catch (e) { results.push([false, `${name}\n      ${e.message.split('\n')[0]}`]); }
};

check('the fixture declares both banned and allowed forms', () => {
  assert.ok(expected.size >= 8, `expected >=8 BAD forms, fixture has ${expected.size}`);
  assert.ok(allowed.size >= 5, `expected >=5 GOOD forms, fixture has ${allowed.size}`);
});

for (const line of [...expected].sort((a, b) => a - b)) {
  check(`fires on the banned form at fixture line ${line}: ${source[line - 1].trim().slice(0, 62)}`,
    () => assert.ok(flagged.has(line), 'rule did NOT fire — this form would slip through'));
}

for (const line of [...allowed].sort((a, b) => a - b)) {
  check(`stays quiet on the allowed form at fixture line ${line}: ${source[line - 1].trim().slice(0, 62)}`,
    () => assert.ok(!flagged.has(line), 'rule fired on legitimate code — false positive'));
}

check('the rule flags nothing outside the declared forms', () => {
  const unexpected = [...flagged].filter((l) => !expected.has(l));
  assert.equal(unexpected.length, 0, `unexpected reports on line(s) ${unexpected.join(', ')}`);
});

console.log('');
let bad = 0;
for (const [ok, name] of results) {
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) bad++;
}
console.log(`\n  ${results.length - bad}/${results.length} passed, ${bad} failed\n`);
process.exit(bad ? 1 : 0);
