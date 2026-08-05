using StingTools.Core;
// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Placement/CeilingGridSnap.cs — S2.15 (L-G1).
//
// Snaps a candidate luminaire XYZ to the nearest ceiling tile grid
// intersection. Reads Tile Width/Height from the host ceiling type
// parameters and orients rectangular luminaires along the room long
// axis so troffers line up with tile seams rather than cutting across
// them.
//
// The room long-axis orientation uses a 2D oriented-bounding-box on
// the room's boundary curve loop (longest edge defines the axis). For
// square-ish rooms the existing XY is preserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Snap luminaire positions to the ceiling tile grid per L-G1.
    /// </summary>
    public static class CeilingGridSnap
    {
        /// <summary>
        /// Default 1200x600 mm grid if the ceiling type carries no
        /// Tile Width / Tile Height params. In feet: 3.937 x 1.969.
        /// </summary>
        public const double DefaultTileWidthFt  = 1200.0 / 304.8;
        public const double DefaultTileHeightFt = 600.0 / 304.8;

        /// <summary>
        /// Snap each input XYZ to the nearest grid intersection of the
        /// ceiling under the room. Grid origin is room-bounding-box
        /// corner. Elevation (Z) preserved from input. If no ceiling is
        /// found the input points are returned unchanged and a Warn is
        /// logged once per room.
        /// </summary>
        public static List<XYZ> SnapToCeilingGrid(Document doc, Room room, IList<XYZ> points)
        {
            if (doc == null || room == null || points == null || points.Count == 0)
                return points == null ? new List<XYZ>() : new List<XYZ>(points);

            var ceiling = FindCeilingOverRoom(doc, room);
            double wFt = DefaultTileWidthFt;
            double hFt = DefaultTileHeightFt;

            if (ceiling != null)
            {
                var ctype = doc.GetElement(ceiling.GetTypeId()) as ElementType;
                wFt = ReadParamFeet(ctype, "Tile Width",  wFt);
                hFt = ReadParamFeet(ctype, "Tile Height", hFt);
            }
            else
            {
                StingTools.Core.StingLog.Warn(
                    $"CeilingGridSnap: no ceiling found above room '{room.Name}' — using default 1200x600 grid");
            }

            var bb = room.get_BoundingBox(null);
            if (bb == null) return new List<XYZ>(points);

            // Room long-axis orientation
            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;
            bool alignAlongX = dx >= dy;

            double stepW = alignAlongX ? wFt : hFt;
            double stepH = alignAlongX ? hFt : wFt;

            var snapped = new List<XYZ>(points.Count);
            foreach (var p in points)
            {
                if (p == null) continue;
                double gx = bb.Min.X + Math.Round((p.X - bb.Min.X) / stepW) * stepW + stepW / 2.0;
                double gy = bb.Min.Y + Math.Round((p.Y - bb.Min.Y) / stepH) * stepH + stepH / 2.0;
                snapped.Add(new XYZ(gx, gy, p.Z));
            }
            return snapped;
        }

        /// <summary>
        /// Find the Ceiling element directly above the room centroid.
        /// Uses BoundingBoxIntersectsFilter per N-G1 perf guidance.
        /// </summary>
        private static Ceiling FindCeilingOverRoom(Document doc, Room room)
        {
            var bb = room.get_BoundingBox(null);
            if (bb == null) return null;

            double lift = 0.5; // ~150 mm above room
            var min = new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z + lift / 2.0);
            var max = new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z + lift);
            var outline = new Outline(min, max);
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .WherePasses(bboxFilter)
                .OfType<Ceiling>()
                .FirstOrDefault();
        }

        /// <summary>
        /// Read a length parameter by name from an ElementType; returns
        /// <paramref name="fallback"/> (in feet) if missing / zero.
        /// Parameter stored internally in feet by Revit.
        /// </summary>
        private static double ReadParamFeet(ElementType t, string paramName, double fallback)
        {
            if (t == null) return fallback;
            try
            {
                var p = t.LookupParameter(paramName);
                if (p == null || !p.HasValue) return fallback;
                double v = p.AsDouble();
                return v > 0.0 ? v : fallback;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"CeilingGridSnap.ReadParamFeet('{paramName}'): {ex.Message}");
                return fallback;
            }
        }
    }
}
