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
    /// Handles large files (multi-part signed-S3 upload, 100 MB parts) and
    /// re-uploads (a duplicate name adds a new VERSION to the existing item
    /// instead of failing).
    ///
    /// CAVEAT: built to the documented APS signatures but NOT verified against a
    /// live ACC project.
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

                // 3. Upload the bytes to OSS (single- or multi-part, signed S3).
                var up = await UploadFileAsync(creds.AccessToken, bucketKey, objectKey, filePath, ct).ConfigureAwait(false);
                if (!up.ok) return Fail(up.err);

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
                {
                    // Item already exists in the folder — add a new VERSION to it
                    // (pointing at the storage object we just uploaded) instead of failing.
                    var ver = await CreateVersionAsync(creds.AccessToken, projectId, folderUrn, fileName, objectId, ct).ConfigureAwait(false);
                    if (!ver.ok) return Fail(ver.err);
                    StingLog.Info($"AccModelUpload: new version of '{fileName}' → {ver.urn}");
                    return new UploadResult { Ok = true, ItemUrn = ver.urn, Message = $"Uploaded a new version of '{fileName}' to ACC." };
                }
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

        // 100 MB per part. S3 allows up to 10,000 parts (min 5 MB each except the
        // last), so this comfortably covers multi-GB models.
        private const long PartSizeBytes = 100L * 1024 * 1024;

        /// <summary>
        /// Upload a file to the OSS object via signed-S3. One PUT for files ≤ one
        /// part (streamed from disk); otherwise request N signed URLs and PUT each
        /// part (one part buffered at a time to bound memory). Finalises with the
        /// uploadKey either way.
        /// </summary>
        private static async Task<(bool ok, string err)> UploadFileAsync(
            string accessToken, string bucketKey, string objectKey, string filePath, CancellationToken ct)
        {
            long size = new FileInfo(filePath).Length;
            int numParts = (int)Math.Max(1, (size + PartSizeBytes - 1) / PartSizeBytes);

            string signUrl = $"{OssBase}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload?minutesExpiration=60"
                             + (numParts > 1 ? $"&parts={numParts}" : "");
            var signResp = await SendAsync(HttpMethod.Get, signUrl, accessToken, null, null, ct).ConfigureAwait(false);
            if (!signResp.ok) return (false, $"Signed-upload request failed (HTTP {signResp.status}). {Trim(signResp.body)}");

            var signJson = JObject.Parse(signResp.body);
            string uploadKey = signJson["uploadKey"]?.Value<string>() ?? "";
            var urls = signJson["urls"] as JArray;
            if (string.IsNullOrEmpty(uploadKey) || urls == null || urls.Count == 0)
                return (false, "Signed-upload response missing uploadKey/urls.");

            if (numParts == 1)
            {
                // Stream the whole file straight from disk (seekable → Content-Length set).
                using var fs = File.OpenRead(filePath);
                using var put = new HttpRequestMessage(HttpMethod.Put, urls[0].Value<string>()) { Content = new StreamContent(fs) };
                var putResp = await _http.SendAsync(put, ct).ConfigureAwait(false);
                if (!putResp.IsSuccessStatusCode) return (false, $"S3 upload failed (HTTP {(int)putResp.StatusCode}).");
            }
            else
            {
                using var fs = File.OpenRead(filePath);
                for (int i = 0; i < urls.Count; i++)
                {
                    long remaining = size - (long)i * PartSizeBytes;
                    int len = (int)Math.Min(PartSizeBytes, remaining);
                    var buffer = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        int n = await fs.ReadAsync(buffer, read, len - read, ct).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }
                    using var put = new HttpRequestMessage(HttpMethod.Put, urls[i].Value<string>()) { Content = new ByteArrayContent(buffer, 0, read) };
                    var putResp = await _http.SendAsync(put, ct).ConfigureAwait(false);
                    if (!putResp.IsSuccessStatusCode) return (false, $"S3 upload part {i + 1}/{urls.Count} failed (HTTP {(int)putResp.StatusCode}).");
                }
            }

            var finResp = await SendAsync(HttpMethod.Post,
                $"{OssBase}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload",
                accessToken, new JObject { ["uploadKey"] = uploadKey }, "application/json", ct).ConfigureAwait(false);
            if (!finResp.ok) return (false, $"Finalise upload failed (HTTP {finResp.status}). {Trim(finResp.body)}");
            return (true, "");
        }

        /// <summary>Add a new version to an existing item (the 409 path), pointing at the just-uploaded storage object.</summary>
        private static async Task<(bool ok, string urn, string err)> CreateVersionAsync(
            string accessToken, string projectId, string folderUrn, string fileName, string objectId, CancellationToken ct)
        {
            string itemId = await FindItemIdAsync(accessToken, projectId, folderUrn, fileName, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(itemId))
                return (false, "", $"'{fileName}' exists but its item couldn't be located in the folder for versioning.");

            var body = new JObject
            {
                ["jsonapi"] = new JObject { ["version"] = "1.0" },
                ["data"] = new JObject
                {
                    ["type"] = "versions",
                    ["attributes"] = new JObject
                    {
                        ["name"] = fileName,
                        ["extension"] = new JObject { ["type"] = "versions:autodesk.bim360:File", ["version"] = "1.0" }
                    },
                    ["relationships"] = new JObject
                    {
                        ["item"] = new JObject { ["data"] = new JObject { ["type"] = "items", ["id"] = itemId } },
                        ["storage"] = new JObject { ["data"] = new JObject { ["type"] = "objects", ["id"] = objectId } }
                    }
                }
            };
            var resp = await SendAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/versions",
                accessToken, body, JsonApi, ct).ConfigureAwait(false);
            if (!resp.ok) return (false, "", $"Create version failed (HTTP {resp.status}). {Trim(resp.body)}");
            string urn = JObject.Parse(resp.body)["data"]?["id"]?.Value<string>() ?? "";
            return (true, urn, "");
        }

        /// <summary>Locate an existing item id by display name within a folder.</summary>
        private static async Task<string> FindItemIdAsync(
            string accessToken, string projectId, string folderUrn, string fileName, CancellationToken ct)
        {
            var resp = await SendAsync(HttpMethod.Get,
                $"{DataBase}/projects/{projectId}/folders/{Uri.EscapeDataString(folderUrn)}/contents",
                accessToken, null, null, ct).ConfigureAwait(false);
            if (!resp.ok) { StingLog.Warn($"folder contents HTTP {resp.status}: {Trim(resp.body)}"); return ""; }

            var data = JObject.Parse(resp.body)["data"] as JArray;
            if (data == null) return "";
            foreach (var it in data)
            {
                if ((it["type"]?.Value<string>() ?? "") != "items") continue;
                string dn = it["attributes"]?["displayName"]?.Value<string>() ?? "";
                if (dn.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return it["id"]?.Value<string>() ?? "";
            }
            return "";
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
