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
        /// Decide the sheets. Order: the step's "sheets" param (comma / semicolon
        /// list of sheet numbers) when given — the step named the sheets, so it
        /// wins over clouds; else clouds + BCC picks; else the STING-stamped
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

            var direct = Distinct((clouded ?? Enumerable.Empty<string>())
                .Concat(picked ?? Enumerable.Empty<string>()));
            if (direct.Count > 0)
            {
                plan.Source = RevisionIssueSource.CloudsOrPicks;
                plan.SheetNumbers = direct;
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
    /// <summary>
    /// Which saved tag snapshot Auto Revision Cloud compares the model against.
    ///
    /// Create Revision saves "pre_rev_&lt;number&gt;" at the moment the revision is made.
    /// Standalone, that is the right baseline: the team then edits the model and
    /// clouds what changed since the revision was opened. In the RevisionIssue preset
    /// Create Revision runs immediately before Auto Revision Cloud, so its snapshot is
    /// identical to the model and every run reported "no changes". With
    /// baseline = "previous" (the preset's step param) the new revision's own
    /// snapshot is skipped and the one before it — the previous revision's baseline —
    /// is used, so the clouds show what changed since the last revision was opened.
    /// Only a snapshot that IS the latest revision's own is skipped; any other latest
    /// snapshot is still the baseline, so "previous" never reaches further back than
    /// it must.
    /// </summary>
    internal static class RevisionSnapshotBaseline
    {
        /// <summary>
        /// Index into <paramref name="fileNamesNewestFirst"/> of the baseline, or -1
        /// when there is none (<paramref name="reason"/> says why).
        /// </summary>
        /// <param name="fileNamesNewestFirst">Snapshot file names (or paths), newest first.</param>
        /// <param name="mode">"latest" (default, "" too) or "previous".</param>
        /// <param name="latestRevisionNumber">RevisionNumber of the newest revision ("" when unknown).</param>
        public static int Pick(IList<string> fileNamesNewestFirst, string mode,
            string latestRevisionNumber, out string reason)
        {
            reason = "";
            var files = fileNamesNewestFirst ?? new List<string>();
            if (files.Count == 0)
            {
                reason = "no tag snapshot has been saved — run Create Revision first to take a baseline";
                return -1;
            }

            string m = (mode ?? "").Trim();
            if (m.Length == 0 || m.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                reason = "latest snapshot (" + LabelOf(files[0]) + ")";
                return 0;
            }
            if (!m.Equals("previous", StringComparison.OrdinalIgnoreCase))
            {
                reason = "unknown baseline \"" + m + "\" — use \"latest\" or \"previous\"";
                return -1;
            }

            string own = string.IsNullOrWhiteSpace(latestRevisionNumber)
                ? null : "pre_rev_" + latestRevisionNumber.Trim();
            bool latestIsOwn = own != null
                && string.Equals(LabelOf(files[0]), own, StringComparison.OrdinalIgnoreCase);
            if (!latestIsOwn)
            {
                reason = "latest snapshot (" + LabelOf(files[0]) + ") — it is not revision "
                    + (latestRevisionNumber ?? "") + "'s own, so nothing is skipped";
                return 0;
            }
            if (files.Count < 2)
            {
                reason = "only revision " + latestRevisionNumber + "'s own snapshot exists — there is no "
                    + "earlier baseline to compare against (first revision of the project)";
                return -1;
            }
            reason = "previous snapshot (" + LabelOf(files[1]) + "), skipping " + LabelOf(files[0])
                + " which Create Revision took for revision " + latestRevisionNumber;
            return 1;
        }

        /// <summary>
        /// The label of a snapshot file: "snapshot_&lt;label&gt;_yyyyMMdd_HHmmss.json" →
        /// "&lt;label&gt;". A name that does not fit the pattern is returned without extension.
        /// </summary>
        public static string LabelOf(string fileNameOrPath)
        {
            if (string.IsNullOrEmpty(fileNameOrPath)) return "";
            string name = fileNameOrPath;
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 5);
            if (!name.StartsWith("snapshot_", StringComparison.OrdinalIgnoreCase)) return name;
            name = name.Substring("snapshot_".Length);
            // Strip the trailing _yyyyMMdd_HHmmss (16 chars incl. both underscores).
            if (name.Length > 16 && name[name.Length - 16] == '_' && name[name.Length - 7] == '_'
                && name.Substring(name.Length - 15, 8).All(char.IsDigit)
                && name.Substring(name.Length - 6).All(char.IsDigit))
                name = name.Substring(0, name.Length - 16);
            return name;
        }
    }
}
