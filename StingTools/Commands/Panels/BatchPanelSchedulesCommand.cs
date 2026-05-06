using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    /// <summary>
    /// Batch-creates one PanelScheduleView per electrical panel using the
    /// rule-based picker in <see cref="PanelScheduleTemplateRegistry"/>.
    /// Skips panels that already have a schedule and panels matching the
    /// configured skip patterns. Replaces the simpler templates.First()
    /// heuristic in <c>StingTools.Temp.PanelScheduleCommand</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchPanelSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            PanelScheduleTemplateRegistry.Reload(doc);

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Panel Schedules", "No electrical equipment found.");
                return Result.Succeeded;
            }

            var existingByPanel = new Dictionary<long, PanelScheduleView>();
            try
            {
                foreach (var psv in new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleView))
                    .Cast<PanelScheduleView>())
                {
                    var pid = psv.GetPanel();
                    if (pid != null && pid != ElementId.InvalidElementId)
                        existingByPanel[pid.Value] = psv;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Existing PSV index: {ex.Message}"); }

            int created = 0, skippedExisting = 0, skippedPattern = 0, failed = 0, noTemplate = 0;
            var perTemplate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            var skippedNames = new List<string>();

            using (var tx = new Transaction(doc, "STING Batch Panel Schedules"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    string panelName = SafePanelName(panel);

                    if (PanelScheduleTemplateRegistry.ShouldSkip(panelName))
                    {
                        skippedPattern++;
                        skippedNames.Add(panelName);
                        continue;
                    }

                    if (existingByPanel.ContainsKey(panel.Id.Value))
                    {
                        skippedExisting++;
                        continue;
                    }

                    var templateId = PanelScheduleTemplateRegistry.Resolve(doc, panel, out _, out string templateUsed);
                    if (templateId == ElementId.InvalidElementId)
                    {
                        noTemplate++;
                        failures.Add($"{panelName}: no PanelScheduleTemplate available in project");
                        continue;
                    }

                    try
                    {
                        var psv = PanelScheduleView.CreateInstanceView(doc, templateId, panel.Id);
                        if (psv != null)
                        {
                            created++;
                            perTemplate[templateUsed ?? "(unknown)"] =
                                perTemplate.TryGetValue(templateUsed ?? "(unknown)", out int n) ? n + 1 : 1;
                        }
                        else
                        {
                            failed++;
                            failures.Add($"{panelName}: CreateInstanceView returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add($"{panelName}: {ex.Message}");
                        StingLog.Warn($"Panel schedule failed for {panelName}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            var result = StingResultPanel.Create("Batch Panel Schedules");
            result.SetSubtitle($"{created} created · {skippedExisting} existing · {skippedPattern} skipped · {failed + noTemplate} failed");
            result.AddSection("SUMMARY")
                  .Metric("Panels found", panels.Count.ToString())
                  .MetricHighlight("Schedules created", created.ToString())
                  .Metric("Already had schedule", skippedExisting.ToString())
                  .Metric("Skipped (pattern)", skippedPattern.ToString())
                  .MetricWarn("No template available", noTemplate.ToString())
                  .MetricError("Failed", failed.ToString());

            if (perTemplate.Count > 0)
            {
                result.AddSection("BY TEMPLATE");
                foreach (var kv in perTemplate.OrderByDescending(k => k.Value))
                    result.Metric(kv.Key, kv.Value.ToString());
            }

            if (failures.Count > 0)
            {
                result.AddSection("FAILURES");
                foreach (string f in failures.Take(25)) result.Text(f);
                if (failures.Count > 25) result.Text($"… {failures.Count - 25} more (see STING log).");
            }

            result.AddSection("NEXT STEPS")
                  .Text("Open each PanelScheduleView from the Project Browser to review.")
                  .Text("Drag schedules onto sheets manually — Revit's PanelScheduleSheetInstance.Create API has been broken since Revit 2024.")
                  .Text("Use 'Panel Schedules → Export to Excel' for bulk circuit-data round-trip.");
            result.Show();

            StingLog.Info($"BatchPanelSchedules: created={created} existing={skippedExisting} skipped={skippedPattern} noTemplate={noTemplate} failed={failed}");
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
