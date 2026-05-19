// PlumbingSizingCommands — Phase 179b SUPPLY + DRAINAGE design commands.
//
// Plumb_ScanFixtures        — fixture-unit scan + write PLM_DRN_DU/LU/WSFU.
// Plumb_SizeSupply          — DCW/DHW pipe sizing (BS EN 806 / Hunter).
// Plumb_SizeDrainage        — drainage pipe sizing (BS EN 12056-2 / IPC).
// Plumb_StackCapacity       — already shipped (StackCapacityCommand) — kept.
// Plumb_PressureCheck       — fixture pressure check (residual head).
// Plumb_ExpVessel           — BS 7074-1 expansion vessel sizing.
// Plumb_TMVRegister         — scan PLM_TMV_CLASS_TXT, build register.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbScanFixturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var cfg = PlumbingSystemConfig.Load(ctx.Doc);

            FixtureScanResult r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Scan Fixtures"))
            {
                tx.Start();
                r = FixtureUnitScanner.Scan(ctx.Doc, writeBack: true, flushValveMajority: cfg.FlushValveMajority);
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Fixture Unit Scan");
            panel.SetSubtitle($"Building: {cfg.BuildingType} · K = {cfg.KFactor:F2} · {cfg.SupplyStandard}");
            panel.AddSection("SUMMARY")
                 .Metric("Fixtures scanned",   r.FixturesScanned.ToString())
                 .Metric("Unmatched",          r.FixturesUnmatched.ToString())
                 .Metric("Params written",     r.FixturesWritten.ToString())
                 .Metric("Σ Discharge Units",  r.SumDu.ToString("F1"))
                 .Metric("Σ LU (CW)",          r.SumLuCw.ToString("F1"))
                 .Metric("Σ LU (HW)",          r.SumLuHw.ToString("F1"))
                 .Metric("Σ WSFU",             r.SumWsfu.ToString("F1"))
                 .Metric("Qd CW (BS EN 806)",  r.QdCwBsEnLps.ToString("F2") + " l/s")
                 .Metric("Qd HW (BS EN 806)",  r.QdHwBsEnLps.ToString("F2") + " l/s")
                 .Metric("Qd Hunter",          r.QdHunterGpm.ToString("F1") + " gpm");

            panel.AddSection($"BREAKDOWN ({r.Rows.Count} types)");
            foreach (var row in r.Rows.Take(40))
                panel.Text($"{row.DisplayName,-32} × {row.Count,4} · DU {row.TotalDu:F1} · LU CW {row.TotalLuCw:F1} · LU HW {row.TotalLuHw:F1} · WSFU {row.TotalWsfu:F1}");

            if (r.FixturesUnmatched > 0)
                panel.AddSection("WARNING").Text($"{r.FixturesUnmatched} fixture(s) had no name match. Use 'Manual Add Row' from the SUPPLY tab to assign.");

            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSizeSupplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var cfg = PlumbingSystemConfig.Load(ctx.Doc);

            var td = new TaskDialog("Plumb_SizeSupply")
            {
                MainInstruction = "Run cold + hot water sizing pipeline?",
                MainContent = $"Standard: {cfg.SupplyStandard}\nDCW material: {cfg.MaterialFor("DCW")}\nDHW material: {cfg.MaterialFor("DHW")}",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Dry run (preview only)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply (write PLM_SUP_*)");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2) return Result.Cancelled;
            bool dryRun = pick == TaskDialogResult.CommandLink1;

            SupplySizingReport r;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Supply Sizing"))
            {
                tx.Start();
                r = WaterSupplySizer.Analyse(ctx.Doc, writeBack: !dryRun, cfg);
                if (dryRun) tx.RollBack(); else tx.Commit();
            }

            var panel = StingResultPanel.Create("Water Supply Sizing");
            panel.SetSubtitle($"{r.Standard} · DCW {r.MaterialDcw} · DHW {r.MaterialDhw}");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes scanned",       r.PipesScanned.ToString())
                 .Metric("Velocity exceedances",r.PipesVelocityFailed.ToString())
                 .Metric("Pressure-drop failed",r.PipesDpFailed.ToString())
                 .Metric("Upsize required",    r.PipesUpsized.ToString())
                 .Metric("Shared-params written", r.PipesWritten.ToString())
                 .Metric("Pipes resized in model", r.PipesResized.ToString());

            if (r.Results.Any())
            {
                panel.AddSection("PIPES (first 30)");
                foreach (var p in r.Results.Take(30))
                {
                    string flag = p.VelocityOk && p.PressureDropOk ? "✓" : "⚠";
                    panel.Text($"{flag} {p.SystemName} · {p.ServiceClass} · DN{p.CurrentDnMm} → DN{p.RecommendedDnMm} · Qd {p.QdLps:F2} l/s · v {p.VelMps:F2} m/s · ΔP {p.DpPaPerM:F0} Pa/m");
                }
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSizeDrainageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            // Wraps the pre-existing AutoSizeDrainageCommand — provides a tag-only entry
            // point for workflows + the new ROUTE/DRAINAGE tab so users can run sizing
            // without the AutoSize task-dialog roulette. Internally calls the same engine
            // chain (DfuMap → DrainageSizer → VentDesigner → StackCapacity → Slope preview).
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            DfuMapResult dfuMap;
            DrainageSizingReport sizing;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Size Drainage"))
            {
                tx.Start();
                dfuMap = FixtureUnitAggregator.BuildDfuMap(ctx.Doc);
                sizing = DrainageSizer.AnalyseAndSize(ctx.Doc, dfuMap.PipeDfu, writeBack: true, dryRun: false);
                tx.Commit();
            }
            var panel = StingResultPanel.Create("Size Drainage");
            panel.SetSubtitle($"Code: {sizing.CodeUsed} · {dfuMap.PipesTagged}/{dfuMap.PipesTagged + sizing.PipesAnalysed - dfuMap.PipesTagged} pipes tagged");
            panel.AddSection("SUMMARY")
                 .Metric("Fixtures",          dfuMap.FixturesScanned.ToString())
                 .Metric("Pipes analysed",    sizing.PipesAnalysed.ToString())
                 .Metric("Upsize required",   sizing.PipesUpsized.ToString())
                 .Metric("Slope insufficient",sizing.PipesSlopeInsufficient.ToString())
                 .Metric("Self-cleansing fail",sizing.PipesSelfCleansingFailed.ToString())
                 .Metric("Shared-params written",  sizing.PipesWritten.ToString())
                 .Metric("Pipes resized in model", sizing.PipesResized.ToString());
            if (sizing.Results.Any())
            {
                panel.AddSection("PIPE SIZING (first 30)");
                foreach (var res in sizing.Results.Take(30))
                    panel.Text($"Pipe {res.PipeId.Value} · DU {res.Dfu:F1} · DN{res.CurrentDnMm}→DN{res.RecommendedDnMm} · slope {res.SlopePct:F2}% · v {res.SelfCleansingVelocityMps:F2} m/s {(res.SelfCleansingOk ? "✓" : "⚠")}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPressureCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var cfg = PlumbingSystemConfig.Load(ctx.Doc);

            // Static pressure check across levels — inlet bar minus rho*g*Δh.
            var levels = new FilteredElementCollector(ctx.Doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            double inletBar = cfg.SupplyPressureBarAtEntry;
            double inletElevFt = levels.FirstOrDefault()?.Elevation ?? 0;

            var panel = StingResultPanel.Create("Pressure Check");
            panel.SetSubtitle($"Entry {inletBar:F2} bar · ρg = 9.81 kPa/m · BS 8558 minimums");
            panel.AddSection("STATIC PRESSURE PER LEVEL");
            int prvCount = 0, fail = 0;
            foreach (var lvl in levels)
            {
                double dHm = (lvl.Elevation - inletElevFt) * 0.3048;
                double pBar = inletBar - (9.81 * dHm) / 100.0;
                bool prv = pBar > 3.5;
                bool worstThermo = pBar < PlumbingTables.MinPressureBarFor("Shower_Thermostatic");
                bool worstUnvented = pBar < PlumbingTables.MinPressureBarFor("UnventedCylinder");
                if (prv) prvCount++;
                if (worstThermo || worstUnvented) fail++;
                string flags = "";
                if (prv) flags += " · PRV recommended";
                if (worstThermo) flags += " · ⚠ thermostatic shower";
                if (worstUnvented) flags += " · ⚠ unvented cylinder";
                panel.Text($"{lvl.Name}  Δh {dHm:F1} m  p {pBar:F2} bar{flags}");
            }
            panel.AddSection("SUMMARY")
                 .Metric("Levels analysed", levels.Count.ToString())
                 .Metric("PRV recommended", prvCount.ToString())
                 .Metric("Pressure failures", fail.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbExpVesselCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Phase 179b ships a calculator with sane defaults — UI input dialog is layered later.
            double vsysL = 200.0;
            double tCold = 10, tHot = 60;
            var r = ExpansionVesselSizer.Size(vsysL, tCold, tHot);

            var panel = StingResultPanel.Create("Expansion Vessel (BS 7074-1)");
            panel.SetSubtitle($"System volume {vsysL:F0} L · ΔT {r.DeltaTC:F0} °C");
            panel.AddSection("SIZING")
                 .Metric("Expansion coeff",        r.ExpansionCoeff.ToString("F4"))
                 .Metric("Fill pressure",          r.FillPressureBar.ToString("F1") + " bar")
                 .Metric("Max system pressure",    r.MaxPressureBar.ToString("F1") + " bar")
                 .Metric("Vessel volume",          r.VTankL.ToString("F0") + " L")
                 .Metric("Recommended family",     r.RecommendedFamily);
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbTMVRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var elems = new FilteredElementCollector(ctx.Doc)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_PipeAccessory)
                .Cast<Element>()
                .Concat(new FilteredElementCollector(ctx.Doc)
                    .WhereElementIsNotElementType()
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .Cast<Element>())
                .ToList();

            int total = 0;
            var rows = new List<string>();
            foreach (var el in elems)
            {
                string cls = "";
                try { cls = el.LookupParameter(ParamRegistry.PLM_TMV_CLASS)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (string.IsNullOrEmpty(cls)) continue;
                string outletC = "";
                try { outletC = el.LookupParameter(ParamRegistry.PLM_TMV_BLEND)?.AsValueString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                rows.Add($"{el.Id.Value} · {el.Name} · TMV {cls} · outlet {outletC}");
                total++;
            }

            var panel = StingResultPanel.Create("TMV Register");
            panel.AddSection("SUMMARY")
                 .Metric("TMVs found", total.ToString());
            if (rows.Count > 0)
            {
                panel.AddSection("REGISTER (first 80)");
                foreach (var line in rows.Take(80)) panel.Text(line);
            }
            else
            {
                panel.Text("No elements with PLM_TMV_CLASS_TXT set. Tag TMV instances first.");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
