// MepCrossStampCommand.cs — Phase 188
//
// Drives MepCrossStampOrchestrator under a single Transaction and
// surfaces the per-discipline stamp counts via StingResultPanel.
//
// Tag: "MepCrossStamp" — wired in StingCommandHandler.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    /// <summary>
    /// Pulls native Revit MEP parameters (duct flow, pipe flow, circuit
    /// apparent current, voltage, system names, insulation thickness) onto
    /// their STING shared-param equivalents so schedules and BOQ
    /// paragraph builders can read them directly. Idempotent — re-running
    /// just refreshes the stamps.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCrossStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }

                MepCrossStampResult r;
                using (var tx = new Transaction(ctx.Doc, "STING MEP Cross-Stamp"))
                {
                    tx.Start();
                    r = MepCrossStampOrchestrator.AnalyseModel(ctx.Doc);
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("MEP Cross-Discipline Stamps");
                panel.SetSubtitle("Native Revit MEP → STING shared params");
                panel.AddSection("STAMPED")
                     .Metric("Ducts (flow + system + insulation)",        r.DuctsStamped.ToString())
                     .Metric("Pipes (flow + system + insulation)",        r.PipesStamped.ToString())
                     .Metric("Circuits (current + voltage + phase + kW)", r.CircuitsStamped.ToString())
                     .Metric("Fixtures (system name)",                    r.FixturesStamped.ToString())
                     .Metric("Insulation-thickness writes",               r.InsulationStamped.ToString());
                if (r.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS (first 20)");
                    foreach (var w in r.Warnings.Take(20)) panel.Text(w);
                }
                panel.Text("Re-run after any MEP system / connector edit to refresh the STING side.");
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepCrossStampCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
