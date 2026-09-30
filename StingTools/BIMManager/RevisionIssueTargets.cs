// StingTools — which sheets a revision issue lands on (Revit-free)
//
// Issue Sheets for Revision used to add the revision to the sheets that carry a
// cloud for it plus the sheets ticked in the BCC form, and then mark the revision
// Issued whatever that came to. A first issue of freshly produced STING sheets has
// no clouds, so it issued nothing and locked the revision (Revit refuses new clouds
// on an Issued revision). This decides the target set once, and says when there is
// nothing to issue, so the command can stop before it locks anything.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.BIMManager
{
    internal enum RevisionIssueSource
    {
        /// <summary>Sheets with a cloud for the revision and/or ticked in the BCC form.</summary>
        CloudsOrPicks,
        /// <summary>The sheet numbers a workflow step named in its "sheets" param.</summary>
        StepParam,
        /// <summary>Sheets stamped with a STING drawing type (produced by STING).</summary>
        StampedSheets,
        /// <summary>Nothing to issue — the revision must not be marked Issued.</summary>
        None,
    }

    internal sealed class RevisionIssuePlan
    {
        public RevisionIssueSource Source { get; set; }
        /// <summary>Sheet numbers to add the revision to (distinct, case-insensitive).</summary>
        public List<string> SheetNumbers { get; set; } = new List<string>();
        /// <summary>Why the plan is empty, or which fallback it used. Never null.</summary>
        public string Reason { get; set; } = "";
        /// <summary>Sheet numbers named in the step param that the model does not have.</summary>
        public List<string> Unknown { get; set; } = new List<string>();
        /// <summary>
        /// True when the stamped-sheet fallback needs a person's yes before it runs
        /// (outside a preset). The command asks; inside a preset it does not.
        /// </summary>
        public bool NeedsConfirmation { get; set; }
        public bool IsEmpty => SheetNumbers.Count == 0;
    }

    internal static class RevisionIssueTargets
    {
        /// <summary>
        /// Decide the sheets. Order: clouds + BCC picks; else the step's "sheets"
        /// param (comma / semicolon list of sheet numbers); else the STING-stamped
        /// sheets (confirmed by a person outside a preset). Empty = issue nothing.
        /// </summary>
        /// <param name="clouded">Sheet numbers carrying a cloud for the revision.</param>
        /// <param name="picked">Sheet numbers ticked in the BCC form that exist in the model.</param>
        /// <param name="stepParamSheets">The step's raw "sheets" param ("" when absent).</param>
        /// <param name="stamped">Sheet numbers stamped with a STING drawing type.</param>
        /// <param name="allSheets">Every sheet number in the model.</param>
        /// <param name="quiet">True inside a workflow preset.</param>
        public static RevisionIssuePlan Plan(
            IEnumerable<string> clouded,
            IEnumerable<string> picked,
            string stepParamSheets,
            IEnumerable<string> stamped,
            IEnumerable<string> allSheets,
            bool quiet)
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            var plan = new RevisionIssuePlan();

            var direct = Distinct((clouded ?? Enumerable.Empty<string>())
                .Concat(picked ?? Enumerable.Empty<string>()));
            if (direct.Count > 0)
            {
                plan.Source = RevisionIssueSource.CloudsOrPicks;
                plan.SheetNumbers = direct;
                return plan;
            }

            var all = new HashSet<string>((allSheets ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), cmp);

            if (!string.IsNullOrWhiteSpace(stepParamSheets))
            {
                var named = Distinct(stepParamSheets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                plan.Unknown = named.Where(n => !all.Contains(n)).ToList();
                plan.SheetNumbers = named.Where(n => all.Contains(n)).ToList();
                plan.Source = plan.SheetNumbers.Count > 0 ? RevisionIssueSource.StepParam : RevisionIssueSource.None;
                plan.Reason = plan.SheetNumbers.Count > 0
                    ? $"{plan.SheetNumbers.Count} sheet(s) named by the step's \"sheets\" param"
                    : "none of the sheets named by the step's \"sheets\" param exist in the model ("
                      + string.Join(", ", plan.Unknown) + ")";
                // A param that names sheets is an instruction; it is never widened
                // to "every stamped sheet" when it names the wrong ones.
                return plan;
            }

            var stampedList = Distinct((stamped ?? Enumerable.Empty<string>()).Where(s => all.Count == 0 || all.Contains(s?.Trim() ?? "")));
            if (stampedList.Count > 0)
            {
                plan.Source = RevisionIssueSource.StampedSheets;
                plan.SheetNumbers = stampedList;
                plan.NeedsConfirmation = !quiet;
                plan.Reason = $"no sheet carries a cloud for this revision and none was picked; "
                    + $"{stampedList.Count} STING-produced sheet(s) found";
                return plan;
            }

            plan.Source = RevisionIssueSource.None;
            plan.Reason = "no sheet carries a cloud for this revision, none was picked in the "
                + "Issue Sheets form, no \"sheets\" param was given and no sheet carries a STING "
                + "drawing-type stamp — nothing to issue, so the revision was left un-issued";
            return plan;
        }

        private static List<string> Distinct(IEnumerable<string> items)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var raw in items)
            {
                var s = raw?.Trim();
                if (string.IsNullOrEmpty(s) || !seen.Add(s)) continue;
                list.Add(s);
            }
            return list;
        }
    }
}
