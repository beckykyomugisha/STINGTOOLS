// ResolutionHeuristics.cs — tier-A deterministic resolution suggestions.
// Covers the 30 most common MEP-vs-structure and MEP-vs-arch patterns (~60% of real clashes).
//
// H1.5: Category identifiers are BuiltInCategory enum names (OST_*) consistent
// with ClashMeshBuffer.Category / ClashMatrix filter DSL / ClashElementRecord
// persisted schema. Prior version used display names ("Ducts") which varied
// by Revit locale and never matched what the extractor wrote. Rules are now
// stable across projects / locales.
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
            // pairs skip this swap — and for those, each rule below uses
            // MatchEither to check both orderings explicitly (G4).
            if (IsStructural(ca) && !IsStructural(cb)) { var t = ca; ca = cb; cb = t; }

            bool MatchEither(string x, string y) =>
                (ca == x && cb == y) || (ca == y && cb == x);

            float dz = c.AabbMax[2] - c.AabbMin[2];
            float dy = c.AabbMax[1] - c.AabbMin[1];
            float dx = c.AabbMax[0] - c.AabbMin[0];
            float dzMm = dz * 304.8f;
            float dxMm = dx * 304.8f;
            float dyMm = dy * 304.8f;

            // ── Original 10 patterns (from Stage 4) ─────────────────────────
            if (ca == "OST_DuctCurves" && cb == "OST_StructuralFraming")
                return $"Lower duct by {dzMm:F0} mm to clear beam bottom flange + 50 mm.";
            if (ca == "OST_PipeCurves" && cb == "OST_StructuralFraming")
                return $"Route pipe below or around beam; offset by {dzMm:F0} mm.";
            if (ca == "OST_PipeCurves" && cb == "OST_Walls")
                return "Add IFC opening element + pipe sleeve at wall penetration.";
            if (ca == "OST_DuctCurves" && cb == "OST_Walls")
                return "Add IFC opening element and verify fire-rating sleeve type.";
            if (ca == "OST_Sprinklers" && cb == "OST_Ceilings")
                return "Align sprinkler head Z to ceiling grid Z.";
            if (ca == "OST_LightingFixtures" && cb == "OST_Ceilings")
                return "Confirm light fixture is hosted by the ceiling family.";
            if (ca == "OST_CableTray" && cb == "OST_DuctCurves")
                return $"Offset tray horizontally by {dxMm:F0} mm to maintain 100 mm clearance.";
            if (ca == "OST_Conduit" && cb == "OST_DuctCurves")
                return "Route conduit above or below duct bank.";
            if (ca == "OST_MechanicalEquipment" && cb == "OST_StructuralFraming")
                return "Verify equipment support and maintenance clearance zones.";
            if (ca == "OST_StructuralColumns" && cb == "OST_Walls")
                return "Rebuild wall around column or offset wall centreline.";

            // ── rec-12: 20 additional high-frequency patterns ───────────────
            // Penetration fire-rating
            if (ca == "OST_PipeCurves" && cb == "OST_Floors")
                return $"Add IFC opening + fire-rated pipe sleeve at floor penetration (nominal {dzMm:F0} mm deep).";
            if (ca == "OST_DuctCurves" && cb == "OST_Floors")
                return $"Add IFC opening + fire damper at floor penetration (BS 9999 compartmentation).";
            if (ca == "OST_CableTray" && cb == "OST_Walls")
                return "Fire-stopping required at cable tray wall penetration — verify BS 8214 penetration seal type.";
            if (ca == "OST_CableTray" && cb == "OST_Floors")
                return "Fire-stopping required at cable tray floor penetration (BS 8214 barrier seal).";
            if (ca == "OST_Conduit" && cb == "OST_Walls")
                return "Add conduit box or intumescent sleeve at wall penetration.";

            // Pipe-vs-pipe / duct-vs-duct crossings
            if (ca == "OST_PipeCurves" && cb == "OST_PipeCurves")
                return $"Offset one pipe run vertically by {dzMm:F0} mm; maintain 50 mm clearance between services.";
            if (ca == "OST_DuctCurves" && cb == "OST_DuctCurves")
                return $"Re-route one duct over/under the other; maintain 100 mm clearance for flange + insulation.";
            if (ca == "OST_CableTray" && cb == "OST_CableTray")
                return $"Stack tray runs with 150 mm vertical clearance (NFPA 70 cable-heat dissipation).";

            // Plumbing-fixture specifics
            if (ca == "OST_PlumbingFixtures" && cb == "OST_Walls")
                return "Confirm plumbing fixture is wall-hosted; check carrier / backing plate position.";
            if (ca == "OST_PlumbingFixtures" && cb == "OST_Floors")
                return "Confirm fixture floor drain alignment (gradient to waste run).";
            if (ca == "OST_PlumbingFixtures" && cb == "OST_StructuralFraming")
                return "Verify fixture supply/waste routing avoids beam soffit (reroute or box-in).";

            // Electrical equipment clearances (BS 7671 / IEC 61439)
            if (ca == "OST_ElectricalEquipment" && cb == "OST_Walls")
                return "Verify 800 mm working clearance in front of panel per BS 7671.";
            if (ca == "OST_ElectricalEquipment" && cb == "OST_StructuralFraming")
                return "Check headroom above panel (min 2.0 m clear working height).";
            if (ca == "OST_ElectricalFixtures" && cb == "OST_Walls")
                return "Confirm fixture is wall-hosted with correct back-box depth.";

            // Sprinkler / fire protection
            if (ca == "OST_Sprinklers" && cb == "OST_DuctCurves")
                return $"Offset sprinkler {dxMm:F0} mm to maintain 450 mm unobstructed discharge cone (BS EN 12845).";
            if (ca == "OST_Sprinklers" && cb == "OST_CableTray")
                return "Relocate sprinkler or tray to maintain BS EN 12845 spray-pattern clearance.";

            // Ceiling grid items
            if (ca == "OST_DuctTerminal" && cb == "OST_Ceilings")
                return "Align diffuser Z to ceiling grid Z (600×600 or 1200×600 module).";
            if (ca == "OST_DuctTerminal" && cb == "OST_LightingFixtures")
                return "Offset diffuser to next ceiling tile to avoid luminaire overlap.";

            // Structural-to-arch — G4: bidirectional.
            if (MatchEither("OST_StructuralFraming", "OST_Ceilings"))
                return "Lower ceiling or box-out around beam soffit (mind ceiling access requirement).";
            if (MatchEither("OST_StructuralColumns", "OST_Floors"))
                return "Verify column head stop correctly matches slab thickness; check for slab punching shear detailing (EC2 §6.4).";
            if (MatchEither("OST_Stairs", "OST_Ceilings"))
                return "Check headroom clearance over stair flight (≥ 2.0 m per BS 5395 / Part K).";
            if (MatchEither("OST_StructuralFoundation", "OST_Walls"))
                return "Align wall centerline with foundation centerline; verify foundation width is wall thickness + 150 mm each side.";

            return null;
        }

        private static bool IsStructural(string c) =>
            c == "OST_StructuralFraming" || c == "OST_StructuralColumns" ||
            c == "OST_StructuralFoundation" || c == "OST_Floors" ||
            c == "OST_StructConnections" || c == "OST_Rebar";
    }
}
