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
using StingTools.UI.Plumbing;

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

            var supplyRows = r.Rows.Select(row => new SupplyFixtureScanRow
            {
                Fixture = row.DisplayName, Count = row.Count,
                LuCw = row.TotalLuCw, LuHw = row.TotalLuHw
            }).ToList();
            var drainageRows = r.Rows.Select(row => new DrainageDuScanRow
            {
                Fixture = row.DisplayName, Count = row.Count,
                DuEach  = row.Count > 0 ? row.TotalDu / row.Count : 0,
                SigmaDu = row.TotalDu
            }).ToList();
            string status = $"Plumbing · {r.FixturesScanned} fixtures · ΣDU {r.SumDu:F1} · ΣWSFU {r.SumWsfu:F1}" +
                            (r.FixturesUnmatched > 0 ? $" · {r.FixturesUnmatched} unmatched" : "");

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetSupplyFixtureScanResult(supplyRows, status);
                inst.SetDrainageDuScanResult(drainageRows, null);
                return Result.Succeeded;
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

            var rows = r.Results.Select(p => new SupplySizingRow
            {
                Section     = $"{p.SystemName} · {p.ServiceClass}",
                SigmaLu     = p.QdLps,
                Dn          = p.RecommendedDnMm,
                VelocityMps = p.VelMps,
                Status      = (p.VelocityOk && p.PressureDropOk ? "OK" : "WARN")
                              + $" · DN{p.CurrentDnMm}→{p.RecommendedDnMm}"
            }).ToList();
            string status = $"Supply · {r.PipesScanned} pipes · {r.PipesUpsized} upsize · "
                          + $"{r.PipesVelocityFailed} v-fail · {r.PipesDpFailed} ΔP-fail"
                          + (dryRun ? " (dry run)" : "");

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetSupplySizingResult(rows, status);
                return Result.Succeeded;
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
                // writeBack=true stamps PLM_DFU_COUNT_INT per pipe.
                dfuMap = FixtureUnitAggregator.BuildDfuMap(ctx.Doc, writeBack: true);
                sizing = DrainageSizer.AnalyseAndSize(ctx.Doc, dfuMap.PipeDfu, writeBack: true, dryRun: false);
                tx.Commit();
            }

            var rows = sizing.Results.Select(res => new DrainageSizingRow
            {
                Pipe        = res.PipeId.Value.ToString(),
                SigmaDu     = res.Dfu,
                Dn          = res.RecommendedDnMm,
                VelocityMps = res.SelfCleansingVelocityMps,
                HdRatio     = 0.0, // not exposed by DrainageSizer; populated when engine surfaces it
                Status      = (res.SelfCleansingOk ? "OK" : "WARN")
                              + $" · DN{res.CurrentDnMm}→{res.RecommendedDnMm}"
                              + $" · slope {res.SlopePct:F2}%"
            }).ToList();
            string status = $"Drainage · {sizing.PipesAnalysed} pipes · {sizing.PipesUpsized} upsize · "
                          + $"{sizing.PipesSlopeInsufficient} slope-fail · {sizing.PipesSelfCleansingFailed} v-fail";

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDrainageSizingResult(rows, status);
                return Result.Succeeded;
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

            int prvCount = 0, fail = 0;
            var lines = new List<string>();
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
                lines.Add($"{lvl.Name}  Δh {dHm:F1} m  p {pBar:F2} bar{flags}");
            }
            string status = $"Pressure · {levels.Count} levels · entry {inletBar:F2} bar · "
                          + $"{prvCount} PRV · {fail} fail";
            var inst = StingPlumbingPanel.Instance;
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

            var panel = StingResultPanel.Create("Pressure Check");
            panel.SetSubtitle($"Entry {inletBar:F2} bar · ρg = 9.81 kPa/m · BS 8558 minimums");
            panel.AddSection("STATIC PRESSURE PER LEVEL");
            foreach (var line in lines) panel.Text(line);
            panel.AddSection("SUMMARY")
                 .Metric("Levels analysed", levels.Count.ToString())
                 .Metric("PRV recommended", prvCount.ToString())
                 .Metric("Pressure failures", fail.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbExpVesselCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // System volume + temperatures are project-specific. Read them from
            // ProjectInformation when bound (PLM_EXPVSL_SYS_VOL_L /
            // PLM_EXPVSL_TCOLD_C / PLM_EXPVSL_THOT_C), otherwise fall back to
            // BS 7074-1 defaults for an indirect DHW system (200 L, 10→60°C)
            // and prompt the user so they know the values are defaults.
            double vsysL = 200.0;
            double tCold = 10, tHot = 60;
            try
            {
                var pi = ctx.Doc?.ProjectInformation;
                if (pi != null)
                {
                    double v = ReadProjDouble(pi, "PLM_EXPVSL_SYS_VOL_L");
                    if (v > 0) vsysL = v;
                    double tc = ReadProjDouble(pi, "PLM_EXPVSL_TCOLD_C");
                    if (tc > 0) tCold = tc;
                    double th = ReadProjDouble(pi, "PLM_EXPVSL_THOT_C");
                    if (th > 0) tHot = th;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExpVessel input read: {ex.Message}"); }

            var confirm = new TaskDialog("STING Expansion Vessel — Inputs")
            {
                MainInstruction = "Confirm sizing inputs",
                MainContent = $"System volume: {vsysL:F0} L\n" +
                              $"Cold fill temperature: {tCold:F0} °C\n" +
                              $"Hot operating temperature: {tHot:F0} °C\n\n" +
                              "These come from ProjectInformation when " +
                              "PLM_EXPVSL_SYS_VOL_L / PLM_EXPVSL_TCOLD_C / " +
                              "PLM_EXPVSL_THOT_C are bound, otherwise from BS 7074-1 " +
                              "defaults. Set those parameters on the project to override.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            var r = ExpansionVesselSizer.Size(vsysL, tCold, tHot);

            string status = $"Exp vessel · V_sys {vsysL:F0} L · ΔT {r.DeltaTC:F0}°C · "
                          + $"V_tank {r.VTankL:F0} L · {r.RecommendedFamily}";
            var inst = StingPlumbingPanel.Instance;
            if (inst != null) { inst.SetStatus(status); return Result.Succeeded; }

            var panel = StingResultPanel.Create("Expansion Vessel (BS 7074-1)");
            panel.SetSubtitle($"System volume {vsysL:F0} L · ΔT {r.DeltaTC:F0} °C");
            panel.AddSection("SIZING")
                 .Metric("Expansion coeff",        r.ExpansionCoeff.ToString("F4"))
                 .Metric("Fill pressure",          r.FillPressureBar.ToString("F1") + " bar")
                 .Metric("Max system pressure",    r.MaxPressureBar.ToString("F1") + " bar")
                 .Metric("Vessel volume",          r.VTankL.ToString("F0") + " L")
                 .Metric("Recommended family",     r.RecommendedFamily);
            panel.Show();

            // Close the calc → model loop: offer to place the recommended
            // vessel FamilyInstance. Mirrors the VentCreationEngine.TryPlaceAav
            // pattern — looks for a loaded family containing "Expansion Vessel"
            // or "EV-" in its name and lets the user pick the placement point.
            var place = new TaskDialog("STING Expansion Vessel — Place?")
            {
                MainInstruction = $"Place {r.RecommendedFamily} now?",
                MainContent = "Pick a point in the active view to place the recommended " +
                              "expansion vessel FamilyInstance, or cancel to keep the " +
                              "sizing report only.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            place.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Place {r.RecommendedFamily}");
            if (place.Show() != TaskDialogResult.CommandLink1)
                return Result.Succeeded;

            try
            {
                var sym = FindExpansionVesselSymbol(ctx.Doc, r.VTankL);
                if (sym == null)
                {
                    TaskDialog.Show("STING Expansion Vessel",
                        "No expansion vessel family found in the project. " +
                        "Load a family whose name contains 'Expansion Vessel' or 'EV' and retry.");
                    return Result.Succeeded;
                }
                XYZ pt;
                try { pt = ctx.UIDoc.Selection.PickPoint("Pick expansion-vessel location"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                Level level = ResolveNearestLevel(ctx.Doc, pt.Z);
                using (var tx = new Transaction(ctx.Doc, "STING Place Expansion Vessel"))
                {
                    tx.Start();
                    if (!sym.IsActive) sym.Activate();
                    var fi = ctx.Doc.Create.NewFamilyInstance(pt, sym, level,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    // Stamp the sized vessel volume + a design-intent note onto
                    // the placed instance so a schedule can render the sizing
                    // outcome alongside the placement.
                    ParameterHelpers.SetString(fi, ParamRegistry.PLM_EXPVSL_SZ,
                        ((int)Math.Round(r.VTankL)).ToString(), overwrite: false);
                    ParameterHelpers.SetString(fi, "Comments",
                        $"STING auto-placed · {r.RecommendedFamily} · sized for {r.SystemVolumeL:F0} L sys @ ΔT {r.DeltaTC:F0}°C", overwrite: false);
                    tx.Commit();
                }
                TaskDialog.Show("STING Expansion Vessel",
                    $"Placed {sym.Family.Name} : {sym.Name} at {pt.X:F1},{pt.Y:F1},{pt.Z:F1}.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExpVessel place: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        private static FamilySymbol FindExpansionVesselSymbol(Document doc, double vTankL)
        {
            // Prefer a symbol whose name includes the recommended litre size;
            // otherwise pick the first matching expansion-vessel family.
            string targetSize = $"EV-{(int)vTankL}L";
            FamilySymbol exact = null, partial = null;
            foreach (var fs in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>())
            {
                string n = ((fs.Family?.Name ?? "") + " " + (fs.Name ?? "")).ToUpperInvariant();
                bool isVessel = n.Contains("EXPANSION VESSEL") || n.Contains("EV-")
                             || (n.StartsWith("EV") && n.Contains("L"));
                if (!isVessel) continue;
                if (n.Contains(targetSize.ToUpperInvariant())) { exact = fs; break; }
                partial = partial ?? fs;
            }
            return exact ?? partial;
        }

        private static Level ResolveNearestLevel(Document doc, double zFt)
        {
            try
            {
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                Level best = levels.FirstOrDefault();
                foreach (var l in levels)
                {
                    if (l.Elevation <= zFt) best = l;
                    else break;
                }
                return best;
            }
            catch { return null; }
        }

        private static double ReadProjDouble(Element pi, string paramName)
        {
            try
            {
                var p = pi.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var v)) return v;
            }
            catch { }
            return 0;
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

            var rows = new List<SupplyTmvRow>();
            var lines = new List<string>();
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
                 .Metric("TMVs found", rows.Count.ToString());
            if (lines.Count > 0)
            {
                panel.AddSection("REGISTER (first 80)");
                foreach (var line in lines.Take(80)) panel.Text(line);
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
