// StingTools — Drawing Template Manager · Bonus
//
// DrawingCropApplier reads DrawingType.Crop and writes the matching
// Revit view-crop state. Crop kinds:
//
//   ScopeBox       use the scope box named in crop.scopeBoxName.
//                  Error if missing.
//   ScopeBoxOrBbox use named scope box if present, else tight bbox + margin.
//   TightBbox      compute element bbox and pad by crop.marginMm.
//   RoomBoundary   union of room boundaries + margin (plans only).
//   None           leave the view's default crop alone.
//
// Called by DrawingTypePresentation.Apply between view-template and
// annotation passes. Null or unparsable crop → no-op.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class DrawingCropApplier
    {
        // PERF-08 + FIX-3: per-view bbox union cache keyed by (docKey, viewId).
        // The union shifts only when the view's element set changes; cache
        // entries carry the cardinality at compute time so a stale entry
        // is detected and refreshed before it produces an out-of-date crop.
        // Evicted on DrawingTypeRegistry.Reload(doc) and on any element
        // count mismatch at lookup time.
        private sealed class BboxEntry
        {
            public BoundingBoxXYZ Union;
            public int            ElementCount;
        }
        private static readonly object _bboxLock = new object();
        private static readonly Dictionary<string, Dictionary<long, BboxEntry>> _bboxCache
            = new Dictionary<string, Dictionary<long, BboxEntry>>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        public static void InvalidateCache(Document doc)
        {
            string key = DocKey(doc);
            lock (_bboxLock)
            {
                if (_bboxCache.ContainsKey(key)) _bboxCache.Remove(key);
            }
        }

        public static List<string> Apply(Document doc, View view, DrawingType dt)
            => Apply(doc, view, dt, null);

        /// <summary>
        /// Apply the profile's crop strategy.
        /// </summary>
        /// <param name="contextScopeBox">
        /// P-6: a scope box supplied by the production context — the box the
        /// view was produced FOR. It wins over the profile's static
        /// Crop.ScopeBoxName, and over the bbox-derived kinds, because it is
        /// the reason the view exists. Previously DrawingContext.ScopeBox was
        /// declared, passed in by ProduceViewsFromScopeBoxesCommand, and read
        /// by nothing: "produce from scope boxes" produced views that were not
        /// cropped to their scope box, while the parallel
        /// GenerateFromScopeBoxesCommand did bind it — two entry points,
        /// materially different output.
        /// </param>
        public static List<string> Apply(Document doc, View view, DrawingType dt, Element contextScopeBox)
        {
            var warnings = new List<string>();
            if (doc == null || view == null || dt?.Crop == null) return warnings;
            if (view.IsTemplate) return warnings;

            var crop = dt.Crop;

            if (contextScopeBox != null && contextScopeBox.Id != ElementId.InvalidElementId)
            {
                SetScopeBox(view, contextScopeBox.Id, warnings);
                ApplyCropVisibility(view, crop, warnings);
                // Stamp the profile's declared kind so the drift detector still
                // compares against the profile, not against this override.
                try { DrawingTypeStamper.StampCrop(view, crop.Kind ?? string.Empty, crop.MarginMm); }
                catch (Exception ex) { warnings.Add($"CropStamp: {ex.Message}"); }
                return warnings;
            }

            try
            {
                switch ((crop.Kind ?? "").Trim())
                {
                    case "None":
                        return warnings;

                    case "ScopeBox":
                        {
                            var sb = FindScopeBox(doc, crop.ScopeBoxName);
                            if (sb == null)
                            {
                                warnings.Add($"Scope box '{crop.ScopeBoxName}' not found.");
                                return warnings;
                            }
                            SetScopeBox(view, sb.Id, warnings);
                            break;
                        }

                    case "ScopeBoxOrBbox":
                        {
                            var sb = FindScopeBox(doc, crop.ScopeBoxName);
                            if (sb != null) SetScopeBox(view, sb.Id, warnings);
                            else            SetTightBboxCrop(doc, view, crop.MarginMm, warnings);
                            break;
                        }

                    case "TightBbox":
                        SetTightBboxCrop(doc, view, crop.MarginMm, warnings);
                        break;

                    case "RoomBoundary":
                        SetRoomBoundaryCrop(doc, view, crop.MarginMm, warnings);
                        break;

                    default:
                        warnings.Add($"Unknown crop kind '{crop.Kind}' — no-op.");
                        break;
                }

                // One place decides whether the crop frame is drawn — the three
                // setters above no longer force it on.
                ApplyCropVisibility(view, crop, warnings);

                // Phase 183 — stamp crop kind + margin so the drift
                // detector can spot bbox-derived crops that have fallen
                // behind the profile. No-op when the params aren't bound.
                try { DrawingTypeStamper.StampCrop(view, crop.Kind ?? string.Empty, crop.MarginMm); }
                catch (Exception ex) { warnings.Add($"CropStamp: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                warnings.Add($"CropApplier: {ex.Message}");
            }
            return warnings;
        }

        private static Element FindScopeBox(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>
        /// Apply the profile's crop-boundary visibility. Previously SetScopeBox,
        /// SetTightBboxCrop and SetRoomBoundaryCrop each hardcoded
        /// CropBoxVisible = true, so every produced drawing carried a visible
        /// crop rectangle regardless of the profile. Now declared once, on
        /// DrawingCropStrategy, and defaulted off.
        /// </summary>
        private static void ApplyCropVisibility(View view, DrawingCropStrategy crop, List<string> warnings)
        {
            if (view == null || crop == null) return;
            try { view.CropBoxVisible = crop.CropBoxVisible; }
            catch (Exception ex) { warnings.Add($"CropBoxVisible: {ex.Message}"); }
        }

        private static void SetScopeBox(View view, ElementId scopeBoxId, List<string> warnings)
        {
            try
            {
                var p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (p == null || p.IsReadOnly) { warnings.Add("VIEWER_VOLUME_OF_INTEREST_CROP not writable."); return; }
                p.Set(scopeBoxId);
                view.CropBoxActive = true;
            }
            catch (Exception ex) { warnings.Add($"SetScopeBox: {ex.Message}"); }
        }

        private static void SetTightBboxCrop(Document doc, View view, double marginMm, List<string> warnings)
        {
            try
            {
                // PERF-08: cache the un-margined union per view; margin is
                // applied below on every call so repeated profile applies
                // (e.g. SyncStyles) skip the FilteredElementCollector pass.
                var union = GetOrComputeUnion(doc, view, warnings);
                if (union == null) { warnings.Add("TightBbox: view is empty, no crop applied."); return; }

                var cropBox = ApplyUnionToCropFrame(view, union, marginMm, warnings);
                if (cropBox == null) return;

                view.CropBoxActive  = true;
                view.CropBox        = cropBox;
            }
            catch (Exception ex) { warnings.Add($"TightBbox: {ex.Message}"); }
        }

        /// <summary>
        /// E-2: convert a MODEL-space bounding box into the view's own crop
        /// frame and apply the margin there.
        ///
        /// View.CropBox reads its Min/Max in the coordinate system carried by
        /// the crop box's Transform, not in model space. Assigning a
        /// model-space box with an identity transform only appears to work on
        /// an unrotated plan, where the two frames coincide in XY; on a
        /// section, an elevation or a rotated plan the crop lands somewhere
        /// arbitrary. The built-in spool profiles use TightBbox on sections,
        /// so this was the common case, not the exotic one.
        ///
        /// All eight corners are transformed, not just Min and Max: a rotation
        /// mixes the axes, so the frame-space extents are only correct if every
        /// corner is considered. The margin is then applied along the frame's
        /// own X and Y, which is what "margin" means on the drawing.
        /// </summary>
        private static BoundingBoxXYZ ApplyUnionToCropFrame(
            View view, BoundingBoxXYZ modelUnion, double marginMm, List<string> warnings)
        {
            try
            {
                var current = view.CropBox;
                var frame = current?.Transform ?? Transform.Identity;
                var inv = frame.Inverse;

                var mn = modelUnion.Min;
                var mx = modelUnion.Max;
                double loX = double.MaxValue, loY = double.MaxValue, loZ = double.MaxValue;
                double hiX = double.MinValue, hiY = double.MinValue, hiZ = double.MinValue;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new XYZ(
                        (i & 1) == 0 ? mn.X : mx.X,
                        (i & 2) == 0 ? mn.Y : mx.Y,
                        (i & 4) == 0 ? mn.Z : mx.Z);
                    var p = inv.OfPoint(corner);
                    if (p.X < loX) loX = p.X; if (p.X > hiX) hiX = p.X;
                    if (p.Y < loY) loY = p.Y; if (p.Y > hiY) hiY = p.Y;
                    if (p.Z < loZ) loZ = p.Z; if (p.Z > hiZ) hiZ = p.Z;
                }

                var marginFt = marginMm / 304.8;
                loX -= marginFt; hiX += marginFt;
                loY -= marginFt; hiY += marginFt;

                // Depth stays as the view had it — widening a crop's Z can
                // pull unrelated geometry into a section.
                if (current != null) { loZ = current.Min.Z; hiZ = current.Max.Z; }
                if (hiZ - loZ < 1e-6) { loZ -= 1.0; hiZ += 1.0; }

                return new BoundingBoxXYZ
                {
                    Transform = frame,
                    Min = new XYZ(loX, loY, loZ),
                    Max = new XYZ(hiX, hiY, hiZ),
                };
            }
            catch (Exception ex)
            {
                warnings.Add($"Crop frame conversion: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// DTW-172: categories that are model-typed or view-visible but say nothing
        /// about where the drawing's content is. Datums run edge to edge, scope
        /// boxes / section boxes / cameras are view furniture, and a link or CAD
        /// import's extents are its whole file.
        /// </summary>
        private static readonly HashSet<BuiltInCategory> ExtentNoise = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Grids, BuiltInCategory.OST_Levels, BuiltInCategory.OST_CLines,
            BuiltInCategory.OST_VolumeOfInterest, BuiltInCategory.OST_SectionBox,
            BuiltInCategory.OST_Cameras, BuiltInCategory.OST_Viewers, BuiltInCategory.OST_Elev,
            BuiltInCategory.OST_RvtLinks,
        };

        /// <summary>Does this element's extent describe the drawing's content?</summary>
        private static bool CountsForExtent(Element el)
        {
            if (el == null || el is ImportInstance) return false;
            var cat = el.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model) return false;
            return !ExtentNoise.Contains((BuiltInCategory)cat.Id.Value);
        }

        /// <summary>
        /// DTW-172: run <paramref name="measure"/> with the view's crop turned off, so a
        /// view-scoped collector sees everything the view would show uncropped. With
        /// the crop on, the collector only returned what the CURRENT crop showed, so
        /// a refresh could only ever shrink the crop, never grow it to new content.
        /// The caller owns the open transaction; the crop state is always restored.
        /// </summary>
        private static T WithCropOff<T>(Document doc, View view, Func<T> measure, List<string> warnings)
        {
            bool wasActive = false;
            try { wasActive = view.CropBoxActive; }
            catch (Exception ex) { warnings?.Add($"Crop state unreadable ({ex.Message}) — measured with the crop as it is."); }
            // Plans only: a plan's view range still bounds what an uncropped view
            // shows. A section or elevation with its crop lifted sees everything
            // within the far clip across the whole model width, so there the
            // current crop stays the measuring frame.
            if (!wasActive || !(view is ViewPlan)) return measure();
            try
            {
                view.CropBoxActive = false;
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                warnings?.Add($"Could not lift the crop to measure ({ex.Message}) — the extent is limited to the current crop.");
                try { view.CropBoxActive = true; } catch (Exception ex2) { StingLog.Warn($"DrawingCropApplier: restore crop: {ex2.Message}"); }
                return measure();
            }
            try { return measure(); }
            finally
            {
                try { view.CropBoxActive = true; }
                catch (Exception ex) { warnings?.Add($"Could not restore the crop after measuring: {ex.Message}"); }
            }
        }

        private static BoundingBoxXYZ GetOrComputeUnion(Document doc, View view, List<string> warnings)
        {
            string key = DocKey(doc);
            long viewKey = view.Id.Value;

            return WithCropOff(doc, view, () =>
            {
                // FIX-3: cheap element-count fingerprint. Element movement alone
                // won't trigger a refresh, which is acceptable for the crop-margin
                // use case (margin dominates the visible result), but element add /
                // delete is captured.
                var members = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(CountsForExtent)
                    .ToList();
                int currentCount = members.Count;

                lock (_bboxLock)
                {
                    if (_bboxCache.TryGetValue(key, out var docMap)
                        && docMap.TryGetValue(viewKey, out var cached)
                        && cached.ElementCount == currentCount
                        && cached.Union != null)
                        return cached.Union;
                }

                var union = UnionOf(members, view);
                lock (_bboxLock)
                {
                    if (!_bboxCache.TryGetValue(key, out var docMap))
                        _bboxCache[key] = docMap = new Dictionary<long, BboxEntry>();
                    docMap[viewKey] = new BboxEntry { Union = union, ElementCount = currentCount };
                }
                return union;
            }, warnings);
        }

        private static BoundingBoxXYZ UnionOf(IEnumerable<Element> elements, View view)
        {
            BoundingBoxXYZ union = null;
            foreach (var el in elements)
            {
                BoundingBoxXYZ bb = null;
                try { bb = el.get_BoundingBox(view); }
                catch (Exception ex) { StingLog.WarnRateLimited("DrawingCropApplier.Bbox", $"Bounding box of {el.Id}: {ex.Message}"); }
                if (bb == null) continue;
                if (union == null) { union = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max }; continue; }
                union.Min = new XYZ(Math.Min(union.Min.X, bb.Min.X),
                                     Math.Min(union.Min.Y, bb.Min.Y),
                                     Math.Min(union.Min.Z, bb.Min.Z));
                union.Max = new XYZ(Math.Max(union.Max.X, bb.Max.X),
                                     Math.Max(union.Max.Y, bb.Max.Y),
                                     Math.Max(union.Max.Z, bb.Max.Z));
            }
            return union;
        }

        private static void SetRoomBoundaryCrop(Document doc, View view, double marginMm, List<string> warnings)
        {
            try
            {
                // DTW-172: measured with the crop lifted, so rooms outside the
                // current crop count and a refresh can grow the crop as well as shrink it.
                int roomCount = 0;
                var union = WithCropOff(doc, view, () =>
                {
                    var rooms = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .ToList();
                    roomCount = rooms.Count;
                    return UnionOf(rooms, view);
                }, warnings);
                if (roomCount == 0) { warnings.Add("RoomBoundary: no rooms in view, falling back to TightBbox."); SetTightBboxCrop(doc, view, marginMm, warnings); return; }
                if (union == null) { warnings.Add("RoomBoundary: no room bboxes."); return; }

                // E-2: same model-space-into-crop-frame conversion as
                // TightBbox. The room union is gathered in model space; the
                // margin is applied in frame coords inside the helper.
                var cropBox = ApplyUnionToCropFrame(view, union, marginMm, warnings);
                if (cropBox == null) return;

                view.CropBoxActive  = true;
                view.CropBox        = cropBox;
            }
            catch (Exception ex) { warnings.Add($"RoomBoundary: {ex.Message}"); }
        }
    }
}
