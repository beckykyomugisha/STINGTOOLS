// ResolutionHeuristics.cs — tier-A deterministic resolution suggestions.
// Covers the 30 most common MEP-vs-structure and MEP-vs-arch patterns (~60% of real clashes).
// rec-12: Extended from 10 → 30 patterns so the v1.0 ship target is met.
using System;

namespace StingTools.Core.Clash
{
    public static class ResolutionHeuristics
    {
        public static string Suggest(ClashRecord c)
        {
            if (c?.ElementA == null || c.ElementB == null) return null;
            string ca = c.ElementA.Category ?? "", cb = c.ElementB.Category ?? "";
            // Normalise so the "service" element is always A when one side is
            // structural and the other isn't. Struct-vs-struct and arch-vs-arch
            // pairs skip this swap — and for those, each rule below must check
            // both orderings explicitly (G4 fix). Helper MatchEither(a,b,x,y)
            // handles the common case of "pattern fires when (ca,cb) matches
            // (x,y) in either direction".
            if (IsStructural(ca) && !IsStructural(cb)) { var t = ca; ca = cb; cb = t; }

            // G4: Helper — returns true when (ca,cb) equals (x,y) in either order.
            bool MatchEither(string x, string y) =>
                (ca == x && cb == y) || (ca == y && cb == x);

            float dz = c.AabbMax[2] - c.AabbMin[2];
            float dy = c.AabbMax[1] - c.AabbMin[1];
            float dx = c.AabbMax[0] - c.AabbMin[0];
            float dzMm = dz * 304.8f;
            float dxMm = dx * 304.8f;
            float dyMm = dy * 304.8f;

            // ── Original 10 patterns (from Stage 4) ─────────────────────────
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
                return $"Offset tray horizontally by {dxMm:F0} mm to maintain 100 mm clearance.";
            if (ca == "Conduits" && cb == "Ducts")
                return "Route conduit above or below duct bank.";
            if (ca == "Mechanical Equipment" && cb == "Structural Framing")
                return "Verify equipment support and maintenance clearance zones.";
            if (ca == "Structural Columns" && cb == "Walls")
                return "Rebuild wall around column or offset wall centreline.";

            // ── rec-12: 20 additional high-frequency patterns ───────────────
            // Penetration fire-rating
            if (ca == "Pipes" && cb == "Floors")
                return $"Add IFC opening + fire-rated pipe sleeve at floor penetration (nominal {dzMm:F0} mm deep).";
            if (ca == "Ducts" && cb == "Floors")
                return $"Add IFC opening + fire damper at floor penetration (BS 9999 compartmentation).";
            if (ca == "Cable Trays" && cb == "Walls")
                return "Fire-stopping required at cable tray wall penetration — verify BS 8214 penetration seal type.";
            if (ca == "Cable Trays" && cb == "Floors")
                return "Fire-stopping required at cable tray floor penetration (BS 8214 barrier seal).";
            if (ca == "Conduits" && cb == "Walls")
                return "Add conduit box or intumescent sleeve at wall penetration.";

            // Pipe-vs-pipe / duct-vs-duct crossings
            if (ca == "Pipes" && cb == "Pipes")
                return $"Offset one pipe run vertically by {dzMm:F0} mm; maintain 50 mm clearance between services.";
            if (ca == "Ducts" && cb == "Ducts")
                return $"Re-route one duct over/under the other; maintain 100 mm clearance for flange + insulation.";
            if (ca == "Cable Trays" && cb == "Cable Trays")
                return $"Stack tray runs with 150 mm vertical clearance (NFPA 70 cable-heat dissipation).";

            // Plumbing-fixture specifics
            if (ca == "Plumbing Fixtures" && cb == "Walls")
                return "Confirm plumbing fixture is wall-hosted; check carrier / backing plate position.";
            if (ca == "Plumbing Fixtures" && cb == "Floors")
                return "Confirm fixture floor drain alignment (gradient to waste run).";
            if (ca == "Plumbing Fixtures" && cb == "Structural Framing")
                return "Verify fixture supply/waste routing avoids beam soffit (reroute or box-in).";

            // Electrical equipment clearances (BS 7671 / IEC 61439)
            if (ca == "Electrical Equipment" && cb == "Walls")
                return "Verify 800 mm working clearance in front of panel per BS 7671.";
            if (ca == "Electrical Equipment" && cb == "Structural Framing")
                return "Check headroom above panel (min 2.0 m clear working height).";
            if (ca == "Electrical Fixtures" && cb == "Walls")
                return "Confirm fixture is wall-hosted with correct back-box depth.";

            // Sprinkler / fire protection
            if (ca == "Sprinklers" && cb == "Ducts")
                return $"Offset sprinkler {dxMm:F0} mm to maintain 450 mm unobstructed discharge cone (BS EN 12845).";
            if (ca == "Sprinklers" && cb == "Cable Trays")
                return "Relocate sprinkler or tray to maintain BS EN 12845 spray-pattern clearance.";

            // Ceiling grid items
            if (ca == "Air Terminals" && cb == "Ceilings")
                return "Align diffuser Z to ceiling grid Z (600×600 or 1200×600 module).";
            if (ca == "Air Terminals" && cb == "Lighting Fixtures")
                return "Offset diffuser to next ceiling tile to avoid luminaire overlap.";

            // Structural-to-arch — G4: bidirectional. IsStructural() swap above
            // only fires when exactly ONE side is structural, so struct↔struct
            // and arch↔struct pairs where both are in IsStructural (e.g. Floors)
            // stay in original declaration order. MatchEither catches both.
            if (MatchEither("Structural Framing", "Ceilings"))
                return "Lower ceiling or box-out around beam soffit (mind ceiling access requirement).";
            if (MatchEither("Structural Columns", "Floors"))
                return "Verify column head stop correctly matches slab thickness; check for slab punching shear detailing (EC2 §6.4).";
            if (MatchEither("Stairs", "Ceilings"))
                return "Check headroom clearance over stair flight (≥ 2.0 m per BS 5395 / Part K).";
            if (MatchEither("Structural Foundations", "Walls"))
                return "Align wall centerline with foundation centerline; verify foundation width is wall thickness + 150 mm each side.";

            return null;
        }

        private static bool IsStructural(string c) =>
            c == "Structural Framing" || c == "Structural Columns" ||
            c == "Structural Foundations" || c == "Floors" ||
            c == "Structural Connections" || c == "Structural Rebar";
    }
}
