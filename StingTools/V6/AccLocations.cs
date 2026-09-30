// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccLocations.cs
//
// The ACC Location Breakdown Structure (LBS) and how it lines up with the STING tag
// vocabulary (LOC / ZONE codes and the model's level codes). Issues, Forms, Assets, Photos
// and RFIs in ACC hang off LBS nodes; STING tags hang off LOC/ZONE/LVL. When the two drift,
// an issue raised "at Level 2 / East Wing" in ACC cannot be traced to a tagged element.
//
// SOURCE (APS docs, read 2026-09-30):
//   GET construction/locations/v2/projects/{projectId}/trees/default/nodes?limit=&offset=
//   3-legged only ("user context required"), scope data:read. ACC projects only — "not
//   compatible with BIM 360 projects". limit 1-10000 (default 10000), pagination.totalResults.
//   Node: id, parentId (null for the root), type (Root / Area / Level), name, barcode, order.
//   Permission for a plain member: not stated; Locations is read by every ACC tool that
//   shows a location picker, so a member is EXPECTED to be allowed. UNCONFIRMED live.
//
// Revit-free and log-free; the comparison is pure and unit-tested.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    public sealed class AccLocationNode
    {
        public string Id { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public int Order { get; set; }
        /// <summary>"Project > Block A > Level 2", computed from parent links.</summary>
        public string Path { get; set; } = string.Empty;
        public int Depth { get; set; }
        public bool IsRoot => string.IsNullOrEmpty(ParentId) || string.Equals(Type, "Root", StringComparison.OrdinalIgnoreCase);
    }

    public static class AccLocationsClient
    {
        public static async Task<AccFetchResult<List<AccLocationNode>>> GetTreeAsync(AccCredentials creds, int pageSize = 10000)
        {
            creds = creds ?? new AccCredentials();
            string pid = AccIds.ForAcc(creds.ProjectId);
            if (pid.Length == 0)
                return AccFetchResult<List<AccLocationNode>>.Failure(AccFetchStatus.NotFound, new List<AccLocationNode>(), 0,
                    "no ACC project is configured for this model");

            var nodes = new List<AccLocationNode>();
            int offset = 0;
            for (int page = 0; page < 100; page++)
            {
                string url = $"{AccIssueSync.Host}/construction/locations/v2/projects/{Uri.EscapeDataString(pid)}/trees/default/nodes?limit={pageSize}&offset={offset}";
                var resp = await AccHttp.SendAsync(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    return AccIssueSync.WithRegion(req, creds);
                }, creds, idempotent: true, timeout: TimeSpan.FromSeconds(60)).ConfigureAwait(false);

                if (resp.Auth != null && !resp.Auth.Ok)
                    return AccFetchResult<List<AccLocationNode>>.Failure(resp.Auth.Status, nodes, 0, resp.Auth.Detail);
                if (!resp.IsSuccess)
                {
                    string why = resp.Status == 403
                        ? "Autodesk refused the Locations read (HTTP 403) — this sign-in lacks data:read or has no access to Locations in this project"
                        : resp.Status == 404
                            ? "Autodesk returned HTTP 404 for the locations tree — the project id is wrong, or this is a BIM 360 project (the Locations API is ACC-only)"
                            : resp.Describe();
                    return AccFetchResult<List<AccLocationNode>>.Failure(resp.Classify(), nodes, resp.Status,
                        "reading the ACC locations tree failed" + (page > 0 ? $" on page {page + 1} (the tree is INCOMPLETE)" : "") + ": " + why);
                }
                JObject j;
                try { j = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<List<AccLocationNode>>.Failure(AccFetchStatus.TransportFailed, nodes, resp.Status,
                        "the locations tree was not valid JSON: " + ex.Message);
                }
                var results = j["results"] as JArray;
                if (results == null)
                    return AccFetchResult<List<AccLocationNode>>.Failure(AccFetchStatus.TransportFailed, nodes, resp.Status,
                        "the locations response carried no 'results' array — the Locations v2 payload shape has changed");
                foreach (var t in results)
                {
                    string id = (string)t["id"] ?? "";
                    if (id.Length == 0) continue;
                    nodes.Add(new AccLocationNode
                    {
                        Id = id,
                        ParentId = (string)t["parentId"] ?? "",
                        Type = (string)t["type"] ?? "",
                        Name = ((string)t["name"] ?? "").Trim(),
                        Barcode = ((string)t["barcode"] ?? "").Trim(),
                        Order = (int?)t["order"] ?? 0,
                    });
                }
                int? total = (int?)j["pagination"]?["totalResults"];
                offset += results.Count;
                bool last = total.HasValue ? offset >= total.Value || results.Count == 0 : results.Count < pageSize;
                if (last)
                {
                    ComputePaths(nodes);
                    return AccFetchResult<List<AccLocationNode>>.Success(nodes, nodes.All(n => n.IsRoot));
                }
            }
            return AccFetchResult<List<AccLocationNode>>.Failure(AccFetchStatus.TransportFailed, nodes, 200,
                "stopped after 100 pages without reaching the last — the locations tree is INCOMPLETE");
        }

        internal static void ComputePaths(List<AccLocationNode> nodes)
        {
            var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var n in nodes)
            {
                var names = new List<string>();
                var cur = n;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (cur != null && seen.Add(cur.Id))
                {
                    names.Add(cur.Name);
                    cur = !string.IsNullOrEmpty(cur.ParentId) && byId.TryGetValue(cur.ParentId, out var p) ? p : null;
                }
                names.Reverse();
                n.Path = string.Join(" > ", names);
                n.Depth = names.Count - 1;
            }
        }
    }

    // ── The comparison ───────────────────────────────────────────────────────────

    /// <summary>One STING code the tags can carry.</summary>
    public sealed class StingSpatialCode
    {
        /// <summary>LOC, ZONE or LVL.</summary>
        public string Kind { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        /// <summary>The Revit name behind a LVL code ("Level 2"); empty for LOC/ZONE.</summary>
        public string SourceName { get; set; } = string.Empty;
    }

    public sealed class AccLocationMatch
    {
        public AccLocationNode Node { get; set; }
        public StingSpatialCode Code { get; set; }
        /// <summary>name / barcode / token / level-name.</summary>
        public string How { get; set; } = string.Empty;
    }

    public sealed class AccLocationMismatch
    {
        public AccLocationNode Node { get; set; }
        public StingSpatialCode Code { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class AccLocationCheckResult
    {
        public int NodesChecked { get; set; }
        public List<AccLocationMatch> Matches { get; } = new List<AccLocationMatch>();
        public List<AccLocationNode> NodesWithoutCode { get; } = new List<AccLocationNode>();
        public List<StingSpatialCode> CodesWithoutNode { get; } = new List<StingSpatialCode>();
        public List<AccLocationMismatch> NameMismatches { get; } = new List<AccLocationMismatch>();
    }

    public static class AccLocationCheck
    {
        /// <summary>
        /// Compare the LBS with the STING vocabulary. A node matches a code when (in order):
        /// its name IS the code, its barcode IS the code, the code appears in its name as a
        /// whole token ("Zone Z01", "BLD1 - East"), or — for LVL — the level code derived from
        /// the node's name (<paramref name="levelCodeFromName"/>, the same rule the tagger
        /// uses) is a model level code. A LVL match whose node name differs from the Revit
        /// level's name is a NAME MISMATCH: both sides mean the same level but a person
        /// reading one will not recognise the other. The root node is never compared.
        /// Placeholder codes ("XX", "ZZ", empty) are ignored.
        /// </summary>
        public static AccLocationCheckResult Compare(IEnumerable<AccLocationNode> nodes, IEnumerable<StingSpatialCode> codes,
            Func<string, string> levelCodeFromName)
        {
            var result = new AccLocationCheckResult();
            var codeList = (codes ?? Enumerable.Empty<StingSpatialCode>())
                .Where(c => c != null && !IsPlaceholder(c.Code))
                .GroupBy(c => c.Kind + "|" + c.Code.Trim().ToUpperInvariant())
                .Select(g => g.First()).ToList();
            var matched = new HashSet<StingSpatialCode>();

            foreach (var n in (nodes ?? Enumerable.Empty<AccLocationNode>()).Where(x => x != null && !x.IsRoot))
            {
                result.NodesChecked++;
                var hits = new List<AccLocationMatch>();
                foreach (var c in codeList)
                {
                    string how = null;
                    if (Same(n.Name, c.Code)) how = "name";
                    else if (n.Barcode.Length > 0 && Same(n.Barcode, c.Code)) how = "barcode";
                    else if (HasToken(n.Name, c.Code)) how = "token";
                    else if (c.Kind == "LVL" && levelCodeFromName != null)
                    {
                        string derived = SafeCode(levelCodeFromName, n.Name);
                        if (derived.Length > 0 && Same(derived, c.Code)) how = "level-name";
                    }
                    if (how != null) hits.Add(new AccLocationMatch { Node = n, Code = c, How = how });
                }
                if (hits.Count == 0) { result.NodesWithoutCode.Add(n); continue; }
                foreach (var h in hits)
                {
                    result.Matches.Add(h);
                    matched.Add(h.Code);
                    if (h.Code.Kind == "LVL" && h.Code.SourceName.Length > 0 && !Same(n.Name, h.Code.SourceName) && !Same(n.Name, h.Code.Code))
                        result.NameMismatches.Add(new AccLocationMismatch
                        {
                            Node = n, Code = h.Code,
                            Detail = $"ACC calls it '{n.Name}', the model's level is '{h.Code.SourceName}' (both {h.Code.Code})",
                        });
                    else if (h.How == "barcode" && !Same(n.Name, h.Code.Code) && !HasToken(n.Name, h.Code.Code))
                        result.NameMismatches.Add(new AccLocationMismatch
                        {
                            Node = n, Code = h.Code,
                            Detail = $"matched only by barcode '{n.Barcode}'; the ACC name '{n.Name}' does not carry the code {h.Code.Code}",
                        });
                }
            }
            foreach (var c in codeList)
                if (!matched.Contains(c)) result.CodesWithoutNode.Add(c);
            return result;
        }

        internal static bool IsPlaceholder(string code)
        {
            string c = (code ?? "").Trim().ToUpperInvariant();
            return c.Length == 0 || c == "XX" || c == "ZZ" || c == "XXX" || c == "NA" || c == "N/A";
        }

        private static bool Same(string a, string b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whole-token containment: the characters either side are not letters or digits.</summary>
        internal static bool HasToken(string text, string code)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(code)) return false;
            return Regex.IsMatch(text, @"(?<![A-Za-z0-9])" + Regex.Escape(code.Trim()) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
        }

        private static string SafeCode(Func<string, string> f, string name)
        {
            try { string c = f(name) ?? ""; return IsPlaceholder(c) ? "" : c; }
            catch (Exception) { return ""; }
        }
    }
}
