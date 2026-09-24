// StingTools — Drawing Template Manager · Phase 175
//
// DimensionStrategy resolves the right Revit DimensionType for a
// pack-declared strategy ("Linear" / "Ordinate" / "Chain") and offers
// shared geometry helpers used by every dimensioner.
//
// "Linear" and "Chain" both map to a Linear-style DimensionType — the
// difference is how many references the caller appends (one extra
// reference = one extra segment Revit auto-creates). "Ordinate" requires
// a DimensionType authored with StyleType == Ordinate; we look up the
// project's first matching type and warn if none is loaded.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing.Dimensioning
{
    public enum DimStrategyKind
    {
        Linear = 0,
        Chain = 1,
        Ordinate = 2,
        /// <summary>
        /// "None" — the profile declares that it does not want automatic
        /// dimensioning. DrawingTypeExcelCommands has always offered "None"
        /// in the dimensionStrategy dropdown and two shipped profiles use it,
        /// but Parse's default arm mapped it to Linear, so "None" dimensioned
        /// exactly like "Linear". Now it is a real member and
        /// <see cref="Suppresses"/> is the one question callers ask.
        /// </summary>
        None = 3,
    }

    internal static class DimensionStrategy
    {
        public const double MmPerFt = 304.8;

        public static DimStrategyKind Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return DimStrategyKind.Linear;
            switch (s.Trim().ToLowerInvariant())
            {
                case "ordinate": return DimStrategyKind.Ordinate;
                case "chain":    return DimStrategyKind.Chain;
                case "none":     return DimStrategyKind.None;
                case "":         return DimStrategyKind.Linear;
                default:         return DimStrategyKind.Linear;
            }
        }

        /// <summary>
        /// True when the profile has asked for no automatic dimensioning.
        /// Checked by the dim pass before any engine runs, so "None" is
        /// honoured once, centrally, rather than by each dimensioner.
        /// </summary>
        public static bool Suppresses(string strategy) => Parse(strategy) == DimStrategyKind.None;

        /// <summary>
        /// Resolve a project-loaded DimensionType matching the strategy +
        /// optional named style. Falls back to the document's default
        /// linear DimensionType when no Ordinate type has been authored
        /// (so we never throw).
        /// </summary>
        public static DimensionType ResolveType(Document doc, DimStrategyKind kind, string namedStyle)
        {
            if (doc == null) return null;

            // Caller-named style wins when present.
            if (!string.IsNullOrEmpty(namedStyle))
            {
                var named = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(t => string.Equals(t.Name, namedStyle, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
            }

            // Strategy-aware fallback.
            try
            {
                var all = new FilteredElementCollector(doc).OfClass(typeof(DimensionType))
                    .Cast<DimensionType>().ToList();
                if (kind == DimStrategyKind.Ordinate)
                {
                    var ord = all.FirstOrDefault(t =>
                        SafeStyleType(t) == DimensionStyleType.LinearFixed ||
                        SafeStyleType(t) == DimensionStyleType.SpotElevation
                            ? false
                            : t.Name?.IndexOf("ordinate", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (ord != null) return ord;
                }

                // Linear / Chain default → first non-spot, non-angular type.
                var linear = all.FirstOrDefault(t =>
                {
                    var st = SafeStyleType(t);
                    return st == DimensionStyleType.Linear ||
                           st == DimensionStyleType.LinearFixed;
                });
                return linear ?? all.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DimensionStrategy.ResolveType: {ex.Message}");
                return null;
            }
        }

        private static DimensionStyleType SafeStyleType(DimensionType t)
        {
            try { return t.StyleType; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return DimensionStyleType.Linear; }
        }

        /// <summary>
        /// Build a witness line offset perpendicular to <paramref name="axis"/>
        /// at the supplied mm offset. Used by every dim pass to keep the
        /// generated dimension clear of the underlying geometry.
        /// </summary>
        public static Line BuildWitnessLine(XYZ origin, XYZ axis, double offsetMm, double lengthFt)
        {
            if (axis == null || axis.IsZeroLength()) axis = XYZ.BasisX;
            var unit = axis.Normalize();
            var perp = new XYZ(-unit.Y, unit.X, 0);
            if (perp.IsZeroLength()) perp = XYZ.BasisY;
            perp = perp.Normalize();
            var off = perp * (offsetMm / MmPerFt);
            var p0 = origin + off;
            var p1 = origin + off + unit * Math.Max(lengthFt, 1.0 / MmPerFt);
            return Line.CreateBound(p0, p1);
        }

        /// <summary>
        /// Sort a list of references by their projection onto an axis so
        /// the resulting Dimension.Segments list reads left-to-right.
        /// </summary>
        public static List<(Reference R, XYZ P)> SortAlongAxis(IEnumerable<(Reference R, XYZ P)> pts, XYZ axis)
        {
            if (axis == null || axis.IsZeroLength()) axis = XYZ.BasisX;
            var u = axis.Normalize();
            return pts.OrderBy(x => x.P.DotProduct(u)).ToList();
        }
    }
}
