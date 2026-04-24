// §5.1 — reader for the seven placement-intelligence parameters.
//
// Bridges family-type parameters to the placement engines (PlaceFixtures,
// PlacementScorer, LightingGrid, etc.). Every field has exactly one
// accessor so callers never re-implement the same LookupParameter dance,
// and all future placement code paths read through this class — producing
// the single point to swap for extensible-storage or richer schemas later.
//
// Defaults are empty / 0 so an un-set parameter behaves identically to a
// pre-Pack-3 family — engines must treat empty as "use the rule library".

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Strongly-typed read of the seven §5.1 placement-hint parameters. Read
    /// from the family type when available, falling back to the instance.
    /// </summary>
    public class PlacementHints
    {
        public string HostType        { get; set; } = "";   // WorkPlane / WallHosted / CeilingHosted / FaceBased / FloorHosted
        public double MountHeightMm   { get; set; }         // BS 8300 / Part M default elevation
        public string SpacingRule     { get; set; } = "";   // Grid(2400,2400) / Perimeter(600) / PerArea(1per10sqm) / WallPitch(1500)
        public string OrientationRule { get; set; } = "";   // FaceAccessDoor / FaceNorth / FaceWallNormal / Free
        public string LevelHint       { get; set; } = "";   // Preferred level keywords ("Plant*", "Roof", "Basement")
        public string GroupKey        { get; set; } = "";   // Families placed as a set (RCP-MODULE-01)
        public double WeightKg        { get; set; }         // Triggers structural check + hanger selection

        public bool IsEmpty =>
            string.IsNullOrEmpty(HostType) && MountHeightMm == 0 &&
            string.IsNullOrEmpty(SpacingRule) && string.IsNullOrEmpty(OrientationRule) &&
            string.IsNullOrEmpty(LevelHint) && string.IsNullOrEmpty(GroupKey) && WeightKg == 0;
    }

    public static class PlacementParamReader
    {
        public static PlacementHints Read(Element el)
        {
            var h = new PlacementHints();
            if (el == null) return h;
            Element type = null;
            try { type = el.Document.GetElement(el.GetTypeId()); } catch { }
            Element primary = type ?? el;

            h.HostType        = ReadString(primary, "PLACE_HOST_TYPE_TXT");
            h.SpacingRule     = ReadString(primary, "PLACE_SPACING_RULE_TXT");
            h.OrientationRule = ReadString(primary, "PLACE_ORIENTATION_RULE_TXT");
            h.LevelHint       = ReadString(primary, "PLACE_LEVEL_HINT_TXT");
            h.GroupKey        = ReadString(primary, "PLACE_GROUP_KEY_TXT");
            h.MountHeightMm   = ReadLengthMm(primary, "PLACE_MOUNT_HEIGHT_MM");
            h.WeightKg        = ReadNumber(primary, "PLACE_WEIGHT_KG");
            return h;
        }

        /// <summary>
        /// Quick rank-order boost for PlacementScorer: returns a bias in
        /// 0..1 range reflecting how well a candidate level matches the
        /// family's LevelHint. Empty hint → no bias (returns 0.5). Wildcard
        /// match → 1.0. Mismatch → 0.1.
        /// </summary>
        public static double LevelHintBias(string hint, string levelName)
        {
            if (string.IsNullOrWhiteSpace(hint)) return 0.5;
            if (string.IsNullOrWhiteSpace(levelName)) return 0.5;
            string h = hint.Trim(); string l = levelName.Trim();
            foreach (string token in h.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (t.EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = t.Substring(0, t.Length - 1);
                    if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1.0;
                }
                else if (string.Equals(t, l, StringComparison.OrdinalIgnoreCase)) return 1.0;
            }
            return 0.1;
        }

        private static string ReadString(Element el, string name)
        {
            try { return el?.LookupParameter(name)?.AsString() ?? ""; }
            catch { return ""; }
        }

        private static double ReadLengthMm(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble() * 304.8;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }

        private static double ReadNumber(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }
    }
}
