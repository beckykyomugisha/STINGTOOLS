// StingTools — Visibility Center · view-filter creation
//
// Follows the StaleFlagCommands.FindOrCreateFilter idiom: look the filter up by name
// first, intersect categories with ParameterFilterUtilities.GetAllFilterableCategories(),
// then ParameterFilterElement.Create → view.AddFilter → view.SetFilterVisibility.
//
// Token filters delegate to the existing AecFilterFactory (it already resolves shared
// parameters through ParamRegistry.AllParamGuids, OR-combines with LogicalOrFilter, and
// reports an unbound parameter as a warning rather than throwing). Two cases it cannot
// cover are handled here directly:
//   · a category-only filter, which needs the rule-less ParameterFilterElement.Create overload;
//   · an inverted (show-only) filter, whose rule tree is the negation of the match set.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Core.Visibility
{
    /// <summary>Creates and applies the <c>STING VIS - </c> view filters a plan needs.</summary>
    internal static class VisibilityFilterBuilder
    {
        /// <summary>Name of the single inverted filter used for show-only in saved mode.</summary>
        internal const string IsolateFilterName = VisibilityRuleMatcher.FilterPrefix + "NOT (isolate)";

        // ── Parameter binding checks (blocker detection) ────────────────

        /// <summary>Category ids (as ints) a shared parameter is bound to in this project.</summary>
        internal static HashSet<int> BoundCategories(Document doc, string paramName)
        {
            var bound = new HashSet<int>();
            if (doc == null || string.IsNullOrWhiteSpace(paramName)) return bound;

            try
            {
                var it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    var def = it.Key as Definition;
                    if (def == null ||
                        !string.Equals(def.Name, paramName, StringComparison.OrdinalIgnoreCase)) continue;

                    var binding = it.Current as ElementBinding;
                    if (binding?.Categories == null) continue;
                    foreach (Category c in binding.Categories)
                    {
                        if (c != null) bound.Add((int)c.Id.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityFilterBuilder.BoundCategories({paramName}): {ex.Message}");
            }
            return bound;
        }

        /// <summary>
        /// Categories in <paramref name="wanted"/> that Revit will actually accept in a view
        /// filter. Mirrors StaleFlagCommands.BuildFilterableCategoryIds.
        /// </summary>
        internal static List<ElementId> FilterableCategoryIds(Document doc, IEnumerable<int> wanted)
        {
            var ids = new List<ElementId>();
            if (doc == null || wanted == null) return ids;

            ICollection<ElementId> filterable;
            try { filterable = ParameterFilterUtilities.GetAllFilterableCategories(); }
            catch (Exception ex)
            {
                StingLog.Warn($"GetAllFilterableCategories: {ex.Message}");
                return ids;
            }

            foreach (int raw in wanted.Distinct())
            {
                try
                {
                    var id = new ElementId((long)raw);
                    if (filterable.Contains(id) && !ids.Contains(id)) ids.Add(id);
                }
                catch (Exception ex) { StingLog.Warn($"Visibility category {raw}: {ex.Message}"); }
            }
            return ids;
        }

        // ── Filter creation ─────────────────────────────────────────────

        /// <summary>
        /// Find-or-create the <c>ParameterFilterElement</c> for one planned filter.
        /// Caller owns the transaction. Returns <see cref="ElementId.InvalidElementId"/> and
        /// appends a user-facing blocker when the filter cannot be built.
        /// </summary>
        internal static ElementId FindOrCreate(
            Document doc, PlannedFilter pf, IList<int> scopeCategoryIds,
            IList<string> blockers, ref int created, ref int reused)
        {
            if (doc == null || pf == null) return ElementId.InvalidElementId;

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, pf.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { reused++; return existing.Id; }

            if (pf.Kind == VisibilityRuleKind.Category)
            {
                var catIds = FilterableCategoryIds(doc, pf.CategoryIds);
                if (catIds.Count == 0)
                {
                    blockers.Add($"'{pf.Value}' cannot be used in a view filter (Revit does not allow it); skipped.");
                    return ElementId.InvalidElementId;
                }
                try
                {
                    // Rule-less overload: matches every element of these categories.
                    var pfe = ParameterFilterElement.Create(doc, pf.Name, catIds);
                    created++;
                    return pfe.Id;
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Visibility category filter '{pf.Name}'", ex);
                    blockers.Add($"Could not create filter '{pf.Name}': {ex.Message}");
                    return ElementId.InvalidElementId;
                }
            }

            // Token filter — check the shared parameter is actually bound before asking
            // AecFilterFactory to build anything, so we can name the categories that are missing.
            string paramName = TokenValueHarvester.ParameterNameFor(pf.TokenKey);
            if (string.IsNullOrWhiteSpace(paramName))
            {
                blockers.Add($"Unknown token '{pf.TokenKey}'; skipped.");
                return ElementId.InvalidElementId;
            }

            var wantedCats = (scopeCategoryIds != null && scopeCategoryIds.Count > 0)
                ? scopeCategoryIds.ToList()
                : pf.CategoryIds.ToList();

            var bound = BoundCategories(doc, paramName);
            var unbound = wantedCats.Where(c => !bound.Contains(c)).ToList();
            var usable = wantedCats.Where(bound.Contains).ToList();

            if (usable.Count == 0)
            {
                blockers.Add(
                    $"{pf.TokenKey} ({paramName}) is not bound to any category in scope, " +
                    $"so it cannot drive a saved view filter. Bind the shared parameter, or use Temporary mode.");
                return ElementId.InvalidElementId;
            }
            if (unbound.Count > 0)
            {
                blockers.Add(
                    $"{pf.TokenKey} is not bound to {DescribeCategories(doc, unbound)}; " +
                    $"{unbound.Count} categor{(unbound.Count == 1 ? "y" : "ies")} skipped.");
            }

            var usableIds = FilterableCategoryIds(doc, usable);
            if (usableIds.Count == 0)
            {
                blockers.Add($"No filterable category remains for {pf.TokenKey}; skipped.");
                return ElementId.InvalidElementId;
            }

            var def = new AecFilterDefinition
            {
                Name = pf.Name,
                Categories = usableIds.Select(id => ((BuiltInCategory)id.Value).ToString()).ToList(),
                Rule = TokenLeaf(paramName, pf.Value, negate: false)
            };

            var result = AecFilterFactory.FindOrCreate(doc, def);
            foreach (var w in result.Warnings) blockers.Add(w);
            if (!result.Ok)
            {
                blockers.Add(result.Error ?? $"Could not create filter '{pf.Name}'.");
                return ElementId.InvalidElementId;
            }
            if (result.Created) created++; else reused++;
            return result.Filter.Id;
        }

        /// <summary>
        /// The single inverted filter that implements show-only in saved mode:
        /// hide everything that does NOT match the set. NOT(A AND B) == (NOT A) OR (NOT B),
        /// and within a group NOT(v1 OR v2) == (≠v1 AND ≠v2).
        /// </summary>
        internal static ElementId FindOrCreateIsolate(
            Document doc, VisibilitySet set, IList<int> scopeCategoryIds,
            IList<string> blockers, ref int created, ref int reused)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, IsolateFilterName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { reused++; return existing.Id; }

            var groups = VisibilityRuleMatcher.GroupRules(set);
            var orBranches = new List<AecFilterRule>();
            var usableCats = new HashSet<int>(scopeCategoryIds ?? new List<int>());

            foreach (var group in groups)
            {
                var rules = group.Value;
                if (rules.Count == 0) continue;

                if (rules[0].Kind == VisibilityRuleKind.Category)
                {
                    // A view filter only sees elements of the categories it binds to, so it
                    // cannot hide elements of *other* categories. Report it rather than
                    // silently producing a filter that does nothing.
                    blockers.Add(
                        "Show-only by category cannot be expressed as a saved view filter " +
                        "(a filter can only act on the categories it is bound to). " +
                        "Use Temporary mode for category isolation, or switch those rules to Hide.");
                    continue;
                }

                string paramName = TokenValueHarvester.ParameterNameFor(rules[0].TokenKey);
                if (string.IsNullOrWhiteSpace(paramName)) continue;

                var bound = BoundCategories(doc, paramName);
                usableCats.RemoveWhere(c => !bound.Contains(c));

                var negations = new List<AecFilterRule>();
                foreach (var r in rules)
                    foreach (var v in r.Values ?? new List<string>())
                        negations.Add(TokenLeaf(paramName, v, negate: true));

                if (negations.Count == 0) continue;
                orBranches.Add(negations.Count == 1
                    ? negations[0]
                    : new AecFilterRule { Logic = "and", Rules = negations });
            }

            if (orBranches.Count == 0)
            {
                if (blockers.Count == 0)
                    blockers.Add("Nothing in this set can be isolated with a saved view filter.");
                return ElementId.InvalidElementId;
            }

            var catIds = FilterableCategoryIds(doc, usableCats);
            if (catIds.Count == 0)
            {
                blockers.Add("No category in scope has the token parameters bound, so isolate cannot be saved to the view.");
                return ElementId.InvalidElementId;
            }

            var def = new AecFilterDefinition
            {
                Name = IsolateFilterName,
                Categories = catIds.Select(id => ((BuiltInCategory)id.Value).ToString()).ToList(),
                Rule = orBranches.Count == 1
                    ? orBranches[0]
                    : new AecFilterRule { Logic = "or", Rules = orBranches }
            };

            var result = AecFilterFactory.FindOrCreate(doc, def);
            foreach (var w in result.Warnings) blockers.Add(w);
            if (!result.Ok)
            {
                blockers.Add(result.Error ?? "Could not create the isolate filter.");
                return ElementId.InvalidElementId;
            }
            if (result.Created) created++; else reused++;
            return result.Filter.Id;
        }

        /// <summary>A leaf rule for one token value. "(unset)" becomes a has-value test.</summary>
        private static AecFilterRule TokenLeaf(string paramName, string value, bool negate)
        {
            if (VisibilityTokens.IsUnset(value))
            {
                // "unset" == the parameter has no value. Its negation is "has a value".
                return new AecFilterRule
                {
                    Param = paramName,
                    Kind = "shared",
                    Op = negate ? "hasValue" : "hasNoValue"
                };
            }
            return new AecFilterRule
            {
                Param = paramName,
                Kind = "shared",
                Op = negate ? "notEquals" : "equals",
                Value = value,
                Type = "string"
            };
        }

        private static string DescribeCategories(Document doc, IList<int> catIds)
        {
            var names = new List<string>();
            foreach (int id in catIds.Take(3))
            {
                string n = null;
                try { n = Category.GetCategory(doc, (BuiltInCategory)id)?.Name; } catch { /* optional */ }
                names.Add(n ?? id.ToString());
            }
            string joined = string.Join(", ", names);
            return catIds.Count > 3 ? $"{joined} and {catIds.Count - 3} more" : joined;
        }
    }
}
