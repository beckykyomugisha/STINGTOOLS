// LpsAutoIssueRaiser.cs — Wave D #16.
//
// When LpsComplianceCheckCommand finds Severity.Fail items, this
// helper auto-raises one BIM issue per failure to the existing
// STING_BIM_MANAGER/issues.json store. Matches the BimIssueEngine
// IssueRow shape so the existing dashboard + SLA tracking pick them
// up without any further wiring.
//
// Idempotent: scans existing OPEN issues for ones with the LPS_AUTO
// origin tag + matching check name; appends only net-new failures.
// Re-running the compliance check after a fix CLOSES the issue
// automatically.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public static class LpsAutoIssueRaiser
    {
        public class Outcome
        {
            public int Raised   { get; set; }   // new issues created this run
            public int Closed   { get; set; }   // existing issues marked CLOSED (failure resolved)
            public int Existing { get; set; }   // open issues for the same failure (already raised)
            public string Path  { get; set; } = "";
        }

        /// <summary>
        /// Compare the latest compliance-check items to the existing
        /// issues.json. Append new failure items; close ones whose
        /// matching check is no longer failing.
        /// </summary>
        public static Outcome RaiseFromFailures(Document doc, IReadOnlyList<LpsComplianceItem> items)
        {
            var outcome = new Outcome();
            if (doc == null || items == null) return outcome;

            try
            {
                // Phase 2 (IM-4): this was the ONLY writer emitting a PascalCase record —
                // "IssueId"/"Title"/"Status" — so an LPS issue was invisible to every reader
                // that looked up "issue_id" or "id", and its status never reached the
                // has_open_issues gate. The whole record now goes through IssueStore, which
                // owns the canonical schema. Existing PascalCase rows on disk are upgraded
                // in place by IssueSchema.Migrate the first time the store is loaded, so
                // history is preserved rather than orphaned.
                using var batch = IssueStore.Begin(doc);
                if (!batch.Ok)
                {
                    StingLog.Warn("LpsAutoIssueRaiser: issue register unreadable — skipped.");
                    return outcome;
                }
                outcome.Path = IssueStore.PathFor(doc);

                // Failures from this run, keyed by check name.
                var currentFailures = items
                    .Where(i => i.Severity == LpsSeverity.Fail)
                    .GroupBy(i => i.CheckName ?? "")
                    .ToDictionary(g => g.Key, g => g.First(),
                        StringComparer.OrdinalIgnoreCase);

                // Walk existing LPS-raised issues. Reads go through IssueSchema so both the
                // migrated snake_case rows and any not-yet-migrated PascalCase ones match.
                foreach (var t in batch.Rows)
                {
                    if (t is not JObject row) continue;
                    string origin = (row["origin"] ?? row["Origin"])?.ToString();
                    if (!string.Equals(origin, "LPS_AUTO", StringComparison.OrdinalIgnoreCase)) continue;

                    string checkName = (row["related_check"] ?? row["RelatedCheck"])?.ToString();
                    bool isOpen = IssueSchema.IsOpen(row);

                    if (currentFailures.ContainsKey(checkName ?? ""))
                    {
                        if (isOpen)
                        {
                            outcome.Existing++;
                            currentFailures.Remove(checkName); // skip — already raised
                        }
                    }
                    else if (isOpen)
                    {
                        // Issue exists for a check that no longer fails — close it.
                        if (batch.SetStatus(IssueSchema.IdOf(row), "CLOSED",
                                            note: "LpsCompliance auto-close — check no longer failing"))
                            outcome.Closed++;
                    }
                }

                // Append net-new failures
                foreach (var kv in currentFailures)
                {
                    var item = kv.Value;
                    int before = batch.Created.Count;
                    var row = batch.Create(new IssueSpec
                    {
                        Type        = "NCR",
                        Priority    = item.Severity == LpsSeverity.Fail ? "HIGH" : "MEDIUM",
                        Title       = $"LPS compliance — {item.CheckName}",
                        Description = item.Message ?? "",
                        Source      = IssueSource.Lps,
                        // One issue per failing check, so re-running the compliance check
                        // does not raise a second copy. Replaces the old timestamp-based id,
                        // which made every run's issues unique and therefore un-dedupable.
                        SourceHash  = $"lps:{item.CheckName}",
                        ElementIds  = item.ElementIds?.Select(eid => eid.Value).ToList() ?? new List<long>(),
                        Extra       = new JObject
                        {
                            ["origin"]           = "LPS_AUTO",
                            ["related_check"]    = item.CheckName ?? "",
                            ["related_standard"] = "BS EN 62305",
                            ["raised_by"]        = "STING — LpsComplianceCheck",
                        },
                    });
                    // Create returns the EXISTING row when dedup fired, so count the batch's
                    // creations rather than the return value being non-null.
                    if (row != null && batch.Created.Count > before) outcome.Raised++;
                    else if (row != null) outcome.Existing++;
                }

                batch.Commit();
                if (outcome.Raised > 0 || outcome.Closed > 0)
                    StingLog.Info($"LpsAutoIssueRaiser: +{outcome.Raised} raised · -{outcome.Closed} closed · " +
                                  $"{outcome.Existing} existing → {outcome.Path}");
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsAutoIssueRaiser.RaiseFromFailures", ex);
            }
            return outcome;
        }
    }
}
