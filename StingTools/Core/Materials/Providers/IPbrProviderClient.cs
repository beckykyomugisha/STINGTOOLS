using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>
    /// Provider-agnostic interface for PBR texture sources. Two work
    /// patterns are supported:
    /// <list type="bullet">
    ///   <item><c>SupportsInlineBrowse == true</c>: thumbnails come from
    ///   <see cref="ListAssetsAsync"/>; the user picks an asset and the
    ///   pack lands locally via <see cref="DownloadPackAsync"/>.</item>
    ///   <item><c>SupportsInlineBrowse == false</c>: <see cref="OpenBrowser"/>
    ///   launches the provider's site so the user can download manually;
    ///   the resulting folder is then ingested via the suffix detector.</item>
    /// </list>
    /// </summary>
    public interface IPbrProviderClient
    {
        string ProviderId { get; }
        string DisplayName { get; }
        bool SupportsInlineBrowse { get; }

        Task<IReadOnlyList<PbrAssetSummary>> ListAssetsAsync(
            string categoryFilter,
            string searchText,
            int maxResults,
            CancellationToken ct);

        Task<string> DownloadThumbnailAsync(PbrAssetSummary asset, CancellationToken ct);

        Task<TexturePackManifest> DownloadPackAsync(
            PbrAssetSummary asset,
            string destinationRoot,
            string resolutionHint,
            string formatHint,
            CancellationToken ct);

        /// <summary>Launch a browser to the provider's library page. Returns
        /// false if the provider has no homepage configured.</summary>
        bool OpenBrowser();
    }

    public sealed class PbrAssetSummary
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string ProviderId { get; set; }
        public string ThumbnailUrl { get; set; }
        public string AssetPageUrl { get; set; }
        public string License { get; set; }
        public int Resolution { get; set; }
        public string Tags { get; set; }
    }
}
