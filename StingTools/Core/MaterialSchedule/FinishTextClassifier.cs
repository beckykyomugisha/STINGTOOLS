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

        // ── ceilings (MATSCHED-T2) ────────────────────────────────────────

        /// <summary>
        /// Board products sold as 1200x2400 SHEETS. The commodity rule converts
        /// m² to sheets at 2.88 m² each, so the predicate has to mean "sold by
        /// that sheet" and nothing looser.
        ///
        /// A BOARD WORD IS REQUIRED. The first draft matched bare "gypsum", and
        /// the test for "Gypsum Skim" caught it: a wet gypsum skim would have
        /// been priced as sheets of plasterboard — wrong in the count, the rate
        /// and the trade. Gypsum is the material; board is the product.
        /// </summary>
        private static readonly Regex CeilingBoardPattern = new Regex(
            @"plasterboard|plaster\s*board|gypsum\s*(wall\s*)?board|gyproc|drywall|wall\s*board|cement\s*board",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Ceiling finishes that are NOT sheet board. Every one of these is a
        /// real product bought a different way — a mineral-fibre tile by the
        /// tile, PVC and T&amp;G by the length — so pricing them per 2.88 m² sheet
        /// would be wrong in both the count and the rate. They are rejected and
        /// NAMED by the scan rather than absorbed.
        ///
        /// It names product FORMS, not adjectives. The first draft carried bare
        /// `acoustic` and bare `metal`, which would have rejected "Acoustic
        /// Plasterboard" — a genuine 1200x2400 sheet. Over-exclusion is not the
        /// safe direction: it drops a real material silently, exactly like the
        /// omission this whole task exists to fix.
        /// </summary>
        private static readonly Regex NotCeilingBoardPattern = new Regex(
            @"ceiling tile|mineral fibre|mineral fiber|acoustic tile|pvc|t&g|tongue|timber|softwood|hardwood|aluminium|aluminum|metal pan|metal tile",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsCeilingBoard(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NotCeilingBoardPattern.IsMatch(name)) return false;
            return CeilingBoardPattern.IsMatch(name);
        }

        /// <summary>
        /// A wet plaster or skim coat on a ceiling. Distinct from board: it is
        /// bought as cement and sand, not as sheets, and a ceiling can carry
        /// BOTH (board skimmed after fixing) — which is why these are two
        /// predicates and not one classification.
        /// </summary>
        private static readonly Regex CeilingPlasterPattern = new Regex(
            @"plaster|skim|render|stucco",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsCeilingPlaster(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // "Plasterboard" and "Gypsum Plaster Board" both contain "plaster"
            // and are neither of them a wet coat. Board wins outright, so the
            // two predicates can never both accept the same layer.
            if (IsCeilingBoard(name)) return false;
            if (name.IndexOf("board", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return CeilingPlasterPattern.IsMatch(name);
        }

        // ── membranes (MATSCHED-T3) ───────────────────────────────────────

        /// <summary>
        /// Sheet membranes: bought by the ROLL, laid and lapped. A damp-proof
        /// membrane under a ground slab and a sarking underlay under a roof
        /// covering are both real purchases whose area the model already states
        /// in a `Membrane` layer, and both reached no export.
        /// </summary>
        private static readonly Regex MembranePattern = new Regex(
            @"membrane|dpm|damp[\s-]*proof|dpc|polythene|polyethylene|visqueen|underlay|sarking|breather|felt|vapou?r barrier|vapou?r control",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Things that sit in or beside a membrane layer and are NOT sheet
        /// membrane. Insulation is the dangerous one: it is bought by THICKNESS,
        /// not by the square metre of roll, so absorbing it into a membrane
        /// commodity would mis-price both. It is rejected here and reported by
        /// name — never absorbed, never silently dropped.
        ///
        /// The three-letter product codes carry word boundaries on BOTH sides.
        /// A bare `eps` matches "Steps"; a right-bounded `eps\b` still matches
        /// "Steps"; only `\beps\b` means the product. Same for `pir` inside
        /// "Respiratory" and `pur` inside "Purlin" — and a purlin underlay is a
        /// membrane this take-off must not throw away.
        /// </summary>
        private static readonly Regex NotMembranePattern = new Regex(
            @"insulat|polystyrene|\b(eps|xps|pir|pur)\b|rockwool|mineral wool|glass wool|screed|concrete|blinding|hardcore",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsMembrane(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (NotMembranePattern.IsMatch(name)) return false;
            return MembranePattern.IsMatch(name);
        }

        /// <summary>
        /// True when a name is INSULATION. Its own predicate rather than a
        /// silent exclusion, because the caution here is not "do not match it"
        /// but "say that you saw it": an insulation layer that produced nothing
        /// and said nothing is the same silent omission as the membranes this
        /// task exists to add.
        /// </summary>
        private static readonly Regex InsulationPattern = new Regex(
            @"insulat|polystyrene|\b(eps|xps|pir|pur)\b|rockwool|mineral wool|glass wool|thermal board",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsInsulation(string name)
            => !string.IsNullOrWhiteSpace(name) && InsulationPattern.IsMatch(name);

        /// <summary>
        /// Which membrane commodity a layer is. The NAME wins when it is
        /// unambiguous; otherwise the HOST decides, which is a fact the model
        /// states rather than a guess — a membrane in a roof is an underlay, a
        /// membrane in a floor is a damp-proof membrane.
        ///
        /// Returns "" for a name that is not a membrane at all, so a caller
        /// cannot accidentally file one under the other.
        /// </summary>
        public static string MembraneKind(string name, bool hostIsRoof)
        {
            if (!IsMembrane(name)) return "";
            string n = name.ToLowerInvariant();

            if (n.Contains("underlay") || n.Contains("sarking") || n.Contains("breather")
             || n.Contains("felt")) return "roof_underlay";
            if (n.Contains("dpm") || n.Contains("damp") || n.Contains("polythene")
             || n.Contains("polyethylene") || n.Contains("visqueen")) return "dpm";

            return hostIsRoof ? "roof_underlay" : "dpm";
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
