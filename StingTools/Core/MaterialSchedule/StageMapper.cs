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
        /// <summary>When set, only rows on these level codes route here.</summary>
        public List<string> LevelCodes = new List<string>();
    }

    public sealed class StageLibrary
    {
        public string SchemaVersion = "1.0";
        public string DefaultStageId = "";
        public List<StageDefinition> Stages = new List<StageDefinition>();
    }

    public static class StageMapper
    {
        /// <summary>
        /// Resolve a row to a stage id. Precedence: constituent kind → category →
        /// level code → the caller's named default. An unmatched row is never
        /// dropped; it lands in the default so it stays visible and countable.
        /// </summary>
        public static string ResolveStageId(string constituentKind, string category,
            string levelCode, IReadOnlyList<StageDefinition> defs, string defaultStageId)
        {
            if (defs == null || defs.Count == 0) return defaultStageId ?? "";
            var ordered = defs.OrderBy(d => d.Order).ToList();

            if (!string.IsNullOrWhiteSpace(constituentKind))
            {
                var hit = ordered.FirstOrDefault(d => d.ConstituentKinds != null
                    && d.ConstituentKinds.Any(k => string.Equals(k, constituentKind, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var hit = ordered.FirstOrDefault(d => d.Categories != null
                    && d.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            if (!string.IsNullOrWhiteSpace(levelCode))
            {
                var hit = ordered.FirstOrDefault(d => d.LevelCodes != null && d.LevelCodes.Count > 0
                    && d.LevelCodes.Any(l => string.Equals(l, levelCode, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            return defaultStageId ?? "";
        }

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
