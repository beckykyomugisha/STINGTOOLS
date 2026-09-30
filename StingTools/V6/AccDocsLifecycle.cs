// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccDocsLifecycle.cs
//
// Why this file exists: superseding or replacing a deliverable changed deliverables.json
// and nothing in the CDE. The document the whole team actually reads — the ACC Docs copy
// — stayed in its SHARED/PUBLISHED folder, still labelled with its live suitability, so a
// superseded drawing looked current to everyone outside Revit.
//
// This client retires an ACC document into the project's ARCHIVE folder and stamps the
// retirement suitability (AB superseded/withdrawn, AR archived) on it.
//
// Revit-free and log-free (StingTools.Acc.Tests links it and drives it over loopback).
//
// ── API FACTS (official APS Data Management reference) ────────────────────────────
//   There is NO move endpoint for an item in BIM 360 / ACC Docs. PATCH items/{id} updates
//   an item's attributes; it does not re-parent it. Autodesk's own guidance
//   ("Move files around on Forge", APS blog) is copy, then retire the original.
//   POST /data/v1/projects/{project_id}/items?copyFrom={version URN, URL-encoded}
//        "This only works on BIM 360 Docs" (which is also the ACC Docs backend).
//        Content-Type application/vnd.api+json; the body names the TARGET folder:
//        {"jsonapi":{"version":"1.0"},
//         "data":{"type":"items","relationships":{"parent":{"data":{"type":"folders","id":"<folder urn>"}}}}}
//        201 → data.id = new item URN; the new tip version is data.relationships.tip.data.id
//        (and/or included[0].id of type "versions").
//        https://aps.autodesk.com/en/docs/data/v2/reference/http/projects-project_id-items-POST/
//   project_id here is the Data Management id WITH the "b." prefix; the Docs custom
//   attribute API (AccDocsMetadata) wants it WITHOUT — each client normalises its own.
//
// ── What "retire" does, in order (each step reported; never a silent partial) ─────
//   1. copy the version into the ARCHIVE folder;
//   2. stamp the COPY with the retirement suitability + ARCHIVE state;
//   3. stamp the ORIGINAL with the same, so anyone who opens the old link sees it retired.
//   The original is NOT deleted: deleting a CDE document is a records decision, not an
//   automation, and a copy that exists with an un-retired original is still reported as
//   a failure (step 3) so the operator finishes it by hand.
//
// ── NOT CONFIRMED against a live tenant ───────────────────────────────────────────
//   * Whether copyFrom carries custom attribute values across (assumed not; step 2 sets them).
//   * The exact 201 shape for copyFrom (tip relationship vs included[]); both are read.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>Outcome of retiring one ACC document.</summary>
    public sealed class AccRetireResult
    {
        public bool Ok { get; set; }
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public string ArchivedItemUrn { get; set; } = string.Empty;
        public string ArchivedVersionUrn { get; set; } = string.Empty;
        public bool CopyStamped { get; set; }
        public bool OriginalStamped { get; set; }
        /// <summary>One line per step, in order, success or failure.</summary>
        public List<string> Steps { get; } = new List<string>();
        public string Detail => string.Join("\n", Steps);
    }

    public static class AccDocsLifecycle
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>Test seam: point at a loopback listener. Production never calls this.</summary>
        internal static void OverrideHostForTests(string host)
            => _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');

        /// <summary>The Data Management API wants the "b." prefix; add it when absent.</summary>
        public static string DmProjectId(string projectId)
        {
            string p = (projectId ?? string.Empty).Trim();
            if (p.Length == 0) return p;
            return p.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? p : "b." + p;
        }

        /// <summary>The copyFrom request body: the target folder, nothing else.</summary>
        internal static JObject BuildCopyBody(string targetFolderUrn) => new JObject
        {
            ["jsonapi"] = new JObject { ["version"] = "1.0" },
            ["data"] = new JObject
            {
                ["type"] = "items",
                ["relationships"] = new JObject
                {
                    ["parent"] = new JObject
                    {
                        ["data"] = new JObject { ["type"] = "folders", ["id"] = targetFolderUrn },
                    },
                },
            },
        };

        /// <summary>Copy a document version into <paramref name="targetFolderUrn"/>.
        /// Value = (new item URN, new version URN); a 2xx without an item id is a failure.</summary>
        public static async Task<AccFetchResult<(string itemUrn, string versionUrn)>> CopyToFolderAsync(
            string accessToken, string projectId, string versionUrn, string targetFolderUrn)
        {
            var empty = (string.Empty, string.Empty);
            string bad = Check(accessToken, projectId, versionUrn, targetFolderUrn);
            if (bad != null) return AccFetchResult<(string, string)>.Failure(AccFetchStatus.NotFound, empty, 0, bad);

            string url = $"{_host}/data/v1/projects/{Uri.EscapeDataString(DmProjectId(projectId))}/items" +
                         $"?copyFrom={Uri.EscapeDataString(versionUrn.Trim())}";
            int status; string body;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                req.Content = new StringContent(BuildCopyBody(targetFolderUrn.Trim()).ToString(Formatting.None),
                                                Encoding.UTF8, "application/vnd.api+json");
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                status = (int)resp.StatusCode;
                body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
            {
                return AccFetchResult<(string, string)>.Failure(AccFetchStatus.TransportFailed, empty, 0,
                    "the copy request did not complete: " + ex.Message);
            }

            if (status < 200 || status >= 300)
            {
                var kind = AccFetchOutcome.Classify(status, 0);
                return AccFetchResult<(string, string)>.Failure(kind, empty, status,
                    $"copying {versionUrn} to {targetFolderUrn}: {AccFetchOutcome.Describe(kind, status)}");
            }

            string item = null, ver = null;
            try
            {
                var root = JToken.Parse(body);
                item = (string)root?["data"]?["id"];
                ver = (string)root?["data"]?["relationships"]?["tip"]?["data"]?["id"];
                if (string.IsNullOrWhiteSpace(ver) && root?["included"] is JArray inc)
                    ver = inc.OfType<JObject>()
                             .Where(o => string.Equals((string)o["type"], "versions", StringComparison.Ordinal))
                             .Select(o => (string)o["id"]).FirstOrDefault();
            }
            catch (JsonException) { /* handled below */ }
            if (string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(ver))
                return AccFetchResult<(string, string)>.Failure(AccFetchStatus.TransportFailed, empty, status,
                    $"copy of {versionUrn} returned HTTP {status} without an item and version id — treated as not copied");
            return AccFetchResult<(string, string)>.Success((item.Trim(), ver.Trim()), false);
        }

        /// <summary>
        /// Retire a document: copy into <paramref name="archiveFolderUrn"/>, then stamp the copy
        /// and the original with <paramref name="retireSuitability"/> (AB or AR) and ARCHIVE.
        /// <paramref name="sourceFolderUrn"/> is the original's folder (for its attribute
        /// definitions); when blank, the original is not stamped and that is reported.
        /// </summary>
        public static async Task<AccRetireResult> RetireAsync(string accessToken, string projectId,
            string versionUrn, string sourceFolderUrn, string archiveFolderUrn, string retireSuitability,
            AccAttributeNames names = null)
        {
            // The project's own attribute names (docsAttributeNames): the default constants
            // would stamp attributes a project that renamed them does not have.
            names = names ?? AccAttributeNames.Default;
            var r = new AccRetireResult();
            string suit = (retireSuitability ?? string.Empty).Trim().ToUpperInvariant();
            if (suit != "AB" && suit != "AR")
            {
                r.Status = AccFetchStatus.NotFound;
                r.Steps.Add($"refused: '{retireSuitability}' is not a retirement suitability (AB or AR) — nothing was changed");
                return r;
            }

            var copy = await CopyToFolderAsync(accessToken, projectId, versionUrn, archiveFolderUrn).ConfigureAwait(false);
            if (!copy.Succeeded)
            {
                r.Status = copy.Status;
                r.Steps.Add("1. copy to ARCHIVE: FAILED — " + copy.Detail + " (nothing was changed)");
                return r;
            }
            r.ArchivedItemUrn = copy.Value.itemUrn;
            r.ArchivedVersionUrn = copy.Value.versionUrn;
            r.Steps.Add($"1. copy to ARCHIVE: ok — {r.ArchivedItemUrn}");

            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [names.Suitability] = suit,
                [names.CdeState] = "ARCHIVE",
            };

            r.CopyStamped = await StampAsync(accessToken, projectId, archiveFolderUrn, r.ArchivedVersionUrn,
                                             values, "2. stamp the archived copy", r).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(sourceFolderUrn))
                r.Steps.Add("3. stamp the original: NOT DONE — its folder is not recorded, so its attribute " +
                            "definitions cannot be read. Set its suitability to " + suit + " in ACC by hand.");
            else
                r.OriginalStamped = await StampAsync(accessToken, projectId, sourceFolderUrn, versionUrn,
                                                     values, "3. stamp the original", r).ConfigureAwait(false);

            r.Ok = r.CopyStamped && r.OriginalStamped;
            r.Status = r.Ok ? AccFetchStatus.Ok : AccFetchStatus.TransportFailed;
            return r;
        }

        private static async Task<bool> StampAsync(string token, string projectId, string folderUrn, string version,
            IDictionary<string, string> values, string label, AccRetireResult r)
        {
            var defs = await AccDocsMetadata.ListDefinitionsAsync(token, projectId, folderUrn).ConfigureAwait(false);
            if (!defs.Succeeded)
            {
                r.Steps.Add($"{label}: FAILED — attribute definitions unreadable: {defs.Detail}");
                return false;
            }
            var w = await AccDocsMetadata.SetVersionAttributesAsync(token, projectId, version, values, defs.Value)
                                         .ConfigureAwait(false);
            if (!w.Succeeded || !w.Value.IsConfirmed)
            {
                r.Steps.Add($"{label}: FAILED — " + (w.Succeeded
                    ? "ACC did not confirm " + string.Join(", ", w.Value.NotConfirmed)
                    : w.Detail));
                return false;
            }
            r.Steps.Add($"{label}: ok — {string.Join(", ", values.Select(kv => kv.Key + "=" + kv.Value))}");
            return true;
        }

        private static string Check(string token, string projectId, string versionUrn, string folderUrn)
        {
            if (string.IsNullOrWhiteSpace(token)) return "no ACC access token was supplied";
            if (string.IsNullOrWhiteSpace(DmProjectId(projectId))) return "no ACC project id was supplied";
            if (string.IsNullOrWhiteSpace(versionUrn) || !versionUrn.Trim().StartsWith("urn:", StringComparison.Ordinal))
                return $"'{versionUrn}' is not a version URN";
            if (!AccCdeRouting.LooksLikeFolderUrn(folderUrn))
                return $"'{folderUrn}' is not a folder URN — set acc_settings.json \"cdeFolders\".\"ARCHIVE\"";
            return null;
        }
    }
}
