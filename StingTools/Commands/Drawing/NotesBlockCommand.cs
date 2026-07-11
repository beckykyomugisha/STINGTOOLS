using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    /// <summary>
    /// W3 — place a General / discipline standard-notes block on the active
    /// sheet, rendered from <see cref="DisciplineNotesRegistry"/> (corporate
    /// baseline <c>STING_DISCIPLINE_NOTES.json</c> + per-project override at
    /// <c>_BIM_COORD/discipline_notes.json</c>).
    ///
    /// Discipline is inferred from the sheet's STING drawing-type stamp; the
    /// block anchors at the title-block "notes" slot when present, else the
    /// sheet's top-right column. Wired to the same registry that feeds the
    /// <see cref="DrawingProducer"/> Notes production path.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class NotesBlockCommand : IExternalCommand
    {
        private const double MmPerFoot = 304.8;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING Notes Block", "Open a sheet view, then re-run to place the notes block.");
                return Result.Cancelled;
            }

            string disc = ResolveSheetDiscipline(doc, sheet);
            var sections = DisciplineNotesRegistry.GetSections(doc, disc);
            if (sections.Count == 0)
            {
                TaskDialog.Show("STING Notes Block",
                    $"No standard notes are defined for discipline '{disc}'.\n\n" +
                    "Add them to STING_DISCIPLINE_NOTES.json (or the project override at " +
                    "_BIM_COORD/discipline_notes.json) and re-run.");
                return Result.Cancelled;
            }

            ResolveNotesAnchor(doc, sheet, out XYZ topLeft, out double wrapWidthFt);

            int placed;
            var warnings = new List<string>();
            using (var tx = new Transaction(doc, "STING Place Notes Block"))
            {
                tx.Start();
                placed = DisciplineNotesRegistry.RenderSections(doc, sheet, sections, topLeft, wrapWidthFt, warnings);
                tx.Commit();
            }

            string discName = DisciplineLegendEngine.DisciplineName(disc);
            string body = $"Placed {placed} note line(s) for {discName} on sheet {sheet.SheetNumber}.";
            if (warnings.Count > 0) body += "\n\n" + string.Join("\n", warnings);
            TaskDialog.Show("STING Notes Block", body);
            return placed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        private static string ResolveSheetDiscipline(Document doc, ViewSheet sheet)
        {
            try
            {
                string dtId = ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                if (!string.IsNullOrEmpty(dtId))
                {
                    var dt = DrawingTypeRegistry.Get(doc, dtId);
                    if (dt != null && !string.IsNullOrEmpty(dt.Discipline) && dt.Discipline != "*")
                        return dt.Discipline;
                }
            }
            catch (Exception ex) { StingLog.Warn($"NotesBlock discipline: {ex.Message}"); }
            return "General";
        }

        private static void ResolveNotesAnchor(Document doc, ViewSheet sheet, out XYZ topLeft, out double wrapWidthFt)
        {
            var outline = sheet.Outline;
            // Default: right-hand column, ~90 mm wide, near the top.
            wrapWidthFt = 90.0 / MmPerFoot;
            double margin = 15.0 / MmPerFoot;
            topLeft = new XYZ(outline.Max.U - margin - wrapWidthFt, outline.Max.V - margin, 0);

            try
            {
                var titleBlock = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                if (titleBlock == null) return;
                var slotMap = TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, titleBlock);
                var slotId = TitleBlockSlotUtils.ResolveSlotIdForTag(slotMap, "notes", null);
                if (slotId != null && slotMap.TryGetValue(slotId, out var bounds) && bounds.Bbox != null)
                {
                    // Top-left of the slot; wrap to the slot width (minus a small inset).
                    double inset = 2.0 / MmPerFoot;
                    topLeft = new XYZ(bounds.Min.X + inset, bounds.Max.Y - inset, 0);
                    wrapWidthFt = Math.Max(0.02, (bounds.Max.X - bounds.Min.X) - 2 * inset);
                }
            }
            catch (Exception ex) { StingLog.Warn($"NotesBlock anchor: {ex.Message}"); }
        }
    }
}
