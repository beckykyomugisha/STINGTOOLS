using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Reads voltage-drop results, finds the minimum compliant CSA for each
    /// failing circuit via <see cref="VoltageDropEngine.MinimumCsaForVDLimit"/>,
    /// previews the proposed changes, and on confirmation writes
    /// ELC_CKT_CSA_MM2 (and best-effort RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoUpsizeWiresCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var opts = StingElectricalCommandHandler.CurrentVDOptions
                       ?? new VDOptionsSnapshot { BranchLimitPct = 3.0, FeederLimitPct = 2.0,
                                                  Material = "Cu", OperatingTempC = 70.0,
                                                  Standard = "BS7671" };

            var vdResults = VoltageDropCommand.Calculate(doc, opts.Standard,
                opts.BranchLimitPct, opts.FeederLimitPct, opts.Material, opts.OperatingTempC);
            var failing = vdResults.Where(r => r.ExceedsThreshold).ToList();
            if (failing.Count == 0)
            {
                TaskDialog.Show("STING Auto-Upsize", "No circuits exceed the voltage-drop threshold.");
                return Result.Succeeded;
            }

            var preview = new List<UpsizeProposal>();
            foreach (var vd in failing)
            {
                var sys = doc.GetElement(vd.CircuitId) as ElectricalSystem;
                if (sys == null) continue;
                int phases = SafePoles(sys) >= 3 ? 3 : 1;
                double v = SafeVoltage(sys);
                if (v <= 0) v = phases == 3 ? 415.0 : 240.0;
                double currentCsa = ParseCsa(vd.WireSize);
                double? minCsa = VoltageDropEngine.MinimumCsaForVDLimit(
                    vd.CurrentA, vd.LengthM, opts.Material, v, phases,
                    phases == 3 ? opts.FeederLimitPct : opts.BranchLimitPct,
                    opts.OperatingTempC);
                if (minCsa == null || minCsa <= currentCsa) continue;
                double newVd = VoltageDropEngine.CalculateVoltDropPercent(
                    vd.CurrentA, vd.LengthM, minCsa.Value, opts.Material, v, phases, opts.OperatingTempC);
                preview.Add(new UpsizeProposal
                {
                    CircuitId    = vd.CircuitId,
                    PanelName    = vd.PanelName,
                    CircuitNumber= vd.CircuitNumber,
                    LoadName     = vd.LoadName,
                    OldCsaMm2    = currentCsa,
                    NewCsaMm2    = minCsa.Value,
                    NewVDPct     = newVd
                });
            }
            if (preview.Count == 0)
            {
                TaskDialog.Show("STING Auto-Upsize",
                    "No upsizing possible — all failing circuits are already at the largest tabulated size or could not be re-sized.");
                return Result.Succeeded;
            }

            var sb = new StringBuilder();
            int top = Math.Min(10, preview.Count);
            for (int i = 0; i < top; i++)
            {
                var p = preview[i];
                sb.AppendLine($"  {p.PanelName}-{p.CircuitNumber}: {p.OldCsaMm2:0.#}mm² → {p.NewCsaMm2:0.#}mm² (new VD {p.NewVDPct:0.0}%)");
            }
            if (preview.Count > top) sb.AppendLine($"  …and {preview.Count - top} more");

            var dlg = new TaskDialog("STING Auto-Upsize Conductors")
            {
                MainInstruction = $"Upsize {preview.Count} circuit(s)?",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int written = 0, fallback = 0;
            using (var tx = new Transaction(doc, "STING Auto-Upsize Conductors"))
            {
                tx.Start();
                foreach (var p in preview)
                {
                    try
                    {
                        var sys = doc.GetElement(p.CircuitId) as ElectricalSystem;
                        if (sys == null) continue;
                        ParameterHelpers.SetString(sys, ParamRegistry.ELC_CKT_CSA_MM2,
                            $"{p.NewCsaMm2:0.#}", overwrite: true);
                        ParameterHelpers.SetString(sys, ParamRegistry.ELC_CKT_VD_PCT,
                            $"{p.NewVDPct:0.00}", overwrite: true);
                        try
                        {
                            var nativeWire = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM);
                            if (nativeWire != null && !nativeWire.IsReadOnly)
                                nativeWire.Set($"{p.NewCsaMm2:0.#}mm²");
                        }
                        catch (Exception ex) { StingLog.Info($"Native wire-size write soft-fail on {p.PanelName}-{p.CircuitNumber}: {ex.Message}"); fallback++; }
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Upsize write: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Auto-Upsize",
                $"Updated {written} circuit(s). {fallback} fell back to STING-only parameter (native wire-size read-only).");
            return Result.Succeeded;
        }

        private class UpsizeProposal
        {
            public ElementId CircuitId;
            public string PanelName, CircuitNumber, LoadName;
            public double OldCsaMm2, NewCsaMm2, NewVDPct;
        }

        private static int SafePoles(ElectricalSystem s)
        { try { return s.PolesNumber; } catch { return 1; } }
        private static double SafeVoltage(ElectricalSystem s)
        { try { return s.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE)?.AsDouble() ?? 0; } catch { return 0; } }
        private static double ParseCsa(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            string digits = "";
            foreach (char ch in s)
            {
                if (char.IsDigit(ch) || ch == '.') digits += ch;
                else if (digits.Length > 0) break;
            }
            return double.TryParse(digits, out double v) ? v : 0;
        }
    }
}
