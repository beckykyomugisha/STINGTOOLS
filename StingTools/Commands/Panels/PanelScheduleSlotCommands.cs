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
    // ════════════════════════════════════════════════════════════════════════════
    //  Panel Schedule Slot Management
    //
    //  AddSpare / AddSpace / RemoveSpare / RemoveSpace are the reliable parts
    //  of the PanelScheduleView API. Real-circuit slots are detected via
    //  GetCircuitByCell so the result panel can distinguish "real circuit
    //  here" from "API rejected this slot".
    // ════════════════════════════════════════════════════════════════════════════

    internal static class PanelScheduleResolver
    {
        public static PanelScheduleView Resolve(UIDocument uidoc, Document doc, out string source)
        {
            source = null;
            var active = doc.ActiveView as PanelScheduleView;
            if (active != null) { source = $"active view '{active.Name}'"; return active; }

            try
            {
                var sel = uidoc.Selection.GetElementIds();
                if (sel != null && sel.Count == 1)
                {
                    var el = doc.GetElement(sel.First());
                    if (el is FamilyInstance fi && fi.Category != null
                        && fi.Category.Id.Value == (long)BuiltInCategory.OST_ElectricalEquipment)
                    {
                        var psv = new FilteredElementCollector(doc)
                            .OfClass(typeof(PanelScheduleView))
                            .Cast<PanelScheduleView>()
                            .FirstOrDefault(p => p.GetPanel() == fi.Id);
                        if (psv != null)
                        {
                            source = $"selected panel '{fi.Name}' → '{psv.Name}'";
                            return psv;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PanelScheduleResolver selection: {ex.Message}"); }
            return null;
        }

        public static List<PanelScheduleView> AllSchedules(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView))
                .Cast<PanelScheduleView>()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool HasRealCircuit(PanelScheduleView psv, int row, int col)
        {
            try
            {
                var sys = psv.GetCircuitByCell(row, col);
                return sys != null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetCircuitByCell [{row},{col}] on '{psv.Name}': {ex.Message}");
                return false;
            }
        }
    }

    internal static class SlotOps
    {
        public sealed class Counts
        {
            public int Filled, AlreadyOccupied, RealCircuits, Errors;
        }

        public static Counts FillEmpty(Document doc, PanelScheduleView psv, bool addSpare)
        {
            var c = new Counts();
            int rows = 0, cols = 0;
            try
            {
                var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                if (body == null) return c;
                rows = body.NumberOfRows; cols = body.NumberOfColumns;
            }
            catch (Exception ex) { StingLog.Error($"FillEmpty body '{psv.Name}'", ex); return c; }

            for (int r = 0; r < rows; r++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (PanelScheduleResolver.HasRealCircuit(psv, r, col)) { c.RealCircuits++; continue; }

                    bool occupied = false;
                    try
                    {
                        occupied = psv.IsSpare(r, col) || psv.IsSpace(r, col) || psv.IsSlotLocked(r, col);
                    }
                    catch (Exception ex2) { StingLog.Warn($"slot probe [{r},{col}] on '{psv.Name}': {ex2.Message}"); }
                    if (occupied) { c.AlreadyOccupied++; continue; }

                    try
                    {
                        if (addSpare) psv.AddSpare(r, col);
                        else psv.AddSpace(r, col);
                        c.Filled++;
                    }
                    catch (Exception ex3)
                    {
                        c.Errors++;
                        StingLog.Warn($"Add{(addSpare ? "Spare" : "Space")} [{r},{col}] on '{psv.Name}': {ex3.Message}");
                    }
                }
            }
            return c;
        }

        public static (int spares, int spaces, int errors) ClearSparesAndSpaces(PanelScheduleView psv)
        {
            int spares = 0, spaces = 0, errors = 0;
            int rows, cols;
            try
            {
                var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                if (body == null) return (0, 0, 0);
                rows = body.NumberOfRows; cols = body.NumberOfColumns;
            }
            catch (Exception ex) { StingLog.Error($"ClearSparesSpaces body '{psv.Name}'", ex); return (0, 0, 0); }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    try
                    {
                        if (psv.IsSpare(r, c)) { psv.RemoveSpare(r, c); spares++; continue; }
                        if (psv.IsSpace(r, c)) { psv.RemoveSpace(r, c); spaces++; }
                    }
                    catch (Exception ex2)
                    {
                        errors++;
                        StingLog.Warn($"Clear [{r},{c}] on '{psv.Name}': {ex2.Message}");
                    }
                }
            }
            return (spares, spaces, errors);
        }

        public static (int converted, int errors) ConvertSpacesToSpares(PanelScheduleView psv)
        {
            int converted = 0, errors = 0;
            int rows, cols;
            try
            {
                var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                if (body == null) return (0, 0);
                rows = body.NumberOfRows; cols = body.NumberOfColumns;
            }
            catch (Exception ex) { StingLog.Error($"Convert body '{psv.Name}'", ex); return (0, 0); }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    bool isSpace = false;
                    try { isSpace = psv.IsSpace(r, c); }
                    catch (Exception ex2) { StingLog.Warn($"IsSpace [{r},{c}]: {ex2.Message}"); }
                    if (!isSpace) continue;

                    try
                    {
                        psv.RemoveSpace(r, c);
                        psv.AddSpare(r, c);
                        converted++;
                    }
                    catch (Exception ex3)
                    {
                        errors++;
                        StingLog.Warn($"Convert space → spare [{r},{c}] on '{psv.Name}': {ex3.Message}");
                    }
                }
            }
            return (converted, errors);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FillEmptySlotsWithSparesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunOne(ctx.UIDoc, ctx.Doc, addSpare: true);
        }

        internal static Result RunOne(UIDocument uidoc, Document doc, bool addSpare)
        {
            var psv = PanelScheduleResolver.Resolve(uidoc, doc, out string source);
            if (psv == null)
            {
                TaskDialog.Show("STING Slot Fill",
                    "Open a panel schedule view, OR select a single electrical panel that has a schedule, then run this command again.");
                return Result.Cancelled;
            }

            string action = addSpare ? "Spares" : "Spaces";
            SlotOps.Counts c;
            using (var tx = new Transaction(doc, $"STING Fill Empty Slots — {action}"))
            {
                tx.Start();
                c = SlotOps.FillEmpty(doc, psv, addSpare);
                tx.Commit();
            }

            try { ActionAuditLog.Record($"PanelSchedule_FillSlots_{action}",
                $"{psv.Name}: filled={c.Filled} realCircuits={c.RealCircuits} occupied={c.AlreadyOccupied} errors={c.Errors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            var panel = StingResultPanel.Create($"Fill Empty Slots — {action}");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight($"{action} added", c.Filled.ToString())
                 .Metric("Real circuits (skipped)", c.RealCircuits.ToString())
                 .Metric("Already spare/space/locked", c.AlreadyOccupied.ToString())
                 .MetricError("Errors", c.Errors.ToString());
            panel.AddSection("NOTES")
                 .Text("Real-circuit slots detected via GetCircuitByCell are skipped silently.")
                 .Text("Errors typically indicate template-protected slots (utility row, header carry-over).");
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FillEmptySlotsWithSpacesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return FillEmptySlotsWithSparesCommand.RunOne(ctx.UIDoc, ctx.Doc, addSpare: false);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FillSparesAllSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var schedules = PanelScheduleResolver.AllSchedules(doc);
            if (schedules.Count == 0)
            {
                TaskDialog.Show("STING Slot Fill — All", "No PanelScheduleView objects in this project.");
                return Result.Succeeded;
            }

            var td = new TaskDialog("Fill Empty Slots in ALL Schedules")
            {
                MainInstruction = $"Add Spare to every empty slot across {schedules.Count} panel schedule(s)?",
                MainContent = "Real circuits are detected and skipped. Slots already marked Spare/Space/Locked are skipped. The pre-flight is one transaction group; per-schedule failures roll back only that schedule.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int totalFilled = 0, totalRealCircuits = 0, totalOccupied = 0, totalErrors = 0;
            int schedulesTouched = 0, schedulesSkipped = 0;
            var perSchedule = new List<string>();

            using (var tg = new TransactionGroup(doc, "STING Fill Spares — All Schedules"))
            {
                tg.Start();
                foreach (var psv in schedules)
                {
                    SlotOps.Counts c;
                    using (var tx = new Transaction(doc, $"Fill Spares — {psv.Name}"))
                    {
                        tx.Start();
                        try
                        {
                            c = SlotOps.FillEmpty(doc, psv, addSpare: true);
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            schedulesSkipped++;
                            perSchedule.Add($"{psv.Name}: rolled back ({ex.Message})");
                            StingLog.Warn($"Fill all - rollback {psv.Name}: {ex.Message}");
                            continue;
                        }
                    }
                    schedulesTouched++;
                    totalFilled += c.Filled;
                    totalRealCircuits += c.RealCircuits;
                    totalOccupied += c.AlreadyOccupied;
                    totalErrors += c.Errors;
                    if (c.Filled > 0)
                        perSchedule.Add($"{psv.Name}: +{c.Filled} spares (real circuits {c.RealCircuits}, occupied {c.AlreadyOccupied}, errors {c.Errors})");
                }
                tg.Assimilate();
            }

            try { ActionAuditLog.Record("PanelSchedule_FillSparesAll",
                $"schedules={schedulesTouched} filled={totalFilled} errors={totalErrors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            var panel = StingResultPanel.Create("Fill Spares — All Schedules");
            panel.SetSubtitle($"{schedulesTouched} schedules · {totalFilled} spares added");
            panel.AddSection("TOTALS")
                 .MetricHighlight("Spares added", totalFilled.ToString())
                 .Metric("Real circuits (skipped)", totalRealCircuits.ToString())
                 .Metric("Already occupied", totalOccupied.ToString())
                 .MetricError("Errors", totalErrors.ToString())
                 .MetricWarn("Schedules rolled back", schedulesSkipped.ToString());
            if (perSchedule.Count > 0)
            {
                panel.AddSection("BY SCHEDULE");
                foreach (string s in perSchedule.Take(50)) panel.Text(s);
                if (perSchedule.Count > 50) panel.Text($"… {perSchedule.Count - 50} more.");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConvertSpacesToSparesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var psv = PanelScheduleResolver.Resolve(ctx.UIDoc, doc, out string source);
            if (psv == null)
            {
                TaskDialog.Show("STING Slot Convert",
                    "Open a panel schedule view, OR select a single electrical panel that has a schedule, then run this command again.");
                return Result.Cancelled;
            }

            (int converted, int errors) result;
            using (var tx = new Transaction(doc, "STING Convert Spaces → Spares"))
            {
                tx.Start();
                result = SlotOps.ConvertSpacesToSpares(psv);
                tx.Commit();
            }

            try { ActionAuditLog.Record("PanelSchedule_SpacesToSpares",
                $"{psv.Name}: converted={result.converted} errors={result.errors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            var panel = StingResultPanel.Create("Convert Spaces → Spares");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight("Converted", result.converted.ToString())
                 .MetricError("Errors", result.errors.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearSparesAndSpacesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var psv = PanelScheduleResolver.Resolve(ctx.UIDoc, doc, out string source);
            if (psv == null)
            {
                TaskDialog.Show("STING Slot Clear",
                    "Open a panel schedule view, OR select a single electrical panel that has a schedule, then run this command again.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("Clear Spares & Spaces")
            {
                MainInstruction = $"Remove all spares and spaces from '{psv.Name}'?",
                MainContent = "This does NOT remove any real circuit assignments. Only slots marked as Spare or Space are cleared.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            (int spares, int spaces, int errors) result;
            using (var tx = new Transaction(doc, "STING Clear Spares & Spaces"))
            {
                tx.Start();
                result = SlotOps.ClearSparesAndSpaces(psv);
                tx.Commit();
            }

            try { ActionAuditLog.Record("PanelSchedule_ClearSparesSpaces",
                $"{psv.Name}: spares={result.spares} spaces={result.spaces} errors={result.errors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            var panel = StingResultPanel.Create("Clear Spares & Spaces");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight("Spares removed", result.spares.ToString())
                 .Metric("Spaces removed", result.spaces.ToString())
                 .MetricError("Errors", result.errors.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }
}
