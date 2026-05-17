using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.Export;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// Single-button round-trip orchestrator: Revit → DIALux evo → Revit
    /// → design review. Walks the user through every step so the workflow
    /// is reproducible:
    ///   1. Photometric pre-flight (catch missing IES, missing reflectances)
    ///   2. IFC 4 export with Pset_StingLightingResults + Pset_StingLuminaireData
    ///   3. Open the export folder in Explorer
    ///   4. Walk-through dialog reminding the user of the DIALux steps
    ///   5. After import, the user runs Design Review to act on results
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DialuxRoundTripCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // 1. Pre-flight — purely advisory, doesn't block on warnings.
            var preflightResult = new TaskDialog("STING DIALux Round-Trip — Step 1/3")
            {
                MainInstruction = "Run pre-flight check before exporting?",
                MainContent =
                    "Pre-flight verifies every fixture has a bound IES/LDT, every room has reflectances, " +
                    "and every fixture is inside a room boundary. This catches the common 'garbage in' " +
                    "failures before DIALux rejects the file.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            preflightResult.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Run pre-flight (recommended)");
            preflightResult.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip — go straight to export");
            var pres = preflightResult.Show();
            if (pres == TaskDialogResult.Cancel) return Result.Cancelled;
            if (pres == TaskDialogResult.CommandLink1)
            {
                try
                {
                    var pre = new PhotometricPreflightCommand();
                    string msg2 = "";
                    pre.Execute(null, ref msg2, new ElementSet());
                }
                catch (Exception ex) { StingLog.Warn($"RoundTrip pre-flight: {ex.Message}"); }
            }

            // 2. Export. We delegate to the upgraded DIALuxExportCommand which
            //    writes Pset_StingLightingResults + preserves Revit GUIDs.
            var exporter = new DIALuxExportCommand();
            string msg3 = "";
            var exportResult = exporter.Execute(null, ref msg3, new ElementSet());
            if (exportResult != Result.Succeeded) return exportResult;

            // 3. Find the latest export from the round-trip log so we can
            //    open its folder and surface the path in the next-step dialog.
            string ifcPath = "";
            try
            {
                var entries = DIALuxExportCommand.LoadEntries(doc);
                var latest = entries.OrderByDescending(e => e.Date).FirstOrDefault();
                if (latest != null) ifcPath = latest.IfcPath;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            if (!string.IsNullOrEmpty(ifcPath) && File.Exists(ifcPath))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ifcPath}\"") { UseShellExecute = true }); }
                catch (Exception ex2) { StingLog.Info($"Open Explorer: {ex2.Message}"); }
            }

            // 4. Walk-through: tell the user what to do in DIALux evo, then
            //    point them at the Import IFC Results command.
            var nextSteps = new TaskDialog("STING DIALux Round-Trip — Step 2/3")
            {
                MainInstruction = "Now: switch to DIALux evo",
                MainContent =
                    (string.IsNullOrEmpty(ifcPath) ? "" : $"Exported: {ifcPath}\n\n") +
                    "1. Open DIALux evo (12 or later — older DIALux only reads IFC 2x3).\n" +
                    "2. File → Import → IFC, then pick the file STING just exported.\n" +
                    "3. STING preserved every Revit GUID into IfcGloballyUniqueId so the round-trip back\n" +
                    "   matches results to the exact same Revit elements automatically.\n" +
                    "4. In DIALux: assign luminaire catalogue entries to the imported fixtures.\n" +
                    "5. Run the calculation; export an IFC of the calculated model.\n" +
                    "6. Back in Revit, run STING → Photometrics → Import IFC Results, then\n" +
                    "   STING → Photometrics → Photometric Design Review to see the actionable changes.",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            nextSteps.Show();
            return Result.Succeeded;
        }
    }
}
