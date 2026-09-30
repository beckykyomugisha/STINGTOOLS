// StingTools — ISO 19650 revision / suitability consistency.
//
// A revision code and a suitability code are two halves of one statement. The UK
// National Annex to ISO 19650-2 pairs them:
//
//   P01, P02 …  preliminary     S0 … S7   (work in progress / shared, not contractual)
//   C01, C02 …  contractual     A1 … An   authorised, B1 … Bn partially authorised,
//                                         CR as-constructed record
//   AB / AR     retired (STING archive codes) — allowed on any revision: a withdrawn
//               or archived document keeps the revision it was retired at.
//
// Nothing checked the pairing, so a drawing could be issued "P03 / A1" (an authorised
// document with a preliminary revision) or "C02 / S2" (a contractual revision shared
// for coordination). Either makes the document's status unreadable to whoever receives
// it. This is the one check; CreateRevision, IssueSheetsForRevision and the ACC upload
// call it. Revit-free, unit-tested.

using System;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public enum RevisionRuleVerdict
    {
        /// <summary>The pair is consistent.</summary>
        Consistent,
        /// <summary>The pair contradicts itself — refuse.</summary>
        Inconsistent,
        /// <summary>Nothing to judge: a blank input, or a revision series this rule does
        /// not govern (T, Co, R, D, status stamps, legacy numerics…).</summary>
        NotApplicable,
    }

    public sealed class RevisionRuleResult
    {
        public RevisionRuleVerdict Verdict { get; set; }
        public string Reason { get; set; } = "";
        public bool IsInconsistent => Verdict == RevisionRuleVerdict.Inconsistent;
        public override string ToString() => $"{Verdict}: {Reason}";
    }

    public static class Iso19650RevisionRules
    {
        /// <summary>Judge a revision code against a suitability code.</summary>
        public static RevisionRuleResult Check(string revisionCode, string suitabilityCode)
        {
            string rev = (revisionCode ?? "").Trim();
            string suitRaw = (suitabilityCode ?? "").Trim();
            if (rev.Length == 0 || suitRaw.Length == 0)
                return Na("revision or suitability is blank — nothing to check");

            string suit = Iso19650Suitability.ExtractCode(suitRaw);
            if (string.IsNullOrEmpty(suit)) suit = suitRaw.ToUpperInvariant();
            string cde = Iso19650Suitability.CdeStateFor(suit);
            if (cde == null)
                return Bad($"'{suitRaw}' is not an ISO 19650 suitability code");

            // Retirement codes stand with any revision.
            if (suit == "AB" || suit == "AR")
                return Ok($"{suit} retires the document at {rev}");

            RevisionSeries.TryParseSeriesPrefix(rev, out string prefix, out _);
            bool isP = string.Equals(prefix, "P", StringComparison.Ordinal);
            bool isC = string.Equals(prefix, "C", StringComparison.Ordinal);
            bool isRecord = string.Equals(prefix, "AB", StringComparison.Ordinal)
                            || (rev.Length == 1 && char.IsLetter(rev[0]));

            if (isP)
            {
                if (cde == "WIP" || cde == "SHARED")
                    return Ok($"preliminary {rev} with {suit}");
                return Bad($"{rev} is a preliminary (P) revision but {suit} is an authorised/record code — " +
                           "an authorised issue takes a contractual C revision (C01 on its first authorisation)");
            }
            if (isC)
            {
                if (cde == "PUBLISHED")
                    return Ok($"contractual {rev} with {suit}");
                return Bad($"{rev} is a contractual (C) revision but {suit} is a work-in-progress/shared code — " +
                           "a document shared for coordination or review stays in the P series");
            }
            if (suit == "CR")
                return isRecord
                    ? Ok($"record {rev} with CR")
                    : Na($"series of {rev} is not governed by the P/C rule");
            if (isRecord)
                return Bad($"{rev} is an as-built record revision; its suitability should be CR (or AB/AR when retired), not {suit}");

            return Na($"series of {rev} is not governed by the P/C rule");
        }

        /// <summary>
        /// For an upload that knows a file name and a suitability but no revision field: the
        /// revision is the LAST token of the name that is a P/C series code
        /// ("KUT-PLN-ZZ-01-DR-A-0001-P03.pdf" → P03). No such token → NotApplicable.
        /// </summary>
        public static RevisionRuleResult CheckFileName(string fileName, string suitabilityCode)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName ?? "") ?? "";
            string rev = null;
            foreach (string t in stem.Split(new[] { '-', '_', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries))
                if (RevisionSeries.TryParseSeriesPrefix(t, out string p, out _) && (p == "P" || p == "C"))
                    rev = t;
            return rev == null
                ? Na("the file name carries no P/C revision code")
                : Check(rev, suitabilityCode);
        }

        /// <summary>True for a P or C series code — the revisions that must be issued at a
        /// suitability for the pair to mean anything.</summary>
        public static bool IsPairedSeries(string revisionCode)
        {
            return RevisionSeries.TryParseSeriesPrefix((revisionCode ?? "").Trim(), out string p, out _)
                   && (p == "P" || p == "C");
        }

        private static RevisionRuleResult Ok(string r) => new RevisionRuleResult { Verdict = RevisionRuleVerdict.Consistent, Reason = r };
        private static RevisionRuleResult Bad(string r) => new RevisionRuleResult { Verdict = RevisionRuleVerdict.Inconsistent, Reason = r };
        private static RevisionRuleResult Na(string r) => new RevisionRuleResult { Verdict = RevisionRuleVerdict.NotApplicable, Reason = r };
    }

    /// <summary>
    /// Whether IssueSheetsForRevision may issue (R6, R7). One Revit-free rule so the
    /// command's gates are tested:
    ///   • no target sheet → never issued (it locked the revision against nothing, burned
    ///     a number and proposed every open issue as RESPONDED);
    ///   • an inconsistent revision/suitability pair → refused;
    ///   • inside a workflow preset, a P/C revision with no suitability → refused: the title
    ///     block, the export file name and the ACC upload would pair the new revision with
    ///     the previous issue's suitability.
    /// </summary>
    public static class RevisionIssueGate
    {
        public static bool MayIssue(string revisionCode, string suitabilityCode, int targetSheets,
            bool inPreset, out string reason)
        {
            string rev = string.IsNullOrWhiteSpace(revisionCode) ? "(unnumbered)" : revisionCode.Trim();
            if (targetSheets <= 0)
            {
                reason = $"Revision {rev} was NOT issued: no sheet carries a cloud for it and none was picked. " +
                         "Cloud the changes (or pick the sheets in the Coordination Center) and issue again.";
                return false;
            }
            var pairing = Iso19650RevisionRules.Check(revisionCode, suitabilityCode);
            if (pairing.IsInconsistent)
            {
                reason = $"Revision {rev} was NOT issued: {pairing.Reason}. " +
                         "Correct the suitability (or the revision series) and issue again.";
                return false;
            }
            if (inPreset && string.IsNullOrWhiteSpace(suitabilityCode) && Iso19650RevisionRules.IsPairedSeries(revisionCode))
            {
                reason = $"Revision {rev} was NOT issued: no suitability to issue it at. Set the revision's " +
                         "'Issued to' field (Revit > Sheet Issues/Revisions) to the suitability code, or give " +
                         "the clouded sheets one suitability (PRJ_DWG_SUITABILITY_COD_TXT), and run again.";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>The one suitability the target sheets agree on, or "" when they carry none
        /// or disagree. A recorded fact, never a default.</summary>
        public static string AgreedSuitability(System.Collections.Generic.IEnumerable<string> sheetCodes)
        {
            string found = null;
            foreach (var raw in sheetCodes ?? System.Linq.Enumerable.Empty<string>())
            {
                string c = (raw ?? "").Trim();
                if (c.Length == 0) continue;
                if (found == null) found = c;
                else if (!string.Equals(found, c, StringComparison.OrdinalIgnoreCase)) return "";
            }
            return found ?? "";
        }
    }
}
