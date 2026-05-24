using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Scale-to-offset and scale-to-text-size mapping used by
    /// <c>SmartTagPlacementCommand.GetModelOffset</c> and
    /// <c>SetScaleAwareTagSizeCommand</c>.
    ///
    /// Resolution order (first win): project_config.json key "SCALE_TIERS" →
    /// Data/SCALE_TIERS.json → hardcoded fallback (2/5/8/12/20 mm, 30 ft cap,
    /// 3.5/3/2.5/2/2 mm text). The loader caches after the first read; call
    /// <see cref="Reload"/> to pick up slider-driven project overrides.
    /// </summary>
    public static class ScaleTiers
    {
        public sealed class Tier
        {
            public int MaxDenominator { get; set; }
            public string Label { get; set; } = "";
            public double OffsetMm { get; set; }
            public string TextSizeMm { get; set; } = "2.5";
        }

        private static readonly object _lock = new object();
        private static List<Tier> _cached;
        private static double _cachedCapFt = 30.0;
        private static string _cachedSource = "";
        // Phase 165 — per-category scale multipliers loaded alongside tiers.
        // Keys are normalised to ToUpperInvariant for case-insensitive lookup.
        private static Dictionary<string, double> _cachedMultipliers
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ordered tiers (ascending by <see cref="Tier.MaxDenominator"/>).</summary>
        public static IReadOnlyList<Tier> Current
        {
            get { EnsureLoaded(null); return _cached; }
        }

        /// <summary>Max model-space offset in feet; clamp applied by <c>GetModelOffset</c>.</summary>
        public static double OffsetCapFt
        {
            get { EnsureLoaded(null); return _cachedCapFt; }
        }

        /// <summary>Where the active tier table came from (diagnostic string).</summary>
        public static string Source
        {
            get { EnsureLoaded(null); return _cachedSource; }
        }

        /// <summary>
        /// Phase 165 — per-category multiplier lookup. Returns 1.0 for missing
        /// keys so callers can multiply unconditionally. Known keys: "DUCTS",
        /// "PIPES", "EQUIPMENT", "FIXTURES" (matching the Tag Studio Scale tab).
        /// </summary>
        public static double GetCategoryMultiplier(string categoryKey)
        {
            EnsureLoaded(null);
            if (string.IsNullOrEmpty(categoryKey)) return 1.0;
            return _cachedMultipliers.TryGetValue(categoryKey, out double m) && m > 0 ? m : 1.0;
        }

        /// <summary>Phase 165 — full multiplier map (read-only snapshot).</summary>
        public static IReadOnlyDictionary<string, double> CategoryMultipliers
        {
            get { EnsureLoaded(null); return _cachedMultipliers; }
        }

        /// <summary>
        /// Pick the tier whose <see cref="Tier.MaxDenominator"/> first meets or
        /// exceeds <paramref name="view"/>.Scale. Falls back to the last tier
        /// when the view scale is larger than every defined max.
        /// </summary>
        public static Tier ForView(View view)
        {
            EnsureLoaded(view?.Document);
            int scale = (view != null && view.Scale > 0) ? view.Scale : 100;
            foreach (Tier t in _cached)
                if (scale <= t.MaxDenominator) return t;
            return _cached[_cached.Count - 1];
        }

        /// <summary>Force a re-read on the next access — call after slider persistence.</summary>
        public static void Reload(Document doc = null)
        {
            lock (_lock) { _cached = null; }
            EnsureLoaded(doc);
        }

        /// <summary>
        /// Persist a fresh set of tiers into the project's project_config.json
        /// under the "SCALE_TIERS" key. Subsequent <see cref="ForView"/> calls
        /// on this document read the override. Returns the file path written,
        /// or null if the document is unsaved.
        /// </summary>
        public static string SaveProjectOverride(Document doc,
            IList<Tier> tiers, double offsetCapFt)
            => SaveProjectOverride(doc, tiers, offsetCapFt, null);

        /// <summary>
        /// Phase 165 — overload that also persists the per-category multiplier
        /// map under "SCALE_CATEGORY_MULTIPLIERS". Pass null/empty to leave the
        /// existing multiplier block untouched.
        /// </summary>
        public static string SaveProjectOverride(Document doc,
            IList<Tier> tiers, double offsetCapFt,
            IDictionary<string, double> categoryMultipliers)
        {
            if (doc == null || tiers == null || tiers.Count == 0) return null;
            string cfgPath = ProjectConfigPath(doc);
            if (string.IsNullOrEmpty(cfgPath)) return null;

            JObject jo = File.Exists(cfgPath)
                ? JObject.Parse(File.ReadAllText(cfgPath))
                : new JObject();

            var block = new JObject
            {
                ["offset_cap_ft"] = offsetCapFt,
                ["tiers"] = new JArray(tiers.Select(t => new JObject
                {
                    ["max_denominator"] = t.MaxDenominator,
                    ["label"]           = t.Label ?? "",
                    ["offset_mm"]       = t.OffsetMm,
                    ["text_size_mm"]    = t.TextSizeMm ?? "2.5",
                })),
            };
            jo["SCALE_TIERS"] = block;

            if (categoryMultipliers != null && categoryMultipliers.Count > 0)
            {
                var multBlock = new JObject();
                foreach (var kv in categoryMultipliers)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                    multBlock[kv.Key.ToUpperInvariant()] = kv.Value;
                }
                jo["SCALE_CATEGORY_MULTIPLIERS"] = multBlock;
            }

            File.WriteAllText(cfgPath, jo.ToString(Newtonsoft.Json.Formatting.Indented));

            Reload(doc);
            return cfgPath;
        }

        private static void EnsureLoaded(Document doc)
        {
            lock (_lock)
            {
                if (_cached != null) return;

                // Reset multipliers each time tiers reload.
                _cachedMultipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                if (TryLoadFromProjectConfig(doc, out var tiers, out double cap))
                {
                    _cached = tiers; _cachedCapFt = cap; _cachedSource = "project_config.json";
                    LoadMultipliersFromProjectConfig(doc);
                    return;
                }
                if (TryLoadFromDataFile(out tiers, out cap))
                {
                    _cached = tiers; _cachedCapFt = cap; _cachedSource = "SCALE_TIERS.json";
                    return;
                }
                _cached = HardcodedFallback();
                _cachedCapFt = 30.0;
                _cachedSource = "hardcoded";
            }
        }

        // Phase 165 — read SCALE_CATEGORY_MULTIPLIERS map from project_config.json.
        // Best-effort: missing block leaves the cache empty (== all 1.0×).
        private static void LoadMultipliersFromProjectConfig(Document doc)
        {
            string path = ProjectConfigPath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                var block = jo["SCALE_CATEGORY_MULTIPLIERS"] as JObject;
                if (block == null) return;
                foreach (var prop in block.Properties())
                {
                    double v = (double?)prop.Value ?? 0.0;
                    if (v > 0) _cachedMultipliers[prop.Name.ToUpperInvariant()] = v;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScaleTiers: SCALE_CATEGORY_MULTIPLIERS read failed — {ex.Message}");
            }
        }

        private static bool TryLoadFromProjectConfig(Document doc,
            out List<Tier> tiers, out double cap)
        {
            tiers = null; cap = 30.0;
            string path = ProjectConfigPath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                var block = jo["SCALE_TIERS"] as JObject;
                if (block == null) return false;
                tiers = ParseTiers(block["tiers"] as JArray);
                if (tiers == null || tiers.Count == 0) { tiers = null; return false; }
                cap = (double?)block["offset_cap_ft"] ?? 30.0;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScaleTiers: project_config read failed — {ex.Message}");
                return false;
            }
        }

        private static bool TryLoadFromDataFile(out List<Tier> tiers, out double cap)
        {
            tiers = null; cap = 30.0;
            string path = StingToolsApp.FindDataFile("SCALE_TIERS.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                tiers = ParseTiers(jo["tiers"] as JArray);
                if (tiers == null || tiers.Count == 0) { tiers = null; return false; }
                cap = (double?)jo["offset_cap_ft"] ?? 30.0;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScaleTiers: SCALE_TIERS.json read failed — {ex.Message}");
                return false;
            }
        }

        private static List<Tier> ParseTiers(JArray arr)
        {
            if (arr == null) return null;
            var list = new List<Tier>();
            foreach (var t in arr)
            {
                int max = (int?)t["max_denominator"] ?? 0;
                double off = (double?)t["offset_mm"] ?? 0;
                if (max <= 0 || off <= 0) continue;
                list.Add(new Tier
                {
                    MaxDenominator = max,
                    Label       = (string)t["label"] ?? "",
                    OffsetMm    = off,
                    TextSizeMm  = (string)t["text_size_mm"] ?? "2.5",
                });
            }
            list.Sort((a, b) => a.MaxDenominator.CompareTo(b.MaxDenominator));
            return list;
        }

        private static List<Tier> HardcodedFallback() => new List<Tier>
        {
            new Tier { MaxDenominator = 50,         Label = "1:1–1:50",    OffsetMm = 2.0,  TextSizeMm = "3.5" },
            new Tier { MaxDenominator = 100,        Label = "1:50–1:100",  OffsetMm = 5.0,  TextSizeMm = "3"   },
            new Tier { MaxDenominator = 200,        Label = "1:100–1:200", OffsetMm = 8.0,  TextSizeMm = "2.5" },
            new Tier { MaxDenominator = 500,        Label = "1:200–1:500", OffsetMm = 12.0, TextSizeMm = "2"   },
            new Tier { MaxDenominator = int.MaxValue, Label = "1:500+",    OffsetMm = 20.0, TextSizeMm = "2"   },
        };

        private static string ProjectConfigPath(Document doc)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "project_config.json");
            }
            catch { return null; }
        }
    }
}
