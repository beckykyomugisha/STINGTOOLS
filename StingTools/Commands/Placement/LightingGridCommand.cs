// StingTools v4 MVP — Lighting grid placement command.
//
// Stub in S2.8; full implementation delegates to
// StingTools.Core.Placement.LightingGridCalculator in S2.10.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = new ParameterHelpers.CommandExecutionContext(commandData);
            var doc = ctx.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            try
            {
                // TODO(S2.10): invoke LightingGridCalculator + BS EN 12464-1
                //              lookup, place fixtures via FamilySymbol.
                TaskDialog.Show("STING v4 — Lighting Grid",
                    "LightingGridCalculator not yet wired.\n\n" +
                    "S2.10 will:\n" +
                    "  1. Classify selected rooms via ROOM_TYPE_CLASSIFIER.csv\n" +
                    "  2. Look up target lux from LUX_TARGETS_EN12464.csv\n" +
                    "  3. Compute grid spacing from fixture lumens + utilisation\n" +
                    "  4. Place luminaires on the computed grid");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LightingGridCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
