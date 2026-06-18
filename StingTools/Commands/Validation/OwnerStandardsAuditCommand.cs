using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Validation;

namespace StingTools.Commands.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B2) — Owner Standards audit command.
    //
    // Runs every enabled Owner Standards Pack rule, prints a RAG summary, and
    // writes a CSV + JSON report to _BIM_COORD/owner_standards_reports/.
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OwnerStandardsAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Pick up on-disk overlay edits without restarting Revit.
            OwnerStandardsRegistry.Reload(doc);
            var def = OwnerStandardsRegistry.Get(doc);
            if (def.Rules == null || def.Rules.Count == 0)
            {
                TaskDialog.Show("Owner Standards",
                    "No Owner Standards Pack found.\n\nShip STING_OWNER_STANDARDS_PACK.json in data/ " +
                    "or add a project overlay at <project>/_BIM_COORD/owner_standards.json.");
                return Result.Succeeded;
            }

            var findings = OwnerStandardsEvaluator.Evaluate(doc);

            int blockFail = findings.Count(f => !f.Skipped && f.Severity == "BLOCK" && f.Violations > 0);
            int warnFail = findings.Count(f => !f.Skipped && f.Severity == "WARN" && f.Violations > 0);
            int infoFail = findings.Count(f => !f.Skipped && f.Severity == "INFO" && f.Violations > 0);
            int skipped = findings.Count(f => f.Skipped);
            string rag = blockFail > 0 ? "RED" : (warnFail > 0 ? "AMBER" : "GREEN");

            string csv = WriteCsv(doc, findings);
            string json = WriteJson(doc, findings, rag);

            var sb = new StringBuilder();
            sb.AppendLine($"RAG: {rag}   ({def.Rules.Count(r => r.Enabled)} enabled rules, {skipped} skipped)");
            sb.AppendLine($"BLOCK failing: {blockFail}   WARN failing: {warnFail}   INFO failing: {infoFail}");
            sb.AppendLine();
            foreach (var f in findings.OrderByDescending(x => SevRank(x.Severity)).ThenByDescending(x => x.Violations))
            {
                string state = f.Skipped ? "SKIP" : (f.Violations > 0 ? $"{f.Violations} fail" : "ok");
                sb.AppendLine($"[{f.Severity}] {f.RuleId}: {state}   ({f.Checked} checked)");
                if (!string.IsNullOrEmpty(f.Source)) sb.AppendLine($"     src: {f.Source}");
                if (f.Skipped) sb.AppendLine($"     skipped: {f.SkipReason}");
                else foreach (var s in f.Samples.Take(3)) sb.AppendLine($"       • {s}");
            }
            if (csv != null) { sb.AppendLine(); sb.AppendLine($"CSV: {csv}"); }
            if (json != null) sb.AppendLine($"Report: {json}");

            new TaskDialog("Owner Standards Audit")
            {
                MainInstruction = $"{rag} — {blockFail} BLOCK, {warnFail} WARN, {infoFail} INFO failing",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"OwnerStandards_Audit: {rag} block={blockFail} warn={warnFail} info={infoFail} skip={skipped}");
            return Result.Succeeded;
        }

        private static int SevRank(string s) => s == "BLOCK" ? 3 : s == "WARN" ? 2 : 1;

        private static string WriteCsv(Document doc, List<OwnerStandardFinding> findings)
        {
            try
            {
                var rows = new List<string> { "RuleId,Type,Severity,Checked,Violations,Skipped,Source,Description,Samples" };
                foreach (var f in findings)
                    rows.Add(string.Join(",",
                        Csv(f.RuleId), Csv(f.Type), Csv(f.Severity), f.Checked, f.Violations,
                        f.Skipped ? "YES" : "", Csv(f.Source), Csv(f.Description),
                        Csv(string.Join(" | ", f.Samples))));
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_OwnerStandards_Audit_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"OwnerStandards CSV: {ex.Message}"); return null; }
        }

        private static string WriteJson(Document doc, List<OwnerStandardFinding> findings, string rag)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(dir)) return null;
                string reportDir = Path.Combine(dir, "_BIM_COORD", "owner_standards_reports");
                Directory.CreateDirectory(reportDir);
                var payload = new
                {
                    generatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    rag,
                    findings = findings.Select(f => new
                    {
                        f.RuleId, f.Type, f.Severity, f.Checked, f.Violations, f.Skipped, f.SkipReason,
                        f.Source, f.Description, f.Samples
                    }).ToList()
                };
                string path = Path.Combine(reportDir, $"owner_standards_{DateTime.Now:yyyyMMddHHmmss}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented), Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"OwnerStandards JSON: {ex.Message}"); return null; }
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
