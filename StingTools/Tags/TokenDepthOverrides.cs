using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>One category's optional display overrides (any field null = use panel global).</summary>
    public sealed class TokenDepthOverride
    {
        public int? SeqPad;   // 2-5 SEQ zero-pad width
        public string Mask;   // 8-char 0/1 segment mask, or null
        public int? Depth;    // 1-10 paragraph/tier depth
    }

    /// <summary>
    /// Per-category token-depth overrides (E2) applied live on "Set depth". Lets different
    /// categories carry different display treatment — e.g. Doors 2-digit SEQ + compact,
    /// Mechanical Equipment 4-digit + full — without touching the panel globals.
    ///
    /// Corporate baseline: <c>Data/STING_TOKEN_DEPTH_OVERRIDES.json</c>. Project override:
    /// <c>&lt;project&gt;/_BIM_COORD/token_depth_overrides.json</c>, merged by category key
    /// (project wins). Category key = Revit category name (spaced). Cached per document;
    /// call <see cref="Invalidate"/> after editing a file.
    /// </summary>
    public static class TokenDepthOverrides
    {
        private static readonly object _lock = new object();
        private static string _loadedKey = "\0uninit";
        private static Dictionary<string, TokenDepthOverride> _map =
            new Dictionary<string, TokenDepthOverride>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True once a doc has been loaded and at least one override exists.</summary>
        public static bool HasAny { get { return _map.Count > 0; } }

        public static void EnsureLoaded(Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_lock)
            {
                if (_loadedKey == key) return;
                _loadedKey = key;
                var map = new Dictionary<string, TokenDepthOverride>(StringComparer.OrdinalIgnoreCase);
                Merge(map, CorporatePath());
                Merge(map, ProjectPath(doc));
                _map = map;
                StingLog.Info($"TokenDepthOverrides: {_map.Count} category override(s) loaded.");
            }
        }

        /// <summary>Resolve a category's override, or null when none is configured.</summary>
        public static TokenDepthOverride Resolve(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;
            var m = _map;
            m.TryGetValue(categoryName, out TokenDepthOverride o);
            return o;
        }

        public static void Invalidate() { lock (_lock) { _loadedKey = "\0uninit"; } }

        private static string CorporatePath()
        {
            try { return StingToolsApp.FindDataFile("STING_TOKEN_DEPTH_OVERRIDES.json"); }
            catch { return null; }
        }

        private static string ProjectPath(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (string.IsNullOrEmpty(p)) return null;
                string dir = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "token_depth_overrides.json");
            }
            catch { return null; }
        }

        private static void Merge(Dictionary<string, TokenDepthOverride> map, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                if (!(root["categories"] is JObject cats)) return;
                foreach (var kv in cats)
                {
                    if (!(kv.Value is JObject v)) continue;
                    var o = new TokenDepthOverride();
                    if (v["seqPad"] != null && v["seqPad"].Type != JTokenType.Null) o.SeqPad = (int?)v["seqPad"];
                    if (v["mask"] != null && v["mask"].Type != JTokenType.Null) o.Mask = (string)v["mask"];
                    if (v["depth"] != null && v["depth"].Type != JTokenType.Null) o.Depth = (int?)v["depth"];
                    map[kv.Key] = o; // later Merge (project) overrides earlier (corporate)
                }
            }
            catch (Exception ex) { StingLog.Warn($"TokenDepthOverrides load '{path}': {ex.Message}"); }
        }
    }
}
