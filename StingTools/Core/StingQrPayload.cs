using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════════
    //  StingQrPayload — THE QR payload contract.
    //
    //  WHY THIS FILE EXISTS
    //  --------------------
    //  Until 2026-09-14 the plugin encoded `sting://asset/{code}/{tag}` and the
    //  Planscape scanner (Planscape/src/services/qrParser.ts) accepted only
    //  `planscape://…` or a bare UUID. Neither side was wrong on its own; they
    //  had simply never been introduced. Every QR the plugin had ever produced
    //  was rejected by our own app, silently, at the parse step — before any
    //  network call, so nothing logged and nothing failed loudly.
    //
    //  The fix is not "pick a scheme". It is having ONE place that states the
    //  format, on both sides, kept in step by a shared corpus: the cases here
    //  are mirrored verbatim in Planscape/tests/qrParser.contract.test.mjs.
    //
    //  THE FORMAT
    //  ----------
    //      https://app.planscape.build/e/{projectCode}/{tag}?u={uniqueId}
    //
    //  https, not a custom scheme, because a site operative's stock camera app
    //  will open it. A custom scheme shows an unopenable string to anyone who
    //  does not already have the app installed — which is exactly the person a
    //  QR on a printed drawing is for.
    //
    //  `u` (the Revit UniqueId) is OPTIONAL and carried for the commissioning
    //  path, which resolves elements by UniqueId and could not act on a scan at
    //  all while the payload carried only the tag.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>What kind of thing a scanned code points at.</summary>
    public enum StingQrKind
    {
        /// <summary>A modelled element, keyed by ISO 19650 tag and/or UniqueId.</summary>
        Element,
        /// <summary>A drawing sheet, keyed by sheet number (+ revision).</summary>
        Sheet,
        /// <summary>A drawing DOCUMENT, keyed by its full ISO 19650 identifier and
        /// carrying the issue facts printed on the title block.</summary>
        Document
    }

    /// <summary>What a scanned STING QR code resolves to.</summary>
    public sealed class StingQrPayload
    {
        /// <summary>Element or Sheet. Callers MUST branch on this — a sheet payload
        /// carries no Tag, and treating it as an element search finds nothing.</summary>
        public StingQrKind Kind { get; set; } = StingQrKind.Element;

        /// <summary>Sheet number, on a <see cref="StingQrKind.Sheet"/> payload.</summary>
        public string SheetNumber { get; set; }

        /// <summary>Sheet revision, when the producer carried one. Null otherwise.</summary>
        public string Revision { get; set; }

        /// <summary>ISO 19650 tag (ASS_TAG_1_TXT). The primary key; always present
        /// on a payload this plugin produced.</summary>
        public string Tag { get; set; }

        /// <summary>Project code (PRJ_ORG_PROJECT_CODE_TXT). Null on a bare-tag or
        /// uid-only payload.</summary>
        public string ProjectCode { get; set; }

        /// <summary>Revit UniqueId, when the producer carried one. Null otherwise —
        /// callers that need it MUST branch, never assume.</summary>
        public string UniqueId { get; set; }

        /// <summary>Full ISO 19650 document identifier (SHT_TAG_1_TXT), on a
        /// Document payload. Null on every other kind.</summary>
        public string DocId { get; set; }

        /// <summary>Issue facts carried IN the code, readable with no network — the
        /// whole point of the rich form. Null on every other kind.</summary>
        public StingQrFormat.DocFacts Facts { get; set; }

        /// <summary>The exact string that was scanned, for diagnostics.</summary>
        public string Raw { get; set; }
    }

    public static class StingQrFormat
    {
        /// <summary>Deep-link host. Claimed by the Planscape app via app-links /
        /// universal-links; falls back to the web app in any browser.</summary>
        public const string BaseUrl = "https://app.planscape.build";

        /// <summary>Path segment for an element deep link. Kept short — QR module
        /// count grows with payload length, and these get printed small.</summary>
        public const string ElementPath = "e";

        /// <summary>Path segment for a sheet deep link.</summary>
        public const string SheetPath = "s";

        /// <summary>Path segment for a DOCUMENT deep link — the rich form, keyed by
        /// the full ISO 19650 document identifier rather than the bare sheet number.
        /// </summary>
        public const string DocPath = "d";

        private const string LegacyPrefix = "sting://asset/";
        private const string NativePrefix = "planscape://element/";

        /// <summary>Build the URL a SHEET's title-block QR code should encode.</summary>
        /// <param name="projectCode">PRJ_ORG_PROJECT_CODE_TXT; "PRJ" when unset.</param>
        /// <param name="sheetNumber">ViewSheet.SheetNumber. Required.</param>
        /// <param name="revision">Current sheet revision, or null to omit ?r=.</param>
        public static string BuildSheetUrl(string projectCode, string sheetNumber, string revision = null)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber))
                throw new ArgumentException("sheetNumber is required", nameof(sheetNumber));

            string code = string.IsNullOrWhiteSpace(projectCode) ? "PRJ" : projectCode.Trim();
            string url = BaseUrl + "/" + SheetPath + "/"
                       + Uri.EscapeDataString(code) + "/"
                       + Uri.EscapeDataString(sheetNumber.Trim());
            if (!string.IsNullOrWhiteSpace(revision))
                url += "?r=" + Uri.EscapeDataString(revision.Trim());
            return url;
        }

        /// <summary>Fields a document deep link can carry, beyond the identifier
        /// itself. Every one is already on the title block or the sheet — nothing
        /// here is invented for the QR.</summary>
        public sealed class DocFacts
        {
            /// <summary>Suitability code — S0..S7 / A1..A5.</summary>
            public string Suitability { get; set; }
            /// <summary>CDE state — WIP / SHARED / PUB / ARCHIVE.</summary>
            public string CdeState { get; set; }
            /// <summary>Issue date, yyyyMMdd.</summary>
            public string IssueDate { get; set; }
            public string Zone { get; set; }
            /// <summary>Sheet position, e.g. "25.100" for sheet 25 of 100.</summary>
            public string SheetOfTotal { get; set; }
            /// <summary>LOD number only — "350", not "LOD 350".</summary>
            public string Lod { get; set; }
            public string PaperSize { get; set; }
            /// <summary>Scale with '.' for ':' — "1.100", since ':' is legal in QR
            /// alphanumeric mode but reserved in a URL path.</summary>
            public string Scale { get; set; }
            /// <summary>Drawn.Checked.Approved initials, e.g. "DRW.CHK.APR".</summary>
            public string Initials { get; set; }
            /// <summary>Truncated HMAC proving the print came from the issue process.
            /// Null until a signing key is configured.</summary>
            public string Signature { get; set; }
            /// <summary>Sheet revision.
            ///
            /// A FACT, not part of the identifier. It used to be the identifier's last
            /// segment, which made every revision mint a new "document" and, once the
            /// identifier became seven proper ISO fields, would have reported the
            /// NUMBER as the revision. ISO keeps revision as metadata beside the
            /// identity, and so does this.
            ///
            /// Appended last because the field order is a printed contract: a code
            /// already on paper reads its fields positionally, so the list may only
            /// ever grow at the end.</summary>
            public string Revision { get; set; }
        }

        /// <summary>Build the RICH document deep link, keyed by the full ISO 19650
        /// document identifier (SHT_TAG_1_TXT).
        ///
        /// WHY PATH SEGMENTS AND NOT A QUERY STRING
        /// ----------------------------------------
        /// QR alphanumeric mode packs 5.5 bits/char, byte mode 8. Its charset is
        /// 0-9 A-Z space and $ % * + - . / : — it does NOT contain '?', '&' or '='.
        /// One query string therefore drops the ENTIRE payload into byte mode and
        /// forfeits ~40% of the symbol's capacity. Measured on ZXing 0.16.9 at
        /// ECC Q: the same facts cost 53 modules as a query string and 49 as path
        /// segments, while carrying ELEVEN MORE characters.
        ///
        /// That is why this is upper-case and slash-separated, and why callers must
        /// not "tidy" it into ?key=value form. SheetQrDensityTests pins the budget.
        ///
        /// Trailing empty facts are omitted, so a project that knows only its
        /// suitability gets a short link rather than a run of empty segments.</summary>
        public static string BuildDocUrl(string docId, DocFacts facts = null)
        {
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId is required", nameof(docId));

            var parts = new List<string> { Seg(docId) };
            if (facts != null)
            {
                // ORDER IS THE CONTRACT — positional, so it can never be reordered
                // without breaking every code already printed. Append only.
                parts.Add(Seg(facts.Suitability));
                parts.Add(Seg(facts.CdeState));
                parts.Add(Seg(facts.IssueDate));
                parts.Add(Seg(facts.Zone));
                parts.Add(Seg(facts.SheetOfTotal));
                parts.Add(Seg(facts.Lod));
                parts.Add(Seg(facts.PaperSize));
                parts.Add(Seg(facts.Scale));
                parts.Add(Seg(facts.Initials));
                parts.Add(Seg(facts.Signature));
                parts.Add(Seg(facts.Revision));
            }

            // Drop trailing blanks only. An INTERIOR blank must stay as "-", or every
            // field after it shifts one place and silently reads as the wrong thing.
            int last = parts.Count - 1;
            while (last > 0 && parts[last] == Blank) last--;

            return BaseUrl.ToUpperInvariant() + "/" + DocPath.ToUpperInvariant() + "/"
                 + string.Join("/", parts.GetRange(0, last + 1));
        }

        /// <summary>How many alphanumeric-mode characters a square cell of this size
        /// can hold and still scan, at ECC Q.
        ///
        /// Derived from the measured symbol ladder for ZXing 0.16.9 at ECC Q with a
        /// 4-module quiet zone, against a 0.50 mm/module floor — the point below which
        /// a phone camera stops reading a plotted, folded, site-handled drawing.
        ///
        ///     cell     total modules at 0.50mm     alphanumeric chars
        ///     20 mm          40                          ~45
        ///     24 mm          48                          ~70
        ///     31 mm          62                         ~140
        ///     40 mm          80                         ~300
        ///
        /// Deliberately conservative. The failure it prevents is silent: an over-dense
        /// code prints perfectly and fails in someone's hand, weeks later, on a site
        /// with no way to reprint.</summary>
        public static int MaxCharsForCell(double cellMm)
        {
            if (cellMm <= 0) return 0;
            if (cellMm < 20.0) return 0;     // too small for any URL worth encoding
            if (cellMm < 24.0) return 45;
            if (cellMm < 28.0) return 70;
            if (cellMm < 31.0) return 100;
            if (cellMm < 40.0) return 140;
            return 300;
        }

        /// <summary>Build the richest document link that FITS the printed cell.
        ///
        /// Facts are dropped from the least important end — signature, initials,
        /// scale, paper, LOD, sheet-of-total, zone — until the payload is inside
        /// budget. The identifier, suitability, CDE state and issue date are the last
        /// to go, because they are the ones that let a scan answer "is the sheet in my
        /// hand the current one?", which is the question worth answering offline.
        ///
        /// Returns null when even the bare identifier will not fit; the caller then
        /// falls back to the short sheet link rather than printing something dense
        /// enough to be decorative.</summary>
        public static string BuildDocUrlWithin(string docId, DocFacts facts, double cellMm)
        {
            int budget = MaxCharsForCell(cellMm);
            if (budget <= 0 || string.IsNullOrWhiteSpace(docId)) return null;

            // Progressive degradation, most expendable first.
            var ladder = new List<Action<DocFacts>>
            {
                f => f.Signature = null,
                f => f.Initials = null,
                f => f.Scale = null,
                f => f.PaperSize = null,
                f => f.Lod = null,
                f => f.SheetOfTotal = null,
                f => f.Zone = null,
                f => f.IssueDate = null,
                f => f.CdeState = null,
                f => f.Suitability = null,
            };

            var trial = Clone(facts);
            string url = BuildDocUrl(docId, trial);
            for (int i = 0; url.Length > budget && i < ladder.Count; i++)
            {
                ladder[i](trial);
                url = BuildDocUrl(docId, trial);
            }

            return url.Length <= budget ? url : null;
        }

        private static DocFacts Clone(DocFacts f) => f == null ? new DocFacts() : new DocFacts
        {
            Suitability  = f.Suitability,
            CdeState     = f.CdeState,
            IssueDate    = f.IssueDate,
            Zone         = f.Zone,
            SheetOfTotal = f.SheetOfTotal,
            Lod          = f.Lod,
            PaperSize    = f.PaperSize,
            Scale        = f.Scale,
            Initials     = f.Initials,
            Signature    = f.Signature,
        };

        /// <summary>Placeholder for an absent interior field. A single '-' is in the
        /// QR alphanumeric charset and costs one character.</summary>
        private const string Blank = "-";

        /// <summary>First '-' segment of the ISO 19650 identifier — the project code.
        /// Null rather than a guess when the identifier has no separator at all.</summary>
        private static string FirstSegment(string docId)
        {
            if (string.IsNullOrWhiteSpace(docId)) return null;
            int i = docId.IndexOf('-');
            return i > 0 ? docId.Substring(0, i) : null;
        }

        /// <summary>Coerce one field to something the alphanumeric charset accepts:
        /// upper case, and anything outside 0-9 A-Z - . folded to '.'. Never
        /// percent-encodes — '%' is legal alphanumeric but the escape DIGITS would
        /// often not be, and an escape triples the length of the thing it escapes.</summary>
        private static string Seg(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Blank;
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw.Trim().ToUpperInvariant())
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '-' || c == '.')
                    sb.Append(c);
                else if (c == ':' || c == '/' || c == ' ' || c == '_')
                    sb.Append('.');      // ':' and '/' are structural here; '_' is not in the charset
            }
            string s = sb.ToString().Trim('.');
            return s.Length == 0 ? Blank : s;
        }

        /// <summary>Build the URL a QR code should encode for one element.</summary>
        /// <param name="projectCode">PRJ_ORG_PROJECT_CODE_TXT; "PRJ" when unset.</param>
        /// <param name="tag">ASS_TAG_1_TXT. Required.</param>
        /// <param name="uniqueId">Revit UniqueId, or null to omit the ?u= segment.</param>
        public static string BuildElementUrl(string projectCode, string tag, string uniqueId = null)
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("tag is required", nameof(tag));

            string code = string.IsNullOrWhiteSpace(projectCode) ? "PRJ" : projectCode.Trim();
            string url = BaseUrl + "/" + ElementPath + "/"
                       + Uri.EscapeDataString(code) + "/"
                       + Uri.EscapeDataString(tag.Trim());
            if (!string.IsNullOrWhiteSpace(uniqueId))
                url += "?u=" + Uri.EscapeDataString(uniqueId.Trim());
            return url;
        }

        /// <summary>Parse a scanned string back into its parts. Mirrors
        /// <c>parseQr</c> in Planscape/src/services/qrParser.ts — change one, change
        /// both, and update the shared corpus in both test suites.
        ///
        /// Accepts, in order:
        ///   1. https://app.planscape.build/e/{code}/{tag}[?u=…]   — current format
        ///   2. sting://asset/{code}/{tag}                         — legacy, pre-2026-09
        ///   3. planscape://element/{id}                           — mobile-native form
        ///   4. a bare Revit UniqueId                              — uid-only payload
        ///   5. a bare ISO 19650 tag                               — hand-typed / label print
        ///
        /// Returns null when the string is none of these. A null return is the
        /// caller's signal to SAY SO — never to substitute a guess.</summary>
        public static StingQrPayload Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();

            // 0a — DOCUMENT deep link, the rich positional form. Matched first
            // because it is the most specific, and case-insensitively because the
            // whole payload is upper-cased to stay in QR alphanumeric mode.
            string docPrefix = BaseUrl + "/" + DocPath + "/";
            if (s.StartsWith(docPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var segs = s.Substring(docPrefix.Length)
                            .Split(new[] { '/' }, StringSplitOptions.None);
                if (segs.Length == 0 || string.IsNullOrWhiteSpace(segs[0])) return null;

                // Positional. A missing trailing field is absent, not empty-string,
                // so a caller can tell "not carried" from "carried as blank".
                string At(int i) =>
                    i < segs.Length && segs[i] != Blank && !string.IsNullOrWhiteSpace(segs[i])
                        ? segs[i] : null;

                string docId = segs[0];
                return new StingQrPayload
                {
                    Kind  = StingQrKind.Document,
                    DocId = docId,
                    // The identifier is Project-Originator-Volume-Level-Type-Role-Number.
                    // The project code is its first segment; the REVISION is not in it
                    // at all any more and arrives as a carried fact instead. Reading
                    // the last segment would now report the NUMBER as the revision.
                    ProjectCode = FirstSegment(docId),
                    Revision    = At(11),
                    Facts = new DocFacts
                    {
                        Suitability  = At(1),
                        CdeState     = At(2),
                        IssueDate    = At(3),
                        Zone         = At(4),
                        SheetOfTotal = At(5),
                        Lod          = At(6),
                        PaperSize    = At(7),
                        Scale        = At(8),
                        Initials     = At(9),
                        Signature    = At(10),
                        Revision     = At(11),
                    },
                    Raw = s
                };
            }

            // 0 — sheet deep link.
            string sheetPrefix = BaseUrl + "/" + SheetPath + "/";
            if (s.StartsWith(sheetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string tail = s.Substring(sheetPrefix.Length);
                string query = null;
                int qs = tail.IndexOf('?');
                if (qs >= 0) { query = tail.Substring(qs + 1); tail = tail.Substring(0, qs); }

                var segs = tail.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length < 2) return null;

                string number = Uri.UnescapeDataString(segs[1]);
                if (string.IsNullOrWhiteSpace(number)) return null;

                return new StingQrPayload
                {
                    Kind        = StingQrKind.Sheet,
                    ProjectCode = Uri.UnescapeDataString(segs[0]),
                    SheetNumber = number,
                    Revision    = ReadQueryValue(query, "r"),
                    Raw         = s
                };
            }

            // 1 + 2 — a URL with a {code}/{tag} tail.
            string prefix = null;
            if (s.StartsWith(BaseUrl + "/" + ElementPath + "/", StringComparison.OrdinalIgnoreCase))
                prefix = BaseUrl + "/" + ElementPath + "/";
            else if (s.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
                prefix = LegacyPrefix;

            if (prefix != null)
            {
                string tail = s.Substring(prefix.Length);
                string query = null;
                int q = tail.IndexOf('?');
                if (q >= 0) { query = tail.Substring(q + 1); tail = tail.Substring(0, q); }

                var parts = tail.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return null;   // a code with no tag identifies nothing

                string tag = Uri.UnescapeDataString(parts[1]);
                if (string.IsNullOrWhiteSpace(tag)) return null;

                return new StingQrPayload
                {
                    ProjectCode = Uri.UnescapeDataString(parts[0]),
                    Tag         = tag,
                    UniqueId    = ReadQueryValue(query, "u"),
                    Raw         = s
                };
            }

            // 3 — planscape://element/{id}. The id may be a UniqueId or a tag; the
            // shape test below decides which, rather than assuming one.
            if (s.StartsWith(NativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string id = Uri.UnescapeDataString(s.Substring(NativePrefix.Length).TrimEnd('/'));
                if (string.IsNullOrWhiteSpace(id)) return null;
                return LooksLikeUniqueId(id)
                    ? new StingQrPayload { UniqueId = id, Raw = s }
                    : new StingQrPayload { Tag = id, Raw = s };
            }

            // 4 — bare UniqueId.
            if (LooksLikeUniqueId(s))
                return new StingQrPayload { UniqueId = s, Raw = s };

            // 5 — bare ISO 19650 tag. Requires a separator and no whitespace, so a
            // stray word does not resolve to a lookup that quietly matches nothing.
            if (s.IndexOf('-') > 0 && s.IndexOf(' ') < 0 && s.IndexOf("://", StringComparison.Ordinal) < 0)
                return new StingQrPayload { Tag = s, Raw = s };

            return null;
        }

        /// <summary>A Revit UniqueId is a GUID, optionally followed by "-" and an
        /// 8-hex-digit element-id suffix.</summary>
        public static bool LooksLikeUniqueId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            Guid ignored;
            if (s.Length == 36 && Guid.TryParse(s, out ignored)) return true;
            if (s.Length == 45 && s[36] == '-' && Guid.TryParse(s.Substring(0, 36), out ignored))
            {
                for (int i = 37; i < 45; i++)
                    if (!Uri.IsHexDigit(s[i])) return false;
                return true;
            }
            return false;
        }

        private static string ReadQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                {
                    var v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    return string.IsNullOrWhiteSpace(v) ? null : v;
                }
            }
            return null;
        }
    }
}
