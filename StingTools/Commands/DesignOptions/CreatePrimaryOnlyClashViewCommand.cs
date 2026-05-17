// StingTools — Primary-Only Clash 3D View.
//
// Generates a single hidden 3D view "STING - Clash (primary only)" with
// VIEWER_OPTION_VISIBILITY set per-set to the primary of every option
// set. The Revit param accepts only ONE option id, so true per-set
// primary-only requires per-link customisation; this command picks the
// document-wide pattern: leave the parameter as InvalidElementId and
// add view-template-driven Visibility/Graphics overrides that hide
// every non-primary option element via filters.
//
// Output: a 3D view callable from the Navisworks/ACC export pipeline so
// clashes never run between mutually exclusive options.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePrimaryOnlyClashViewCommand : IExternalCommand
    {
        public const string ViewName = "STING - Clash (primary only)";

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING — Clash View",
                    "No design option sets in this document — clashes already run against a single "
                    + "model. No clash isolation view is needed.");
                return Result.Cancelled;
            }

            var threeDType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (threeDType == null)
            {
                TaskDialog.Show("STING", "No 3D view family type available.");
                return Result.Failed;
            }

            View3D made = null;
            int hiddenCount = 0;

            using (var t = new Transaction(doc, "STING Primary-Only Clash View"))
            {
                t.Start();
                try
                {
                    var stale = new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => v.Name == ViewName);
                    if (stale != null) doc.Delete(stale.Id);

                    made = View3D.CreateIsometric(doc, threeDType.Id);
                    made.Name = ViewName;

                    // Hide every element that lives in a non-primary option
                    var nonPrimary = new List<ElementId>();
                    foreach (var s in sets)
                    foreach (var o in s.Options.Where(x => !x.IsPrimary))
                    {
                        var f = new ElementDesignOptionFilter(o.OptionId);
                        var ids = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .WherePasses(f)
                            .ToElementIds()
                            .ToList();
                        nonPrimary.AddRange(ids);
                    }
                    if (nonPrimary.Count > 0)
                    {
                        try
                        {
                            made.HideElements(nonPrimary);
                            hiddenCount = nonPrimary.Count;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"HideElements: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error($"CreatePrimaryOnlyClashView: {ex.Message}", ex);
                    t.RollBack();
                    TaskDialog.Show("STING — Clash View", $"Failed: {ex.Message}");
                    return Result.Failed;
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"View name      : {ViewName}");
            sb.AppendLine($"Sets processed : {sets.Count}");
            sb.AppendLine($"Elements hidden: {hiddenCount}");
            sb.AppendLine();
            sb.AppendLine("Use this view as the source for NWC / IFC clash export.");
            sb.AppendLine("Re-run after option content changes to refresh the hidden set.");
            TaskDialog.Show("STING — Primary-Only Clash View", sb.ToString());
            return Result.Succeeded;
        }
    }
}
