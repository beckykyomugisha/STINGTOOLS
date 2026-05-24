using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>
    /// Loads <see cref="TextureProviderCatalogue"/> from the corporate
    /// `STING_TEXTURE_PROVIDERS.json` and overlays the per-project
    /// `_BIM_COORD/texture_providers.json` on top. Provider entries match
    /// by id (project wins); suffix rules merge (project additions append).
    /// </summary>
    public static class TextureProviderRegistry
    {
        private const string CorporateFile = "STING_TEXTURE_PROVIDERS.json";
        private const string ProjectFile = "texture_providers.json";

        private static readonly object _lock = new object();
        private static TextureProviderCatalogue _cache;
        private static string _cacheProjectKey;

        public static TextureProviderCatalogue Load(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            lock (_lock)
            {
                if (_cache != null && string.Equals(_cacheProjectKey, key, StringComparison.OrdinalIgnoreCase))
                    return _cache;

                var cat = LoadCorporate();
                if (cat == null) cat = new TextureProviderCatalogue();
                Overlay(cat, LoadProject(doc));
                _cache = cat;
                _cacheProjectKey = key;
                return cat;
            }
        }

        public static void Reload()
        {
            lock (_lock) { _cache = null; _cacheProjectKey = null; }
        }

        private static TextureProviderCatalogue LoadCorporate()
        {
            try
            {
                string path = StingToolsApp.FindDataFile(CorporateFile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn($"TextureProviderRegistry: corporate file '{CorporateFile}' not found");
                    return null;
                }
                return JsonConvert.DeserializeObject<TextureProviderCatalogue>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TextureProviderRegistry.LoadCorporate: {ex.Message}");
                return null;
            }
        }

        private static TextureProviderCatalogue LoadProject(Document doc)
        {
            try
            {
                string root = ProjectBimCoordRoot(doc);
                if (string.IsNullOrEmpty(root)) return null;
                string path = Path.Combine(root, ProjectFile);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<TextureProviderCatalogue>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TextureProviderRegistry.LoadProject: {ex.Message}");
                return null;
            }
        }

        private static void Overlay(TextureProviderCatalogue target, TextureProviderCatalogue overlay)
        {
            if (overlay == null) return;

            if (overlay.Providers != null)
            {
                foreach (var p in overlay.Providers)
                {
                    if (string.IsNullOrEmpty(p?.Id)) continue;
                    int idx = target.Providers.FindIndex(x =>
                        string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) target.Providers[idx] = p;
                    else target.Providers.Add(p);
                }
            }

            if (overlay.MapSuffixRules != null)
            {
                foreach (var kv in overlay.MapSuffixRules)
                {
                    if (target.MapSuffixRules.ContainsKey(kv.Key))
                    {
                        foreach (var suf in kv.Value ?? new List<string>())
                            if (!target.MapSuffixRules[kv.Key].Contains(suf, StringComparer.OrdinalIgnoreCase))
                                target.MapSuffixRules[kv.Key].Add(suf);
                    }
                    else
                    {
                        target.MapSuffixRules[kv.Key] = kv.Value ?? new List<string>();
                    }
                }
            }

            if (overlay.Defaults != null) target.Defaults = overlay.Defaults;
        }

        public static string ProjectBimCoordRoot(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (string.IsNullOrEmpty(p)) return null;
                string dir = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(dir)) return null;
                string bim = Path.Combine(dir, "_BIM_COORD");
                if (!Directory.Exists(bim))
                {
                    try { Directory.CreateDirectory(bim); } catch { return null; }
                }
                return bim;
            }
            catch { return null; }
        }

        public static string ProjectTexturesRoot(Document doc)
        {
            string bim = ProjectBimCoordRoot(doc);
            if (string.IsNullOrEmpty(bim)) return null;
            string tex = Path.Combine(bim, "textures");
            try { Directory.CreateDirectory(tex); } catch { return null; }
            return tex;
        }

        public static IDictionary<string, IList<string>> SuffixRulesFor(Document doc)
        {
            var cat = Load(doc);
            if (cat?.MapSuffixRules == null || cat.MapSuffixRules.Count == 0)
                return TexturePackIngester.BuiltInSuffixRules();
            return cat.MapSuffixRules.ToDictionary(
                kv => kv.Key,
                kv => (IList<string>)kv.Value);
        }
    }

    public sealed class TextureProviderCatalogue
    {
        [JsonProperty("providers")]
        public List<TextureProviderEntry> Providers { get; set; } = new List<TextureProviderEntry>();

        [JsonProperty("mapSuffixRules")]
        public Dictionary<string, List<string>> MapSuffixRules { get; set; } = new Dictionary<string, List<string>>();

        [JsonProperty("defaults")]
        public TexturePackDefaults Defaults { get; set; }
    }

    public sealed class TextureProviderEntry
    {
        [JsonProperty("id")]            public string Id { get; set; }
        [JsonProperty("name")]          public string Name { get; set; }
        [JsonProperty("kind")]          public string Kind { get; set; }   // inline | url-launch | folder-watch
        [JsonProperty("license")]       public string License { get; set; }
        [JsonProperty("cost")]          public string Cost { get; set; }
        [JsonProperty("apiBase")]       public string ApiBase { get; set; }
        [JsonProperty("assetListPath")] public string AssetListPath { get; set; }
        [JsonProperty("thumbnailPattern")]   public string ThumbnailPattern { get; set; }
        [JsonProperty("downloadIndexPath")]  public string DownloadIndexPath { get; set; }
        [JsonProperty("downloadPattern")]    public string DownloadPattern { get; set; }
        [JsonProperty("preferredResolution")]public string PreferredResolution { get; set; }
        [JsonProperty("preferredFormat")]    public string PreferredFormat { get; set; }
        [JsonProperty("homepageUrl")]   public string HomepageUrl { get; set; }
        [JsonProperty("description")]   public string Description { get; set; }
        [JsonProperty("enabledByDefault")]   public bool EnabledByDefault { get; set; } = true;
        [JsonProperty("ingestFolder")]  public string IngestFolder { get; set; }
        [JsonProperty("categories")]    public List<string> Categories { get; set; } = new List<string>();
    }
}
