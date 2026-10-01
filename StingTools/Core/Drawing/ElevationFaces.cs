// StingTools — Drawing Template Manager · which elevations to make, and which way they look
//
// Interior elevations: the producer made ONE elevation per room — marker face 0,
// whatever the drawing type asked for — on the first plan view in the model, which is
// usually on another level. Exterior elevations assumed Revit's marker faces were
// N=0, E=1, S=2, W=3, which the API does not promise.
//
// Now the faces come from the drawing type:
//   • productionRules that set "elevationFace" (0-3) — exactly those faces;
//   • productionRules without it — one face per Elevation rule, in order;
//   • no productionRules — one face per Elevation slot the type lays out (a type with
//     four elevation slots gets all four, a type with one gets one).
// And which way a produced elevation LOOKS is read back from the view it made, not
// assumed: exterior faces are kept only when they look at the building, and every
// elevation is named by the compass direction it looks.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One elevation to produce: the rule (Idx) it is produced under, the marker face and the slot.</summary>
    public sealed class ElevationFaceItem
    {
        public int RuleIdx { get; set; }
        public int Face { get; set; }
        public int SlotIndex { get; set; }
    }

    public static class ElevationFaces
    {
        public const int MaxFaces = 4;

        /// <summary>
        /// The faces a drawing type asks for. <paramref name="rules"/> are its production
        /// rules (may be empty); <paramref name="slotViewTypes"/> its slots' view types, in order.
        /// </summary>
        public static List<ElevationFaceItem> Plan(IList<ProductionRule> rules, IList<string> slotViewTypes)
        {
            var items = new List<ElevationFaceItem>();
            var elevRules = (rules ?? new List<ProductionRule>())
                .Where(r => r != null && string.Equals((r.ViewType ?? "").Trim(), "Elevation", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Idx).ToList();
            if (elevRules.Count > 0)
            {
                int order = 0;
                foreach (var r in elevRules)
                {
                    int face = r.ElevationFace ?? order;
                    order++;
                    if (face < 0 || face >= MaxFaces) continue;
                    if (items.Any(i => i.Face == face)) continue;
                    items.Add(new ElevationFaceItem { RuleIdx = r.Idx, Face = face, SlotIndex = r.SlotIndex >= 0 ? r.SlotIndex : items.Count });
                }
                return items;
            }

            var slots = (slotViewTypes ?? new List<string>()).ToList();
            var elevSlots = Enumerable.Range(0, slots.Count)
                .Where(i => string.Equals((slots[i] ?? "").Trim(), "Elevation", StringComparison.OrdinalIgnoreCase))
                .Take(MaxFaces).ToList();
            if (elevSlots.Count == 0) elevSlots.Add(0);   // no elevation slot: one elevation, as before
            for (int f = 0; f < elevSlots.Count; f++)
                items.Add(new ElevationFaceItem { RuleIdx = f, Face = f, SlotIndex = elevSlots[f] });
            return items;
        }

        /// <summary>The compass direction a view LOOKS towards (the wall or façade it shows lies that way).</summary>
        public static string Compass(double lookX, double lookY)
        {
            if (Math.Abs(lookX) < 1e-9 && Math.Abs(lookY) < 1e-9) return "";
            double deg = Math.Atan2(lookY, lookX) * 180.0 / Math.PI;   // 0 = east, 90 = north
            deg = (deg + 360.0) % 360.0;
            if (deg >= 45 && deg < 135) return "North";
            if (deg >= 135 && deg < 225) return "West";
            if (deg >= 225 && deg < 315) return "South";
            return "East";
        }

        /// <summary>True when looking along (lookX, lookY) is within 45° of (wantX, wantY).</summary>
        public static bool LooksToward(double lookX, double lookY, double wantX, double wantY)
        {
            double a = Math.Sqrt(lookX * lookX + lookY * lookY), b = Math.Sqrt(wantX * wantX + wantY * wantY);
            if (a < 1e-9 || b < 1e-9) return false;
            return (lookX * wantX + lookY * wantY) / (a * b) > Math.Cos(Math.PI / 4) - 1e-9;
        }

        /// <summary>
        /// Where the marker for the <paramref name="face"/> façade ("North" …) stands, and the
        /// way it must look: outside that side of the footprint, at <paramref name="offset"/>,
        /// looking back at the building. Null for a name that is not a compass face.
        /// </summary>
        public static (double X, double Y, double LookX, double LookY)? ExteriorStation(string face,
            double minX, double minY, double maxX, double maxY, double offset)
        {
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            switch ((face ?? "").Trim().ToLowerInvariant())
            {
                case "north": return (cx, maxY + offset, 0, -1);
                case "south": return (cx, minY - offset, 0, 1);
                case "east":  return (maxX + offset, cy, -1, 0);
                case "west":  return (minX - offset, cy, 1, 0);
                default: return null;
            }
        }
    }
}
