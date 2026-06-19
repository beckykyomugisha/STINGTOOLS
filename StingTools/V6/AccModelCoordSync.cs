// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccModelCoordSync.cs
//
// Autodesk Construction Cloud (ACC) Model Coordination — READ client.
//
// Closes the "clash in ACC AND STING" loop: ACC Model Coordination is the
// system of record; this client PULLS ACC's grouped clash results so STING
// can triage them (ClashTriageEngine) and push prioritised Issues back via
// AccIssueSync.PushIssueAsync. The push half already existed (AccIssueSync);
// this is the missing read half.
//
// Reuses AccCredentials + AccIssueSync.EnsureAuthAsync for OAuth/token —
// no second credential store, no duplicate refresh logic.
//
// MVP scope: raw HttpClient + Newtonsoft.Json against the documented
// Model Coordination v3 endpoints. The exact v3 path prefix and the grouped-
// clash JSON field names are parsed DEFENSIVELY (multiple candidate keys) and
// marked // TODO-VERIFY-API — confirm against current APS docs before the
// engagement relies on the field mapping. A wrong path fails soft (logs the
// HTTP status, returns an empty list) rather than throwing.
//
//   Endpoints (APS, host https://developer.api.autodesk.com):
//     GET  bim360/modelset/v3/containers/{containerId}/modelsets
//     GET  bim360/clash/v3/containers/{containerId}/modelsets/{modelSetId}/clashes/grouped
//
//   The container id is the ACC project's coordination container; for most
//   projects this is the same id used for Issues (AccCredentials.ProjectId).

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>One grouped clash pulled from ACC Model Coordination.</summary>
    public sealed class AccClashGroup
    {
        public string Id { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Status { get; set; } = "open";
        public long LeftObjectId { get; set; }
        public long RightObjectId { get; set; }
        public string LeftCategory { get; set; } = string.Empty;
        public string RightCategory { get; set; } = string.Empty;
        public string LeftDocument { get; set; } = string.Empty;
        public string RightDocument { get; set; } = string.Empty;
    }

    public sealed class AccModelSet
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public static class AccModelCoordSync
    {
        private static readonly HttpClient _http = new HttpClient();

        // TODO-VERIFY-API: confirm the v3 service prefixes against current APS docs.
        private const string Host = "https://developer.api.autodesk.com";
        private const string ModelSetBase = Host + "/bim360/modelset/v3";
        private const string ClashBase    = Host + "/bim360/clash/v3";

        /// <summary>List the coordination model sets in the ACC container.</summary>
        public static async Task<List<AccModelSet>> ListModelSetsAsync(AccCredentials creds, string containerId)
        {
            var list = new List<AccModelSet>();
            if (string.IsNullOrEmpty(containerId)) return list;
            var json = await GetAsync(creds,
                $"{ModelSetBase}/containers/{containerId}/modelsets").ConfigureAwait(false);
            if (json == null) return list;
            // Defensive: APS has returned model sets under both "modelSets" and a bare array.
            var arr = json["modelSets"] as JArray ?? json["results"] as JArray ?? json as JArray;
            if (arr == null) return list;
            foreach (var m in arr)
            {
                list.Add(new AccModelSet
                {
                    Id   = (string)(m["modelSetId"] ?? m["id"]) ?? string.Empty,
                    Name = (string)(m["name"] ?? m["title"]) ?? "(unnamed model set)",
                });
            }
            return list;
        }

        /// <summary>Pull grouped clash results for a model set.</summary>
        public static async Task<List<AccClashGroup>> GetGroupedClashesAsync(
            AccCredentials creds, string containerId, string modelSetId, int max = 500)
        {
            var list = new List<AccClashGroup>();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId)) return list;
            var json = await GetAsync(creds,
                $"{ClashBase}/containers/{containerId}/modelsets/{modelSetId}/clashes/grouped").ConfigureAwait(false);
            if (json == null) return list;

            // TODO-VERIFY-API: grouped-clash payload shape. Parse defensively across
            // the field names APS has used (clashGroups / groups / results; clashData
            // left/right object + document ids).
            var groups = json["clashGroups"] as JArray ?? json["groups"] as JArray ?? json["results"] as JArray;
            if (groups == null) return list;

            foreach (var g in groups)
            {
                if (list.Count >= max) break;
                var data = g["clashData"] ?? g;
                list.Add(new AccClashGroup
                {
                    Id           = (string)(g["id"] ?? g["groupId"] ?? g["clashGroupId"]) ?? Guid.NewGuid().ToString("N"),
                    Count        = (int?)(g["count"] ?? g["clashCount"]) ?? 1,
                    Status       = (string)(g["status"] ?? "open"),
                    LeftObjectId  = ParseLong(data["leftObjectId"] ?? data["lvid1"] ?? data["objectId1"]),
                    RightObjectId = ParseLong(data["rightObjectId"] ?? data["lvid2"] ?? data["objectId2"]),
                    LeftCategory  = (string)(data["leftCategory"] ?? data["category1"]) ?? string.Empty,
                    RightCategory = (string)(data["rightCategory"] ?? data["category2"]) ?? string.Empty,
                    LeftDocument  = (string)(data["leftDocument"] ?? data["documentId1"]) ?? string.Empty,
                    RightDocument = (string)(data["rightDocument"] ?? data["documentId2"]) ?? string.Empty,
                });
            }
            return list;
        }

        // ── shared authenticated GET with 429 back-off ──
        private static async Task<JToken> GetAsync(AccCredentials creds, string url)
        {
            if (!await AccIssueSync.EnsureAuthAsync(creds).ConfigureAwait(false))
            {
                StingLog.Warn("AccModelCoordSync: auth failed (check acc_credentials.json refresh token).");
                return null;
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if ((int)resp.StatusCode == 429)
                {
                    int wait = 1 << attempt;
                    StingLog.Warn($"ACC MC 429 — retrying in {wait}s");
                    await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"AccModelCoordSync GET {(int)resp.StatusCode}: {url}");
                    return null;
                }
                try { return JToken.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false)); }
                catch (Exception ex) { StingLog.Warn("AccModelCoordSync parse: " + ex.Message); return null; }
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
