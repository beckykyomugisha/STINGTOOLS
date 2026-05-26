using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.VoltageDrop
{
    public class VDResult
    {
        public ElementId CircuitId { get; set; }
        public string PanelName { get; set; }
        public string CircuitNumber { get; set; }
        public string LoadName { get; set; }
        public double CurrentA { get; set; }
        public double LengthM { get; set; }
        public string WireSize { get; set; }
        public double VoltDropPct { get; set; }
        public bool ExceedsThreshold { get; set; }
    }

    /// <summary>
    /// Computes voltage drop for every power circuit using actual 3D wire
    /// lengths from <see cref="ElectricalSystem.Length"/>. Closes the calc →
    /// model loop: stamps the computed VD% to ELC_VLT_DROP_PCT (the alias
    /// behind ELC_CKT_VD_PCT, already in MR_PARAMETERS.txt) on every circuit
    /// so downstream wire-upsize commands, schedules and paragraph builders
    /// can read it without re-running the calc. Pushes results back to the
    /// dock panel via the snapshot builder so the VD grid reflects the
    /// calculation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VoltageDropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var opts = StingElectricalCommandHandler.CurrentVDOptions
                       ?? new VDOptionsSnapshot
                       { BranchLimitPct = 3.0, FeederLimitPct = 2.0,
                         Material = "Cu", OperatingTempC = 70.0, Standard = "BS7671" };

            var results = Calculate(doc, opts.Standard, opts.BranchLimitPct, opts.FeederLimitPct,
                                    opts.Material, opts.OperatingTempC);
            int exceed = results.Count(r => r.ExceedsThreshold);

            // Stamp VD% per circuit. Existing param ELC_VLT_DROP_PCT (alias
            // ELC_CKT_VD_PCT) — no new params introduced.
            int stamped = 0;
            using (var tx = new Transaction(doc, "STING Stamp Voltage Drop"))
            {
                tx.Start();
                foreach (var r in results)
                {
                    if (r?.CircuitId == null) continue;
                    if (!(doc.GetElement(r.CircuitId) is ElectricalSystem sys)) continue;
                    try
                    {
                        if (ParameterHelpers.SetString(sys, ParamRegistry.ELC_CKT_VD_PCT,
                                $"{r.VoltDropPct:0.00}", overwrite: true))
                            stamped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"VD stamp {r.CircuitId.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Voltage Drop",
                $"Calculated VD for {results.Count} circuit(s).\n" +
                $"Exceeding threshold: {exceed}\n" +
                $"ELC_VLT_DROP_PCT stamped: {stamped}");
            return Result.Succeeded;
        }

        public static List<VDResult> Calculate(Document doc, string standard,
            double branchLimitPct, double feederLimitPct,
            string material = "Cu", double operatingTempC = 70.0)
        {
            var results = new List<VDResult>();
            if (doc == null) return results;

            try
            {
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(s =>
                    {
                        try { return s.SystemType == ElectricalSystemType.PowerCircuit; }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; }
                    })
                    .ToList();

                foreach (var sys in systems)
                {
                    try
                    {
                        double currentA = SafeApparentCurrent(sys);
                        double lengthFt = SafeLength(sys);
                        double lengthM = lengthFt * 0.3048;
                        int phases = SafePoles(sys) >= 3 ? 3 : 1;
                        double voltageV = SafeVoltage(sys);
                        double csa = ParseCsa(SafeWireSize(sys));
                        if (voltageV <= 0 || lengthM <= 0 || csa <= 0)
                        {
                            results.Add(BuildEmpty(sys));
                            continue;
                        }
                        double vd = VoltageDropEngine.CalculateVoltDropPercent(
                            currentA, lengthM, csa, material, voltageV, phases, operatingTempC);
                        bool isFeeder = SafePoles(sys) >= 3;
                        double limit = isFeeder ? feederLimitPct : branchLimitPct;
                        results.Add(new VDResult
                        {
                            CircuitId = sys.Id,
                            PanelName = SafePanel(sys),
                            CircuitNumber = SafeCircuitNumber(sys),
                            LoadName = sys.LoadName ?? sys.Name,
                            CurrentA = currentA,
                            LengthM = lengthM,
                            WireSize = SafeWireSize(sys),
                            VoltDropPct = vd,
                            ExceedsThreshold = vd > limit
                        });
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"VD per-system: {ex.Message}");
                        results.Add(BuildEmpty(sys));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"VD.Calculate: {ex.Message}"); }
            return results;
        }

        private static VDResult BuildEmpty(ElectricalSystem sys) => new VDResult
        {
            CircuitId = sys?.Id, PanelName = SafePanel(sys),
            CircuitNumber = SafeCircuitNumber(sys),
            LoadName = sys?.LoadName ?? sys?.Name,
            CurrentA = SafeApparentCurrent(sys), LengthM = 0,
            WireSize = SafeWireSize(sys), VoltDropPct = 0, ExceedsThreshold = false
        };

        private static double SafeApparentCurrent(ElectricalSystem s)
        { try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
        private static double SafeLength(ElectricalSystem s)
        { try { return s.Length; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
        private static int SafePoles(ElectricalSystem s)
        { try { return s.PolesNumber; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 1; } }
        private static double SafeVoltage(ElectricalSystem s)
        {
            try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE)?.AsDouble() ?? 0; }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return 0; }
        }
        private static string SafeWireSize(ElectricalSystem s)
        {
            try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? ""; }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return ""; }
        }
        private static string SafePanel(ElectricalSystem s) { try { return s?.PanelName ?? ""; } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return ""; } }
        private static string SafeCircuitNumber(ElectricalSystem s)
        {
            try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? ""; } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return ""; }
        }
        private static double ParseCsa(string wireSize)
        {
            if (string.IsNullOrEmpty(wireSize)) return 0;
            string digits = "";
            foreach (char ch in wireSize)
            {
                if (char.IsDigit(ch) || ch == '.') digits += ch;
                else if (digits.Length > 0) break;
            }
            return double.TryParse(digits, out double v) ? v : 0;
        }
    }

    /// <summary>
    /// Highlights circuits whose voltage drop exceeds the configured limit by
    /// applying a graphic override in the active view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VoltageDropFlagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null) { TaskDialog.Show("STING Voltage Drop", "Activate a graphical view first."); return Result.Cancelled; }

            var opts = StingElectricalCommandHandler.CurrentVDOptions
                       ?? new VDOptionsSnapshot { BranchLimitPct = 3.0, FeederLimitPct = 2.0,
                                                  Material = "Cu", OperatingTempC = 70.0 };
            var results = VoltageDropCommand.Calculate(doc, opts.Standard, opts.BranchLimitPct,
                                                       opts.FeederLimitPct, opts.Material, opts.OperatingTempC);

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(244, 67, 54));
            ogs.SetProjectionLineWeight(6);

            int flagged = 0;
            using (var tx = new Transaction(doc, "STING Flag VD Exceedances"))
            {
                tx.Start();
                foreach (var r in results.Where(x => x.ExceedsThreshold))
                {
                    try
                    {
                        var sys = doc.GetElement(r.CircuitId) as ElectricalSystem;
                        if (sys == null) continue;
                        foreach (Element el in sys.Elements)
                        {
                            try { view.SetElementOverrides(el.Id, ogs); flagged++; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Flag VD: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("STING Voltage Drop",
                $"Flagged {flagged} element(s) on {results.Count(r => r.ExceedsThreshold)} circuit(s).");
            return Result.Succeeded;
        }
    }
}
