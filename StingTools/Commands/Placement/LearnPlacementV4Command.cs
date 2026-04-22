// StingTools v4 MVP — Learn Placement command.
//
// Inspects existing fixtures in the model and proposes project-level
// rule overrides for STING_PLACEMENT_RULES.project.json. Stub in S2.8;
// the scorer + rule-extraction pass is left as a future enhancement.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LearnPlacementV4Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            try
            {
                TaskDialog.Show("STING v4 — Learn Placement",
                    "Learn-from-model pass not yet implemented.\n\n" +
                    "Planned behaviour:\n" +
                    "  1. Collect already-placed Electrical Fixtures, Lighting\n" +
                    "     Devices, Plumbing Fixtures etc. in the active view\n" +
                    "  2. Derive anchor type + offset + mounting height from\n" +
                    "     each instance's location vs containing room\n" +
                    "  3. Cluster observations by (Category, RoomName) and\n" +
                    "     emit STING_PLACEMENT_RULES.project.json next to the\n" +
                    "     .rvt with Priority = 90 (overrides defaults)\n" +
                    "  4. Report per-cluster deviation so the coordinator\n" +
                    "     can accept, reject or adjust each proposed rule");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LearnPlacementV4Command failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
