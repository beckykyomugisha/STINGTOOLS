// ============================================================================
// TagStyleCatalogue.cs — Single source of truth for tag style dimensions.
//
// Loaded from Data/tag_style_catalogue.json at first use. Every piece of code
// that enumerates sizes, styles, colours, arrowheads, depth tiers, or
// disciplinary defaults MUST read from this class rather than maintaining its
// own array — this keeps the UI, family creator, migration command,
// placement path, and style audit in lock-step.
//
// Variant strategy
// ----------------
// The canonical size × style × colour × arrowhead × depth-tier space is:
//
//   4 sizes × 4 styles × 8 colours × 8 arrowheads × 10 depth tiers  =  10 240
//
// Pre-materialising 10 000+ Revit family types per tag family is neither
// practical nor desirable.  The catalogue therefore exposes a curated set of
// "standard variants" (EnumerateStandardVariants) that covers:
//
//   1. The eight disciplinary defaults (defaults_per_discipline)
//   2. A compact black baseline for every size (depth tiers 1, 2, 3)
//   3. A small hand-picked set of common combos (see tag_style_catalogue.json)
//
// MigrateTagFamiliesCommand pre-creates these variants.  Any other combination
// encountered by TagStyleEngine.FindTypeVariant at placement time is created
// on-demand (also via MigrateTagFamiliesCommand's variant writer).  This gives
// a ~20-type warm set per tag family out of the box and grows the catalogue
// lazily as users pick exotic combinations.
//
// If a requested variant cannot be created — usually because the arrowhead
// name is absent from the project's OST_ArrowHeads element types — the
// migration logs a warning and the placement path falls back to the base type.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core
{
    /// <summary>Disciplinary default style preset (e.g. M = 2.5/BOLD/BLUE/Filled30/T3).</summary>
    public class DisciplineDefault
    {
        public string Disc { get; set; } = "";
        public string Size { get; set; } = "2.5";
        public string Style { get; set; } = "NOM";
        public string Colour { get; set; } = "BLACK";
        public string Arrowhead { get; set; } = "None";
        public int DepthTier { get; set; } = 3;

        public TypeVariantSpec ToVariantSpec() => new TypeVariantSpec
        {
            Size = Size, Style = Style, Colour = Colour,
            Arrowhead = Arrowhead, DepthTier = DepthTier,
        };
    }

    /// <summary>
    /// A tag family type variant: the 5-tuple (size, style, colour, arrowhead, depth_tier)
    /// that uniquely identifies a Revit FamilySymbol inside a STING tag family.
    /// </summary>
    public class TypeVariantSpec : IEquatable<TypeVariantSpec>
    {
        public string Size { get; set; } = "2.5";
        public string Style { get; set; } = "NOM";
        public string Colour { get; set; } = "BLACK";
        public string Arrowhead { get; set; } = "None";
        public int DepthTier { get; set; } = 3;

        /// <summary>
        /// Canonical type name used in Revit. Example: "2.5_BOLD_RED_Filled30_T3".
        /// Arrowhead name is sanitised: spaces removed, "Arrow " prefix stripped.
        /// </summary>
        public string CanonicalTypeName
        {
            get
            {
                string arrowShort = ShortArrowName(Arrowhead);
                return $"{Size}_{Style}_{Colour}_{arrowShort}_T{DepthTier}";
            }
        }

        /// <summary>
        /// Shorten an arrowhead display name for use in type names.
        /// "Arrow Filled 30" → "Filled30"; "Dot Filled" → "DotFilled"; "None" → "None".
        /// </summary>
        public static string ShortArrowName(string arrowhead)
        {
            if (string.IsNullOrEmpty(arrowhead)) return "None";
            string s = arrowhead.Trim();
            if (s.StartsWith("Arrow ", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(6);
            // Strip spaces, keep digits and letters
            return new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        public override string ToString() => CanonicalTypeName;

        public bool Equals(TypeVariantSpec other)
        {
            if (other == null) return false;
            return string.Equals(Size, other.Size, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Style, other.Style, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Colour, other.Colour, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Arrowhead, other.Arrowhead, StringComparison.OrdinalIgnoreCase)
                && DepthTier == other.DepthTier;
        }

        public override bool Equals(object obj) => Equals(obj as TypeVariantSpec);

        public override int GetHashCode() => CanonicalTypeName.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Static reader for Data/tag_style_catalogue.json. Lazy-loads on first access.
    /// </summary>
    public static class TagStyleCatalogue
    {
        private static readonly object _lock = new object();
        // volatile ensures the DCL first-check outside the lock sees the write
        // from whichever thread completed EnsureLoaded() without a memory-barrier gap.
        private static volatile bool _loaded;

        // Backing fields typed as IReadOnlyList so even a cast attempt at the
        // call site gets a compile-time warning rather than silent mutability.
        private static IReadOnlyList<string> _sizes = new[] { "2", "2.5", "3", "3.5" };
        private static IReadOnlyList<string> _styles = new[] { "NOM", "BOLD", "ITALIC", "BOLDITALIC" };
        private static IReadOnlyList<string> _colours = new[] { "BLACK", "BLUE", "GREEN", "RED", "ORANGE", "PURPLE", "GREY", "WHITE" };
        private static IReadOnlyList<string> _arrowheads = new[] { "None", "Arrow Filled 15", "Arrow Filled 30", "Arrow Open 30", "Dot Filled", "Tick", "Heavy End" };
        private static IReadOnlyList<int> _depthTiers = Enumerable.Range(1, 10).ToArray();
        private static Dictionary<string, DisciplineDefault> _defaults =
            new Dictionary<string, DisciplineDefault>(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<TypeVariantSpec> _standardVariants = Array.Empty<TypeVariantSpec>();

        public static IReadOnlyList<string> Sizes      { get { EnsureLoaded(); return _sizes; } }
        public static IReadOnlyList<string> Styles     { get { EnsureLoaded(); return _styles; } }
        public static IReadOnlyList<string> Colours    { get { EnsureLoaded(); return _colours; } }
        public static IReadOnlyList<string> Arrowheads { get { EnsureLoaded(); return _arrowheads; } }
        public static IReadOnlyList<int>    DepthTiers { get { EnsureLoaded(); return _depthTiers; } }

        /// <summary>
        /// Disciplinary default for the given discipline code (M/E/P/A/S/FP/LV/G).
        /// Returns a sensible default for unmapped codes rather than null.
        /// </summary>
        public static DisciplineDefault GetDisciplineDefault(string disc)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(disc) && _defaults.TryGetValue(disc, out var dd))
                return dd;
            return new DisciplineDefault
            {
                Disc = disc ?? "G",
                Size = "2.5", Style = "NOM", Colour = "BLACK",
                Arrowhead = "None", DepthTier = 2,
            };
        }

        /// <summary>
        /// Enumerate the curated standard variants that MigrateTagFamiliesCommand
        /// creates up front. Additional variants are created on-demand during
        /// placement (see TagStyleEngine.FindTypeVariant).
        /// </summary>
        public static IEnumerable<TypeVariantSpec> EnumerateStandardVariants()
        {
            EnsureLoaded();
            return _standardVariants;
        }

        /// <summary>Force a reload from disk (for live-editing workflows).</summary>
        public static void Reload()
        {
            lock (_lock) { _loaded = false; }
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try { LoadFromDisk(); }
                catch (Exception ex) { StingLog.Warn($"TagStyleCatalogue: falling back to built-in defaults — {ex.Message}"); BuildBuiltInDefaults(); }
                _loaded = true;
            }
        }

        private static void LoadFromDisk()
        {
            string path = StingToolsApp.FindDataFile("tag_style_catalogue.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Info("TagStyleCatalogue: tag_style_catalogue.json not found — using built-in defaults");
                BuildBuiltInDefaults();
                return;
            }

            string json = File.ReadAllText(path);
            JObject root = JObject.Parse(json);

            _sizes = ReadStringArray(root, "sizes", _sizes);
            _styles = ReadStringArray(root, "styles", _styles);
            _colours = ReadStringArray(root, "colours", _colours);
            _arrowheads = ReadStringArray(root, "arrowheads", _arrowheads);

            var depthArr = root["depth_tiers"] as JArray;
            if (depthArr != null)
            {
                var list = new List<int>();
                foreach (var t in depthArr)
                {
                    if (int.TryParse(t.ToString(), out int i) && i >= 1 && i <= 10)
                        list.Add(i);
                }
                if (list.Count > 0) _depthTiers = list;
            }

            _defaults = new Dictionary<string, DisciplineDefault>(StringComparer.OrdinalIgnoreCase);
            var defObj = root["defaults_per_discipline"] as JObject;
            if (defObj != null)
            {
                foreach (var prop in defObj.Properties())
                {
                    var d = prop.Value as JObject;
                    if (d == null) continue;
                    _defaults[prop.Name] = new DisciplineDefault
                    {
                        Disc = prop.Name,
                        Size = d["size"]?.ToString() ?? "2.5",
                        Style = d["style"]?.ToString() ?? "NOM",
                        Colour = d["colour"]?.ToString() ?? "BLACK",
                        Arrowhead = d["arrowhead"]?.ToString() ?? "None",
                        DepthTier = d["depth_tier"]?.Value<int>() ?? 3,
                    };
                }
            }

            var variants = new List<TypeVariantSpec>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Always include disciplinary defaults as standard variants.
            foreach (var dd in _defaults.Values)
            {
                var spec = dd.ToVariantSpec();
                if (seen.Add(spec.CanonicalTypeName))
                    variants.Add(spec);
            }

            var stdArr = root["standard_variants"] as JArray;
            if (stdArr != null)
            {
                foreach (JObject v in stdArr.OfType<JObject>())
                {
                    var spec = new TypeVariantSpec
                    {
                        Size = v["size"]?.ToString() ?? "2.5",
                        Style = v["style"]?.ToString() ?? "NOM",
                        Colour = v["colour"]?.ToString() ?? "BLACK",
                        Arrowhead = v["arrowhead"]?.ToString() ?? "None",
                        DepthTier = v["depth_tier"]?.Value<int>() ?? 3,
                    };
                    if (seen.Add(spec.CanonicalTypeName))
                        variants.Add(spec);
                }
            }

            if (variants.Count == 0) BuildBuiltInStandardVariants(seen, variants);
            _standardVariants = variants;

            StingLog.Info($"TagStyleCatalogue: loaded {_sizes.Count} sizes, {_styles.Count} styles, " +
                $"{_colours.Count} colours, {_arrowheads.Count} arrowheads, " +
                $"{_defaults.Count} disc defaults, {_standardVariants.Count} standard variants");
        }

        private static IReadOnlyList<string> ReadStringArray(JObject root, string name, IReadOnlyList<string> fallback)
        {
            var arr = root[name] as JArray;
            if (arr == null) return fallback;
            var list = arr.Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return list.Length > 0 ? list : fallback;
        }

        private static void BuildBuiltInDefaults()
        {
            _defaults = new Dictionary<string, DisciplineDefault>(StringComparer.OrdinalIgnoreCase)
            {
                ["M"]  = new DisciplineDefault { Disc = "M",  Size = "2.5", Style = "BOLD",   Colour = "BLUE",   Arrowhead = "Arrow Filled 30", DepthTier = 3 },
                ["E"]  = new DisciplineDefault { Disc = "E",  Size = "2.5", Style = "BOLD",   Colour = "ORANGE", Arrowhead = "Arrow Filled 30", DepthTier = 3 },
                ["P"]  = new DisciplineDefault { Disc = "P",  Size = "2.5", Style = "BOLD",   Colour = "GREEN",  Arrowhead = "Arrow Filled 30", DepthTier = 3 },
                ["A"]  = new DisciplineDefault { Disc = "A",  Size = "2.5", Style = "NOM",    Colour = "BLACK",  Arrowhead = "Arrow Open 30",   DepthTier = 2 },
                ["S"]  = new DisciplineDefault { Disc = "S",  Size = "2.5", Style = "BOLD",   Colour = "RED",    Arrowhead = "Arrow Filled 30", DepthTier = 3 },
                ["FP"] = new DisciplineDefault { Disc = "FP", Size = "2.5", Style = "BOLD",   Colour = "RED",    Arrowhead = "Arrow Filled 30", DepthTier = 3 },
                ["LV"] = new DisciplineDefault { Disc = "LV", Size = "2",   Style = "ITALIC", Colour = "PURPLE", Arrowhead = "Dot Filled",      DepthTier = 2 },
                ["G"]  = new DisciplineDefault { Disc = "G",  Size = "2",   Style = "NOM",    Colour = "BLACK",  Arrowhead = "None",            DepthTier = 1 },
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var variants = new List<TypeVariantSpec>();
            foreach (var dd in _defaults.Values)
            {
                var v = dd.ToVariantSpec();
                if (seen.Add(v.CanonicalTypeName)) variants.Add(v);
            }
            BuildBuiltInStandardVariants(seen, variants);
            _standardVariants = variants;
        }

        private static void BuildBuiltInStandardVariants(HashSet<string> seen, List<TypeVariantSpec> variants)
        {
            foreach (string size in _sizes)
            {
                for (int t = 1; t <= 3; t++)
                {
                    var v = new TypeVariantSpec
                    {
                        Size = size, Style = "NOM", Colour = "BLACK",
                        Arrowhead = "None", DepthTier = t,
                    };
                    if (seen.Add(v.CanonicalTypeName))
                        variants.Add(v);
                }
            }
        }
    }
}
