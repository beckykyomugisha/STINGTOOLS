using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>
    /// ambientCG client. Endpoints used:
    ///   GET /api/v2/full_json?type=Material&amp;include=downloadData,previewData[&amp;limit=...]
    ///   GET /get?file=&lt;assetId&gt;_&lt;res&gt;-&lt;fmt&gt;.zip
    /// Packs are CC0 zips containing all maps; we unzip into the per-asset
    /// folder and let the suffix ingester register slots.
    /// </summary>
    public sealed class AmbientCgClient : IPbrProviderClient
    {
        private readonly TextureProviderEntry _entry;

        public AmbientCgClient(TextureProviderEntry entry) { _entry = entry; }

        public string ProviderId => "ambientcg";
        public string DisplayName => _entry?.Name ?? "ambientCG";
        public bool SupportsInlineBrowse => true;

        public async Task<IReadOnlyList<PbrAssetSummary>> ListAssetsAsync(
            string categoryFilter, string searchText, int maxResults, CancellationToken ct)
        {
            try
            {
                string apiBase = (_entry?.ApiBase ?? "https://ambientCG.com");
                string listPath = (_entry?.AssetListPath ??
                                   "/api/v2/full_json?type=Material&include=downloadData,previewData");
                string url = apiBase + listPath + "&limit=" + maxResults;
                if (!string.IsNullOrWhiteSpace(categoryFilter))
                    url += "&category=" + Uri.EscapeDataString(categoryFilter);
                if (!string.IsNullOrWhiteSpace(searchText))
                    url += "&q=" + Uri.EscapeDataString(searchText);

                string json = await PbrHttp.GetStringAsync(url, ct);
                if (string.IsNullOrEmpty(json)) return Array.Empty<PbrAssetSummary>();

                var root = JObject.Parse(json);
                var foundAssets = root.Value<JArray>("foundAssets");
                if (foundAssets == null) return Array.Empty<PbrAssetSummary>();

                var results = new List<PbrAssetSummary>(Math.Min(foundAssets.Count, maxResults));
                foreach (var item in foundAssets.OfType<JObject>().Take(maxResults))
                {
                    string id = item.Value<string>("assetId");
                    if (string.IsNullOrEmpty(id)) continue;

                    string name = item.Value<string>("displayName") ?? id;
                    string category = item.Value<string>("displayCategory") ?? "";
                    string tagsRaw = string.Join(" ", item.Value<JArray>("tags")?.Select(t => t.ToString()) ?? Enumerable.Empty<string>());
                    int dimX = item.Value<int?>("dimensionX") ?? 0;
                    int dimY = item.Value<int?>("dimensionY") ?? 0;

                    results.Add(new PbrAssetSummary
                    {
                        Id = id,
                        DisplayName = name,
                        Category = category,
                        ProviderId = ProviderId,
                        ThumbnailUrl = FormatThumbnail(id),
                        AssetPageUrl = $"https://ambientcg.com/view?id={Uri.EscapeDataString(id)}",
                        License = "CC0",
                        Resolution = Math.Max(dimX, dimY),
                        Tags = tagsRaw,
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AmbientCgClient.ListAssetsAsync: {ex.Message}");
                return Array.Empty<PbrAssetSummary>();
            }
        }

        public async Task<string> DownloadThumbnailAsync(PbrAssetSummary asset, CancellationToken ct)
        {
            if (asset == null || string.IsNullOrEmpty(asset.ThumbnailUrl)) return null;
            string cacheDir = PolyHavenClient.ThumbnailCacheRoot();
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
            string resolution = (string.IsNullOrEmpty(resolutionHint) ? (_entry?.PreferredResolution ?? "2K") : resolutionHint).ToUpperInvariant();
            string format = (string.IsNullOrEmpty(formatHint) ? (_entry?.PreferredFormat ?? "PNG") : formatHint).ToUpperInvariant();

            string packFolder = Path.Combine(destinationRoot, ProviderId, asset.Id);
            Directory.CreateDirectory(packFolder);

            string downloadFile = $"{asset.Id}_{resolution}-{format}.zip";
            string url = (_entry?.DownloadPattern ?? "https://ambientCG.com/get?file={file}")
                .Replace("{file}", downloadFile)
                .Replace("{id}", Uri.EscapeDataString(asset.Id))
                .Replace("{res}", resolution)
                .Replace("{fmt}", format);

            string zipPath = Path.Combine(packFolder, downloadFile);
            bool ok = await PbrHttp.DownloadFileAsync(url, zipPath, ct);
            if (!ok)
            {
                StingLog.Warn($"ambientCG download failed: {url}");
                return null;
            }

            try
            {
                // Zip-bomb / zip-slip guards: cap entry count + cumulative
                // decompressed bytes; reject any entry whose normalised
                // output path escapes the destination folder.
                const int MaxEntries = 32;
                const long MaxDecompressedBytes = 1L * 1024 * 1024 * 1024; // 1 GiB total
                long extracted = 0;
                int entryCount = 0;
                string packFolderFull = Path.GetFullPath(packFolder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                using (var zs = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zs.Entries)
                    {
                        if (++entryCount > MaxEntries)
                        {
                            StingLog.Warn($"ambientCG zip aborted: > {MaxEntries} entries (possible zip bomb): {zipPath}");
                            return null;
                        }
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        if (entry.Length > MaxDecompressedBytes) { StingLog.Warn($"ambientCG zip entry oversized ({entry.Length} B): {entry.FullName}"); return null; }
                        extracted += entry.Length;
                        if (extracted > MaxDecompressedBytes) { StingLog.Warn($"ambientCG zip aborted: cumulative > {MaxDecompressedBytes} B"); return null; }

                        string outPath = Path.GetFullPath(Path.Combine(packFolder, entry.Name));
                        if (!outPath.StartsWith(packFolderFull, StringComparison.OrdinalIgnoreCase))
                        {
                            StingLog.Warn($"ambientCG zip-slip blocked: '{entry.FullName}' tried to escape pack folder");
                            return null;
                        }

                        if (File.Exists(outPath)) continue;
                        entry.ExtractToFile(outPath, overwrite: false);
                    }
                }
                try { File.Delete(zipPath); } catch { /* keep zip if locked by AV scanner */ }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ambientCG unzip failed: {ex.Message}");
                return null;
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
                Process.Start(new ProcessStartInfo(_entry?.HomepageUrl ?? "https://ambientcg.com/list?type=Material") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"OpenBrowser: {ex.Message}"); return false; }
        }

        private string FormatThumbnail(string id)
        {
            string pattern = _entry?.ThumbnailPattern ??
                "https://acg-media.struffelproductions.com/file/ambientCG-Web/media/thumbnail/256-PNG-242424/{id}.png";
            return pattern.Replace("{id}", Uri.EscapeDataString(id));
        }
    }
}
