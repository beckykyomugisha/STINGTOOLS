using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Places 3D annotation tags (FamilyInstance-based) on elements in the active 3D view.
    /// Writes the assembled ISO 19650 tag to a label parameter on each placed tag family instance.
    /// Also writes the full container pipeline (WriteContainers + WriteTag7All) to each tagged element.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Tag3DCommand : IExternalCommand
    {
        private const string TAG_3D_LABEL = "ASS_TAG_3D_TXT";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            View view = doc.ActiveView;
            if (view == null || !(view is View3D))
            {
                TaskDialog.Show("Tag 3D", "Active view must be a 3D view.");
                return Result.Succeeded;
            }

            // Find or load 3D tag family
            FamilySymbol tagSymbol = FindTagFamily(doc);
            if (tagSymbol == null)
            {
                TaskDialog.Show("Tag 3D",
                    "No 3D tag family found.\n\nLoad a Generic Model family with a '" +
                    TAG_3D_LABEL + "' label parameter, or set 'tag3DFamilyPath' in project_config.json.");
                return Result.Succeeded;
            }

            // Collect taggable elements in view
            var catEnums = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> viewElements;
            if (catEnums != null && catEnums.Length > 0)
                viewElements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            else
                viewElements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();

            var elList = viewElements.ToList();
            if (elList.Count == 0)
            {
                TaskDialog.Show("Tag 3D", "No taggable elements found in the active 3D view.");
                return Result.Succeeded;
            }

            int placed = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag 3D"))
            {
                tx.Start();

                if (!tagSymbol.IsActive) tagSymbol.Activate();

                foreach (Element el in elList)
                {
                    try
                    {
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) continue;

                        // Get element location for placement
                        XYZ point = GetElementCenter(el);
                        if (point == null) continue;

                        // Offset tag slightly above element
                        XYZ tagPoint = new XYZ(point.X, point.Y, point.Z + 1.0);

                        // Place 3D tag family instance
                        FamilyInstance fi = doc.Create.NewFamilyInstance(
                            tagPoint, tagSymbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        if (fi == null) continue;

                        // Write tag value to label parameter
                        ParameterHelpers.SetString(fi, TAG_3D_LABEL, tag1, overwrite: true);

                        // A6: Write full container pipeline for 3D-tagged instances
                        try
                        {
                            string catName3D = ParameterHelpers.GetCategoryName(fi);
                            string[] tokens3D = ParamRegistry.ReadTokenValues(fi);
                            ParamRegistry.WriteContainers(fi, tokens3D, catName3D);
                            TagConfig.WriteTag7All(doc, fi, catName3D, tokens3D, overwrite: false);
                        }
                        catch (Exception pEx3D)
                        {
                            StingLog.Warn($"Tag3D container write for {fi.Id}: {pEx3D.Message}");
                        }

                        placed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StingLog.Warn($"Tag3D placement for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            // FIX-B09: Invalidate caches and check compliance gate after 3D tagging
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            TagConfig.CheckComplianceGate(doc, "Tag3D");

            string report = $"3D tags placed: {placed}";
            if (errors > 0) report += $"\nErrors: {errors}";
            TaskDialog.Show("Tag 3D", report);
            return Result.Succeeded;
        }

        /// <summary>
        /// Find a suitable 3D tag family already loaded in the document,
        /// or attempt to load from project_config.json tag3DFamilyPath.
        /// </summary>
        private static FamilySymbol FindTagFamily(Document doc)
        {
            // First try: find a loaded Generic Model family with the tag label parameter
            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family?.FamilyCategory?.Id.Value ==
                    (long)BuiltInCategory.OST_GenericModel)
                .ToList();

            foreach (var fs in candidates)
            {
                try
                {
                    // Check family name for tag/3D indicators — no temp instance needed
                    string famName = fs.Family?.Name?.ToUpperInvariant() ?? "";
                    if (famName.Contains("TAG") || famName.Contains("3D"))
                    {
                        StingLog.Info($"Tag3D: found family '{fs.Family?.Name}' type '{fs.Name}'");
                        return fs;
                    }
                }
                catch { }
            }

            // Second try: any Generic Model family
            var fallback = candidates.FirstOrDefault();
            if (fallback != null)
            {
                StingLog.Info($"Tag3D: using fallback family '{fallback.Family?.Name}'");
                return fallback;
            }

            // A6: Attempt to load family from project_config.json tag3DFamilyPath
            try
            {
                string docPath = doc.PathName ?? string.Empty;
                if (!string.IsNullOrEmpty(docPath))
                {
                    string cfgPath = Path.Combine(
                        Path.GetDirectoryName(docPath) ?? string.Empty,
                        "project_config.json");
                    if (File.Exists(cfgPath))
                    {
                        string json = File.ReadAllText(cfgPath);
                        var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<
                            Dictionary<string, object>>(json);
                        if (cfg != null && cfg.TryGetValue("tag3DFamilyPath", out object pathObj)
                            && pathObj is string familyPath && File.Exists(familyPath))
                        {
                            if (doc.LoadFamily(familyPath, out Family fam) && fam != null)
                            {
                                var ids = fam.GetFamilySymbolIds();
                                if (ids.Count > 0)
                                {
                                    var sym = doc.GetElement(ids.First()) as FamilySymbol;
                                    StingLog.Info($"Tag3D: loaded family from config path: {familyPath}");
                                    return sym;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception cfgEx)
            {
                StingLog.Warn($"Tag3D family load from config: {cfgEx.Message}");
            }

            return null;
        }

        /// <summary>Get the center point of an element from its bounding box or location.</summary>
        private static XYZ GetElementCenter(Element el)
        {
            LocationPoint lp = el.Location as LocationPoint;
            if (lp != null) return lp.Point;

            LocationCurve lc = el.Location as LocationCurve;
            if (lc != null)
            {
                Curve c = lc.Curve;
                return c.Evaluate(0.5, true);
            }

            BoundingBoxXYZ bb = el.get_BoundingBox(null);
            if (bb != null)
                return new XYZ(
                    (bb.Min.X + bb.Max.X) / 2,
                    (bb.Min.Y + bb.Max.Y) / 2,
                    (bb.Min.Z + bb.Max.Z) / 2);

            return null;
        }
    }
}
