// StingTools v4 MVP — pre-flight clash checker (#9).
//
// Runs a bounding-box interference check across the filtered element
// set BEFORE Generate Package commits — so foremen catch a duct that
// rams through a beam while still in preview. Uses a simple O(n²)
// AABB overlap scan (faster than Revit's Interference Check for the
// small sets the workspace dialog typically filters to — a few
// hundred pipes at most); for project-scale scans the engine falls
// back to FilteredElementCollector + ElementIntersectsSolidFilter.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public class ClashHit
    {
        public long ElementIdA { get; set; }
        public long ElementIdB { get; set; }
        public string NameA    { get; set; } = "";
        public string NameB    { get; set; } = "";
        public double OverlapMm { get; set; } // shortest-side overlap, mm
    }

    public static class FabricationClashChecker
    {
        /// <summary>
        /// Compute element-vs-element clashes inside the filtered set.
        /// Returns hits ordered by overlap volume (largest first).
        /// </summary>
        public static List<ClashHit> ScanAabb(Document doc, IList<ElementId> ids, int maxHits = 200)
        {
            var boxes = new List<(ElementId Id, BoundingBoxXYZ Box, string Name)>();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                BoundingBoxXYZ bb = null;
                try { bb = el.get_BoundingBox(null); } catch { }
                if (bb == null) continue;
                boxes.Add((id, bb, el.Name ?? ""));
            }

            var hits = new List<ClashHit>();
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    double overlap = AabbOverlapShortMm(boxes[i].Box, boxes[j].Box);
                    if (overlap > 1.0) // 1 mm tolerance
                    {
                        hits.Add(new ClashHit
                        {
                            ElementIdA = boxes[i].Id.Value,
                            ElementIdB = boxes[j].Id.Value,
                            NameA = boxes[i].Name,
                            NameB = boxes[j].Name,
                            OverlapMm = overlap,
                        });
                        if (hits.Count >= maxHits) return hits;
                    }
                }
            }
            return hits.OrderByDescending(h => h.OverlapMm).ToList();
        }

        private static double AabbOverlapShortMm(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            try
            {
                double dx = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X);
                double dy = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y);
                double dz = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
                if (dx <= 0 || dy <= 0 || dz <= 0) return 0;
                double shortestFt = Math.Min(dx, Math.Min(dy, dz));
                return shortestFt * 304.8;
            }
            catch { return 0; }
        }
    }
}
