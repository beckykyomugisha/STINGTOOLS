// STING Tools — Phase 113: MEP Design Extensions (MEP-A-01..12).

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.Standards;
using StingTools.UI;

namespace StingTools.Commands.MepDesign
{
    internal static class MepPanel
    {
        public static StingResultPanel.Builder Build(string title, string subtitle)
            => StingResultPanel.Create(title).SetSubtitle(subtitle);
    }

    // MEP-A-01 — Cable sizing auto-apply to every circuit
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class CableSizeApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            var doc = ctx.Doc;

            if (!NumericPrompt.TryAsk("MEP-A-01 Cable size apply (project-wide)",
                new[] { "Default length (m)", "Default ambient °C", "Conduit fill" },
                new[] { 30.0,                   30.0,                  3.0 }, out var v)) return Result.Cancelled;

            int inspected = 0, sized = 0, skipped = 0;
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "STING MEP-A-01 cable size apply"))
            {
                try { tx.Start(); } catch (Exception ex) { warnings.Add($"tx: {ex.Message}"); goto Done; }
                try
                {
                    foreach (var el in new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_ElectricalCircuit).WhereElementIsNotElementType())
                    {
                        inspected++;
                        try
                        {
                            // Read voltage + apparent current by name — RBS_ELEC_APPARENT_LOAD_A
                            // is not a valid BuiltInParameter enum member across all Revit
                            // versions; use LookupParameter with common display names + a
                            // BuiltInParameter fallback for voltage.
                            double voltageV = ReadBip(el, BuiltInParameter.RBS_ELEC_VOLTAGE);
                            double currentA = ReadNamed(el, new[] { "Apparent Current", "Current", "Total Installed Current" });
                            if (voltageV <= 0 || currentA <= 0) { skipped++; continue; }

                            // Region-aware: BS 7671 / IEC 60364 / NEC 310 driven by the active project region.
                            string elecStd = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
                            var res = StingTools.Standards.StandardsAPI.CalculateCableSize(
                                voltageV: voltageV, currentA: currentA, lengthM: v[0],
                                conductorType: "Copper", insulationType: "THHN",
                                conduitFill: (int)v[2], ambientTempC: v[1], standard: elecStd);
                            if (!res.Success || string.IsNullOrEmpty(res.SizeAWG)) { skipped++; continue; }

                            var p = el.LookupParameter("CABLE_SIZE") ??
                                    el.LookupParameter("ELC_CBL_SIZE_TXT");
                            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                            {
                                p.Set(res.SizeAWG);
                                sized++;
                            }
                            else skipped++;
                        }
                        catch (Exception ex2)
                        {
                            skipped++;
                            warnings.Add($"circuit {el?.Id}: {ex2.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    warnings.Add($"fatal: {ex.Message}");
                }
            }
        Done:
            var panel = MepPanel.Build("MEP-A-01 Cable size apply", "BS 7671 / IEC 60364 / NEC — whole project")
                .AddSection("RESULT")
                .Metric("Circuits inspected", inspected.ToString())
                .Metric("Cables sized + written", sized.ToString())
                .Metric("Skipped (missing V/A or param)", skipped.ToString());
            if (warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in warnings.GetRange(0, Math.Min(30, warnings.Count))) panel.Text(w);
                if (warnings.Count > 30) panel.Text($"(+{warnings.Count - 30} more)");
            }
            panel.Show();
            return Result.Succeeded;
        }

        private static double ReadBip(Element el, BuiltInParameter bip)
        {
            try { var p = el?.get_Parameter(bip);
                  if (p == null) return 0;
                  if (p.StorageType == StorageType.Double) return p.AsDouble();
                  if (p.StorageType == StorageType.Integer) return p.AsInteger();
                  if (p.StorageType == StorageType.String &&
                      double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v; }
            catch (Exception ex) { StingLog.Warn($"ReadBip: {ex.Message}"); }
            return 0;
        }

        private static double ReadNamed(Element el, string[] names)
        {
            if (el == null) return 0;
            foreach (var n in names)
            {
                try { var p = el.LookupParameter(n);
                      if (p == null) continue;
                      if (p.StorageType == StorageType.Double) return p.AsDouble();
                      if (p.StorageType == StorageType.Integer) return p.AsInteger();
                      if (p.StorageType == StorageType.String &&
                          double.TryParse(p.AsString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double v)) return v; }
                catch (Exception ex) { StingLog.Warn($"ReadNamed '{n}': {ex.Message}"); }
            }
            return 0;
        }
    }

    // MEP-A-02 — Panel schedule builder
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class PanelScheduleBuildCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-02 Panel schedule build",
                new[] { "Panel voltage (V)", "Number of circuits", "Total connected load (kW)", "Demand factor" },
                new[] { 415.0, 24.0, 35.0, 0.80 }, out var v)) return Result.Cancelled;
            double demandKW = v[2] * v[3];
            double demandA  = demandKW * 1000 / (v[0] * 1.732);
            MepPanel.Build("MEP-A-02 Panel schedule", "BS 7671 + BS EN 60439 / NEC 408")
                .AddSection("DEMAND ANALYSIS")
                .Metric("Connected kW",  $"{v[2]:F1}")
                .Metric("Demand factor",  $"{v[3]:F2}")
                .Metric("Demand kW",      $"{demandKW:F1}")
                .Metric("Demand A (3φ)",  $"{demandA:F1} A")
                .Metric("Main breaker rec.", $"{Math.Ceiling(demandA * 1.25 / 10) * 10:F0} A")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-03 — Breaker auto-size
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class BreakerAutoSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-03 Breaker size (IEC 60947-2)",
                new[] { "Circuit load (A)", "Cable ampacity (A)", "Margin %" },
                new[] { 40.0, 63.0, 125.0 }, out var v)) return Result.Cancelled;
            double minRating = v[0] * v[2] / 100.0;
            double maxRating = v[1];
            int[] std = { 6,10,16,20,25,32,40,50,63,80,100,125,160,200,250,320,400,500,630 };
            int selected = 6;
            foreach (var s in std) if (s >= minRating && s <= maxRating) { selected = s; break; }
            MepPanel.Build("MEP-A-03 Breaker auto-size", "IEC 60947-2 / NEC 240")
                .AddSection("SELECTION")
                .Metric("Circuit load",  $"{v[0]:F0} A")
                .Metric("Min rating (125%)", $"{minRating:F0} A")
                .Metric("Cable ampacity cap", $"{maxRating:F0} A")
                .Metric("Selected breaker",   $"{selected} A")
                .Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Commands.MepDesign
{
    // MEP-A-04 — Auto-size every conduit in project
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class AutoSizeConduitAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            // Dispatch the existing Phase 109 Mep_AutoSizeConduit but in a loop
            // across every circuit. For now, delegate to the Phase 109 command
            // which already iterates the whole project.
            return new Mep.MepAutoSizeConduitCommand().Execute(cd, ref message, elements);
        }
    }

    // MEP-A-05 — Grounding design
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class GroundingDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-05 Grounding (BS 7671 + IEEE 142)",
                new[] { "Max fault current (kA)", "Clearing time (s)", "Soil ρ (Ω·m)" },
                new[] { 10.0, 0.4, 100.0 }, out var v)) return Result.Cancelled;
            double cpcMm2 = v[0] * 1000 * Math.Sqrt(v[1]) / 143;
            int[] std = { 2,4,6,10,16,25,35,50,70,95,120,150,185,240,300,400 };
            int selected = 400;
            foreach (var s in std) if (s >= cpcMm2) { selected = s; break; }
            MepPanel.Build("MEP-A-05 Grounding", "BS 7671 542 + IEEE 142")
                .AddSection("SIZING")
                .Metric("Calculated CPC",   $"{cpcMm2:F1} mm²")
                .Metric("Standard size",    $"{selected} mm²")
                .Metric("Soil resistivity", $"{v[2]:F0} Ω·m")
                .Metric("Earth electrode",  v[2] <= 100 ? "Single rod 1.5m" : v[2] <= 500 ? "3 rods at 3m" : "Ring earth 20m")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-06 — Duct static regain sizing
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class DuctStaticRegainCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-06 Duct static regain",
                new[] { "Upstream velocity (m/s)", "Downstream velocity (m/s)", "R recovery" },
                new[] { 10.0, 6.0, 0.75 }, out var v)) return Result.Cancelled;
            double regainPa = v[2] * 0.5 * 1.2 * (v[0]*v[0] - v[1]*v[1]);
            MepPanel.Build("MEP-A-06 Static regain", "CIBSE Guide B3 / ASHRAE")
                .AddSection("REGAIN")
                .Metric("Upstream V",   $"{v[0]:F1} m/s")
                .Metric("Downstream V", $"{v[1]:F1} m/s")
                .Metric("Regain factor R", $"{v[2]:F2}")
                .Metric("Static regain", $"{regainPa:F0} Pa")
                .Text("Sequential sizing: drop velocity at each branch to recover static pressure, not friction loss.")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-07 — Pump sizing
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PumpSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-07 Pump size (CIBSE Guide C)",
                new[] { "Flow (L/s)", "Head TDH (m)", "Efficiency η", "SG" },
                new[] { 10.0, 25.0, 0.75, 1.0 }, out var v)) return Result.Cancelled;
            double powerKW = v[0] * v[1] * v[3] * 9.81 / (1000 * v[2]);
            MepPanel.Build("MEP-A-07 Pump size", "CIBSE Guide C")
                .AddSection("HYDRAULIC")
                .Metric("Flow",       $"{v[0]:F1} L/s")
                .Metric("TDH",        $"{v[1]:F1} m")
                .Metric("η",          $"{v[2]:F2}")
                .Metric("Shaft power", $"{powerKW:F2} kW")
                .Metric("Motor select (×1.25)", $"{powerKW * 1.25:F1} kW")
                .Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Commands.MepDesign
{
    // MEP-A-08 — Transformer size
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class TransformerSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-08 Transformer (IEC 60076)",
                new[] { "Connected load kW", "Power factor", "Future growth %" },
                new[] { 300.0, 0.85, 25.0 }, out var v)) return Result.Cancelled;
            double kva = v[0] / v[1] * (1 + v[2]/100);
            int[] std = { 50, 75, 100, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500 };
            int sel = std[^1];
            foreach (var s in std) if (s >= kva) { sel = s; break; }
            MepPanel.Build("MEP-A-08 Transformer", "IEC 60076 / NEC 450")
                .AddSection("SIZING")
                .Metric("Connected kW",       $"{v[0]:F0}")
                .Metric("Design kVA (w/growth)", $"{kva:F0}")
                .Metric("Standard kVA",         $"{sel}")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-09 — Generator size
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class GeneratorSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-09 Generator",
                new[] { "Block load kVA", "Starting kVA multiplier", "Altitude m" },
                new[] { 500.0, 2.5, 1100.0 }, out var v)) return Result.Cancelled;
            double derate = v[2] > 1000 ? 1 - ((v[2]-1000) * 0.03 / 300.0) : 1.0;
            double kvaReq = v[0] * v[1] / derate;
            MepPanel.Build("MEP-A-09 Generator", "BS 5514 / ISO 8528")
                .AddSection("SIZING")
                .Metric("Block load",      $"{v[0]:F0} kVA")
                .Metric("Start-kVA factor", $"{v[1]:F1}×")
                .Metric("Altitude derate",  $"{derate:F2}")
                .Metric("Required kVA",     $"{kvaReq:F0}")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-10 — Water heater size
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class WaterHeaterSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-10 Water heater (BS 6700 + CIBSE G)",
                new[] { "Peak demand L/hr", "Recovery time min", "ΔT °C" },
                new[] { 400.0, 60.0, 50.0 }, out var v)) return Result.Cancelled;
            double storageL = v[0] * (v[1]/60);
            double kw = storageL * 4.187 * v[2] / (3600 * v[1]/60);
            MepPanel.Build("MEP-A-10 Water heater", "BS 6700 + CIBSE Guide G")
                .AddSection("SIZE")
                .Metric("Storage",      $"{storageL:F0} L")
                .Metric("Recovery kW",  $"{kw:F1} kW")
                .Metric("Common sizes", "100 / 150 / 200 / 300 / 500 L")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-11 — Drainage size
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class DrainageSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("MEP-A-11 Drainage (BS EN 12056)",
                new[] { "Discharge units DU", "Slope %", "System type (1=I, 2=II, 3=III, 4=IV)" },
                new[] { 30.0, 1.5, 1.0 }, out var v)) return Result.Cancelled;
            // BS EN 12056 System I: DU 0-4 → DN50, 4-25 → DN75, 25-60 → DN100, 60-220 → DN125, 220+ → DN150
            int dn = v[0] < 4 ? 50 : v[0] < 25 ? 75 : v[0] < 60 ? 100 : v[0] < 220 ? 125 : 150;
            MepPanel.Build("MEP-A-11 Drainage pipe size", "BS EN 12056-2")
                .AddSection("SELECTION")
                .Metric("DU total",  $"{v[0]:F0}")
                .Metric("Slope",     $"{v[1]:F2}%")
                .Metric("DN select", $"DN {dn}")
                .Metric("Min slope (San)", "1:80 = 1.25%")
                .Show();
            return Result.Succeeded;
        }
    }

    // MEP-A-12 — Balance apply
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class BalanceApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            var doc = ctx.Doc;

            // Collect every duct + pipe with a non-zero RBS flow parameter,
            // build a branch dataset, run MEPBalancingEngine, then write the
            // balanced flow back as the element's "Design flow" override.
            var branches = new List<(string Name, double DesignFlowLs, double ResistanceCoeff, ElementId Id, bool IsDuct)>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType())
                {
                    double cfm = ReadBip(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    if (cfm <= 0) continue;
                    double lps = cfm * 0.4719;
                    branches.Add(($"D{el.Id}", lps, 0.1, el.Id, true));
                }
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType())
                {
                    double lps = ReadBip(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                    if (lps <= 0) continue;
                    branches.Add(($"P{el.Id}", lps, 0.2, el.Id, false));
                }
            }
            catch (Exception ex) { StingLog.Warn($"BalanceApply collect: {ex.Message}"); }

            if (branches.Count == 0)
            {
                MepPanel.Build("MEP-A-12 Balance apply", "Hardy-Cross → Revit")
                    .AddSection("NO BRANCHES")
                    .Text("No ducts/pipes with flow parameter set. Run design flow assignment first (Mep_DuctStaticRegain or design tool).")
                    .Show();
                return Result.Succeeded;
            }

            // Run the balancer
            var enginBranches = branches.Select(b => (b.Name, b.DesignFlowLs, b.ResistanceCoeff)).ToList();
            var result = StingTools.Model.MEPBalancingEngine.BalanceSystem(
                enginBranches, totalSupplyPressurePa: 250.0, maxIterations: 50, tolerancePa: 1.0);

            // Build a name → balanced-flow lookup so we can write back.
            var byName = result.BranchResults.ToDictionary(b => b.BranchName, b => b);

            int written = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING MEP-A-12 balance apply"))
            {
                try { tx.Start(); }
                catch (Exception ex2) { MepPanel.Build("MEP-A-12 Balance apply", "tx failed").AddSection("").Text(ex2.Message).Show(); return Result.Failed; }
                try
                {
                    foreach (var b in branches)
                    {
                        if (!byName.TryGetValue(b.Name, out var outcome)) { skipped++; continue; }
                        var el = doc.GetElement(b.Id);
                        if (el == null) { skipped++; continue; }
                        try
                        {
                            if (b.IsDuct)
                            {
                                double cfm = outcome.ActualFlowLs / 0.4719;
                                var p = el.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) { p.Set(cfm); written++; }
                                else skipped++;
                            }
                            else
                            {
                                var p = el.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) { p.Set(outcome.ActualFlowLs); written++; }
                                else skipped++;
                            }
                        }
                        catch (Exception ex3) { StingLog.Warn($"Suppressed: {ex3.Message}"); skipped++; }
                    }
                    tx.Commit();
                }
                catch (Exception ex3)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Warn($"BalanceApply commit: {ex3.Message}");
                }
            }

            MepPanel.Build("MEP-A-12 Balance apply", "Hardy-Cross → Revit")
                .AddSection("BALANCER")
                .Metric("Iterations", result.Iterations.ToString())
                .Metric("Converged",   result.Converged ? "yes" : "no")
                .Metric("Max imbalance", $"{result.MaxImbalancePa:F0} Pa")
                .AddSection("WRITE-BACK")
                .Metric("Branches",  branches.Count.ToString())
                .Metric("Written",    written.ToString())
                .Metric("Skipped",    skipped.ToString())
                .Show();
            return Result.Succeeded;
        }

        private static double ReadBip(Element el, BuiltInParameter bip)
        {
            try { var p = el?.get_Parameter(bip);
                  if (p == null || p.StorageType != StorageType.Double) return 0;
                  return p.AsDouble(); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }
    }
}
