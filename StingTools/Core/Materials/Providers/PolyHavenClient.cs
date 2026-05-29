using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>
    /// Poly Haven REST client. Endpoints used:
    ///   GET /assets?t=textures        — full asset listing
    ///   GET /files/{id}               — per-asset file index
    ///   GET /info/{id}                — per-asset metadata
    /// Files are CC0; UA + Accept headers required for civility.
    /// </summary>
    public sealed class PolyHavenClient : IPbrProviderClient
    {
        private readonly TextureProviderEntry _entry;

        public PolyHavenClient(TextureProviderEntry entry) { _entry = entry; }

        public string ProviderId => "polyhaven";
        public string DisplayName => _entry?.Name ?? "Poly Haven";
        public bool SupportsInlineBrowse => true;

        public async Task<IReadOnlyList<PbrAssetSummary>> ListAssetsAsync(
            string categoryFilter, string searchText, int maxResults, CancellationToken ct)
        {
            try
            {
                string url = (_entry?.ApiBase ?? "https://api.polyhaven.com") +
                             (_entry?.AssetListPath ?? "/assets?t=textures");
                if (!string.IsNullOrWhiteSpace(categoryFilter))
                    url += "&c=" + Uri.EscapeDataString(categoryFilter);
                string json = await PbrHttp.GetStringAsync(url, ct);
                if (string.IsNullOrEmpty(json)) return Array.Empty<PbrAssetSummary>();

                var root = JObject.Parse(json);
                var results = new List<PbrAssetSummary>(Math.Min(maxResults, root.Count));
                foreach (var prop in root.Properties())
                {
                    if (results.Count >= maxResults) break;
                    string id = prop.Name;
                    var item = prop.Value as JObject;
                    string name = item?.Value<string>("name") ?? id;
                    string category = string.Join(",", item?.Value<JArray>("categories")?.Select(c => c.ToString()) ?? Enumerable.Empty<string>());
                    string tags = string.Join(" ", item?.Value<JArray>("tags")?.Select(t => t.ToString()) ?? Enumerable.Empty<string>());
                    int maxRes = item?.Value<int?>("max_resolution") ?? 0;

                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        string hay = (name + " " + tags + " " + category).ToLowerInvariant();
                        if (!hay.Contains(searchText.ToLowerInvariant())) continue;
                    }

                    results.Add(new PbrAssetSummary
                    {
                        Id = id,
                        DisplayName = name,
                        Category = category,
                        ProviderId = ProviderId,
                        ThumbnailUrl = FormatThumbnail(id),
                        AssetPageUrl = $"https://polyhaven.com/a/{id}",
                        License = "CC0",
                        Resolution = maxRes,
                        Tags = tags,
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PolyHavenClient.ListAssetsAsync: {ex.Message}");
                return Array.Empty<PbrAssetSummary>();
            }
        }

        public async Task<string> DownloadThumbnailAsync(PbrAssetSummary asset, CancellationToken ct)
        {
            if (asset == null || string.IsNullOrEmpty(asset.ThumbnailUrl)) return null;
            string cacheDir = ThumbnailCacheRoot();
            string dest = Path.Combine(cacheDir, $"{ProviderId}__{asset.Id}.png");
            if (File.Exists(dest)) return dest;
            bool ok = await PbrHttp.DownloadFileAsync(asset.ThumbnailUrl, dest, ct);
            return ok ? dest : null;
        }

        public async Task<TexturePackManifest> DownloadPackAsync(
            PbrAssetSummary asset, string destinationRoot,
            string resolutionHint, string formatHint, CancellationToken ct)
        {
            if (asset == null) return null;
            string resolution = string.IsNullOrEmpty(resolutionHint) ? (_entry?.PreferredResolution ?? "2k") : resolutionHint.ToLowerInvariant();
            string format = string.IsNullOrEmpty(formatHint) ? (_entry?.PreferredFormat ?? "png") : formatHint.ToLowerInvariant();

            string indexUrl = (_entry?.ApiBase ?? "https://api.polyhaven.com") +
                              (_entry?.DownloadIndexPath ?? "/files/{id}").Replace("{id}", Uri.EscapeDataString(asset.Id));
            string json = await PbrHttp.GetStringAsync(indexUrl, ct);
            if (string.IsNullOrEmpty(json)) return null;

            var packFolder = Path.Combine(destinationRoot, ProviderId, asset.Id);
            Directory.CreateDirectory(packFolder);

            // Poly Haven file layout: { "<map_name>": { "<resolution>": { "<fmt>": { "url": "..." } } } }
            // Map names: Diffuse, nor_gl, Rough, Metal, AO, Bump, Displacement, etc.
            var maps = JObject.Parse(json);
            foreach (var mapProp in maps.Properties())
            {
                var resObj = mapProp.Value as JObject;
                var resEntry = resObj?.Property(resolution, StringComparison.OrdinalIgnoreCase)?.Value as JObject
                            ?? resObj?.Properties().FirstOrDefault()?.Value as JObject;
                if (resEntry == null) continue;

                var fmtEntry = resEntry.Property(format, StringComparison.OrdinalIgnoreCase)?.Value as JObject
                            ?? resEntry.Properties().FirstOrDefault()?.Value as JObject;
                if (fmtEntry == null) continue;

                string url = fmtEntry.Value<string>("url");
                if (string.IsNullOrEmpty(url)) continue;

                string fileName = $"{asset.Id}_{NormaliseMapName(mapProp.Name)}.{ExtFromUrl(url)}";
                string dest = Path.Combine(packFolder, fileName);
                if (!File.Exists(dest))
                {
                    bool ok = await PbrHttp.DownloadFileAsync(url, dest, ct);
                    if (!ok) StingLog.Warn($"Poly Haven download failed: {url}");
                }
            }

            return TexturePackIngester.LoadOrIngest(
                packFolder,
                providerId: ProviderId,
                sourceUrl: asset.AssetPageUrl,
                license: "CC0",
                suffixRules: null);
        }

        public bool OpenBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_entry?.HomepageUrl ?? "https://polyhaven.com/textures") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"OpenBrowser: {ex.Message}"); return false; }
        }

        private string FormatThumbnail(string id)
        {
            string pattern = _entry?.ThumbnailPattern ?? "https://cdn.polyhaven.com/asset_img/thumbs/{id}.png?width=256";
            return pattern.Replace("{id}", Uri.EscapeDataString(id));
        }

        private static string NormaliseMapName(string raw)
        {
            // Poly Haven map names → our suffix conventions. The ingester
            // also runs and will re-detect, but neat names help debugging.
            switch (raw.ToLowerInvariant())
            {
                case "diffuse": return "diffuse";
                case "nor_gl": case "nor_dx": case "nor": return "normal";
                case "rough": return "roughness";
                case "metal": return "metalness";
                case "ao": return "ao";
                case "bump": return "bump";
                case "displacement": case "disp": return "disp";
                case "opacity": return "opacity";
                case "emission": return "emission";
                default: return raw.ToLowerInvariant();
            }
        }

        private static string ExtFromUrl(string url)
        {
            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.');
                return string.IsNullOrEmpty(ext) ? "png" : ext;
            }
            catch { return "png"; }
        }

        internal static string ThumbnailCacheRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "STING", "PbrThumbs");
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
