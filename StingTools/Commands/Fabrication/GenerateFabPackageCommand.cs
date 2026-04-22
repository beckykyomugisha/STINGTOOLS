// StingTools v4 MVP — GenerateFabPackageCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFabPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = new ParameterHelpers.CommandExecutionContext(commandData);
            var doc = ctx.Document;
            var uidoc = ctx.UIDocument;
            if (doc == null || uidoc == null) { message = "No active document."; return Result.Failed; }

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                TaskDialog.Show("STING v4 — Generate Fabrication Package",
                    "Select MEP elements (pipes / ducts / conduits / fittings) before running.\n\n" +
                    "FabricationEngine will:\n" +
                    "  1. Group elements per discipline rules (STING_FAB_RULES.json)\n" +
                    "  2. Create AssemblyInstances with SP-{DISC}-{SYS}-{LVL}-{SEQ} naming\n" +
                    "  3. Generate 5 views per assembly + BOM schedule\n" +
                    "  4. Lay out shop drawing sheets with title block populated\n" +
                    "  5. Emit per-discipline CSV sidecars (bend / weld / seam)");
                return Result.Cancelled;
            }

            FabricationResult res;
            try
            {
                var ids = new List<ElementId>(selIds);
                res = FabricationEngine.GenerateFabricationPackage(doc, ids);
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateFabPackageCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            ShowResult(res);

            // Open first generated sheet for instant feedback
            if (res.SheetIds.Count > 0)
            {
                try
                {
                    var sheet = doc.GetElement(res.SheetIds[0]) as ViewSheet;
                    if (sheet != null) uidoc.ActiveView = sheet;
                }
                catch (Exception ex) { StingLog.Warn($"GenerateFabPackage open sheet failed: {ex.Message}"); }
            }

            return Result.Succeeded;
        }

        private void ShowResult(FabricationResult res)
        {
            var panel = StingResultPanel.Create("v4 Fabrication Package");
            panel.SetSubtitle(res.FormatSummary());
            panel.AddSection("ASSEMBLIES BY DISCIPLINE");
            if (res.AssembliesByDiscipline.Count == 0)
                panel.Text("No assemblies created.");
            else
                foreach (var kv in res.AssembliesByDiscipline)
                    panel.Metric(kv.Key, kv.Value.ToString());

            panel.AddSection("SHEETS")
                 .Metric("Generated", res.SheetIds.Count.ToString())
                 .Metric("Failed",    res.FailedCount.ToString());

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }
            panel.Show();
        }
    }
}
