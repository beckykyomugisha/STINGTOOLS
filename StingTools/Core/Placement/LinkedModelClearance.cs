using StingTools.Core;
// Phase 139 G — Linked-model clearance check.
//
// Inspects RevitLinkInstances in the host document and reports whether
// any linked element's bounding box (transformed into host coordinates)
// falls within ObstructionClearanceMm of a placement candidate.  Used
// as part of PlacementScorer's collision component.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    public class LinkedModelClearance
    {
        private const double MmToFt = 1.0 / 304.8;

        private readonly Document _hostDoc;
        private List<(Document linked, Transform t)> _linkedDocs;

        public LinkedModelClearance(Document hostDoc)
        {
            _hostDoc = hostDoc;
        }

        private void EnsureLoaded()
        {
            if (_linkedDocs != null) return;
            _linkedDocs = new List<(Document, Transform)>();
            try
            {
                foreach (var li in new FilteredElementCollector(_hostDoc)
                    .OfClass(typeof(RevitLinkInstance)))
                {
                    var inst = li as RevitLinkInstance;
                    if (inst == null) continue;
                    var doc = inst.GetLinkDocument();
                    if (doc == null) continue;
                    _linkedDocs.Add((doc, inst.GetTransform()));
                }
            }
            catch (Exception ex) { StingLog.Warn($"LinkedModelClearance.EnsureLoaded: {ex.Message}"); }
        }

        /// <summary>
        /// Returns true when the candidate point has a linked element
        /// within clearanceMm.  Coarse: only category-relevant elements
        /// (walls, columns, MEP equipment, ducts, pipes) are checked.
        /// </summary>
        public bool HasObstructionWithin(XYZ pt, double clearanceMm)
        {
            if (pt == null || clearanceMm <= 0) return false;
            EnsureLoaded();
            if (_linkedDocs.Count == 0) return false;
            double clearFt = clearanceMm * MmToFt;
            var cats = new[]
            {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
            };
            var filter = new ElementMulticategoryFilter(cats);

            foreach (var (linkedDoc, t) in _linkedDocs)
            {
                try
                {
                    foreach (var el in new FilteredElementCollector(linkedDoc)
                        .WherePasses(filter)
                        .WhereElementIsNotElementType())
                    {
                        var bb = el.get_BoundingBox(null);
                        if (bb == null) continue;
                        // Transform link bbox corners into host space.
                        var min = t.OfPoint(bb.Min);
                        var max = t.OfPoint(bb.Max);
                        double xmin = Math.Min(min.X, max.X) - clearFt;
                        double xmax = Math.Max(min.X, max.X) + clearFt;
                        double ymin = Math.Min(min.Y, max.Y) - clearFt;
                        double ymax = Math.Max(min.Y, max.Y) + clearFt;
                        double zmin = Math.Min(min.Z, max.Z) - clearFt;
                        double zmax = Math.Max(min.Z, max.Z) + clearFt;
                        if (pt.X >= xmin && pt.X <= xmax &&
                            pt.Y >= ymin && pt.Y <= ymax &&
                            pt.Z >= zmin && pt.Z <= zmax)
                            return true;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"LinkedModelClearance.scan: {ex.Message}"); }
            }
            return false;
        }
    }
}
