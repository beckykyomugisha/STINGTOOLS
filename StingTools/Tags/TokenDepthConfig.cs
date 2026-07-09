using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>The full Tokens &amp; Depth control state — shared by named presets (E3)
    /// and per-view persistence (E4). Plain fields so Newtonsoft round-trips cleanly.</summary>
    public sealed class TokenDepthConfig
    {
        public string Mask = "11111111";
        public string Separator = "-";
        public int SeqPad = 4;
        public string SegOrder = "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ";
        public int Depth = 10;
        public string HandoverMode = "Handover";
        public string Scope = "View";
    }

    /// <summary>
    /// E3 — named Tokens &amp; Depth presets. Corporate baseline
    /// <c>Data/STING_TOKEN_DEPTH_PRESETS.json</c> + project presets at
    /// <c>&lt;project&gt;/_BIM_COORD/token_depth_presets.json</c> (merged by name, project
    /// wins). Saving writes to the project file only — the corporate baseline stays pristine.
    /// </summary>
    public static class TokenDepthPresets
    {
        private static readonly object _lock = new object();
        private static string _loadedKey = "\0uninit";
        private static Dictionary<string, TokenDepthConfig> _map =
            new Dictionary<string, TokenDepthConfig>(StringComparer.OrdinalIgnoreCase);

        public static void EnsureLoaded(Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_lock)
            {
                if (_loadedKey == key) return;
                _loadedKey = key;
                var map = new Dictionary<string, TokenDepthConfig>(StringComparer.OrdinalIgnoreCase);
                Merge(map, CorporatePath());
                Merge(map, ProjectPath(doc));
                _map = map;
                StingLog.Info($"TokenDepthPresets: {_map.Count} preset(s) loaded.");
            }
        }

        public static List<string> Names(Document doc)
        {
            EnsureLoaded(doc);
            var list = new List<string>(_map.Keys);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static TokenDepthConfig Get(Document doc, string name)
        {
            EnsureLoaded(doc);
            if (name != null && _map.TryGetValue(name, out TokenDepthConfig c)) return c;
            return null;
        }

        /// <summary>Save/replace a project preset. Returns null on success else an error.</summary>
        public static string Save(Document doc, string name, TokenDepthConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Preset name is required.";
            string path = ProjectPath(doc);
            if (string.IsNullOrEmpty(path)) return "Save the project first (presets live in _BIM_COORD).";
            try
            {
                var root = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
                if (!(root["presets"] is JObject presets)) { presets = new JObject(); root["presets"] = presets; }
                presets[name] = JObject.Parse(JsonConvert.SerializeObject(cfg));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, root.ToString());
                Invalidate();
                return null;
            }
            catch (Exception ex) { return "Could not save preset: " + ex.Message; }
        }

        public static void Invalidate() { lock (_lock) { _loadedKey = "\0uninit"; } }

        private static string CorporatePath()
        { try { return StingToolsApp.FindDataFile("STING_TOKEN_DEPTH_PRESETS.json"); } catch { return null; } }

        private static string ProjectPath(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (string.IsNullOrEmpty(p)) return null;
                string dir = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "token_depth_presets.json");
            }
            catch { return null; }
        }

        private static void Merge(Dictionary<string, TokenDepthConfig> map, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                if (!(root["presets"] is JObject presets)) return;
                foreach (var kv in presets)
                {
                    try
                    {
                        var cfg = JsonConvert.DeserializeObject<TokenDepthConfig>(kv.Value.ToString());
                        if (cfg != null) map[kv.Key] = cfg;
                    }
                    catch { /* skip a malformed preset */ }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TokenDepthPresets load '{path}': {ex.Message}"); }
        }
    }

    /// <summary>
    /// E4 — per-view persistence of the full Tokens &amp; Depth config, keyed by view
    /// UniqueId, at <c>&lt;project&gt;/_BIM_COORD/view_token_configs.json</c>. On Set depth the
    /// active view's config is saved; on view activation the panel is repopulated from it.
    /// </summary>
    public static class TokenDepthViewStore
    {
        private static readonly object _lock = new object();

        public static TokenDepthConfig Get(Document doc, string viewUniqueId)
        {
            if (doc == null || string.IsNullOrEmpty(viewUniqueId)) return null;
            string path = StorePath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                lock (_lock)
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    if (root["views"] is JObject views && views[viewUniqueId] != null)
                        return JsonConvert.DeserializeObject<TokenDepthConfig>(views[viewUniqueId].ToString());
                }
            }
            catch (Exception ex) { StingLog.Warn($"TokenDepthViewStore.Get: {ex.Message}"); }
            return null;
        }

        public static void Save(Document doc, string viewUniqueId, TokenDepthConfig cfg)
        {
            if (doc == null || string.IsNullOrEmpty(viewUniqueId) || cfg == null) return;
            string path = StorePath(doc);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                lock (_lock)
                {
                    var root = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
                    if (!(root["views"] is JObject views)) { views = new JObject(); root["views"] = views; }
                    views[viewUniqueId] = JObject.Parse(JsonConvert.SerializeObject(cfg));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, root.ToString());
                }
            }
            catch (Exception ex) { StingLog.Warn($"TokenDepthViewStore.Save: {ex.Message}"); }
        }

        private static string StorePath(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (string.IsNullOrEmpty(p)) return null;
                string dir = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "view_token_configs.json");
            }
            catch { return null; }
        }
    }
}
