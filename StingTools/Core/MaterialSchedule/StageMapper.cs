// ══════════════════════════════════════════════════════════════════════════
//  StageMapper.cs — MAT-SCHED construction-stage routing and lettering.
//
//  Routing key is the CONSTITUENT KIND, not the element. One wall sends its
//  blockwork and mortar to a storey stage and its plaster to Finishes — which
//  is why the PATMAC reference sample's Finishes section carries its own cement.
//
//  Section letters are ASSIGNED here and never authored, so the sample's
//  duplicated C/D/E letters and its mismatched summary order are unrepresentable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class StageDefinition
    {
        public string StageId = "";
        public string Title = "";
        public string Preamble = "";
        public int Order;
        /// <summary>Constituent kinds routed here. Checked before Categories.</summary>
        public List<string> ConstituentKinds = new List<string>();
        /// <summary>Revit category display names routed here.</summary>
        public List<string> Categories = new List<string>();

        /// <summary>
        /// Type-name / description substrings routed here, checked AFTER the
        /// constituent kind and BEFORE the category.
        ///
        /// Category alone routes the element, and an element's category often
        /// says nothing about the work it belongs to: the first real export put
        /// `2022_RoofCap_Eagle_HighProfileTiles` and a gate in SUPERSTRUCTURE
        /// because that is where their categories point. A kind still wins —
        /// a roof-shaped name must not drag real concrete out of the frame.
        /// </summary>
        public List<string> TypePatterns = new List<string>();
        /// <summary>When set, only rows on these level codes route here.</summary>
        public List<string> LevelCodes = new List<string>();
    }

    public sealed class StageLibrary
    {
        public string SchemaVersion = "1.0";
        public string DefaultStageId = "";
        public List<StageDefinition> Stages = new List<StageDefinition>();

        /// <summary>
        /// Revit categories that are NOT materials and must never appear in a
        /// material schedule — furniture, entourage, planting, topography.
        /// Excluded rows are counted and reported, never silently dropped.
        ///
        /// This list is deliberately conservative. Doors, windows, fixtures and
        /// fittings ARE bought and belong in the schedule (the reference sample
        /// gives them their own section); they simply have no supplier-unit rule
        /// yet, which is a missing rate, not a reason to hide them.
        /// </summary>
        public List<string> ExcludedCategories = new List<string>();

        /// <summary>
        /// Description / type-name substrings that are never materials, for rows
        /// whose CATEGORY is legitimate. The first real export sold an OPENING
        /// 1,187 times: `M_GM_OpeningWall_Instance — Opening` is a void, but its
        /// category is Generic Models, which elsewhere holds real building
        /// elements — so excluding the category would hide genuine work.
        ///
        /// A blank pattern is inert, never a wildcard.
        /// </summary>
        public List<string> ExcludedDescriptionPatterns = new List<string>();

        /// <summary>
        /// Categories a description pattern may NEVER exclude. The first real
        /// export dropped one Windows row as not-a-material while keeping twelve
        /// others: some pattern written for Generic Models voids matched a real
        /// window type. A door or a window is always bought, whatever it is
        /// called, so the category outranks the pattern.
        /// </summary>
        public List<string> ExclusionProtectedCategories = new List<string>();

        /// <summary>
        /// Intermediate measures and the constituents bought in their place.
        /// See <see cref="IntermediateMeasureMarker"/>.
        /// </summary>
        public List<IntermediateMeasureRule> IntermediateMeasures = new List<IntermediateMeasureRule>();
    }

    /// <summary>
    /// Stage resolution, indexed once per build.
    ///
    /// PERF: ResolveStageId sorted the definition list and allocated a new List
    /// on EVERY row, then walked it with three LINQ closures. Invisible on a
    /// 60-row model, wasteful on a 20k-element federated one. The answer is
    /// identical — StageIndexEquivalenceTests pins that — the work just happens
    /// once instead of per row.
    ///
    /// Lookups are first-wins in Order sequence, which is what the sort was for.
    /// </summary>
    public sealed class StageIndex
    {
        private readonly Dictionary<string, string> _byKind =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _byCategory =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _byLevel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Ordered, not a dictionary: these are substring tests, and first-wins
        // in stage Order is the same precedence every other lookup here uses.
        private readonly List<KeyValuePair<string, string>> _byTypePattern =
            new List<KeyValuePair<string, string>>();
        private readonly string _default;

        private StageIndex(string defaultStageId) { _default = defaultStageId ?? ""; }

        public static StageIndex Build(IReadOnlyList<StageDefinition> defs, string defaultStageId)
        {
            var ix = new StageIndex(defaultStageId);
            if (defs == null) return ix;

            // Ascending Order, so the first writer of a key is the winner and
            // later duplicates are ignored — same precedence the old FirstOrDefault
            // over the sorted list produced.
            foreach (var d in defs.OrderBy(d => d.Order))
            {
                if (d == null) continue;
                Seed(ix._byKind, d.ConstituentKinds, d.StageId);
                Seed(ix._byCategory, d.Categories, d.StageId);
                Seed(ix._byLevel, d.LevelCodes, d.StageId);
                foreach (string p in d.TypePatterns ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(p))
                        ix._byTypePattern.Add(new KeyValuePair<string, string>(p.Trim(), d.StageId));
            }
            return ix;
        }

        private static void Seed(Dictionary<string, string> map, List<string> keys, string stageId)
        {
            if (keys == null) return;
            foreach (string k in keys)
                if (!string.IsNullOrWhiteSpace(k) && !map.ContainsKey(k.Trim()))
                    map[k.Trim()] = stageId;
        }

        /// <summary>Kind → category → level → the caller's default. An unmatched
        /// row is never dropped; it lands in the default so it stays countable.</summary>
        public string Resolve(string constituentKind, string category, string levelCode)
            => Resolve(constituentKind, category, levelCode, null);

        /// <summary>
        /// Kind → type pattern → category → level → the caller's default.
        /// <paramref name="typeText"/> is the type name and description joined;
        /// pass null and this behaves exactly as the three-argument overload.
        /// </summary>
        public string Resolve(string constituentKind, string category, string levelCode, string typeText)
        {
            if (!string.IsNullOrWhiteSpace(constituentKind)
                && _byKind.TryGetValue(constituentKind.Trim(), out string s)) return s;

            if (!string.IsNullOrWhiteSpace(typeText) && _byTypePattern.Count > 0)
            {
                foreach (var kv in _byTypePattern)
                    if (PatternMatch.Contains(typeText, kv.Key))
                        return kv.Value;
            }

            if (!string.IsNullOrWhiteSpace(category)
                && _byCategory.TryGetValue(category.Trim(), out s)) return s;
            if (!string.IsNullOrWhiteSpace(levelCode)
                && _byLevel.TryGetValue(levelCode.Trim(), out s)) return s;
            return _default;
        }
    }

    public static class StageMapper
    {
        /// <summary>
        /// Resolve a row to a stage id. Precedence: constituent kind → category →
        /// level code → the caller's named default. An unmatched row is never
        /// dropped; it lands in the default so it stays visible and countable.
        /// </summary>
        /// <summary>
        /// Resolve a single row. Convenience wrapper over <see cref="StageIndex"/>
        /// for callers holding one row; anything looping over rows should build
        /// the index once and call Resolve on it instead.
        /// </summary>
        public static string ResolveStageId(string constituentKind, string category,
            string levelCode, IReadOnlyList<StageDefinition> defs, string defaultStageId)
            => StageIndex.Build(defs, defaultStageId).Resolve(constituentKind, category, levelCode);

        /// <summary>
        /// Stamp sequential letters (A, B, … Z, AA, AB, …) in list order. The list
        /// order IS the document order, and the summary projects from the same
        /// list, so body and summary cannot disagree.
        /// </summary>
        public static void AssignLetters(IList<StageSection> stages)
        {
            if (stages == null) return;
            for (int i = 0; i < stages.Count; i++)
                if (stages[i] != null) stages[i].Letter = ToLetter(i);
        }

        /// <summary>0 → "A", 25 → "Z", 26 → "AA" (spreadsheet-column style).</summary>
        public static string ToLetter(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0) return "";
            string s = "";
            int n = zeroBasedIndex;
            while (true)
            {
                s = (char)('A' + (n % 26)) + s;
                n = n / 26 - 1;
                if (n < 0) break;
            }
            return s;
        }
    }
}
