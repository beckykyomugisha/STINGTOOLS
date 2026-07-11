using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Reports
{
    /// <summary>
    /// W2 — discipline symbol-legend commands for Mechanical / Plumbing /
    /// Structural, modelled on <c>ElectricalDrawingLegendCommand</c> and backed
    /// by the shared <see cref="DisciplineLegendEngine"/>. Each walks its
    /// discipline's family instances, dedupes by (category, family, type),
    /// resolves each to a STING symbol concept, and renders a populated legend
    /// via the Phase 92 LegendBuilder pipeline.
    ///
    /// Architectural is intentionally omitted: the symbol catalogue ships no
    /// dedicated architectural symbol set, so an A legend would carry no
    /// standards references (see SEED_FOLLOWUP.md).
    /// </summary>
    internal static class DisciplineLegendRunner
    {
        public static Result Run(ExternalCommandData data, ref string msg, string disciplineCode)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            string discName = DisciplineLegendEngine.DisciplineName(disciplineCode);

            View legendView = null;
            int uniqueCount = 0;
            using (var tx = new Transaction(doc, $"STING {discName} Drawing Legend"))
            {
                tx.Start();
                try
                {
                    legendView = DisciplineLegendEngine.CreateDisciplineLegend(doc, disciplineCode, out uniqueCount);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"{discName} DrawingLegend create: {ex.Message}", ex);
                    msg = ex.Message;
                    tx.RollBack();
                    return Result.Failed;
                }
                tx.Commit();
            }

            if (legendView == null)
            {
                TaskDialog.Show($"STING {discName} Drawing Legend",
                    "Failed to create the legend view. Check the project has at least one " +
                    "Drafting view family type loaded.");
                return Result.Failed;
            }

            try { ctx.UIDoc.ActiveView = legendView; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string body = uniqueCount > 0
                ? $"Created '{legendView.Name}' with {uniqueCount} unique {discName.ToLowerInvariant()} symbol(s).\n\n"
                : $"Created '{legendView.Name}'.\n\nNo {discName.ToLowerInvariant()} components were placed, so a " +
                  "discipline-colour reference was produced instead. Place components and re-run for a symbol legend.\n\n";
            TaskDialog.Show($"STING {discName} Drawing Legend",
                body + "Drag the legend onto sheets via Project Browser, or use " +
                "DOCS → Place on All Sheets to batch-place it on the drawing set.");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MechanicalDrawingLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => DisciplineLegendRunner.Run(data, ref msg, "M");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbingDrawingLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => DisciplineLegendRunner.Run(data, ref msg, "P");
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StructuralDrawingLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => DisciplineLegendRunner.Run(data, ref msg, "S");
    }
}
