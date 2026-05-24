using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>
    /// Provider client for paid / subscription services that don't expose a
    /// public download API (Architextures Pro, Substance Source, etc.).
    /// <see cref="ListAssetsAsync"/> returns an empty list and the UI shows
    /// a "browse → drop into ingest folder" hint; <see cref="OpenBrowser"/>
    /// launches the homepage so the user can authenticate and download
    /// manually.
    /// </summary>
    public sealed class UrlLaunchClient : IPbrProviderClient
    {
        private readonly TextureProviderEntry _entry;

        public UrlLaunchClient(TextureProviderEntry entry) { _entry = entry; }

        public string ProviderId => _entry?.Id ?? "url-launch";
        public string DisplayName => _entry?.Name ?? "External (browser)";
        public bool SupportsInlineBrowse => false;

        public Task<IReadOnlyList<PbrAssetSummary>> ListAssetsAsync(
            string categoryFilter, string searchText, int maxResults, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PbrAssetSummary>>(Array.Empty<PbrAssetSummary>());

        public Task<string> DownloadThumbnailAsync(PbrAssetSummary asset, CancellationToken ct)
            => Task.FromResult<string>(null);

        public Task<TexturePackManifest> DownloadPackAsync(
            PbrAssetSummary asset, string destinationRoot,
            string resolutionHint, string formatHint, CancellationToken ct)
            => Task.FromResult<TexturePackManifest>(null);

        public bool OpenBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_entry?.HomepageUrl ?? "about:blank") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"UrlLaunchClient.OpenBrowser: {ex.Message}"); return false; }
        }
    }

    /// <summary>
    /// Provider client for the "drop a folder into _BIM_COORD/textures/ and
    /// I'll find it" pattern. Used for user-supplied packs (Megascans
    /// extracts, Substance exports, in-house libraries).
    /// <see cref="ListAssetsAsync"/> walks the project's texture root and
    /// surfaces every sub-folder that contains at least one image file.
    /// </summary>
    public sealed class UserFolderClient : IPbrProviderClient
    {
        private readonly TextureProviderEntry _entry;
        private readonly string _projectTexturesRoot;

        public UserFolderClient(TextureProviderEntry entry, string projectTexturesRoot)
        {
            _entry = entry;
            _projectTexturesRoot = projectTexturesRoot;
        }

        public string ProviderId => _entry?.Id ?? "user-folder";
        public string DisplayName => _entry?.Name ?? "User-supplied folder";
        public bool SupportsInlineBrowse => true;

        public Task<IReadOnlyList<PbrAssetSummary>> ListAssetsAsync(
            string categoryFilter, string searchText, int maxResults, CancellationToken ct)
        {
            var hits = new List<PbrAssetSummary>();
            try
            {
                if (string.IsNullOrEmpty(_projectTexturesRoot) || !Directory.Exists(_projectTexturesRoot))
                    return Task.FromResult<IReadOnlyList<PbrAssetSummary>>(hits);

                foreach (var dir in Directory.GetDirectories(_projectTexturesRoot, "*", SearchOption.AllDirectories))
                {
                    if (hits.Count >= maxResults) break;
                    if (!HasAnyImage(dir)) continue;

                    string id = Path.GetFileName(dir);
                    string parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");
                    string category = parent != null && !parent.Equals("textures", StringComparison.OrdinalIgnoreCase) ? parent : "";

                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        string hay = (id + " " + category).ToLowerInvariant();
                        if (!hay.Contains(searchText.ToLowerInvariant())) continue;
                    }
                    if (!string.IsNullOrWhiteSpace(categoryFilter) &&
                        !category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string thumb = FindFirstImage(dir);

                    hits.Add(new PbrAssetSummary
                    {
                        Id = dir,                           // full path acts as id
                        DisplayName = id,
                        Category = category,
                        ProviderId = ProviderId,
                        ThumbnailUrl = thumb,               // local file path
                        AssetPageUrl = dir,
                        License = "varies",
                        Resolution = 0,
                        Tags = "",
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"UserFolderClient.ListAssetsAsync: {ex.Message}"); }
            return Task.FromResult<IReadOnlyList<PbrAssetSummary>>(hits);
        }

        public Task<string> DownloadThumbnailAsync(PbrAssetSummary asset, CancellationToken ct)
            => Task.FromResult(asset?.ThumbnailUrl);   // already a local path

        public Task<TexturePackManifest> DownloadPackAsync(
            PbrAssetSummary asset, string destinationRoot,
            string resolutionHint, string formatHint, CancellationToken ct)
        {
            try
            {
                string folder = asset?.Id;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return Task.FromResult<TexturePackManifest>(null);
                var m = TexturePackIngester.LoadOrIngest(folder, providerId: ProviderId, sourceUrl: folder, license: "varies");
                return Task.FromResult(m);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UserFolderClient.DownloadPackAsync: {ex.Message}");
                return Task.FromResult<TexturePackManifest>(null);
            }
        }

        public bool OpenBrowser()
        {
            try
            {
                if (string.IsNullOrEmpty(_projectTexturesRoot)) return false;
                Process.Start(new ProcessStartInfo(_projectTexturesRoot) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"UserFolderClient.OpenBrowser: {ex.Message}"); return false; }
        }

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".hdr", ".tga", ".bmp" };
        private static bool HasAnyImage(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Any(p => ImageExts.Contains(Path.GetExtension(p).ToLowerInvariant()));
            }
            catch { return false; }
        }
        private static string FindFirstImage(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(p => ImageExts.Contains(Path.GetExtension(p).ToLowerInvariant()));
            }
            catch { return null; }
        }
    }
}
