using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Speckle Link — Snapshot + HTTP transport (Phase 6a engine + Phase 161 transport)
    //
    //  Serializes tagged elements to a local JSON snapshot in
    //  STING_BIM_MANAGER/speckle_snapshot.json AND pushes/pulls them to a
    //  Speckle server when streamUrl + token are configured in
    //  STING_BIM_MANAGER/speckle_config.json.
    //
    //  Transport (SpeckleHttpTransport): raw HTTP against the Speckle Server
    //  v2 GraphQL surface — no Speckle SDK NuGet dependency. Send writes a
    //  single root Base object containing the tag DTO array inline, no
    //  detached children. Receive fetches the latest commit's referenced
    //  object via /objects/{streamId}/{objectId}/single. The v2 GraphQL
    //  surface is kept as a compatibility layer on every modern Speckle
    //  server, so this works against both legacy v2 hosts and current FE2
    //  / project-based hosts.
    //
    //  Pattern: matches PlatformLinkEngine (internal static class + atomic
    //  temp-file + File.Move write pattern from StructuralCADPipeline, CLAUDE.md
    //  §683).
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DTO for a single tagged element in a Speckle snapshot. Kept flat and
    /// JSON-serialisable so the snapshot can round-trip through Speckle or any
    /// third-party JSON consumer.
    /// </summary>
    internal class SpeckleElementDto
    {
        public string ElementId { get; set; }
        public string Tag1 { get; set; }
        public string Tag2 { get; set; }
        public string Tag3 { get; set; }
        public string CategoryName { get; set; }
        public string FamilyName { get; set; }
        public DateTime ExportedAt { get; set; }
    }

    #region ── Internal Engine: SpeckleLinkEngine ──

    internal static class SpeckleLinkEngine
    {
        // Snapshot filename persisted alongside project in STING_BIM_MANAGER/
        private const string SnapshotFileName = "speckle_snapshot.json";

        /// <summary>
        /// Collect all elements that carry a non-empty STING_TAG1, project them
        /// to <see cref="SpeckleElementDto"/>, and persist the list to
        /// STING_BIM_MANAGER/speckle_snapshot.json using the atomic temp+Move
        /// write pattern. When <paramref name="streamUrl"/> and
        /// <paramref name="token"/> are both supplied, the snapshot is also
        /// pushed to the Speckle stream as a single-commit upload via
        /// <see cref="SpeckleHttpTransport"/>. A server failure surfaces in a
        /// TaskDialog but never invalidates the local snapshot.
        /// </summary>
        internal static void SendToSpeckle(Document doc, string streamUrl, string token)
        {
            if (doc == null)
            {
                StingLog.Error("Speckle: SendToSpeckle called with null document");
                return;
            }

            int count = 0;
            try
            {
                var dtos = CollectTaggedDtos(doc);
                count = dtos.Count;

                string json = JsonConvert.SerializeObject(dtos, Formatting.Indented);
                string snapshotPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), SnapshotFileName);

                // Atomic write: temp file + File.Move (overwrite). Copied from
                // StructuralCADPipeline sidecar pattern (CLAUDE.md §683) — prevents
                // a corrupt snapshot if the process crashes mid-write.
                string tempPath = snapshotPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, snapshotPath, true);

                StingLog.Info($"Speckle: exported {count} elements to snapshot");
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: SendToSpeckle failed while writing snapshot", ex);
                TaskDialog.Show("Speckle Send", $"Snapshot save failed:\n{ex.Message}");
                return;
            }

            // HTTP push when configured. Local snapshot is already on disk,
            // so a server failure never loses local state — we just surface
            // the error and keep going.
            if (!string.IsNullOrWhiteSpace(streamUrl) && !string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    var dtosForServer = ReadSnapshotFromDisk(doc) ?? new List<SpeckleElementDto>();
                    string commitId = SpeckleHttpTransport.Send(
                        streamUrl, token, dtosForServer,
                        $"STING tag snapshot — {dtosForServer.Count} elements");
                    StingLog.Info($"Speckle: pushed commit {commitId} ({dtosForServer.Count} elements)");
                    TaskDialog.Show("Speckle Send",
                        $"Pushed {dtosForServer.Count} elements.\nCommit: {commitId}");
                    return;
                }
                catch (Exception ex2)
                {
                    StingLog.Error("Speckle: HTTP push failed", ex);
                    TaskDialog.Show("Speckle Send",
                        $"Snapshot saved locally ({count} elements).\nServer push failed:\n{ex.Message}");
                    return;
                }
            }

            try
            {
                TaskDialog.Show("Speckle Send",
                    $"Snapshot saved — {count} elements.\n(No streamUrl/token in speckle_config.json — local-only.)");
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: SendToSpeckle post-write dialog failed", ex);
            }
        }

        /// <summary>
        /// When <paramref name="streamUrl"/> and <paramref name="token"/> are
        /// both supplied, fetch the latest commit on the target branch via
        /// <see cref="SpeckleHttpTransport"/> and overwrite the local snapshot
        /// with the server payload. On server failure, or when the streamUrl /
        /// token are blank, fall back to whatever is in
        /// STING_BIM_MANAGER/speckle_snapshot.json. Returns an empty list when
        /// neither source has data.
        /// </summary>
        internal static List<SpeckleElementDto> ReceiveFromSpeckle(
            Document doc, string streamUrl, string token)
        {
            if (doc == null)
            {
                StingLog.Error("Speckle: ReceiveFromSpeckle called with null document");
                return new List<SpeckleElementDto>();
            }

            // HTTP pull when configured. On success, persist the server payload
            // as the new local snapshot so subsequent DiffSnapshot calls compare
            // current model state against the latest server commit. On failure,
            // fall back to whatever is on disk.
            if (!string.IsNullOrWhiteSpace(streamUrl) && !string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    var fromServer = SpeckleHttpTransport.Receive(streamUrl, token);
                    if (fromServer != null)
                    {
                        WriteSnapshotToDisk(doc, fromServer);
                        StingLog.Info($"Speckle: pulled {fromServer.Count} elements from server, snapshot updated");
                        return fromServer;
                    }
                    StingLog.Warn("Speckle: server returned no commit — falling back to local snapshot");
                }
                catch (Exception ex)
                {
                    StingLog.Error("Speckle: HTTP pull failed, falling back to local snapshot", ex);
                }
            }

            return ReadSnapshotFromDisk(doc) ?? new List<SpeckleElementDto>();
        }

        /// <summary>
        /// Read the persisted snapshot file. Returns <c>null</c> when the file
        /// does not exist or fails to parse, so callers can distinguish "no
        /// data on disk" from "empty list on disk".
        /// </summary>
        private static List<SpeckleElementDto> ReadSnapshotFromDisk(Document doc)
        {
            try
            {
                string snapshotPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), SnapshotFileName);
                if (!File.Exists(snapshotPath))
                {
                    StingLog.Info("Speckle: loaded 0 elements from snapshot (file not found)");
                    return null;
                }

                string json = File.ReadAllText(snapshotPath);
                var parsed = JsonConvert.DeserializeObject<List<SpeckleElementDto>>(json);
                StingLog.Info($"Speckle: loaded {parsed?.Count ?? 0} elements from snapshot");
                return parsed ?? new List<SpeckleElementDto>();
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: ReadSnapshotFromDisk failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Persist a DTO list to the snapshot file using the same atomic
        /// temp+Move write pattern as <see cref="SendToSpeckle"/>.
        /// </summary>
        private static void WriteSnapshotToDisk(Document doc, List<SpeckleElementDto> dtos)
        {
            try
            {
                string snapshotPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), SnapshotFileName);
                string json = JsonConvert.SerializeObject(dtos, Formatting.Indented);
                string tempPath = snapshotPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, snapshotPath, true);
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: WriteSnapshotToDisk failed", ex);
            }
        }

        /// <summary>
        /// Compare the persisted snapshot against the current model's tagged
        /// elements and return (Added, Removed, Changed) counts. Matching is
        /// done on <see cref="SpeckleElementDto.ElementId"/> string equality.
        /// "Changed" means same ElementId but one of Tag1/Tag2/Tag3/Category/
        /// Family differs between snapshot and current model.
        /// </summary>
        internal static (int Added, int Removed, int Changed) DiffSnapshot(Document doc)
        {
            int added = 0, removed = 0, changed = 0;
            if (doc == null)
            {
                StingLog.Error("Speckle: DiffSnapshot called with null document");
                return (0, 0, 0);
            }

            try
            {
                var snapshot = ReceiveFromSpeckle(doc, "", "");
                var current = CollectTaggedDtos(doc);

                var snapshotById = new Dictionary<string, SpeckleElementDto>(StringComparer.Ordinal);
                foreach (var dto in snapshot)
                {
                    if (dto == null || string.IsNullOrEmpty(dto.ElementId)) continue;
                    snapshotById[dto.ElementId] = dto;
                }

                var currentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var dto in current)
                {
                    if (dto == null || string.IsNullOrEmpty(dto.ElementId)) continue;
                    currentIds.Add(dto.ElementId);

                    if (!snapshotById.TryGetValue(dto.ElementId, out var prev))
                    {
                        added++;
                        continue;
                    }

                    if (!TokenEquals(prev.Tag1, dto.Tag1) ||
                        !TokenEquals(prev.Tag2, dto.Tag2) ||
                        !TokenEquals(prev.Tag3, dto.Tag3) ||
                        !TokenEquals(prev.CategoryName, dto.CategoryName) ||
                        !TokenEquals(prev.FamilyName, dto.FamilyName))
                    {
                        changed++;
                    }
                }

                foreach (string snapId in snapshotById.Keys)
                {
                    if (!currentIds.Contains(snapId)) removed++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: DiffSnapshot failed", ex);
                return (0, 0, 0);
            }

            StingLog.Info($"Speckle diff: +{added} -{removed} ~{changed}");
            return (added, removed, changed);
        }

        // ── Internal helpers ───────────────────────────────────────────────

        /// <summary>
        /// Collect all elements where STING_TAG1 is non-empty and project to
        /// <see cref="SpeckleElementDto"/>. Uses FilteredElementCollector with
        /// WhereElementIsNotElementType() to match the Excel/Platform engines.
        /// </summary>
        private static List<SpeckleElementDto> CollectTaggedDtos(Document doc)
        {
            var dtos = new List<SpeckleElementDto>();
            DateTime stamp = DateTime.UtcNow;

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                foreach (Element el in collector)
                {
                    if (el == null) continue;

                    string tag1 = ParameterHelpers.GetString(el, StingTools.Core.ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    dtos.Add(new SpeckleElementDto
                    {
                        ElementId = el.Id.Value.ToString(),
                        Tag1 = tag1,
                        Tag2 = ParameterHelpers.GetString(el, StingTools.Core.ParamRegistry.TAG2),
                        Tag3 = ParameterHelpers.GetString(el, StingTools.Core.ParamRegistry.TAG3),
                        CategoryName = ParameterHelpers.GetCategoryName(el) ?? string.Empty,
                        FamilyName = ParameterHelpers.GetFamilyName(el) ?? string.Empty,
                        ExportedAt = stamp,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: CollectTaggedDtos failed", ex);
            }

            return dtos;
        }

        /// <summary>Null-safe ordinal comparison, treating null and "" as equal.</summary>
        private static bool TokenEquals(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }
    }

    #endregion

    #region ── HTTP Transport: SpeckleHttpTransport ──

    /// <summary>
    /// Raw-HTTP transport against the Speckle Server v2 GraphQL surface.
    /// Two public operations:
    /// <list type="bullet">
    ///   <item><description><see cref="Send"/> — gzip-encodes a single root
    ///   <c>Base</c> object containing the DTO array inline, POSTs it to
    ///   <c>/objects/{streamId}</c> as multipart form-data, then issues a
    ///   <c>commitCreate</c> GraphQL mutation referencing the new object.</description></item>
    ///   <item><description><see cref="Receive"/> — reads the latest commit on
    ///   the target branch via GraphQL, then GETs
    ///   <c>/objects/{streamId}/{objectId}/single</c> and parses the
    ///   <c>tags</c> array out of the root.</description></item>
    /// </list>
    /// No Speckle SDK NuGet dependency — Speckle's v2 GraphQL surface is kept
    /// as a compatibility layer on every modern server (FE2 / project-based
    /// hosts included), so legacy <c>/streams/</c> URLs and modern
    /// <c>/projects/</c> URLs both resolve to the same backend.
    /// </summary>
    internal static class SpeckleHttpTransport
    {
        // Single shared HttpClient — thread-safe and intended to be reused
        // for the lifetime of the AppDomain. BaseAddress is intentionally NOT
        // set so different speckle_config.json instances (different servers)
        // can route through the same client by passing absolute URIs.
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private const string DefaultBranch    = "main";
        private const string SpeckleBaseType  = "Base";
        private const string SourceApp        = "STING-Tags";

        /// <summary>
        /// Push a tag-snapshot DTO list to a Speckle stream as a single commit.
        /// Returns the new commit id on success. Throws on any HTTP / GraphQL
        /// error so the caller can surface it to the user — the caller is
        /// expected to wrap this in try/catch (the local snapshot is already
        /// safe on disk by the time we get here, so a thrown exception never
        /// loses local state).
        /// </summary>
        internal static string Send(
            string streamUrl, string token,
            List<SpeckleElementDto> dtos, string commitMessage)
        {
            if (dtos == null) dtos = new List<SpeckleElementDto>();

            var (server, streamId, branch) = ParseStreamUrl(streamUrl);

            // Build root Base with tag DTOs inline. No detached children — the
            // whole payload is one self-contained object so we never have to
            // chase references on Receive.
            var root = new JObject
            {
                ["speckle_type"]        = SpeckleBaseType,
                ["applicationId"]       = SourceApp,
                ["totalChildrenCount"]  = 0,
                ["tags"]                = JArray.FromObject(dtos),
            };
            string objectId = ComputeObjectId(root);
            root["id"] = objectId;

            // NDJSON line + gzip + multipart upload to /objects/{streamId}.
            string ndjsonLine = root.ToString(Formatting.None) + "\n";
            byte[] gzipped = GzipBytes(Encoding.UTF8.GetBytes(ndjsonLine));

            using (var req = new HttpRequestMessage(HttpMethod.Post, $"{server}/objects/{streamId}"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var multipart = new MultipartFormDataContent();
                var batch = new ByteArrayContent(gzipped);
                batch.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
                multipart.Add(batch, "batch-1", "batch-1");
                req.Content = multipart;

                var resp = _http.SendAsync(req).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    string body = SafeReadBody(resp);
                    if ((int)resp.StatusCode == 401)
                        throw new HttpRequestException($"Speckle authentication failed (401). Check your token in speckle_config.json.");
                    throw new HttpRequestException(
                        $"Speckle object upload failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
                }
            }

            // commitCreate mutation. parents/totalChildrenCount are optional;
            // we send sourceApplication so commits are attributable in the
            // Speckle UI activity log.
            const string mutation = @"
                mutation CommitCreate($input: CommitCreateInput!) {
                    commitCreate(input: $input)
                }";
            var variables = new JObject
            {
                ["input"] = new JObject
                {
                    ["streamId"]          = streamId,
                    ["branchName"]        = branch,
                    ["objectId"]          = objectId,
                    ["message"]           = commitMessage ?? string.Empty,
                    ["sourceApplication"] = SourceApp,
                }
            };
            var data = GraphQLData(server, token, mutation, variables);
            string commitId = data?["commitCreate"]?.Value<string>() ?? string.Empty;
            if (string.IsNullOrEmpty(commitId))
                throw new HttpRequestException("Speckle commitCreate returned an empty commit id.");
            return commitId;
        }

        /// <summary>
        /// Pull the latest commit on the target branch and parse its root
        /// object's <c>tags</c> array back into DTOs. Returns <c>null</c> when
        /// the branch has no commits (so the caller can distinguish "empty
        /// branch" from "non-empty branch with zero tags"). Throws on any
        /// HTTP / GraphQL / parse error.
        /// </summary>
        internal static List<SpeckleElementDto> Receive(string streamUrl, string token)
        {
            var (server, streamId, branch) = ParseStreamUrl(streamUrl);

            // Speckle Server v2 GraphQL — kept as compat layer on FE2 / v3 hosts.
            const string query = @"
                query LatestCommit($streamId: String!, $branch: String!) {
                    stream(id: $streamId) {
                        branch(name: $branch) {
                            commits(limit: 1) {
                                items { id referencedObject }
                            }
                        }
                    }
                }";
            var variables = new JObject
            {
                ["streamId"] = streamId,
                ["branch"]   = branch,
            };

            var data = GraphQLData(server, token, query, variables);
            var items = data?["stream"]?["branch"]?["commits"]?["items"] as JArray;
            if (items == null || items.Count == 0)
            {
                StingLog.Info($"Speckle: branch '{branch}' on stream '{streamId}' has no commits");
                return null;
            }

            string objectId = items[0]?["referencedObject"]?.Value<string>() ?? string.Empty;
            if (string.IsNullOrEmpty(objectId))
                throw new HttpRequestException("Speckle latest-commit response missing referencedObject.");

            using (var req = new HttpRequestMessage(HttpMethod.Get,
                $"{server}/objects/{streamId}/{objectId}/single"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var resp = _http.SendAsync(req).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    string body = SafeReadBody(resp);
                    throw new HttpRequestException(
                        $"Speckle object fetch failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
                }

                string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var rootObj = JObject.Parse(json);
                var tags = rootObj["tags"] as JArray;
                if (tags == null) return new List<SpeckleElementDto>();
                return tags.ToObject<List<SpeckleElementDto>>() ?? new List<SpeckleElementDto>();
            }
        }

        // ── URL parsing ────────────────────────────────────────────────────

        /// <summary>
        /// Accepts both v2 and FE2/v3 stream URL shapes:
        /// <c>https://host/streams/{id}</c>,
        /// <c>https://host/streams/{id}/branches/{name}</c>,
        /// <c>https://host/projects/{id}</c>,
        /// <c>https://host/projects/{id}/models/{name}</c>.
        /// Branch defaults to <c>main</c> when not specified.
        /// </summary>
        internal static (string server, string streamId, string branch) ParseStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Speckle stream URL is empty (set 'streamUrl' in speckle_config.json).");

            Uri uri;
            try { uri = new Uri(url); }
            catch (UriFormatException ex)
            {
                throw new ArgumentException($"Speckle stream URL is malformed: {url}", ex);
            }

            string server = uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
                throw new ArgumentException(
                    $"Speckle stream URL must contain '/streams/<id>' or '/projects/<id>': {url}");

            string streamId;
            string branch = DefaultBranch;
            string head = segments[0].ToLowerInvariant();

            if (head == "streams")
            {
                streamId = segments[1];
                if (segments.Length >= 4 && segments[2].Equals("branches", StringComparison.OrdinalIgnoreCase))
                    branch = Uri.UnescapeDataString(segments[3]);
            }
            else if (head == "projects")
            {
                streamId = segments[1];
                if (segments.Length >= 4 && segments[2].Equals("models", StringComparison.OrdinalIgnoreCase))
                    branch = Uri.UnescapeDataString(segments[3]);
            }
            else
            {
                throw new ArgumentException(
                    $"Speckle stream URL path must start with /streams/ or /projects/: {url}");
            }

            if (string.IsNullOrWhiteSpace(streamId))
                throw new ArgumentException($"Speckle stream URL has empty stream id: {url}");

            return (server, streamId, branch);
        }

        // ── Object id (deterministic hash) ─────────────────────────────────

        /// <summary>
        /// Speckle-style sha256 hash over the canonicalised object JSON, with
        /// the <c>id</c> field excluded from the hash input. Keys are sorted
        /// alphabetically (recursively); array order is preserved. Lowercase
        /// hex output. Self-consistent across <see cref="Send"/> /
        /// <see cref="Receive"/> — that's all that's strictly required for
        /// round-trip, since the Speckle server stores objects under whatever
        /// id the client sends.
        /// </summary>
        private static string ComputeObjectId(JObject obj)
        {
            var copy = (JObject)obj.DeepClone();
            copy.Remove("id");
            var canonical = (JObject)SortRecursive(copy);
            byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString(Formatting.None));
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static JToken SortRecursive(JToken token)
        {
            if (token is JObject jo)
            {
                var sorted = new JObject();
                foreach (var prop in jo.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted[prop.Name] = SortRecursive(prop.Value);
                return sorted;
            }
            if (token is JArray ja)
            {
                var sortedArr = new JArray();
                foreach (var item in ja) sortedArr.Add(SortRecursive(item));
                return sortedArr;
            }
            return token.DeepClone();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static byte[] GzipBytes(byte[] input)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gz.Write(input, 0, input.Length);
                }
                return ms.ToArray();
            }
        }

        private static JObject GraphQLData(string server, string token, string query, JObject variables)
        {
            var body = new JObject
            {
                ["query"]     = query,
                ["variables"] = variables ?? new JObject(),
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, $"{server}/graphql"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                var resp = _http.SendAsync(req).GetAwaiter().GetResult();
                string respBody = SafeReadBody(resp);

                if (!resp.IsSuccessStatusCode)
                {
                    if ((int)resp.StatusCode == 401)
                        throw new HttpRequestException("Speckle authentication failed (401). Check your token in speckle_config.json.");
                    throw new HttpRequestException(
                        $"Speckle GraphQL HTTP failure: {(int)resp.StatusCode} {resp.ReasonPhrase}. {respBody}");
                }

                JObject parsed;
                try { parsed = JObject.Parse(respBody); }
                catch (JsonException ex)
                {
                    throw new HttpRequestException(
                        $"Speckle GraphQL response was not valid JSON: {respBody}", ex);
                }

                if (parsed["errors"] is JArray errs && errs.Count > 0)
                {
                    string firstMsg = errs[0]?["message"]?.Value<string>() ?? errs.ToString(Formatting.None);
                    throw new HttpRequestException($"Speckle GraphQL error: {firstMsg}");
                }
                return parsed["data"] as JObject ?? new JObject();
            }
        }

        private static string SafeReadBody(HttpResponseMessage resp)
        {
            try { return resp.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return string.Empty; }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  Speckle Commands (Phase 6b) — thin IExternalCommand wrappers around
    //  SpeckleLinkEngine. Config is loaded from STING_BIM_MANAGER/speckle_config.json
    //  following the same pattern as planscape_connection.json.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Speckle Send ──

    /// <summary>
    /// Push tagged elements to a Speckle stream. Reads streamUrl/token from
    /// STING_BIM_MANAGER/speckle_config.json (created out-of-band by the
    /// user). When config is present, the engine writes the local snapshot
    /// and pushes a Speckle commit via <see cref="SpeckleHttpTransport"/>.
    /// When config is missing or empty, the engine writes a local snapshot
    /// only.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleSendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                var (streamUrl, token) = SpeckleConfig.Load(ctx.Doc);
                SpeckleLinkEngine.SendToSpeckle(ctx.Doc, streamUrl, token);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleSendCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region ── Speckle Receive ──

    /// <summary>
    /// Pull the latest Speckle commit (when streamUrl/token are configured)
    /// or load the local snapshot (when not), then report the element count.
    /// On a successful HTTP pull the local snapshot is overwritten with the
    /// server payload so subsequent <c>SpeckleDiff</c> compares the current
    /// model against the latest server commit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleReceiveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                var (streamUrl, token) = SpeckleConfig.Load(ctx.Doc);
                bool serverConfigured = !string.IsNullOrWhiteSpace(streamUrl)
                                     && !string.IsNullOrWhiteSpace(token);

                var elements2 = SpeckleLinkEngine.ReceiveFromSpeckle(ctx.Doc, streamUrl, token);
                string source = serverConfigured ? "server (or local fallback)" : "local snapshot";
                TaskDialog.Show("Speckle Receive",
                    $"Loaded {elements2.Count} elements from {source}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleReceiveCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region ── Config loader ──

    /// <summary>
    /// Reads <c>STING_BIM_MANAGER/speckle_config.json</c> with two keys:
    /// <c>streamUrl</c> (e.g. <c>https://app.speckle.systems/streams/abc123</c>
    /// or the FE2 <c>/projects/abc123</c> form) and <c>token</c> (a personal
    /// access token with <c>streams:write</c> + <c>streams:read</c>).
    /// Missing file or missing fields return empty strings — the engine
    /// treats that as "local-only" mode.
    /// </summary>
    internal static class SpeckleConfig
    {
        internal static (string streamUrl, string token) Load(Document doc)
        {
            try
            {
                string cfgPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), "speckle_config.json");
                if (!File.Exists(cfgPath)) return ("", "");
                var cfg = JObject.Parse(File.ReadAllText(cfgPath));
                string url   = cfg["streamUrl"]?.Value<string>() ?? "";
                string token = cfg["token"]?.Value<string>()     ?? "";
                return (url, token);
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: SpeckleConfig.Load failed", ex);
                return ("", "");
            }
        }
    }

    #endregion

    #region ── Speckle Diff ──

    /// <summary>
    /// Compare the local Speckle snapshot against the current model's tagged
    /// elements and report Added/Removed/Changed counts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleDiffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                var (added, removed, changed) = SpeckleLinkEngine.DiffSnapshot(ctx.Doc);
                TaskDialog.Show("Speckle Diff",
                    $"vs last snapshot:\n  Added:   {added}\n  Removed: {removed}\n  Changed: {changed}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleDiffCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion
}
