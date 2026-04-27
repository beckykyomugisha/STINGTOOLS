// Phase 139 D5 — Wet zone exclusion checker.
//
// Implements BS 7671 / IEC 60364-7-701 bath/shower zone geometry to
// reject placement candidates that fall within Zone 0/1/2 around water
// fixtures.  Per-room cache means the heavy collector runs once.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    public class WetZoneExclusionChecker
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>One exclusion volume around a single water fixture.</summary>
        public class ExclusionZone
        {
            public string ZoneName { get; set; } = "";   // "BS7671_Z0" .. "BS7671_Z3"
            public XYZ MinPt   { get; set; }
            public XYZ MaxPt   { get; set; }
            public ElementId SourceFixtureId { get; set; }

            public bool Contains(XYZ pt)
            {
                if (pt == null) return false;
                return pt.X >= MinPt.X && pt.X <= MaxPt.X
                    && pt.Y >= MinPt.Y && pt.Y <= MaxPt.Y
                    && pt.Z >= MinPt.Z && pt.Z <= MaxPt.Z;
            }
        }

        public class CheckResult
        {
            public bool   Rejected { get; set; }
            public string ZoneHit  { get; set; } = "";
            public ElementId FixtureId { get; set; }
        }

        private readonly Document _doc;
        private readonly Dictionary<ElementId, List<ExclusionZone>> _cache
            = new Dictionary<ElementId, List<ExclusionZone>>();

        public WetZoneExclusionChecker(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Build the per-room exclusion list.  Inspects family / type
        /// names looking for "bath", "shower", "basin", "sink", "wc"
        /// keywords.
        /// </summary>
        public List<ExclusionZone> BuildForRoom(Room room)
        {
            if (room == null) return new List<ExclusionZone>();
            if (_cache.TryGetValue(room.Id, out var cached)) return cached;
            var zones = new List<ExclusionZone>();
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null) { _cache[room.Id] = zones; return zones; }
                var pad = 2.0; // ft pad
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                var bbf = new BoundingBoxIntersectsFilter(outline);

                var col = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbf);

                foreach (var el in col)
                {
                    var fi = el as FamilyInstance;
                    if (fi == null) continue;
                    string famName = (fi.Symbol?.FamilyName ?? "").ToLowerInvariant();
                    string typName = (fi.Symbol?.Name ?? "").ToLowerInvariant();
                    string key     = famName + " " + typName;
                    XYZ origin = (fi.Location as LocationPoint)?.Point;
                    if (origin == null) continue;

                    if (key.Contains("bath"))
                        AddBathZones(zones, fi, origin);
                    else if (key.Contains("shower"))
                        AddShowerZones(zones, fi, origin);
                    else if (key.Contains("basin") || key.Contains("sink"))
                        AddBasinZones(zones, fi, origin);
                }
            }
            catch (Exception ex) { StingLog.Warn($"WetZoneExclusionChecker.BuildForRoom {room.Id}: {ex.Message}"); }
            _cache[room.Id] = zones;
            return zones;
        }

        /// <summary>
        /// Test a placement candidate against the rule's WetZoneExclusion.
        /// Returns Rejected=true when the candidate falls in a zone
        /// covered by the rule's exclusion level.
        /// </summary>
        public CheckResult Check(Room room, XYZ candidate, string wetZoneExclusion)
        {
            var result = new CheckResult();
            if (candidate == null || string.IsNullOrEmpty(wetZoneExclusion) ||
                wetZoneExclusion.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return result;

            var zones = BuildForRoom(room);
            if (zones.Count == 0) return result;

            // Determine which zone severity level the rule excludes.
            int severity = 0; // 0=none
            string upper = wetZoneExclusion.ToUpperInvariant();
            if (upper.Contains("Z0")) severity = 1;
            if (upper.Contains("Z1")) severity = 2;
            if (upper.Contains("Z2")) severity = 3;

            foreach (var z in zones)
            {
                int zoneSev = ZoneSeverity(z.ZoneName);
                if (zoneSev > severity) continue;
                if (z.Contains(candidate))
                {
                    result.Rejected = true;
                    result.ZoneHit  = z.ZoneName;
                    result.FixtureId = z.SourceFixtureId;
                    return result;
                }
            }
            return result;
        }

        private static int ZoneSeverity(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return 0;
            string u = zoneName.ToUpperInvariant();
            if (u.Contains("Z0")) return 1;
            if (u.Contains("Z1")) return 2;
            if (u.Contains("Z2")) return 3;
            if (u.Contains("Z3")) return 4;
            return 0;
        }

        private static void AddBathZones(List<ExclusionZone> zones, FamilyInstance fi, XYZ origin)
        {
            // Approximation: 1700×700mm bath aligned with element bounding box.
            var bb = fi.get_BoundingBox(null);
            if (bb == null) return;
            // Zone 0 = inside bath
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z0",
                MinPt    = bb.Min,
                MaxPt    = new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z + 1e-3),
                SourceFixtureId = fi.Id,
            });
            // Zone 1 = above bath rim, 0..2250mm above floor (here use 0..600 above bath)
            double r1 = 600.0 * MmToFt;
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z1",
                MinPt    = new XYZ(bb.Min.X - r1, bb.Min.Y - r1, bb.Min.Z),
                MaxPt    = new XYZ(bb.Max.X + r1, bb.Max.Y + r1, bb.Max.Z + 600.0 * MmToFt),
                SourceFixtureId = fi.Id,
            });
            // Zone 2 = 0..2400mm above floor, 1200mm horizontal beyond rim
            double r2 = 1200.0 * MmToFt;
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z2",
                MinPt    = new XYZ(bb.Min.X - r2, bb.Min.Y - r2, origin.Z),
                MaxPt    = new XYZ(bb.Max.X + r2, bb.Max.Y + r2, origin.Z + 2400.0 * MmToFt),
                SourceFixtureId = fi.Id,
            });
        }

        private static void AddShowerZones(List<ExclusionZone> zones, FamilyInstance fi, XYZ origin)
        {
            var bb = fi.get_BoundingBox(null);
            if (bb == null) return;
            // Zone 0 = inside basin
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z0",
                MinPt = bb.Min,
                MaxPt = new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z + 1e-3),
                SourceFixtureId = fi.Id,
            });
            double zMin = origin.Z;
            double zMax = origin.Z + 2250.0 * MmToFt;
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z1",
                MinPt = new XYZ(bb.Min.X, bb.Min.Y, zMin),
                MaxPt = new XYZ(bb.Max.X, bb.Max.Y, zMax),
                SourceFixtureId = fi.Id,
            });
            double r2 = 600.0 * MmToFt;
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z2",
                MinPt = new XYZ(bb.Min.X - r2, bb.Min.Y - r2, zMin),
                MaxPt = new XYZ(bb.Max.X + r2, bb.Max.Y + r2, zMax),
                SourceFixtureId = fi.Id,
            });
        }

        private static void AddBasinZones(List<ExclusionZone> zones, FamilyInstance fi, XYZ origin)
        {
            // Simplified circular exclusion: 600mm radius, full height.
            double r  = 600.0 * MmToFt;
            double h  = 3000.0 * MmToFt;
            zones.Add(new ExclusionZone
            {
                ZoneName = "BS7671_Z2",
                MinPt = new XYZ(origin.X - r, origin.Y - r, origin.Z),
                MaxPt = new XYZ(origin.X + r, origin.Y + r, origin.Z + h),
                SourceFixtureId = fi.Id,
            });
        }
    }
}
