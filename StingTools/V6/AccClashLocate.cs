// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccClashLocate.cs — ACC-HARD-5: make an escalated clash FINDABLE.
//
// THE CONSTRAINT. ACC's Issues API cannot create a pushpin: linkedDocuments (the pushpin
// block) is read-only, and the issue-create permitted-attributes list says linkedDocument
// "is not applicable" (Issues.yaml in autodesk-platform-services/aps-sdk-openapi). So an
// issue STING raises from a clash cannot be pinned to the two objects, and the assignee
// used to get "object 17299 in KUT_MEP.rvt" and a search.
//
// WHAT THIS DOES INSTEAD (every step documented by APS; see docs/PLANSCAPE_PROTOCOL.md §5):
//
//   clash lvid/rvid (viewer dbIds)  +  ldid/rdid → document scope `urn` (version URN)
//     → model set version documentVersions[] (versionUrn → bubbleUrn, viewableGuid)
//     → Model Derivative GET  /modelderivative/v2/designdata/{b64 urn}/metadata       → view guid
//     → Model Derivative POST …/metadata/{guid}/properties:query {$in objectid}      → externalId
//     → externalId of a Revit object = its UniqueId
//     → planscape://revit/select?doc=…&uid=… in the issue description (PlanscapeProtocol)
//   and, per document version, Data Management GET data/v1/projects/{p}/versions/{v}
//     → data.links.webView.href — the ACC viewer URL the API itself returns (no URL is
//       assembled here; there is no documented URL shape to assemble).
//
// FAILURE RULE. Anything that does not resolve cleanly yields NO link for that side, with a
// reason that is counted and reported. The issue is still created. A dbId whose externalId
// is not a Revit UniqueId gives no link rather than a guessed one; a UniqueId that a model
// does not contain resolves to nothing in Revit, never to another element.
//
// Revit-free; logs through StingLog (the tests link a shim).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>One side of a clash, resolved (or not) to something a person can open.</summary>
    public sealed class AccObjectLocation
    {
        public string DocumentName { get; set; } = string.Empty;
        public string VersionUrn { get; set; } = string.Empty;
        public long DbId { get; set; }
        /// <summary>The Revit UniqueId, or empty when it could not be resolved.</summary>
        public string UniqueId { get; set; } = string.Empty;
        /// <summary>Why <see cref="UniqueId"/> is empty. Empty when resolved.</summary>
        public string Reason { get; set; } = string.Empty;
        /// <summary>ACC's own viewer URL for the document version (data.links.webView.href), or empty.</summary>
        public string ViewerUrl { get; set; } = string.Empty;
        public bool Resolved => !string.IsNullOrEmpty(UniqueId);
    }

    public sealed class AccClashLocation
    {
        public AccObjectLocation Left { get; set; } = new AccObjectLocation();
        public AccObjectLocation Right { get; set; } = new AccObjectLocation();
        public IEnumerable<AccObjectLocation> Sides { get { yield return Left; yield return Right; } }
    }

    /// <summary>What the locator achieved across a batch, for the command's report.</summary>
    public sealed class AccLocateSummary
    {
        public int Sides, Resolved, ViewerLinks;
        public List<string> Reasons { get; } = new List<string>();
        public int Unresolved => Sides - Resolved;

        public string Describe()
        {
            string s = $"located {Resolved} of {Sides} clash object(s) as Revit elements";
            if (ViewerLinks > 0) s += $", {ViewerLinks} ACC viewer link(s)";
            if (Unresolved > 0 && Reasons.Count > 0)
                s += "; not located: " + string.Join(" | ", Reasons.Take(3)) + (Reasons.Count > 3 ? " …" : "");
            return s;
        }
    }

    /// <summary>Model Derivative: viewer dbId → externalId. Cached per document version.</summary>
    public static class AccModelDerivative
    {
        /// <summary>Ids per properties:query request. The documented page limit is 1000; half
        /// keeps the request body small.</summary>
        internal const int QueryChunk = 500;

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, string> _guidByUrn = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _extByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Test seam: forget every cached lookup.</summary>
        internal static void ClearCache()
        {
            lock (_gate) { _guidByUrn.Clear(); _extByKey.Clear(); }
        }

        private static string Base => AccIssueSync.Host + "/modelderivative/v2/designdata";

        /// <summary>The URL-safe, unpadded Base64 form Model Derivative takes in its path.</summary>
        public static string EncodeUrn(string urn)
        {
            if (string.IsNullOrEmpty(urn)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(urn)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Map viewer dbIds to externalIds in one document version. Ids the model does not
        /// contain are simply absent from the returned map. A failure (404, 202 "still
        /// processing", unparseable body) is a failed result - never a partial guess.
        /// </summary>
        public static async Task<AccFetchResult<Dictionary<long, string>>> ResolveExternalIdsAsync(
            AccCredentials creds, string derivativeUrn, string preferredViewGuid, IEnumerable<long> dbIds)
        {
            var map = new Dictionary<long, string>();
            var wanted = (dbIds ?? Enumerable.Empty<long>()).Where(i => i > 0).Distinct().ToList();
            if (string.IsNullOrEmpty(derivativeUrn))
                return AccFetchResult<Dictionary<long, string>>.Failure(AccFetchStatus.NotFound, map, 0, "no document URN");
            if (wanted.Count == 0) return AccFetchResult<Dictionary<long, string>>.Success(map, empty: true);

            // Cache first: object ids are stable within one document VERSION, and the URN is
            // version-specific, so a hit is exact.
            var todo = new List<long>();
            lock (_gate)
                foreach (long id in wanted)
                {
                    if (_extByKey.TryGetValue(derivativeUrn + "|" + id.ToString(CultureInfo.InvariantCulture), out var ext)) map[id] = ext;
                    else todo.Add(id);
                }
            if (todo.Count == 0) return AccFetchResult<Dictionary<long, string>>.Success(map, empty: false);

            string b64 = EncodeUrn(derivativeUrn);
            var guid = await ResolveViewGuidAsync(creds, derivativeUrn, b64, preferredViewGuid).ConfigureAwait(false);
            if (!guid.Succeeded)
                return AccFetchResult<Dictionary<long, string>>.Failure(guid.Status, map, guid.HttpStatus, guid.Detail);

            for (int i = 0; i < todo.Count; i += QueryChunk)
            {
                var chunk = todo.Skip(i).Take(QueryChunk).ToList();
                var inArr = new JArray("objectid");
                foreach (long id in chunk) inArr.Add(id);
                var body = new JObject
                {
                    ["query"] = new JObject { ["$in"] = inArr },
                    ["fields"] = new JArray("objectid", "externalId"),
                    ["pagination"] = new JObject { ["offset"] = 0, ["limit"] = 1000 },
                };
                string payload = body.ToString(Newtonsoft.Json.Formatting.None);
                // A read, so retrying it is safe even though it is a POST.
                var resp = await AccHttp.SendAsync(() => WithMdRegion(new HttpRequestMessage(HttpMethod.Post,
                        $"{Base}/{b64}/metadata/{Uri.EscapeDataString(guid.Value)}/properties:query")
                    { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, creds),
                    creds, idempotent: true, timeout: TimeSpan.FromSeconds(45), maxAttempts: 3).ConfigureAwait(false);

                if (resp.Status == 202)
                    return AccFetchResult<Dictionary<long, string>>.Failure(AccFetchStatus.TransportFailed, map, 202,
                        "Model Derivative is still indexing this model's properties (HTTP 202)");
                if (!resp.IsSuccess)
                    return AccFetchResult<Dictionary<long, string>>.Failure(resp.Classify(), map, resp.Status,
                        "Model Derivative properties: " + resp.Describe());

                JArray coll;
                try { coll = JObject.Parse(resp.Body)?["data"]?["collection"] as JArray; }
                catch (Exception ex)
                {
                    return AccFetchResult<Dictionary<long, string>>.Failure(AccFetchStatus.TransportFailed, map, resp.Status,
                        "Model Derivative properties response was not JSON: " + ex.Message);
                }
                if (coll == null)
                    return AccFetchResult<Dictionary<long, string>>.Failure(AccFetchStatus.TransportFailed, map, resp.Status,
                        "Model Derivative properties response carried no data.collection");

                foreach (var o in coll)
                {
                    long oid = o["objectid"]?.Type == JTokenType.Integer ? (long)o["objectid"] : 0;
                    string ext = (string)o["externalId"];
                    // Only ids we asked for: a server that ignores the filter must not smuggle
                    // other objects into the map.
                    if (oid <= 0 || string.IsNullOrEmpty(ext) || !chunk.Contains(oid)) continue;
                    map[oid] = ext;
                    lock (_gate) _extByKey[derivativeUrn + "|" + oid.ToString(CultureInfo.InvariantCulture)] = ext;
                }
            }
            return AccFetchResult<Dictionary<long, string>>.Success(map, map.Count == 0);
        }

        /// <summary>Which Model View to query. The model set version names the geometry node
        /// the clash ran on (viewableGuid); when the metadata list carries it, it wins.
        /// Otherwise the master 3D view, then the first 3D view. No 3D view → failure.</summary>
        private static async Task<AccFetchResult<string>> ResolveViewGuidAsync(
            AccCredentials creds, string urn, string b64, string preferred)
        {
            lock (_gate)
                if (_guidByUrn.TryGetValue(urn, out var cached)) return AccFetchResult<string>.Success(cached, empty: false);

            var resp = await AccHttp.SendAsync(() => WithMdRegion(new HttpRequestMessage(HttpMethod.Get, $"{Base}/{b64}/metadata"), creds),
                creds, idempotent: true, timeout: TimeSpan.FromSeconds(45), maxAttempts: 3).ConfigureAwait(false);
            if (resp.Status == 202)
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, null, 202,
                    "Model Derivative has not finished processing this model version (HTTP 202)");
            if (!resp.IsSuccess)
                return AccFetchResult<string>.Failure(resp.Classify(), null, resp.Status, "Model Derivative metadata: " + resp.Describe());

            JArray views;
            try { views = JObject.Parse(resp.Body)?["data"]?["metadata"] as JArray; }
            catch (Exception ex)
            {
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    "Model Derivative metadata was not JSON: " + ex.Message);
            }
            string chosen = ChooseViewGuid(views, preferred);
            if (string.IsNullOrEmpty(chosen))
                return AccFetchResult<string>.Failure(AccFetchStatus.NotFound, null, resp.Status,
                    "the model version has no 3D Model View in Model Derivative");
            lock (_gate) _guidByUrn[urn] = chosen;
            return AccFetchResult<string>.Success(chosen, empty: false);
        }

        internal static string ChooseViewGuid(JArray views, string preferred)
        {
            if (views == null) return null;
            var threeD = views.Where(v => string.Equals((string)v["role"], "3d", StringComparison.OrdinalIgnoreCase))
                              .Select(v => (guid: (string)v["guid"], master: v["isMasterView"]?.Type == JTokenType.Boolean && (bool)v["isMasterView"]))
                              .Where(v => !string.IsNullOrEmpty(v.guid)).ToList();
            if (!string.IsNullOrEmpty(preferred) && threeD.Any(v => string.Equals(v.guid, preferred, StringComparison.OrdinalIgnoreCase)))
                return preferred;
            return threeD.FirstOrDefault(v => v.master).guid ?? threeD.FirstOrDefault().guid;
        }

        /// <summary>Model Derivative's region header is named <c>region</c> (modelderivative.yaml
        /// components/parameters/region), not the <c>x-ads-region</c> the ACC APIs take.</summary>
        internal static HttpRequestMessage WithMdRegion(HttpRequestMessage req, AccCredentials c)
        {
            string r = AccIds.NormaliseRegion(c?.Region);
            if (req != null && r.Length > 0) req.Headers.TryAddWithoutValidation("region", r);
            return req;
        }

        // ── Revit UniqueId recognition ───────────────────────────────────────

        private static readonly Regex UniqueIdPattern = new Regex(
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}-[0-9a-fA-F]{8}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>A Revit UniqueId: an episode GUID plus 8 hex digits of element id. For a
        /// Revit model the derivative's externalId has exactly this shape (the Fetch Specific
        /// Properties example shows one); anything else is not a Revit element and gets no link.</summary>
        public static bool IsRevitUniqueId(string s) => !string.IsNullOrEmpty(s) && UniqueIdPattern.IsMatch(s.Trim());
    }

    /// <summary>Data Management: the ACC viewer URL for a document version, as returned by the
    /// API in data.links.webView.href. Cached per version URN.</summary>
    public static class AccDocsWebView
    {
        private static readonly object _gate = new object();
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static void ClearCache() { lock (_gate) _cache.Clear(); }

        public static async Task<AccFetchResult<string>> GetViewerUrlAsync(AccCredentials creds, string versionUrn)
        {
            if (string.IsNullOrEmpty(versionUrn) || string.IsNullOrEmpty(creds?.ProjectId))
                return AccFetchResult<string>.Failure(AccFetchStatus.NotFound, null, 0, "no project or version URN");
            lock (_gate)
                if (_cache.TryGetValue(versionUrn, out var hit)) return AccFetchResult<string>.Success(hit, empty: false);

            string url = $"{AccIssueSync.Host}/data/v1/projects/{Uri.EscapeDataString(AccIds.ForDataManagement(creds.ProjectId))}" +
                         $"/versions/{Uri.EscapeDataString(versionUrn)}";
            var resp = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), creds,
                idempotent: true, timeout: TimeSpan.FromSeconds(45), maxAttempts: 3).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return AccFetchResult<string>.Failure(resp.Classify(), null, resp.Status, "Docs version lookup: " + resp.Describe());

            string href;
            try { href = (string)JObject.Parse(resp.Body)?["data"]?["links"]?["webView"]?["href"]; }
            catch (Exception ex)
            {
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, null, resp.Status, "Docs version response was not JSON: " + ex.Message);
            }
            // Only an absolute https URL the API gave us. Nothing is assembled or repaired.
            if (string.IsNullOrEmpty(href) || !Uri.TryCreate(href, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    "the Docs version carried no https webView link");
            lock (_gate) _cache[versionUrn] = href;
            return AccFetchResult<string>.Success(href, empty: false);
        }
    }

    /// <summary>Resolves a batch of clashes to Revit UniqueIds and viewer URLs.</summary>
    public static class AccIssueLocator
    {
        /// <summary>Most document versions one run will query. A model set is a handful of
        /// models; more than this is a sign something is wrong, not a reason to make 100 calls.</summary>
        internal const int MaxDocuments = 20;

        public static async Task<Dictionary<string, AccClashLocation>> LocateAsync(
            AccCredentials creds, string containerId, string modelSetId, IEnumerable<AccClashRecord> clashes,
            bool resolveIds, bool viewerLinks, AccLocateSummary summary)
        {
            summary = summary ?? new AccLocateSummary();
            var result = new Dictionary<string, AccClashLocation>(StringComparer.OrdinalIgnoreCase);
            var list = (clashes ?? Enumerable.Empty<AccClashRecord>()).Where(c => c != null && !string.IsNullOrEmpty(c.Id)).ToList();
            if (list.Count == 0 || (!resolveIds && !viewerLinks)) return result;

            // 1. The model set version's documents: versionUrn → bubble + view guid.
            var byVersion = new Dictionary<string, AccModelSetDocument>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, AccModelSetDocument>(StringComparer.OrdinalIgnoreCase);
            var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string msdFailure = null;
            foreach (int v in list.Select(c => c.ModelSetVersion).Where(v => v > 0).Distinct().Take(3))
            {
                AccFetchResult<List<AccModelSetDocument>> docs;
                try { docs = await AccModelCoordSync.GetModelSetVersionDocumentsAsync(creds, containerId, modelSetId, v).ConfigureAwait(false); }
                catch (Exception ex) { msdFailure = "model set version read failed: " + ex.Message; continue; }
                if (!docs.Succeeded) { msdFailure = "model set version read failed: " + docs.Detail; continue; }
                foreach (var d in docs.Value)
                {
                    if (!string.IsNullOrEmpty(d.VersionUrn)) byVersion[d.VersionUrn] = d;
                    if (string.IsNullOrEmpty(d.DisplayName)) continue;
                    if (byName.ContainsKey(d.DisplayName)) ambiguousNames.Add(d.DisplayName);
                    else byName[d.DisplayName] = d;
                }
            }
            if (msdFailure != null) StingLog.Warn("AccIssueLocator: " + msdFailure);

            // 2. Build every side, attached to the model set document it belongs to.
            var sides = new List<(AccObjectLocation loc, string derivUrn, string viewGuid)>();
            foreach (var c in list)
            {
                var loc = new AccClashLocation
                {
                    Left = new AccObjectLocation { DocumentName = c.LeftDocument, DbId = c.LeftObjectId, VersionUrn = c.LeftDocumentUrn ?? "" },
                    Right = new AccObjectLocation { DocumentName = c.RightDocument, DbId = c.RightObjectId, VersionUrn = c.RightDocumentUrn ?? "" },
                };
                result[c.Id] = loc;
                foreach (var side in loc.Sides)
                {
                    AccModelSetDocument msd = null;
                    if (side.VersionUrn.Length > 0) byVersion.TryGetValue(side.VersionUrn, out msd);
                    // No URN in the scope file: a display name that names exactly one document
                    // is still an exact join. A name shared by two documents is not.
                    if (msd == null && side.VersionUrn.Length == 0 && !string.IsNullOrEmpty(side.DocumentName) &&
                        !ambiguousNames.Contains(side.DocumentName))
                        byName.TryGetValue(side.DocumentName, out msd);
                    if (msd != null && side.VersionUrn.Length == 0) side.VersionUrn = msd.VersionUrn;

                    string deriv = !string.IsNullOrEmpty(msd?.BubbleUrn) ? msd.BubbleUrn : side.VersionUrn;
                    sides.Add((side, deriv, msd?.ViewableGuid ?? ""));
                    summary.Sides++;
                }
            }

            // 3. dbId → externalId, one batched lookup per document version.
            if (resolveIds)
            {
                var groups = sides.Where(s => !string.IsNullOrEmpty(s.derivUrn) && s.loc.DbId > 0)
                                  .GroupBy(s => s.derivUrn, StringComparer.Ordinal).ToList();
                int queried = 0;
                foreach (var g in groups)
                {
                    if (++queried > MaxDocuments)
                    {
                        foreach (var s in g) s.loc.Reason = $"more than {MaxDocuments} model versions in one run; not looked up";
                        continue;
                    }
                    string viewGuid = g.Select(s => s.viewGuid).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
                    AccFetchResult<Dictionary<long, string>> r;
                    try { r = await AccModelDerivative.ResolveExternalIdsAsync(creds, g.Key, viewGuid, g.Select(s => s.loc.DbId)).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        r = AccFetchResult<Dictionary<long, string>>.Failure(AccFetchStatus.TransportFailed, new Dictionary<long, string>(), 0, ex.Message);
                    }
                    foreach (var s in g)
                    {
                        if (!r.Succeeded) { s.loc.Reason = r.Detail; continue; }
                        if (!r.Value.TryGetValue(s.loc.DbId, out var ext))
                            s.loc.Reason = $"object {s.loc.DbId} is not in the model's property index";
                        else if (!AccModelDerivative.IsRevitUniqueId(ext))
                            s.loc.Reason = $"object {s.loc.DbId} is not a Revit element (externalId '{Short(ext)}')";
                        else s.loc.UniqueId = ext.Trim();
                    }
                }
                foreach (var s in sides.Where(s => !s.loc.Resolved && string.IsNullOrEmpty(s.loc.Reason)))
                    s.loc.Reason = s.loc.DbId <= 0 ? "ACC gave no object id for this side"
                                 : msdFailure != null ? "ACC gave no document URN for this model (" + msdFailure + ")"
                                 : "ACC gave no document URN for this model";
            }

            // 4. The ACC viewer URL, one lookup per document version.
            if (viewerLinks)
                foreach (var g in sides.Where(s => !string.IsNullOrEmpty(s.loc.VersionUrn))
                                       .GroupBy(s => s.loc.VersionUrn, StringComparer.Ordinal).Take(MaxDocuments))
                {
                    AccFetchResult<string> w;
                    try { w = await AccDocsWebView.GetViewerUrlAsync(creds, g.Key).ConfigureAwait(false); }
                    catch (Exception ex) { StingLog.Warn("AccIssueLocator webView: " + ex.Message); continue; }
                    if (!w.Succeeded) { StingLog.Warn($"AccIssueLocator: no viewer link for {g.Key}: {w.Detail}"); continue; }
                    foreach (var s in g) s.loc.ViewerUrl = w.Value;
                }

            summary.Resolved += sides.Count(s => s.loc.Resolved);
            summary.ViewerLinks += sides.Select(s => s.loc.ViewerUrl).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count();
            foreach (var r in sides.Where(s => !s.loc.Resolved && !string.IsNullOrEmpty(s.loc.Reason)).Select(s => s.loc.Reason).Distinct())
                summary.Reasons.Add(r);
            return result;
        }

        private static string Short(string s) => s == null ? "" : (s.Length > 40 ? s.Substring(0, 40) + "…" : s);
    }

    /// <summary>The text an escalated clash issue carries, inside ACC's 1000-character
    /// description limit. A URL is never cut: a link that does not fit whole is left out.</summary>
    public static class AccIssueLinks
    {
        public const int DescriptionLimit = 1000;

        /// <summary>The lines to append for one clash: Revit select link(s), ACC viewer link(s),
        /// and - when a side could not be located - one short line saying why.</summary>
        public static List<string> BuildLinkLines(AccClashLocation loc, bool deepLinks, bool viewerLinks)
        {
            var lines = new List<string>();
            if (loc == null) return lines;

            if (deepLinks)
            {
                // One link per model: the two sides of a clash are usually in different models
                // (one open, one linked), and a single-model link can be acted on by whichever
                // model is open.
                foreach (var g in loc.Sides.Where(s => s.Resolved).GroupBy(s => s.DocumentName ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    string link = PlanscapeProtocol.BuildRevitSelectLink(g.Key, g.Select(s => s.UniqueId).ToList(), out _);
                    if (link != null) lines.Add($"Select in Revit ({ShortDoc(g.Key)}): {link}");
                }
                var failed = loc.Sides.Where(s => !s.Resolved && !string.IsNullOrEmpty(s.Reason)).ToList();
                if (failed.Count > 0)
                    lines.Add("No Revit link for " + string.Join("; ", failed.Select(s => $"{ShortDoc(s.DocumentName)} object {s.DbId}: {Cap(s.Reason, 90)}")));
            }
            if (viewerLinks)
                foreach (var g in loc.Sides.Where(s => !string.IsNullOrEmpty(s.ViewerUrl)).GroupBy(s => s.ViewerUrl, StringComparer.Ordinal))
                    lines.Add($"Open {ShortDoc(g.First().DocumentName)} in ACC: {g.Key}");
            return lines;
        }

        /// <summary>
        /// Base text first, then as many whole link lines as fit, in order. The base text is
        /// trimmed (never below <paramref name="baseFloor"/> characters) to make room for
        /// links; a line that still does not fit is dropped whole, never truncated.
        /// </summary>
        public static string ComposeDescription(string baseText, IList<string> linkLines,
            int max = DescriptionLimit, int baseFloor = 200)
        {
            baseText = (baseText ?? string.Empty).TrimEnd();
            int floor = Math.Min(baseText.Length, baseFloor);
            var kept = new List<string>();
            int linksLen = 0;
            foreach (var line in linkLines ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int add = line.Length + 1;   // newline before it
                if (floor + linksLen + add > max) continue;
                kept.Add(line);
                linksLen += add;
            }
            int room = max - linksLen;
            string head = baseText.Length <= room ? baseText : baseText.Substring(0, Math.Max(0, room - 1)) + "…";
            return kept.Count == 0 ? head : head + "\n" + string.Join("\n", kept);
        }

        private static string ShortDoc(string name)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(name ?? string.Empty);
            if (n.Length == 0) n = "model";
            return n.Length > 28 ? n.Substring(0, 28) : n;
        }

        private static string Cap(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s ?? "" : s.Substring(0, n - 1) + "…";
    }
}
