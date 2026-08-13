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
    /// <summary>A distinct token value and how many elements carry it — renders as "Z02 (147)".</summary>
    public sealed class TokenValueTally
    {
        public string Value { get; set; }
        public int Count { get; set; }
        public bool IsUnset => VisibilityTokens.IsUnset(Value);
        public string Display => $"{Value} ({Count:N0})";
    }

    /// <summary>A distinct category present in scope.</summary>
    public sealed class CategoryTally
    {
        public int CategoryId { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
        public string Display => $"{Name} ({Count:N0})";
    }

    /// <summary>Everything one scan pass produced.</summary>
    public sealed class TokenHarvest
    {
        /// <summary>Per-element snapshots — the planner's input.</summary>
        public List<VisibilityElementSnapshot> Elements { get; set; }
            = new List<VisibilityElementSnapshot>();

        /// <summary>Token key → distinct values, ordered, with counts.</summary>
        public Dictionary<string, List<TokenValueTally>> TokenValues { get; set; }
            = new Dictionary<string, List<TokenValueTally>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Categories present in scope, ordered by display name.</summary>
        public List<CategoryTally> Categories { get; set; } = new List<CategoryTally>();

        public int TotalElements => Elements == null ? 0 : Elements.Count;

        public List<TokenValueTally> ValuesFor(string tokenKey)
        {
            List<TokenValueTally> v;
            return TokenValues != null && TokenValues.TryGetValue(tokenKey ?? string.Empty, out v)
                ? v : new List<TokenValueTally>();
        }
    }

    /// <summary>
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
        private static TokenHarvest _cached;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static string _cacheKey;

        /// <summary>Drop the cached harvest. Safe to call from any thread.</summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cached = null;
                _cacheTime = DateTime.MinValue;
                _cacheKey = null;
            }
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
                if (_cached != null && string.Equals(_cacheKey, key, StringComparison.Ordinal) &&
                    DateTime.Now - _cacheTime < CacheLifetime)
                    return _cached;
            }

            TokenHarvest harvest = Scan(doc, view);

            lock (_cacheLock)
            {
                _cached = harvest;
                _cacheTime = DateTime.Now;
                _cacheKey = key;
            }
            return harvest;
        }

        private static TokenHarvest Scan(Document doc, View view)
        {
            var harvest = new TokenHarvest();

            // Token key → parameter name, resolved through ParamRegistry so a project that
            // renames its shared parameters keeps matching. Never hardcode "ASS_ZONE_TXT".
            var paramNames = TokenParameterNames();

            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in VisibilityTokens.All)
                counts[t] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var catTally = new Dictionary<int, CategoryTally>();

            var collector = view == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            foreach (var el in collector.WhereElementIsNotElementType())
            {
                if (el == null) continue;

                int catId = 0;
                string catName = null;
                try
                {
                    var cat = el.Category;
                    if (cat != null)
                    {
                        catId = (int)cat.Id.Value;
                        catName = cat.Name;
                    }
                }
                catch { /* optional read — some elements have no category */ }

                if (catId == 0) continue;   // no category = not addressable by either mechanism

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
                    ct = new CategoryTally { CategoryId = catId, Name = catName ?? catId.ToString() };
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

            return harvest;
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
