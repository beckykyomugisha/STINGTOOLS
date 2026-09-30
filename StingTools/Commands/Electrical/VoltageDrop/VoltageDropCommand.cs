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
        /// <summary>True when the circuit feeds lighting (BS 7671 App 12: 3 % limit, else 5 %).</summary>
        public bool IsLighting { get; set; }
        /// <summary>The limit this circuit was judged against, %.</summary>
        public double LimitPct { get; set; }
        /// <summary>How the figure was obtained (ELEC-22). Null only on a failed read.</summary>
        public StingTools.Core.Electrical.CircuitVdResult Vd { get; set; }
        /// <summary>A figure exists. False = not calculated; VoltDropPct is then 0 and means nothing.</summary>
        public bool HasValue => Vd?.HasValue == true;
        /// <summary>The figure is an upper bound (no cable recorded): over the limit is possible, not certain.</summary>
        public bool UpperBound => Vd?.UpperBound == true;
        public bool PossiblyExceeds => HasValue && UpperBound && VoltDropPct > LimitPct + 1e-9;
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
    // Workflow preset (Calc_VoltageDrop): no dialog; the summary goes to the step message.
    // Step params (ElectricalStepInputs.VdOptions): lightingLimitPct, otherLimitPct,
    // material (Cu | Al), operatingTempC, standard. Defaults: the Electrical panel's VOLTAGE
    // DROP expander (re-read at the step), else 3 % lighting / 5 % other, Cu, 70 °C, BS 7671.
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VoltageDropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var fallback = new VDOptionsSnapshot
                       { LightingLimitPct = 3.0, OtherLimitPct = 5.0,
                         Material = "Cu", OperatingTempC = 70.0, Standard = "BS7671" };
            VDOptionsSnapshot opts;
            if (WorkflowEngine.IsRunningPreset)
            {
                if (!ElectricalStepInputs.VdOptions(fallback, out opts, out var err))
                { message = "Voltage drop: " + err; return Result.Failed; }
            }
            else opts = StingElectricalCommandHandler.CurrentVDOptions ?? fallback;

            var results = Calculate(doc, opts.Standard, opts.LightingLimitPct, opts.OtherLimitPct,
                                    opts.Material, opts.OperatingTempC);
            int exceed = results.Count(r => r.ExceedsThreshold);
            int possible = results.Count(r => r.PossiblyExceeds);
            int notCalc = results.Count(r => !r.HasValue);
            int bounded = results.Count(r => r.HasValue && r.UpperBound);

            // Stamp VD% and its basis (ELC_CKT_VD_BASIS_TXT) per circuit. A circuit that
            // could not be calculated gets NONE and the reason, never 0.00.
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
                        if (r.Vd != null && StingTools.Core.Electrical.CircuitVoltageDropModel.Stamp(sys, r.Vd))
                            stamped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"VD stamp {r.CircuitId.Value}: {ex.Message}"); }
                }
                tx.Commit();
            }

            PresetDialog.Show("STING Voltage Drop",
                $"Circuits: {results.Count}\n" +
                $"Exceeding the limit: {exceed}\n" +
                (possible > 0 ? $"Possibly exceeding (upper bound, no cable recorded): {possible}\n" : "") +
                (bounded > 0 ? $"Upper-bound figures (A4-MAX — apply a cable size to record the cable): {bounded}\n" : "") +
                (notCalc > 0 ? $"Not calculated (basis NONE gives the reason): {notCalc}\n" : "") +
                $"Voltage drop and basis stamped: {stamped}" +
                (stamped == 0 && results.Count > 0 ? "\n\nNothing was stamped: ELC_CKT_VD_BASIS_TXT is not bound to Electrical Circuits. Run Load Shared Params." : ""), ref message);
            return Result.Succeeded;
        }

        /// <param name="lightingLimitPct">Limit for circuits feeding lighting (BS 7671 App 12: 3 %).</param>
        /// <param name="otherLimitPct">Limit for every other circuit (BS 7671 App 12: 5 %).</param>
        /// <remarks>The App 12 limits apply from the ORIGIN of the installation; this compares
        /// each final circuit's own drop against them and does not add the drop in the
        /// upstream distribution — leave headroom for it.</remarks>
        public static List<VDResult> Calculate(Document doc, string standard,
            double lightingLimitPct, double otherLimitPct,
            string material = "Cu", double operatingTempC = 70.0)
        {
            var results = new List<VDResult>();
            if (doc == null) return results;
            // ELEC-22: one resolver for every writer — Appendix 4 mV/A/m from the circuit's
            // recorded cable, else an upper bound; conductor resistance only on the NEC path.
            var tables = StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables(doc);

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
                        var input = StingTools.Core.Electrical.CircuitVoltageDropModel.Read(sys, standard, material);
                        var vd = StingTools.Core.Electrical.CircuitVoltageDrop.Resolve(input, tables,
                            StingTools.Core.Electrical.CircuitVoltageDropModel.Resistance(operatingTempC));
                        bool isLighting = IsLightingCircuit(sys);
                        double limit = VoltageDropEngine.LimitFor(isLighting, lightingLimitPct, otherLimitPct);
                        results.Add(new VDResult
                        {
                            CircuitId = sys.Id,
                            PanelName = SafePanel(sys),
                            CircuitNumber = SafeCircuitNumber(sys),
                            LoadName = sys.LoadName ?? sys.Name,
                            CurrentA = input.CurrentA,
                            LengthM = input.LengthM,
                            WireSize = SafeWireSize(sys),
                            VoltDropPct = vd.HasValue ? vd.Pct : 0,
                            // Only a figure that is not an upper bound can prove an exceedance.
                            ExceedsThreshold = vd.HasValue && !vd.UpperBound && vd.Pct > limit + 1e-9,
                            IsLighting = isLighting,
                            LimitPct = limit,
                            Vd = vd,
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
            WireSize = SafeWireSize(sys), VoltDropPct = 0, ExceedsThreshold = false,
            Vd = new StingTools.Core.Electrical.CircuitVdResult { Detail = "the circuit could not be read (see the STING log)" },
        };

        private static double SafeApparentCurrent(ElectricalSystem s)
        { try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
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
        /// <summary>CSA from Revit's wire-size string. ELEC-5: this took the FIRST number,
        /// so "2 x 2.5mm²" was read as 2 mm² (the conductor count). Now delegates to
        /// <see cref="StingTools.Core.Electrical.WireSizeParser"/>.</summary>
        internal static double ParseCsa(string wireSize)
            => StingTools.Core.Electrical.WireSizeParser.ParseCsaMm2(wireSize);

        /// <summary>True when any element on the circuit is a lighting fixture or lighting
        /// device — the BS 7671 Appendix 12 "lighting" class.</summary>
        internal static bool IsLightingCircuit(ElectricalSystem s)
        {
            try
            {
                if (s?.Elements == null) return false;
                foreach (Element el in s.Elements)
                {
                    long cat = el?.Category?.Id?.Value ?? 0;
                    if (cat == (long)BuiltInCategory.OST_LightingFixtures
                        || cat == (long)BuiltInCategory.OST_LightingDevices) return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"VD lighting class {s?.Id}: {ex.Message}"); }
            return false;
        }
    }

    /// <summary>
    /// Highlights circuits whose voltage drop exceeds the configured limit by
    /// applying a graphic override in the active view.
    /// </summary>
    // Workflow preset (Calc_FlagVD): no dialog; the summary goes to the step message.
    // Same step params and defaults as Calc_VoltageDrop. Overrides go on the ACTIVE view;
    // with no active view the step fails.
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
            if (view == null)
            {
                if (WorkflowEngine.IsRunningPreset) { message = "Flag voltage drop: no active graphical view to flag in."; return Result.Failed; }
                TaskDialog.Show("STING Voltage Drop", "Activate a graphical view first."); return Result.Cancelled;
            }

            var fallback = new VDOptionsSnapshot { LightingLimitPct = 3.0, OtherLimitPct = 5.0,
                                                  Material = "Cu", OperatingTempC = 70.0 };
            VDOptionsSnapshot opts;
            if (WorkflowEngine.IsRunningPreset)
            {
                if (!ElectricalStepInputs.VdOptions(fallback, out opts, out var err))
                { message = "Flag voltage drop: " + err; return Result.Failed; }
            }
            else opts = StingElectricalCommandHandler.CurrentVDOptions ?? fallback;
            var results = VoltageDropCommand.Calculate(doc, opts.Standard, opts.LightingLimitPct,
                                                       opts.OtherLimitPct, opts.Material, opts.OperatingTempC);

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
            PresetDialog.Show("STING Voltage Drop",
                $"Flagged {flagged} element(s) on {results.Count(r => r.ExceedsThreshold)} circuit(s).", ref message);
            return Result.Succeeded;
        }
    }
}
