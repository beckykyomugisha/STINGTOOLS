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
        // The id routing gives E / ELEC_PANEL_SCHEDULE, resolved once per run (Execute).
        private string _drawingTypeId;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            _drawingTypeId = StingTools.Core.Drawing.DrawingRouteResolver.IdFor(doc, StingTools.Core.Drawing.DrawingRouteRequests.PanelSchedule);

            // Inside a workflow preset the unattended AutoSheets mode is the default
            // (PanelSheetPlacementMode): GuidedManual would only list schedules to drag.
            bool inPreset = WorkflowEngine.IsRunningPreset;
            string mode = StingTools.Core.Panels.PanelSheetPlacementMode.Resolve(inPreset,
                inPreset ? WorkflowEngine.StepParam("mode") : null,
                StingElectricalCommandHandler.CurrentSheetPlacementMode, out var modeError);
            if (mode == null) { message = "Panel schedules on sheets: " + modeError; return Result.Failed; }

            if (mode == StingTools.Core.Panels.PanelSheetPlacementMode.AutoSheets)
                return PlaceOnDrawingTypeSheets(doc, _drawingTypeId, ref message);

            if (mode == "GuidedManual")
            {
                ShowGuidedManual(doc);
                return Result.Succeeded;
            }
            if (mode == "PDF")
            {
                // Not built: it places nothing, so it must not report success. Cancelled
                // tells the panel (and a workflow report, as SKIP) that nothing was done.
                message = "Panel schedules on sheets: PDF embed mode is not implemented — nothing was placed. "
                        + "Use AutoSheets, ViewSchedule or Guided Manual.";
                TaskDialog.Show("STING Sheet Placement", message);
                return Result.Cancelled;
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

            int created = 0, reused = 0, placed = 0, alreadyPlaced = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Place Panel ViewSchedules"))
            {
                tx.Start();
                double y = 0;
                var byName = SchedulesByName(doc);
                foreach (var panel in panels)
                {
                    // The board's Panel Name, as AutoSheets uses: panel.Name is the family type
                    // name, which the circuits' "Panel" field never equals, so the filter left
                    // every schedule empty — and boards sharing a type deleted each other's.
                    string panelName = BoardName(panel);
                    try
                    {
                        string viewName = $"STING - Panel - {panelName}";
                        // Reused (refiltered), not deleted and remade: deleting took the schedule
                        // off every sheet AutoSheets had put it on. It is the same schedule both
                        // modes make.
                        if (byName.TryGetValue(viewName, out var schedule))
                        {
                            ResetPanelFilter(schedule, panelName);
                            reused++;
                        }
                        else
                        {
                            schedule = ViewSchedule.CreateSchedule(doc,
                                new ElementId(BuiltInCategory.OST_ElectricalCircuit));
                            try { schedule.Name = viewName; }
                            catch (Exception ex) { StingLog.Warn($"Panel schedule name '{viewName}': {ex.Message}"); }
                            AddCircuitFields(schedule);
                            AddPanelFilter(schedule, panelName);
                            byName[viewName] = schedule;
                            created++;
                        }
                        StampDrawingType(schedule, _drawingTypeId);

                        if (sheet != null)
                        {
                            try
                            {
                                // A schedule goes on a sheet as a ScheduleSheetInstance;
                                // Viewport.Create refuses a schedule view, so nothing was placed.
                                bool onSheet = new FilteredElementCollector(doc, sheet.Id)
                                    .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                                    .Any(i => i.ScheduleId == schedule.Id);
                                if (onSheet) alreadyPlaced++;
                                else
                                {
                                    var pt = new XYZ(0.5, 0.5 - y, 0);
                                    ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, pt);
                                    placed++;
                                    y += 0.4;
                                }
                            }
                            catch (Exception ex2) { StingLog.Warn($"Place schedule '{viewName}' on sheet: {ex2.Message}"); }
                        }
                    }
                    catch (Exception ex2) { StingLog.Warn($"PanelViewSchedule {panelName}: {ex2.Message}"); skipped++; }
                }
                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    message = $"STING Sheet Placement: the transaction did not commit ({status}); nothing was created or placed.";
                    StingLog.Warn(message);
                    TaskDialog.Show("STING Sheet Placement", message);
                    return Result.Failed;
                }
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Sheet Placement",
                $"ViewSchedules: {created} created, {reused} reused. Placed on sheet: {placed}"
                + (alreadyPlaced > 0 ? $" ({alreadyPlaced} already there)" : "")
                + (sheet == null ? " (no target sheet chosen)" : "") + $". Skipped: {skipped}.\n\n" +
                "Note: ViewSchedule does not show Revit-computed totals. For live computed-cell data, use the native panel schedule and drag manually.");
            return Result.Succeeded;
        }

        /// <summary>
        /// AutoSheets: one circuit ViewSchedule per board — reused by name on a re-run,
        /// not deleted and remade — each placed on its own panel-schedule drawing-type sheet
        /// through DrawingProducer.PlaceExistingView, so the sheet is stamped, numbered
        /// by the drawing type, and found again (by the board's context tag) next time.
        /// The result goes to a dialog, or inside a preset to the log and the step message.
        /// </summary>
        private static Result PlaceOnDrawingTypeSheets(Document doc, string DrawingTypeId, ref string message)
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

            // Sheets stamped with the shipped id before a project re-routed the key are
            // the same boards' sheets: found under either id, never duplicated.
            var stampIds = StingTools.Core.Drawing.DrawingRouteRequests.StampIds(DrawingTypeId, StingTools.Core.Drawing.DrawingRouteRequests.PanelSchedule);
            int made = 0, reusedSchedules = 0, placed = 0, alreadyPlaced = 0, newSheets = 0, failed = 0;
            var warnings = new List<string>();
            using (StingTools.Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
            using (var tx = new Transaction(doc, "STING Panel Schedules on Sheets"))
            {
                tx.Start();
                var byName = SchedulesByName(doc);
                foreach (var panel in panels)
                {
                    string panelName = BoardName(panel);
                    string viewName = $"STING - Panel - {panelName}";
                    bool madeHere = false;
                    // DTW-216: each board in its own sub-transaction, so a board production
                    // refuses (its sheet number could not be reserved) is undone alone and the
                    // boards before it are kept.
                    using (var st = new SubTransaction(doc))
                    try
                    {
                        st.Start();
                        if (!byName.TryGetValue(viewName, out var schedule))
                        {
                            schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_ElectricalCircuit));
                            try { schedule.Name = viewName; } catch (Exception ex) { StingLog.Warn($"Panel schedule name '{viewName}': {ex.Message}"); }
                            AddCircuitFields(schedule);
                            AddPanelFilter(schedule, panelName);
                            madeHere = true;
                        }

                        var pr = StingTools.Core.Drawing.DrawingProducer.PlaceExistingView(doc, dt,
                            new StingTools.Core.Drawing.DrawingContext { Tag = StingTools.Core.Drawing.BoardNaming.ScheduleSheetTag(panel.Id.Value), FormerDrawingTypeIds = stampIds }, schedule);
                        var notes = new List<string>();
                        var failure = pr.TakeInto(notes);
                        warnings.AddRange(notes.Select(w => $"{panelName}: {w}"));
                        if (failure != null)
                        {
                            st.RollBack();
                            failed++;
                            warnings.Add(StingTools.Core.Drawing.ProductionEdgeDecisions.RolledBackLine(panelName, failure));
                            continue;
                        }
                        st.Commit();
                        if (madeHere) { byName[viewName] = schedule; made++; } else reusedSchedules++;
                        if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) newSheets++;
                        if (pr.ViewportIds.Count == 0) { failed++; continue; }
                        if (pr.ViewportsReused > 0) alreadyPlaced++; else placed++;
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        failed++;
                        warnings.Add($"{panelName}: {ex.Message} — rolled back.");
                        StingLog.Warn($"PanelViewSchedule AutoSheets {panelName}: {ex.Message}");
                    }
                }
                // Counted only once Revit has committed: a commit a failure handler rolls
                // back placed nothing, however many schedules the loop created.
                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                {
                    message = $"{title}: the transaction did not commit ({status}); {made} schedule(s), {placed} placement(s) "
                            + $"and {newSheets} sheet(s) were not kept.";
                    StingLog.Warn(message);
                    if (!PresetDialog.Quiet) TaskDialog.Show(title, message);
                    return Result.Failed;
                }
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
        private static string BoardName(FamilyInstance panel) => StingTools.Core.Drawing.BoardNames.Of(panel);

        private static Dictionary<string, ViewSchedule> SchedulesByName(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(v => !v.IsTemplate)
                .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Point a reused schedule's Panel filter at <paramref name="panelName"/>: a schedule
        /// made before the filter used the Panel Name filtered on the type name and was empty.
        /// </summary>
        private static void ResetPanelFilter(ViewSchedule sched, string panelName)
        {
            try
            {
                var def = sched.Definition;
                var panelField = def.GetFieldOrder().Select(id => def.GetField(id))
                    .FirstOrDefault(f => f.GetName() == "Panel");
                if (panelField == null) return;
                for (int i = def.GetFilterCount() - 1; i >= 0; i--)
                    if (def.GetFilter(i).FieldId == panelField.FieldId) def.RemoveFilter(i);
                def.AddFilter(new ScheduleFilter(panelField.FieldId, ScheduleFilterType.Equal, panelName));
            }
            catch (Exception ex) { StingLog.Warn($"ResetPanelFilter '{sched?.Name}': {ex.Message}"); }
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

        private static void StampDrawingType(View v, string drawingTypeId)
        {
            try
            {
                StingTools.Core.Drawing.DrawingTypeStamper.Stamp(v, drawingTypeId);
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
