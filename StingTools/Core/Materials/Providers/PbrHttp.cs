using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.Core.Materials.Providers
{
    /// <summary>Process-wide HttpClient shared by all provider clients.
    /// Long-lived per Microsoft guidance; no per-request disposal.</summary>
    internal static class PbrHttp
    {
        private static readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(() =>
        {
            var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            var c = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5),
            };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("STING-Tools-Revit/1.0 (+https://planscape.com)");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json,image/*,application/zip,*/*");
            return c;
        });

        public static HttpClient Client => _client.Value;

        public static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            try
            {
                using (var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PbrHttp.GetStringAsync('{url}'): {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> DownloadFileAsync(string url, string destPath, CancellationToken ct)
        {
            // Retry up to 3 times with exponential back-off — large packs are
            // shipped from CDNs that occasionally 503 or close connections
            // mid-stream. Honour cancellation between attempts.
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    using (var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        using (var ds = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            await ds.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
                        }
                    }
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    StingLog.Warn($"PbrHttp.DownloadFileAsync('{url}' → '{destPath}') attempt {attempt}/{maxAttempts}: {ex.Message}");
                    if (attempt == maxAttempts) return false;
                    try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }
            return false;
        }
    }
}
