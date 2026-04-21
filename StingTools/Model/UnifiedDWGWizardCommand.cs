// Phase 108m — Unified DWG wizard. Auto-detects whether the imported
// DWG is structural / architectural / MEP and dispatches to the right
// specialised engine. User clicks one button instead of picking from
// three.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Model
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UnifiedDWGWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Scan imports in the doc and classify by layer prefix
                var imports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();
                if (imports.Count == 0)
                {
                    TaskDialog.Show("Unified DWG Wizard", "No DWG imports detected. Link or import a DWG first.");
                    return Result.Cancelled;
                }

                // Count layer hits per discipline pattern
                int struc = 0, arch = 0, mep = 0;
                foreach (var imp in imports)
                {
                    var cats = imp.Category?.SubCategories;
                    if (cats == null) continue;
                    foreach (Category sc in cats)
                    {
                        string n = (sc.Name ?? "").ToLowerInvariant();
                        if (n.Contains("col") || n.Contains("beam") || n.Contains("struct") || n.Contains("found") || n.Contains("slab")) struc++;
                        else if (n.Contains("door") || n.Contains("wind") || n.Contains("wall") || n.Contains("part") || n.Contains("a-")) arch++;
                        else if (n.Contains("duct") || n.Contains("pipe") || n.Contains("mech") || n.Contains("elec") || n.Contains("plumb") || n.Contains("m-") || n.Contains("e-") || n.Contains("p-")) mep++;
                    }
                }

                string dispatch = null;
                string dominant;
                if (struc >= arch && struc >= mep) { dispatch = "StructuralDWGWizard"; dominant = $"Structural ({struc} layers)"; }
                else if (arch >= mep)              { dispatch = "DWGToModel";          dominant = $"Architectural ({arch} layers)"; }
                else                                { dispatch = "DWGToModel";          dominant = $"MEP ({mep} layers)"; }

                var td = new TaskDialog("Unified DWG Wizard")
                {
                    MainInstruction = $"Detected dominant discipline: {dominant}",
                    MainContent = $"Dispatching to {dispatch}. Layer hit counts:\n"
                                + $"  Structural: {struc}\n  Architectural: {arch}\n  MEP: {mep}\n\n"
                                + "Cancel to pick a different wizard manually.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                };
                if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                StingDockPanel.DispatchCommand(dispatch);
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("UnifiedDWGWizardCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
