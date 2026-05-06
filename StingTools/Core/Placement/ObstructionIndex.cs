// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Placement/ObstructionIndex.cs — S2.16 (L-G2).
//
// Ceiling-mounted obstruction index for luminaire placement.
//
// Collects ceiling-mounted obstructions (air terminals, sprinklers,
// smoke detectors, speakers) within a target room. Builds 2D AABB
// exclusion zones padded by a configurable buffer (default 350 mm,
// CIBSE Guide B4 minimum luminaire-to-diffuser clearance). Candidate
// luminaire positions that fall inside any exclusion zone are
// rejected before the Scorer ranks survivors.
//
// Performance: single FilteredElementCollector per room, pre-filtered
// with ElementMulticategoryFilter + BoundingBoxIntersectsFilter per
// S1.3-S1.4 N-G1 guidance.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// 2D AABB around a ceiling obstruction in model space (feet).
    /// </summary>
    public readonly struct ExclusionRect
    {
        public readonly double MinX;
        public readonly double MinY;
        public readonly double MaxX;
        public readonly double MaxY;
        public readonly ElementId SourceId;
        public readonly string SourceCategory;

        public ExclusionRect(double minX, double minY, double maxX, double maxY,
                             ElementId srcId, string srcCat)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            SourceId = srcId; SourceCategory = srcCat ?? string.Empty;
        }

        public bool Contains(double x, double y)
            => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }

    /// <summary>
    /// Obstruction-aware luminaire placement filter (L-G2).
    /// </summary>
    public static class ObstructionIndex
    {
        /// <summary>
        /// 350 mm buffer around each obstruction AABB in feet.
        /// CIBSE Guide B4 §3.6 minimum clearance.
        /// </summary>
        public const double DefaultBufferFt = 350.0 / 304.8;

        /// <summary>
        /// Categories to treat as ceiling obstructions for luminaire
        /// placement. Extendable via the <paramref name="extraCats"/>
        /// parameter on <see cref="BuildForRoom"/>.
        /// Phase 139.27 (M-01) — added Furniture / Casework / GenericModel
        /// so a luminaire can't be placed inside the swept-volume of a
        /// suspended cupboard, kitchen island top, or generic ceiling
        /// rose. The buffer applied around their AABB is the same
        /// 350 mm CIBSE Guide B4 default.
        /// </summary>
        public static readonly BuiltInCategory[] DefaultCategories = new[]
        {
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_GenericModel,
            // Suspended ducts + pipes — a luminaire can't share Z-space
            // with a 200 mm extract spine. Z-pad on Outline already
            // covers the swept volume (3 ft above room max Z).
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_CableTray,
        };

        /// <summary>
        /// Build an exclusion list for <paramref name="room"/>.
        /// </summary>
        public static List<ExclusionRect> BuildForRoom(
            Document doc,
            Room room,
            double bufferFt = DefaultBufferFt,
            IEnumerable<BuiltInCategory> extraCats = null)
        {
            var result = new List<ExclusionRect>();
            if (doc == null || room == null) return result;

            var bb = room.get_BoundingBox(null);
            if (bb == null) return result;

            // Expand the room AABB by buffer so obstructions on the
            // boundary are still caught. Z span uses room height
            // plus a full floor because ceiling-hung fixtures sit
            // above the room bounding box top.
            var outlineMin = new XYZ(bb.Min.X - bufferFt, bb.Min.Y - bufferFt, bb.Min.Z);
            var outlineMax = new XYZ(bb.Max.X + bufferFt, bb.Max.Y + bufferFt, bb.Max.Z + 3.0);
            var outline = new Outline(outlineMin, outlineMax);
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

            var cats = new List<BuiltInCategory>(DefaultCategories);
            if (extraCats != null) cats.AddRange(extraCats);

            var collector = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(cats))
                .WhereElementIsNotElementType()
                .WherePasses(bboxFilter);

            foreach (var el in collector)
            {
                var eb = el.get_BoundingBox(null);
                if (eb == null) continue;
                var rect = new ExclusionRect(
                    eb.Min.X - bufferFt, eb.Min.Y - bufferFt,
                    eb.Max.X + bufferFt, eb.Max.Y + bufferFt,
                    el.Id,
                    el.Category?.Name ?? string.Empty);
                result.Add(rect);
            }
            return result;
        }

        /// <summary>
        /// Reject any candidate XYZ whose XY falls inside an exclusion
        /// rect. Returns survivors in input order.
        /// </summary>
        public static List<XYZ> FilterPoints(IList<XYZ> candidates, IList<ExclusionRect> exclusions)
        {
            if (candidates == null || candidates.Count == 0) return new List<XYZ>();
            if (exclusions == null || exclusions.Count == 0) return new List<XYZ>(candidates);

            var ok = new List<XYZ>(candidates.Count);
            foreach (var p in candidates)
            {
                if (p == null) continue;
                bool blocked = false;
                foreach (var r in exclusions)
                {
                    if (r.Contains(p.X, p.Y)) { blocked = true; break; }
                }
                if (!blocked) ok.Add(p);
            }
            return ok;
        }

        /// <summary>
        /// Count candidates that would be rejected without actually
        /// filtering — useful for pre-flight reporting.
        /// </summary>
        public static int CountBlocked(IList<XYZ> candidates, IList<ExclusionRect> exclusions)
        {
            if (candidates == null || exclusions == null) return 0;
            int n = 0;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                foreach (var r in exclusions)
                    if (r.Contains(p.X, p.Y)) { n++; break; }
            }
            return n;
        }
    }
}
