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
        /// <summary>The project's attribute names (acc_settings.json "docsAttributeNames");
        /// null = the defaults.</summary>
        public AccAttributeNames AttributeNames { get; set; }
        /// <summary>The project uses the 7-field ISO 19650 name: a file whose name ends in
        /// "-{Suitability}-{Revision}" is REFUSED (every revision would become a new ACC item).</summary>
        public bool SevenFieldNaming { get; set; }
        /// <summary>Read the target folder's ACC naming standard and validate the name before
        /// sending bytes. A name that does not fit is refused; a standard that cannot be read or
        /// interpreted is reported in <see cref="AccModelUpload.UploadResult.NamingNote"/>.</summary>
        public bool CheckNamingStandard { get; set; }
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
            /// <summary>Metadata stamping was asked for on this upload.</summary>
            public bool MetadataRequested { get; set; }
            /// <summary>A11: the upload happened but the ISO 19650 attributes asked for are not all
            /// on the version (not written, partly written, or not confirmed by ACC). A caller
            /// that reports an upload must report this too; <see cref="MetadataNote"/> says why.</summary>
            public bool MetadataIncomplete => Ok && MetadataRequested && !MetadataComplete;
            /// <summary>What the naming-standard check found; empty when it was not run.</summary>
            public string NamingNote { get; set; } = "";
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

                // 1b. The name, before any bytes: a revision-bearing name in a 7-field project,
                // or a name the folder's naming standard would reject, is refused here.
                string namingNote = "";
                if (options != null)
                {
                    bool embeds = AccNamingStandard.NameEmbedsStatus(fileName, options.Suitability, options.Metadata?.Revision);
                    if (embeds && options.SevenFieldNaming)
                        return Fail($"Not uploaded: '{fileName}' ends in suitability and revision, but this project names files " +
                                    "with the 7-field ISO 19650 name (acc_settings.json \"fileNamingFields\"). ACC matches items by " +
                                    "name, so every revision would become a NEW item instead of a new version. Export with the " +
                                    "7-field name — suitability and revision travel as ACC attributes.");
                    if (options.CheckNamingStandard)
                    {
                        var naming = await CheckNamingStandardAsync(creds, projectId, folderUrn, fileName, embeds, ct).ConfigureAwait(false);
                        if (naming.refusal != null) return naming.refusal;
                        namingNote = naming.note;
                    }
                }

                // 2 + 3. Storage object and bytes — resumed when an earlier attempt at this exact
                // upload (same file, size, time stamp, project and folder) got part of the way.
                var fi = new FileInfo(filePath);
                string rkey = AccUploadResume.KeyFor(filePath, fi.Length, fi.LastWriteTimeUtc, projectId, folderUrn);
                var state = AccUploadResume.Load(rkey, PartSizeBytes, DateTime.UtcNow);
                bool resumed = state != null;
                if (resumed)
                    StingLog.Info($"AccModelUpload: resuming '{fileName}' at part {state.PartsCompleted + 1}/{state.TotalParts}" +
                                  (state.Finalised ? " (bytes already complete)" : ""));

                var bytes = await StoreBytesAsync(creds, projectId, folderUrn, fileName, filePath, rkey, state, ct).ConfigureAwait(false);
                if (!bytes.ok && resumed && bytes.resumeRejected)
                {
                    // Autodesk no longer knows the earlier upload session. Start again, once,
                    // from the first byte — never leave a half-written object behind as "done".
                    StingLog.Warn("AccModelUpload: the earlier upload session has expired — starting again from the first part");
                    AccUploadResume.Delete(rkey);
                    bytes = await StoreBytesAsync(creds, projectId, folderUrn, fileName, filePath, rkey, null, ct).ConfigureAwait(false);
                }
                if (!bytes.ok) return bytes.fail;
                string objectId = bytes.objectId;

                // 4. Item + first version, or a new version.
                var result = await CreateItemOrVersionAsync(creds, projectId, folderUrn, fileName, objectId, ct).ConfigureAwait(false);
                if (!result.Ok)
                {
                    result.Message += " The file's bytes are already in ACC storage; running the upload again resumes from here.";
                    return result;
                }
                AccUploadResume.Delete(rkey);
                if (resumed) result.Message += " (resumed an earlier, interrupted upload)";
                result.FolderUrn = folderUrn;
                result.FolderReason = folder.reason;
                result.NamingNote = namingNote;

                // 5. ISO 19650 attributes. The file is in ACC by now, so nothing here may turn
                //    the upload into a failure: a stamping problem - including one that throws
                //    - is recorded on the result (MetadataRequested && !MetadataComplete) for
                //    the caller to report, never swallowed and never promoted to "upload failed".
                if (options?.Metadata != null)
                {
                    result.MetadataRequested = true;
                    try
                    {
                        // AccDocsMetadata sends the access token as it is (no refresh of its
                        // own - A11, deferred). A large upload can outlive the token, so renew
                        // a stale one first; a refusal is reported rather than sent to 401.
                        var reAuth = await AccIssueSync.EnsureAuthDetailedAsync(creds).ConfigureAwait(false);
                        if (!reAuth.Ok)
                        {
                            result.MetadataComplete = false;
                            result.MetadataNote = "ISO 19650 attributes NOT written: the Autodesk sign-in could not be renewed " +
                                                  "after the upload - " + reAuth.Detail;
                        }
                        else
                            await StampMetadataAsync(creds, projectId, folderUrn, result, options).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result.MetadataComplete = false;
                        result.MetadataNote = "ISO 19650 attributes NOT written: " + ex.Message;
                        StingLog.Warn("AccModelUpload: metadata stamping threw after a successful upload: " + ex.Message);
                    }
                }

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

        // ── 1b. naming standard ───────────────────────────────────────────────

        /// <summary>
        /// Read the folder's naming standard(s) and validate the name. Refuses (returns a failed
        /// UploadResult) only on what is known: a name with the wrong number of fields, or a
        /// suitability/revision-bearing name going into a folder that enforces any standard.
        /// A folder or standard that cannot be read, or a standard that cannot be interpreted,
        /// is reported in the note and the upload proceeds — ACC itself remains the judge.
        /// </summary>
        private static async Task<(UploadResult refusal, string note)> CheckNamingStandardAsync(
            AccCredentials creds, string projectId, string folderUrn, string fileName, bool embedsStatus, CancellationToken ct)
        {
            var folderResp = await SendJsonAsync(HttpMethod.Get,
                $"{DataBase}/projects/{projectId}/folders/{Uri.EscapeDataString(folderUrn)}",
                creds, null, null, idempotent: true, ct).ConfigureAwait(false);
            if (!folderResp.IsSuccess)
                return (null, $"Naming standard NOT checked: reading the folder failed (HTTP {folderResp.Status}).");
            var ids = AccNamingStandard.ParseFolderNamingStandardIds(folderResp.Body);
            if (ids == null)
                return (null, "Naming standard NOT checked: the folder record was not in the expected shape.");
            if (ids.Count == 0)
                return (null, "The target folder enforces no ACC naming standard.");

            if (embedsStatus)
                return (Fail($"Not uploaded: the target folder enforces an ACC naming standard, and '{fileName}' ends in " +
                             "suitability and revision. Those are metadata in ISO 19650 (sent as ACC attributes); a name that " +
                             "carries them makes every revision a new ACC item. Export with the 7-field name."), null);

            var notes = new List<string>();
            string docsProject = AccDocsMetadata.DocsProjectId(projectId);
            foreach (string id in ids)
            {
                var sresp = await SendJsonAsync(HttpMethod.Get,
                    $"{_host}/bim360/docs/v1/projects/{Uri.EscapeDataString(docsProject)}/naming-standards/{Uri.EscapeDataString(id)}",
                    creds, null, null, idempotent: true, ct).ConfigureAwait(false);
                if (!sresp.IsSuccess)
                {
                    notes.Add($"naming standard {id} could not be read (HTTP {sresp.Status}), so the name was NOT validated against it");
                    continue;
                }
                var spec = AccNamingStandard.ParseStandard(sresp.Body, id);
                var check = AccNamingStandard.Validate(spec, fileName);
                if (check.Conforms == false)
                    return (Fail("Not uploaded: " + check.Detail + ". ACC would reject it, or file it outside the standard."), null);
                notes.Add(check.Detail);
                notes.AddRange(check.Warnings);
            }
            return (null, "Naming standard: " + string.Join("; ", notes) + ".");
        }

        // ── 2 + 3. storage object and bytes (resumable) ───────────────────────

        private static async Task<(bool ok, string objectId, bool resumeRejected, UploadResult fail)> StoreBytesAsync(
            AccCredentials creds, string projectId, string folderUrn, string fileName, string filePath,
            string resumeKey, AccUploadResumeState state, CancellationToken ct)
        {
            var fi = new FileInfo(filePath);
            if (state == null)
            {
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
                if (!storageResp.IsSuccess)
                    return (false, "", false, Fail($"Create storage failed (HTTP {storageResp.Status}). {Trim(storageResp.Body)}", storageResp));
                string oid = JObject.Parse(storageResp.Body)["data"]?["id"]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(oid)) return (false, "", false, Fail("Storage response had no object id."));

                // urn:adsk.objects:os.object:{bucketKey}/{objectKey}
                int lastColon = oid.LastIndexOf(':');
                string bucketAndKey = lastColon >= 0 ? oid.Substring(lastColon + 1) : oid;
                int slash = bucketAndKey.IndexOf('/');
                if (slash < 0) return (false, "", false, Fail($"Unexpected storage object id: {oid}"));

                state = new AccUploadResumeState
                {
                    Key = resumeKey,
                    FilePath = Path.GetFullPath(filePath),
                    FileSize = fi.Length,
                    FileWriteUtc = fi.LastWriteTimeUtc,
                    ProjectId = projectId,
                    FolderUrn = folderUrn,
                    ObjectId = oid,
                    BucketKey = bucketAndKey.Substring(0, slash),
                    ObjectKey = bucketAndKey.Substring(slash + 1),
                    PartSize = PartSizeBytes,
                    TotalParts = (int)Math.Max(1, (fi.Length + PartSizeBytes - 1) / PartSizeBytes),
                    StartedUtc = DateTime.UtcNow,
                };
                AccUploadResume.Save(state);
            }

            if (!state.Finalised)
            {
                var up = await UploadPartsAsync(creds, state, filePath, ct).ConfigureAwait(false);
                if (!up.ok) return (false, "", up.resumeRejected, up.fail);
            }
            return (true, state.ObjectId, false, null);
        }

        /// <summary>
        /// Send parts PartsCompleted+1..TotalParts with signed S3 URLs (≤ 25 per request,
        /// firstPart 1-based), recording each confirmed part so an interruption resumes after
        /// it; then complete the upload. <c>resumeRejected</c> is true when Autodesk refused
        /// the RESUMED uploadKey itself (the session expired), so the caller can restart.
        /// </summary>
        private static async Task<(bool ok, bool resumeRejected, UploadResult fail)> UploadPartsAsync(
            AccCredentials creds, AccUploadResumeState state, string filePath, CancellationToken ct)
        {
            long size = state.FileSize;
            int numParts = state.TotalParts;
            long partSize = state.PartSize;
            string signBase = $"{OssBase}/buckets/{state.BucketKey}/objects/{Uri.EscapeDataString(state.ObjectKey)}/signeds3upload";
            bool resuming = !string.IsNullOrEmpty(state.UploadKey);

            using var fs = File.OpenRead(filePath);
            for (int batchStart = state.PartsCompleted + 1; batchStart <= numParts; batchStart += MaxUrlsPerRequest)
            {
                int count = Math.Min(MaxUrlsPerRequest, numParts - batchStart + 1);
                var sign = await SignAsync(creds, signBase, batchStart, count, state.UploadKey, ct).ConfigureAwait(false);
                if (!sign.ok)
                {
                    bool rejected = resuming && sign.fail.HttpStatus >= 400 && sign.fail.HttpStatus < 500 && sign.fail.HttpStatus != 401 && sign.fail.HttpStatus != 429;
                    return (false, rejected, sign.fail);
                }
                resuming = false;
                if (string.IsNullOrEmpty(state.UploadKey)) { state.UploadKey = sign.uploadKey; AccUploadResume.Save(state); }
                var urls = sign.urls;

                for (int i = 0; i < count; i++)
                {
                    int partNumber = batchStart + i;   // 1-based
                    long offset = (long)(partNumber - 1) * partSize;
                    int len = (int)Math.Min(partSize, size - offset);
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
                            var again = await SignAsync(creds, signBase, partNumber, count - i, state.UploadKey, ct).ConfigureAwait(false);
                            if (!again.ok) return (false, false, again.fail);
                            for (int k = 0; k < again.urls.Count; k++) urls[i + k] = again.urls[k];
                            continue;
                        }
                        return (false, false, Fail($"S3 upload of part {partNumber}/{numParts} failed after {put.Attempts} attempt(s): {put.Describe()}. " +
                                                   $"{state.PartsCompleted} of {numParts} parts are uploaded — run the upload again within " +
                                                   $"{AccUploadResume.MaxAge.TotalHours:F0} h to resume from part {state.PartsCompleted + 1}.", put));
                    }
                    if (!done) return (false, false, Fail($"S3 upload of part {partNumber}/{numParts} failed: the signed URLs kept expiring."));
                    state.PartsCompleted = partNumber;
                    AccUploadResume.Save(state);
                }
            }

            var fin = await SendJsonAsync(HttpMethod.Post, signBase, creds, new JObject { ["uploadKey"] = state.UploadKey },
                "application/json", idempotent: true, ct).ConfigureAwait(false);
            if (!fin.IsSuccess) return (false, false, Fail($"Finalise upload failed (HTTP {fin.Status}). {Trim(fin.Body)}", fin));
            state.Finalised = true;
            AccUploadResume.Save(state);
            return (true, false, null);
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
                var found = await FindItemAsync(creds, projectId, folderUrn, fileName, ct).ConfigureAwait(false);
                // AUT-1: say WHY the existing item was not found - an HTTP failure, a search that
                // stopped at its page cap, or a genuine miss - instead of one undifferentiated line.
                if (found.Failure != null)
                    return Fail($"'{fileName}' already exists in the folder, but listing the folder to find it failed " +
                                $"(HTTP {found.Failure.Status}). {Trim(found.Failure.Body)}", found.Failure);
                string itemId = found.ItemId;
                if (string.IsNullOrEmpty(itemId))
                    return Fail(found.Incomplete
                        ? $"'{fileName}' already exists in the folder, but the folder listing was INCOMPLETE ({found.Pages} pages read, " +
                          "the limit) and the item was not among them - nothing was uploaded as a new version."
                        : $"'{fileName}' exists but its item couldn't be located in the folder for versioning.");
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

        /// <summary>AUT-1: what a by-name item search found. Exactly one of: an id, a failure
        /// (the HTTP answer), or neither - in which case <see cref="Incomplete"/> says whether
        /// the search stopped at its page cap rather than at the end of the folder.</summary>
        internal sealed class ItemLookup
        {
            public string ItemId { get; set; } = "";
            public AccHttpResponse Failure { get; set; }
            public bool Incomplete { get; set; }
            public int Pages { get; set; }
        }

        internal const int ItemSearchMaxPages = 100;

        /// <summary>Locate an item by display name, following folder-contents pagination
        /// (links.next): a folder of more than 200 files used to report "exists but couldn't be
        /// located". AUT-1: a failed page is returned, not logged and dropped, and a search that
        /// stops at <see cref="ItemSearchMaxPages"/> says so.</summary>
        internal static async Task<ItemLookup> FindItemAsync(
            AccCredentials creds, string projectId, string folderUrn, string fileName, CancellationToken ct)
        {
            var r = new ItemLookup();
            string url = $"{DataBase}/projects/{projectId}/folders/{Uri.EscapeDataString(folderUrn)}/contents" +
                         $"?filter[type]=items&filter[displayName]={Uri.EscapeDataString(fileName)}&page[limit]=200";
            while (!string.IsNullOrEmpty(url))
            {
                if (r.Pages >= ItemSearchMaxPages) { r.Incomplete = true; break; }
                var resp = await SendJsonAsync(HttpMethod.Get, url, creds, null, null, idempotent: true, ct).ConfigureAwait(false);
                r.Pages++;
                if (!resp.IsSuccess)
                {
                    StingLog.Warn($"folder contents HTTP {resp.Status}: {Trim(resp.Body)}");
                    r.Failure = resp;
                    return r;
                }
                var doc = JObject.Parse(resp.Body);
                foreach (var it in doc["data"] as JArray ?? new JArray())
                {
                    if ((it["type"]?.Value<string>() ?? "") != "items") continue;
                    string dn = it["attributes"]?["displayName"]?.Value<string>() ?? "";
                    if (dn.Equals(fileName, StringComparison.OrdinalIgnoreCase)) { r.ItemId = it["id"]?.Value<string>() ?? ""; return r; }
                }
                url = doc["links"]?["next"]?["href"]?.Value<string>();
            }
            return r;
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
            var values = AccDocsAttributeSet.Build(options.Metadata, options.AttributeNames);
            if (!values.IsClean)
            {
                result.MetadataComplete = false;
                result.MetadataNote = "ISO 19650 attributes NOT written: " + string.Join("; ", values.Problems);
                return;
            }
            var defs = await AccDocsMetadata.EnsureDefinitionsAsync(creds.AccessToken, projectId, folderUrn,
                options.CreateMissingAttributes, (options.AttributeNames ?? AccAttributeNames.Default).Specs(), creds).ConfigureAwait(false);
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
                toWrite, defs.Value.Usable, creds).ConfigureAwait(false);
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
