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
// A wrong path fails soft (logs the HTTP status, returns empty) — never throws.

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

        // N-G4: ACC clash "dist" units are not guaranteed metres. The factor that
        // converts the raw distance to millimetres is configurable (acc.json
        // distToMm, default 1000 = metres). GetClashesAsync stamps it per record.
        public double DistToMm { get; set; } = 1000.0;
        public double PenetrationMm => Math.Abs(DistanceM) * DistToMm;
    }

    public static class AccModelCoordSync
    {
        private static readonly HttpClient _http = new HttpClient();

        private const string Host = "https://developer.api.autodesk.com";
        private const string ModelSetBase = Host + "/bim360/modelset/v3";
        private const string ClashBase    = Host + "/bim360/clash/v3";

        // ── Model sets ──
        public static async Task<List<AccModelSet>> ListModelSetsAsync(AccCredentials creds, string containerId)
        {
            var list = new List<AccModelSet>();
            if (string.IsNullOrEmpty(containerId)) return list;
            var json = await GetJsonAsync(creds, $"{ModelSetBase}/containers/{containerId}/modelsets").ConfigureAwait(false);
            var arr = json?["modelSets"] as JArray ?? json?["results"] as JArray ?? json as JArray;
            if (arr == null) return list;
            foreach (var m in arr)
                list.Add(new AccModelSet
                {
                    Id   = (string)(m["modelSetId"] ?? m["id"]) ?? string.Empty,
                    Name = (string)(m["name"] ?? m["title"]) ?? "(unnamed model set)",
                });
            return list;
        }

        // ── Clashes (tests -> resources -> scope files -> join) ──
        public static async Task<List<AccClashRecord>> GetClashesAsync(
            AccCredentials creds, string containerId, string modelSetId, int max = 1000, double distToMm = 1000.0)
        {
            var result = new List<AccClashRecord>();
            if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(modelSetId)) return result;

            // 1. latest completed clash test
            var testsJson = await GetJsonAsync(creds,
                $"{ClashBase}/containers/{containerId}/modelsets/{modelSetId}/tests").ConfigureAwait(false);
            var tests = testsJson?["tests"] as JArray ?? testsJson?["results"] as JArray;
            if (tests == null || tests.Count == 0) { StingLog.Info("ACC MC: no clash tests on model set."); return result; }

            var latest = tests
                .Where(t => ((string)(t["status"]) ?? "").IndexOf("complet", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(t => (string)(t["completedAt"] ?? t["completedDate"] ?? t["updatedAt"]) ?? "")
                .FirstOrDefault() ?? tests.First();
            string testId = (string)(latest["clashTestId"] ?? latest["id"] ?? latest["testId"]) ?? "";
            if (string.IsNullOrEmpty(testId)) { StingLog.Warn("ACC MC: clash test id missing."); return result; }

            // 2. resources (pre-signed scope-file URLs)
            var resJson = await GetJsonAsync(creds,
                $"{ClashBase}/containers/{containerId}/tests/{testId}/resources").ConfigureAwait(false);
            var resources = resJson?["resources"] as JArray ?? resJson as JArray;
            if (resources == null) { StingLog.Warn("ACC MC: clash test resources missing."); return result; }

            string clashUrl    = ResourceUrl(resources, "clash",    excludes: new[] { "instance", "issue" });
            string instanceUrl = ResourceUrl(resources, "instance");
            string documentUrl = ResourceUrl(resources, "document");
            if (clashUrl == null || instanceUrl == null)
            {
                StingLog.Warn("ACC MC: clash/instance scope resource URL not found.");
                return result;
            }

            // 3. download + gunzip the scope files
            var clashScope    = await DownloadScopeAsync(creds, clashUrl).ConfigureAwait(false);
            var instanceScope = await DownloadScopeAsync(creds, instanceUrl).ConfigureAwait(false);
            var documentScope = documentUrl != null ? await DownloadScopeAsync(creds, documentUrl).ConfigureAwait(false) : null;
            if (clashScope == null || instanceScope == null) return result;

            // 4. join. instances are keyed by cid (== clash id); first instance per cid wins.
            var instByCid = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (var ins in instanceScope["instances"] as JArray ?? new JArray())
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

            foreach (var c in clashScope["clashes"] as JArray ?? new JArray())
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
                    DistToMm      = distToMm,
                    LeftObjectId  = ParseLong(ins?["lvid"]),
                    RightObjectId = ParseLong(ins?["rvid"]),
                    LeftDocument  = ldid != null && docNameById.TryGetValue(ldid, out var ln) ? ln : (ldid ?? ""),
                    RightDocument = rdid != null && docNameById.TryGetValue(rdid, out var rn) ? rn : (rdid ?? ""),
                });
            }
            return result;
        }

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
        private static async Task<JToken> GetJsonAsync(AccCredentials creds, string url)
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
                if ((int)resp.StatusCode == 429) { await Task.Delay(TimeSpan.FromSeconds(1 << attempt)).ConfigureAwait(false); continue; }
                if (!resp.IsSuccessStatusCode) { StingLog.Warn($"AccModelCoordSync GET {(int)resp.StatusCode}: {url}"); return null; }
                try { return JToken.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false)); }
                catch (Exception ex) { StingLog.Warn("AccModelCoordSync parse: " + ex.Message); return null; }
            }
            return null;
        }

        /// <summary>Download a scope resource and gunzip it to JSON.
        /// N-G5: pre-signed (e.g. S3) URLs carry their own auth and ignore a
        /// bearer; but a resource hosted on developer.api.autodesk.com needs the
        /// OAuth bearer or it 403s. Send the bearer only for the APS host.</summary>
        private static async Task<JObject> DownloadScopeAsync(AccCredentials creds, string url)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (Uri.TryCreate(url, UriKind.Absolute, out var u) &&
                    u.Host.Equals("developer.api.autodesk.com", StringComparison.OrdinalIgnoreCase) &&
                    creds != null && !string.IsNullOrEmpty(creds.AccessToken))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                }
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { StingLog.Warn($"ACC scope download {(int)resp.StatusCode}"); return null; }
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string text = TryGunzip(bytes) ?? Encoding.UTF8.GetString(bytes);
                return JObject.Parse(text);
            }
            catch (Exception ex) { StingLog.Warn("ACC scope download/parse: " + ex.Message); return null; }
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
