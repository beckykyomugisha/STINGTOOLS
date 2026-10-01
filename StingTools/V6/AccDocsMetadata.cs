// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccDocsMetadata.cs
//
// Why this file exists: an upload that lands a drawing in the right ACC folder but with
// no ISO 19650 metadata leaves the reviewer reading the suitability out of the title
// block. ACC Docs custom attributes are the CDE's own metadata columns; this client
// reads the folder's attribute definitions, creates the STING ones when (and only when)
// explicitly allowed, and stamps values on an uploaded version.
//
// Revit-free and log-free (StingTools.Acc.Tests links it). It takes a bare access-token
// string, not AccCredentials, so it does not depend on the auth code.
//
// ── API FACTS (official APS reference, "Custom Attributes (beta)") ────────────────
//   GET  /bim360/docs/v1/projects/{project_id}/folders/{folder_id}/custom-attribute-definitions
//        scope data:read · ?limit=1..200 (default 10) &offset · 200 {results[{id:int,name,type,arrayValues}],pagination}
//        https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-custom-attribute-definitions-GET/
//   POST same path · scope data:write · body {name, type: string|date|array, arrayValues}
//        ONE definition per call · name unique within the folder · 201 {id,name,type,arrayValues}
//        https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-custom-attribute-definitions-POST/
//   POST /bim360/docs/v1/projects/{project_id}/versions/{version_id}/custom-attributes:batch-update
//        scope data:write · body is a BARE ARRAY [{id, value}] · null value clears ·
//        text max 255 · date ISO 8601 · drop-list value must be one of arrayValues ·
//        200 {results[{id,name,type,value}]}
//        https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-custom-attributesbatch-update-POST/
//   project_id is the Data Management id WITHOUT the "b." prefix (the reference says
//   so explicitly); folder_id and version_id are URL-ENCODED URNs
//   (urn%3Aadsk.wipprod%3Afs.file%3Avf.…%3Fversion%3D1).
//
// ── NOT CONFIRMED against a live tenant ───────────────────────────────────────────
//   * Inheritance: the APS blog announcing the beta says GET returns "definitions of
//     current folder and those inherited from parent folders". The reference page does
//     not say so. So definitions are created on the TARGET folder, and a name present
//     via inheritance counts as present.
//   * 429 / Retry-After behaviour is not documented for these endpoints; it is handled
//     the way APS rate limits are documented elsewhere (Retry-After seconds).
//   * The request body documents `id` as string while every example sends a number;
//     the number the GET returned is sent back as-is.
//   * Duplicate-name create: undocumented status (400 or 409). Either is a failure here.
//   * Whether the ACC project's "Document naming standard" (GET
//     /bim360/docs/v1/projects/{id}/naming-standards/{id}, configured in the UI only,
//     one per project, id at folder data.attributes.extension.data.namingStandardIds)
//     rejects uploads whose file name does not conform — not tested here.

