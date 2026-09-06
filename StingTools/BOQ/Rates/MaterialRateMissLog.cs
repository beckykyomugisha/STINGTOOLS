// ══════════════════════════════════════════════════════════════════════════
//  MaterialRateMissLog.cs — E-6. Make a material-rate miss visible.
//
//  MaterialLibraryRateProvider (priority 95) returns null when the element's
//  material has no price, and the chain falls through to CsvRateProvider (90),
//  which is keyed on CATEGORY. So "Walls" gets one figure whether it is 200 mm
//  hollow block or a glazed screen — and nothing reaches the bill to say so.
//  The only trace was a provenance string on the row.
//
//  This is the same fix shape as H-1: report what actually happened, not what
//  was attempted. A rate that resolved by falling back is not the same fact as
//  a rate that resolved on the material, and only one of them is a price.
//
//  Scope is deliberately one BuildBOQDocument pass: BOQCostManager.Reset()s the
//  log at the start of every build, so a reader can never be shown a tally
//  accumulated across two different bills. An empty log after a build means
//  "no misses", and an empty log with zero attempts means "the provider never
//  ran" — the two are distinguished, because conflating them is exactly how an
//  unrun gate comes to be read as a clean one.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.BOQ.Rates
{
    /// <summary>One material that failed to price, with how often and where.</summary>
    public sealed class MaterialRateMiss
    {
        public string Material { get; set; } = "";
        public int Rows { get; set; }
        /// <summary>Distinct categories the miss was seen on — a material missing
        /// across many categories is a library gap; one category is often a typo.</summary>
        public HashSet<string> Categories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class MaterialRateMissLog
    {
        private static readonly ConcurrentDictionary<string, MaterialRateMiss> _misses =
            new ConcurrentDictionary<string, MaterialRateMiss>(StringComparer.OrdinalIgnoreCase);

        private static int _attempts;
        private static int _hits;
        private static int _noMaterial;

        /// <summary>Element had a material name, but no rate anywhere in the library.</summary>
        public const string NoMaterialKey = "(no material resolved)";

        public static void Reset()
        {
            _misses.Clear();
            _attempts = 0;
            _hits = 0;
            _noMaterial = 0;
        }

        public static void RecordAttempt() => System.Threading.Interlocked.Increment(ref _attempts);

        public static void RecordHit() => System.Threading.Interlocked.Increment(ref _hits);

        /// <summary>
        /// The element resolved no material at all — a different fact from "this
        /// material has no price", and counted separately so the report does not
        /// blame the rate library for a modelling gap.
        /// </summary>
        public static void RecordNoMaterial(string category)
        {
            System.Threading.Interlocked.Increment(ref _noMaterial);
            Add(NoMaterialKey, category);
        }

        /// <summary>The material is named but carries no price in any tier.</summary>
        public static void RecordMiss(string material, string category)
        {
            if (string.IsNullOrWhiteSpace(material)) { RecordNoMaterial(category); return; }
            Add(material.Trim(), category);
        }

        private static void Add(string key, string category)
        {
            var rec = _misses.GetOrAdd(key, k => new MaterialRateMiss { Material = k });
            lock (rec)
            {
                rec.Rows++;
                if (!string.IsNullOrWhiteSpace(category)) rec.Categories.Add(category.Trim());
            }
        }

        public static int Attempts => _attempts;
        public static int Hits => _hits;
        public static int NoMaterialRows => _noMaterial;

        /// <summary>Rows that fell through to a category-keyed rate, all causes.</summary>
        public static int MissRows => _misses.Values.Sum(m => m.Rows);

        /// <summary>Distinct materials that failed to price (excludes the no-material bucket).</summary>
        public static int DistinctMaterials =>
            _misses.Keys.Count(k => !string.Equals(k, NoMaterialKey, StringComparison.Ordinal));

        /// <summary>Worst offenders first, by row count.</summary>
        public static IReadOnlyList<MaterialRateMiss> Top(int n) =>
            _misses.Values.OrderByDescending(m => m.Rows)
                          .ThenBy(m => m.Material, StringComparer.OrdinalIgnoreCase)
                          .Take(n).ToList();

        public static IReadOnlyList<MaterialRateMiss> All() =>
            _misses.Values.OrderByDescending(m => m.Rows)
                          .ThenBy(m => m.Material, StringComparer.OrdinalIgnoreCase)
                          .ToList();
    }
}
