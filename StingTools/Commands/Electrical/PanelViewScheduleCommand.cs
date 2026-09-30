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

            // Inside a workflow preset the unattended AutoSheets mode is the default
            // (PanelSheetPlacementMode): GuidedManual would only list schedules to drag.
            bool inPreset = WorkflowEngine.IsRunningPreset;
            string mode = StingTools.Core.Panels.PanelSheetPlacementMode.Resolve(inPreset,
                inPreset ? WorkflowEngine.StepParam("mode") : null,
                StingElectricalCommandHandler.CurrentSheetPlacementMode, out var modeError);
            if (mode == null) { message = "Panel schedules on sheets: " + modeError; return Result.Failed; }

            if (mode == StingTools.Core.Panels.PanelSheetPlacementMode.AutoSheets)
                return PlaceOnDrawingTypeSheets(doc, ref message);

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
                    catch (Exception ex2) { StingLog.Warn($"PanelViewSchedule {panel.Name}: {ex2.Message}"); skipped++; }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Sheet Placement",
                $"Created {created} ViewSchedule(s). Placed on sheet: {placed}. Skipped: {skipped}.\n\n" +
                "Note: ViewSchedule does not show Revit-computed totals. For live computed-cell data, use the native panel schedule and drag manually.");
            return Result.Succeeded;
        }

        /// <summary>
        /// AutoSheets: one circuit ViewSchedule per board — reused by name on a re-run,
        /// not deleted and remade — each placed on its own elec-panel-schedule-A3 sheet
        /// through DrawingProducer.PlaceExistingView, so the sheet is stamped, numbered
        /// by the drawing type, and found again (by the board's context tag) next time.
        /// The result goes to a dialog, or inside a preset to the log and the step message.
        /// </summary>
        private static Result PlaceOnDrawingTypeSheets(Document doc, ref string message)
        {
            const string title = "STING Panel Schedules on Sheets";
            var dt = StingTools.Core.Drawing.DrawingTypeRegistry.Get(doc, DrawingTypeId);
            if (dt == null)
            {
                message = $"{title}: drawing type '{DrawingTypeId}' is not in the catalogue.";
                return Result.Failed;
            }
            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                PresetDialog.Show(title, "No electrical equipment found — nothing to place.", ref message);
                return Result.Succeeded;
            }

            int made = 0, reusedSchedules = 0, placed = 0, alreadyPlaced = 0, newSheets = 0, failed = 0;
            var warnings = new List<string>();
            using (StingTools.Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
            using (var tx = new Transaction(doc, "STING Panel Schedules on Sheets"))
            {
                tx.Start();
                var byName = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .Where(v => !v.IsTemplate)
                    .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var panel in panels)
                {
                    string panelName = BoardName(panel);
                    try
                    {
                        string viewName = $"STING - Panel - {panelName}";
                        if (!byName.TryGetValue(viewName, out var schedule))
                        {
                            schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_ElectricalCircuit));
                            try { schedule.Name = viewName; } catch (Exception ex) { StingLog.Warn($"Panel schedule name '{viewName}': {ex.Message}"); }
                            AddCircuitFields(schedule);
                            AddPanelFilter(schedule, panelName);
                            byName[viewName] = schedule;
                            made++;
                        }
                        else reusedSchedules++;

                        var pr = StingTools.Core.Drawing.DrawingProducer.PlaceExistingView(doc, dt,
                            new StingTools.Core.Drawing.DrawingContext { Tag = "PANEL-" + panel.Id.Value }, schedule);
                        warnings.AddRange(pr.Warnings.Select(w => $"{panelName}: {w}"));
                        if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) newSheets++;
                        if (pr.ViewportIds.Count == 0) { failed++; continue; }
                        if (pr.ViewportsReused > 0) alreadyPlaced++; else placed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        warnings.Add($"{panelName}: {ex.Message}");
                        StingLog.Warn($"PanelViewSchedule AutoSheets {panelName}: {ex.Message}");
                    }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            foreach (var w in warnings.Distinct()) StingLog.Warn($"{title}: {w}");
            string body = $"{panels.Count} board(s): {placed} schedule(s) placed, {alreadyPlaced} already on their sheet, "
                        + $"{failed} not placed. Schedules: {made} new, {reusedSchedules} reused. Sheets: {newSheets} new ({DrawingTypeId})."
                        + (warnings.Count > 0 ? $"\n{warnings.Distinct().Count()} warning(s) in the STING log." : "")
                        + "\n\nThese are circuit ViewSchedules (no Revit-computed totals). The native panel schedules still have to be dragged by hand — Revit 2024+ cannot place them by API.";
            PresetDialog.Show(title, body, ref message);
            return placed + alreadyPlaced > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>The board's Panel Name — what a circuit's "Panel" field reads — else its element name.</summary>
        private static string BoardName(FamilyInstance panel)
        {
            try
            {
                var n = panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch (Exception ex) { StingLog.Warn($"Panel name {panel?.Id}: {ex.Message}"); }
            return panel?.Name ?? panel?.Id.ToString() ?? "(unnamed)";
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
                StingTools.Core.Drawing.DrawingTypeStamper.Stamp(v, DrawingTypeId);
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
