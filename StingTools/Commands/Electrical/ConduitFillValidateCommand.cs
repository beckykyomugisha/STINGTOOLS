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
using ConduitFillData = StingTools.UI.ConduitFillData;

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
                                try { activeView.SetElementOverrides(el.Id, ogsRed); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                            if (pct > worstFill) { worstFill = pct; worstName = el.Name ?? ""; }
                        }
                    }
                    catch (Exception ex2) { StingLog.Warn($"Fill compute: {ex2.Message}"); }
                }
                tx.Commit();
            }
            StingElectricalCommandHandler.LastConduitFills = results;
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            string worstStr = string.IsNullOrEmpty(worstName) ? "—" : $"{worstName} ({worstFill:0.0}%)";
            TaskDialog.Show("STING Conduit Fill",
                $"Checked {results.Count} containment element(s). Passing: {passed}. Failing: {failed}.\nWorst: {worstStr}.");

            // --- Iterative auto-size: upsize conduits that fail fill limit ---
            if (failed > 0)
            {
                var dlg2 = new TaskDialog("STING Conduit Fill — Auto-Size")
                {
                    MainContent = $"{failed} conduit(s) exceed fill limits. Auto-upsize to the next standard size?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };
                if (dlg2.Show() == TaskDialogResult.Yes)
                {
                    int upsized = 0;
                    using (var txUp = new Transaction(doc, "STING Conduit Auto-Upsize"))
                    {
                        txUp.Start();
                        foreach (var r in results.Where(r2 => !r2.Passes))
                        {
                            try
                            {
                                var el = doc.GetElement(r.ConduitId);
                                if (el == null) continue;
                                // Read current diameter
                                double curDiaMm = 0;
                                try
                                {
                                    var diaP = el.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                                    if (diaP != null) curDiaMm = diaP.AsDouble() * 304.8; // ft → mm
                                }
                                catch { }
                                // Find next standard size up
                                double[] stdSizes = { 16, 20, 25, 32, 40, 50, 63 };
                                double nextSize = stdSizes.FirstOrDefault(s => s > curDiaMm + 0.5);
                                if (nextSize <= 0) continue;
                                try
                                {
                                    var diaP = el.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                                    if (diaP != null && !diaP.IsReadOnly)
                                    {
                                        diaP.Set(nextSize / 304.8); // mm → ft
                                        upsized++;
                                    }
                                }
                                catch (Exception ex2) { StingLog.Warn($"Upsize {r.ConduitId}: {ex2.Message}"); }
                            }
                            catch (Exception ex2) { StingLog.Warn($"AutoUpsize loop: {ex2.Message}"); }
                        }
                        txUp.Commit();
                    }

                    // Re-validate after upsize
                    if (upsized > 0)
                    {
                        int passedAfter = 0, failedAfter = 0;
                        using (var txRecheck = new Transaction(doc, "STING Fill Re-Check"))
                        {
                            txRecheck.Start();
                            foreach (var r in results)
                            {
                                try
                                {
                                    var el = doc.GetElement(r.ConduitId);
                                    if (el == null) continue;
                                    var report2 = TrayFillCalculator.Compute(doc, el, manifest);
                                    r.FillPct = report2.FillRatio * 100.0;
                                    r.Passes  = report2.PassesLimit;
                                    ParameterHelpers.SetString(el, ParamRegistry.ELC_CONDUIT_FILL_PCT,
                                        $"{r.FillPct:0.0}", overwrite: true);
                                    if (r.Passes) passedAfter++;
                                    else failedAfter++;
                                }
                                catch { }
                            }
                            txRecheck.Commit();
                        }
                        TaskDialog.Show("STING Auto-Upsize",
                            $"Upsized {upsized} conduit(s).\nNow passing: {passedAfter}  |  Still failing: {failedAfter}");
                    }
                }
            }

            // Push conduit fill compliance to server snapshot
            try
            {
                int overFillCount = results.Count(r => !r.Passes);
                double avgFill = results.Count > 0 ? results.Average(r => r.FillPct) : 0;
                StingLog.Info($"ConduitFill snapshot: checked={results.Count}, overFill={overFillCount}, avgFill={avgFill:0.1}%");
                // Additionally write a summary parameter to ProjectInformation if it's writable
                var projInfo = doc.ProjectInformation;
                if (projInfo != null)
                {
                    var summP = projInfo.LookupParameter("STING_CONDUIT_FILL_SUMMARY_TXT");
                    if (summP != null && !summP.IsReadOnly)
                    {
                        using (var txSumm = new Transaction(doc, "STING Conduit Fill Summary"))
                        {
                            txSumm.Start();
                            summP.Set($"Checked:{results.Count} Fail:{overFillCount} Avg:{avgFill:0.1}% @ {DateTime.Now:yyyy-MM-dd HH:mm}");
                            txSumm.Commit();
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ConduitFill snapshot push: {ex.Message}"); }

            return Result.Succeeded;
        }
    }
}
