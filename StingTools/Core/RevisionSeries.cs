// StingTools — Revision Series (canonical table)
//
// Single source of truth for the revision-code series STING recognises.
// Previously this knowledge lived in three places that had drifted apart:
//
//   - RevisionEngine.ValidateRevisionNumber accepted only P##/C##/single
//     letter/numeric, so legitimate T01 / Co02 / AB01 codes were flagged
//     invalid and "auto-corrected" by RevisionNamingEnforceCommand.
//   - CreateRevisionCommand.InferSeriesName mapped prefixes to labels.
//   - BIMCoordinationCenter.BuildIsoRevisionCodes offered 9 series plus
//     status stamps in the Revisions-tab dropdown.
//
// Both the validator and the series-name inference now delegate here, so
// what the BCC offers is exactly what the validator accepts.
//
// Series (ISO 19650-2 / BS 1192 UK national annex practice):
//   T##   Tender          P##  Preliminary     Co## Contract
//   C##   Construction    R##  Revision        B##  Building (partial sign-off)
//   D##   Digital         A1/A2 Approved       AB## As-Built (explicit)
//   A-Z   As-Built (single-letter record drawings)
//   IFC / IFA / IFR / IFT / IFP / IFI / IFPT / WD / SS / OB — status stamps
//   1, 2, 3…  legacy numeric

using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    /// <summary>
    /// Canonical revision-code series table plus validation and series-name
    /// inference. See <see cref="Series"/> for the full catalogue.
    /// </summary>
    public static class RevisionSeries
    {
        /// <summary>One recognised revision series.</summary>
        public sealed class SeriesDef
        {
            /// <summary>Code prefix as typed by users, e.g. "P", "Co", "AB".
            /// Empty for the pattern-only entries (single-letter, numeric).</summary>
            public string Prefix { get; }
            /// <summary>Human-readable series label, e.g. "Preliminary".</summary>
            public string Label  { get; }
            /// <summary>Anchored regex matched against the UPPERCASED code.</summary>
            public Regex  Pattern { get; }

            internal SeriesDef(string prefix, string label, string pattern)
            {
                Prefix  = prefix;
                Label   = label;
                Pattern = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            }
        }

        // ORDER MATTERS: longer prefixes must precede shorter ones that would
        // otherwise swallow them (CO before C, AB before A, IF* before the
        // single-letter fallback).
        private static readonly SeriesDef[] _series =
        {
            new SeriesDef("T",  "Tender",       @"^T\d{2}$"),
            new SeriesDef("P",  "Preliminary",  @"^P\d{2}$"),
            new SeriesDef("Co", "Contract",     @"^CO\d{2}$"),
            new SeriesDef("C",  "Construction", @"^C\d{2}$"),
            new SeriesDef("R",  "Revision",     @"^R\d{2}$"),
            new SeriesDef("B",  "Building",     @"^B\d{2}$"),
            new SeriesDef("D",  "Digital",      @"^D\d{2}$"),
            new SeriesDef("AB", "As-Built",     @"^AB\d{2}$"),
            new SeriesDef("A",  "Approved",     @"^A\d{1,2}$"),
            new SeriesDef("",   "Status Stamp", @"^(IFC|IFA|IFR|IFT|IFP|IFI|IFPT|WD|SS|OB)$"),
            new SeriesDef("",   "As-Built",     @"^[A-Z]$"),
            new SeriesDef("",   "Legacy",       @"^\d+$"),
        };

        /// <summary>The canonical series catalogue, in match order.</summary>
        public static SeriesDef[] Series => _series;

        /// <summary>Code prefixes for the named series, in match order
        /// (excludes the pattern-only entries).</summary>
        public static string[] Prefixes =>
            _series.Where(s => !string.IsNullOrEmpty(s.Prefix)).Select(s => s.Prefix).ToArray();

        /// <summary>
        /// Validates a revision number against the canonical series table.
        /// Returns null when valid, otherwise a human-readable message.
        /// </summary>
        public static string Validate(string revNum)
        {
            if (string.IsNullOrWhiteSpace(revNum)) return "Revision number is empty";
            string c = revNum.Trim().ToUpperInvariant();

            foreach (var s in _series)
                if (s.Pattern.IsMatch(c)) return null;

            return $"Non-standard revision number '{revNum}'. Expected one of: " +
                   "T01-T99, P01-P99, Co01-Co99, C01-C99, R01-R99, B01-B99, D01-D99, " +
                   "A1/A2, AB01-AB99, a single letter A-Z, a status stamp " +
                   "(IFC/IFA/IFR/IFT/IFP/IFI/IFPT/WD/SS/OB), or a legacy number.";
        }

        /// <summary>
        /// Extracts the series prefix from a NUMBERED series code ("P01" → "P",
        /// "Co03" → "Co", "AB02" → "AB"). Returns false for status stamps,
        /// bare single letters, plain numerics, and unrecognised codes — those
        /// have no numbering sequence.
        /// </summary>
        public static bool TryParseSeriesPrefix(string code, out string prefix, out string label)
        {
            prefix = null; label = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            string c = code.Trim().ToUpperInvariant();

            foreach (var s in _series.Where(x => !string.IsNullOrEmpty(x.Prefix))
                                     .OrderByDescending(x => x.Prefix.Length))
            {
                if (s.Pattern.IsMatch(c))
                {
                    prefix = s.Prefix;   // canonical casing, e.g. "Co"
                    label  = s.Label;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The full ordered code list for a series' Revit numbering sequence:
        /// "{prefix}01" … "{prefix}99". Used to mint an alphanumeric
        /// RevisionNumberingSequence whose values ARE the ISO codes, so
        /// Revit's RevisionNumber (and every native revision schedule /
        /// Current Revision label) shows "P01" instead of "3".
        /// </summary>
        public static string[] BuildSequenceCodes(string prefix)
        {
            var codes = new string[99];
            for (int i = 1; i <= 99; i++)
                codes[i - 1] = prefix + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            return codes;
        }

        /// <summary>
        /// Maps a revision code to its series label for human-readable revision
        /// names. Deliberately more permissive than <see cref="Validate"/>: it
        /// matches on prefix, so bespoke project codes ("PQ-01", "G3-A") still
        /// resolve to a sensible series rather than failing. Unrecognised codes
        /// return "Custom".
        /// </summary>
        public static string InferSeriesName(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "Custom";
            string c = code.Trim().ToUpperInvariant();

            // Exact-match entries first (status stamps).
            foreach (var s in _series)
                if (string.IsNullOrEmpty(s.Prefix) && s.Label == "Status Stamp" && s.Pattern.IsMatch(c))
                    return s.Label;

            // Prefix match, longest prefix first so CO/AB beat C/A.
            foreach (var s in _series
                         .Where(x => !string.IsNullOrEmpty(x.Prefix))
                         .OrderByDescending(x => x.Prefix.Length))
            {
                if (c.StartsWith(s.Prefix.ToUpperInvariant(), StringComparison.Ordinal))
                {
                    // A bare "A"/"AB" with no digits is a record-drawing code.
                    if (s.Label == "Approved" && c.Length == 1) return "As-Built";
                    return s.Label;
                }
            }

            // Plain single-letter as-built codes (A-Z without suffix).
            if (c.Length == 1 && c[0] >= 'A' && c[0] <= 'Z') return "As-Built";
            return "Custom";
        }
    }
}
