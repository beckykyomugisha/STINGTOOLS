// StingTools — Symbol line-weight registry
//
// Data-driven subcategory -> projection line weight resolution for symbol curves.
// The corporate baseline ships in Data/Symbols/STING_LINE_WEIGHTS.json; a project can
// override or extend it at _BIM_COORD/line_weights.json (resolved through StingPaths).
// Edits are picked up on the next Symbols_CreateAll without recompiling.
//
// Resolution normalises a subcategory (lowercase, alphanumeric only) then tries, in
// order: exact alias, exact keyword, SLD_ prefix strip, longest keyword substring.
// When the baseline JSON is missing nothing is invented: every lookup returns 0, the
// curve keeps the template's default weight, and the missing file is logged.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Symbols
{
    public sealed class LineWeightRegistry
    {
        private readonly Dictionary<string, int> _weights;      // normalised keyword -> weight
        private readonly Dictionary<string, string> _aliases;   // normalised value -> keyword
        private readonly Dictionary<string, int> _styleWeights; // lower-case style token -> weight

        /// <summary>True when the corporate baseline was found and parsed.</summary>
        public bool BaselineLoaded { get; }

        private static readonly object _lock = new object();
        private static LineWeightRegistry _active;
        private static LineWeightRegistry _baseline;

        private LineWeightRegistry(Dictionary<string, int> weights,
            Dictionary<string, string> aliases, Dictionary<string, int> styleWeights, bool baselineLoaded)
        {
            _weights = weights; _aliases = aliases; _styleWeights = styleWeights;
            BaselineLoaded = baselineLoaded;
        }

        /// <summary>The registry the creator resolves against. Set by <see cref="Load"/>
        /// at the top of a build; falls back to the corporate baseline otherwise.</summary>
        public static LineWeightRegistry Active
        {
            get { lock (_lock) { return _active ?? (_baseline ?? (_baseline = Build(null))); } }
        }

        /// <summary>Rebuilds from the corporate JSON plus this document's project override
        /// and makes it Active. Rebuilt on every call so JSON edits are picked up.</summary>
        public static LineWeightRegistry Load(Document doc)
        {
            var reg = Build(ResolveOverridePath(doc));
            lock (_lock) { _active = reg; }
            return reg;
        }

        public static void Reload()
        {
            lock (_lock) { _active = null; _baseline = null; }
        }

        /// <summary>Resolves the projection line weight (1–16) for a subcategory, or 0
        /// when nothing matches (the caller then tries the style hint and symbol default).</summary>
        public int Resolve(string subcat)
        {
            if (string.IsNullOrWhiteSpace(subcat)) return 0;
            string n = Norm(subcat);

            // 1) exact alias -> keyword
            if (_aliases.TryGetValue(n, out var ali))
            {
                string k = Norm(ali);
                if (_weights.TryGetValue(k, out var wa)) return wa;
                n = k;
            }
            // 2) exact keyword
            if (_weights.TryGetValue(n, out var w)) return w;

            // 3) SLD_ prefix strip (schematic variants of a base subcategory)
            if (n.StartsWith("sld", StringComparison.Ordinal) && n.Length > 3)
            {
                string n2 = n.Substring(3);
                if (_aliases.TryGetValue(n2, out var a2))
                {
                    string k = Norm(a2);
                    if (_weights.TryGetValue(k, out var w2)) return w2;
                    n2 = k;
                }
                if (_weights.TryGetValue(n2, out var w3)) return w3;
                n = n2;
            }

            // 4) longest keyword substring (most specific wins)
            int best = 0, blen = 0;
            foreach (var kv in _weights)
                if (kv.Key.Length > blen && n.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                { best = kv.Value; blen = kv.Key.Length; }
            return best;
        }

        /// <summary>Maps a legacy line-style hint ("Wide/Medium/Thin Lines") to a weight.</summary>
        public int StyleWeight(string style)
        {
            if (string.IsNullOrWhiteSpace(style)) return 0;
            string s = style.ToLowerInvariant();
            foreach (var kv in _styleWeights)
                if (s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) return kv.Value;
            return 0;
        }

        // ── build ─────────────────────────────────────────────────────────

        private static LineWeightRegistry Build(string overridePath)
        {
            var weights = new Dictionary<string, int>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var styles = new Dictionary<string, int>(StringComparer.Ordinal);

            string corp = StingToolsApp.FindDataFile("Symbols/STING_LINE_WEIGHTS.json")
                ?? StingToolsApp.FindDataFile("STING_LINE_WEIGHTS.json");
            bool loaded = MergeJson(corp, weights, aliases, styles);
            if (!loaded)
                StingLog.Warn("LineWeightRegistry: STING_LINE_WEIGHTS.json not found or unreadable. " +
                    "Symbol curves keep the family template's default line weight.");

            // Project override (additive; wins by key).
            if (!string.IsNullOrEmpty(overridePath))
                MergeJson(overridePath, weights, aliases, styles);

            return new LineWeightRegistry(weights, aliases, styles, loaded);
        }

        private static bool MergeJson(string path, Dictionary<string, int> weights,
            Dictionary<string, string> aliases, Dictionary<string, int> styles)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                var root = JObject.Parse(File.ReadAllText(path));
                if (root["weights"] is JObject wj)
                    foreach (var p in wj.Properties()) weights[Norm(p.Name)] = (int)p.Value;
                if (root["aliases"] is JObject aj)
                    foreach (var p in aj.Properties()) aliases[Norm(p.Name)] = (string)p.Value;
                if (root["styleWeights"] is JObject sj)
                    foreach (var p in sj.Properties()) styles[p.Name.ToLowerInvariant()] = (int)p.Value;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LineWeightRegistry.MergeJson '{path}': {ex.Message}");
                return false;
            }
        }

        private static string ResolveOverridePath(Document doc)
        {
            try
            {
                string p = StingPaths.MetaFile(doc, "_BIM_COORD", "line_weights.json");
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LineWeightRegistry.ResolveOverridePath: {ex.Message}");
                return null;
            }
        }

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
