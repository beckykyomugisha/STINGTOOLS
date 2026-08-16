// StingTools — Visibility Center · token value harvester
//
// One FilteredElementCollector pass fills all seven token buckets AND the element
// snapshots the planner consumes. Seven separate passes over a 50k-element model is
// the obvious-but-wrong implementation; this reads each element once.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Visibility
{
    /// <summary>
    /// <c>TokenValueTally</c> / <c>CategoryTally</c> / <c>TokenHarvest</c> live in the
    /// Revit-free <c>VisibilityHarvestModel.cs</c> so the category tree and the state
    /// reconciler can be unit-tested. This file is the SCANNER only.
    ///
    /// Scans the document (or the active view — honouring the same project/view scope toggle
    /// the Select commands use) for distinct ISO 19650 token values, with counts.
    /// Cached per scope for 30 seconds, mirroring <see cref="ComplianceScan"/>; the cache is
    /// dropped by <see cref="InvalidateCache"/>, which <c>ComplianceScan.InvalidateCache</c>
    /// chains into so every existing invalidation point covers this too.
    /// </summary>
    public static class TokenValueHarvester
    {
        private static readonly object _cacheLock = new object();
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

        // Keyed by scope rather than holding a single entry. VisibilityStateReader needs the
        // view-scoped AND the document-scoped harvest back to back; a one-slot cache made each
        // call evict the other, so every read re-scanned the whole model twice.
        private static readonly Dictionary<string, Tuple<DateTime, TokenHarvest>> _cache =
            new Dictionary<string, Tuple<DateTime, TokenHarvest>>(StringComparer.Ordinal);

        /// <summary>Drop every cached harvest. Safe to call from any thread.</summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock) { _cache.Clear(); }
        }

        /// <summary>
        /// Harvest token values and element snapshots. Pass <paramref name="view"/> to limit the
        /// scan to that view, or null for the whole project.
        /// </summary>
        public static TokenHarvest Harvest(Document doc, View view)
        {
            if (doc == null) return new TokenHarvest();

            string key = (doc.Title ?? "?") + "|" + (view == null ? "project" : view.Id.ToString());
            lock (_cacheLock)
            {
                Tuple<DateTime, TokenHarvest> hit;
                if (_cache.TryGetValue(key, out hit) && DateTime.Now - hit.Item1 < CacheLifetime)
                    return hit.Item2;
            }

            TokenHarvest harvest = Scan(doc, view);

            lock (_cacheLock)
            {
                _cache[key] = Tuple.Create(DateTime.Now, harvest);
            }
            return harvest;
        }

        private static TokenHarvest Scan(Document doc, View view)
        {
            var harvest = new TokenHarvest();
            var excluded = ExcludedCategoryNames(doc);

            // Token key → parameter name, resolved through ParamRegistry so a project that
            // renames its shared parameters keeps matching. Never hardcode "ASS_ZONE_TXT".
            var paramNames = TokenParameterNames();

            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in VisibilityTokens.All)
                counts[t] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var catTally = new Dictionary<int, CategoryTally>();
            var excludedTally = new Dictionary<int, CategoryTally>();

            var collector = view == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            foreach (var el in collector.WhereElementIsNotElementType())
            {
                if (el == null) continue;

                int catId = 0;
                string catName = null;
                Category cat = null;
                try
                {
                    cat = el.Category;
                    if (cat != null)
                    {
                        catId = (int)cat.Id.Value;
                        catName = cat.Name;
                    }
                }
                catch { /* optional read — some elements have no category */ }

                if (catId == 0) continue;   // no category = not addressable by either mechanism

                // View-management categories (Cameras, Views, Section Boxes, Scope Boxes) are
                // not model content and hiding them is never the intent. They are counted
                // separately rather than dropped, so the harvest log can account for them.
                if (excluded.Count > 0)
                {
                    CategoryTally ex;
                    if (excludedTally.TryGetValue(catId, out ex)) { ex.Count++; continue; }
                    var meta = Describe(cat, catId, catName);
                    if (!string.IsNullOrEmpty(meta.BuiltInName) && excluded.Contains(meta.BuiltInName))
                    {
                        meta.Count = 1;
                        excludedTally[catId] = meta;
                        continue;
                    }
                }

                var snap = new VisibilityElementSnapshot
                {
                    Id = el.Id.Value,
                    CategoryId = catId,
                    CategoryName = catName
                };

                // One pass, seven buckets.
                foreach (var kv in paramNames)
                {
                    string raw = null;
                    try { raw = ParameterHelpers.GetString(el, kv.Value); }
                    catch { /* optional parameter read — absent parameters are normal */ }

                    string value = string.IsNullOrWhiteSpace(raw) ? VisibilityTokens.Unset : raw.Trim();
                    snap.Tokens[kv.Key] = raw;

                    var bucket = counts[kv.Key];
                    int n;
                    bucket[value] = bucket.TryGetValue(value, out n) ? n + 1 : 1;
                }

                harvest.Elements.Add(snap);

                CategoryTally ct;
                if (!catTally.TryGetValue(catId, out ct))
                {
                    // Metadata (parent / group / BuiltInCategory name) is resolved ONCE per
                    // category, not once per element — the Category API calls are not free.
                    ct = Describe(cat, catId, catName);
                    catTally[catId] = ct;
                }
                ct.Count++;
            }

            foreach (var t in VisibilityTokens.All)
            {
                harvest.TokenValues[t] = counts[t]
                    .Select(p => new TokenValueTally { Value = p.Key, Count = p.Value })
                    // Real values first, alphabetically; the synthetic "(unset)" sinks to the bottom.
                    .OrderBy(v => v.IsUnset ? 1 : 0)
                    .ThenBy(v => v.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            harvest.Categories = catTally.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            harvest.ExcludedCategories = excludedTally.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Nothing may be silently dropped — say once per harvest what was left out.
            if (harvest.ExcludedCategories.Count > 0)
                StingLog.Info(
                    $"TokenValueHarvester: excluded {harvest.ExcludedCategories.Count} " +
                    $"view-management categor(y/ies) ({harvest.ExcludedElementCount:N0} elements): " +
                    string.Join(", ", harvest.ExcludedCategories.Select(c => c.Name)));

            return harvest;
        }

        /// <summary>
        /// Read a category's identity once: display name, BuiltInCategory name (the string the
        /// exclusion list is written against), parent id for nesting, and which of Revit's own
        /// V/G tabs it belongs to. Every read is optional — a category that will not answer
        /// still gets a tally, in the Model group, rather than disappearing.
        /// </summary>
        private static CategoryTally Describe(Category cat, int catId, string catName)
        {
            var tally = new CategoryTally
            {
                CategoryId = catId,
                Name = catName ?? catId.ToString(),
                Group = CategoryGroupKind.Model
            };
            if (cat == null) return tally;

            try
            {
                var bic = cat.BuiltInCategory;
                if (bic != BuiltInCategory.INVALID) tally.BuiltInName = bic.ToString();
            }
            catch { /* optional read — import categories are not BuiltInCategory-backed */ }

            Category parent = null;
            try { parent = cat.Parent; }
            catch { /* optional read */ }
            if (parent != null)
            {
                try { tally.ParentCategoryId = (int)parent.Id.Value; }
                catch { /* optional read */ }
            }

            tally.Group = ClassifyGroup(cat, parent);
            return tally;
        }

        /// <summary>
        /// Mirror Revit's own V/G tab split. Imports are detected from the root category being
        /// <c>OST_ImportObjectStyles</c> (a DWG/DXF contributes one category per file, none of
        /// which is BuiltInCategory-backed). Anything unclassifiable lands in Model — never
        /// nowhere.
        /// </summary>
        private static CategoryGroupKind ClassifyGroup(Category cat, Category parent)
        {
            try
            {
                var root = parent ?? cat;
                for (int guard = 0; guard < 8 && root != null; guard++)
                {
                    if ((int)root.Id.Value == (int)BuiltInCategory.OST_ImportObjectStyles)
                        return CategoryGroupKind.Imports;
                    Category next = null;
                    try { next = root.Parent; } catch { break; }
                    if (next == null) break;
                    root = next;
                }

                if (cat.CategoryType == CategoryType.Annotation) return CategoryGroupKind.Annotation;
            }
            catch { /* optional read */ }
            return CategoryGroupKind.Model;
        }

        /// <summary>
        /// BuiltInCategory names to leave out of the category list, from the corporate baseline
        /// with the project override layered on. Never hardcoded here — the list lives in the
        /// same JSON as the presets, so a project can change it through the path that already
        /// exists.
        /// </summary>
        private static HashSet<string> ExcludedCategoryNames(Document doc)
        {
            try
            {
                return new HashSet<string>(
                    VisibilitySession.ExcludedCategories(doc), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TokenValueHarvester.ExcludedCategoryNames: {ex.Message}");
                return new HashSet<string>(
                    VisibilityCategoryTreeBuilder.DefaultExclusions, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Token key → the live parameter name from <see cref="ParamRegistry"/>.</summary>
        public static Dictionary<string, string> TokenParameterNames()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { VisibilityTokens.Disc, ParamRegistry.DISC },
                { VisibilityTokens.Loc,  ParamRegistry.LOC  },
                { VisibilityTokens.Zone, ParamRegistry.ZONE },
                { VisibilityTokens.Lvl,  ParamRegistry.LVL  },
                { VisibilityTokens.Sys,  ParamRegistry.SYS  },
                { VisibilityTokens.Func, ParamRegistry.FUNC },
                { VisibilityTokens.Prod, ParamRegistry.PROD },
            };
        }

        /// <summary>The parameter name backing one token key, or null when unknown.</summary>
        public static string ParameterNameFor(string tokenKey)
        {
            string name;
            return TokenParameterNames().TryGetValue(tokenKey ?? string.Empty, out name) ? name : null;
        }
    }
}
