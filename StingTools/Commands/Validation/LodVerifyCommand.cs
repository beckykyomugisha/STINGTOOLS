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
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B1) — LOD verification commands.
    //
    // LOD_Verify  (ReadOnly): milestone picker → LodVerificationEngine → summary
    //             TaskDialog + CSV + JSON gate report under _BIM_COORD/lod_reports/.
    //             The gate report is the artefact that goes in front of the Owner
    //             alongside the drawings at each deliverable gate.
    // LOD_Stamp   (Manual):   write the verified milestone id into ASS_LOD_VERIFIED_TXT
    //             on every passing element.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class LodScope
    {
        /// <summary>Selection-else-project scope of elements that carry a LOD
        /// matrix rule (explicit category). Selection is taken verbatim so the
        /// "*" default rule applies to anything the user picked.</summary>
        public static List<Element> Collect(UIDocument uidoc, Document doc, out string label)
        {
            var selIds = uidoc?.Selection?.GetElementIds();
            if (selIds != null && selIds.Count > 0)
            {
                label = $"selection ({selIds.Count})";
                return selIds.Select(doc.GetElement).Where(e => e != null && e.Category != null).ToList();
            }

            label = "project";
            var cats = new HashSet<string>(LodVerificationEngine.ExplicitCategories(doc), StringComparer.OrdinalIgnoreCase);
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && cats.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();
        }

        public static LodMilestone PickMilestone(Document doc, string action)
        {
            var matrix = LodMatrixRegistry.Get(doc);
            var milestones = matrix.Milestones ?? new List<LodMilestone>();
            if (milestones.Count == 0) return null;
            var labels = milestones.Select(m => $"{m.Name}  (LOD {m.Lod})").ToList();
            string pick = StingListPicker.Show($"LOD {action} — pick milestone",
                "Verification is a parameter/naming/geometry-presence maturity proxy, not a geometric survey.",
                labels);
            if (string.IsNullOrEmpty(pick)) return null;
            int idx = labels.IndexOf(pick);
            return idx >= 0 ? milestones[idx] : null;
        }

        public static StringBuilder BuildReport(LodVerificationResult r, string scopeLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Milestone: {r.MilestoneName}  (LOD {r.Lod})");
            sb.AppendLine($"Scope: {scopeLabel} — {r.Total} element(s) in matrix categories");
            sb.AppendLine($"PASS {r.Passed} / {r.Total}  ({r.OverallPct:F1}%)   FAIL {r.Failed}");
            sb.AppendLine();
            sb.AppendLine("Note: parameter / naming / geometry-presence maturity proxy —");
            sb.AppendLine("not a geometric survey. STING does not verify dimensional accuracy.");
            if (r.ClashCheckRequestedButNotVerifiable)
                sb.AppendLine("Note: a rule requested clash verification — not API-verifiable here; run Navisworks/clash kernel separately.");
            sb.AppendLine();

            if (r.ByDiscipline.Count > 0)
            {
                sb.AppendLine("By discipline:");
                foreach (var kv in r.ByDiscipline.OrderBy(k => k.Key))
                    sb.AppendLine($"   {kv.Key,-12} {kv.Value.pass}/{kv.Value.total}  ({Pct(kv.Value)}%)");
                sb.AppendLine();
            }
            sb.AppendLine("By category (worst first):");
            foreach (var kv in r.ByCategory.OrderBy(k => Pct(k.Value)).Take(15))
                sb.AppendLine($"   {Pct(kv.Value),5}%  {kv.Value.pass}/{kv.Value.total}  {kv.Key}");

            var fails = r.Elements.Where(e => !e.Pass).Take(10).ToList();
            if (fails.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("First failures:");
                foreach (var f in fails)
                    sb.AppendLine($"   {f.ElementId} [{f.Category}]: {string.Join("; ", f.Reasons)}");
            }
            return sb;
        }

        private static double Pct((int total, int pass) v) =>
            v.total > 0 ? Math.Round(100.0 * v.pass / v.total, 1) : 100.0;

        public static string WriteCsv(Document doc, LodVerificationResult r)
        {
            try
            {
                var rows = new List<string> { "ElementId,Category,Discipline,Pass,Reasons" };
                foreach (var e in r.Elements)
                    rows.Add(string.Join(",",
                        e.ElementId.Value,
                        Csv(e.Category), Csv(e.Discipline),
                        e.Pass ? "PASS" : "FAIL",
                        Csv(string.Join("; ", e.Reasons))));
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_LOD_{r.MilestoneId}_Audit.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"LOD CSV write: {ex.Message}"); return null; }
        }

        public static string WriteGateReport(Document doc, LodVerificationResult r, string stamp)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(dir)) return null;
                string reportDir = Path.Combine(dir, "_BIM_COORD", "lod_reports");
                Directory.CreateDirectory(reportDir);
                var payload = new
                {
                    milestoneId = r.MilestoneId,
                    milestoneName = r.MilestoneName,
                    lod = r.Lod,
                    generatedUtc = stamp,
                    total = r.Total,
                    passed = r.Passed,
                    failed = r.Failed,
                    overallPct = Math.Round(r.OverallPct, 2),
                    note = "Parameter/naming/geometry-presence maturity proxy, not a geometric survey.",
                    byDiscipline = r.ByDiscipline.ToDictionary(k => k.Key, v => new { v.Value.total, v.Value.pass }),
                    byCategory = r.ByCategory.ToDictionary(k => k.Key, v => new { v.Value.total, v.Value.pass }),
                    failures = r.Elements.Where(e => !e.Pass)
                        .Select(e => new { id = e.ElementId.Value, e.Category, e.Discipline, reasons = e.Reasons })
                        .ToList()
                };
                string fileStamp = stamp.Replace(":", "").Replace("-", "").Substring(0, 8);
                string path = Path.Combine(reportDir, $"{r.MilestoneId}_{fileStamp}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented), Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"LOD gate report write: {ex.Message}"); return null; }
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LodVerifyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var matrix = LodMatrixRegistry.Get(doc);
            if (matrix.Milestones == null || matrix.Milestones.Count == 0)
            {
                TaskDialog.Show("LOD Verify",
                    "No LOD matrix found.\n\nShip STING_LOD_MATRIX.json in data/ or add a project " +
                    "overlay at <project>/_BIM_COORD/lod_matrix.json.");
                return Result.Succeeded;
            }

            var ms = LodScope.PickMilestone(doc, "Verify");
            if (ms == null) return Result.Cancelled;

            var scope = LodScope.Collect(ctx.UIDoc, doc, out string scopeLabel);
            var r = LodVerificationEngine.Verify(doc, ms.Id, scope);

            string stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string csvPath = LodScope.WriteCsv(doc, r);
            string gatePath = LodScope.WriteGateReport(doc, r, DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

            var report = LodScope.BuildReport(r, scopeLabel);
            if (csvPath != null) { report.AppendLine(); report.AppendLine($"CSV: {csvPath}"); }
            if (gatePath != null) report.AppendLine($"Gate report: {gatePath}");

            new TaskDialog("LOD Verify")
            {
                MainInstruction = $"{r.MilestoneName}: {r.OverallPct:F1}% mature ({r.Passed}/{r.Total})",
                MainContent = report.ToString()
            }.Show();
            StingLog.Info($"LOD_Verify: {r.MilestoneId} {r.Passed}/{r.Total} pass ({scopeLabel})");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LodStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var matrix = LodMatrixRegistry.Get(doc);
            if (matrix.Milestones == null || matrix.Milestones.Count == 0)
            {
                TaskDialog.Show("LOD Stamp", "No LOD matrix found.");
                return Result.Succeeded;
            }

            var ms = LodScope.PickMilestone(doc, "Stamp");
            if (ms == null) return Result.Cancelled;

            var scope = LodScope.Collect(ctx.UIDoc, doc, out string scopeLabel);
            var r = LodVerificationEngine.Verify(doc, ms.Id, scope);

            var passIds = new HashSet<long>(r.Elements.Where(e => e.Pass).Select(e => e.ElementId.Value));
            int stamped = 0, locked = 0;
            using (var t = new Transaction(doc, "STING LOD Stamp"))
            {
                t.Start();
                foreach (var el in scope)
                {
                    if (!passIds.Contains(el.Id.Value)) continue;
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) { locked++; continue; }
                    if (ParameterHelpers.SetString(el, ParamRegistry.LOD_VERIFIED, ms.Id, overwrite: true))
                        stamped++;
                }
                t.Commit();
            }

            new TaskDialog("LOD Stamp")
            {
                MainInstruction = $"Stamped {stamped} passing element(s) with '{ms.Id}'",
                MainContent = $"Milestone: {ms.Name} (LOD {ms.Lod})\nScope: {scopeLabel}\n" +
                              $"Passed: {r.Passed}/{r.Total}\nStamped: {stamped}\nLocked/skipped: {locked}\n\n" +
                              $"ASS_LOD_VERIFIED_TXT now records the highest milestone each element has passed."
            }.Show();
            StingLog.Info($"LOD_Stamp: {stamped} stamped '{ms.Id}', {locked} locked ({scopeLabel})");
            return Result.Succeeded;
        }
    }
}
