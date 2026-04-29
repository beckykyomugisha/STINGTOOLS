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

        /// <summary>
        /// Phase 165 — result envelope for programmatic Tag3D runs.
        /// </summary>
        public sealed class Tag3DResult
        {
            public int Placed   { get; set; }
            public int Enriched { get; set; }
            public int Errors   { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            View view = doc.ActiveView;
            if (view == null || !(view is View3D v3d))
            {
                TaskDialog.Show("Tag 3D", "Active view must be a 3D view.");
                return Result.Succeeded;
            }

            var r = PlaceTagsInView(doc, v3d, useTag7Narrative: false);
            string report = $"3D tags placed: {r.Placed}";
            if (r.Enriched > 0) report += $"\nElements enriched via pipeline: {r.Enriched}";
            if (r.Errors > 0)   report += $"\nErrors: {r.Errors}";
            TaskDialog.Show("Tag 3D", report);
            return Result.Succeeded;
        }

        /// <summary>
        /// Phase 165 — programmatic entry point used by AnnotationRunner when a
        /// DrawingType profile pack carries an <c>Auto3DTag</c> rule. Self-contained
        /// transaction-managed run; safe to invoke from event handlers and rule
        /// dispatch.
        /// </summary>
        /// <param name="useTag7Narrative">
        /// When true, the placed 3D tag's <c>ASS_TAG_3D_TXT</c> label receives
        /// the rich TAG7 plain-language narrative instead of the technical
        /// 8-segment tag — matches DrawingType displayMode 6.
        /// </param>
        public static Tag3DResult PlaceTagsInView(Document doc, View3D view, bool useTag7Narrative)
        {
            var r = new Tag3DResult();
            if (doc == null || view == null) return r;

            FamilySymbol tagSymbol = FindTagFamily(doc);
            if (tagSymbol == null)
            {
                r.Warnings.Add("No 3D tag family found. Load a Generic Model family with " +
                               $"a '{TAG_3D_LABEL}' label parameter, or set 'tag3DFamilyPath' " +
                               "in project_config.json.");
                StingLog.Warn(r.Warnings[r.Warnings.Count - 1]);
                return r;
            }
            PlaceTagsCore(doc, view, tagSymbol, useTag7Narrative, r);
            return r;
        }

        private static void PlaceTagsCore(Document doc, View view, FamilySymbol tagSymbol,
            bool useTag7Narrative, Tag3DResult result)
        {

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
                StingLog.Info($"Tag3D: no taggable elements in view '{view.Name}'.");
                return;
            }

            // TAG-02: Build pipeline context once for enriching untagged elements
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            if (tagIndex == null) tagIndex = new HashSet<string>();
            if (seqCounters == null) seqCounters = new Dictionary<string, int>();
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);

            using (Transaction tx = new Transaction(doc, "STING Tag 3D"))
            {
                tx.Start();

                if (!tagSymbol.IsActive) tagSymbol.Activate();

                foreach (Element el in elList)
                {
                    try
                    {
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                        // TAG-02: If element is untagged, run full pipeline to enrich it first
                        if (string.IsNullOrEmpty(tag1))
                        {
                            try
                            {
                                bool ok = TagPipelineHelper.RunFullPipeline(
                                    doc, el, popCtx, tagIndex, seqCounters,
                                    formulas, gridLines, overwrite: false,
                                    skipComplete: false, collisionMode: TagCollisionMode.AutoIncrement);
                                if (ok)
                                {
                                    tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                    result.Enriched++;
                                }
                            }
                            catch (Exception pipeEx)
                            {
                                StingLog.Warn($"Tag3D pipeline for {el.Id}: {pipeEx.Message}");
                            }
                        }
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

                        // Phase 165 — when the active DrawingType profile asks for the
                        // TAG7 narrative (displayMode 6), prefer the rich narrative as
                        // the visible label. Falls back to the technical tag if the
                        // narrative is empty (element not yet tagged with TAG7).
                        string label = tag1;
                        if (useTag7Narrative)
                        {
                            string narrative = ParameterHelpers.GetString(el, ParamRegistry.TAG7);
                            if (!string.IsNullOrEmpty(narrative)) label = narrative;
                        }

                        ParameterHelpers.SetString(fi, TAG_3D_LABEL, label, overwrite: true);

                        // Note: Containers/TAG7 already written to source element (el) by
                        // RunFullPipeline above. The annotation instance (fi) is just a
                        // visual marker — it has no STING token parameters bound.

                        result.Placed++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        StingLog.Warn($"Tag3D placement for {el.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            // FIX-B09: Invalidate caches and check compliance gate after 3D tagging
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"Tag3D SaveSeqSidecar: {ssEx.Message}"); }
            TagConfig.CheckComplianceGate(doc, "Tag3D");
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
                catch (Exception ex) { StingLog.Warn($"Tag family name check failed for '{fs?.Name}': {ex.Message}"); }
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
