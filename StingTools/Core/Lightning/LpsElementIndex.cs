// LpsElementIndex.cs — Wave C #7.
//
// Single-pass cache of LPS-classified family instances in a Document,
// keyed by canonical element-type tag (AIR_TERMINAL / DOWN_CONDUCTOR /
// EARTH_ELECTRODE / BONDING_BAR / SPD / TEST_CLAMP).
//
// Before this cache, every grid loader on the panel called
// LpsEngine.CollectLpsFamily(doc, ...) which walks OST_ElectricalEquipment
// + OST_GenericModel twice (pass 1 by ELC_LPS_ELEMENT_TYPE_TXT param,
// pass 2 by family-name substring). With 7 grids each calling it
// independently → 14 full sweeps per Load-Model click, ~6 s on a 5k-
// element project.
//
// LpsElementIndex.Get(doc) builds the index ONCE per document with a
// single FilteredElementCollector pass, returns the cached version
// thereafter. Invalidated on DocumentChanged (any LPS BIC element add/
// remove/modify) or explicit Invalidate(doc) call.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public static class LpsElementIndex
    {
        // Six canonical type buckets — matches the ResolveLpsFunc keys.
        public const string AT   = "AIR_TERMINAL";
        public const string DC   = "DOWN_CONDUCTOR";
        public const string EE   = "EARTH_ELECTRODE";
        public const string BOND = "BONDING_BAR";
        public const string SPD  = "SPD";
        public const string TC   = "TEST_CLAMP";

        public class Index
        {
            public Dictionary<string, List<FamilyInstance>> ByType { get; }
                = new Dictionary<string, List<FamilyInstance>>(StringComparer.OrdinalIgnoreCase);
            public DateTime BuiltUtc { get; set; }
            public int TotalElements => ByType.Values.Sum(v => v.Count);
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Index> _cache
            = new Dictionary<string, Index>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);

        public static Index Get(Document doc)
        {
            if (doc == null) return new Index();
            string key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existing) &&
                    (DateTime.UtcNow - existing.BuiltUtc) < _ttl)
                    return existing;
            }
            var idx = Build(doc);
            lock (_lock) { _cache[key] = idx; }
            return idx;
        }

        public static void Invalidate(Document doc)
        {
            if (doc == null) return;
            string key = DocKey(doc);
            lock (_lock) { _cache.Remove(key); }
        }

        public static void InvalidateAll()
        {
            lock (_lock) { _cache.Clear(); }
        }

        // ── Convenience accessors that mimic CollectLpsFamily shape ────

        public static List<FamilyInstance> ByType_(Document doc, string type)
        {
            var idx = Get(doc);
            return idx.ByType.TryGetValue(type, out var list) ? list : new List<FamilyInstance>();
        }

        public static List<FamilyInstance> AirTerminals(Document doc)   => ByType_(doc, AT);
        public static List<FamilyInstance> DownConductors(Document doc) => ByType_(doc, DC);
        public static List<FamilyInstance> EarthElectrodes(Document doc) => ByType_(doc, EE);
        public static List<FamilyInstance> BondingBars(Document doc)    => ByType_(doc, BOND);
        public static List<FamilyInstance> Spds(Document doc)           => ByType_(doc, SPD);
        public static List<FamilyInstance> TestClamps(Document doc)     => ByType_(doc, TC);

        // ── Build ──────────────────────────────────────────────────────

        private static Index Build(Document doc)
        {
            var idx = new Index { BuiltUtc = DateTime.UtcNow };
            foreach (var t in new[] { AT, DC, EE, BOND, SPD, TC })
                idx.ByType[t] = new List<FamilyInstance>();

            // Single sweep across the 2 BICs that host LPS families.
            BuiltInCategory[] cats = {
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_GenericModel
            };
            foreach (var bic in cats)
            {
                try
                {
                    var coll = new FilteredElementCollector(doc).OfCategory(bic)
                        .WhereElementIsNotElementType().Cast<FamilyInstance>();
                    foreach (var fi in coll)
                    {
                        string bucket = Classify(fi);
                        if (bucket == null) continue;
                        idx.ByType[bucket].Add(fi);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"LpsElementIndex.Build {bic}: {ex.Message}"); }
            }
            return idx;
        }

        /// <summary>
        /// Two-tier classification:
        ///   Tier 1 — exact ELC_LPS_ELEMENT_TYPE_TXT match (param wins)
        ///   Tier 2 — family / type name substring (fallback)
        /// Returns null when no LPS pattern matches.
        /// </summary>
        private static string Classify(FamilyInstance fi)
        {
            try
            {
                if (fi == null) return null;
                string elementType = ParameterHelpers.GetString(fi, "ELC_LPS_ELEMENT_TYPE_TXT");
                if (!string.IsNullOrWhiteSpace(elementType))
                {
                    string t = elementType.Trim().ToUpperInvariant();
                    if (t == AT || t == DC || t == EE || t == BOND || t == SPD || t == TC) return t;
                }

                string fam = fi.Symbol?.FamilyName ?? "";
                string sym = fi.Symbol?.Name ?? "";
                string upper = ($"{fam} {sym}").ToUpperInvariant();

                if (upper.Contains("AIR TERMINAL") || upper.Contains("AIR_TERMINAL") ||
                    upper.Contains("FRANKLIN") || upper.Contains("FINIAL") ||
                    upper.Contains("AIR ROD") || upper.Contains("STRIKE TERMINATION") ||
                    upper.Contains("CATENARY"))
                    return AT;
                if (upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWN_CONDUCTOR") ||
                    upper.Contains("DOWNCOND") || upper.Contains("DESCENT"))
                    return DC;
                if (upper.Contains("EARTH ROD") || upper.Contains("GROUND ROD") ||
                    upper.Contains("GROUNDROD") || upper.Contains("EARTH ELECTRODE") ||
                    upper.Contains("EARTH PLATE") || upper.Contains("RING EARTH") ||
                    upper.Contains("FOUNDATION EARTH"))
                    return EE;
                if (upper.Contains("BONDING") || upper.Contains("BOND BAR") ||
                    upper.Contains("BONDINGBAR") || upper.Contains("EARTH BAR") ||
                    upper.Contains("MAIN EARTH BAR") || upper.Contains("MEB") ||
                    upper.Contains("EQUIPOTENTIAL"))
                    return BOND;
                if (upper.Contains("SPD") || upper.Contains("SURGE PROTECT") ||
                    upper.Contains("SURGE-PROTECT"))
                    return SPD;
                if (upper.Contains("TEST CLAMP") || upper.Contains("INSPECTION POINT"))
                    return TC;
            }
            catch (Exception ex) { StingLog.Warn($"LpsElementIndex.Classify: {ex.Message}"); }
            return null;
        }

        private static string DocKey(Document doc)
        {
            try { return doc.PathName ?? doc.Title ?? Guid.NewGuid().ToString(); }
            catch { return Guid.NewGuid().ToString(); }
        }
    }
}
