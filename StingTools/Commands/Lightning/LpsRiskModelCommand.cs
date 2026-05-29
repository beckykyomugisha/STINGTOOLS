// LpsRiskModelCommand.cs
//
// Thin UI entry point onto the BS EN 62305-2 full risk model
// (StingTools.Core.Lightning.LpsRiskModel.Compute). It builds an
// LpsRiskInput from a quick model-extent estimate (wall bounding boxes →
// plan L×W, level span → height) with conservative defaults for every
// other Annex B/C factor, runs the four-risk component model, and reports
// R1–R4 against their tolerable thresholds plus the recommended LPS class +
// coordinated SPD level.
//
// This is intentionally a screening-grade launcher — the detailed per-field
// inputs (line list, mesh widths, withstand voltage, occupancy) live on the
// dedicated LPS panel. Defaults here mirror BS EN 62305-2 worst-case
// assumptions so a bare run still produces a valid, conservative verdict.

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Lightning;

namespace StingTools.Commands.Lightning
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsRiskModelCommand : IExternalCommand, IPanelCommand
    {
        private const double MmPerFoot = 304.8;
        private const double FtToM = 0.3048;

        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Risk Model", "No active document."); return Result.Cancelled; }
            return RunInternal(doc);
        }

        private Result RunInternal(Document doc)
        {
            try
            {
                EstimateExtents(doc, out double L, out double W, out double H);

                var input = new LpsRiskInput
                {
                    LossTypeId = "L1",
                    PlanLengthM = L,
                    PlanWidthM = W,
                    HeightM = H,
                    // Connected services left at the model's representative
                    // power + telecom default (LpsRiskModel.ResolveLines).
                };

                LpsRiskResult r = LpsRiskModel.Compute(input);

                var sb = new StringBuilder();
                sb.AppendLine(r.RequiresLps
                    ? $"LPS REQUIRED — recommend Class {r.RecommendedClass} + coordinated SPD level {r.RecommendedSpdLevel}."
                    : "LPS NOT REQUIRED on these (screening) inputs.");
                sb.AppendLine();
                sb.AppendLine($"Estimated geometry: {L:0.#} × {W:0.#} × {H:0.#} m  (collection area {r.CollectionAreaM2:0} m²)");
                sb.AppendLine($"Annual strikes to structure N_D = {r.AnnualStrikeFrequency:E2}");
                sb.AppendLine();

                if (r.RiskByLossType != null && r.RiskByLossType.Count > 0)
                {
                    sb.AppendLine("Risk by loss type (R vs tolerable Rt):");
                    foreach (var kv in r.RiskByLossType.OrderBy(k => k.Key))
                    {
                        double rt = (r.TolerableByLossType != null && r.TolerableByLossType.TryGetValue(kv.Key, out var t)) ? t : 0.0;
                        bool exceeds = r.ExceedsByLossType != null && r.ExceedsByLossType.TryGetValue(kv.Key, out var x) && x;
                        sb.AppendLine($"  {kv.Key}: R = {kv.Value:E2}   Rt = {rt:E2}   {(exceeds ? "✗ EXCEEDS" : "✓ ok")}");
                    }
                    sb.AppendLine();
                }

                if (r.ComponentBreakdown != null && r.ComponentBreakdown.Count > 0)
                {
                    sb.AppendLine("Component breakdown (headline loss type):");
                    foreach (var kv in r.ComponentBreakdown.OrderByDescending(k => k.Value))
                        sb.AppendLine($"  {kv.Key} = {kv.Value:E2}");
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(r.Notes)) sb.AppendLine(r.Notes);
                sb.AppendLine();
                sb.AppendLine("Screening run with default Annex B/C factors. Use the LPS panel for "
                    + "line list, mesh widths, withstand voltage and occupancy inputs.");

                TaskDialog.Show("STING — LPS Risk Model (BS EN 62305-2)", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsRiskModelCommand failed", ex);
                TaskDialog.Show("STING — LPS Risk Model", "Risk assessment failed: " + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Rough plan extent (m) from the union of wall bounding boxes and
        /// height from the level span. Falls back to a 30×20×6 m structure
        /// when the model has no walls / levels.
        /// </summary>
        private static void EstimateExtents(Document doc, out double L, out double W, out double H)
        {
            L = 30; W = 20; H = 6;
            try
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                bool any = false;
                var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType();
                foreach (var w in walls)
                {
                    var bb = w.get_BoundingBox(null);
                    if (bb == null) continue;
                    any = true;
                    minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y);
                    maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y);
                }
                if (any)
                {
                    double lx = (maxX - minX) * FtToM, ly = (maxY - minY) * FtToM;
                    if (lx > 1) L = lx;
                    if (ly > 1) W = ly;
                }

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .Select(lv => lv.Elevation).OrderBy(e => e).ToList();
                if (levels.Count >= 2)
                {
                    double span = (levels.Last() - levels.First()) * FtToM;
                    if (span > 1) H = span;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LpsRiskModel extent estimate: {ex.Message}"); }
        }
    }
}
