// StingTools — PlacementCategoryRegistry.
//
// The engine-side answer to "which categories can this run actually place,
// and if not, why not?". The Placement Centre's Auto-place checklist is
// generated from this rather than from a hand-maintained list of named
// CheckBox fields, so the UI can no longer drift from the engine.
//
// Two inputs, both already the source of truth elsewhere:
//
//   1. STING_CATEGORY_TO_SEED_MAP.json (via CategoryToSeedRegistry) — the
//      per-category seed family plus the v2 `placeable` / `reason` contract.
//      Categories flagged placeable:false are the routing outputs (Conduits /
//      Pipes / Ducts / Cable Trays) and host elements (Stairs) that the engine
//      consumes as obstructions but never point-places.
//
//   2. The rules actually loaded for this run — a category with zero matching
//      rules is still offered (a project pack may add rules later) but is
//      reported as such, which is what "tick Specialty Equipment, get nothing"
//      used to look like with no explanation.
//
// Ordering/grouping comes from the JSON (`group` / `order`) so a project can
// re-group the checklist without a code change. Categories that appear only in
// a rule's CategoryFilter and not in the map are appended under "Other" —
// visible rather than silently dropped.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// One row of the Auto-place checklist: a category the run may target,
    /// whether the engine can place it, and the reason when it cannot.
    /// </summary>
    public class PlacementCategorySupport
    {
        /// <summary>Revit category name, matching a rule's CategoryFilter.</summary>
        public string Category { get; set; } = "";

        /// <summary>False when the engine cannot point-place this category.</summary>
        public bool Placeable { get; set; } = true;

        /// <summary>
        /// Tooltip text. For non-placeable categories this is the JSON `reason`.
        /// For placeable ones it is a generated advisory (rule count, seed
        /// availability) — never a hand-written per-category string.
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>Display grouping ("Electrical", "Routing outputs", "Other").</summary>
        public string Group { get; set; } = "";

        /// <summary>Display order within the checklist.</summary>
        public int Order { get; set; } = int.MaxValue;

        /// <summary>Number of loaded rules whose CategoryFilter targets this category.</summary>
        public int RuleCount { get; set; }

        /// <summary>True when a seed family is declared, so placement works with no manufacturer family loaded.</summary>
        public bool HasSeed { get; set; }
    }

    public static class PlacementCategoryRegistry
    {
        private const string OtherGroup = "Other";

        /// <summary>
        /// Build the checklist for a document and the rules loaded for the run.
        /// <paramref name="rules"/> may be null/empty — the map still drives the
        /// full category list, every row reporting "no rules loaded".
        /// </summary>
        public static List<PlacementCategorySupport> GetSupport(
            Autodesk.Revit.DB.Document doc, IEnumerable<PlacementRule> rules)
        {
            var ruleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    var cat = r?.CategoryFilter;
                    if (string.IsNullOrWhiteSpace(cat)) continue;
                    ruleCounts.TryGetValue(cat, out int n);
                    ruleCounts[cat] = n + 1;
                }
            }

            Dictionary<string, PlacementCategoryEntry> entries;
            try { entries = CategoryToSeedRegistry.GetEntries(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementCategoryRegistry: category map unavailable ({ex.Message}); " +
                              "falling back to the categories named by the loaded rules.");
                entries = new Dictionary<string, PlacementCategoryEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var rows = new List<PlacementCategorySupport>();

            foreach (var kv in entries)
            {
                var e = kv.Value;
                if (e == null) continue;
                ruleCounts.TryGetValue(e.Category, out int n);
                rows.Add(new PlacementCategorySupport
                {
                    Category  = e.Category,
                    Placeable = e.Placeable,
                    Group     = string.IsNullOrWhiteSpace(e.Group) ? OtherGroup : e.Group,
                    Order     = e.Order,
                    RuleCount = n,
                    HasSeed   = !string.IsNullOrWhiteSpace(e.Seed),
                    Reason    = BuildReason(e, n),
                });
            }

            // Categories named only by a rule — surfaced rather than dropped, so
            // a project pack targeting an unmapped category is still tickable.
            foreach (var kv in ruleCounts)
            {
                if (entries.ContainsKey(kv.Key)) continue;
                rows.Add(new PlacementCategorySupport
                {
                    Category  = kv.Key,
                    Placeable = true,
                    Group     = OtherGroup,
                    Order     = int.MaxValue,
                    RuleCount = kv.Value,
                    HasSeed   = false,
                    Reason    = $"{kv.Value} rule(s) target this category, but it is not in " +
                                "STING_CATEGORY_TO_SEED_MAP.json — no seed family is declared, so placement " +
                                "needs a matching family already loaded in the project.",
                });
            }

            return rows
                .OrderBy(r => r.Group == OtherGroup ? 1 : 0)
                .ThenBy(r => r.Order)
                .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildReason(PlacementCategoryEntry e, int ruleCount)
        {
            if (!e.Placeable)
            {
                return string.IsNullOrWhiteSpace(e.Reason)
                    ? "Not a point-placement target for the placement engine."
                    : e.Reason;
            }

            if (ruleCount == 0)
            {
                return string.IsNullOrWhiteSpace(e.Seed)
                    ? "No rules in the active pack target this category, and no seed family is declared — " +
                      "ticking it places nothing until a project rule pack covers it."
                    : $"No rules in the active pack target this category — ticking it places nothing until a " +
                      $"project rule pack covers it. Seed family '{e.Seed}' is available once rules exist.";
            }

            return string.IsNullOrWhiteSpace(e.Seed)
                ? $"{ruleCount} rule(s) in the active pack. No seed family is declared, so a matching family " +
                  "must already be loaded in the project."
                : $"{ruleCount} rule(s) in the active pack. Seed family '{e.Seed}' is built on demand when no " +
                  "manufacturer family is loaded.";
        }
    }
}