using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>A custom attribute definition as ACC returned it.</summary>
    public sealed class AccAttributeDefinition
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>"string", "date" or "array".</summary>
        public string Type { get; set; } = string.Empty;
        public List<string> ArrayValues { get; set; } = new List<string>();
        public override string ToString() => $"{Name} [{Type}, id {Id}]";
    }

    /// <summary>The outcome of checking a folder for the STING attribute set.
    /// Every required name lands in exactly one of Existing, Created, Missing,
    /// TypeMismatch or CreateFailed — nothing is silently skipped.</summary>
    public sealed class AccDefinitionReport
    {
        /// <summary>Already on the folder (directly or, if ACC does so, inherited).</summary>
        public List<AccAttributeDefinition> Existing { get; } = new List<AccAttributeDefinition>();
        /// <summary>Created by this call.</summary>
        public List<AccAttributeDefinition> Created { get; } = new List<AccAttributeDefinition>();
        /// <summary>Absent and NOT created, because creation was not allowed.</summary>
        public List<string> Missing { get; } = new List<string>();
        /// <summary>Present under the name but with a different type; not usable, not altered.</summary>
        public List<string> TypeMismatch { get; } = new List<string>();
        /// <summary>Name -> reason a create was attempted and failed.</summary>
        public Dictionary<string, string> CreateFailed { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Every definition usable for stamping: Existing + Created.</summary>
        public IReadOnlyList<AccAttributeDefinition> Usable => Existing.Concat(Created).ToList();

        /// <summary>True only when every required attribute exists with the right type.</summary>
        public bool IsComplete => Missing.Count == 0 && TypeMismatch.Count == 0 && CreateFailed.Count == 0;
    }

    /// <summary>What a batch-update wrote.</summary>
    public sealed class AccAttributeWriteReport
    {
        /// <summary>Attribute name -> value ACC echoed back.</summary>
        public Dictionary<string, string> Written { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Names whose echoed value differs from what was sent (or was not echoed).</summary>
        public List<string> NotConfirmed { get; } = new List<string>();
        public bool IsConfirmed => NotConfirmed.Count == 0;
    }

    public static class AccDocsMetadata
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point at a loopback listener. Production never calls this.</summary>
        internal static void OverrideHostForTests(string host)
            => _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');

        /// <summary>Test seam: the 429 back-off wait (same shape as AccIssueSync.DelayHook).</summary>
        internal static Func<TimeSpan, Task> DelayHook = t => Task.Delay(t);

        /// <summary>Total attempts per request, including the first. Bounded so a
        /// persistently rate-limited upload fails visibly instead of hanging.</summary>
        public const int MaxAttempts = 4;
        /// <summary>Longest single wait honoured from a Retry-After header.</summary>
        public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
        private const int PageLimit = 200;          // documented max
        private const int MaxPages = 50;            // 10,000 definitions: a runaway guard, not a real limit

        private static string DocsBase => _host + "/bim360/docs/v1";

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>The Docs API wants the project id without the Data Management "b."
        /// prefix (documented). An id already without it passes through unchanged.</summary>
        public static string DocsProjectId(string projectId)
        {
            string p = (projectId ?? string.Empty).Trim();
            return p.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? p.Substring(2) : p;
        }

        // ── List ───────────────────────────────────────────────────────────────

        /// <summary>Every custom attribute definition visible on a folder, all pages.
        /// A 200 without a <c>results</c> array, or an entry without id/name/type, is a
        /// TransportFailed — never an empty folder, because "no definitions" would lead
        /// EnsureDefinitionsAsync to create duplicates.</summary>
        public static async Task<AccFetchResult<List<AccAttributeDefinition>>> ListDefinitionsAsync(
            string accessToken, string projectId, string folderUrn, AccCredentials creds = null)
        {
            var empty = new List<AccAttributeDefinition>();
            string bad = CheckInputs(accessToken, projectId, folderUrn, "folder URN");
            if (bad != null)
                return AccFetchResult<List<AccAttributeDefinition>>.Failure(AccFetchStatus.NotFound, empty, 0, bad);

            string baseUrl = $"{DocsBase}/projects/{Uri.EscapeDataString(DocsProjectId(projectId))}" +
                             $"/folders/{Uri.EscapeDataString(folderUrn.Trim())}/custom-attribute-definitions";
            var all = new List<AccAttributeDefinition>();
            int offset = 0;
            for (int page = 0; page < MaxPages; page++)
            {
                string url = $"{baseUrl}?limit={PageLimit}&offset={offset}";
                var resp = await SendAsync(accessToken, HttpMethod.Get, url, null, creds, idempotent: true).ConfigureAwait(false);
                if (resp.Failure != null)
                    return AccFetchResult<List<AccAttributeDefinition>>.Failure(resp.Failure.Value, empty, resp.Status,
                        $"listing custom attributes on folder {folderUrn}: {resp.Detail}");

                var kind = AccFetchOutcome.ClassifyArrayBody(resp.Status, resp.Body, "results");
                if (kind != AccFetchStatus.Ok && kind != AccFetchStatus.EmptyOk)
                    return AccFetchResult<List<AccAttributeDefinition>>.Failure(kind, empty, resp.Status,
                        $"listing custom attributes on folder {folderUrn}: {AccFetchOutcome.Describe(kind, resp.Status)}");

                var arr = AccFetchOutcome.FindArray(resp.Body, "results");
                foreach (var t in arr)
                {
                    var def = ParseDefinition(t);
                    if (def == null)
                        return AccFetchResult<List<AccAttributeDefinition>>.Failure(AccFetchStatus.TransportFailed, empty,
                            resp.Status, $"a custom attribute definition on folder {folderUrn} has no usable id/name/type: " +
                                         Shorten(t.ToString(Formatting.None)));
                    all.Add(def);
                }

                offset += arr.Count;
                int? total = TotalResults(resp.Body);
                bool more = total.HasValue ? offset < total.Value : arr.Count >= PageLimit;
                if (arr.Count == 0 || !more)
                    return AccFetchResult<List<AccAttributeDefinition>>.Success(all, all.Count == 0);
            }
            return AccFetchResult<List<AccAttributeDefinition>>.Failure(AccFetchStatus.TransportFailed, empty, 200,
                $"custom attribute listing on folder {folderUrn} did not finish within {MaxPages} pages — the pagination did not terminate");
        }

        // ── Ensure ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Check the folder for <paramref name="required"/> (default: the STING set) and,
        /// only when <paramref name="allowCreate"/> is true, create the missing ones.
        ///
        /// Status: a listing failure fails the call with an empty report. A create that
        /// fails with an auth/transport status fails the call too, carrying the partial
        /// report in Value. Otherwise Success — and the caller MUST read
        /// <see cref="AccDefinitionReport.IsComplete"/>: Missing / TypeMismatch name what
        /// cannot be stamped.
        /// </summary>
        public static async Task<AccFetchResult<AccDefinitionReport>> EnsureDefinitionsAsync(
            string accessToken, string projectId, string folderUrn, bool allowCreate,
            IEnumerable<AccAttributeSpec> required = null, AccCredentials creds = null)
        {
            var report = new AccDefinitionReport();
            var listed = await ListDefinitionsAsync(accessToken, projectId, folderUrn, creds).ConfigureAwait(false);
            if (!listed.Succeeded)
                return AccFetchResult<AccDefinitionReport>.Failure(listed.Status, report, listed.HttpStatus, listed.Detail);

            string createUrl = $"{DocsBase}/projects/{Uri.EscapeDataString(DocsProjectId(projectId))}" +
                               $"/folders/{Uri.EscapeDataString(folderUrn.Trim())}/custom-attribute-definitions";

            foreach (var spec in (required ?? AccDocsAttributeSet.All))
            {
                var matches = listed.Value.Where(d => string.Equals(d.Name, spec.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1)
                {
                    report.TypeMismatch.Add($"{spec.Name} (defined {matches.Count} times on this folder or its parents — ambiguous)");
                    continue;
                }
                if (matches.Count == 1)
                {
                    var m = matches[0];
                    if (spec.Accepts(m.Type)) report.Existing.Add(m);
                    else report.TypeMismatch.Add($"{spec.Name} (exists as '{m.Type}', STING needs '{spec.Type}')");
                    continue;
                }
                if (!allowCreate) { report.Missing.Add(spec.Name); continue; }

                var body = new JObject { ["name"] = spec.Name, ["type"] = spec.Type };
                if (string.Equals(spec.Type, "array", StringComparison.OrdinalIgnoreCase))
                    body["arrayValues"] = new JArray(spec.ArrayValues);

                var resp = await SendAsync(accessToken, HttpMethod.Post, createUrl, body.ToString(Formatting.None), creds, idempotent: false).ConfigureAwait(false);
                if (resp.Failure != null || resp.Status < 200 || resp.Status >= 300)
                {
                    var kind = resp.Failure ?? AccFetchOutcome.Classify(resp.Status, 0);
                    string why = resp.Failure != null ? resp.Detail : AccFetchOutcome.Describe(kind, resp.Status);
                    report.CreateFailed[spec.Name] = why;
                    // An auth or transport failure will fail every following create the same way.
                    if (kind == AccFetchStatus.AuthFailed || resp.Status == 0 || resp.Status == 429)
                        return AccFetchResult<AccDefinitionReport>.Failure(kind, report, resp.Status,
                            $"creating custom attribute '{spec.Name}' on folder {folderUrn}: {why}");
                    continue;
                }

                AccAttributeDefinition created = null;
                try { created = ParseDefinition(JToken.Parse(resp.Body)); } catch (JsonException) { }
                if (created == null)
                    report.CreateFailed[spec.Name] = $"ACC answered HTTP {resp.Status} but returned no definition id — cannot stamp it";
                else
                    report.Created.Add(created);
            }
            return AccFetchResult<AccDefinitionReport>.Success(report, false);
        }

        // ── Set values ─────────────────────────────────────────────────────────

        /// <summary>
        /// Stamp attribute values on one version. <paramref name="valuesByName"/> is keyed by
        /// attribute NAME (e.g. AccDocsAttributeSet.Suitability) and resolved to ids through
        /// <paramref name="definitions"/> (from ListDefinitionsAsync / EnsureDefinitionsAsync
        /// .Usable). A null value CLEARS the attribute.
        ///
        /// All-or-nothing on the client side: an unknown name, an ambiguous name, a text
        /// value over 255 characters or a drop-list value not in the list refuses the
        /// whole write (NotFound / TransportFailed with the names in Detail) before any
        /// request — a partial stamp would look like a complete one in the ACC grid.
        /// </summary>
        public static async Task<AccFetchResult<AccAttributeWriteReport>> SetVersionAttributesAsync(
            string accessToken, string projectId, string versionUrn,
            IDictionary<string, string> valuesByName, IEnumerable<AccAttributeDefinition> definitions,
            AccCredentials creds = null)
        {
            var report = new AccAttributeWriteReport();
            string bad = CheckInputs(accessToken, projectId, versionUrn, "version URN");
            if (bad != null)
                return AccFetchResult<AccAttributeWriteReport>.Failure(AccFetchStatus.NotFound, report, 0, bad);
            if (valuesByName == null || valuesByName.Count == 0)
                return AccFetchResult<AccAttributeWriteReport>.Failure(AccFetchStatus.TransportFailed, report, 0,
                    "no attribute values were supplied — nothing to stamp");

            var plan = BuildBatchBody(valuesByName, definitions, out var sentByName, out string refusal);
            if (plan == null)
                return AccFetchResult<AccAttributeWriteReport>.Failure(AccFetchStatus.NotFound, report, 0, refusal);

            string url = $"{DocsBase}/projects/{Uri.EscapeDataString(DocsProjectId(projectId))}" +
                         $"/versions/{Uri.EscapeDataString(versionUrn.Trim())}/custom-attributes:batch-update";
            // batch-update sets values: sending the same plan twice leaves the same state.
            var resp = await SendAsync(accessToken, HttpMethod.Post, url, plan.ToString(Formatting.None), creds, idempotent: true).ConfigureAwait(false);
            if (resp.Failure != null)
                return AccFetchResult<AccAttributeWriteReport>.Failure(resp.Failure.Value, report, resp.Status,
                    $"stamping custom attributes on {versionUrn}: {resp.Detail}");

            var kind = AccFetchOutcome.ClassifyArrayBody(resp.Status, resp.Body, "results");
            if (kind != AccFetchStatus.Ok && kind != AccFetchStatus.EmptyOk)
                return AccFetchResult<AccAttributeWriteReport>.Failure(kind, report, resp.Status,
                    $"stamping custom attributes on {versionUrn}: {AccFetchOutcome.Describe(kind, resp.Status)}");

            // Confirm from the echo. The response lists the version's attributes with values.
            var echoed = new Dictionary<long, string>();
            foreach (var t in AccFetchOutcome.FindArray(resp.Body, "results"))
            {
                var idTok = t["id"];
                if (idTok == null || !long.TryParse(idTok.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                var v = t["value"];
                echoed[id] = v == null || v.Type == JTokenType.Null ? null : v.ToString();
            }
            foreach (var kv in sentByName)
            {
                if (echoed.TryGetValue(kv.Value.Id, out string got) && ValueMatches(kv.Value.Type, kv.Value.Sent, got))
                    report.Written[kv.Key] = got;
                else
                    report.NotConfirmed.Add(kv.Key);
            }
            return AccFetchResult<AccAttributeWriteReport>.Success(report, report.Written.Count == 0);
        }

        /// <summary>The batch-update body: a bare JSON array of {id, value}. Null when
        /// refused; <paramref name="refusal"/> names every offending attribute.</summary>
        internal static JArray BuildBatchBody(IDictionary<string, string> valuesByName,
            IEnumerable<AccAttributeDefinition> definitions,
            out Dictionary<string, (long Id, string Type, string Sent)> sentByName, out string refusal)
        {
            sentByName = new Dictionary<string, (long, string, string)>(StringComparer.Ordinal);
            var defs = (definitions ?? Enumerable.Empty<AccAttributeDefinition>()).ToList();
            var problems = new List<string>();
            var body = new JArray();

            foreach (var kv in valuesByName)
            {
                var m = defs.Where(d => string.Equals(d.Name, kv.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (m.Count == 0) { problems.Add($"'{kv.Key}' has no definition on this folder"); continue; }
                if (m.Count > 1) { problems.Add($"'{kv.Key}' is defined {m.Count} times — ambiguous"); continue; }
                var d = m[0];
                string v = kv.Value;
                if (v != null)
                {
                    string type = (d.Type ?? string.Empty).ToLowerInvariant();
                    if (type == "string" && v.Length > AccDocsAttributeSet.MaxStringLength)
                    { problems.Add($"'{kv.Key}' is {v.Length} characters (ACC max {AccDocsAttributeSet.MaxStringLength})"); continue; }
                    if (type == "array" && !d.ArrayValues.Contains(v, StringComparer.Ordinal))
                    { problems.Add($"'{kv.Key}' value '{v}' is not one of its drop-list values"); continue; }
                    if (type == "date" && !DateTime.TryParse(v, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
                    { problems.Add($"'{kv.Key}' value '{v}' is not an ISO 8601 date"); continue; }
                }
                body.Add(new JObject { ["id"] = d.Id, ["value"] = v == null ? JValue.CreateNull() : new JValue(v) });
                sentByName[kv.Key] = (d.Id, d.Type, v);
            }

            if (problems.Count > 0)
            {
                refusal = "custom attributes not stamped (nothing was sent): " + string.Join("; ", problems);
                return null;
            }
            refusal = null;
            return body;
        }

        // ── transport ──────────────────────────────────────────────────────────

        private sealed class Resp
        {
            public int Status;
            public string Body = string.Empty;
            /// <summary>Set when the exchange failed before a classifiable body existed:
            /// network error, or rate-limited on every attempt.</summary>
            public AccFetchStatus? Failure;
            public string Detail = string.Empty;
        }

        /// <summary>Send, retrying 429 up to <see cref="MaxAttempts"/> total. The request is
        /// rebuilt per attempt (HttpRequestMessage is single-use). Retry-After is honoured
        /// (delta-seconds or HTTP date), capped at <see cref="MaxRetryWait"/>; absent, the
        /// wait is 1, 2, 4 s.</summary>
        private static async Task<Resp> SendAsync(string token, HttpMethod method, string url, string jsonBody,
            AccCredentials creds = null, bool idempotent = true)
        {
            // AUT-4: with credentials, go through the shared ACC transport - a 401 refreshes the
            // token once and resends (an upload that outlived its token used to fail its stamp),
            // and 429/503 follow the same Retry-After rules as every other ACC call.
            if (creds != null)
            {
                var sent = await AccHttp.SendAsync(() =>
                {
                    var req = new HttpRequestMessage(method, url);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    return req;
                // H-8: keep the old path's rate-limit behaviour - cap a long Retry-After at
                // MaxRetryWait and retry - and one extra attempt so a 401 refresh does not use
                // up one of the 429 retries the token-only path had.
                }, creds, idempotent, timeout: TimeSpan.FromSeconds(60), maxAttempts: MaxAttempts + 1,
                   capLongWaits: true).ConfigureAwait(false);
                if (sent.Auth != null && !sent.Auth.Ok)
                    return new Resp { Status = 0, Failure = AccFetchStatus.AuthFailed, Detail = "not signed in to ACC: " + sent.Auth.Detail };
                if (sent.Status == 0)
                    return new Resp { Status = 0, Failure = AccFetchStatus.TransportFailed, Detail = "the request did not complete: " + sent.Error };
                if (sent.Status == 429)
                    return new Resp
                    {
                        Status = 429, Body = sent.Body ?? string.Empty, Failure = AccFetchStatus.TransportFailed,
                        Detail = $"Autodesk rate-limited all {sent.Attempts} attempts (HTTP 429) — nothing was changed; retry later",
                    };
                return new Resp { Status = sent.Status, Body = sent.Body ?? string.Empty };
            }
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(method, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                    int status = (int)resp.StatusCode;
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (status == 429)
                    {
                        if (attempt == MaxAttempts - 1)
                            return new Resp
                            {
                                Status = 429, Body = body, Failure = AccFetchStatus.TransportFailed,
                                Detail = $"Autodesk rate-limited all {MaxAttempts} attempts (HTTP 429) — nothing was changed; retry later",
                            };
                        await DelayHook(RetryWait(resp.Headers.RetryAfter, attempt)).ConfigureAwait(false);
                        continue;
                    }
                    return new Resp { Status = status, Body = body };
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                {
                    return new Resp
                    {
                        Status = 0, Failure = AccFetchStatus.TransportFailed,
                        Detail = "the request did not complete: " + ex.Message,
                    };
                }
            }
            // Unreachable: the last attempt returns above.
            return new Resp { Status = 0, Failure = AccFetchStatus.TransportFailed, Detail = "no attempt was made" };
        }

        internal static TimeSpan RetryWait(RetryConditionHeaderValue header, int attempt)
        {
            TimeSpan wait = TimeSpan.FromSeconds(1 << attempt);
            if (header?.Delta != null) wait = header.Delta.Value;
            else if (header?.Date != null) wait = header.Date.Value - DateTimeOffset.UtcNow;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            return wait > MaxRetryWait ? MaxRetryWait : wait;
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static string CheckInputs(string token, string projectId, string urn, string urnKind)
        {
            if (string.IsNullOrWhiteSpace(token)) return "no ACC access token was supplied";
            if (string.IsNullOrWhiteSpace(DocsProjectId(projectId))) return "no ACC project id was supplied";
            if (string.IsNullOrWhiteSpace(urn)) return $"no {urnKind} was supplied";
            if (!urn.Trim().StartsWith("urn:", StringComparison.Ordinal))
                return $"'{urn}' is not a {urnKind} (expected 'urn:adsk.wipprod:…')";
            return null;
        }

        private static AccAttributeDefinition ParseDefinition(JToken t)
        {
            if (!(t is JObject o)) return null;
            var idTok = o["id"];
            string name = (string)o["name"];
            string type = (string)o["type"];
            if (idTok == null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) return null;
            if (!long.TryParse(idTok.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) return null;
            var def = new AccAttributeDefinition { Id = id, Name = name, Type = type };
            if (o["arrayValues"] is JArray av)
                def.ArrayValues.AddRange(av.Select(v => v.ToString()));
            return def;
        }

        private static int? TotalResults(string body)
        {
            try
            {
                var tot = JToken.Parse(body)?["pagination"]?["totalResults"];
                if (tot != null && int.TryParse(tot.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
            }
            catch (JsonException) { }
            return null;
        }

        private static bool ValueMatches(string type, string sent, string got)
        {
            if (sent == null) return string.IsNullOrEmpty(got);
            if (got == null) return false;
            if (string.Equals(type, "date", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var a)
                && DateTime.TryParse(got, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var b))
                return Math.Abs((a - b).TotalSeconds) < 1;   // ACC discards milliseconds
            return string.Equals(sent, got, StringComparison.Ordinal);
        }

        private static string Shorten(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "…";
    }
}
