// ════════════════════════════════════════════════════════════════════════
//  qrParser — THE QR payload contract, mobile side.
//
//  WHY THIS FILE CHANGED (2026-09-14)
//  ----------------------------------
//  This parser accepted `planscape://…` and bare UUIDs. The Revit plugin has
//  only ever produced `sting://asset/{code}/{tag}`. So every QR code StingTools
//  generated hit the `unknown` branch below and was rejected here — before any
//  network call, so nothing logged, nothing 500'd, and the failure looked like
//  a bad scan rather than a format mismatch.
//
//  The server end always worked: /api/tagsync/elements/search ILIKEs on Tag1,
//  so a STING tag resolves fine once it gets past this function.
//
//  This is the mirror of StingTools/Core/StingQrPayload.cs (StingQrFormat).
//  Change one, change both; the shared corpus in tests/qrParser.contract.test.mjs
//  asserts the two agree on every case.
// ════════════════════════════════════════════════════════════════════════

export interface QrPayload {
  type: 'element' | 'issue' | 'document' | 'sheet' | 'unknown';
  /** Primary lookup key: the ISO 19650 tag, or a UniqueId when that is all we got. */
  id?: string;
  /** ISO 19650 tag, when the payload carried one. */
  tag?: string;
  /** Revit UniqueId, when the payload carried one. Needed by commissioning. */
  uniqueId?: string;
  /** Sheet number, on a `sheet` payload (a title-block stamp). */
  sheetNumber?: string;
  /** Sheet revision carried by a `sheet` payload, when the producer had one. */
  revision?: string;
  /** Project code from the payload. Informational — the app scopes lookups by
   *  the active project, so a mismatch here is worth SHOWING, not acting on. */
  projectCode?: string;
  /** Full ISO 19650 document identifier, on a `/d/` document payload. Its
   *  presence is what distinguishes the rich drawing form from the older
   *  `planscape://document/{id}` form, which carries only an id. */
  docId?: string;
  /** Issue facts carried IN the code. Readable with NO network — the reason the
   *  rich form exists. Undefined on every other payload kind. */
  facts?: QrDocFacts;
  raw: string;
}

/** The issue record a `/d/` code carries, in the order it is encoded. Every
 *  field is optional: the producer sheds them from the least important end when
 *  the printed cell is too small (StingQrFormat.BuildDocUrlWithin), so an absent
 *  field means "did not fit or not known", never "empty". */
