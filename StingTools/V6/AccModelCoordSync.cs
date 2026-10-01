// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccModelCoordSync.cs
//
// Autodesk Construction Cloud (ACC) Model Coordination — READ client.
//
// Closes the "clash in ACC AND STING" loop: ACC Model Coordination is the
// system of record; this client PULLS ACC's clash results so STING can triage
// them (ClashTriageEngine) and push prioritised Issues back via
// AccIssueSync.PushIssueAsync. The push half already existed (AccIssueSync);
// this is the missing read half.
//
// Reuses AccCredentials + AccIssueSync.EnsureAuthAsync for OAuth/token —
// no second credential store, no duplicate refresh logic.
//
// Endpoints + payload schema verified against the public APS sample
// "aps-clash-data-view" (Autodesk Developer Advocacy) and the APS Model
// Coordination v3 reference. Host https://developer.api.autodesk.com:
//
//   GET  bim360/modelset/v3/containers/{containerId}/modelsets
//        -> { modelSets: [ { modelSetId, name } ] }
//   GET  bim360/clash/v3/containers/{containerId}/modelsets/{modelSetId}/tests
//        -> { tests: [ { clashTestId|id, status, completedAt } ] }
//   GET  bim360/clash/v3/containers/{containerId}/tests/{testId}/resources
//        -> { resources: [ { type|name, url } ] }   (pre-signed download URLs)
//   GET  <signed url>  -> gzipped scope JSON:
//        scope-version-clash.2.0.0.json.gz          -> { clashes:   [ { id, dist, status } ] }
//        scope-version-clash-instance.2.0.0.json.gz -> { instances: [ { cid, ldid, rdid, lvid, rvid } ] }
//        scope-version-document.2.0.0.json.gz       -> { documents: [ { id, name } ] }
//
// Join: clashes[i].id == instances[].cid; instance carries the left/right
// document ids (ldid/rdid -> documents[].name) and object dbIds (lvid/rvid).
// ACC clash data carries NO Revit category — only object dbIds + document
// names — so severity is derived from the document-name discipline plus the
// real penetration distance (dist), not a Revit category lookup.
//
// The container id is the ACC project's coordination container; for most
// projects this is the id used for Issues (AccCredentials.ProjectId).
//
// Residual: the exact clash-service sub-paths (tests / resources) live inside
// the APS SDK; confirm with one live pull before the engagement leans on them.
//
// A wrong path NO LONGER fails soft into an empty list. Every read returns an
// AccFetchResult carrying an AccFetchStatus, so EmptyOk (genuinely nothing)
// and NotFound / AuthFailed / TransportFailed are different values that the
// caller must branch on. Before that split, a wrong container id produced the
// same empty list as a clash-clean federation, and the fortnightly KUT
// coordination cycle reported success without having checked anything.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class AccModelSet
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>One clash pulled from ACC Model Coordination, joined across the
    /// clash + instance + document scope files.</summary>
    public sealed class AccClashRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = "active";
        public double DistanceM { get; set; }          // negative = penetration depth
        public long LeftObjectId { get; set; }         // lvid (dbId)
        public long RightObjectId { get; set; }        // rvid (dbId)
        public string LeftDocument { get; set; } = string.Empty;
        public string RightDocument { get; set; } = string.Empty;
        /// <summary>True when both sides' document NAMES were resolved. Without the document
        /// scope file the sides carry raw document ids, which change with every model
        /// version, so they cannot key an escalation that must survive the next upload.</summary>
        public bool DocumentsNamed { get; set; }
        /// <summary>Factor converting DistanceM → mm (from AccCredentials.DistToMm; default 1000 = metres).</summary>
        public double DistToMm { get; set; } = 1000.0;
        public double PenetrationMm => Math.Abs(DistanceM) * DistToMm;

        // ACC-HARD-5: what is needed to LOCATE the objects, not just name them. The clash
        // test tutorial (bim360/v1/tutorials/mc-tutorial-clash) says lvid/rvid are viewer ids
        // into "the document version URNs" the document scope file lists for ldid/rdid, and
        // that those ids are NOT stable across versions - so they are only meaningful paired
        // with the exact version URN. Empty when the scope file carried no URN.

        /// <summary>Document version URN of the left side (document scope <c>urn</c>).</summary>
        public string LeftDocumentUrn { get; set; } = string.Empty;
        /// <summary>Document version URN of the right side.</summary>
        public string RightDocumentUrn { get; set; } = string.Empty;
        /// <summary>The model set version the clash test ran against (tests[].modelSetVersion);
        /// 0 when the test did not report one.</summary>
        public int ModelSetVersion { get; set; }
    }

    /// <summary>One document in a model set version (GET modelsets/{id}/versions/{v} →
    /// documentVersions[]). Only the fields the issue locator needs.</summary>
    public sealed class AccModelSetDocument
    {
        public string VersionUrn { get; set; } = string.Empty;
        /// <summary>"The URN of the Model Derivative bubble for the document version" - the URN
        /// the viewer loads, so the one whose object ids the clash test's lvid/rvid index.</summary>
        public string BubbleUrn { get; set; } = string.Empty;
        /// <summary>"The ID of the geometry node in the derivative manifest to which this
        /// document version refers."</summary>
        public string ViewableGuid { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>What the clash-tests endpoint says about a model set, without its results.</summary>
    /// <summary>One model set version: its number, status and documents.</summary>
    public sealed class AccModelSetVersion
    {
        public int Version { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<AccModelSetDocument> Documents { get; set; } = new List<AccModelSetDocument>();
    }

    public sealed class AccClashTestSummary
    {
        public int TestCount { get; set; }
        public List<string> States { get; set; } = new List<string>();
        /// <summary>Empty when no test has completed.</summary>
        public string LatestCompletedId { get; set; } = string.Empty;
        public string LatestCompletedAt { get; set; } = string.Empty;
        public bool HasCompleted => !string.IsNullOrEmpty(LatestCompletedId);
    }

    public static class AccModelCoordSync
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point the client at a loopback listener. Production never
        /// calls this; StingTools.Acc.Tests does, to prove end-to-end that a 404 does not
        /// become EmptyOk. Pass null to restore the real APS host.</summary>
        internal static void OverrideHostForTests(string host)
        {
            _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');
            // The token endpoint too: a 401 now triggers one forced refresh, and a test must
            // never reach the real Autodesk sign-in service.
            AccIssueSync.OverrideHostForTests(host);
        }

        /// <summary>Test seam: the 429 back-off. Production waits; tests replace it with a
        /// no-op so the suite does not sleep. Production timings are never altered.</summary>
        internal static Func<TimeSpan, Task> DelayHook
        {
            get => AccHttp.DelayHook;
            set => AccHttp.DelayHook = value;
        }

        private static string Host => _host;
        private static string ModelSetBase => Host + "/bim360/modelset/v3";
        private static string ClashBase    => Host + "/bim360/clash/v3";

        // ── Model sets (paginated; dedupes by id so an offset-ignoring endpoint can't loop) ──
        public static async Task<AccFetchResult<List<AccModelSet>>> ListModelSetsAsync(
            AccCredentials creds, string containerId)
        {
            var list = new List<AccModelSet>();
            if (string.IsNullOrEmpty(containerId))
                return AccFetchResult<List<AccModelSet>>.Failure(AccFetchStatus.NotFound, list, 0,
                    "no ACC container id was supplied (set ProjectId, or CoordContainerId when Model Coordination differs)");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int pageSize = 100;
            int offset = 0;
            string cid = AccIds.ForAcc(containerId);
            string next = null;   // D3: v3 pages with a continuation token; offset is the fallback
            for (int page = 0; page < 50; page++)
            {
                string url = next != null
                    ? $"{ModelSetBase}/containers/{cid}/modelsets?continuationToken={Uri.EscapeDataString(next)}"
                    : $"{ModelSetBase}/containers/{cid}/modelsets?limit={pageSize}&offset={offset}";
                var got = await GetJsonAsync(creds, url).ConfigureAwait(false);
                if (!got.Succeeded)
                    return AccFetchResult<List<AccModelSet>>.Failure(got.Status, list, got.HttpStatus, got.Detail);

                var arr = AccFetchOutcome.FindArray(got.Value, new[] { "modelSets", "results" });
                if (arr == null)
                    // A 200 that carries no recognised array is a schema/sub-path change,
                    // never "no model sets". Reporting it as empty is the bug this closes.
                    return AccFetchResult<List<AccModelSet>>.Failure(AccFetchStatus.TransportFailed, list, got.HttpStatus,
                        "the model-set response carried no 'modelSets' array — the APS sub-path or payload shape has changed");
                if (arr.Count == 0) return AccFetchResult<List<AccModelSet>>.Success(list, list.Count == 0);

                int added = 0;
                foreach (var m in arr)
                {
                    string id = (string)(m["modelSetId"] ?? m["id"]) ?? string.Empty;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                    list.Add(new AccModelSet { Id = id, Name = (string)(m["name"] ?? m["title"]) ?? "(unnamed model set)" });
                    added++;
                }
                string token = ContinuationToken(got.Value);
                if (token != null)
                {
                    if (token == next || added == 0)
                        return AccFetchResult<List<AccModelSet>>.Failure(AccFetchStatus.TransportFailed, list, got.HttpStatus,
                            "the model-set list repeated a page — paging is broken, the list is INCOMPLETE");
                    next = token;
                    continue;
                }
                if (next != null || arr.Count < pageSize || added == 0)  // token paging ended, last page, or offset ignored
                    return AccFetchResult<List<AccModelSet>>.Success(list, list.Count == 0);
                offset += pageSize;
            }
            // Fifty full pages and never a short one: there are more sets than were read.
            return AccFetchResult<List<AccModelSet>>.Failure(AccFetchStatus.TransportFailed, list, 200,
                $"stopped after 50 pages ({list.Count} model sets) without reaching the last page - the list is INCOMPLETE");
        }

        /// <summary>The Model Coordination v3 continuation token of a page, or null on the last.</summary>
        internal static string ContinuationToken(JToken page)
        {
            string t = (string)(page?["page"]?["continuationToken"] ?? page?["continuationToken"]
                                ?? page?["pagination"]?["continuationToken"]);
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        /// <summary>
        /// D3: EVERY clash test on a model set. The tests list is paged with a continuation token
        /// (Model Coordination v3); reading only the first page could miss the newest completed
        /// test once a fortnightly federation has produced a page of them, and an older test was
        /// then triaged and escalated as current. Fails loudly at the cap, never returns a part.
        /// </summary>
        internal static async Task<AccFetchResult<JArray>> ReadAllTestsAsync(AccCredentials creds, string containerId, string modelSetId)
        {
            var all = new JArray();
            string token = null;
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            int http = 0;
            for (int page = 0; page < 50; page++)
            {
                string url = $"{ClashBase}/containers/{containerId}/modelsets/{modelSetId}/tests" +
                             (token == null ? "" : "?continuationToken=" + Uri.EscapeDataString(token));
                var got = await GetJsonAsync(creds, url).ConfigureAwait(false);
                if (!got.Succeeded) return AccFetchResult<JArray>.Failure(got.Status, all, got.HttpStatus, got.Detail);
                http = got.HttpStatus;
                var arr = AccFetchOutcome.FindArray(got.Value, new[] { "tests", "results" });
                if (arr == null)
                    return AccFetchResult<JArray>.Failure(AccFetchStatus.TransportFailed, all, got.HttpStatus,
                        "the clash-test response carried no 'tests' array — the bim360/clash/v3 tests sub-path or payload shape has changed");
                foreach (var t in arr) all.Add(t);
                token = ContinuationToken(got.Value);
                if (token == null) return AccFetchResult<JArray>.Success(all, all.Count == 0);
                if (!seenTokens.Add(token))
                    return AccFetchResult<JArray>.Failure(AccFetchStatus.TransportFailed, all, got.HttpStatus,
                        "the clash-test list repeated a continuation token — paging is broken, the list is INCOMPLETE");
            }
            return AccFetchResult<JArray>.Failure(AccFetchStatus.TransportFailed, all, http,
                $"stopped after 50 pages ({all.Count} clash tests) with more to read - the list is INCOMPLETE");
        }

        // ── Clashes (tests -> resources -> scope files -> join) ──
        public static async Task<AccFetchResult<List<AccClashRecord>>> GetClashesAsync(
            AccCredentials creds, string containerId, string modelSetId, int max = 0)
        {
            // max <= 0 means every clash. A cap is the caller's explicit choice and is reported
            // (Truncated / TotalAvailable), never silent: a 1,000 cap used to hand back the first
            // 1,000 of a 3,000-clash federation as "Ok", and the report said the CSV held all.
            var result = new List<AccClashRecord>();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId))
                return Fail(AccFetchStatus.NotFound, 0,
                    "no ACC container id or model-set id was supplied");
            containerId = AccIds.ForAcc(containerId);

            // 1. latest completed clash test
            var testsGot = await ReadAllTestsAsync(creds, containerId, modelSetId).ConfigureAwait(false);
            if (!testsGot.Succeeded) return Fail(testsGot.Status, testsGot.HttpStatus, testsGot.Detail);
            var tests = testsGot.Value;
            if (tests.Count == 0)
            {
                // Genuinely nothing: the endpoint answered and said there are no tests.
                StingLog.Info("ACC MC: no clash tests on model set.");
                return AccFetchResult<List<AccClashRecord>>.Success(result, empty: true);
            }

            var completed = tests
                .Where(t => IsCompletedTest((string)t["status"]))
                .OrderByDescending(t => CompletedStamp(t))
                .ToList();
            if (completed.Count == 0)
            {
                // Tests exist but none has finished. This used to fall back to tests.First()
                // - a running or failed test - and read whatever it had. Nothing has been
                // checked yet, and that is what the result now says.
                string states = string.Join(", ", tests.Select(t => (string)t["status"] ?? "?").Distinct());
                StingLog.Info("ACC MC: no completed clash test yet (" + states + ").");
                var pending = AccFetchResult<List<AccClashRecord>>.Success(result, empty: true);
                pending.Detail = $"no clash test on this model set has completed yet (test status: {states}) - " +
                                 "nothing has been checked; run again when ACC finishes the test";
                pending.NotReady = true;
                return pending;
            }
            var latest = completed.First();
            string testId = (string)(latest["clashTestId"] ?? latest["id"] ?? latest["testId"]) ?? "";
            if (string.IsNullOrEmpty(testId))
            {
                StingLog.Warn("ACC MC: clash test id missing.");
                return Fail(AccFetchStatus.TransportFailed, testsGot.HttpStatus,
                    "the latest clash test carried no id — the payload shape has changed");
            }

            // 2. resources (pre-signed scope-file URLs)
            var resGot = await GetJsonAsync(creds,
                $"{ClashBase}/containers/{containerId}/tests/{testId}/resources").ConfigureAwait(false);
            if (!resGot.Succeeded) return Fail(resGot.Status, resGot.HttpStatus, resGot.Detail);

            var resources = AccFetchOutcome.FindArray(resGot.Value, new[] { "resources" });
            if (resources == null)
            {
                StingLog.Warn("ACC MC: clash test resources missing.");
                return Fail(AccFetchStatus.TransportFailed, resGot.HttpStatus,
                    "the clash-resources response carried no 'resources' array — the bim360/clash/v3 resources sub-path or payload shape has changed");
            }

            string clashUrl    = ResourceUrl(resources, "clash",    excludes: new[] { "instance", "issue" });
            string instanceUrl = ResourceUrl(resources, "instance");
            string documentUrl = ResourceUrl(resources, "document");
            if (clashUrl == null || instanceUrl == null)
            {
                StingLog.Warn("ACC MC: clash/instance scope resource URL not found.");
                return Fail(AccFetchStatus.TransportFailed, resGot.HttpStatus,
                    "the clash test exposed no clash/instance scope-file URL");
            }

            // 3. download + gunzip the scope files
            var clashScope    = await DownloadScopeAsync(clashUrl, creds).ConfigureAwait(false);
            if (!clashScope.Succeeded) return Fail(clashScope.Status, clashScope.HttpStatus, clashScope.Detail);
            var instanceScope = await DownloadScopeAsync(instanceUrl, creds).ConfigureAwait(false);
            if (!instanceScope.Succeeded) return Fail(instanceScope.Status, instanceScope.HttpStatus, instanceScope.Detail);
            // The document scope is optional — its absence costs document NAMES, not clashes.
            JObject documentScope = null;
            if (documentUrl != null)
            {
                var docGot = await DownloadScopeAsync(documentUrl, creds).ConfigureAwait(false);
                if (docGot.Succeeded) documentScope = docGot.Value;
                else StingLog.Warn("ACC MC: document scope unavailable (" + docGot.Detail + ") — clash rows will carry raw document ids.");
            }

            // 4. join. instances are keyed by cid (== clash id); first instance per cid wins.
            var instByCid = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (var ins in instanceScope.Value["instances"] as JArray ?? new JArray())
            {
                string cid = (string)ins["cid"] ?? "";
                if (cid.Length > 0 && !instByCid.ContainsKey(cid)) instByCid[cid] = ins;
            }
            var docNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var docUrnById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in documentScope?["documents"] as JArray ?? new JArray())
            {
                string id = (string)(d["id"] ?? d["clashDocId"]) ?? "";
                if (id.Length == 0) continue;
                docNameById[id] = (string)(d["name"] ?? d["displayName"]) ?? id;
                // The aps-clash-data-view sample joins documents[].urn to the model set
                // version's documentVersions[].versionUrn; versionUrn is accepted as well.
                string urn = (string)(d["urn"] ?? d["versionUrn"]);
                if (!string.IsNullOrEmpty(urn)) docUrnById[id] = urn;
            }
            int modelSetVersion = (int)ParseLong(latest["modelSetVersion"]);

            var clashArr = clashScope.Value["clashes"] as JArray;
            if (clashArr == null)
                return Fail(AccFetchStatus.TransportFailed, clashScope.HttpStatus,
                    "the clash scope file carried no 'clashes' array — the scope-file schema has changed");

            int available = 0;
            foreach (var c in clashArr)
            {
                available++;
                if (max > 0 && result.Count >= max) continue;
                string id = (string)(c["id"] ?? c["cid"]) ?? "";
                if (id.Length == 0) continue;
                instByCid.TryGetValue(id, out var ins);
                string ldid = ins != null ? (string)ins["ldid"] : null;
                string rdid = ins != null ? (string)ins["rdid"] : null;
                result.Add(new AccClashRecord
                {
                    Id            = id,
                    Status        = (string)c["status"] ?? "active",
                    DistanceM     = (double?)c["dist"] ?? 0.0,
                    LeftObjectId  = ParseLong(ins?["lvid"]),
                    RightObjectId = ParseLong(ins?["rvid"]),
                    LeftDocument  = ldid != null && docNameById.TryGetValue(ldid, out var ln) ? ln : (ldid ?? ""),
                    RightDocument = rdid != null && docNameById.TryGetValue(rdid, out var rn) ? rn : (rdid ?? ""),
                    DocumentsNamed = ldid != null && rdid != null && docNameById.ContainsKey(ldid) && docNameById.ContainsKey(rdid),
                    DistToMm      = creds.DistToMm,
                    LeftDocumentUrn  = ldid != null && docUrnById.TryGetValue(ldid, out var lu) ? lu : "",
                    RightDocumentUrn = rdid != null && docUrnById.TryGetValue(rdid, out var ru) ? ru : "",
                    ModelSetVersion  = modelSetVersion,
                });
            }
            var ok = AccFetchResult<List<AccClashRecord>>.Success(result, result.Count == 0);
            ok.TotalAvailable = available;
            ok.Truncated = result.Count < available;
            return ok;
        }

        /// <summary>
        /// The clash tests on a model set, WITHOUT reading any results: the tests endpoint
        /// only, no resources call, no scope-file download. Used by the ACC self-check to say
        /// "a completed test exists" cheaply. The same array keys and the same completed-test
        /// rule as <see cref="GetClashesAsync"/>, so the two cannot disagree about what a
        /// completed test is. A 200 with no tests array is TransportFailed, never "no tests".
        /// </summary>
        public static async Task<AccFetchResult<AccClashTestSummary>> GetClashTestSummaryAsync(
            AccCredentials creds, string containerId, string modelSetId)
        {
            var summary = new AccClashTestSummary();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId))
                return AccFetchResult<AccClashTestSummary>.Failure(AccFetchStatus.NotFound, summary, 0,
                    "no ACC container id or model-set id was supplied");

            var got = await ReadAllTestsAsync(creds, AccIds.ForAcc(containerId), modelSetId).ConfigureAwait(false);
            if (!got.Succeeded)
                return AccFetchResult<AccClashTestSummary>.Failure(got.Status, summary, got.HttpStatus, got.Detail);
            var tests = got.Value;

            summary.TestCount = tests.Count;
            summary.States = tests.Select(t => (string)t["status"] ?? "?").Distinct().ToList();
            var latest = tests
                .Where(t => IsCompletedTest((string)t["status"]))
                .OrderByDescending(t => CompletedStamp(t))
                .FirstOrDefault();
            if (latest != null)
            {
                string tid = (string)(latest["clashTestId"] ?? latest["id"] ?? latest["testId"]);
                summary.LatestCompletedId = string.IsNullOrEmpty(tid) ? "(id not reported)" : tid;
                summary.LatestCompletedAt = (string)(latest["completedAt"] ?? latest["completedDate"] ?? latest["updatedAt"]) ?? "";
            }
            return AccFetchResult<AccClashTestSummary>.Success(summary, tests.Count == 0);
        }

        /// <summary>
        /// When a test completed, as a sortable ISO string. The reference names the field
        /// <c>completedOn</c> (get-model-set-clash-tests-GET); <c>completedAt</c> is what the
        /// earlier sample read, so both are accepted. Newtonsoft parses an ISO date into a
        /// Date token, whose (string) cast is culture-formatted and does NOT sort - so a date
        /// token is re-serialised round-trip.
        /// </summary>
        internal static string CompletedStamp(JToken test)
        {
            var t = test?["completedOn"] ?? test?["completedAt"] ?? test?["completedDate"] ?? test?["updatedAt"];
            if (t == null) return string.Empty;
            if (t.Type == JTokenType.Date)
            {
                var v = ((JValue)t).Value;
                if (v is DateTime dt) return dt.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                if (v is DateTimeOffset dto) return dto.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            }
            return (string)t ?? string.Empty;
        }

        /// <summary>
        /// The documents of one model set version (bim360/modelset/v3 …/modelsets/{id}/versions/{v}),
        /// keyed for the issue locator. Documented fields: documentVersions[].versionUrn,
        /// bubbleUrn, viewableGuid, displayName. A 200 without the array is TransportFailed.
        /// </summary>
        public static async Task<AccFetchResult<List<AccModelSetDocument>>> GetModelSetVersionDocumentsAsync(
            AccCredentials creds, string containerId, string modelSetId, int version)
        {
            var list = new List<AccModelSetDocument>();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId) || version <= 0)
                return AccFetchResult<List<AccModelSetDocument>>.Failure(AccFetchStatus.NotFound, list, 0,
                    "no container, model set or model set version to read");

            var got = await GetJsonAsync(creds,
                $"{ModelSetBase}/containers/{AccIds.ForAcc(containerId)}/modelsets/{modelSetId}/versions/{version}").ConfigureAwait(false);
            if (!got.Succeeded)
                return AccFetchResult<List<AccModelSetDocument>>.Failure(got.Status, list, got.HttpStatus, got.Detail);

            return ParseModelSetVersionDocuments(got.Value, got.HttpStatus);
        }

        /// <summary>
        /// The LATEST version of a model set with its documents (GET
        /// bim360/modelset/v3/containers/{c}/modelsets/{id}/versions/latest — Model Coordination
        /// v3 reference, "get-model-set-version-latest"). The federated compliance read needs the
        /// documents as ACC federates them now, not as they were when a clash test last ran.
        /// A 200 without a documentVersions array is TransportFailed, never "no documents".
        /// </summary>
        public static async Task<AccFetchResult<AccModelSetVersion>> GetLatestModelSetVersionAsync(
            AccCredentials creds, string containerId, string modelSetId)
        {
            var empty = new AccModelSetVersion();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId))
                return AccFetchResult<AccModelSetVersion>.Failure(AccFetchStatus.NotFound, empty, 0,
                    "no container or model set to read");

            var got = await GetJsonAsync(creds,
                $"{ModelSetBase}/containers/{AccIds.ForAcc(containerId)}/modelsets/{modelSetId}/versions/latest").ConfigureAwait(false);
            if (!got.Succeeded)
                return AccFetchResult<AccModelSetVersion>.Failure(got.Status, empty, got.HttpStatus, got.Detail);

            var docs = ParseModelSetVersionDocuments(got.Value, got.HttpStatus);
            if (!docs.Succeeded)
                return AccFetchResult<AccModelSetVersion>.Failure(docs.Status, empty, docs.HttpStatus, docs.Detail);

            var obj = got.Value as JObject;
            int version = 0;
            var v = obj?["version"];
            if (v != null && v.Type == JTokenType.Integer) version = (int)v;
            else if (v != null) int.TryParse((string)v, out version);
            return AccFetchResult<AccModelSetVersion>.Success(new AccModelSetVersion
            {
                Version = version,
                Status = (string)obj?["status"] ?? string.Empty,
                Documents = docs.Value,
            }, docs.Value.Count == 0);
        }

        private static AccFetchResult<List<AccModelSetDocument>> ParseModelSetVersionDocuments(JToken body, int httpStatus)
        {
            var list = new List<AccModelSetDocument>();
            var arr = AccFetchOutcome.FindArray(body, new[] { "documentVersions" });
            if (arr == null)
                return AccFetchResult<List<AccModelSetDocument>>.Failure(AccFetchStatus.TransportFailed, list, httpStatus,
                    "the model set version carried no 'documentVersions' array");
            foreach (var d in arr)
                list.Add(new AccModelSetDocument
                {
                    VersionUrn = (string)d["versionUrn"] ?? string.Empty,
                    BubbleUrn = (string)d["bubbleUrn"] ?? string.Empty,
                    ViewableGuid = (string)d["viewableGuid"] ?? string.Empty,
                    DisplayName = (string)d["displayName"] ?? string.Empty,
                });
            return AccFetchResult<List<AccModelSetDocument>>.Success(list, list.Count == 0);
        }

        private static AccFetchResult<List<AccClashRecord>> Fail(AccFetchStatus status, int httpStatus, string detail)
            => AccFetchResult<List<AccClashRecord>>.Failure(status, new List<AccClashRecord>(), httpStatus, detail);

        /// <summary>Map a document/file name to a coarse discipline token the
        /// ClashTriageEngine severity rule understands. ACC clash data has no
        /// Revit category, so this is a document-name heuristic — override by
        /// renaming source models to carry a discipline token.</summary>
        public static string DisciplineOst(string documentName, IReadOnlyDictionary<string, string> projectMap = null)
            => AccDisciplineResolver.Ost(documentName, projectMap);

        /// <summary>A clash test that has finished and produced results. The API reports
        /// Pending / Processing / Success / Failed; "Completed" is accepted as well. Anything
        /// else is not a result to read.</summary>
        internal static bool IsCompletedTest(string status)
        {
            string s = (status ?? string.Empty).Trim();
            return s.IndexOf("complet", StringComparison.OrdinalIgnoreCase) >= 0
                || s.Equals("success", StringComparison.OrdinalIgnoreCase)
                || s.Equals("successful", StringComparison.OrdinalIgnoreCase)
                || s.Equals("succeeded", StringComparison.OrdinalIgnoreCase);
        }

        // ── HTTP helpers ──
        private static async Task<AccFetchResult<JToken>> GetJsonAsync(AccCredentials creds, string url)
        {
            var resp = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Get, url), creds),
                creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                StingLog.Warn($"AccModelCoordSync GET {resp.Status}: {url} - {resp.Describe()}");
                return AccFetchResult<JToken>.Failure(resp.Classify(), null, resp.Status, resp.Describe());
            }
            try { return AccFetchResult<JToken>.Success(JToken.Parse(resp.Body), empty: false); }
            catch (Exception ex)
            {
                StingLog.Warn("AccModelCoordSync parse: " + ex.Message);
                return AccFetchResult<JToken>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    "the response body was not valid JSON: " + ex.Message);
            }
        }

        /// <summary>Download a scope resource and gunzip it to JSON. Pre-signed S3/
        /// CloudFront URLs carry their own auth; API-hosted resources (developer.api.
        /// autodesk.com) need the bearer, so we attach it when the host matches.</summary>
        private static async Task<AccFetchResult<JObject>> DownloadScopeAsync(string url, AccCredentials creds)
        {
            // Pre-signed S3/CloudFront URLs carry their own auth; API-hosted resources need the bearer.
            bool apiHosted = url.StartsWith(Host, StringComparison.OrdinalIgnoreCase);
            var resp = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url),
                apiHosted ? creds : null, idempotent: true, timeout: TimeSpan.FromMinutes(5), readBytes: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                StingLog.Warn($"ACC scope download {resp.Status}");
                return AccFetchResult<JObject>.Failure(resp.Classify(), null, resp.Status,
                    "clash scope-file download: " + resp.Describe());
            }
            try
            {
                var bytes = resp.Bytes ?? Array.Empty<byte>();
                string text = TryGunzip(bytes) ?? Encoding.UTF8.GetString(bytes);
                return AccFetchResult<JObject>.Success(JObject.Parse(text), empty: false);
            }
            catch (Exception ex)
            {
                StingLog.Warn("ACC scope parse: " + ex.Message);
                return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    "clash scope file could not be parsed: " + ex.Message);
            }
        }

        private static string TryGunzip(byte[] bytes)
        {
            // gzip magic 0x1f 0x8b
            if (bytes == null || bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var sr = new StreamReader(gz, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch (Exception ex) { StingLog.Warn("ACC gunzip: " + ex.Message); return null; }
        }

        private static string ResourceUrl(JArray resources, string includeKey, string[] excludes = null)
        {
            foreach (var r in resources)
            {
                string key = ((string)(r["type"] ?? r["name"] ?? r["id"]) ?? "").ToLowerInvariant();
                string url = (string)(r["url"] ?? r["signedUrl"] ?? r["href"]);
                if (string.IsNullOrEmpty(url)) continue;
                if (!key.Contains(includeKey) && !url.ToLowerInvariant().Contains(includeKey)) continue;
                if (excludes != null && excludes.Any(x => key.Contains(x) || url.ToLowerInvariant().Contains(x))) continue;
                return url;
            }
            return null;
        }

        private static long ParseLong(JToken t)
        {
            if (t == null) return 0;
            if (t.Type == JTokenType.Integer) return (long)t;
            return long.TryParse((string)t, out var v) ? v : 0;
        }
    }
}
