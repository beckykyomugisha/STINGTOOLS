// StingTools v4 MVP — Routing stub commands.
//
// GenerateLayout and ValidateFills are placeholders in the S3.6
// registration pass. They report what the full implementation will
// do and are wired to the same Routing tab.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateLayoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = new ParameterHelpers.CommandExecutionContext(commandData);
            if (ctx.Document == null) { message = "No active document."; return Result.Failed; }

            try
            {
                TaskDialog.Show("STING v4 — Generate Layout",
                    "Layout generator not yet implemented.\n\n" +
                    "Planned behaviour:\n" +
                    "  1. Read selection (conduit / pipe / duct runs)\n" +
                    "  2. Propose main trunk + branch arrangement using\n" +
                    "     Manhattan routing between major equipment\n" +
                    "  3. Emit preview lines in a dedicated view\n" +
                    "  4. User confirms, runs inserted as MEPCurves");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateLayoutCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateFillsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = new ParameterHelpers.CommandExecutionContext(commandData);
            if (ctx.Document == null) { message = "No active document."; return Result.Failed; }

            try
            {
                // Thin wrapper around FillValidator once S4.3 lands.
                TaskDialog.Show("STING v4 — Validate Fills",
                    "Fill validator surfaced in S4.3. Use Run All Validators\n" +
                    "from the Validation tab to execute the full suite.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ValidateFillsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
