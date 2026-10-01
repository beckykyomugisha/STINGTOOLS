// StingTools — DwgCaptureDedup (DTW-112).
//
// Revit-free half of the DWG fixture bridge's idempotency check. A re-run of the
// bridge must not place a second fixture on every DWG symbol it already placed.
// The bridge stamps each instance's provenance RuleId with the DWG capture point
// ("pt:x,y", model feet); a capture whose plan point matches a prior capture of the
// same category is skipped. Instances stamped before the point was recorded fall
// back to their location, with a wider tolerance because hosting snaps the
// insertion point onto the host face.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace StingTools.Core.Placement
{
    public static class DwgCaptureDedup
    {
        /// <summary>Plan tolerance for a recorded capture point (the same DWG symbol). 50 mm.</summary>
        public const double CapturePointToleranceFt = 50.0 / 304.8;

        /// <summary>Plan tolerance for a prior instance with no recorded capture point:
        /// its location is the hosted point, which can sit up to half a wall away. 300 mm.</summary>
        public const double LocationToleranceFt = 300.0 / 304.8;

        private const string Prefix = "pt:";

        /// <summary>The provenance token that records a capture point (model feet, plan only).</summary>
        public static string PointToken(double xFt, double yFt)
            => Prefix + xFt.ToString("F4", CultureInfo.InvariantCulture) + "," +
               yFt.ToString("F4", CultureInfo.InvariantCulture);

        /// <summary>Read the capture point back out of a provenance RuleId
        /// ("DWG:block|layer|...|pt:x,y"). False when the RuleId carries none.</summary>
        public static bool TryParsePoint(string ruleId, out double xFt, out double yFt)
        {
            xFt = 0; yFt = 0;
            if (string.IsNullOrEmpty(ruleId)) return false;
            foreach (var part in ruleId.Split('|'))
            {
                var p = part.Trim();
                if (!p.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var xy = p.Substring(Prefix.Length).Split(',');
                if (xy.Length != 2) return false;
                return double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out xFt)
                    && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out yFt);
            }
            return false;
        }

        /// <summary>Prior placements of one category: recorded capture points and, for
        /// older instances, bare locations.</summary>
        public sealed class PriorIndex
        {
            public List<(double X, double Y)> CapturePoints { get; } = new List<(double, double)>();
            public List<(double X, double Y)> Locations { get; } = new List<(double, double)>();
            public int Count => CapturePoints.Count + Locations.Count;

            /// <summary>Add one prior instance from its provenance RuleId and location.</summary>
            public void Add(string ruleId, double locX, double locY, bool hasLocation)
            {
                if (TryParsePoint(ruleId, out var x, out var y)) CapturePoints.Add((x, y));
                else if (hasLocation) Locations.Add((locX, locY));
            }

            /// <summary>True when a capture at (x, y) was already placed.</summary>
            public bool IsDuplicate(double xFt, double yFt)
            {
                foreach (var p in CapturePoints)
                    if (Within(p, xFt, yFt, CapturePointToleranceFt)) return true;
                foreach (var p in Locations)
                    if (Within(p, xFt, yFt, LocationToleranceFt)) return true;
                return false;
            }

            private static bool Within((double X, double Y) p, double x, double y, double tol)
            {
                double dx = p.X - x, dy = p.Y - y;
                return dx * dx + dy * dy <= tol * tol;
            }
        }
    }
}
