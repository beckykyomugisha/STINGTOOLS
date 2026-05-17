using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

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

        // Preferred idempotency stamp — dedicated shared parameter on the
        // placed FamilyInstance. Bound to OST_GenericModel via
        // MR_PARAMETERS.csv; LoadSharedParams binds it to projects that
        // run the standard setup. Stores ElementId.Value (Int64) as a
        // decimal string so values exceeding Int32 still survive.
        private const string HOST_ID_PARAM = "STING_TAG3D_HOST_ID_TXT";

        // Fallback idempotency stamp written to Comments when the
        // dedicated shared parameter isn't bound on the project. Marker
        // prefix lets us round-trip the host id without colliding with
        // user-authored Comments. The existing-host-id scan parses both
        // forms — projects that later run LoadSharedParams to bind the
        // dedicated param continue to recognise legacy stamps.
        private const string HOST_STAMP_PREFIX = "[STING3D host=";
        private const string HOST_STAMP_SUFFIX = "]";
        private const string LINK_STAMP_PREFIX = "[STING3D link=";
        private const string LINK_STAMP_SUFFIX = "]";

        // Annotation workset name we prefer when present. If missing the
        // FamilyInstance lands on the user's active workset, which is fine.
        private const string ANNOTATION_WORKSET_NAME = "STING-Annotations";

        public enum SkipReason
        {
            NoLocation,
            AlreadyTagged,
            OwnedByOtherUser,
            NoTagAfterPipeline,
            ExceptionDuringPlacement
        }

        /// <summary>Result envelope for programmatic Tag3D runs (Phase 165 +).</summary>
        public sealed class Tag3DResult
        {
            public int Placed       { get; set; }
            public int Enriched     { get; set; }
            public int Skipped      { get; set; }
            public int Errors       { get; set; }
            public int LinkedPlaced { get; set; }
            public Dictionary<SkipReason, int> SkipReasons { get; }
                = new Dictionary<SkipReason, int>();
            public List<string> Warnings { get; } = new List<string>();

            internal void RecordSkip(SkipReason reason)
            {
                Skipped++;
                if (!SkipReasons.ContainsKey(reason)) SkipReasons[reason] = 0;
                SkipReasons[reason]++;
            }
        }

        // ── Per-project placement config ──────────────────────────────────

        private enum AnchorMode { Centroid, TopOfBbox }

        private sealed class PlacementConfig
        {
            // Defaults preserve historic behaviour (centroid + 1 ft) so
            // upgrading existing projects doesn't shift the visual position
            // of every 3D tag. Projects that prefer top-of-bounding-box
            // placement opt in via project_config.json:
            //   { "tag3DPlacement": {
            //       "anchor": "TopOfBbox",
            //       "defaultOffsetMm": 300,
            //       "perCategoryOffsetMm": { "Mechanical Equipment": 600 }
            //   } }
            public AnchorMode Anchor { get; set; } = AnchorMode.Centroid;
            public double DefaultOffsetFt { get; set; } = 1.0; // 1 ft, original
            public Dictionary<string, double> PerCategoryOffsetFt { get; set; }
                = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public bool IncludeLinked { get; set; } = false;
            public bool PinPlacedInstances { get; set; } = true;
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
            if (view.IsTemplate)
            {
                TaskDialog.Show("Tag 3D", "Active view is a view template; nothing placed.");
                return Result.Succeeded;
            }

            // Selection scope — if user has an active selection, ask whether to
            // restrict to it. Default for empty selection is whole-view.
            ICollection<ElementId> selected = null;
            try { selected = uidoc.Selection.GetElementIds(); } catch { /* defensive */ }

            HashSet<ElementId> hostFilter = null;
            if (selected != null && selected.Count > 0)
            {
                var td = new TaskDialog("Tag 3D")
                {
                    MainInstruction = $"You have {selected.Count:N0} element(s) selected.",
                    MainContent = "Tag only the selection, or every taggable element in the view?"
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tag selected only");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Tag entire view");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                td.DefaultButton = TaskDialogResult.CommandLink1;
                var resp = td.Show();
                if (resp == TaskDialogResult.Cancel) return Result.Cancelled;
                if (resp == TaskDialogResult.CommandLink1)
                    hostFilter = new HashSet<ElementId>(selected);
            }

            var progress = StingProgressDialog.Show("Tag 3D", 0);
            Tag3DResult r;
            try
            {
                r = PlaceTagsInView(doc, v3d,
                    useTag7Narrative: false,
                    hostFilter: hostFilter,
                    wrapTransaction: true,
                    progress: progress);
            }
            finally
            {
                try { progress.Close(); } catch { /* defensive */ }
            }

            string report = $"3D tags placed: {r.Placed}";
            if (r.LinkedPlaced > 0) report += $" (incl. {r.LinkedPlaced} on linked elements)";
            if (r.Enriched > 0)     report += $"\nElements enriched via pipeline: {r.Enriched}";
            if (r.Skipped > 0)
            {
                report += $"\nSkipped: {r.Skipped}";
                foreach (var kv in r.SkipReasons.OrderByDescending(k => k.Value))
                    report += $"\n  • {kv.Key}: {kv.Value}";
            }
            if (r.Errors > 0)       report += $"\nErrors: {r.Errors}";
            if (r.Warnings.Count > 0)
            {
                report += "\n\nWarnings:";
                foreach (var w in r.Warnings.Take(5)) report += $"\n  • {w}";
                if (r.Warnings.Count > 5) report += $"\n  • (+{r.Warnings.Count - 5} more)";
            }

            TaskDialog.Show("Tag 3D", report);
            TokenAutoPopulator.PopulationContext.EndSession();
            return Result.Succeeded;
        }

        /// <summary>
        /// Programmatic entry point used by AnnotationRunner when a DrawingType
        /// profile pack carries an <c>Auto3DTag</c> rule. Pass
        /// <paramref name="wrapTransaction"/>=false when called from inside an
        /// already-open Transaction (the AnnotationRunner contract).
        /// </summary>
        /// <param name="useTag7Narrative">
        /// When true, the placed 3D tag's <c>ASS_TAG_3D_TXT</c> label receives
        /// the rich TAG7 plain-language narrative instead of the technical
        /// 8-segment tag — matches DrawingType displayMode 6.
        /// </param>
        public static Tag3DResult PlaceTagsInView(Document doc, View3D view, bool useTag7Narrative)
            => PlaceTagsInView(doc, view, useTag7Narrative,
                hostFilter: null, wrapTransaction: true, progress: null);

        /// <summary>
        /// Full-control entry point. <paramref name="hostFilter"/> restricts
        /// tagging to those host element ids when non-null;
        /// <paramref name="wrapTransaction"/>=false skips the internal
        /// Transaction so the call can nest under an existing one;
        /// <paramref name="progress"/> is updated per element when supplied.
        /// </summary>
        public static Tag3DResult PlaceTagsInView(
            Document doc, View3D view, bool useTag7Narrative,
            ICollection<ElementId> hostFilter,
            bool wrapTransaction,
            StingProgressDialog progress)
        {
            var r = new Tag3DResult();
            if (doc == null || view == null) return r;
            if (view.IsTemplate)
            {
                r.Warnings.Add($"View '{view.Name}' is a template; nothing placed.");
                return r;
            }

            FamilySymbol tagSymbol = FindTagFamily(doc);
            if (tagSymbol == null)
            {
                r.Warnings.Add("No 3D tag family found. Load a Generic Model family with " +
                               $"a '{TAG_3D_LABEL}' label parameter, or set 'tag3DFamilyPath' " +
                               "in project_config.json.");
                StingLog.Warn(r.Warnings[r.Warnings.Count - 1]);
                return r;
            }

            var cfg = LoadPlacementConfig(doc);

            if (wrapTransaction)
            {
                using (var tx = new Transaction(doc, "STING Tag 3D"))
                {
                    tx.Start();
                    PlaceTagsCore(doc, view, tagSymbol, useTag7Narrative,
                        hostFilter, cfg, progress, r);
                    tx.Commit();
                }
            }
            else
            {
                // Caller owns the open Transaction (AnnotationRunner contract).
                PlaceTagsCore(doc, view, tagSymbol, useTag7Narrative,
                    hostFilter, cfg, progress, r);
            }

            // Cache invalidation + sidecar save can happen outside the transaction.
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            return r;
        }

        private static void PlaceTagsCore(
            Document doc, View view, FamilySymbol tagSymbol,
            bool useTag7Narrative, ICollection<ElementId> hostFilter,
            PlacementConfig cfg, StingProgressDialog progress, Tag3DResult result)
        {
            // 1. Activate symbol + regen so NewFamilyInstance sees the live type.
            if (!tagSymbol.IsActive)
            {
                tagSymbol.Activate();
                doc.Regenerate();
            }

            // 2. Validate the chosen family carries the label we will write to.
            //    If not, abort the whole run — placing instances we can't label
            //    is pure noise.
            if (!ValidateFamilyHasLabel(doc, tagSymbol, result))
                return;

            // 3. Collect taggable elements in this view.
            var catEnums = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> viewElements;
            if (catEnums != null && catEnums.Length > 0)
                viewElements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            else
                viewElements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();

            var elList = viewElements
                .Where(e => hostFilter == null || hostFilter.Contains(e.Id))
                .ToList();

            int totalForProgress = elList.Count + (cfg.IncludeLinked ? 0 : 0);
            if (progress != null) progress.UpdateTotal(totalForProgress);

            if (elList.Count == 0 && !cfg.IncludeLinked)
            {
                StingLog.Info($"Tag3D: no taggable elements in view '{view.Name}'.");
                return;
            }

            // 4. Build pipeline context once.
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            if (tagIndex == null) tagIndex = new HashSet<string>();
            if (seqCounters == null) seqCounters = new Dictionary<string, int>();
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);

            // 5. Idempotency: scan existing 3D tag instances in this view and
            //    capture host element ids already tagged. Skip those.
            var alreadyTagged = BuildExistingHostIdSet(doc, view, tagSymbol);

            // 6. Resolve preferred annotation workset (optional).
            WorksetId annotationWorksetId = ResolveAnnotationWorkset(doc);

            int annotationWorksetSkips = 0;

            foreach (Element el in elList)
            {
                try
                {
                    if (progress != null && progress.IsCancelled) break;

                    if (alreadyTagged.Contains(el.Id.Value))
                    {
                        result.RecordSkip(SkipReason.AlreadyTagged);
                        continue;
                    }

                    // Workshared safety: skip elements owned by another user
                    // before attempting any write.
                    if (!IsEditable(doc, el))
                    {
                        result.RecordSkip(SkipReason.OwnedByOtherUser);
                        continue;
                    }

                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                    // Run full pipeline for untagged hosts so we never label
                    // a 3D tag with an empty string.
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
                    if (string.IsNullOrEmpty(tag1))
                    {
                        result.RecordSkip(SkipReason.NoTagAfterPipeline);
                        continue;
                    }

                    XYZ tagPoint = ComputeTagPoint(el, cfg);
                    if (tagPoint == null)
                    {
                        result.RecordSkip(SkipReason.NoLocation);
                        continue;
                    }

                    var fi = doc.Create.NewFamilyInstance(
                        tagPoint, tagSymbol,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    if (fi == null) continue;

                    string label = tag1;
                    if (useTag7Narrative)
                    {
                        string narrative = ParameterHelpers.GetString(el, ParamRegistry.TAG7);
                        if (!string.IsNullOrEmpty(narrative)) label = narrative;
                    }
                    ParameterHelpers.SetString(fi, TAG_3D_LABEL, label, overwrite: true);

                    StampHostId(fi, el.Id);
                    if (cfg.PinPlacedInstances) TryPin(fi);
                    if (annotationWorksetId != WorksetId.InvalidWorksetId)
                    {
                        if (!TryAssignWorkset(fi, annotationWorksetId))
                            annotationWorksetSkips++;
                    }

                    result.Placed++;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.RecordSkip(SkipReason.ExceptionDuringPlacement);
                    StingLog.Warn($"Tag3D placement for {el.Id}: {ex.Message}");
                }
                finally
                {
                    progress?.Increment();
                }
            }

            // 7. Optionally label elements in linked Revit files.
            if (cfg.IncludeLinked)
                PlaceLinkedTags(doc, view, tagSymbol, useTag7Narrative, cfg, result);

            if (annotationWorksetSkips > 0)
                result.Warnings.Add(
                    $"{annotationWorksetSkips} placed instance(s) could not be moved to " +
                    $"workset '{ANNOTATION_WORKSET_NAME}'.");

            // 8. Persist seq sidecar + run compliance gate.
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"Tag3D SaveSeqSidecar: {ssEx.Message}"); }
            TagConfig.CheckComplianceGate(doc, "Tag3D");
        }

        // ── Family resolution + validation ────────────────────────────────

        private static FamilySymbol FindTagFamily(Document doc)
        {
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
                    string famName = fs.Family?.Name?.ToUpperInvariant() ?? "";
                    if (famName.Contains("TAG") || famName.Contains("3D"))
                    {
                        StingLog.Info($"Tag3D: found family '{fs.Family?.Name}' type '{fs.Name}'");
                        return fs;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Tag family name check failed for '{fs?.Name}': {ex.Message}"); }
            }

            var fallback = candidates.FirstOrDefault();
            if (fallback != null)
            {
                StingLog.Info($"Tag3D: using fallback family '{fallback.Family?.Name}'");
                return fallback;
            }

            // Fallback: load from project_config.json tag3DFamilyPath.
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

        // Per-document cache of validated family ids — reused across runs and
        // across the (interactive, AnnotationRunner) entry points.
        private static readonly Dictionary<string, bool> s_validatedFamilies
            = new Dictionary<string, bool>();

        /// <summary>
        /// Probe-place a temporary instance inside a SubTransaction to confirm
        /// the family carries the <c>ASS_TAG_3D_TXT</c> label. The probe is
        /// rolled back so nothing is left in the model.
        /// </summary>
        private static bool ValidateFamilyHasLabel(Document doc, FamilySymbol fs, Tag3DResult result)
        {
            string key = $"{doc.PathName}::{fs.Family?.Name}::{fs.Name}";
            if (s_validatedFamilies.TryGetValue(key, out bool cached))
            {
                if (!cached)
                    result.Warnings.Add(
                        $"Tag3D family '{fs.Family?.Name}' has no '{TAG_3D_LABEL}' parameter.");
                return cached;
            }

            bool valid = false;
            try
            {
                using (var sub = new SubTransaction(doc))
                {
                    sub.Start();
                    try
                    {
                        if (!fs.IsActive) fs.Activate();
                        doc.Regenerate();
                        var fi = doc.Create.NewFamilyInstance(
                            XYZ.Zero, fs,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        valid = fi != null && fi.LookupParameter(TAG_3D_LABEL) != null;
                    }
                    finally
                    {
                        sub.RollBack();
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Tag3D family probe '{fs.Family?.Name}': {ex.Message}");
                result.Warnings.Add(
                    $"Tag3D family '{fs.Family?.Name}' could not be probed: {ex.Message}");
                valid = false;
            }

            s_validatedFamilies[key] = valid;
            if (!valid)
                result.Warnings.Add(
                    $"Tag3D family '{fs.Family?.Name}' has no '{TAG_3D_LABEL}' parameter; " +
                    "load a 3D tag family with that label and retry.");
            return valid;
        }

        // ── Placement geometry ────────────────────────────────────────────

        private static XYZ ComputeTagPoint(Element el, PlacementConfig cfg)
        {
            XYZ basePt = GetElementCenter(el);
            if (basePt == null) return null;

            double offsetFt = cfg.DefaultOffsetFt;
            string catName = el.Category?.Name;
            if (!string.IsNullOrEmpty(catName)
                && cfg.PerCategoryOffsetFt.TryGetValue(catName, out double catOffset))
                offsetFt = catOffset;

            if (cfg.Anchor == AnchorMode.TopOfBbox)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null)
                    return new XYZ(basePt.X, basePt.Y, bb.Max.Z + offsetFt);
            }
            return new XYZ(basePt.X, basePt.Y, basePt.Z + offsetFt);
        }

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

        // ── Idempotency ───────────────────────────────────────────────────

        private static HashSet<long> BuildExistingHostIdSet(Document doc, View view, FamilySymbol tagSymbol)
        {
            var hosts = new HashSet<long>();
            try
            {
                var familyId = tagSymbol.Family?.Id;
                var existing = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => familyId == null || fi.Symbol?.Family?.Id == familyId);
                foreach (var fi in existing)
                {
                    long? hostId = ReadStampedHostId(fi);
                    if (hostId.HasValue) hosts.Add(hostId.Value);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Tag3D BuildExistingHostIdSet: {ex.Message}");
            }
            return hosts;
        }

        private static void StampHostId(FamilyInstance fi, ElementId hostId)
        {
            string idStr = hostId.Value.ToString();

            // Preferred: dedicated shared parameter — no collision with
            // user-authored Comments.
            try
            {
                var dedicated = fi.LookupParameter(HOST_ID_PARAM);
                if (dedicated != null && !dedicated.IsReadOnly
                    && dedicated.StorageType == StorageType.String)
                {
                    dedicated.Set(idStr);
                    return;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D StampHostId (dedicated): {ex.Message}"); }

            // Fallback: Comments with marker prefix.
            try
            {
                var p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p == null || p.IsReadOnly) return;
                p.Set($"{HOST_STAMP_PREFIX}{idStr}{HOST_STAMP_SUFFIX}");
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D StampHostId (comments): {ex.Message}"); }
        }

        private static long? ReadStampedHostId(FamilyInstance fi)
        {
            // Preferred: dedicated shared parameter.
            try
            {
                var dedicated = fi.LookupParameter(HOST_ID_PARAM);
                string s = dedicated?.AsString();
                if (!string.IsNullOrEmpty(s) && long.TryParse(s, out long id))
                    return id;
            }
            catch { /* defensive */ }

            // Fallback: parse legacy Comments stamp.
            try
            {
                var p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                string s = p?.AsString();
                if (string.IsNullOrEmpty(s)) return null;
                int i = s.IndexOf(HOST_STAMP_PREFIX, StringComparison.Ordinal);
                if (i < 0) return null;
                int start = i + HOST_STAMP_PREFIX.Length;
                int end = s.IndexOf(HOST_STAMP_SUFFIX, start, StringComparison.Ordinal);
                if (end <= start) return null;
                if (long.TryParse(s.Substring(start, end - start), out long id)) return id;
            }
            catch { /* defensive */ }
            return null;
        }

        private static void StampLinkedHostKey(FamilyInstance fi, string hostKey)
        {
            // Linked stamps go into the dedicated param when available
            // (prefixed so we can distinguish from local-host stamps),
            // else fall back to a marker-prefixed Comments string.
            try
            {
                var dedicated = fi.LookupParameter(HOST_ID_PARAM);
                if (dedicated != null && !dedicated.IsReadOnly
                    && dedicated.StorageType == StorageType.String)
                {
                    dedicated.Set("link:" + hostKey);
                    return;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D StampLinkedHostKey (dedicated): {ex.Message}"); }

            try
            {
                var p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (p == null || p.IsReadOnly) return;
                p.Set($"{LINK_STAMP_PREFIX}{hostKey}{LINK_STAMP_SUFFIX}");
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D StampLinkedHostKey (comments): {ex.Message}"); }
        }

        private static string ReadStampedLinkedHostKey(FamilyInstance fi)
        {
            try
            {
                var dedicated = fi.LookupParameter(HOST_ID_PARAM);
                string s = dedicated?.AsString();
                if (!string.IsNullOrEmpty(s) && s.StartsWith("link:", StringComparison.Ordinal))
                    return s.Substring("link:".Length);
            }
            catch { /* defensive */ }

            try
            {
                var p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                string s = p?.AsString();
                if (string.IsNullOrEmpty(s)) return null;
                int i = s.IndexOf(LINK_STAMP_PREFIX, StringComparison.Ordinal);
                if (i < 0) return null;
                int start = i + LINK_STAMP_PREFIX.Length;
                int end = s.IndexOf(LINK_STAMP_SUFFIX, start, StringComparison.Ordinal);
                if (end <= start) return null;
                return s.Substring(start, end - start);
            }
            catch { /* defensive */ }
            return null;
        }

        // ── Workshared / pin / workset helpers ────────────────────────────

        private static bool IsEditable(Document doc, Element el)
        {
            try
            {
                if (!doc.IsWorkshared) return true;
                var status = WorksharingUtils.GetCheckoutStatus(doc, el.Id);
                return status != CheckoutStatus.OwnedByOtherUser;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; }
        }

        private static void TryPin(FamilyInstance fi)
        {
            try { fi.Pinned = true; }
            catch (Exception ex) { StingLog.Warn($"Tag3D pin: {ex.Message}"); }
        }

        private static WorksetId ResolveAnnotationWorkset(Document doc)
        {
            try
            {
                if (!doc.IsWorkshared) return WorksetId.InvalidWorksetId;
                var ws = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .FirstOrDefault(w => string.Equals(w.Name, ANNOTATION_WORKSET_NAME,
                        StringComparison.OrdinalIgnoreCase));
                return ws?.Id ?? WorksetId.InvalidWorksetId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return WorksetId.InvalidWorksetId; }
        }

        private static bool TryAssignWorkset(FamilyInstance fi, WorksetId wsId)
        {
            try
            {
                var p = fi.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (p == null || p.IsReadOnly) return false;
                p.Set(wsId.IntegerValue);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Tag3D workset assign: {ex.Message}");
                return false;
            }
        }

        // ── Linked-element placement ──────────────────────────────────────

        private static void PlaceLinkedTags(
            Document doc, View view, FamilySymbol tagSymbol,
            bool useTag7Narrative, PlacementConfig cfg, Tag3DResult result)
        {
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(li => li.GetLinkDocument() != null)
                .ToList();
            if (links.Count == 0) return;

            // Build a host-id set keyed by "<linkInstanceId>:<linkedElementId>"
            // stamped into the placed instance via the dedicated shared
            // param ("link:<key>") or legacy Comments ([STING3D link=<key>]).
            var alreadyTaggedLinked = new HashSet<string>();
            try
            {
                var familyId = tagSymbol.Family?.Id;
                var existing = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => familyId == null || fi.Symbol?.Family?.Id == familyId);
                foreach (var fi in existing)
                {
                    string key = ReadStampedLinkedHostKey(fi);
                    if (!string.IsNullOrEmpty(key)) alreadyTaggedLinked.Add(key);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D linked-host scan: {ex.Message}"); }

            foreach (var link in links)
            {
                try
                {
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;
                    Transform xf = link.GetTotalTransform();

                    var catEnums = SharedParamGuids.AllCategoryEnums;
                    IEnumerable<Element> linkEls;
                    if (catEnums != null && catEnums.Length > 0)
                        linkEls = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType()
                            .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                    else
                        linkEls = new FilteredElementCollector(linkDoc)
                            .WhereElementIsNotElementType();

                    foreach (var le in linkEls)
                    {
                        try
                        {
                            string hostKey = $"{link.Id.Value}:{le.Id.Value}";
                            if (alreadyTaggedLinked.Contains(hostKey))
                            {
                                result.RecordSkip(SkipReason.AlreadyTagged);
                                continue;
                            }

                            string tag1 = ParameterHelpers.GetString(le, ParamRegistry.TAG1);
                            // Linked elements are read-only — we can't run the
                            // pipeline against them. Skip if untagged.
                            if (string.IsNullOrEmpty(tag1))
                            {
                                result.RecordSkip(SkipReason.NoTagAfterPipeline);
                                continue;
                            }

                            XYZ basePt = GetElementCenter(le);
                            if (basePt == null)
                            {
                                result.RecordSkip(SkipReason.NoLocation);
                                continue;
                            }

                            double offsetFt = cfg.DefaultOffsetFt;
                            string catName = le.Category?.Name;
                            if (!string.IsNullOrEmpty(catName)
                                && cfg.PerCategoryOffsetFt.TryGetValue(catName, out double catOffset))
                                offsetFt = catOffset;

                            XYZ localTagPoint = (cfg.Anchor == AnchorMode.TopOfBbox && le.get_BoundingBox(null) is BoundingBoxXYZ bb)
                                ? new XYZ(basePt.X, basePt.Y, bb.Max.Z + offsetFt)
                                : new XYZ(basePt.X, basePt.Y, basePt.Z + offsetFt);

                            XYZ worldPt = xf.OfPoint(localTagPoint);

                            var fi = doc.Create.NewFamilyInstance(
                                worldPt, tagSymbol,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            if (fi == null) continue;

                            string label = tag1;
                            if (useTag7Narrative)
                            {
                                string narrative = ParameterHelpers.GetString(le, ParamRegistry.TAG7);
                                if (!string.IsNullOrEmpty(narrative)) label = narrative;
                            }
                            ParameterHelpers.SetString(fi, TAG_3D_LABEL, label, overwrite: true);

                            StampLinkedHostKey(fi, hostKey);

                            if (cfg.PinPlacedInstances) TryPin(fi);

                            result.LinkedPlaced++;
                            result.Placed++;
                        }
                        catch (Exception inner)
                        {
                            result.Errors++;
                            StingLog.Warn($"Tag3D linked placement {le?.Id}: {inner.Message}");
                        }
                    }
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"Tag3D link '{link?.Name}': {ex.Message}");
                    result.Warnings.Add($"Linked file '{link?.Name}' skipped: {ex.Message}");
                }
            }
        }

        // ── project_config.json reader ────────────────────────────────────

        private static PlacementConfig LoadPlacementConfig(Document doc)
        {
            var cfg = new PlacementConfig();
            try
            {
                string docPath = doc.PathName ?? string.Empty;
                if (string.IsNullOrEmpty(docPath)) return cfg;
                string cfgPath = Path.Combine(
                    Path.GetDirectoryName(docPath) ?? string.Empty,
                    "project_config.json");
                if (!File.Exists(cfgPath)) return cfg;

                string json = File.ReadAllText(cfgPath);
                var root = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (root == null) return cfg;

                if (root.TryGetValue("tag3DPlacement", out object pObj)
                    && pObj is Newtonsoft.Json.Linq.JObject placement)
                {
                    string anchor = placement.Value<string>("anchor");
                    if (string.Equals(anchor, "Centroid", StringComparison.OrdinalIgnoreCase))
                        cfg.Anchor = AnchorMode.Centroid;

                    var defaultMm = placement.Value<double?>("defaultOffsetMm");
                    if (defaultMm.HasValue) cfg.DefaultOffsetFt = defaultMm.Value / 304.8;

                    var pinObj = placement.Value<bool?>("pinPlacedInstances");
                    if (pinObj.HasValue) cfg.PinPlacedInstances = pinObj.Value;

                    var includeLinked = placement.Value<bool?>("includeLinked");
                    if (includeLinked.HasValue) cfg.IncludeLinked = includeLinked.Value;

                    var perCat = placement["perCategoryOffsetMm"] as Newtonsoft.Json.Linq.JObject;
                    if (perCat != null)
                    {
                        foreach (var prop in perCat.Properties())
                        {
                            // JToken supports an explicit cast to nullable
                            // primitives; JToken.Value<T> requires a key
                            // argument, which doesn't apply here.
                            double? mm = (double?)prop.Value;
                            if (mm.HasValue)
                                cfg.PerCategoryOffsetFt[prop.Name] = mm.Value / 304.8;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Tag3D LoadPlacementConfig: {ex.Message}"); }
            return cfg;
        }
    }
}
