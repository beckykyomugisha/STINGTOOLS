using System;

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
        Sheet
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
