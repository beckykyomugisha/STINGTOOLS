// Pack 6 — AVF heat-map commands.
//
// Four "Visualise" buttons plus one Clear button. Each opens a fresh
// transaction on the active view, pipes an IAvfMetricAdapter through
// AvfHeatmapEngine.Paint, and reports how many primitives rendered.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Visualization;

namespace StingTools.Commands.Visualization
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VisualiseComplianceHeatmapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => HeatmapHelpers.Paint(commandData, new ComplianceHeatmapAdapter(), ref message);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VisualiseFillHeatmapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => HeatmapHelpers.Paint(commandData, new FillHeatmapAdapter(), ref message);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VisualiseCarbonHeatmapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => HeatmapHelpers.Paint(commandData, new CarbonHeatmapAdapter(), ref message);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VisualiseAcousticHeatmapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => HeatmapHelpers.Paint(commandData, new AcousticHeatmapAdapter(), ref message);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearHeatmapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                using (var t = new Transaction(ctx.Doc, "STING Clear Heat-map"))
                {
                    t.Start();
                    AvfHeatmapEngine.Clear(ctx.Doc.ActiveView);
                    t.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ClearHeatmapCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    internal static class HeatmapHelpers
    {
        public static Result Paint(ExternalCommandData commandData, IAvfMetricAdapter adapter, ref string message)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document"; return Result.Failed; }
                int n;
                using (var t = new Transaction(ctx.Doc, $"STING AVF: {adapter.MetricName}"))
                {
                    t.Start();
                    AvfHeatmapEngine.Clear(ctx.Doc.ActiveView);
                    n = AvfHeatmapEngine.Paint(ctx.Doc.ActiveView, adapter);
                    t.Commit();
                }
                TaskDialog.Show("STING Heat-map",
                    $"{adapter.MetricName}\n\nRendered {n} primitive(s) on '{ctx.Doc.ActiveView.Name}'.\n\n" +
                    (n == 0 ? "No data samples — check that the underlying engine has values to paint." :
                     "Gradient legend available from View ▸ Analysis Display Style."));
                return n > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex) { StingLog.Error($"Paint heat-map {adapter?.MetricName}", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
