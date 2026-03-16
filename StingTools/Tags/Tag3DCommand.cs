using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Place 3D tag family instances at element locations in a 3D view.
    /// Unlike IndependentTag annotations (2D only), this places FamilyInstance
    /// objects from a tag family that renders in 3D space.
    /// Runs the full tagging pipeline on each placed instance.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Tag3DCommand : IExternalCommand
    {
        private const string TAG_3D_LABEL = "ASS_TAG_1_TXT";

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            View activeView = doc.ActiveView;
            if (activeView == null || !(activeView is View3D))
            {
                TaskDialog.Show("Tag 3D", "Switch to a 3D view first.\n3D tags require a 3D view.");
                return Result.Succeeded;
            }

            // Find tag family
            FamilySymbol tagFamily = FindTagFamily(doc);
            if (tagFamily == null)
            {
                TaskDialog.Show("Tag 3D",
                    "No 3D tag family found.\n\n" +
                    "Load a Generic Model family named '3D_Tag' or 'STING_3D_Tag',\n" +
                    "or set 'tag3DFamilyPath' in project_config.json.");
                return Result.Succeeded;
            }

            // Get selected elements or all taggable in view
            var selectedIds = uidoc.Selection.GetElementIds();
            List<Element> targets;
            if (selectedIds.Count > 0)
            {
                targets = selectedIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && TagConfig.DiscMap.ContainsKey(
                        ParameterHelpers.GetCategoryName(e)))
                    .ToList();
            }
            else
            {
                targets = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => TagConfig.DiscMap.ContainsKey(
                        ParameterHelpers.GetCategoryName(e)))
                    .ToList();
            }

            if (targets.Count == 0)
            {
                TaskDialog.Show("Tag 3D", "No taggable elements found in view/selection.");
                return Result.Succeeded;
            }

            // Build pipeline context
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            int placed = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Place 3D Tags"))
            {
                tx.Start();

                if (!tagFamily.IsActive)
                    tagFamily.Activate();

                foreach (Element el in targets)
                {
                    try
                    {
                        // Run full pipeline on the source element first
                        TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                            tagIndex, seqCounters, formulas, gridLines,
                            overwrite: false, skipComplete: true,
                            collisionMode: TagCollisionMode.AutoIncrement);

                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1))
                        {
                            skipped++;
                            continue;
                        }

                        // Track new tag
                        tagIndex.Add(tag1);

                        // Get element center point
                        XYZ center = GetElementCenter(el);
                        if (center == null) { skipped++; continue; }

                        // Place 3D tag family instance
                        FamilyInstance fi = doc.Create.NewFamilyInstance(
                            center, tagFamily, StructuralType.NonStructural);

                        if (fi != null)
                        {
                            // Write tag value to label parameter
                            ParameterHelpers.SetString(fi, TAG_3D_LABEL, tag1, overwrite: true);

                            // NG1/A6: Write full container pipeline for the placed 3D tag instance
                            try
                            {
                                string cat3D = ParameterHelpers.GetCategoryName(fi);
                                string[] toks3D = ParamRegistry.ReadTokenValues(fi);
                                ParamRegistry.WriteContainers(fi, toks3D, cat3D);
                                TagConfig.WriteTag7All(doc, fi, cat3D, toks3D, overwrite: false);
                                // NG5: Bridge native params for the placed instance
                                NativeParamMapper.MapAll(doc, fi);
                            }
                            catch (Exception pEx)
                            {
                                StingLog.Warn($"Tag3D pipeline for placed instance {fi.Id}: {pEx.Message}");
                            }

                            placed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Tag3D placement for {el.Id}: {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            // Save SEQ sidecar
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"Tag3D SaveSeqSidecar: {ssEx.Message}"); }

            ComplianceScan.InvalidateCache();

            TaskDialog.Show("Tag 3D",
                $"3D tags placed: {placed}\n" +
                $"Skipped: {skipped}\n\n" +
                $"Family: {tagFamily.FamilyName}");
            return Result.Succeeded;
        }

        /// <summary>Get element center point from location.</summary>
        private static XYZ GetElementCenter(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp) return lp.Point;
                if (el.Location is LocationCurve lc)
                    return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null)
                    return (bb.Min + bb.Max) / 2.0;
            }
            catch { }
            return null;
        }

        /// <summary>Find a 3D tag family in the document.</summary>
        private static FamilySymbol FindTagFamily(Document doc)
        {
            // Search by exact name first
            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            var exact = allTypes.FirstOrDefault(fs =>
                fs.FamilyName.Equals("STING_3D_Tag", StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Search by partial name
            var partial = allTypes.FirstOrDefault(fs =>
                fs.FamilyName.IndexOf("3D_Tag", StringComparison.OrdinalIgnoreCase) >= 0
                || fs.FamilyName.IndexOf("3DTag", StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return partial;

            // A6: Load from tag3DFamilyPath in project_config.json if not found in document
            return LoadTagFamilyFromConfig(doc);
        }

        /// <summary>A6: Load tag family from path specified in project_config.json.</summary>
        private static FamilySymbol LoadTagFamilyFromConfig(Document doc)
        {
            try
            {
                string docPath = doc.PathName ?? string.Empty;
                if (string.IsNullOrEmpty(docPath)) return null;
                string cfgPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(docPath) ?? string.Empty, "project_config.json");
                if (!System.IO.File.Exists(cfgPath)) return null;
                var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Dictionary<string, object>>(System.IO.File.ReadAllText(cfgPath));
                if (cfg == null || !cfg.TryGetValue("tag3DFamilyPath", out object pObj)
                    || !(pObj is string fp) || !System.IO.File.Exists(fp)) return null;
                if (doc.LoadFamily(fp, out Family fam) && fam != null)
                {
                    var sym = fam.GetFamilySymbolIds()
                        .Select(id => doc.GetElement(id) as FamilySymbol)
                        .FirstOrDefault(s => s != null);
                    if (sym != null) StingLog.Info($"Tag3D: loaded family from config: {fp}");
                    return sym;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D LoadTagFamilyFromConfig: {ex.Message}"); }
            return null;
        }
    }
}
