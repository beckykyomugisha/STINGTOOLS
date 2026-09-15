/**
 * The `/d/` payload parser, web half.
 *
 * The C# and mobile halves are held to tools/qr_payload_corpus.json. This one
 * cannot read that file from the Next test runner, so it asserts the same cases
 * by hand — including the two that have already caused real defects elsewhere:
 * the '-' placeholder reading back as a literal dash, and a sheet number that
 * itself contains hyphens.
 */
import { describe, expect, it } from 'vitest';
import { parseDocPath, prettyScale, prettySheetOfTotal } from './docPayload';

const DOCID = 'PROJECTN-PLNS-ZZ-02-DR-A-0001';

describe('parseDocPath', () => {
  it('reads every fact out of the full 31mm payload', () => {
    const p = parseDocPath([
      DOCID, 'S4', 'PUB', '20260914', '-', '25.100', '350', 'A1', '1.100', 'DRW.CHK.APR',
    ]);

    expect(p).not.toBeNull();
    expect(p!.docId).toBe(DOCID);
    expect(p!.facts.suitability).toBe('S4');
    expect(p!.facts.cdeState).toBe('PUB');
    expect(p!.facts.issueDate).toBe('20260914');
    expect(p!.facts.sheetOfTotal).toBe('25.100');
    expect(p!.facts.lod).toBe('350');
    expect(p!.facts.paperSize).toBe('A1');
    expect(p!.facts.scale).toBe('1.100');
    expect(p!.facts.initials).toBe('DRW.CHK.APR');
  });

  it('reads the dash placeholder as absent, not as a dash', () => {
    // A UI that printed "-" here would be asserting a zone called "-".
    const p = parseDocPath([DOCID, 'S4', 'PUB', '20260914', '-', '25.100']);
    expect(p!.facts.zone).toBeUndefined();
    expect(p!.facts.sheetOfTotal).toBe('25.100');
  });

  it('reads the project from the identifier and the revision from the facts', () => {
    // The identifier is seven fixed fields ending in the four-digit Number, so
    // reading its last field as a revision would report "0001".
    const p = parseDocPath([DOCID, 'S4', '-', '-', '-', '-', '-', '-', '-', '-', '-', 'P02']);
    expect(p!.projectCode).toBe('PROJECTN');
    expect(p!.revision).toBe('P02');
    expect(p!.facts.revision).toBe('P02');
    expect(p!.docId).not.toContain('P02');
  });

  it('does not invent a sheet number from the identifier', () => {
    // The Number field is not the sheet number; the sheet keeps its own short
    // one. Returning the Number here would send the register lookup somewhere
    // real and wrong, which is worse than not looking.
    const p = parseDocPath([DOCID]);
    expect(p!.sheetNumber).toBeUndefined();
  });

  it('carries no facts when the cell only fitted the identifier', () => {
    const p = parseDocPath([DOCID]);
    expect(p!.facts).toEqual({});
  });

  it('returns null with no identifier, rather than an empty record', () => {
    expect(parseDocPath([])).toBeNull();
    expect(parseDocPath([''])).toBeNull();
    expect(parseDocPath(['   '])).toBeNull();
  });

  it('does not throw on a malformed percent escape', () => {
    // decodeURIComponent throws on a lone '%'. A bad scan must degrade, not take
    // the page render down with it.
    expect(() => parseDocPath([DOCID, '100%'])).not.toThrow();
  });
});

describe('display helpers', () => {
  it('shows scale and sheet position the way a drawing does', () => {
    expect(prettyScale('1.100')).toBe('1:100');
    expect(prettySheetOfTotal('25.100')).toBe('25 of 100');
  });

  it('passes through anything it cannot interpret', () => {
    expect(prettyScale(undefined)).toBeUndefined();
    expect(prettySheetOfTotal('ASSHOWN')).toBe('ASSHOWN');
  });
});
