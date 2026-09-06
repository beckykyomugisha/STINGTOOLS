// ══════════════════════════════════════════════════════════════════════════
//  FinishTextClassifier.cs — MAT-SCHED-3: what a finish NAME means.
//
//  Two independent places need this answer and must agree:
//    * a compound-structure finish LAYER's material name, and
//    * a Room's Floor / Wall / Base Finish text.
//
//  They were about to hold two copies of the same regex, which is how the
//  supplier-unit and stage files came to disagree (MATSCHED-3c). One copy,
//  Revit-free, unit-tested.
//
//  Deliberately NARROW. A false positive prices a whole floor as tiling, which
//  is the defect the withdrawn unit-table rules produced; a false negative is
//  visible, because the scan reports every name it rejected.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Text.RegularExpressions;

namespace StingTools.Core.MaterialSchedule
{
    public static class FinishTextClassifier
    {
        private static readonly Regex TilePattern = new Regex(
            @"tile|ceramic|porcelain|terrazzo|mosaic|vitrified|granite|marble",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Names that read as tiling but are NOT a tiled surface. "Roof tile"
        /// on a floor finish is a mis-typed schedule, and "carpet tile" is
        /// carpet — it is laid, not bedded in adhesive and grouted, so pricing
        /// it as ceramic would be wrong in both quantity and rate.
        /// </summary>
        private static readonly Regex NotTilePattern = new Regex(
            @"carpet|vinyl tile|lvt|ceiling tile|roof tile|acoustic",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsTile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NotTilePattern.IsMatch(name)) return false;
            return TilePattern.IsMatch(name);
        }

        /// <summary>Finish name → MATERIAL_LOOKUP TypeKey. Unmatched falls to DEFAULT.</summary>
        public static string TileKey(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("porcelain") || n.Contains("vitrified")) return "PORCELAIN";
            if (n.Contains("terrazzo")) return "TERRAZZO";
            if (n.Contains("granite") || n.Contains("marble")) return "STONE";
            if (n.Contains("mosaic")) return "MOSAIC";
            return "CERAMIC";
        }

        // ── screed (MATSCHED-T1) ──────────────────────────────────────────

        /// <summary>
        /// Names that are a cement/sand SCREED. Deliberately narrower than the
        /// tile pattern, because the three things a screed is most easily
        /// confused with are all measured somewhere else already: plaster and
        /// render come off the wall path, mortar off the masonry path. Matching
        /// any of them here would not add a material — it would double-count one.
        /// </summary>
        private static readonly Regex ScreedPattern = new Regex(
            @"screed|sand[\s/_-]*cement|cement[\s/_-]*sand",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The double-count guard, and it is the whole reason IsScreed exists as
        /// its own predicate rather than a substring test at the call site.
        /// </summary>
        private static readonly Regex NotScreedPattern = new Regex(
            @"plaster|render|mortar|skim|insulat|adhesive|grout|paint",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsScreed(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NotScreedPattern.IsMatch(name)) return false;
            // Disjoint from tiling BY CONSTRUCTION, not by hoping the two
            // patterns never overlap. Two sources must never measure the same
            // surface, and a layer whose name reads as both would be measured
            // once as tiling and once as screed with nothing to say so.
            if (IsTile(name)) return false;
            return ScreedPattern.IsMatch(name);
        }

        /// <summary>
        /// Screed name → MATERIAL_LOOKUP TypeKey. The keys are the ones the
        /// shipped SCREED rows already use (STANDARD / HEAVY_DUTY / DEFAULT) —
        /// inventing a parallel set of keys for the same mixes is how two
        /// figures for one ratio come to disagree without anyone comparing them.
        /// Unmatched falls to STANDARD, which has its own row; the builder's
        /// DEFAULT fallback is a second net below that, not the primary answer.
        /// </summary>
        public static string ScreedKey(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("granolithic") || n.Contains("grano")
             || n.Contains("heavy duty") || n.Contains("heavy-duty")) return "HEAVY_DUTY";
            return "STANDARD";
        }

        /// <summary>
        /// True when a Base Finish names something real to buy. Rooms carry the
        /// literal strings "None", "N/A" and "-" far more often than they carry
        /// nothing at all, and each of those would otherwise mint a skirting run
        /// around a room that has none.
        /// </summary>
        public static bool IsRealFinish(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();
            if (n.Length <= 1) return false;
            return !Regex.IsMatch(n, @"^\s*(none|n/?a|nil|-+|tbc|tbd|x)\s*$", RegexOptions.IgnoreCase);
        }
    }
}
