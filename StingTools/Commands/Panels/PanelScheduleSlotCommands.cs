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
    //  AddSpare / AddSpace / RemoveSpare / RemoveSpace / MoveSlotTo are the
    //  reliable parts of the PanelScheduleView API. This file exposes three
    //  user-facing batch commands that operate on either:
    //    - the active view (if it is a PanelScheduleView), or
    //    - the panel schedule of a single selected panel element
    //
    //  Per-row/per-cell editing is done via Revit's UI; STING only provides
    //  bulk operations.
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
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FillEmptySlotsWithSparesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return Run(ctx.UIDoc, ctx.Doc, addSpare: true);
        }

        internal static Result Run(UIDocument uidoc, Document doc, bool addSpare)
        {
            var psv = PanelScheduleResolver.Resolve(uidoc, doc, out string source);
            if (psv == null)
            {
                TaskDialog.Show("STING Slot Fill",
                    "Open a panel schedule view, OR select a single electrical panel that has a schedule, then run this command again.");
                return Result.Cancelled;
            }

            int filled = 0, alreadyOccupied = 0, errors = 0;
            string action = addSpare ? "Spares" : "Spaces";
            using (var tx = new Transaction(doc, $"STING Fill Empty Slots — {action}"))
            {
                tx.Start();
                int rows = 0, cols = 0;
                try
                {
                    var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                    if (body == null)
                    {
                        TaskDialog.Show("STING Slot Fill", "Body section unavailable for this panel schedule.");
                        return Result.Failed;
                    }
                    rows = body.NumberOfRows;
                    cols = body.NumberOfColumns;
                }
                catch (Exception ex) { StingLog.Error("FillEmptySlots Body", ex); return Result.Failed; }

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        bool occupied = false;
                        try
                        {
                            occupied = psv.IsSpare(r, c) || psv.IsSpace(r, c) || psv.IsSlotLocked(r, c);
                        }
                        catch (Exception ex) { StingLog.Warn($"slot probe [{r},{c}]: {ex.Message}"); }

                        if (occupied) { alreadyOccupied++; continue; }

                        try
                        {
                            if (addSpare) psv.AddSpare(r, c);
                            else psv.AddSpace(r, c);
                            filled++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Add{action} [{r},{c}] on '{psv.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create($"Fill Empty Slots — {action}");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight($"{action} added", filled.ToString())
                 .Metric("Already occupied", alreadyOccupied.ToString())
                 .MetricError("Errors", errors.ToString());
            panel.AddSection("NOTES")
                 .Text("Slot occupancy = circuit row OR existing spare OR existing space OR locked.")
                 .Text("Errors are typical when the template marks a slot as non-fillable (e.g. utility row, header carry-over).");
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
            return FillEmptySlotsWithSparesCommand.Run(ctx.UIDoc, ctx.Doc, addSpare: false);
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

            int converted = 0, errors = 0;
            using (var tx = new Transaction(doc, "STING Convert Spaces → Spares"))
            {
                tx.Start();
                int rows = 0, cols = 0;
                try
                {
                    var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                    if (body == null) { return Result.Failed; }
                    rows = body.NumberOfRows; cols = body.NumberOfColumns;
                }
                catch (Exception ex) { StingLog.Error("Convert Body", ex); message = ex.Message; return Result.Failed; }

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        bool isSpace = false;
                        try { isSpace = psv.IsSpace(r, c); }
                        catch (Exception ex) { StingLog.Warn($"IsSpace [{r},{c}]: {ex.Message}"); }
                        if (!isSpace) continue;

                        try
                        {
                            psv.RemoveSpace(r, c);
                            psv.AddSpare(r, c);
                            converted++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Convert space → spare [{r},{c}] on '{psv.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Convert Spaces → Spares");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight("Converted", converted.ToString())
                 .MetricError("Errors", errors.ToString());
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

            int removedSpares = 0, removedSpaces = 0, errors = 0;
            using (var tx = new Transaction(doc, "STING Clear Spares & Spaces"))
            {
                tx.Start();
                int rows = 0, cols = 0;
                try
                {
                    var body = psv.GetTableData()?.GetSectionData(SectionType.Body);
                    if (body == null) { return Result.Failed; }
                    rows = body.NumberOfRows; cols = body.NumberOfColumns;
                }
                catch (Exception ex) { StingLog.Error("Clear Body", ex); message = ex.Message; return Result.Failed; }

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        try
                        {
                            if (psv.IsSpare(r, c)) { psv.RemoveSpare(r, c); removedSpares++; continue; }
                            if (psv.IsSpace(r, c)) { psv.RemoveSpace(r, c); removedSpaces++; }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Clear [{r},{c}] on '{psv.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Clear Spares & Spaces");
            panel.SetSubtitle($"{psv.Name} · {source}");
            panel.AddSection("RESULT")
                 .MetricHighlight("Spares removed", removedSpares.ToString())
                 .Metric("Spaces removed", removedSpaces.ToString())
                 .MetricError("Errors", errors.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }
}
