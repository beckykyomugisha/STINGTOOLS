// Plumbing commands suite — 10 IExternalCommand classes.
// Phase 178c.
//
// Tags (used by StingCommandHandler dispatch + dock-panel buttons):
//   Plumbing_AutoSizeDrainage   — DFU sizing + slope correct + vent design
//   Plumbing_BackflowAudit      — BS EN 1717 fluid category audit
//   Plumbing_RainwaterCalc      — RWH yield + SuDS + soakaway + septic
//   Plumbing_TrapVentAudit      — trap type + seal + vent DN audit
//   Plumbing_PRVSchedule        — pressure zone + PRV set-point
//   Plumbing_DeadLegScan        — HSG 274 dead-leg detector
//   Plumbing_CrossConnection    — potable / non-potable cross-conn graph
//   Plumbing_RecircBalance      — DHW recirc heat-loss + DRV pre-set
//   Plumbing_StackCapacity      — BS EN 12056-2 stack DU capacity check
//   Plumbing_MaterialAudit      — material × jointing × service compat

using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.UI.Plumbing;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSizeDrainageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var td = new TaskDialog("STING Plumbing — Auto-Size Drainage")
            {
                MainInstruction = "Run drainage auto-sizing pipeline?",
                MainContent = "Builds DFU map, sizes pipes (BS EN 12056-2 / IPC 2021), evaluates self-cleansing velocity, designs vents, and previews slope corrections.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Dry run (preview only)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply (writeback)");
            var pick = td.Show();
            bool dryRun = pick == TaskDialogResult.CommandLink1;
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2) return Result.Cancelled;

            DfuMapResult dfuMap;
            DrainageSizingReport sizing;
            using (var tx = new Transaction(doc, "STING v4 Plumbing AutoSize"))
            {
                tx.Start();
                // writeBack=true stamps PLM_DFU_COUNT_INT per pipe (existing param);
                // downstream sizers + paragraph builders can read it without
                // re-walking the connector graph.
                dfuMap = FixtureUnitAggregator.BuildDfuMap(doc, writeBack: !dryRun);
                sizing = DrainageSizer.AnalyseAndSize(doc, dfuMap.PipeDfu, writeBack: !dryRun, dryRun);
                tx.Commit();
            }

            var vents = VentDesigner.DesignVents(doc, dfuMap.PipeDfu);
            var stackReport = StackCapacityValidator.Validate(doc, dfuMap);

            if (!dryRun)
            {
                var preview = SlopeAutoCorrector.Preview(doc);
                if (preview.Fixes.Count > 0)
                {
                    var decision = SlopeFixPreviewDialog.Show(preview);
                    if (decision.Decision == SlopeFixDecision.ApplyAll)
                        SlopeAutoCorrector.RunFix(doc, dryRun: false);
                }
            }

            var panel = StingResultPanel.Create("Auto-Size Drainage");
            panel.SetSubtitle($"Code: {sizing.CodeUsed} · {dfuMap.PipesTagged} pipes tagged from {dfuMap.FixturesScanned} fixtures");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes analysed",        sizing.PipesAnalysed.ToString())
                 .Metric("Upsize required",       sizing.PipesUpsized.ToString())
                 .Metric("Slope insufficient",    sizing.PipesSlopeInsufficient.ToString())
                 .Metric("Velocity < 0.7 m/s",    sizing.PipesSelfCleansingFailed.ToString())
                 .Metric("Shared-params written", sizing.PipesWritten.ToString())
                 .Metric("Pipes resized in model",sizing.PipesResized.ToString())
                 .Metric("Vent records",          vents.Count.ToString())
                 .Metric("Stacks flagged",        stackReport.StacksFlagged.ToString());

            if (sizing.Results.Any())
            {
                panel.AddSection("PIPE SIZING (first 30)");
                foreach (var res in sizing.Results.Take(30))
                    panel.Text($"Pipe {res.PipeId.Value} · DFU {res.Dfu:F1} · current DN{res.CurrentDnMm} → recommend DN{res.RecommendedDnMm} · slope {res.SlopePct:F2}% · v {res.SelfCleansingVelocityMps:F2} m/s {(res.SelfCleansingOk ? "✓" : "⚠")}");
            }
            if (vents.Any())
            {
                panel.AddSection("VENT REQUIREMENTS (first 20)");
                foreach (var v in vents.Take(20))
                    panel.Text($"Drain {v.DrainPipeId.Value} DN{v.DrainDnMm} · DU {v.Dfu:F1} → vent DN{v.RecommendedVentDnMm} · max {v.MaxVentLengthM:F1} m {(v.RequiresAav ? "· AAV" : "")} {(v.RequiresReliefVent ? "· RELIEF" : "")}");
            }
            if (stackReport.Findings.Any())
            {
                panel.AddSection("STACK CAPACITY");
                foreach (var f in stackReport.Findings.OrderByDescending(x => x.UtilisationPct).Take(20))
                    panel.Text($"[{f.Severity}] Stack {f.StackPipeId.Value} DN{f.DnMm} · DU {f.Dfu:F1} / cap {f.CapacityDu:F1} ({f.UtilisationPct:F0} %)");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BackflowAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var classified = BackflowClassifier.ClassifyAll(ctx.Doc);
            var crossConn  = CrossConnectionChecker.Scan(ctx.Doc);

            // Close the calc → model loop: stamp PLM_FLUID_CATEGORY_TXT +
            // PLM_VLV_BACKFLOW_TYPE_TXT on every classified pipe so schedules
            // and BOQ paragraph builders see the result without re-running.
            int stamped = 0;
            using (var tx = new Transaction(ctx.Doc, "STING Backflow Audit — stamp"))
            {
                tx.Start();
                stamped = BackflowClassifier.WriteBack(ctx.Doc, classified);
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Backflow Audit (BS EN 1717)");
            panel.AddSection("CATEGORY DISTRIBUTION");
            foreach (var grp in classified.GroupBy(x => x.Category).OrderBy(g => g.Key))
                panel.Metric($"Category {(int)grp.Key}", grp.Count().ToString());
            panel.Metric("Params stamped", stamped.ToString());
            if (crossConn.Any())
            {
                panel.AddSection($"CROSS-CONNECTION FINDINGS ({crossConn.Count})");
                foreach (var c in crossConn.OrderByDescending(x => x.NonPotableCategory).Take(50))
                    panel.Text($"[{c.Severity}] Potable {c.PotableElementId.Value} ↔ Cat-{(int)c.NonPotableCategory} {c.NonPotableElementId.Value}: {c.Notes}");
            }
            panel.AddSection("DEVICE GUIDANCE")
                 .Text("Cat-2 → SCV (single check valve)")
                 .Text("Cat-3 → DCV (double check valve)")
                 .Text("Cat-4 → RPZ (Type BA)")
                 .Text("Cat-5 → Air gap (Type AA / AB) — mandatory");
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RainwaterCalcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            // First commit ships defaults; user-input dialog is Phase 178d.
            var rwh = RainwaterHarvestingCalc.Calculate(
                roofAreaM2: 500, annualRainfallMm: 800, runoffCoefficient: 0.75,
                filterEfficiency: 0.90, dailyDemandM3: 1.5);
            double sudsM3 = RainwaterHarvestingCalc.CalcSudsAttenuationVolumeM3(
                postDevAreaM2: 5000, preDevGreenAreaM2: 5000,
                rainfallIntensityMmHr: 25, stormDurationHr: 1.0,
                postDevCv: 0.9, preDevCv: 0.05, climateUpliftPct: 40);
            double soakM3 = RainwaterHarvestingCalc.CalcSoakawayVolumeM3(
                catchmentAreaM2: 200, rainfallIntensityMHr: 0.025,
                stormDurationHr: 1.0, infiltrationRateMHr: 0.05);
            double septicL = RainwaterHarvestingCalc.CalcSepticTankVolumeLitres(populationEquivalent: 6);

            var panel = StingResultPanel.Create("Rainwater / SuDS / Soakaway / Septic");
            panel.SetSubtitle("Defaults: 500 m² roof · 800 mm/yr rain · 1.5 m³/day non-potable demand");
            panel.AddSection("RWH (BS 8515)")
                 .Metric("Annual catch (m³)",   rwh.AnnualRainfallM3.ToString("F1"))
                 .Metric("Annual demand (m³)",  rwh.AnnualDemandM3.ToString("F1"))
                 .Metric("Annual yield (m³)",   rwh.AnnualYieldM3.ToString("F1"))
                 .Metric("Yield efficiency %",  rwh.YieldEfficiencyPct.ToString("F0"))
                 .Metric("Recommended tank m³", rwh.RecommendedTankM3.ToString("F2"));
            panel.AddSection("SuDS attenuation (CIRIA C753)")
                 .Metric("Volume (m³)", sudsM3.ToString("F1"));
            panel.AddSection("Soakaway (BRE 365)")
                 .Metric("Volume (m³)", soakM3.ToString("F2"));
            panel.AddSection("Septic tank (BS EN 12566-1)")
                 .Metric("Primary volume (l)", septicL.ToString("F0"));
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrapAndVentAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var fixtures = new FilteredElementCollector(ctx.Doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType().ToElements();

            int matches = 0, mismatches = 0, missing = 0;
            var panel = StingResultPanel.Create("Trap & Vent Audit");
            panel.AddSection("FIXTURES (first 50)");
            foreach (var el in fixtures.Take(50))
            {
                var sel = TrapDesigner.SelectTrap(el);
                string current = "";
                try { current = el.LookupParameter(ParamRegistry.PLM_TRAP_TYPE)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                bool match = string.Equals(current, sel.TrapType, StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(current)) { missing++; }
                else if (match)                    { matches++; }
                else                                { mismatches++; }
                panel.Text($"{el.Id.Value} {el.Name} → trap {sel.TrapType} seal {sel.SealDepthMm} mm · current '{current}' {(string.IsNullOrEmpty(current) ? "✗" : match ? "✓" : "⚠")}");
            }
            panel.AddSection("SUMMARY")
                 .Metric("Fixtures scanned", fixtures.Count.ToString())
                 .Metric("Matches",          matches.ToString())
                 .Metric("Mismatches",       mismatches.ToString())
                 .Metric("Missing",          missing.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PRVScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            double inletKpa = 400.0;
            const double rho = 1000, g = 9.81;
            double inletElevFt = levels.FirstOrDefault()?.Elevation ?? 0;

            var panel = StingResultPanel.Create("Pressure Zone / PRV Schedule");
            panel.SetSubtitle("Static pressure per level · 500 kPa Approved Doc G ceiling");
            panel.AddSection("PER LEVEL");
            int prvCount = 0;
            foreach (var lvl in levels)
            {
                double dHm = (lvl.Elevation - inletElevFt) * 0.3048;
                double pStaticKpa = inletKpa - rho * g * dHm / 1000.0;
                bool prv = pStaticKpa > 500;
                if (prv) prvCount++;
                string zone = pStaticKpa > 500 ? "BOOSTED" :
                              pStaticKpa > 350 ? "HIGH"    :
                              pStaticKpa > 200 ? "MID"     : "LOW";
                panel.Text($"{lvl.Name} (Δh {dHm:F1} m) · static {pStaticKpa:F0} kPa · zone {zone} {(prv ? "· PRV required" : "")}");
            }
            panel.AddSection("SUMMARY")
                 .Metric("Levels analysed", levels.Count.ToString())
                 .Metric("PRV recommendations", prvCount.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeadLegScanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            DeadLegResult r;
            using (var tx = new Transaction(ctx.Doc, "STING v4 Plumbing DeadLegScan"))
            {
                tx.Start();
                r = DeadLegDetector.Scan(ctx.Doc, writeBack: true);
                tx.Commit();
            }
            var panel = StingResultPanel.Create("Dead-Leg Scan (HSG 274)");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes scanned", r.PipesScanned.ToString())
                 .Metric("Legs flagged",  r.LegsFlagged.ToString())
                 .Metric("Pipes written", r.PipesWritten.ToString());
            if (r.Findings.Any())
            {
                panel.AddSection("FINDINGS (first 50)");
                foreach (var f in r.Findings.OrderByDescending(x => x.LegLengthM).Take(50))
                    panel.Text($"[{f.Severity}] Pipe {f.TerminalPipeId.Value} ({f.SystemName}) · leg {f.LegLengthM:F1} m · DN{f.LegPipeDiameterMm:F0} — {f.Notes}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CrossConnectionScanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var findings = CrossConnectionChecker.Scan(ctx.Doc);
            var panel = StingResultPanel.Create("Cross-Connection Scan (BS EN 1717)");
            panel.AddSection("SUMMARY")
                 .Metric("Findings",   findings.Count.ToString())
                 .Metric("CRITICAL",   findings.Count(f => f.Severity == "CRITICAL").ToString())
                 .Metric("ERROR",      findings.Count(f => f.Severity == "ERROR").ToString());
            if (findings.Any())
            {
                panel.AddSection("FINDINGS");
                foreach (var f in findings.OrderByDescending(x => x.NonPotableCategory).Take(80))
                    panel.Text($"[{f.Severity}] Potable {f.PotableElementId.Value} ↔ Cat-{(int)f.NonPotableCategory} {f.NonPotableElementId.Value}: {f.Notes}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RecircBalanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            RecircLoopReport r;
            using (var tx = new Transaction(ctx.Doc, "STING Recirc Loop Balance"))
            {
                tx.Start();
                r = RecircLoopBalancer.Analyse(ctx.Doc, systemNameFilter: null, writeBack: true);
                tx.Commit();
            }

            var panel = StingResultPanel.Create("DHW Recirculation Loop Balance");
            panel.SetSubtitle(string.IsNullOrEmpty(r.SystemName) ? "(no recirc system found)" : $"System: {r.SystemName}");
            panel.AddSection("LOOP")
                 .Metric("Total heat loss (W)", r.TotalHeatLossW.ToString("F0"))
                 .Metric("Pump duty (l/min)",   r.PumpDutyLpm.ToString("F1"))
                 .Metric("Branches",            r.Branches.Count.ToString())
                 .Metric("PLM_RECIRC_* stamped", r.BranchesStamped.ToString());
            if (r.Branches.Any())
            {
                panel.AddSection("DRV PRE-SETS (first 40)");
                foreach (var b in r.Branches.Take(40))
                    panel.Text($"Pipe {b.PipeId.Value} · {b.LengthM:F1} m DN{b.DiameterMm:F0} · Q {b.HeatLossW:F0} W · flow {b.FlowLpm:F2} l/min · kv {b.DrvPresetKv:F2}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StackCapacityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            StackCapacityReport rep;
            using (var tx = new Transaction(ctx.Doc, "STING Stack Capacity"))
            {
                tx.Start();
                // Stamp PLM_DFU_COUNT_INT per pipe too while we have the
                // transaction open — saves a second walk if the user runs
                // Auto-Size Drainage next.
                var dfu = FixtureUnitAggregator.BuildDfuMap(ctx.Doc, writeBack: true);
                rep = StackCapacityValidator.Validate(ctx.Doc, dfu, writeBack: true);
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Stack Capacity (BS EN 12056-2 Table 11)");
            panel.AddSection("SUMMARY")
                 .Metric("Stacks scanned",     rep.StacksScanned.ToString())
                 .Metric("Stacks flagged",     rep.StacksFlagged.ToString())
                 .Metric("Stacks over capacity", rep.StacksOverCapacity.ToString())
                 .Metric("PLM_STACK_* stamped",  rep.StacksStamped.ToString());
            if (rep.Findings.Any())
            {
                panel.AddSection("FINDINGS");
                foreach (var f in rep.Findings.OrderByDescending(x => x.UtilisationPct).Take(50))
                    panel.Text($"[{f.Severity}] Stack {f.StackPipeId.Value} DN{f.DnMm} · DU {f.Dfu:F1} / {f.CapacityDu:F1} ({f.UtilisationPct:F0} %)");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var rep = PlumbingMaterialValidator.Validate(ctx.Doc);
            var panel = StingResultPanel.Create("Plumbing Material & Jointing Audit");
            panel.SetSubtitle($"Rules: {rep.RulesSource}");
            panel.AddSection("SUMMARY")
                 .Metric("Elements scanned", rep.ElementsScanned.ToString())
                 .Metric("Findings",         rep.Findings.Count.ToString())
                 .Metric("CRITICAL",         rep.Findings.Count(f => f.Severity == "CRITICAL").ToString())
                 .Metric("ERROR",            rep.Findings.Count(f => f.Severity == "ERROR").ToString())
                 .Metric("WARN",             rep.Findings.Count(f => f.Severity == "WARN").ToString());
            if (rep.Findings.Any())
            {
                panel.AddSection("FINDINGS (first 80)");
                foreach (var f in rep.Findings.OrderByDescending(x => x.Severity).Take(80))
                    panel.Text($"[{f.Severity}] {f.ElementId.Value} · {f.Kind} · mat={f.Material} joint={f.Joint} svc={f.Service} — {f.Notes}");
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
