// PlumbingPlacementProfile — bridges the existing FixturePlacementEngine
// to plumbing-specific rules without forking the engine.
//
// Phase 178c. Specialises the WetZoneExclusionChecker (inverted to
// prefer wet zones for plumbing fixtures), adds trap-arm length check
// (BS EN 12056 §6 — distance from fixture to vent ≤ 1.5 m typical,
// 1.7 m for kitchen sink, 2.4 m IPC), and a drain-fall feasibility
// guard so basin → stack horizontal runs that would breach slab
// depth at design slope are flagged before placement.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core.Placement;

namespace StingTools.Core.Plumbing
{
    public class PlumbingPlacementProfile
    {
        public PlumbingPlacementProfile(Document doc)
        {
            _doc = doc;
            _wetZone = new WetZoneExclusionChecker(doc) { Discipline = PlacementDiscipline.Plumbing };
        }

        private readonly Document _doc;
        private readonly WetZoneExclusionChecker _wetZone;

        // BS EN 12056 — max trap-arm length (fixture outlet → vent)
        // by fixture type. IPC equivalents in Table 909.1 are similar.
        public static readonly Dictionary<string, double> TrapArmMaxM = new Dictionary<string, double>
        {
            { "BASIN",   1.5 },
            { "WHB",     1.5 },
            { "SHOWER",  1.5 },
            { "BATH",    1.5 },
            { "BIDET",   1.5 },
            { "WC",      6.0 },
            { "URINAL",  3.0 },
            { "KITCHEN", 1.7 },
            { "SINK",    1.7 },
            { "WASHING", 1.7 },
            { "FLOOR",   3.0 },
        };

        public class PlacementCheck
        {
            public bool Allowed       { get; set; } = true;
            public string Reason      { get; set; } = "";
            public string ZoneHit     { get; set; } = "";
        }

        // Wet-zone preference: a plumbing fixture should fall within a
        // wet zone of its own room. Returns Allowed=true when inside,
        // false otherwise.
        public PlacementCheck CheckWetZonePreference(Room room, XYZ candidate, string zoneSpec = "Z2")
        {
            var pc = new PlacementCheck();
            var res = _wetZone.Check(room, candidate, zoneSpec);
            pc.Allowed = !res.Rejected;
            pc.ZoneHit = res.ZoneHit;
            if (res.Rejected) pc.Reason = "Plumbing fixture should fall in a wet zone of its host room";
            return pc;
        }

        // Trap-arm length check: candidate fixture must be within
        // TrapArmMaxM of the nearest stack/vent point provided by
        // caller. Returns Allowed=false with the required separation.
        public PlacementCheck CheckTrapArmLength(string fixtureKind, XYZ fixturePoint, XYZ stackPoint)
        {
            var pc = new PlacementCheck();
            if (fixturePoint == null || stackPoint == null) return pc;
            double maxM = 1.5;
            if (!string.IsNullOrEmpty(fixtureKind))
            {
                string upper = fixtureKind.ToUpperInvariant();
                foreach (var kv in TrapArmMaxM)
                {
                    if (upper.Contains(kv.Key)) { maxM = kv.Value; break; }
                }
            }
            double distFt = fixturePoint.DistanceTo(stackPoint);
            double distM  = distFt * 0.3048;
            if (distM > maxM)
            {
                pc.Allowed = false;
                pc.Reason  = $"Trap-arm {distM:F2} m > {maxM:F2} m max for {fixtureKind} (BS EN 12056 §6)";
            }
            return pc;
        }

        // Drain-fall feasibility: at projectSlopePct, a horizontal run
        // of `runLengthM` will drop `runLengthM * slopePct/100`.
        // Returns Allowed=false when that drop exceeds availableDropMm
        // (typically (slabDepth - structuralRebate)).
        public PlacementCheck CheckDrainFallFeasibility(
            double runLengthM,
            double projectSlopePct,
            double availableDropMm)
        {
            var pc = new PlacementCheck();
            if (runLengthM <= 0 || projectSlopePct <= 0) return pc;
            double requiredDropMm = runLengthM * projectSlopePct * 10.0; // %·m → mm
            if (requiredDropMm > availableDropMm)
            {
                pc.Allowed = false;
                pc.Reason  = $"Drain fall {requiredDropMm:F0} mm exceeds {availableDropMm:F0} mm available — relocate stack closer or increase slab depth";
            }
            return pc;
        }
    }
}
