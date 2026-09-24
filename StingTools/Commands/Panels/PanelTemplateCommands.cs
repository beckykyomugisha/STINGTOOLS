using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Panels;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    /// <summary>
    /// Creates (or rebuilds in place) the STING standard panel schedule templates
    /// from STING_PANEL_SCHEDULE_SPECS.json + the project override. Each template
    /// is built in its own sub-transaction, so one that Revit rejects does not
    /// undo the others. Every cell written is read back and the report says what
    /// actually persisted.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelTemplatesCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var run = Run(doc);
            if (run == null) { message = "Panel template build failed — see the STING log."; return Result.Failed; }
            Report(run);
            return run.Results.Any(r => !r.Failed) ? Result.Succeeded : Result.Failed;
        }

        public sealed class RunResult
        {
            public List<string> Warnings = new List<string>();
            public List<PanelTemplateBuildResult> Results = new List<PanelTemplateBuildResult>();
        }

        /// <summary>Build every spec'd template. Opens its own transaction.</summary>
        public static RunResult Run(Document doc)
        {
            var run = new RunResult();
            var specs = PanelTemplateBuilder.LoadSpecs(doc, run.Warnings);
            if (specs.Templates.Count == 0) return run;

            using (var tx = new Transaction(doc, "STING Panel Schedule Templates"))
            {
                tx.Start();
                foreach (var spec in specs.Templates)
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        PanelTemplateBuildResult r;
                        try
                        {
                            r = PanelTemplateBuilder.BuildOrUpdate(doc, spec);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Error($"Panel template '{spec.Name}'", ex);
                            r = new PanelTemplateBuildResult { Name = spec.Name, Failed = true, FailReason = ex.Message };
                        }
                        if (r.Failed) st.RollBack(); else st.Commit();
                        run.Results.Add(r);
                    }
                }
                tx.Commit();
            }
            try { PanelScheduleTemplateRegistry.Reload(doc); }
            catch (Exception ex) { StingLog.Warn($"Template registry reload: {ex.Message}"); }
            return run;
        }

        public static void Report(RunResult run)
        {
            var ok = run.Results.Where(r => !r.Failed).ToList();
            var panel = StingResultPanel.Create("STING Panel Schedule Templates");
            panel.SetSubtitle($"{ok.Count(r => r.Created)} created · {ok.Count(r => !r.Created)} rebuilt · {run.Results.Count(r => r.Failed)} failed");

            foreach (var r in run.Results)
            {
                panel.AddSection(r.Name.ToUpperInvariant());
                if (r.Failed) { panel.MetricError("Not built", r.FailReason); continue; }
                panel.Metric("Action", r.Created ? "created" : "rebuilt in place");
                if (r.CellsVerified == r.CellsRequested)
                    panel.MetricHighlight("Cells verified", $"{r.CellsVerified}/{r.CellsRequested}");
                else
                    panel.MetricWarn("Cells verified", $"{r.CellsVerified}/{r.CellsRequested}");
                foreach (var p in r.Problems.Take(12)) panel.Text("⚠ " + p);
                if (r.Problems.Count > 12) panel.Text($"… {r.Problems.Count - 12} more (STING log).");
                foreach (var n in r.Notes.Take(5)) panel.Text("ℹ " + n);
                foreach (var p in r.Problems) StingLog.Warn($"Panel template '{r.Name}': {p}");
            }

            if (run.Warnings.Count > 0)
            {
                panel.AddSection("SPEC");
                foreach (var w in run.Warnings) panel.Text(w);
            }

            panel.AddSection("NEXT STEPS")
                 .Text("PNLS → ⚡ Batch Create Schedules now picks these templates by board type (rules in STING_PANEL_SCHEDULE_TEMPLATES.json).")
                 .Text("Shared-parameter columns need Load Params first; run CALCS → Recalculate All so VD (%) is filled.")
                 .Text("If a section looks wrong, run 'Inspect template' and send the CSV — it shows every cell.");
            panel.Show();
        }
    }

    /// <summary>
    /// Dumps any panel schedule template's cells (section, row, column, type,
    /// text, bound parameter) to CSV — the ground truth for how Revit laid a
    /// template out, which the API does not document.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class PanelTemplateInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var templates = new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleTemplate))
                .Cast<PanelScheduleTemplate>().OrderBy(t => t.Name).ToList();
            if (templates.Count == 0)
            {
                TaskDialog.Show("STING Panel Templates", "This project has no panel schedule templates.");
                return Result.Cancelled;
            }

            string pick = StingTools.Select.StingListPicker.Show("Inspect panel schedule template",
                "Choose a template to dump every cell to CSV:",
                templates.Select(t => $"{t.Name}  [{t.GetPanelScheduleType()}]").ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            var template = templates.FirstOrDefault(t => pick.StartsWith(t.Name + "  [", StringComparison.Ordinal));
            if (template == null) return Result.Cancelled;

            List<string[]> rows;
            try { rows = PanelTemplateBuilder.Dump(doc, template); }
            catch (Exception ex) { message = ex.Message; StingLog.Error("Panel template inspect", ex); return Result.Failed; }

            string path;
            try
            {
                path = StingPaths.ExportFile(doc, "Schedule", "PanelTemplate_" + Safe(template.Name), ".csv");
                var sb = new StringBuilder("Section,Row,Column,CellType,Text,Parameter\n");
                foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Csv)));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("Panel template inspect write", ex); return Result.Failed; }

            var bySection = rows.GroupBy(r => r[0]).Select(g =>
                $"{g.Key}: {g.Select(r => r[1]).Where(x => x != "").Distinct().Count()} rows × " +
                $"{g.Select(r => r[2]).Where(x => x != "").Distinct().Count()} cols, " +
                $"{g.Count(r => r[5] != "")} parameter cell(s)");
            TaskDialog.Show("STING Panel Templates",
                $"'{template.Name}' ({template.GetPanelScheduleType()})\n\n" + string.Join("\n", bySection) +
                $"\n\nEvery cell written to:\n{path}");
            return Result.Succeeded;
        }

        private static string Safe(string s)
            => new string((s ?? "").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
