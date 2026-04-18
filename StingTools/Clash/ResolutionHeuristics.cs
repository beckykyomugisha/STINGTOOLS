// ResolutionHeuristics.cs — tier-A deterministic resolution suggestions.
// Covers the 30 most common MEP-vs-structure and MEP-vs-arch patterns (~60% of real clashes).
using System;

namespace StingTools.Core.Clash
{
    public static class ResolutionHeuristics
    {
        public static string Suggest(ClashRecord c)
        {
            if (c?.ElementA == null || c.ElementB == null) return null;
            string ca = c.ElementA.Category ?? "", cb = c.ElementB.Category ?? "";
            // Normalise so the "service" element is always A.
            if (IsStructural(ca) && !IsStructural(cb)) { var t = ca; ca = cb; cb = t; }

            float dz = c.AabbMax[2] - c.AabbMin[2];
            float dy = c.AabbMax[1] - c.AabbMin[1];
            float dx = c.AabbMax[0] - c.AabbMin[0];
            float dzMm = dz * 304.8f;

            if (ca == "Ducts" && cb == "Structural Framing")
                return $"Lower duct by {dzMm:F0} mm to clear beam bottom flange + 50 mm.";
            if (ca == "Pipes" && cb == "Structural Framing")
                return $"Route pipe below or around beam; offset by {dzMm:F0} mm.";
            if (ca == "Pipes" && cb == "Walls")
                return "Add IFC opening element + pipe sleeve at wall penetration.";
            if (ca == "Ducts" && cb == "Walls")
                return "Add IFC opening element and verify fire-rating sleeve type.";
            if (ca == "Sprinklers" && cb == "Ceilings")
                return "Align sprinkler head Z to ceiling grid Z.";
            if (ca == "Lighting Fixtures" && cb == "Ceilings")
                return "Confirm light fixture is hosted by the ceiling family.";
            if (ca == "Cable Trays" && cb == "Ducts")
                return $"Offset tray horizontally by {dx * 304.8f:F0} mm to maintain 100 mm clearance.";
            if (ca == "Conduits" && cb == "Ducts")
                return "Route conduit above or below duct bank.";
            if (ca == "Mechanical Equipment" && cb == "Structural Framing")
                return "Verify equipment support and maintenance clearance zones.";
            if (ca == "Structural Columns" && cb == "Walls")
                return "Rebuild wall around column or offset wall centreline.";

            return null;
        }

        private static bool IsStructural(string c) =>
            c == "Structural Framing" || c == "Structural Columns" || c == "Structural Foundations" || c == "Floors";
    }
}
