using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>
    /// Live model upload to Autodesk Construction Cloud via the APS Data
    /// Management API. Turns the local "ACC Publish" bundle into a real upload:
    ///
    ///   1. (optional) resolve the project's "Project Files" top folder
    ///   2. create a storage object in that folder
    ///   3. get a signed-S3 upload URL, PUT the bytes, finalise
    ///   4. create the item (first version) pointing at the storage object
    ///
    /// Reuses <see cref="AccIssueSync"/> for OAuth (same creds /
    /// %APPDATA%\Planscape\acc_credentials.json). Requires the APS app to carry
    /// at least <c>data:read data:write data:create</c> scopes.
    ///
    /// CAVEAT: built to the documented APS signatures but NOT verified against a
    /// live ACC project. Single-part upload only (fine for &lt; ~100 MB GLB/IFC);
    /// updating an existing file (new version on a 409) is a TODO — today a
    /// duplicate name reports a friendly error rather than versioning.
    /// </summary>
    public static class AccModelUpload
    {
        private const string DataBase    = "https://developer.api.autodesk.com/data/v1";
        private const string ProjectBase = "https://developer.api.autodesk.com/project/v1";
        private const string OssBase     = "https://developer.api.autodesk.com/oss/v2";
        private const string JsonApi     = "application/vnd.api+json";

        private static readonly HttpClient _http = new HttpClient();

        public sealed class UploadResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
            public string ItemUrn { get; set; } = "";
        }

        /// <summary>DM endpoints want the account/project id in 'b.{guid}' form.</summary>
        private static string EnsureB(string id)
        {
            id = (id ?? "").Trim();
            if (id.Length == 0) return id;
            return id.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? id : "b." + id;
        }

        public static async Task<UploadResult> UploadAsync(
            AccCredentials creds, string filePath, CancellationToken ct = default)
        {
            try
            {
                if (creds == null) return Fail("No credentials.");
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return Fail("Pick a file to upload first.");
                if (string.IsNullOrWhiteSpace(creds.ProjectId))
                    return Fail("Set the Issues Project ID (the ACC project) first.");

                if (!await AccIssueSync.EnsureAuthAsync(creds).ConfigureAwait(false))
                    return Fail("Not authenticated — Sign in with Autodesk (or Test/Refresh) first.");

                string projectId = EnsureB(creds.ProjectId);
                string fileName = Path.GetFileName(filePath);

                // 1. Resolve the destination folder.
                string folderUrn = creds.FolderUrn?.Trim() ?? "";
                if (string.IsNullOrEmpty(folderUrn))
                {
                    folderUrn = await ResolveProjectFilesFolderAsync(creds, projectId, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(folderUrn))
                        return Fail("Couldn't resolve a target folder — set the ACC Folder URN, or confirm Hub ID + Project ID.");
                }

                // 2. Create a storage object in the folder.
                var storageBody = new JObject
                {
                    ["jsonapi"] = new JObject { ["version"] = "1.0" },
                    ["data"] = new JObject
                    {
                        ["type"] = "objects",
                        ["attributes"] = new JObject { ["name"] = fileName },
                        ["relationships"] = new JObject
                        {
                            ["target"] = new JObject { ["data"] = new JObject { ["type"] = "folders", ["id"] = folderUrn } }
                        }
                    }
                };
                var storageResp = await SendAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/storage",
                    creds.AccessToken, storageBody, JsonApi, ct).ConfigureAwait(false);
                if (!storageResp.ok) return Fail($"Create storage failed (HTTP {storageResp.status}). {Trim(storageResp.body)}");
                string objectId = JObject.Parse(storageResp.body)["data"]?["id"]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(objectId)) return Fail("Storage response had no object id.");

                // urn:adsk.objects:os.object:{bucketKey}/{objectKey}
                int lastColon = objectId.LastIndexOf(':');
                string bucketAndKey = lastColon >= 0 ? objectId.Substring(lastColon + 1) : objectId;
                int slash = bucketAndKey.IndexOf('/');
                if (slash < 0) return Fail($"Unexpected storage object id: {objectId}");
                string bucketKey = bucketAndKey.Substring(0, slash);
                string objectKey = bucketAndKey.Substring(slash + 1);

                // 3a. Get a signed S3 upload URL.
                var signResp = await SendAsync(HttpMethod.Get,
                    $"{OssBase}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload?minutesExpiration=60",
                    creds.AccessToken, null, null, ct).ConfigureAwait(false);
                if (!signResp.ok) return Fail($"Signed-upload request failed (HTTP {signResp.status}). {Trim(signResp.body)}");
                var signJson = JObject.Parse(signResp.body);
                string uploadKey = signJson["uploadKey"]?.Value<string>() ?? "";
                string signedUrl = signJson["urls"]?[0]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(uploadKey) || string.IsNullOrEmpty(signedUrl))
                    return Fail("Signed-upload response missing uploadKey/urls (file may be too large for single-part upload).");

                // 3b. PUT the bytes to S3 (presigned — NO Authorization header).
                // Stream straight from disk so a large model isn't buffered in
                // memory; the seekable FileStream lets HttpClient set Content-Length.
                using (var fs = File.OpenRead(filePath))
                using (var put = new HttpRequestMessage(HttpMethod.Put, signedUrl) { Content = new StreamContent(fs) })
                {
                    var putResp = await _http.SendAsync(put, ct).ConfigureAwait(false);
                    if (!putResp.IsSuccessStatusCode)
                        return Fail($"S3 upload failed (HTTP {(int)putResp.StatusCode}).");
                }

                // 3c. Finalise the upload.
                var finalizeBody = new JObject { ["uploadKey"] = uploadKey };
                var finResp = await SendAsync(HttpMethod.Post,
                    $"{OssBase}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload",
                    creds.AccessToken, finalizeBody, "application/json", ct).ConfigureAwait(false);
                if (!finResp.ok) return Fail($"Finalise upload failed (HTTP {finResp.status}). {Trim(finResp.body)}");

                // 4. Create the item + first version pointing at the storage object.
                var itemBody = new JObject
                {
                    ["jsonapi"] = new JObject { ["version"] = "1.0" },
                    ["data"] = new JObject
                    {
                        ["type"] = "items",
                        ["attributes"] = new JObject
                        {
                            ["displayName"] = fileName,
                            ["extension"] = new JObject { ["type"] = "items:autodesk.bim360:File", ["version"] = "1.0" }
                        },
                        ["relationships"] = new JObject
                        {
                            ["tip"] = new JObject { ["data"] = new JObject { ["type"] = "versions", ["id"] = "1" } },
                            ["parent"] = new JObject { ["data"] = new JObject { ["type"] = "folders", ["id"] = folderUrn } }
                        }
                    },
                    ["included"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "versions",
                            ["id"] = "1",
                            ["attributes"] = new JObject
                            {
                                ["name"] = fileName,
                                ["extension"] = new JObject { ["type"] = "versions:autodesk.bim360:File", ["version"] = "1.0" }
                            },
                            ["relationships"] = new JObject
                            {
                                ["storage"] = new JObject { ["data"] = new JObject { ["type"] = "objects", ["id"] = objectId } }
                            }
                        }
                    }
                };
                var itemResp = await SendAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/items",
                    creds.AccessToken, itemBody, JsonApi, ct).ConfigureAwait(false);
                if (itemResp.status == 409)
                    return Fail($"A file named '{fileName}' already exists in that folder. Versioning an existing item isn't implemented yet — rename or upload to a different folder.");
                if (!itemResp.ok) return Fail($"Create item failed (HTTP {itemResp.status}). {Trim(itemResp.body)}");

                string itemUrn = JObject.Parse(itemResp.body)["data"]?["id"]?.Value<string>() ?? "";
                StingLog.Info($"AccModelUpload: uploaded '{fileName}' → {itemUrn}");
                return new UploadResult { Ok = true, ItemUrn = itemUrn, Message = $"Uploaded '{fileName}' to ACC." };
            }
            catch (Exception ex)
            {
                StingLog.Error("AccModelUpload.UploadAsync failed", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>Find the project's "Project Files" top folder URN via the Project API.</summary>
        private static async Task<string> ResolveProjectFilesFolderAsync(
            AccCredentials creds, string projectId, CancellationToken ct)
        {
            string hubId = EnsureB(creds.HubId);
            if (string.IsNullOrEmpty(hubId)) return "";
            var resp = await SendAsync(HttpMethod.Get,
                $"{ProjectBase}/hubs/{hubId}/projects/{projectId}/topFolders",
                creds.AccessToken, null, null, ct).ConfigureAwait(false);
            if (!resp.ok) { StingLog.Warn($"topFolders HTTP {resp.status}: {Trim(resp.body)}"); return ""; }

            var data = JObject.Parse(resp.body)["data"] as JArray;
            if (data == null) return "";
            string firstId = "";
            foreach (var f in data)
            {
                string id = f["id"]?.Value<string>() ?? "";
                string name = f["attributes"]?["name"]?.Value<string>()
                              ?? f["attributes"]?["displayName"]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(firstId)) firstId = id;
                if (name.Equals("Project Files", StringComparison.OrdinalIgnoreCase)) return id;
            }
            return firstId; // fall back to the first top folder
        }

        private static async Task<(bool ok, int status, string body)> SendAsync(
            HttpMethod method, string url, string bearer, JObject body, string contentType, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (body != null)
                req.Content = new StringContent(body.ToString(), Encoding.UTF8, contentType ?? "application/json");
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, respBody);
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(0, 300) : s);
        private static UploadResult Fail(string msg) => new UploadResult { Ok = false, Message = msg };
    }
}
