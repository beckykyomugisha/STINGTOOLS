using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    /// <summary>
    /// Batch-creates one PanelScheduleView per electrical panel using the
    /// rule-based picker in <see cref="PanelScheduleTemplateRegistry"/>.
    /// Skips panels that already have a schedule and panels matching the
    /// configured skip patterns. After successful creation, populates the
    /// panel-side STING tag containers (ELC_PNL_NAME / VOLTAGE / LOAD /
    /// FED_FROM / MAIN_BRK / WAYS), stamps the schedule view with the
    /// elec-panel-schedule-A3 Drawing Type id, and writes
    /// ELC_PANEL_SCHEDULE_REF_TXT on every circuit feeding the panel so
    /// circuit tags can render the back-reference.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPanelSchedulesCommand : IExternalCommand
    {
        private const string DrawingTypeId = "elec-panel-schedule-A3";

        // Phase (dialog→engine) — thin UI wrapper over PanelScheduleApplyEngine (the
        // single source of panel-schedule truth, dialog-free). Button behaviour is
        // unchanged: project-scope batch create, then render the SAME StingResultPanel
        // built FROM the engine result.
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // One-click path: if none of the STING standard templates exist yet, offer
            // to build them first, so the rules pick STING layouts rather than falling
            // back to whatever template happens to be first in the project.
            PanelTemplatesCreateCommand.RunResult templateRun = null;
            string templateError = null;
            try
            {
                var warn = new List<string>();
                var specNames = StingTools.Core.Panels.PanelTemplateBuilder.LoadSpecs(doc, warn)
                    .Templates.Select(t => t.Name).ToList();
                var missing = specNames.Where(n => StingTools.Core.Panels.PanelTemplateBuilder.FindTemplate(doc, n) == null).ToList();
                if (specNames.Count > 0 && missing.Count == specNames.Count)
                {
                    var ask = TaskDialog.Show("STING Panel Schedules",
                        "This project has none of the STING standard panel schedule templates " +
                        "(ISO 19650 header + BS 7671 circuit table).\n\nCreate them now, then build the schedules?\n\n" +
                        "Choose No to use the project's existing templates.",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.Yes);
                    if (ask == TaskDialogResult.Yes)
                    {
                        try { templateRun = PanelTemplatesCreateCommand.Run(doc); }
                        catch (Exception ex)
                        {
                            // Shown in the result panel: the schedules below then use the
                            // project's own templates, and the user must know why.
                            templateError = ex.Message;
                            StingLog.Error("STING template creation (batch path)", ex);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"STING template pre-check: {ex.Message}"); }

            StingTools.Core.Panels.PanelScheduleApplyResult applied;
            try
            {
                applied = StingTools.Core.Panels.PanelScheduleApplyEngine.Apply(
                    doc, StingTools.Core.Panels.PanelScheduleScope.Project(), dryRun: false);
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }

            if (applied.Inspected == 0)
            {
                TaskDialog.Show("STING Panel Schedules", "No electrical equipment found.");
                return Result.Succeeded;
            }

            var result = StingResultPanel.Create("Batch Panel Schedules");
            result.SetSubtitle($"{applied.Created} created · {applied.SkippedExisting} existing · {applied.SkippedPattern} skipped · {applied.Failed + applied.NoTemplate} failed");
            result.AddSection("SUMMARY")
                  .Metric("Panels found", applied.Inspected.ToString())
                  .MetricHighlight("Schedules created", applied.Created.ToString())
                  .Metric("Already had schedule", applied.SkippedExisting.ToString())
                  .Metric("Skipped (pattern)", applied.SkippedPattern.ToString())
                  .MetricWarn("No template available", applied.NoTemplate.ToString())
                  .MetricError("Failed", applied.Failed.ToString());

            result.AddSection("INTEGRATION")
                  .Metric("Drawing-type stamps", applied.DrawingTypeStamped.ToString(), $"id={DrawingTypeId}")
                  .Metric("Panel-param fills", applied.ParamsStamped.ToString(), "ELC_PNL_NAME / VOLTAGE / LOAD / FED_FROM / MAIN_BRK / WAYS")
                  .Metric("Circuit back-refs", applied.CircuitRefsStamped.ToString(), "ELC_PANEL_SCHEDULE_REF_TXT");

            if (templateError != null)
                result.AddSection("STING TEMPLATES")
                      .MetricError("Template creation failed", templateError, "schedules below use the project's existing templates");
            if (templateRun != null)
            {
                result.AddSection("STING TEMPLATES (created first)");
                foreach (var r in templateRun.Results)
                {
                    if (r.Failed) result.MetricError(r.Name, "not built", r.FailReason);
                    else if (r.Clean) result.MetricHighlight(r.Name, $"{r.CellsVerified}/{r.CellsRequested} cells");
                    else result.MetricWarn(r.Name, $"{r.CellsVerified}/{r.CellsRequested} cells",
                                           $"{r.Problems.Count} problem(s) — run PNLS 📐 for the full report");
                    foreach (var p in r.Problems) StingLog.Warn($"Panel template '{r.Name}': {p}");
                }
                foreach (var w in templateRun.Warnings) result.Text("ℹ " + w);
            }

            if (applied.NoWritesPersisted)
                result.AddSection("WARNING")
                      .Text("Panels needed schedules but none were created — every candidate PanelScheduleTemplate was rejected by CreateInstanceView.");

            if (applied.PerTemplate.Count > 0)
            {
                result.AddSection("BY TEMPLATE");
                foreach (var kv in applied.PerTemplate.OrderByDescending(k => k.Value))
                    result.Metric(kv.Key, kv.Value.ToString());
            }

            if (applied.SkippedNames.Count > 0)
            {
                result.AddSection("SKIPPED (PATTERN MATCH)");
                foreach (string n in applied.SkippedNames.Take(15)) result.Text(n);
                if (applied.SkippedNames.Count > 15) result.Text($"… {applied.SkippedNames.Count - 15} more.");
            }

            if (applied.Failures.Count > 0)
            {
                result.AddSection("FAILURES");
                foreach (string f in applied.Failures.Take(25)) result.Text(f);
                if (applied.Failures.Count > 25) result.Text($"… {applied.Failures.Count - 25} more (see STING log).");
            }

            result.AddSection("NEXT STEPS")
                  .Text("Open each PanelScheduleView from the Project Browser to review.")
                  .Text("Drag schedules onto sheets manually — Revit's PanelScheduleSheetInstance.Create API has been broken since Revit 2024.")
                  .Text("Use 'Panel Schedules → Export to Excel' for bulk circuit-data round-trip.")
                  .Text("Run 'Panel Schedule Audit' to surface drift between rules and reality.");
            result.Show();

            return Result.Succeeded;
        }

        internal static string SafePanelName(Element panel)
        {
            if (panel == null) return "(null)";
            string n = panel.Name;
            if (string.IsNullOrEmpty(n))
                n = ParameterHelpers.GetString(panel, "Panel Name");
            if (string.IsNullOrEmpty(n))
                n = panel.Id.ToString();
            return n;
        }
    }
}
