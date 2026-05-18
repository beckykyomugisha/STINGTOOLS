using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Sheet-placement workflow with three modes:
    ///  • GuidedManual — Phase 177 behaviour (drag manually).
    ///  • ViewSchedule  — creates a per-panel ViewSchedule of OST_ElectricalCircuit
    ///                    filtered to the panel name and places it on a target
    ///                    sheet via Viewport.Create() (works around the broken
    ///                    PanelScheduleSheetInstance.Create()).
    ///  • PDF           — falls back to a TaskDialog message (PDF embed is Phase 179).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelViewScheduleCommand : IExternalCommand
    {
        private const string DrawingTypeId = "elec-panel-schedule-A3";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string mode = StingElectricalCommandHandler.CurrentSheetPlacementMode ?? "GuidedManual";

            if (mode == "GuidedManual")
            {
                ShowGuidedManual(doc);
                return Result.Succeeded;
            }
            if (mode == "PDF")
            {
                TaskDialog.Show("STING Sheet Placement",
                    "PDF embed mode is queued for Phase 179. Use Guided Manual or ViewSchedule for now.");
                return Result.Succeeded;
            }

            // ViewSchedule mode.
            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Sheet Placement", "No electrical equipment found.");
                return Result.Cancelled;
            }

            var sheetId = StingElectricalCommandHandler.CurrentSheetPlacementSheetId;
            ViewSheet sheet = sheetId != null && sheetId != ElementId.InvalidElementId
                ? doc.GetElement(sheetId) as ViewSheet
                : null;

            int created = 0, placed = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Place Panel ViewSchedules"))
            {
                tx.Start();
                double y = 0;
                foreach (var panel in panels)
                {
                    try
                    {
                        string viewName = $"STING - Panel - {panel.Name}";
                        var existing = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                            .FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            try { doc.Delete(existing.Id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); skipped++; continue; }
                        }
                        var schedule = ViewSchedule.CreateSchedule(doc,
                            new ElementId(BuiltInCategory.OST_ElectricalCircuit));
                        try { schedule.Name = viewName; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        AddCircuitFields(schedule);
                        AddPanelFilter(schedule, panel.Name);
                        StampDrawingType(schedule);
                        created++;

                        if (sheet != null)
                        {
                            try
                            {
                                var pt = new XYZ(0.5, 0.5 - y, 0);
                                Viewport.Create(doc, sheet.Id, schedule.Id, pt);
                                placed++;
                                y += 0.4;
                            }
                            catch (Exception ex2) { StingLog.Warn($"Viewport.Create: {ex2.Message}"); }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"PanelViewSchedule {panel.Name}: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Sheet Placement",
                $"Created {created} ViewSchedule(s). Placed on sheet: {placed}. Skipped: {skipped}.\n\n" +
                "Note: ViewSchedule does not show Revit-computed totals. For live computed-cell data, use the native panel schedule and drag manually.");
            return Result.Succeeded;
        }

        private static void ShowGuidedManual(Document doc)
        {
            // Set of panel-schedule-view ids that already have at least one
            // PanelScheduleSheetInstance referencing them.
            var placed = new HashSet<long>();
            try
            {
                foreach (var inst in new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleSheetInstance)).Cast<PanelScheduleSheetInstance>())
                {
                    try { placed.Add(inst.ScheduleId.Value); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GuidedManual collect placed: {ex.Message}"); }

            var unplaced = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>()
                .Where(v => !placed.Contains(v.Id.Value))
                .Take(20).ToList();
            string body = unplaced.Count == 0
                ? "All panel schedules already placed."
                : "Drag the following from the Project Browser onto the appropriate sheets:\n  " +
                  string.Join("\n  ", unplaced.Select(v => v.Name));
            TaskDialog.Show("STING Sheet Placement — Guided Manual", body +
                "\n\nNote: PanelScheduleSheetInstance.Create() is broken in Revit 2024+; STING does not call it.");
        }

        private static void AddCircuitFields(ViewSchedule sched)
        {
            try
            {
                var def = sched.Definition;
                BuiltInParameter[] bips =
                {
                    BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER,
                    BuiltInParameter.RBS_ELEC_CIRCUIT_NAME,
                    BuiltInParameter.RBS_ELEC_APPARENT_LOAD,
                    BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES,
                    BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM
                };
                foreach (var bip in bips)
                {
                    try
                    {
                        var pid = new ElementId(bip);
                        var sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == pid);
                        if (sf != null) def.AddField(sf);
                    }
                    catch (Exception ex) { StingLog.Warn($"AddField {bip}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"AddCircuitFields: {ex.Message}"); }
        }

        private static void AddPanelFilter(ViewSchedule sched, string panelName)
        {
            try
            {
                var def = sched.Definition;
                var panelField = def.GetFieldOrder()
                    .Select(id => def.GetField(id))
                    .FirstOrDefault(f => f.GetName() == "Panel");
                if (panelField == null) return;
                var f = new ScheduleFilter(panelField.FieldId, ScheduleFilterType.Equal, panelName);
                def.AddFilter(f);
            }
            catch (Exception ex) { StingLog.Warn($"AddPanelFilter: {ex.Message}"); }
        }

        private static void StampDrawingType(View v)
        {
            try
            {
                var t = Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper");
                t?.GetMethod("Stamp", new[] { typeof(Element), typeof(string) })
                  ?.Invoke(null, new object[] { v, DrawingTypeId });
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
