// AccIssuesClient.cs — posts BCF exports to ACC Issues API.
// OAuth acquisition is a Stage 7 concern; here we accept a bearer token and POST.
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class AccIssuesClient
    {
        // rec-22: Static HttpClient per .NET best practice (avoid socket
        // exhaustion from per-call 'new HttpClient()'). Authorization is set
        // per-request on HttpRequestMessage so concurrent calls with different
        // bearer tokens don't trample each other's shared DefaultRequestHeaders.
        private static readonly HttpClient _http = new HttpClient();
        private readonly string _bearer;

        public AccIssuesClient(string bearerToken) { _bearer = bearerToken; }

        public async Task<bool> PostBcfAsync(string projectId, string bcfZipPath)
        {
            if (string.IsNullOrEmpty(projectId) || !File.Exists(bcfZipPath)) return false;
            try
            {
                var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId}/issues/bulk";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                // rec-22: per-request auth header, not shared DefaultRequestHeaders.
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);

                using var content = new MultipartFormDataContent();
                content.Add(new ByteArrayContent(File.ReadAllBytes(bcfZipPath)), "file", Path.GetFileName(bcfZipPath));
                req.Content = content;

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    StingLog.Warn($"AccIssuesClient.PostBcf HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex) { StingLog.Warn("AccIssuesClient.PostBcf: " + ex.Message); return false; }
        }
    }
}
