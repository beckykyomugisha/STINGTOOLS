using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.CableSizer
{
    /// <summary>
    /// Read-only cable-sizing command. Reads inputs from the dock-panel
    /// CABLE tab (via <see cref="StingElectricalCommandHandler.CurrentCableSizeInput"/>),
    /// runs <see cref="CableSizerEngine.Calculate"/>, stashes the result in
    /// <see cref="StingElectricalCommandHandler.LastCableSizeResult"/>, and
    /// pushes it back to the panel for display.
    ///
    /// Workflow preset (Cable_Calculate): no dialog. Step params
    /// (ElectricalStepInputs.CableInputs): loadKW, voltageV, phases (1 | 3), powerFactor,
    /// lengthM, vdLimitPct, installMethod, material, insulation, cableType, standard. Base:
    /// the panel's CABLE tab when the panel is open (re-read at the step). Without the
    /// panel loadKW, voltageV and lengthM are REQUIRED — the step fails naming them; the
    /// rest default to PF 0.85, method C, Cu, PVC70, Multicore, VD 3 %, BS 7671, 1-phase.
    /// The result (or the refusal) goes to the step message; a refusal fails the step.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CableSizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                bool headless = WorkflowEngine.IsRunningPreset;
                CableSizerInputSnapshot snap;
                if (headless)
                {
                    if (!ElectricalStepInputs.CableInputs(out snap, out var err))
                    { message = "Cable sizing: " + err; return Result.Failed; }
                }
                else snap = StingElectricalCommandHandler.CurrentCableSizeInput;
                if (snap == null)
                {
                    TaskDialog.Show("STING Electrical", "No cable inputs captured. Enter values on the CABLE tab and click Calculate.");
                    return Result.Cancelled;
                }
                var input = new CableSizeInput
                {
                    LoadKW = snap.LoadKW,
                    VoltageV = snap.VoltageV,
                    Phases = snap.Phases <= 0 ? 1 : snap.Phases,
                    PowerFactor = snap.PowerFactor <= 0 ? 0.85 : snap.PowerFactor,
                    LengthM = snap.LengthM,
                    InstallMethod = snap.InstallMethod ?? "C",
                    Material = snap.Material ?? "Cu",
                    Insulation = snap.Insulation ?? "PVC70",
                    CableType = snap.CableType ?? "Multicore",
                    VDLimitPct = snap.VDLimitPct <= 0 ? 3.0 : snap.VDLimitPct,
                    Standard = snap.Standard ?? "BS7671",
                };
                var result = CableSizerEngine.Calculate(input,
                    CableSizerEngine.Bs7671Tables(ParameterHelpers.GetContext(commandData)?.Doc));
                StingElectricalCommandHandler.LastCableSizeResult = result;
                StingElectricalCommandHandler.ActivePanel?.RefreshCableResult(result);
                if (headless)
                {
                    string inputs = $"{input.LoadKW:0.###} kW, {input.VoltageV:0} V {input.Phases}-ph, PF {input.PowerFactor:0.##}, " +
                                    $"{input.LengthM:0.#} m, method {input.InstallMethod}, {input.Material}/{input.Insulation} {input.CableType}, " +
                                    $"VD limit {input.VDLimitPct:0.##} %, {input.Standard}";
                    if (!result.Sized)
                    {
                        message = $"Cable sizing: NOT SIZED ({inputs}) — {result.Warning}";
                        return Result.Failed;
                    }
                    PresetDialog.Show("STING Cable Sizing",
                        $"{result.CsaLabel} · Ib {result.DesignCurrentA:0.#} A · {result.ProposedBreakerA} A {result.ProtectiveDevice} · " +
                        $"VD {result.ActualVoltDropPct:0.##} % ({(result.VDCompliant ? "within" : "OVER")} limit)\n" +
                        $"Inputs: {inputs}" +
                        (string.IsNullOrEmpty(result.Warning) ? "" : $"\nWarning: {result.Warning}") +
                        (string.IsNullOrEmpty(result.Basis) ? "" : $"\nBasis: {result.Basis}"), ref message);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CableSizerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
