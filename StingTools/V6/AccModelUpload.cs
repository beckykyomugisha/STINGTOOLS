using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>What an upload should do beyond "put this file in ACC".</summary>
    public sealed class AccUploadOptions
    {
        /// <summary>ISO 19650 suitability of the file. With <see cref="CdeFolders"/> it decides the
        /// folder: S0 → WIP, S1–S7 → SHARED, A/B/CR → PUBLISHED, AB/AR → ARCHIVE.</summary>
        public string Suitability { get; set; } = string.Empty;
        /// <summary>CDE state → folder URN (acc_settings.json "cdeFolders"). When set, an
        /// upload whose state has no folder is REFUSED rather than sent elsewhere.</summary>
        public IReadOnlyDictionary<string, string> CdeFolders { get; set; }
        /// <summary>Stamp ISO 19650 metadata on the uploaded version as ACC custom attributes.</summary>
        public AccDocMetadataInput Metadata { get; set; }
        /// <summary>Create missing attribute definitions on the folder (off by default: they
        /// are project-wide admin configuration).</summary>
        public bool CreateMissingAttributes { get; set; }
    }

    /// <summary>
    /// Live upload to Autodesk Construction Cloud via the APS Data Management API:
    ///
    ///   1. choose the folder — by CDE state when the project maps them, else the project's
    ///      folder setting, else the project's "Project Files" top folder BY NAME. Never the
    ///      first folder offered: a CDE upload into an unintended (possibly restricted) folder
    ///      is an issue-control error, not a convenience.
    ///   2. create a storage object in that folder
    ///   3. upload the bytes to OSS with signed S3 URLs, in 16 MB parts, at most 25 URLs per
    ///      request (APS limit, `firstPart` 1-based), each part retried on its own, and URLs
    ///      re-requested with the same uploadKey when they expire (a 403 on the PUT). A dropped
    ///      part no longer restarts a multi-GB upload, and a slow link no longer hits a 100 s
    ///      whole-exchange timeout: every part carries a timeout sized to the part.
    ///   4. create the item (first version) — or, if the name exists, a new VERSION
    ///   5. optionally stamp ISO 19650 custom attributes on the new version.
    ///
    /// Tokens come from <see cref="AccIssueSync"/>; retries from <see cref="AccHttp"/>.
    /// </summary>
    public static class AccModelUpload
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point the client at a loopback listener. Pass null to restore.</summary>
        internal static void OverrideHostForTests(string host)
        {
            _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');
            // The token endpoint too: a 401 now triggers one forced refresh, and a test must
            // never reach the real Autodesk sign-in service.
            AccIssueSync.OverrideHostForTests(host);
        }

        private static string DataBase    => _host + "/data/v1";
        private static string ProjectBase => _host + "/project/v1";
        private static string OssBase     => _host + "/oss/v2";
        private const string JsonApi     = "application/vnd.api+json";

        /// <summary>16 MB. S3 needs ≥ 5 MB for every part but the last and allows 10,000 parts,
        /// so this covers files to ~160 GB while keeping a re-sent part cheap on a slow link.</summary>
        internal static long PartSizeBytes = 16L * 1024 * 1024;

        /// <summary>APS returns at most 25 signed URLs per request.</summary>
        internal const int MaxUrlsPerRequest = 25;

        /// <summary>Slowest link a part must survive: 64 KB/s (~0.5 Mbit/s) plus a margin.</summary>
        private static TimeSpan PartTimeout(long bytes) =>
            TimeSpan.FromSeconds(Math.Max(120, bytes / (64 * 1024) + 60));

        /// <summary>The outcome of an upload. <see cref="Status"/>/<see cref="HttpStatus"/> say
        /// WHICH kind of failure, with the same classification every ACC read uses.</summary>
        public sealed class UploadResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
            public string ItemUrn { get; set; } = "";
            /// <summary>The version created (needed for custom attributes and transmittals).</summary>
            public string VersionUrn { get; set; } = "";
            /// <summary>The folder the file went to, and why that folder.</summary>
            public string FolderUrn { get; set; } = "";
            public string FolderReason { get; set; } = "";
            /// <summary>What happened to the ISO 19650 attributes; empty when none were requested.
            /// A metadata failure does not undo an upload that happened — it is reported here.</summary>
            public string MetadataNote { get; set; } = "";
            public bool MetadataComplete { get; set; } = true;
            public AccFetchStatus Status { get; set; } = AccFetchStatus.Ok;
            public int HttpStatus { get; set; }
            public string Remedy => Ok ? "" : AccCommandOutcome.Remedy(Status);
        }

        private static string EnsureB(string id) => AccIds.ForDataManagement(id);

        public static Task<UploadResult> UploadAsync(AccCredentials creds, string filePath, CancellationToken ct = default)
            => UploadAsync(creds, filePath, null, ct);

        public static async Task<UploadResult> UploadAsync(
            AccCredentials creds, string filePath, AccUploadOptions options, CancellationToken ct = default)
        {
            try
            {
                if (creds == null) return Fail("No credentials.");
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return Fail("Pick a file to upload first.");
                if (string.IsNullOrWhiteSpace(creds.ProjectId))
                    return Fail("Set the ACC project first (BIM Coordination Center > ACC > Discover).");

                var auth = await AccIssueSync.EnsureAuthDetailedAsync(creds).ConfigureAwait(false);
                if (!auth.Ok)
                    return new UploadResult
                    {
                        Ok = false,
                        Message = "Not authenticated — Sign in with Autodesk first. (" + auth.Detail + ")",
                        Status = auth.Status,
                    };

                string projectId = EnsureB(creds.ProjectId);
                string fileName = Path.GetFileName(filePath);

                // 1. Folder.
                var folder = await ResolveFolderAsync(creds, projectId, options, ct).ConfigureAwait(false);
                if (!folder.ok) return folder.fail;
                string folderUrn = folder.urn;

                // 2. Storage object.
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
                var storageResp = await SendJsonAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/storage",
                    creds, storageBody, JsonApi, idempotent: false, ct).ConfigureAwait(false);
                if (!storageResp.IsSuccess) return Fail($"Create storage failed (HTTP {storageResp.Status}). {Trim(storageResp.Body)}", storageResp);
                string objectId = JObject.Parse(storageResp.Body)["data"]?["id"]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(objectId)) return Fail("Storage response had no object id.");

                // urn:adsk.objects:os.object:{bucketKey}/{objectKey}
                int lastColon = objectId.LastIndexOf(':');
                string bucketAndKey = lastColon >= 0 ? objectId.Substring(lastColon + 1) : objectId;
                int slash = bucketAndKey.IndexOf('/');
                if (slash < 0) return Fail($"Unexpected storage object id: {objectId}");
                string bucketKey = bucketAndKey.Substring(0, slash);
                string objectKey = bucketAndKey.Substring(slash + 1);

                // 3. Bytes.
                var up = await UploadFileAsync(creds, bucketKey, objectKey, filePath, ct).ConfigureAwait(false);
                if (!up.ok) return up.fail;

                // 4. Item + first version, or a new version.
                var result = await CreateItemOrVersionAsync(creds, projectId, folderUrn, fileName, objectId, ct).ConfigureAwait(false);
                if (!result.Ok) return result;
                result.FolderUrn = folderUrn;
                result.FolderReason = folder.reason;

                // 5. ISO 19650 attributes.
                if (options?.Metadata != null)
                    await StampMetadataAsync(creds, projectId, folderUrn, result, options).ConfigureAwait(false);

                StingLog.Info($"AccModelUpload: '{fileName}' → {result.VersionUrn} in {folderUrn} ({folder.reason})");
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Fail("Upload cancelled.");
            }
            catch (Exception ex)
            {
                StingLog.Error("AccModelUpload.UploadAsync failed", ex);
                return Fail(ex.Message);
            }
        }

        // ── 1. folder ─────────────────────────────────────────────────────────

        private static async Task<(bool ok, string urn, string reason, UploadResult fail)> ResolveFolderAsync(
            AccCredentials creds, string projectId, AccUploadOptions options, CancellationToken ct)
        {
            var map = options?.CdeFolders;
            if (map != null && map.Count > 0)
            {
                var route = AccCdeRouting.Resolve(options.Suitability, map);
                if (!route.IsRouted)
                    return (false, "", "", Fail("Not uploaded: " + route.Detail +
                        ". The project maps its CDE states to ACC folders, so a file whose state has no folder is not sent anywhere else."));
                return (true, route.FolderUrn, $"CDE state {route.CdeState} (suitability {route.SuitabilityCode})", null);
            }

            string configured = creds.FolderUrn?.Trim() ?? "";
            if (configured.Length > 0) return (true, configured, "the project's upload folder setting", null);

            if (string.IsNullOrWhiteSpace(creds.HubId))
                return (false, "", "", Fail("No upload folder is set and the ACC hub is unknown, so 'Project Files' cannot be located. " +
                    "Use Discover on the ACC card (it records the hub), or set the upload folder / CDE folders in the project's ACC settings."));

            var resp = await SendJsonAsync(HttpMethod.Get, $"{ProjectBase}/hubs/{EnsureB(creds.HubId)}/projects/{projectId}/topFolders",
                creds, null, null, idempotent: true, ct).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return (false, "", "", Fail($"Listing the project's top folders failed (HTTP {resp.Status}). {Trim(resp.Body)}", resp));

            var data = JObject.Parse(resp.Body)["data"] as JArray ?? new JArray();
            var names = new List<string>();
            foreach (var f in data)
            {
                string id = f["id"]?.Value<string>() ?? "";
                string name = f["attributes"]?["name"]?.Value<string>()
                              ?? f["attributes"]?["displayName"]?.Value<string>() ?? "";
                names.Add(name);
                if (name.Equals("Project Files", StringComparison.OrdinalIgnoreCase)) return (true, id, "the project's 'Project Files' folder", null);
            }
            return (false, "", "", Fail("The ACC project has no 'Project Files' top folder and no upload folder is configured; " +
                "nothing was uploaded rather than guessing. Top folders: " + string.Join(", ", names.Select(n => $"'{n}'")) +
                ". Set the upload folder or the CDE folders in the project's ACC settings."));
        }

        // ── 3. bytes ──────────────────────────────────────────────────────────

        private static async Task<(bool ok, UploadResult fail)> UploadFileAsync(
            AccCredentials creds, string bucketKey, string objectKey, string filePath, CancellationToken ct)
        {
            long size = new FileInfo(filePath).Length;
            int numParts = (int)Math.Max(1, (size + PartSizeBytes - 1) / PartSizeBytes);
            string signBase = $"{OssBase}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload";
            string uploadKey = null;

            using var fs = File.OpenRead(filePath);
            for (int batchStart = 1; batchStart <= numParts; batchStart += MaxUrlsPerRequest)
            {
                int count = Math.Min(MaxUrlsPerRequest, numParts - batchStart + 1);
                var sign = await SignAsync(creds, signBase, batchStart, count, uploadKey, ct).ConfigureAwait(false);
                if (!sign.ok) return (false, sign.fail);
                uploadKey = sign.uploadKey;
                var urls = sign.urls;

                for (int i = 0; i < count; i++)
                {
                    int partNumber = batchStart + i;   // 1-based
                    long offset = (long)(partNumber - 1) * PartSizeBytes;
                    int len = (int)Math.Min(PartSizeBytes, size - offset);
                    var buffer = new byte[len];
                    fs.Seek(offset, SeekOrigin.Begin);
                    int read = 0;
                    while (read < len)
                    {
                        int n = await fs.ReadAsync(buffer, read, len - read, ct).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }

                    bool done = false;
                    for (int renew = 0; renew < 3 && !done; renew++)
                    {
                        string url = urls[i];
                        var put = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Put, url)
                        { Content = new ByteArrayContent(buffer, 0, read) },
                            creds: null, idempotent: true, ct: ct, timeout: PartTimeout(read)).ConfigureAwait(false);
                        if (put.IsSuccess) { done = true; break; }
                        if (put.Status == 403)
                        {
                            // The signed URLs expired. Ask again for the rest of this batch with the
                            // same uploadKey; the parts already sent stay sent.
                            StingLog.Warn($"AccModelUpload: signed URLs expired at part {partNumber}/{numParts} — renewing");
                            var again = await SignAsync(creds, signBase, partNumber, count - i, uploadKey, ct).ConfigureAwait(false);
                            if (!again.ok) return (false, again.fail);
                            for (int k = 0; k < again.urls.Count; k++) urls[i + k] = again.urls[k];
                            continue;
                        }
                        return (false, Fail($"S3 upload of part {partNumber}/{numParts} failed after {put.Attempts} attempt(s): {put.Describe()}", put));
                    }
                    if (!done) return (false, Fail($"S3 upload of part {partNumber}/{numParts} failed: the signed URLs kept expiring."));
                }
            }

            var fin = await SendJsonAsync(HttpMethod.Post, signBase, creds, new JObject { ["uploadKey"] = uploadKey },
                "application/json", idempotent: true, ct).ConfigureAwait(false);
            if (!fin.IsSuccess) return (false, Fail($"Finalise upload failed (HTTP {fin.Status}). {Trim(fin.Body)}", fin));
            return (true, null);
        }

        private static async Task<(bool ok, string uploadKey, List<string> urls, UploadResult fail)> SignAsync(
            AccCredentials creds, string signBase, int firstPart, int parts, string uploadKey, CancellationToken ct)
        {
            string url = $"{signBase}?minutesExpiration=60&parts={parts}&firstPart={firstPart}" +
                         (string.IsNullOrEmpty(uploadKey) ? "" : "&uploadKey=" + Uri.EscapeDataString(uploadKey));
            var resp = await SendJsonAsync(HttpMethod.Get, url, creds, null, null, idempotent: true, ct).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return (false, null, null, Fail($"Signed-upload request failed (HTTP {resp.Status}). {Trim(resp.Body)}", resp));
            var j = JObject.Parse(resp.Body);
            string key = j["uploadKey"]?.Value<string>() ?? uploadKey ?? "";
            var urls = (j["urls"] as JArray)?.Select(u => u.Value<string>()).ToList() ?? new List<string>();
            if (string.IsNullOrEmpty(key) || urls.Count < parts)
                return (false, null, null, Fail($"Signed-upload response missing uploadKey or URLs (asked for {parts}, got {urls.Count})."));
            return (true, key, urls, null);
        }

        // ── 4. item / version ─────────────────────────────────────────────────

        private static async Task<UploadResult> CreateItemOrVersionAsync(
            AccCredentials creds, string projectId, string folderUrn, string fileName, string objectId, CancellationToken ct)
        {
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
            var itemResp = await SendJsonAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/items",
                creds, itemBody, JsonApi, idempotent: false, ct).ConfigureAwait(false);

            if (itemResp.Status == 409)
            {
                // The name exists in the folder: add a VERSION to that item instead of failing.
                string itemId = await FindItemIdAsync(creds, projectId, folderUrn, fileName, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(itemId))
                    return Fail($"'{fileName}' exists but its item couldn't be located in the folder for versioning.");
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
                var ver = await SendJsonAsync(HttpMethod.Post, $"{DataBase}/projects/{projectId}/versions",
                    creds, body, JsonApi, idempotent: false, ct).ConfigureAwait(false);
                if (!ver.IsSuccess) return Fail($"Create version failed (HTTP {ver.Status}). {Trim(ver.Body)}", ver);
                string vUrn = JObject.Parse(ver.Body)["data"]?["id"]?.Value<string>() ?? "";
                return new UploadResult { Ok = true, ItemUrn = itemId, VersionUrn = vUrn, Message = $"Uploaded a new version of '{fileName}' to ACC." };
            }
            if (!itemResp.IsSuccess) return Fail($"Create item failed (HTTP {itemResp.Status}). {Trim(itemResp.Body)}", itemResp);

            var j = JObject.Parse(itemResp.Body);
            string itemUrn = j["data"]?["id"]?.Value<string>() ?? "";
            string versionUrn = (j["included"] as JArray)?.FirstOrDefault(t => (string)t["type"] == "versions")?["id"]?.Value<string>()
                                ?? j["data"]?["relationships"]?["tip"]?["data"]?["id"]?.Value<string>() ?? "";
            return new UploadResult { Ok = true, ItemUrn = itemUrn, VersionUrn = versionUrn, Message = $"Uploaded '{fileName}' to ACC." };
        }

        /// <summary>Locate an item by display name, following folder-contents pagination
        /// (links.next): a folder of more than 200 files used to report "exists but couldn't be
        /// located".</summary>
        private static async Task<string> FindItemIdAsync(
            AccCredentials creds, string projectId, string folderUrn, string fileName, CancellationToken ct)
        {
            string url = $"{DataBase}/projects/{projectId}/folders/{Uri.EscapeDataString(folderUrn)}/contents" +
                         $"?filter[type]=items&filter[displayName]={Uri.EscapeDataString(fileName)}&page[limit]=200";
            for (int page = 0; page < 100 && !string.IsNullOrEmpty(url); page++)
            {
                var resp = await SendJsonAsync(HttpMethod.Get, url, creds, null, null, idempotent: true, ct).ConfigureAwait(false);
                if (!resp.IsSuccess) { StingLog.Warn($"folder contents HTTP {resp.Status}: {Trim(resp.Body)}"); return ""; }
                var doc = JObject.Parse(resp.Body);
                foreach (var it in doc["data"] as JArray ?? new JArray())
                {
                    if ((it["type"]?.Value<string>() ?? "") != "items") continue;
                    string dn = it["attributes"]?["displayName"]?.Value<string>() ?? "";
                    if (dn.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return it["id"]?.Value<string>() ?? "";
                }
                url = doc["links"]?["next"]?["href"]?.Value<string>();
            }
            return "";
        }

        // ── 5. ISO 19650 attributes ───────────────────────────────────────────

        private static async Task StampMetadataAsync(AccCredentials creds, string projectId, string folderUrn,
            UploadResult result, AccUploadOptions options)
        {
            if (string.IsNullOrEmpty(result.VersionUrn))
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: ACC returned no version id.";
                return;
            }
            var values = AccDocsAttributeSet.Build(options.Metadata);
            if (!values.IsClean)
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: " + string.Join("; ", values.Problems);
                return;
            }
            var defs = await AccDocsMetadata.EnsureDefinitionsAsync(creds.AccessToken, projectId, folderUrn,
                options.CreateMissingAttributes).ConfigureAwait(false);
            if (!defs.Succeeded)
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: " + defs.Detail;
                return;
            }
            var usableNames = new HashSet<string>(defs.Value.Usable.Select(d => d.Name), StringComparer.Ordinal);
            var toWrite = values.Values.Where(kv => usableNames.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            var skipped = values.Values.Keys.Where(k => !usableNames.Contains(k)).ToList();

            if (toWrite.Count == 0)
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: the folder has none of the STING attribute definitions (" +
                                      string.Join(", ", defs.Value.Missing) + "). Create them in ACC Docs, or allow STING to.";
                return;
            }
            var write = await AccDocsMetadata.SetVersionAttributesAsync(creds.AccessToken, projectId, result.VersionUrn,
                toWrite, defs.Value.Usable).ConfigureAwait(false);
            if (!write.Succeeded)
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: " + write.Detail;
                return;
            }
            result.MetadataComplete = skipped.Count == 0 && write.Value.IsConfirmed;
            result.MetadataNote = $"ISO 19650 attributes written: {string.Join(", ", write.Value.Written.Keys)}" +
                                  (skipped.Count > 0 ? $". Not defined on the folder, so not written: {string.Join(", ", skipped)}" : "") +
                                  (write.Value.IsConfirmed ? "" : $". Not confirmed by ACC: {string.Join(", ", write.Value.NotConfirmed)}");
        }

        // ── transport ─────────────────────────────────────────────────────────

        private static Task<AccHttpResponse> SendJsonAsync(HttpMethod method, string url, AccCredentials creds,
            JObject body, string contentType, bool idempotent, CancellationToken ct)
        {
            string payload = body?.ToString();
            return AccHttp.SendAsync(() =>
            {
                var req = new HttpRequestMessage(method, url);
                if (payload != null) req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                if (payload != null && contentType != null)
                    req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                return req;
            }, creds, idempotent, ct);
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(0, 300) : s);

        /// <summary>A failure with no HTTP exchange behind it (no file, no credentials,
        /// unparseable id, a refused folder choice).</summary>
        private static UploadResult Fail(string msg) =>
            new UploadResult { Ok = false, Message = msg, Status = AccFetchStatus.TransportFailed, HttpStatus = 0 };

        /// <summary>A failure Autodesk answered, classified the way every ACC read is.</summary>
        private static UploadResult Fail(string msg, AccHttpResponse resp) => new UploadResult
        {
            Ok = false,
            Message = msg,
            Status = resp.Classify(),
            HttpStatus = resp.Status,
        };
    }
}
