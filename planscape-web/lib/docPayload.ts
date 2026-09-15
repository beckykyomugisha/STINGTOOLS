/**
 * The rich `/d/` document payload, web half.
 *
 * Mirrors `StingQrFormat` in StingTools/Core/StingQrPayload.cs and `parseQr` in
 * Planscape/src/services/qrParser.ts. Three copies is two too many, but they run
 * in three runtimes with no shared package; what keeps them honest is
 * tools/qr_payload_corpus.json, which the C# and mobile suites both read.
 *
 * WHY THE FORMAT LOOKS LIKE THIS
 * ------------------------------
 * Positional path segments, upper case, no query string. QR alphanumeric mode
 * packs 5.5 bits/char against byte mode's 8, and its charset has no '?', '&' or
 * '='. One query character drops the whole payload into byte mode and costs
 * ~40% of what a 31 mm printed cell can hold. So the facts are positional, and
 * an absent interior field is a '-' holding its place — without that, every
 * field after a gap shifts one position and reads as its neighbour.
 */

/** The issue record a `/d/` code carries. Every field optional: the producer
 *  sheds them from the least important end when the printed cell is too small,
 *  so absent means "did not fit, or not known" — never "empty". */
export interface DocFacts {
  suitability?: string;
  cdeState?: string;
  /** yyyyMMdd — the date the code was STAMPED, a fact about this print. */
  issueDate?: string;
  zone?: string;
  /** "25.100" = sheet 25 of 100. */
  sheetOfTotal?: string;
  lod?: string;
  paperSize?: string;
  /** "1.100" = 1:100 — the producer substitutes '.' for ':'. */
  scale?: string;
  /** "DRW.CHK.APR" — drawn, checked, approved. */
  initials?: string;
  signature?: string;
  /** Sheet revision. A FACT, not part of the identifier: ISO keeps revision
   *  as metadata beside the identity, and the identifier's last field is now
   *  the four-digit Number. Reading the revision off the end would report
   *  that number as the revision. Appended last -- the field order is a
   *  printed contract and may only grow at the end. */
  revision?: string;
}

export interface DocPayload {
  /** Full ISO 19650 identifier:
   *  Project-Originator-Volume-Level-Type-Role-Number. Seven fields, no revision. */
  docId: string;
  projectCode?: string;
  revision?: string;
  /** Always undefined now. The identifier's Number field is NOT the sheet
   *  number — the sheet keeps its own short number, which is the point of the
   *  arrangement — and nothing can recover one from the other. Kept so callers
   *  that branch on it still compile, and so the absence is explicit. */
  sheetNumber?: string;
  facts: DocFacts;
}

const BLANK = '-';

/** The order is the contract. Append only — reordering silently re-labels every
 *  code already printed on paper. */
const FIELDS: (keyof DocFacts)[] = [
  'suitability', 'cdeState', 'issueDate', 'zone', 'sheetOfTotal',
  'lod', 'paperSize', 'scale', 'initials', 'signature', 'revision',
];

function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    // decodeURIComponent throws on a lone '%'. A malformed segment must degrade
    // to its raw text, not throw out of a page render.
    return value;
  }
}

/**
 * Parse the path parts after `/d/`. Returns null when there is no identifier —
 * the caller must then say so rather than render an empty record.
 */
export function parseDocPath(parts: string[]): DocPayload | null {
  if (!parts || parts.length === 0) return null;

  const docId = safeDecode(parts[0] || '').trim();
  if (!docId) return null;

  const facts: DocFacts = {};
  FIELDS.forEach((key, i) => {
    const raw = parts[i + 1];
    if (raw === undefined) return;
    const v = safeDecode(raw).trim();
    if (v && v !== BLANK) facts[key] = v;
  });

  // Project-Originator-Volume-Level-Type-Role-Number -- SEVEN fixed fields,
  // assembled by Iso19650DocumentCode. The revision is NOT in it; it arrives as
  // a carried fact, because a document's identity does not change when it is
  // revised. Reading the last field as a revision would report the Number.
  //
  // The Number is also not the SHEET number: the sheet keeps its own short one,
  // which is the entire point of the arrangement, and nothing here can recover
  // it from the identifier -- so nothing here pretends to.
  const seg = docId.split('-');
  const projectCode = seg.length > 1 ? seg[0] : undefined;
  const revision = facts.revision;
  const sheetNumber = undefined;

  return { docId, projectCode, revision, sheetNumber, facts };
}

/** Render "1.100" back as "1:100" and "25.100" as "25 of 100" for display only.
 *  Never fed back into a lookup. */
export function prettyScale(scale?: string): string | undefined {
  return scale ? scale.replace('.', ':') : undefined;
}

export function prettySheetOfTotal(v?: string): string | undefined {
  if (!v) return undefined;
  const [n, total] = v.split('.');
  return n && total ? `${n} of ${total}` : v;
}
