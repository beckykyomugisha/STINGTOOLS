using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Model-wide conduit + cable-tray fill validation. Delegates to the
    /// existing Phase 175 <see cref="TrayFillCalculator"/>; writes
    /// ELC_CONDUIT_FILL_PCT to each conduit and applies a red graphic
    /// override to elements that exceed the regulatory fill limit.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConduitFillValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); }
            catch (Exception ex) { StingLog.Warn($"CableManifest.Load: {ex.Message}"); manifest = null; }
            if (manifest == null)
            {
                TaskDialog.Show("STING Conduit Fill",
                    "No cable manifest found. Add cables to the manifest first (CABLE tab → Phase 175 cable engine).");
                return Result.Cancelled;
            }

            var conduits = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToList();
            var trays = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .ToList();
            var all = conduits.Concat(trays).ToList();

            if (all.Count == 0)
            {
                TaskDialog.Show("STING Conduit Fill", "No conduits or cable trays in the model.");
                return Result.Succeeded;
            }

            var ogsRed = new OverrideGraphicSettings();
            ogsRed.SetProjectionLineColor(new Color(244, 67, 54));
            ogsRed.SetProjectionLineWeight(6);
            var fillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            if (fillPattern != null)
            {
                ogsRed.SetSurfaceForegroundPatternId(fillPattern.Id);
                ogsRed.SetSurfaceForegroundPatternColor(new Color(244, 67, 54));
            }

            var results = new List<ConduitFillData>();
            int passed = 0, failed = 0;
            string worstName = ""; double worstFill = 0;
            View activeView = doc.ActiveView;

            using (var tx = new Transaction(doc, "STING Conduit Fill Validation"))
            {
                tx.Start();
                foreach (var el in all)
                {
                    try
                    {
                        var report = TrayFillCalculator.Compute(doc, el, manifest);
                        double pct = report.FillRatio * 100.0;
                        ParameterHelpers.SetString(el, ParamRegistry.ELC_CONDUIT_FILL_PCT,
                            $"{pct:0.0}", overwrite: true);
                        results.Add(new ConduitFillData
                        {
                            ConduitId   = el.Id,
                            ConduitName = el.Name ?? "",
                            FillPct     = pct,
                            LimitPct    = report.FillLimit * 100.0,
                            Passes      = report.PassesLimit
                        });
                        if (report.PassesLimit) passed++;
                        else
                        {
                            failed++;
                            if (activeView != null)
                                try { activeView.SetElementOverrides(el.Id, ogsRed); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            if (pct > worstFill) { worstFill = pct; worstName = el.Name ?? ""; }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Fill compute: {ex.Message}"); }
                }
                tx.Commit();
            }
            StingElectricalCommandHandler.LastConduitFills = results;
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string worstStr = string.IsNullOrEmpty(worstName) ? "—" : $"{worstName} ({worstFill:0.0}%)";
            TaskDialog.Show("STING Conduit Fill",
                $"Checked {results.Count} containment element(s). Passing: {passed}. Failing: {failed}.\nWorst: {worstStr}.");
            return Result.Succeeded;
        }
    }
}
