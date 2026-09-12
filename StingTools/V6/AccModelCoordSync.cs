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
        /// <summary>Factor converting DistanceM → mm (from AccCredentials.DistToMm; default 1000 = metres).</summary>
        public double DistToMm { get; set; } = 1000.0;
        public double PenetrationMm => Math.Abs(DistanceM) * DistToMm;
    }

    public static class AccModelCoordSync
    {
        private static readonly HttpClient _http = new HttpClient();

        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point the client at a loopback listener. Production never
        /// calls this; StingTools.Acc.Tests does, to prove end-to-end that a 404 does not
        /// become EmptyOk. Pass null to restore the real APS host.</summary>
        internal static void OverrideHostForTests(string host)
            => _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');

        /// <summary>Test seam: the 429 back-off. Production waits; tests replace it with a
        /// no-op so the suite does not sleep. Production timings are never altered.</summary>
        internal static Func<TimeSpan, Task> DelayHook = t => Task.Delay(t);

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
            for (int page = 0; page < 50; page++)
            {
                var got = await GetJsonAsync(creds,
                    $"{ModelSetBase}/containers/{containerId}/modelsets?limit={pageSize}&offset={offset}").ConfigureAwait(false);
                if (!got.Succeeded)
                    return AccFetchResult<List<AccModelSet>>.Failure(got.Status, list, got.HttpStatus, got.Detail);

                var arr = AccFetchOutcome.FindArray(got.Value, new[] { "modelSets", "results" });
                if (arr == null)
                    // A 200 that carries no recognised array is a schema/sub-path change,
                    // never "no model sets". Reporting it as empty is the bug this closes.
                    return AccFetchResult<List<AccModelSet>>.Failure(AccFetchStatus.TransportFailed, list, got.HttpStatus,
                        "the model-set response carried no 'modelSets' array — the APS sub-path or payload shape has changed");
                if (arr.Count == 0) break;

                int added = 0;
                foreach (var m in arr)
                {
                    string id = (string)(m["modelSetId"] ?? m["id"]) ?? string.Empty;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                    list.Add(new AccModelSet { Id = id, Name = (string)(m["name"] ?? m["title"]) ?? "(unnamed model set)" });
                    added++;
                }
                if (arr.Count < pageSize || added == 0) break;  // last page, or endpoint ignored offset
                offset += pageSize;
            }
            return AccFetchResult<List<AccModelSet>>.Success(list, list.Count == 0);
        }

        // ── Clashes (tests -> resources -> scope files -> join) ──
        public static async Task<AccFetchResult<List<AccClashRecord>>> GetClashesAsync(
            AccCredentials creds, string containerId, string modelSetId, int max = 1000)
        {
            var result = new List<AccClashRecord>();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId))
                return Fail(AccFetchStatus.NotFound, 0,
                    "no ACC container id or model-set id was supplied");

            // 1. latest completed clash test
            var testsGot = await GetJsonAsync(creds,
                $"{ClashBase}/containers/{containerId}/modelsets/{modelSetId}/tests").ConfigureAwait(false);
            if (!testsGot.Succeeded) return Fail(testsGot.Status, testsGot.HttpStatus, testsGot.Detail);

            var tests = AccFetchOutcome.FindArray(testsGot.Value, new[] { "tests", "results" });
            if (tests == null)
                return Fail(AccFetchStatus.TransportFailed, testsGot.HttpStatus,
                    "the clash-test response carried no 'tests' array — the bim360/clash/v3 tests sub-path or payload shape has changed");
            if (tests.Count == 0)
            {
                // Genuinely nothing: the endpoint answered and said there are no tests.
                StingLog.Info("ACC MC: no clash tests on model set.");
                return AccFetchResult<List<AccClashRecord>>.Success(result, empty: true);
            }

            var latest = tests
                .Where(t => ((string)(t["status"]) ?? "").IndexOf("complet", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(t => (string)(t["completedAt"] ?? t["completedDate"] ?? t["updatedAt"]) ?? "")
                .FirstOrDefault() ?? tests.First();
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
            foreach (var d in documentScope?["documents"] as JArray ?? new JArray())
            {
                string id = (string)(d["id"] ?? d["clashDocId"]) ?? "";
                if (id.Length > 0) docNameById[id] = (string)(d["name"] ?? d["displayName"]) ?? id;
            }

            var clashArr = clashScope.Value["clashes"] as JArray;
            if (clashArr == null)
                return Fail(AccFetchStatus.TransportFailed, clashScope.HttpStatus,
                    "the clash scope file carried no 'clashes' array — the scope-file schema has changed");

            foreach (var c in clashArr)
            {
                if (result.Count >= max) break;
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
                    DistToMm      = creds.DistToMm,
                });
            }
            return AccFetchResult<List<AccClashRecord>>.Success(result, result.Count == 0);
        }

        private static AccFetchResult<List<AccClashRecord>> Fail(AccFetchStatus status, int httpStatus, string detail)
            => AccFetchResult<List<AccClashRecord>>.Failure(status, new List<AccClashRecord>(), httpStatus, detail);

        /// <summary>Map a document/file name to a coarse discipline token the
        /// ClashTriageEngine severity rule understands. ACC clash data has no
        /// Revit category, so this is a document-name heuristic — override by
        /// renaming source models to carry a discipline token.</summary>
        public static string DisciplineOst(string documentName)
        {
            string n = (documentName ?? "").ToUpperInvariant();
            if (n.Contains("STRUCT") || n.Contains("-S-") || n.Contains("_S_") || n.Contains("STR"))
                return "OST_StructuralFraming";
            if (n.Contains("DUCT") || n.Contains("HVAC") || n.Contains("MECH") || n.Contains("-M-"))
                return "OST_DuctCurves";
            if (n.Contains("PIPE") || n.Contains("PLUMB") || n.Contains("-P-"))
                return "OST_PipeCurves";
            if (n.Contains("ELEC") || n.Contains("-E-") || n.Contains("CABLE") || n.Contains("TRAY"))
                return "OST_ElectricalEquipment";
            if (n.Contains("FIRE") || n.Contains("SPRINK") || n.Contains("-FP-"))
                return "OST_Sprinklers";
            return ""; // architectural / unknown -> triage treats as non-structural, non-services
        }

        // ── HTTP helpers ──
        private static async Task<AccFetchResult<JToken>> GetJsonAsync(AccCredentials creds, string url)
        {
            if (!await AccIssueSync.EnsureAuthAsync(creds).ConfigureAwait(false))
            {
                StingLog.Warn("AccModelCoordSync: auth failed (check acc_credentials.json refresh token).");
                return AccFetchResult<JToken>.Failure(AccFetchStatus.AuthFailed, null, 0,
                    "no ACC access token could be obtained — the refresh token in acc_credentials.json was rejected or is absent");
            }
            int lastStatus = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                    using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                    lastStatus = (int)resp.StatusCode;
                    if (lastStatus == 429)
                    {
                        await DelayHook(TimeSpan.FromSeconds(1 << attempt)).ConfigureAwait(false);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        StingLog.Warn($"AccModelCoordSync GET {lastStatus}: {url}");
                        var status = AccFetchOutcome.Classify(lastStatus, -1);
                        return AccFetchResult<JToken>.Failure(status, null, lastStatus,
                            AccFetchOutcome.Describe(status, lastStatus));
                    }
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { return AccFetchResult<JToken>.Success(JToken.Parse(body), empty: false); }
                    catch (Exception ex)
                    {
                        StingLog.Warn("AccModelCoordSync parse: " + ex.Message);
                        return AccFetchResult<JToken>.Failure(AccFetchStatus.TransportFailed, null, lastStatus,
                            "the response body was not valid JSON: " + ex.Message);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                {
                    // A network failure is a failure. It must never reach the caller as an
                    // empty result — that is the shape that made a broken pull look clean.
                    StingLog.Warn("AccModelCoordSync GET transport: " + ex.Message);
                    return AccFetchResult<JToken>.Failure(AccFetchStatus.TransportFailed, null, 0,
                        "the request did not complete: " + ex.Message);
                }
            }
            return AccFetchResult<JToken>.Failure(AccFetchStatus.TransportFailed, null, lastStatus,
                $"Autodesk rate-limited the request (HTTP {lastStatus}) after 4 attempts");
        }

        /// <summary>Download a scope resource and gunzip it to JSON. Pre-signed S3/
        /// CloudFront URLs carry their own auth; API-hosted resources (developer.api.
        /// autodesk.com) need the bearer, so we attach it when the host matches.</summary>
        private static async Task<AccFetchResult<JObject>> DownloadScopeAsync(string url, AccCredentials creds)
        {
            int code = 0;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (url.StartsWith(Host, StringComparison.OrdinalIgnoreCase) && creds != null && !string.IsNullOrEmpty(creds.AccessToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                code = (int)resp.StatusCode;
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"ACC scope download {code}");
                    var status = AccFetchOutcome.Classify(code, -1);
                    return AccFetchResult<JObject>.Failure(status, null, code,
                        "clash scope-file download: " + AccFetchOutcome.Describe(status, code));
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string text = TryGunzip(bytes) ?? Encoding.UTF8.GetString(bytes);
                return AccFetchResult<JObject>.Success(JObject.Parse(text), empty: false);
            }
            catch (Exception ex)
            {
                StingLog.Warn("ACC scope download/parse: " + ex.Message);
                return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, null, code,
                    "clash scope file could not be downloaded or parsed: " + ex.Message);
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
