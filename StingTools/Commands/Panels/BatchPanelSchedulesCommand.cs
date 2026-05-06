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

            int created = 0, skippedExisting = 0, skippedPattern = 0,
                failed = 0, noTemplate = 0, paramsStamped = 0, drawingTypeStamped = 0,
                circuitRefsStamped = 0;
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

                    if (existingByPanel.TryGetValue(panel.Id.Value, out var existing))
                    {
                        skippedExisting++;
                        // Even when skipping create, run the post-create wiring on
                        // the existing schedule so re-runs converge on the same state.
                        if (StampDrawingType(existing)) drawingTypeStamped++;
                        if (StampPanelParams(panel, existing)) paramsStamped++;
                        circuitRefsStamped += StampCircuitBackrefs(doc, panel, existing.Name);
                        continue;
                    }

                    var candidates = PanelScheduleTemplateRegistry.ResolveCandidates(
                        doc, panel, out _, out string templateUsed);
                    if (candidates.Count == 0)
                    {
                        noTemplate++;
                        failures.Add($"{panelName}: no PanelScheduleTemplate available in project");
                        continue;
                    }

                    PanelScheduleView psv = null;
                    string lastError = null;
                    string winningTemplate = null;
                    foreach (var tid in candidates) // AUTO-2 multi-template trial
                    {
                        try
                        {
                            psv = PanelScheduleView.CreateInstanceView(doc, tid, panel.Id);
                            if (psv != null)
                            {
                                var tEl = doc.GetElement(tid) as PanelScheduleTemplate;
                                winningTemplate = tEl?.Name ?? templateUsed;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = ex.Message;
                            StingLog.Warn($"CreateInstanceView '{panelName}' template={tid}: {ex.Message}");
                        }
                    }

                    if (psv == null)
                    {
                        failed++;
                        failures.Add($"{panelName}: every candidate template rejected ({lastError ?? "null result"})");
                        continue;
                    }

                    created++;
                    perTemplate[winningTemplate ?? "(unknown)"] =
                        perTemplate.TryGetValue(winningTemplate ?? "(unknown)", out int n) ? n + 1 : 1;

                    if (StampDrawingType(psv)) drawingTypeStamped++;
                    if (StampPanelParams(panel, psv)) paramsStamped++;
                    circuitRefsStamped += StampCircuitBackrefs(doc, panel, psv.Name);
                }
                tx.Commit();
            }

            try { ActionAuditLog.Record("PanelSchedule_BatchCreate",
                $"created={created} existing={skippedExisting} noTemplate={noTemplate} failed={failed} drawingType={drawingTypeStamped} params={paramsStamped} circuitRefs={circuitRefsStamped}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            var result = StingResultPanel.Create("Batch Panel Schedules");
            result.SetSubtitle($"{created} created · {skippedExisting} existing · {skippedPattern} skipped · {failed + noTemplate} failed");
            result.AddSection("SUMMARY")
                  .Metric("Panels found", panels.Count.ToString())
                  .MetricHighlight("Schedules created", created.ToString())
                  .Metric("Already had schedule", skippedExisting.ToString())
                  .Metric("Skipped (pattern)", skippedPattern.ToString())
                  .MetricWarn("No template available", noTemplate.ToString())
                  .MetricError("Failed", failed.ToString());

            result.AddSection("INTEGRATION")
                  .Metric("Drawing-type stamps", drawingTypeStamped.ToString(), $"id={DrawingTypeId}")
                  .Metric("Panel-param fills", paramsStamped.ToString(), "ELC_PNL_NAME / VOLTAGE / LOAD / FED_FROM / MAIN_BRK / WAYS")
                  .Metric("Circuit back-refs", circuitRefsStamped.ToString(), "ELC_PANEL_SCHEDULE_REF_TXT");

            if (perTemplate.Count > 0)
            {
                result.AddSection("BY TEMPLATE");
                foreach (var kv in perTemplate.OrderByDescending(k => k.Value))
                    result.Metric(kv.Key, kv.Value.ToString());
            }

            if (skippedNames.Count > 0)
            {
                result.AddSection("SKIPPED (PATTERN MATCH)");
                foreach (string n in skippedNames.Take(15)) result.Text(n);
                if (skippedNames.Count > 15) result.Text($"… {skippedNames.Count - 15} more.");
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
                  .Text("Use 'Panel Schedules → Export to Excel' for bulk circuit-data round-trip.")
                  .Text("Run 'Panel Schedule Audit' to surface drift between rules and reality.");
            result.Show();

            StingLog.Info($"BatchPanelSchedules: created={created} existing={skippedExisting} skipped={skippedPattern} noTemplate={noTemplate} failed={failed} drawingType={drawingTypeStamped} params={paramsStamped} circuitRefs={circuitRefsStamped}");
            return Result.Succeeded;
        }

        // INTEGRATION-4: stamp STING_DRAWING_TYPE_ID_TXT on the schedule view.
        private static bool StampDrawingType(PanelScheduleView psv)
        {
            try { return DrawingTypeStamper.Stamp(psv, DrawingTypeId); }
            catch (Exception ex) { StingLog.Warn($"Stamp drawing-type on '{psv.Name}': {ex.Message}"); return false; }
        }

        // INTEGRATION-2: backfill ELC_PNL_* parameters on the panel from electrical data
        // and the new schedule. SetIfEmpty so user-authored values are never overwritten.
        private static bool StampPanelParams(FamilyInstance panel, PanelScheduleView psv)
        {
            if (panel == null || psv == null) return false;
            int wrote = 0;
            try
            {
                wrote += Try(panel, ParamRegistry.ELC_PNL_NAME, psv.Name);
                wrote += Try(panel, ParamRegistry.ELC_PNL_VOLTAGE, ReadString(panel, "Panel Voltage"));
                wrote += Try(panel, ParamRegistry.ELC_PNL_LOAD, ReadString(panel, "Total Connected"));
                wrote += Try(panel, ParamRegistry.ELC_PNL_FED_FROM, ReadString(panel, "Panel Source")
                    ?? ReadString(panel, "Source"));
                wrote += Try(panel, ParamRegistry.ELC_MAIN_BRK, ReadString(panel, "Mains")
                    ?? ReadString(panel, "Main Disconnect"));
                wrote += Try(panel, ParamRegistry.ELC_WAYS, ReadInt(panel, "Number Of Circuits")
                    ?? ReadInt(panel, "Number of Slots"));
            }
            catch (Exception ex) { StingLog.Warn($"StampPanelParams '{panel.Name}': {ex.Message}"); }
            return wrote > 0;
        }

        // INTEGRATION-3: write back-reference on every ElectricalSystem feeding the panel.
        // We iterate ElectricalSystems and match BaseEquipment.Id rather than the
        // (deprecated/version-variant) MEPModel.GetElectricalSystems API.
        private static int StampCircuitBackrefs(Document doc, FamilyInstance panel, string scheduleName)
        {
            if (panel == null || string.IsNullOrEmpty(scheduleName)) return 0;
            int n = 0;
            try
            {
                string refValue = $"PS: {scheduleName}";
                var circuits = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(s =>
                    {
                        try { return s.BaseEquipment != null && s.BaseEquipment.Id == panel.Id; }
                        catch (Exception ex) { StingLog.Warn($"BaseEquipment probe: {ex.Message}"); return false; }
                    });

                foreach (var sys in circuits)
                {
                    if (ParameterHelpers.SetString(sys, ParamRegistry.PARA_ELC_PANEL, refValue, overwrite: true))
                        n++;
                    if (ParameterHelpers.SetString(sys, "ELC_PANEL_SCHEDULE_REF_TXT", refValue, overwrite: true))
                        n++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"StampCircuitBackrefs '{panel.Name}': {ex.Message}"); }
            return n;
        }

        private static int Try(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return ParameterHelpers.SetIfEmpty(el, paramName, value) ? 1 : 0;
        }

        private static string ReadString(Element el, string nativeParam)
        {
            try
            {
                var p = el?.LookupParameter(nativeParam);
                if (p == null) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                if (p.StorageType == StorageType.Double)
                    return p.AsValueString();
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
            }
            catch (Exception ex) { StingLog.Warn($"ReadString {nativeParam}: {ex.Message}"); }
            return null;
        }

        private static string ReadInt(Element el, string nativeParam)
        {
            try
            {
                var p = el?.LookupParameter(nativeParam);
                if (p == null) return null;
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.Double) return ((int)Math.Round(p.AsDouble())).ToString();
            }
            catch (Exception ex) { StingLog.Warn($"ReadInt {nativeParam}: {ex.Message}"); }
            return null;
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
