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
                string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                if (string.IsNullOrEmpty(projDir)) return outcome;
                string dir = Path.Combine(projDir, "STING_BIM_MANAGER");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string issuesPath = Path.Combine(dir, "issues.json");
                outcome.Path = issuesPath;

                JArray arr = File.Exists(issuesPath)
                    ? JArray.Parse(File.ReadAllText(issuesPath))
                    : new JArray();

                // Failures from this run, keyed by check name.
                var currentFailures = items
                    .Where(i => i.Severity == LpsSeverity.Fail)
                    .GroupBy(i => i.CheckName ?? "")
                    .ToDictionary(g => g.Key, g => g.First(),
                        StringComparer.OrdinalIgnoreCase);

                // Walk existing issues
                foreach (var t in arr)
                {
                    if (t is not JObject row) continue;
                    string origin = row["Origin"]?.ToString();
                    if (!string.Equals(origin, "LPS_AUTO", StringComparison.OrdinalIgnoreCase)) continue;
                    string checkName = row["RelatedCheck"]?.ToString();
                    string status = row["Status"]?.ToString();

                    if (currentFailures.ContainsKey(checkName ?? ""))
                    {
                        if (string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase))
                        {
                            outcome.Existing++;
                            currentFailures.Remove(checkName); // skip — already raised
                        }
                    }
                    else
                    {
                        // Issue exists for a check that no longer fails — close it.
                        if (string.Equals(status, "OPEN", StringComparison.OrdinalIgnoreCase))
                        {
                            row["Status"]      = "CLOSED";
                            row["DateClosed"]  = DateTime.UtcNow.ToString("yyyy-MM-dd");
                            row["ClosedBy"]    = "STING — LpsCompliance auto-close";
                            outcome.Closed++;
                        }
                    }
                }

                // Append net-new failures
                foreach (var kv in currentFailures)
                {
                    var item = kv.Value;
                    string id = $"LPS-AUTO-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{outcome.Raised + 1}";
                    var newRow = new JObject
                    {
                        ["IssueId"]      = id,
                        ["Title"]        = $"LPS compliance — {item.CheckName}",
                        ["Status"]       = "OPEN",
                        ["Priority"]     = item.Severity == LpsSeverity.Fail ? "HIGH" : "MEDIUM",
                        ["Type"]         = "NCR",
                        ["DateRaised"]   = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ["DateDue"]      = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-dd"),
                        ["Description"]  = item.Message ?? "",
                        ["RaisedBy"]     = "STING — LpsComplianceCheck",
                        ["Origin"]       = "LPS_AUTO",
                        ["RelatedCheck"] = item.CheckName ?? "",
                        ["RelatedStandard"] = "BS EN 62305",
                        ["ElementIds"]   = new JArray(item.ElementIds?.Select(eid => eid.Value.ToString()) ?? Array.Empty<string>())
                    };
                    arr.Add(newRow);
                    outcome.Raised++;
                }

                if (outcome.Raised > 0 || outcome.Closed > 0)
                {
                    File.WriteAllText(issuesPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    StingLog.Info($"LpsAutoIssueRaiser: +{outcome.Raised} raised · -{outcome.Closed} closed · {outcome.Existing} existing → {issuesPath}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsAutoIssueRaiser.RaiseFromFailures", ex);
            }
            return outcome;
        }
    }
}
