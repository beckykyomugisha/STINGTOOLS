// StingTools Phase 182 — save sizing rules to project override JSON.
//
// Closes gap D1 / A6 from the post-Phase-181 review. The CALCS tab
// SizingRoleGrid is editable; without a save path, edits live only
// in memory and get discarded on Revit close. This command:
//
//   1. Reads the current grid rows from the StingHvacPanel singleton.
//   2. Writes them to <project>/_BIM_COORD/mep_sizing_rules.json
//      (merging into the existing project override if present —
//      we only touch the `duct.roles` block).
//   3. Calls MepSizingRegistry.Reload() so subsequent sizing runs
//      pick up the new values without restarting Revit.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacSaveRulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                if (string.IsNullOrEmpty(doc.PathName))
                {
                    TaskDialog.Show("STING HVAC — Save rules",
                        "Save the project first — the override file lives next to the .rvt.");
                    return Result.Cancelled;
                }

                var panel = StingTools.UI.StingHvacPanel.Instance;
                if (panel == null)
                {
                    TaskDialog.Show("STING HVAC — Save rules",
                        "HVAC panel is not open. Open it (Ribbon ❄ HVAC → STING HVAC) and try again.");
                    return Result.Cancelled;
                }

                string projectFolder = System.IO.Path.GetDirectoryName(doc.PathName);
                string writtenPath = panel.SaveSizingRolesToProjectOverride(projectFolder);

                if (string.IsNullOrEmpty(writtenPath))
                {
                    TaskDialog.Show("STING HVAC — Save rules",
                        "Failed to write the override file. See StingTools.log for details.");
                    return Result.Failed;
                }

                panel.PushRunRow("Save sizing rules → project override", "⬤");
                TaskDialog.Show("STING HVAC — Save rules",
                    $"Project override saved:\n{writtenPath}\n\n" +
                    $"MepSizingRegistry has been reloaded — subsequent sizing runs will use the new targets.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacSaveRulesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
