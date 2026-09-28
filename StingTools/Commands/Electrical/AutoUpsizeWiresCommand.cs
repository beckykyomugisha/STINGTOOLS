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
    /// failing circuit via <see cref="StingTools.Core.Electrical.CircuitVoltageDrop.MinimumCsaForLimit"/>,
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
                       ?? new VDOptionsSnapshot { LightingLimitPct = 3.0, OtherLimitPct = 5.0,
                                                  Material = "Cu", OperatingTempC = 70.0,
                                                  Standard = "BS7671" };

            var vdResults = VoltageDropCommand.Calculate(doc, opts.Standard,
                opts.LightingLimitPct, opts.OtherLimitPct, opts.Material, opts.OperatingTempC);
            // A certain exceedance, or an upper bound over the limit (no cable recorded):
            // the second may already comply, so the preview says which is which.
            var failing = vdResults.Where(r => r.ExceedsThreshold || r.PossiblyExceeds).ToList();
            if (failing.Count == 0)
            {
                int notCalc = vdResults.Count(r => !r.HasValue);
                TaskDialog.Show("STING Auto-Upsize", "No circuits exceed the voltage-drop limit." +
                    (notCalc > 0 ? $"\n\n{notCalc} circuit(s) could not be calculated — run Voltage Drop and read ELC_CKT_VD_BASIS_TXT for why." : ""));
                return Result.Succeeded;
            }

            // ELEC-22: the same resolver as every other VD writer, so the new size is judged by
            // the figure that will be stamped — Appendix 4 mV/A/m, not conductor resistance.
            var tables = StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables(doc);
            var resistance = StingTools.Core.Electrical.CircuitVoltageDropModel.Resistance(opts.OperatingTempC);
            var preview = new List<UpsizeProposal>();
            foreach (var vd in failing)
            {
                var sys = doc.GetElement(vd.CircuitId) as ElectricalSystem;
                if (sys == null) continue;
                var input = StingTools.Core.Electrical.CircuitVoltageDropModel.Read(sys, opts.Standard, opts.Material);
                double currentCsa = input.CsaMm2;
                double limit = vd.LimitPct > 0 ? vd.LimitPct
                    : VoltageDropEngine.LimitFor(vd.IsLighting, opts.LightingLimitPct, opts.OtherLimitPct);
                double? minCsa = StingTools.Core.Electrical.CircuitVoltageDrop.MinimumCsaForLimit(
                    input, tables, limit, VoltageDropEngine.StandardSizesMm2.Where(x => x > currentCsa + 1e-9),
                    out var at, resistance);
                if (minCsa == null || at == null) continue;
                preview.Add(new UpsizeProposal
                {
                    CircuitId    = vd.CircuitId,
                    PanelName    = vd.PanelName,
                    CircuitNumber= vd.CircuitNumber,
                    LoadName     = vd.LoadName,
                    OldCsaMm2    = currentCsa,
                    NewCsaMm2    = minCsa.Value,
                    NewVDPct     = at.Pct,
                    NewVd        = at,
                    OnUpperBound = vd.PossiblyExceeds,
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
                sb.AppendLine($"  {p.PanelName}-{p.CircuitNumber}: {p.OldCsaMm2:0.#}mm² → {p.NewCsaMm2:0.#}mm² (new VD {p.NewVDPct:0.0}%" +
                              (p.NewVd.UpperBound ? " upper bound" : "") + ")" +
                              (p.OnUpperBound ? " — current figure is an upper bound; the circuit may already comply" : ""));
            }
            if (preview.Count > top) sb.AppendLine($"  …and {preview.Count - top} more");
            int onBound = preview.Count(p => p.OnUpperBound);
            if (onBound > 0)
                sb.AppendLine($"\n{onBound} circuit(s) carry no cable record, so their drop is an upper bound. " +
                              "Applying a cable size from the CABLE tab records the cable and gives an exact figure.");

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
                        StingTools.Core.Electrical.CircuitVoltageDropModel.Stamp(sys, p.NewVd);
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
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Auto-Upsize",
                $"Updated {written} circuit(s). {fallback} fell back to STING-only parameter (native wire-size read-only).");
            return Result.Succeeded;
        }

        private class UpsizeProposal
        {
            public ElementId CircuitId;
            public string PanelName, CircuitNumber, LoadName;
            public double OldCsaMm2, NewCsaMm2, NewVDPct;
            public StingTools.Core.Electrical.CircuitVdResult NewVd;
            public bool OnUpperBound;
        }

    }
}
