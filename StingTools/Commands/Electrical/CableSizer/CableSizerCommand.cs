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
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CableSizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var snap = StingElectricalCommandHandler.CurrentCableSizeInput;
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
                    Insulation = snap.Insulation ?? "XLPE90",
                    VDLimitPct = snap.VDLimitPct <= 0 ? 3.0 : snap.VDLimitPct,
                    Standard = snap.Standard ?? "BS7671",
                };
                var result = CableSizerEngine.Calculate(input);
                StingElectricalCommandHandler.LastCableSizeResult = result;
                StingElectricalCommandHandler.ActivePanel?.RefreshCableResult(result);
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