export interface QrDocFacts {
  suitability?: string;
  cdeState?: string;
  /** yyyyMMdd, the date the code was stamped. */
  issueDate?: string;
  zone?: string;
  /** "25.100" = sheet 25 of 100. */
  sheetOfTotal?: string;
  lod?: string;
  paperSize?: string;
  /** "1.100" = 1:100 — ':' is legal in QR alphanumeric mode but reserved in a
   *  URL path, so the producer substitutes '.'. */
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

/** Deep-link host. Mirrors StingQrFormat.BaseUrl + ElementPath. */
const ELEMENT_URL = /^https?:\/\/app\.planscape\.build\/e\/([^/?#]+)\/([^/?#]+)(?:\?([^#]*))?$/i;

/** Sheet deep link, stamped into the title block by SheetQrStamper. */
const SHEET_URL = /^https?:\/\/app\.planscape\.build\/s\/([^/?#]+)\/([^/?#]+)(?:\?([^#]*))?$/i;

/** The RICH document deep link: `/d/{iso19650-id}/{fact}/{fact}/...`.
 *
 *  Positional, not key=value, and that is deliberate on the producer side: QR
 *  alphanumeric mode has no '?', '&' or '=', so one query character drops the
 *  whole payload into byte mode and costs ~40% of the printable capacity. The
 *  trailing part is matched loosely here and split below. */
const DOC_URL = /^https?:\/\/app\.planscape\.build\/d\/([^/?#]+)((?:\/[^?#]*)?)$/i;

/** Placeholder the producer writes for an absent INTERIOR fact, so the fields
 *  after it keep their positions. Must read back as "not carried", not as "-". */
const BLANK = '-';

/** Legacy scheme, pre-2026-09. Still parsed: codes are already printed on
 *  issued sheets and must keep resolving. Never emitted. */
const LEGACY_URL = /^sting:\/\/asset\/([^/?#]+)\/([^/?#]+)(?:\?([^#]*))?$/i;

const NATIVE_URL = /^planscape:\/\/(element|issue|document)\/([^/?#]+)\/?$/i;

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A Revit UniqueId: a GUID, optionally with "-" + 8 hex digits of element id. */
const UNIQUE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(-[0-9a-f]{8})?$/i;

function readQuery(query: string | undefined, key: string): string | undefined {
  if (!query) return undefined;
  for (const pair of query.split('&')) {
    const eq = pair.indexOf('=');
    if (eq <= 0) continue;
    if (pair.slice(0, eq).toLowerCase() !== key) continue;
    const value = safeDecode(pair.slice(eq + 1));
    return value ? value : undefined;
  }
  return undefined;
}

/** decodeURIComponent throws on a lone `%`. A malformed scan must fall through
 *  to `unknown` and say so, not crash the camera view. */
function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export function parseQr(raw: string): QrPayload {
  const trimmed = raw.trim();
  if (!trimmed) return { type: 'unknown', raw: trimmed };

  // 0a — rich document deep link. Matched before the sheet form because it is
  //      the more specific of the two, and the facts it carries are the whole
  //      reason to prefer it: they are readable with no network.
  const doc = trimmed.match(DOC_URL);
  if (doc) {
    const docId = safeDecode(doc[1]);
    if (!docId) return { type: 'unknown', raw: trimmed };

    const segs = (doc[2] || '')
      .replace(/^\//, '')
      .split('/')
      .map(safeDecode);
    const at = (i: number): string | undefined => {
      const v = segs[i];
      return v && v !== BLANK ? v : undefined;
    };

    // Project-Originator-Volume-Level-Type-Role-Number -- SEVEN fields, fixed,
    // assembled by Iso19650DocumentCode. The project is the first; the REVISION
    // is not in it at all and arrives as a carried fact. Reading the last field
    // as a revision would report the four-digit Number instead.
    const parts = docId.split('-');
    return {
      type: 'document',
      id: docId,
      docId,
      projectCode: parts.length > 1 ? parts[0] : undefined,
      revision: at(10),
      // The Number field is not the SHEET number -- the sheet keeps its own short
      // number, which is the whole point of the arrangement. Nothing here can
      // recover it, so nothing here pretends to.
      sheetNumber: undefined,
      facts: {
        suitability: at(0),
        cdeState: at(1),
        issueDate: at(2),
        zone: at(3),
        sheetOfTotal: at(4),
        lod: at(5),
        paperSize: at(6),
        scale: at(7),
        initials: at(8),
        signature: at(9),
        revision: at(10),
      },
      raw: trimmed,
    };
  }

  // 0 — sheet deep link (a title-block stamp, not an element).
  const sheet = trimmed.match(SHEET_URL);
  if (sheet) {
    const sheetNumber = safeDecode(sheet[2]);
    if (!sheetNumber) return { type: 'unknown', raw: trimmed };
    return {
      type: 'sheet',
      id: sheetNumber,
      sheetNumber,
      projectCode: safeDecode(sheet[1]),
      revision: readQuery(sheet[3], 'r'),
      raw: trimmed,
    };
  }

  // 1 + 2 — current https deep link, and the legacy sting:// form.
  const url = trimmed.match(ELEMENT_URL) ?? trimmed.match(LEGACY_URL);
  if (url) {
    const tag = safeDecode(url[2]);
    if (!tag) return { type: 'unknown', raw: trimmed };
    return {
      type: 'element',
      id: tag,
      tag,
      projectCode: safeDecode(url[1]),
      uniqueId: readQuery(url[3], 'u'),
      raw: trimmed,
    };
  }

  // 3 — mobile-native scheme. For an element the id may be a UniqueId or a tag;
  //     test the shape rather than assuming one.
  const native = trimmed.match(NATIVE_URL);
  if (native) {
    const kind = native[1].toLowerCase() as 'element' | 'issue' | 'document';
    const id = safeDecode(native[2]);
    if (!id) return { type: 'unknown', raw: trimmed };
    if (kind !== 'element') return { type: kind, id, raw: trimmed };
    return UNIQUE_ID.test(id)
      ? { type: 'element', id, uniqueId: id, raw: trimmed }
      : { type: 'element', id, tag: id, raw: trimmed };
  }

  // 4 — bare UniqueId (or plain UUID, which the old parser already accepted).
  if (UNIQUE_ID.test(trimmed) || UUID.test(trimmed)) {
    return { type: 'element', id: trimmed, uniqueId: trimmed, raw: trimmed };
  }

  // 5 — bare ISO 19650 tag, e.g. from a hand-typed label. Requires a separator
  //     and no whitespace so a stray word does not become a lookup that matches
  //     nothing and reads as "element not in this project".
  if (trimmed.includes('-') && !/\s/.test(trimmed) && !trimmed.includes('://')) {
    return { type: 'element', id: trimmed, tag: trimmed, raw: trimmed };
  }

  return { type: 'unknown', raw: trimmed };
}
