/**
 * QR payload contract — mobile half.
 *
 * Reads tools/qr_payload_corpus.json, the SAME file StingQrFormatTests.cs reads.
 * That is the point: the plugin's parser and this one disagreed for the entire
 * life of the feature (plugin emitted `sting://asset/…`, this accepted only
 * `planscape://` or a bare UUID) and nothing noticed, because a payload this
 * function rejects is indistinguishable from a bad scan.
 *
 * Adding a case to the corpus now fails whichever side has not implemented it.
 *
 * Run: npm run test:qr
 */

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import './register.mjs';

const { parseQr } = await import('../src/services/qrParser.ts');

const here = path.dirname(fileURLToPath(import.meta.url));
const corpusPath = path.resolve(here, '..', '..', 'tools', 'qr_payload_corpus.json');
const corpus = JSON.parse(fs.readFileSync(corpusPath, 'utf8'));

assert.ok(corpus.cases.length > 0, 'corpus must not be empty');

const failures = [];
for (const c of corpus.cases) {
  try {
    const got = parseQr(c.raw);
    if (!c.expect.ok) {
      assert.equal(
        got.type,
        'unknown',
        `must be rejected, got type=${got.type} id=${got.id}`,
      );
    } else if (c.expect.kind === 'document') {
      assert.equal(got.type, 'document', 'must resolve to a document');
      assert.equal(got.docId ?? null, c.expect.docId ?? null, 'docId');
      assert.equal(got.projectCode ?? null, c.expect.projectCode ?? null, 'projectCode');
      assert.equal(got.revision ?? null, c.expect.revision ?? null, 'revision');
      assert.equal(got.tag ?? null, null, 'a document payload carries no tag');
      assert.ok(got.id, 'id (the lookup key) must be set');

      // Every fact, individually. The '-' placeholder for an absent interior
      // field must read back as undefined, never as the literal dash — a UI
      // would otherwise print "-" where it should print nothing at all.
      const f = c.expect.facts ?? {};
      for (const key of [
        'suitability', 'cdeState', 'issueDate', 'zone', 'sheetOfTotal',
        'lod', 'paperSize', 'scale', 'initials', 'signature',
      ]) {
        assert.equal((got.facts ?? {})[key] ?? null, f[key] ?? null, `facts.${key}`);
      }
    } else if (c.expect.kind === 'sheet') {
      assert.equal(got.type, 'sheet', 'must resolve to a sheet');
      assert.equal(got.sheetNumber ?? null, c.expect.sheetNumber ?? null, 'sheetNumber');
      assert.equal(got.revision ?? null, c.expect.revision ?? null, 'revision');
      assert.equal(got.projectCode ?? null, c.expect.projectCode ?? null, 'projectCode');
      assert.equal(got.id, c.expect.sheetNumber, 'id is the sheet number');
      // A sheet payload must NOT masquerade as an element: an element search on a
      // sheet number silently returns nothing, which reads as "not in this project".
      assert.equal(got.tag ?? null, null, 'a sheet payload carries no tag');
    } else {
      assert.equal(got.type, 'element', 'must resolve to an element');
      assert.equal(got.tag ?? null, c.expect.tag ?? null, 'tag');
      assert.equal(got.uniqueId ?? null, c.expect.uniqueId ?? null, 'uniqueId');
      assert.equal(got.projectCode ?? null, c.expect.projectCode ?? null, 'projectCode');
      // `id` is what the scanner actually searches on, so it must never be empty
      // for a case we claim resolves.
      assert.ok(got.id, 'id (the lookup key) must be set');
      assert.equal(got.id, c.expect.tag ?? c.expect.uniqueId, 'id prefers the tag');
    }
  } catch (err) {
    failures.push(`  ${c.name}\n    raw: ${JSON.stringify(c.raw)}\n    ${err.message}`);
  }
}

if (failures.length) {
  console.error(`qrParser contract: ${failures.length}/${corpus.cases.length} FAILED\n`);
  console.error(failures.join('\n\n'));
  process.exit(1);
}

console.log(`qrParser contract: ${corpus.cases.length}/${corpus.cases.length} passed`);
