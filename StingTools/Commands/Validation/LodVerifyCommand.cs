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
        /// <summary>
        /// What the run actually looked at — carried alongside the result so every
        /// output form can DISCLOSE its scope. A gate that quietly narrows and then
        /// reports a confident percentage manufactures false confidence, which is
        /// the failure the gate exists to prevent.
        /// </summary>
        public class LodScopeReport
        {
            public string Label = "";
            public int InScope;
            public int ExcludedCount;
            public bool StarRuleAvailable;
            /// <summary>Categories in scope with no explicit rule — judged by "*".</summary>
            public List<string> CategoriesUsingStarRule = new List<string>();
            /// <summary>Model categories present in the document but NOT scanned.</summary>
            public Dictionary<string, int> ExcludedByCategory
                = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> DisclosureLines()
            {
                yield return $"Scope: {Label} — {InScope} element(s) verified.";
                if (CategoriesUsingStarRule.Count > 0)
                    yield return $"Judged by the \"*\" default rule (no category rule of their own): " +
                                 string.Join(", ", CategoriesUsingStarRule);
                if (!StarRuleAvailable)
                    yield return "NOT SCANNED: the matrix defines no \"*\" default rule, so only categories " +
                                 "with an explicit rule were collected.";
                if (ExcludedByCategory.Count > 0)
                {
                    yield return $"NOT SCANNED — {ExcludedCount} element(s) in {ExcludedByCategory.Count} " +
                                 "model category/ies carrying no LOD rule:";
                    foreach (var kv in ExcludedByCategory.OrderByDescending(k => k.Value))
                        yield return $"   {kv.Value,6}  {kv.Key}";
                }
                else yield return "NOT SCANNED: none — every model category present was in scope.";
            }
        }

        // Model-type categories STING never assigns BUILD MATURITY to: non-physical,
        // derived, reference or analytical content. A geometry/parameter maturity check
        // on these is meaningless rather than merely strict. They are excluded from
        // project scope and DISCLOSED in the report, never silently dropped.
        private static readonly HashSet<string> NonPhysicalCategories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Materials", "Profiles", "RVT Links", "Model Groups", "Assemblies", "Detail Items",
            "Mass", "Parts", "Areas", "Zones", "Property Lines", "Property Line Segments",
            "Toposolid Links",
            "Analytical Members", "Analytical Nodes", "Analytical Links", "Analytical Openings",
            "Analytical Panels", "Analytical Duct Segments", "Analytical Pipe Segments",
            "Area Based Loads", "Area Loads", "Line Loads", "Point Loads",
            "Internal Area Loads", "Internal Line Loads", "Internal Point Loads",
        };

        /// <summary>
        /// Selection-else-project scope. Selection is taken verbatim.
        ///
        /// <para>Project scope previously collected ONLY the matrix's explicitly named
        /// categories (<see cref="LodVerificationEngine.ExplicitCategories"/> filters out
        /// "*"), so the "*" fallback — the rule the matrix defines precisely so nothing
        /// escapes — never applied outside a manual selection. Roofs, Ceilings, Stairs,
        /// Railings, Ramps, Furniture and Furniture Systems were never scanned and the
        /// result did not say so.</para>
        ///
        /// <para><see cref="LodVerificationEngine.Resolve"/> already falls through to the
        /// "*" rule for an unmatched category, so widening the collector is sufficient —
        /// no engine change. When the matrix defines no "*" rule, the explicit-category
        /// filter is kept (widening would collect elements nothing can judge).</para>
        ///
        /// <para>Blast radius is constrained three ways: <c>CategoryType.Model</c> (drops
        /// annotation and view content), membership of <see cref="TagConfig.DiscMap"/> (the
        /// same taggable-category definition the tagging pipeline uses, so LOD scope and
        /// tag scope agree), and <see cref="NonPhysicalCategories"/>. The project's own
        /// <see cref="TagConfig.CategorySkipList"/> is honoured too. A category with an
        /// explicit rule is ALWAYS collected, whatever those filters say.</para>
        /// </summary>
        public static List<Element> Collect(UIDocument uidoc, Document doc, out LodScopeReport report)
        {
            report = new LodScopeReport();

            var selIds = uidoc?.Selection?.GetElementIds();
            if (selIds != null && selIds.Count > 0)
            {
                report.Label = $"selection ({selIds.Count})";
                report.StarRuleAvailable = true;
                var sel = selIds.Select(doc.GetElement).Where(e => e != null && e.Category != null).ToList();
                report.InScope = sel.Count;
                return sel;
            }

            report.Label = "project";
            var matrix = LodMatrixRegistry.Get(doc);
            report.StarRuleAvailable =
                (matrix.CategoryRules ?? new List<LodCategoryRule>()).Any(r => r.Category == "*");

            var explicitCats = new HashSet<string>(
                LodVerificationEngine.ExplicitCategories(doc), StringComparer.OrdinalIgnoreCase);
            var taggable = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);

            var scope = new List<Element>();
            var starCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category cat = null;
                try { cat = e.Category; }
                catch (Exception ex) { StingLog.Warn($"LodScope category read {e?.Id}: {ex.Message}"); }
                if (cat == null || cat.CategoryType != CategoryType.Model) continue;

                string name = ParameterHelpers.GetCategoryName(e) ?? "";
                if (name.Length == 0) continue;

                bool hasExplicit = explicitCats.Contains(name);
                bool eligible = hasExplicit ||
                                (report.StarRuleAvailable
                                 && taggable.Contains(name)
                                 && !NonPhysicalCategories.Contains(name)
                                 && !TagConfig.CategorySkipList.Contains(name));

                if (!eligible)
                {
                    report.ExcludedByCategory.TryGetValue(name, out int c);
                    report.ExcludedByCategory[name] = c + 1;
                    continue;
                }
                if (!hasExplicit) starCats.Add(name);
                scope.Add(e);
            }

            report.CategoriesUsingStarRule = starCats.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            report.InScope = scope.Count;
            report.ExcludedCount = report.ExcludedByCategory.Values.Sum();
            StingLog.Info($"LodScope: {report.InScope} in scope, {report.ExcludedCount} excluded across " +
                          $"{report.ExcludedByCategory.Count} category/ies, " +
                          $"{report.CategoriesUsingStarRule.Count} category/ies on the \"*\" rule");
            return scope;
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

        public static StringBuilder BuildReport(LodVerificationResult r, LodScopeReport scope)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Milestone: {r.MilestoneName}  (LOD {r.Lod})");
            sb.AppendLine($"PASS {r.Passed} / {r.Total}  ({r.OverallPct:F1}%)   FAIL {r.Failed}");
            sb.AppendLine();
            foreach (var line in scope.DisclosureLines()) sb.AppendLine(line);
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

        public static string WriteCsv(Document doc, LodVerificationResult r, LodScopeReport scope)
        {
            try
            {
                // Scope disclosure leads the file as '#' comments so a reader opening the
                // CSV alone still learns what was NOT scanned. Excel tolerates them and
                // StingToolsApp.ParseCsvLine callers already skip '#'.
                var rows = new List<string>
                {
                    $"# STING LOD audit — {r.MilestoneName} (LOD {r.Lod})",
                    $"# PASS {r.Passed}/{r.Total} ({r.OverallPct:F1}%)",
                };
                foreach (var line in scope.DisclosureLines()) rows.Add("# " + line);
                rows.Add("# Parameter/naming/geometry-presence maturity proxy — not a geometric survey.");
                rows.Add("ElementId,Category,Discipline,Pass,Reasons");
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

        public static string WriteGateReport(Document doc, LodVerificationResult r, string stamp, LodScopeReport scope)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(dir)) return null;
                string reportDir = StingPaths.MetaFile(doc, "_BIM_COORD", "lod_reports");
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
                    // Scope disclosure — what the gate looked at, and what it did NOT.
                    // A gate report that omits this reads as full coverage when it is not.
                    scope = new
                    {
                        label = scope.Label,
                        inScope = scope.InScope,
                        starRuleAvailable = scope.StarRuleAvailable,
                        categoriesUsingStarRule = scope.CategoriesUsingStarRule,
                        notScannedElementCount = scope.ExcludedCount,
                        notScannedByCategory = scope.ExcludedByCategory
                            .OrderByDescending(k => k.Value).ToDictionary(k => k.Key, v => v.Value),
                        disclosure = scope.DisclosureLines().ToList()
                    },
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

            var scope = LodScope.Collect(ctx.UIDoc, doc, out var scopeReport);
            var r = LodVerificationEngine.Verify(doc, ms.Id, scope);

            string csvPath = LodScope.WriteCsv(doc, r, scopeReport);
            string gatePath = LodScope.WriteGateReport(doc, r, DateTime.UtcNow.ToString("yyyyMMddHHmmss"), scopeReport);

            var report = LodScope.BuildReport(r, scopeReport);
            if (csvPath != null) { report.AppendLine(); report.AppendLine($"CSV: {csvPath}"); }
            if (gatePath != null) report.AppendLine($"Gate report: {gatePath}");

            new TaskDialog("LOD Verify")
            {
                MainInstruction = $"{r.MilestoneName}: {r.OverallPct:F1}% mature ({r.Passed}/{r.Total})",
                MainContent = report.ToString()
            }.Show();
            StingLog.Info($"LOD_Verify: {r.MilestoneId} {r.Passed}/{r.Total} pass ({scopeReport.Label}), " +
                          $"{scopeReport.ExcludedCount} element(s) not scanned");
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

            var scope = LodScope.Collect(ctx.UIDoc, doc, out var scopeReport);
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
                MainContent = $"Milestone: {ms.Name} (LOD {ms.Lod})\n" +
                              string.Join("\n", scopeReport.DisclosureLines()) + "\n" +
                              $"Passed: {r.Passed}/{r.Total}\nStamped: {stamped}\nLocked/skipped: {locked}\n\n" +
                              $"ASS_LOD_VERIFIED_TXT now records the highest milestone each element has passed."
            }.Show();
            StingLog.Info($"LOD_Stamp: {stamped} stamped '{ms.Id}', {locked} locked ({scopeReport.Label})");
            return Result.Succeeded;
        }
    }
}
