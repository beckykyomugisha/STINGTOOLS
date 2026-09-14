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
  raw: string;
}

/** Deep-link host. Mirrors StingQrFormat.BaseUrl + ElementPath. */
const ELEMENT_URL = /^https?:\/\/app\.planscape\.build\/e\/([^/?#]+)\/([^/?#]+)(?:\?([^#]*))?$/i;

/** Sheet deep link, stamped into the title block by SheetQrStamper. */
const SHEET_URL = /^https?:\/\/app\.planscape\.build\/s\/([^/?#]+)\/([^/?#]+)(?:\?([^#]*))?$/i;

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
