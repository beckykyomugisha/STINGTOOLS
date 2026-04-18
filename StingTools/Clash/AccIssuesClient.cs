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
        private readonly HttpClient _http = new HttpClient();
        private readonly string _bearer;
        public AccIssuesClient(string bearerToken) { _bearer = bearerToken; }

        public async Task<bool> PostBcfAsync(string projectId, string bcfZipPath)
        {
            if (string.IsNullOrEmpty(projectId) || !File.Exists(bcfZipPath)) return false;
            try
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
                using var content = new MultipartFormDataContent();
                content.Add(new ByteArrayContent(File.ReadAllBytes(bcfZipPath)), "file", Path.GetFileName(bcfZipPath));
                var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId}/issues/bulk";
                var resp = await _http.PostAsync(url, content);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex) { StingLog.Warn("AccIssuesClient.PostBcf: " + ex.Message); return false; }
        }
    }
}
