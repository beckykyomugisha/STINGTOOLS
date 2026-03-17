using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// NF-01: Places 3D tag annotations using generic model family instances
    /// positioned at element bounding box centroids. Writes ASS_TAG_1 value
    /// to TAG_3D_LABEL_TXT parameter on the placed family instance.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Tag3DCommand : IExternalCommand
    {
        private const string TAG_3D_FAMILY = "STING_3D_Tag";
        private const string TAG_3D_LABEL = "TAG_3D_LABEL_TXT";

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                var doc = uiDoc.Document;

                // Get selected elements or prompt
                var selIds = uiDoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("3D Tags", "Select elements to tag in 3D, then run this command.");
                    return Result.Cancelled;
                }

                // Find the 3D tag family
                FamilySymbol tagSymbol = FindTagFamily(doc);
                if (tagSymbol == null)
                {
                    TaskDialog.Show("3D Tags",
                        $"Family '{TAG_3D_FAMILY}' not found in project.\n\n" +
                        "Load the STING_3D_Tag.rfa family into your project first,\n" +
                        "or set 'tag3DFamilyPath' in project_config.json.");
                    return Result.Failed;
                }

                int placed = 0;
                int skipped = 0;

                using (var t = new Transaction(doc, "STING Place 3D Tags"))
                {
                    t.Start();

                    // Activate symbol if needed
                    if (!tagSymbol.IsActive)
                        tagSymbol.Activate();

                    foreach (ElementId id in selIds)
                    {
                        Element el = doc.GetElement(id);
                        if (el == null) continue;

                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1))
                        {
                            skipped++;
                            continue;
                        }

                        // Get element centroid from bounding box
                        BoundingBoxXYZ bb = el.get_BoundingBox(null);
                        if (bb == null)
                        {
                            skipped++;
                            continue;
                        }

                        XYZ centroid = (bb.Min + bb.Max) / 2.0;
                        // Offset slightly above element top for visibility
                        XYZ placement = new XYZ(centroid.X, centroid.Y, bb.Max.Z + 0.5);

                        // Place non-structural family instance
                        FamilyInstance fi = doc.Create.NewFamilyInstance(
                            placement, tagSymbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        if (fi != null)
                        {
                            // Write tag value to label parameter
                            ParameterHelpers.SetString(fi, TAG_3D_LABEL, tag1, overwrite: true);
                            placed++;
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("3D Tags",
                    $"Placed {placed} 3D tag(s).\nSkipped {skipped} (no tag or no bounding box).");

                StingLog.Info($"Tag3D: placed {placed}, skipped {skipped}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("Tag3DCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static FamilySymbol FindTagFamily(Document doc)
        {
            // Search by family name
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.FamilyName.Equals(TAG_3D_FAMILY, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (symbols.Count > 0)
                return symbols[0];

            // Fallback: search for any family containing "3D_Tag" or "3DTag"
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.FamilyName.IndexOf("3D_Tag", StringComparison.OrdinalIgnoreCase) >= 0
                    || fs.FamilyName.IndexOf("3DTag", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
