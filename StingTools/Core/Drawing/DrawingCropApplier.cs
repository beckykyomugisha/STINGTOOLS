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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "__unknown__"; }
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
        {
            var warnings = new List<string>();
            if (doc == null || view == null || dt?.Crop == null) return warnings;
            if (view.IsTemplate) return warnings;

            var crop = dt.Crop;
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static void SetScopeBox(View view, ElementId scopeBoxId, List<string> warnings)
        {
            try
            {
                var p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (p == null || p.IsReadOnly) { warnings.Add("VIEWER_VOLUME_OF_INTEREST_CROP not writable."); return; }
                p.Set(scopeBoxId);
                view.CropBoxActive = true;
                view.CropBoxVisible = true;
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

                var marginFt = (marginMm / 304.8);
                var withMargin = new BoundingBoxXYZ
                {
                    Min = union.Min - new XYZ(marginFt, marginFt, 0),
                    Max = union.Max + new XYZ(marginFt, marginFt, 0),
                };

                view.CropBoxActive  = true;
                view.CropBoxVisible = true;
                view.CropBox        = withMargin;
            }
            catch (Exception ex) { warnings.Add($"TightBbox: {ex.Message}"); }
        }

        private static BoundingBoxXYZ GetOrComputeUnion(Document doc, View view, List<string> warnings)
        {
            string key = DocKey(doc);
            long viewKey = view.Id.Value;

            // FIX-3: cheap element-count fingerprint. ID() runs O(1) per
            // element via the FilteredElementCollector; element movement
            // alone won't trigger a refresh, which is acceptable for the
            // crop-margin use case (margin dominates the visible result),
            // but element add / delete is captured.
            int currentCount = 0;
            try
            {
                currentCount = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType().GetElementCount();
            }
            catch { /* fall through; treat as forced re-compute */ }

            lock (_bboxLock)
            {
                if (_bboxCache.TryGetValue(key, out var docMap)
                    && docMap.TryGetValue(viewKey, out var cached)
                    && cached.ElementCount == currentCount
                    && cached.Union != null)
                    return cached.Union;
            }

            BoundingBoxXYZ union = null;
            int counted = 0;
            foreach (var el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                counted++;
                var bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                if (union == null) { union = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max }; continue; }
                union.Min = new XYZ(Math.Min(union.Min.X, bb.Min.X),
                                     Math.Min(union.Min.Y, bb.Min.Y),
                                     Math.Min(union.Min.Z, bb.Min.Z));
                union.Max = new XYZ(Math.Max(union.Max.X, bb.Max.X),
                                     Math.Max(union.Max.Y, bb.Max.Y),
                                     Math.Max(union.Max.Z, bb.Max.Z));
            }

            lock (_bboxLock)
            {
                if (!_bboxCache.TryGetValue(key, out var docMap))
                    _bboxCache[key] = docMap = new Dictionary<long, BboxEntry>();
                docMap[viewKey] = new BboxEntry { Union = union, ElementCount = counted };
            }
            return union;
        }

        private static void SetRoomBoundaryCrop(Document doc, View view, double marginMm, List<string> warnings)
        {
            try
            {
                var rooms = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (rooms.Count == 0) { warnings.Add("RoomBoundary: no rooms in view, falling back to TightBbox."); SetTightBboxCrop(doc, view, marginMm, warnings); return; }

                BoundingBoxXYZ union = null;
                foreach (var r in rooms)
                {
                    var bb = r.get_BoundingBox(view);
                    if (bb == null) continue;
                    if (union == null) { union = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max }; continue; }
                    union.Min = new XYZ(Math.Min(union.Min.X, bb.Min.X),
                                         Math.Min(union.Min.Y, bb.Min.Y),
                                         Math.Min(union.Min.Z, bb.Min.Z));
                    union.Max = new XYZ(Math.Max(union.Max.X, bb.Max.X),
                                         Math.Max(union.Max.Y, bb.Max.Y),
                                         Math.Max(union.Max.Z, bb.Max.Z));
                }
                if (union == null) { warnings.Add("RoomBoundary: no room bboxes."); return; }

                var marginFt = (marginMm / 304.8);
                union.Min = union.Min - new XYZ(marginFt, marginFt, 0);
                union.Max = union.Max + new XYZ(marginFt, marginFt, 0);

                view.CropBoxActive  = true;
                view.CropBoxVisible = true;
                view.CropBox        = union;
            }
            catch (Exception ex) { warnings.Add($"RoomBoundary: {ex.Message}"); }
        }
    }
}
