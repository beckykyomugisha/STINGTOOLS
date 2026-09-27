// StingTools — Drawing Template Manager · suitability, and what follows from it
//
// WHY THIS FILE EXISTS
// --------------------
// A title block printed this, on one drawing, at the same time:
//
//     STATUS        S2
//                   WIP
//     SUITABILITY   S4 - FOR APROVAL
//     CDE REF       WIP
//
// Four cells, two facts, and one outright contradiction: S2 and S4 are both
// suitability codes and a document has exactly one. "WIP" appeared twice because
// TITLE_BLOCK.csv defaulted two different parameters to it.
//
// The fix is not to pick different words for each cell. It is to notice that
// these are not four independent fields — ISO 19650 has ONE input, the
// suitability code, and the rest FOLLOW from it:
//
//     suitability  S4                            <- the only thing a person sets
//     description  Suitable for stage approval   <- defined by the code
//     CDE state    SHARED                        <- defined by the code
//
// Derive them and they cannot disagree. Let a person type each one and they
// eventually will, which is what the drawing above is a picture of.
//
// Revit-free, so it is unit-tested.

using System;
using System.Linq;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public static class Iso19650Suitability
    {
        /// <summary>Which CDE container a document with this suitability belongs in.
        ///
        /// ISO 19650-2:
        ///   S0            work in progress, not yet shared        -> WIP
        ///   S1 .. S7      shared for coordination/review/approval -> SHARED
        ///   A1 .. An      authorized and accepted                 -> PUBLISHED
        ///   B1 .. Bn      partial sign-off, with comments         -> PUBLISHED
        ///   CR            as-constructed record                   -> PUBLISHED
        ///
        /// Returns null for anything it does not recognise, so a caller reports the
        /// unknown code rather than filing the document somewhere plausible. Putting a
        /// drawing in the wrong CDE container is not a cosmetic error — PUBLISHED is a
        /// contractual statement.</summary>
        public static string CdeStateFor(string suitabilityCode)
        {
            string c = Clean(suitabilityCode);
            if (c.Length == 0) return null;

            if (c == "S0") return "WIP";
            if (c.Length == 2 && c[0] == 'S' && c[1] >= '1' && c[1] <= '7') return "SHARED";
            // AB (abandoned / superseded) and AR (archive) retire a document; they are
            // filed in ARCHIVE. They used to fall into the 'A' branch below and read as
            // PUBLISHED — a retired document asserted as the contractual issue.
            if (c == "AB" || c == "AR") return "ARCHIVE";
            // A1..An / B1..Bn: letter AND digit. A bare first-letter test also accepted
            // "ARCHIVE", "ABANDONED", "A" and "B9X" as published.
            if (c.Length == 2 && (c[0] == 'A' || c[0] == 'B') && char.IsDigit(c[1]) && c[1] != '0')
                return "PUBLISHED";
            if (c == "CR") return "PUBLISHED";
            return null;
        }

        /// <summary>The suitability a document takes by default when it enters
        /// <paramref name="cdeState"/> and carries no code that already belongs there.
        ///
        /// The single answer to "what code does a promotion stamp?". Five sites answered
        /// it separately and PUBLISHED got S4 at all of them — a code
        /// <see cref="CdeStateFor"/> files in SHARED, so every promoted document
        /// contradicted the title block that derives its state from the same code.
        ///   WIP        S0  initial status
        ///   SHARED     S3  suitable for review and comment
        ///   PUBLISHED  A1  authorized and accepted — the generic A code; a project that
        ///                  issues for construction (A2) or manufacture (A3) sets it
        ///   ARCHIVE    AR; SUPERSEDED / WITHDRAWN AB; OBSOLETE AR
        /// Round-trips for every live state: CdeStateFor(DefaultFor(s)) == s.</summary>
        public static string DefaultFor(string cdeState)
        {
            switch ((cdeState ?? "").Trim().ToUpperInvariant())
            {
                case "WIP":        return "S0";
                case "SHARED":     return "S3";
                case "PUBLISHED":  return "A1";
                case "ARCHIVE":    return "AR";
                case "SUPERSEDED":
                case "WITHDRAWN":  return "AB";
                case "OBSOLETE":   return "AR";
                default:           return null;
            }
        }

        /// <summary>The code to record when a document moves to <paramref name="cdeState"/>:
        /// its current code when that code already belongs in the state (an S1 document
        /// shared stays S1), otherwise <see cref="DefaultFor"/>.</summary>
        public static string ForTransition(string currentCode, string cdeState)
        {
            string target = (cdeState ?? "").Trim().ToUpperInvariant();
            string code = ExtractCode(currentCode);
            if (code.Length > 0 && string.Equals(CdeStateFor(code), target, StringComparison.Ordinal))
                return code;
            return DefaultFor(target);
        }

        /// <summary>The ISO meaning of the code — what the description cell should say.
        /// Null when unrecognised, so nothing invents a description for a code that is
        /// not in the standard.</summary>
        public static string DescriptionFor(string suitabilityCode)
        {
            string c = Clean(suitabilityCode);
            return c.Length > 0 && Descriptions.TryGetValue(c, out var d) ? d : null;
        }

        public static bool IsKnown(string suitabilityCode)
            => DescriptionFor(suitabilityCode) != null;

        /// <summary>Descriptions in the standard's own words. Upper case because that
        /// is how a title block prints them, and because a cell that alternates between
        /// "Suitable for information" and "SUITABLE FOR INFORMATION" across a drawing
        /// set looks like two different things.</summary>
        private static readonly Dictionary<string, string> Descriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S0"] = "INITIAL STATUS / WORK IN PROGRESS",
            ["S1"] = "SUITABLE FOR COORDINATION",
            ["S2"] = "SUITABLE FOR INFORMATION",
            ["S3"] = "SUITABLE FOR REVIEW AND COMMENT",
            ["S4"] = "SUITABLE FOR STAGE APPROVAL",
            ["S5"] = "SUITABLE FOR MANUFACTURE / PROCUREMENT",
            ["S6"] = "SUITABLE FOR PIM AUTHORIZATION",
            ["S7"] = "SUITABLE FOR AIM AUTHORIZATION",
            ["A1"] = "AUTHORIZED AND ACCEPTED",
            ["A2"] = "AUTHORIZED FOR CONSTRUCTION",
            ["A3"] = "AUTHORIZED FOR MANUFACTURE",
            ["A4"] = "AUTHORIZED FOR INSTALLATION",
            ["A5"] = "AUTHORIZED FOR ARCHIVE",
            ["B1"] = "PARTIAL SIGN-OFF, WITH COMMENTS",
            ["B2"] = "PARTIAL SIGN-OFF, WITH COMMENTS",
            ["B3"] = "PARTIAL SIGN-OFF, WITH COMMENTS",
            ["B4"] = "PARTIAL SIGN-OFF, WITH COMMENTS",
            ["B5"] = "PARTIAL SIGN-OFF, WITH COMMENTS",
            ["CR"] = "AS CONSTRUCTED RECORD",
            // Retirement codes, in Iso19650Vocabulary since IM-14 but missing here, so a
            // document marked AB or AR had no description and read as an unknown code.
            ["AB"] = "ABANDONED / SUPERSEDED",
            ["AR"] = "ARCHIVE",
        };

        /// <summary>Pull the CODE out of a cell that may hold the code, the description,
        /// or both — "S4", "S4 - FOR APPROVAL", "s4/for approval" all yield "S4".
        ///
        /// Needed because a project fills these by hand and the two facts end up in one
        /// box. Without it, deriving from "S4 - FOR APROVAL" would find no code and
        /// silently leave the CDE state unset.</summary>
        public static string ExtractCode(string raw)
        {
            string s = (raw ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0) return "";

            // The code is a letter followed by a digit, at the start of a token.
            // Letter+digit codes win over the letter-only ones (CR, AB, AR): those
            // are ordinary two-letter words and initials, so "AR" in a note must not
            // outrank an "S3" elsewhere in the same text.
            var tokens = s.Split(new[] { ' ', '-', '/', ',', ':', '\t' },
                                 StringSplitOptions.RemoveEmptyEntries)
                          .Select(Clean).ToList();
            foreach (var t in tokens)
                if (t.Length == 2 && char.IsLetter(t[0]) && char.IsDigit(t[1]) && IsKnown(t)) return t;
            foreach (var t in tokens)
                if (t == "CR" || t == "AB" || t == "AR") return t;
            return "";
        }

        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char ch in raw.Trim().ToUpperInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }
}
