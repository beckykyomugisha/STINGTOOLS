// StingTools — the token vocabulary every sheet-number pattern shares
//
// Two builders turn a pattern into a sheet number:
//
//   * SheetNumberEngine.ApplyTokenPattern — a drawing type's sheetNumberPattern
//     (Drawing Types production, DrawingTypes_Renumber, the fabrication composer).
//   * SheetDisciplineResolver.FormatNumber — the project's title-block pattern
//     PRJ_TB_SHEET_NUMBER_PATTERN_TXT (Sheet Manager, sheet sets, Auto-Number,
//     Reorder).
//
// They differ on purpose in ONE way: what an empty value becomes. A drawing-type
// number is often a positional ISO 19650 identifier, where a missing field must
// stay visible as "XX"; a project number collapses the empty token and its
// separator so "A--001" never reaches a filename. Everything else — the width of
// a bare {seq}, how {seq:Dn} is read, and which names mean the project and
// originator — used to differ by accident. A bare {seq} was 3 digits in one and 4
// in the other, and {proj} worked in one while {project} worked in the other, so a
// pattern copied between the two settings silently changed shape. Those rules
// live here now, and both builders call them.
//
// Revit-free; unit-tested (StingTools.Tags.Tests).

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class SheetNumberTokens
    {
        /// <summary>Digits a bare {seq} pads to. Four is what every shipped
        /// drawing type, the fabrication composer and the MEP level producer
        /// already used; write {seq:D3} to ask for three.</summary>
        public const int DefaultSeqWidth = 4;

        /// <summary>Widest {seq:Dn} honoured. A wider request is capped rather than
        /// producing a 40-digit sheet number from a typo.</summary>
        public const int MaxSeqWidth = 8;

        private static readonly Regex SeqToken = new Regex(@"\{seq(?::[Dd](\d+))?\}", RegexOptions.Compiled);

        /// <summary>Token names that mean the same thing. The key is the name the
        /// builders resolve; the values are accepted spellings of it.</summary>
        private static readonly Dictionary<string, string> Canonical =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "proj", "project" }, { "project", "project" },
                { "orig", "originator" }, { "originator", "originator" },
            };

        /// <summary>
        /// The ISO 19650 tokens a drawing-type number takes from the token dictionary
        /// (DrawingTokenContext) rather than from a named argument, by canonical name.
        /// A KNOWN token is never left in braces: when the dictionary has no entry for
        /// it, SheetNumberEngine prints the empty-value placeholder "XX". A supplied
        /// empty string is the caller's and is kept. An UNKNOWN token ({bogus}) is left
        /// literal so DrawingTokenContext.AuditPattern reports it and Revit refuses it,
        /// rather than being guessed at.
        /// </summary>
        public static readonly IReadOnlyList<string> DictionaryTokens =
            new[] { "project", "originator", "vol", "type", "role", "suit", "rev" };

        /// <summary>Every accepted spelling of <see cref="DictionaryTokens"/>
        /// ({proj}, {orig} included).</summary>
        public static IEnumerable<string> DictionaryTokenSpellings()
        {
            foreach (var t in DictionaryTokens)
            {
                bool any = false;
                foreach (var s in Spellings(t)) { any = true; yield return s; }
                if (!any) yield return t;
            }
        }

        /// <summary>The canonical name for a token ("proj" → "project"), or the
        /// name unchanged when it has no alias.</summary>
        public static string CanonicalName(string name)
            => name != null && Canonical.TryGetValue(name, out string c) ? c : name;

        /// <summary>Every spelling of a canonical token, itself included.</summary>
        public static IEnumerable<string> Spellings(string canonical)
        {
            foreach (var kv in Canonical)
                if (string.Equals(kv.Value, canonical, StringComparison.OrdinalIgnoreCase))
                    yield return kv.Key;
        }

        /// <summary>Width a {seq} token's format asks for: "D3" → 3, null or
        /// unreadable → <see cref="DefaultSeqWidth"/>.</summary>
        public static int SeqWidth(string format)
        {
            if (string.IsNullOrEmpty(format) || format.Length < 2) return DefaultSeqWidth;
            if (format[0] != 'D' && format[0] != 'd') return DefaultSeqWidth;
            if (!int.TryParse(format.Substring(1), out int w) || w <= 0) return DefaultSeqWidth;
            return Math.Min(w, MaxSeqWidth);
        }

        /// <summary>The sequence at the width the token asks for.</summary>
        public static string FormatSeq(int seq, string format)
            => seq.ToString("D" + SeqWidth(format));

        /// <summary>Replace every {seq} / {seq:Dn} in <paramref name="pattern"/>.</summary>
        public static string ApplySeq(string pattern, int seq)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            return SeqToken.Replace(pattern, m =>
                FormatSeq(seq, m.Groups[1].Success ? "D" + m.Groups[1].Value : null));
        }
    }
}
