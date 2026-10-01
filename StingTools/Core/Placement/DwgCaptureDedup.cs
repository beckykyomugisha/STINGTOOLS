// StingTools — DwgCaptureDedup (DTW-112, DTW-131).
//
// Revit-free half of the DWG fixture bridge's idempotency check. A re-run of the
// bridge must not place a second fixture on every DWG symbol it already placed.
// The bridge stamps each instance's provenance RuleId with the DWG capture point
// ("pt:x,y", model feet) and the capture's level ("lvl:<level id>"); a capture whose
// plan point matches a prior capture of the same category ON THE SAME LEVEL is
// skipped. Instances stamped before the point was recorded fall back to their
// location, with a wider tolerance because hosting snaps the insertion point onto
// the host face.
//
// DTW-131 — the match used to be plan X/Y only, so on stacked identical floors every
// fixture of every later floor counted as "already placed" by the floor below and
// was silently dropped. A prior stamp with a level is compared by level id; one
// without (stamped before DTW-131) is compared by level elevation, and only counts
// when its level is within half a storey of the capture's level. A prior with no
// level evidence at all is never treated as a duplicate: a visible duplicate is
// recoverable, a silently missing fixture is not.

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

        /// <summary>Half-storey band used when the model has only one level (half of a 3 m storey).</summary>
        public const double DefaultHalfStoreyFt = 1500.0 / 304.8;

        private const string Prefix = "pt:";
        private const string LevelPrefix = "lvl:";

        /// <summary>The provenance token that records a capture point (model feet, plan only).</summary>
        public static string PointToken(double xFt, double yFt)
            => Prefix + xFt.ToString("F4", CultureInfo.InvariantCulture) + "," +
               yFt.ToString("F4", CultureInfo.InvariantCulture);

        /// <summary>DTW-131 — the provenance token that records the capture's level (its element id).</summary>
        public static string LevelToken(string levelKey) => LevelPrefix + (levelKey ?? "");

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

        /// <summary>DTW-131 — read the level key out of a provenance RuleId. False when the
        /// RuleId was stamped before levels were recorded.</summary>
        public static bool TryParseLevel(string ruleId, out string levelKey)
        {
            levelKey = null;
            if (string.IsNullOrEmpty(ruleId)) return false;
            foreach (var part in ruleId.Split('|'))
            {
                var p = part.Trim();
                if (!p.StartsWith(LevelPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var v = p.Substring(LevelPrefix.Length).Trim();
                if (v.Length == 0) return false;
                levelKey = v;
                return true;
            }
            return false;
        }

        /// <summary>DTW-131 — half the storey height at <paramref name="levelElevFt"/>: half the
        /// gap to the next level above, else to the level below, else <see cref="DefaultHalfStoreyFt"/>.</summary>
        public static double HalfStoreyFt(IEnumerable<double> levelElevationsFt, double levelElevFt)
        {
            const double same = 1e-6;
            double? above = null, below = null;
            if (levelElevationsFt != null)
            {
                foreach (var e in levelElevationsFt)
                {
                    if (e > levelElevFt + same) { if (above == null || e < above) above = e; }
                    else if (e < levelElevFt - same) { if (below == null || e > below) below = e; }
                }
            }
            if (above != null) return (above.Value - levelElevFt) / 2.0;
            if (below != null) return (levelElevFt - below.Value) / 2.0;
            return DefaultHalfStoreyFt;
        }

        /// <summary>The level a capture sits on: its key (level element id) and elevation,
        /// plus the half-storey band an unkeyed prior must fall within.</summary>
        public readonly struct CaptureLevel
        {
            public CaptureLevel(string key, double elevationFt, double halfStoreyFt)
            { Key = key; ElevationFt = elevationFt; HalfStoreyFt = halfStoreyFt; }
            public string Key { get; }
            public double ElevationFt { get; }
            public double HalfStoreyFt { get; }
        }

        /// <summary>Prior placements of one category: recorded capture points and, for
        /// older instances, bare locations — each with the level evidence it carries.</summary>
        public sealed class PriorIndex
        {
            private sealed class Prior
            {
                public double X, Y;
                public bool IsCapturePoint;
                public string LevelKey;        // from the "lvl:" token (DTW-131 stamps)
                public double? LevelElevFt;    // the instance's own level elevation (fallback)
            }

            private readonly List<Prior> _items = new List<Prior>();
            public int Count => _items.Count;

            /// <summary>Add one prior instance from its provenance RuleId, its location, and the
            /// elevation of the level it sits on (null when unknown).</summary>
            public void Add(string ruleId, double locX, double locY, bool hasLocation, double? priorLevelElevFt)
            {
                TryParseLevel(ruleId, out var key);
                if (TryParsePoint(ruleId, out var x, out var y))
                    _items.Add(new Prior { X = x, Y = y, IsCapturePoint = true, LevelKey = key, LevelElevFt = priorLevelElevFt });
                else if (hasLocation)
                    _items.Add(new Prior { X = locX, Y = locY, IsCapturePoint = false, LevelKey = key, LevelElevFt = priorLevelElevFt });
            }

            /// <summary>True when a capture at (x, y) on <paramref name="level"/> was already placed.</summary>
            public bool IsDuplicate(double xFt, double yFt, CaptureLevel level)
            {
                foreach (var p in _items)
                {
                    if (!SameLevel(p, level)) continue;
                    double tol = p.IsCapturePoint ? CapturePointToleranceFt : LocationToleranceFt;
                    if (Within(p, xFt, yFt, tol)) return true;
                }
                return false;
            }

            private static bool SameLevel(Prior p, CaptureLevel level)
            {
                if (p.LevelKey != null && level.Key != null)
                    return string.Equals(p.LevelKey, level.Key, StringComparison.Ordinal);
                if (p.LevelElevFt != null)
                    return Math.Abs(p.LevelElevFt.Value - level.ElevationFt) < level.HalfStoreyFt;
                return false;   // no level evidence — never silently drop a capture on it
            }

            private static bool Within(Prior p, double x, double y, double tol)
            {
                double dx = p.X - x, dy = p.Y - y;
                return dx * dx + dy * dy <= tol * tol;
            }
        }
    }
}
