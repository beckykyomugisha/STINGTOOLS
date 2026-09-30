// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccModelProperties.cs — read STING tag parameters out of every model in an
// ACC federation WITHOUT opening any of them in Revit.
//
// API CHOSEN: the ACC Model Properties API (the "Index" service, construction/index/v2).
// Documented at developer.doc.autodesk.com/bPlouYTd/…/acc/v1/tutorials/model-properties-querying,
// …/overview/model-properties-field-guide and …/tutorials/model-properties-query-ref.
//
//   POST construction/index/v2/projects/{projectId}/indexes:batch-status  {versions:[{versionUrn}]}
//        lazy: starts the index job when absent, returns the same indexId for the same input
//   GET  …/indexes/{indexId}                     state PROCESSING | FINISHED | FAILED, retryAt
//   GET  …/indexes/{indexId}/fields              gzipped NDJSON {key, category, type, name, uom}
//   POST …/indexes/{indexId}/queries             {query, columns} → queryId
//   GET  …/indexes/{indexId}/queries/{queryId}   state, as above
//   GET  …/indexes/{indexId}/queries/{queryId}/properties   gzipped NDJSON result rows
//
// Scope data:read, three-legged (user context). The user needs at least View+Download on the
// Docs folder that holds each model. x-ads-region: US / EMEA (documented values).
//
// WHY NOT the alternatives:
//   * Model Derivative properties (what AccClashLocate uses for one dbId → externalId) is per
//     model, per view, paginated, and returns every property of every object - it is the
//     right tool for a handful of ids, not for scanning a federation.
//   * The AEC Data Model GraphQL API only covers models published from Revit 2024 or later,
//     must be activated by the Account Admin, and is hosted in AMER / EMEA / AUS. A KUT
//     consultant on Revit 2023 would be invisible to it. The Index API works on any version
//     ACC has translated.
//
// WHY PARAMETERS ARE MATCHED BY NAME. A field key is a hash of the property's category and
// name, so the same shared parameter can carry different keys in two models (grouped under
// "Identity Data" in one and "Other" in another). Every key whose NAME matches is collected
// and coalesced; a STING parameter absent from a model's fields is reported as ABSENT, which
// is a finding (that consultant has not loaded the STING parameters), not a read failure.
//
// FAILURE RULE (see CLAUDE.md "The failure mode this codebase produces"). A document whose
// index failed, timed out, or answered with a payload this client does not recognise is a
// FAILED read, carried with its reason. It never contributes "0 elements", and a federation
// with a failed document is never reported as compliant.
//
// Revit-free; logs through StingLog (the tests link a shim).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>How one document's property read ended.</summary>
    public enum AccDocumentReadStatus
    {
        /// <summary>Index and query finished; <see cref="AccDocumentRead.Elements"/> is the truth
        /// (possibly genuinely empty).</summary>
        Read = 0,
        /// <summary>The index finished but carries no Revit category field: not a Revit model
        /// (an IFC, NWC or DWG in the model set). Listed, not counted, not a failure.</summary>
        NotRevit = 1,
        /// <summary>The read did not complete. Never counted as zero elements.</summary>
        Failed = 2,
    }

    /// <summary>One element as the index reports it: identity plus the STING values it carries.</summary>
    public sealed class AccElementRecord
    {
        public string DocumentName { get; set; } = string.Empty;
        public string VersionUrn { get; set; } = string.Empty;
        /// <summary>The object's externalId - for a Revit model, the element's UniqueId.</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Parameter NAME → value as text. A parameter the model does not have is absent.</summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string parameterName)
            => parameterName != null && Values.TryGetValue(parameterName, out var v) ? (v ?? string.Empty) : string.Empty;
    }

    public sealed class AccDocumentRead
    {
        public string DocumentName { get; set; } = string.Empty;
        public string VersionUrn { get; set; } = string.Empty;
        public AccDocumentReadStatus Status { get; set; } = AccDocumentReadStatus.Failed;
        /// <summary>Why, for NotRevit and Failed. Empty on a clean read.</summary>
        public string Detail { get; set; } = string.Empty;
        public int HttpStatus { get; set; }
        public AccFetchStatus FetchStatus { get; set; } = AccFetchStatus.TransportFailed;
        public string IndexId { get; set; } = string.Empty;
        /// <summary>Objects in the whole index (stats.objects), when the service reported it.</summary>
        public long IndexObjects { get; set; }
        /// <summary>Requested parameters the model's fields do not contain at all.</summary>
        public List<string> AbsentParameters { get; } = new List<string>();
        public List<AccElementRecord> Elements { get; } = new List<AccElementRecord>();
    }

    /// <summary>What to read, and how long to wait for it.</summary>
    public sealed class AccModelPropertiesOptions
    {
        /// <summary>STING parameter names to read (the 8 tag tokens and ASS_TAG_1_TXT). The
        /// caller passes ParamRegistry's names; this file does not know them.</summary>
        public IList<string> ParameterNames { get; set; } = new List<string>();

        /// <summary>Revit category names (as the index's _RC field reports them, e.g. "Walls")
        /// whose elements are in scope. An element outside them is still read when it carries
        /// a value in <see cref="AlwaysIncludeWhenPresent"/> (a tagged element is in scope
        /// wherever it is). Empty = every viewable element with a Revit category.</summary>
        public IList<string> CategoryNames { get; set; } = new List<string>();

        /// <summary>Parameter whose presence alone brings an element into scope (ASS_TAG_1_TXT).</summary>
        public string AlwaysIncludeWhenPresent { get; set; } = string.Empty;

        /// <summary>Longest total wait for one document (index, then query).</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(15);
        public TimeSpan MinPoll { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan MaxPoll { get; set; } = TimeSpan.FromSeconds(30);
    }

    public static class AccModelProperties
    {
        private static string Base => AccIssueSync.Host + "/construction/index/v2/projects";

        /// <summary>The Revit category field in an index (documented in the query reference:
        /// {"category":"__category__","name":"_RC"}).</summary>
        internal const string RevitCategoryFieldName = "_RC";
        internal const string CategoryFieldCategory = "__category__";
        internal const string NameFieldCategory = "__name__";

        // ── Federation ─────────────────────────────────────────────────────────

        /// <summary>
        /// Read every document in <paramref name="documents"/>. Documents repeat in a model set
        /// version (one row per viewable), so they are de-duplicated by version URN. Each
        /// document gets its own index, so every row is attributable to exactly one model.
        /// </summary>
        public static async Task<List<AccDocumentRead>> ReadFederationAsync(
            AccCredentials creds, IEnumerable<AccModelSetDocument> documents, AccModelPropertiesOptions options)
        {
            var reads = new List<AccDocumentRead>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in documents ?? Enumerable.Empty<AccModelSetDocument>())
            {
                if (d == null || string.IsNullOrEmpty(d.VersionUrn) || !seen.Add(d.VersionUrn)) continue;
                AccDocumentRead r;
                try { r = await ReadDocumentAsync(creds, d.VersionUrn, d.DisplayName, options).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    StingLog.Warn($"AccModelProperties {d.DisplayName}: {ex.Message}");
                    r = Failed(d.VersionUrn, d.DisplayName, AccFetchStatus.TransportFailed, 0, "the read threw: " + ex.Message);
                }
                reads.Add(r);
            }
            return reads;
        }

        // ── One document ───────────────────────────────────────────────────────

        public static async Task<AccDocumentRead> ReadDocumentAsync(
            AccCredentials creds, string versionUrn, string displayName, AccModelPropertiesOptions options)
        {
            options ??= new AccModelPropertiesOptions();
            string name = string.IsNullOrEmpty(displayName) ? versionUrn : displayName;
            if (string.IsNullOrEmpty(creds?.ProjectId))
                return Failed(versionUrn, name, AccFetchStatus.NotFound, 0, "no ACC project id");
            if (string.IsNullOrEmpty(versionUrn))
                return Failed(versionUrn, name, AccFetchStatus.NotFound, 0, "no document version URN");

            string project = Uri.EscapeDataString(AccIds.ForAcc(creds.ProjectId));
            var budget = new PollBudget(options);

            // 1. Index (lazy create + poll).
            var startBody = new JObject { ["versions"] = new JArray(new JObject { ["versionUrn"] = versionUrn }) };
            var start = await PostJsonAsync(creds, $"{Base}/{project}/indexes:batch-status", startBody).ConfigureAwait(false);
            if (!start.Succeeded) return Failed(versionUrn, name, start.Status, start.HttpStatus, "index request: " + start.Detail);

            var jobs = AccFetchOutcome.FindArray(start.Value, new[] { "indexes" });
            if (jobs == null || jobs.Count == 0)
                return Failed(versionUrn, name, AccFetchStatus.TransportFailed, start.HttpStatus,
                    "the index request answered without an 'indexes' array - the construction/index/v2 payload has changed");
            var job = jobs.OfType<JObject>().FirstOrDefault(j => (j["versionUrns"] as JArray)?.Any(u => (string)u == versionUrn) == true)
                      ?? jobs.OfType<JObject>().FirstOrDefault();
            string indexId = (string)job?["indexId"];
            if (string.IsNullOrEmpty(indexId))
                return Failed(versionUrn, name, AccFetchStatus.TransportFailed, start.HttpStatus, "the index job carried no indexId");

            var indexDone = await PollAsync(creds, $"{Base}/{project}/indexes/{Uri.EscapeDataString(indexId)}", job, budget, "index")
                .ConfigureAwait(false);
            if (!indexDone.Succeeded)
                return Tag(Failed(versionUrn, name, indexDone.Status, indexDone.HttpStatus, indexDone.Detail), indexId);

            var read = new AccDocumentRead
            {
                DocumentName = name, VersionUrn = versionUrn, IndexId = indexId,
                IndexObjects = ReadLong(indexDone.Value?["stats"]?["objects"]),
            };

            // 2. Fields → which keys hold which STING parameter.
            var fieldsResp = await GetBytesAsync(creds, $"{Base}/{project}/indexes/{Uri.EscapeDataString(indexId)}/fields").ConfigureAwait(false);
            if (!fieldsResp.IsSuccess)
                return Tag(Failed(versionUrn, name, fieldsResp.Classify(), fieldsResp.Status, "index fields: " + fieldsResp.Describe()), indexId);
            List<AccIndexField> fields;
            try { fields = ParseFields(Decode(fieldsResp.Bytes)); }
            catch (Exception ex)
            {
                return Tag(Failed(versionUrn, name, AccFetchStatus.TransportFailed, fieldsResp.Status, "index fields could not be parsed: " + ex.Message), indexId);
            }
            var map = AccPropertyFieldMap.Build(fields, options.ParameterNames);
            if (map.CategoryKeys.Count == 0)
            {
                read.Status = AccDocumentReadStatus.NotRevit;
                read.FetchStatus = AccFetchStatus.EmptyOk;
                read.HttpStatus = fieldsResp.Status;
                read.Detail = fields.Count == 0
                    ? "the index has no fields at all"
                    : "the index has no Revit category field (_RC) - not a Revit model";
                return read;
            }
            read.AbsentParameters.AddRange(map.AbsentParameters);

            // 3. Query: elements in scope, only the columns we need.
            var queryBody = BuildQuery(map, options.CategoryNames, options.AlwaysIncludeWhenPresent);
            var q = await PostJsonAsync(creds, $"{Base}/{project}/indexes/{Uri.EscapeDataString(indexId)}/queries", queryBody).ConfigureAwait(false);
            if (!q.Succeeded) return Tag(Failed(versionUrn, name, q.Status, q.HttpStatus, "index query: " + q.Detail), indexId);
            var qJob = q.Value as JObject ?? AccFetchOutcome.FindArray(q.Value, new[] { "queries" })?.OfType<JObject>().FirstOrDefault();
            string queryId = (string)qJob?["queryId"];
            if (string.IsNullOrEmpty(queryId))
                return Tag(Failed(versionUrn, name, AccFetchStatus.TransportFailed, q.HttpStatus, "the query answer carried no queryId"), indexId);

            string queryUrl = $"{Base}/{project}/indexes/{Uri.EscapeDataString(indexId)}/queries/{Uri.EscapeDataString(queryId)}";
            var queryDone = await PollAsync(creds, queryUrl, qJob, budget, "query").ConfigureAwait(false);
            if (!queryDone.Succeeded)
                return Tag(Failed(versionUrn, name, queryDone.Status, queryDone.HttpStatus, queryDone.Detail), indexId);

            // 4. Results (gzipped NDJSON).
            var rows = await GetBytesAsync(creds, queryUrl + "/properties").ConfigureAwait(false);
            if (!rows.IsSuccess)
                return Tag(Failed(versionUrn, name, rows.Classify(), rows.Status, "query results: " + rows.Describe()), indexId);
            try { ParseRows(Decode(rows.Bytes), map, read); }
            catch (Exception ex)
            {
                read.Elements.Clear();
                return Tag(Failed(versionUrn, name, AccFetchStatus.TransportFailed, rows.Status, "query results could not be parsed: " + ex.Message), indexId);
            }
            read.Status = AccDocumentReadStatus.Read;
            read.FetchStatus = read.Elements.Count == 0 ? AccFetchStatus.EmptyOk : AccFetchStatus.Ok;
            read.HttpStatus = rows.Status;
            return read;
        }

        // ── Query ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The query body. Rows: viewable (count(views) &gt; 0, so types and non-graphical rows
        /// drop out) AND (Revit category in scope OR the always-include parameter present).
        /// Columns are aliased so a row carries only identity + the STING values.
        /// </summary>
        internal static JObject BuildQuery(AccPropertyFieldMap map, IEnumerable<string> categoryNames, string alwaysInclude)
        {
            var scope = new JArray();
            var cats = (categoryNames ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (cats.Count > 0)
            {
                foreach (var key in map.CategoryKeys)
                {
                    var inArr = new JArray(Prop(key));
                    foreach (var c in cats) inArr.Add(Quote(c));
                    scope.Add(new JObject { ["$in"] = inArr });
                }
                if (!string.IsNullOrEmpty(alwaysInclude) && map.KeysByParameter.TryGetValue(alwaysInclude, out var tagKeys))
                    foreach (var k in tagKeys) scope.Add(new JObject { ["$notnull"] = Prop(k) });
            }
            else
            {
                foreach (var key in map.CategoryKeys) scope.Add(new JObject { ["$notnull"] = Prop(key) });
            }

            var and = new JArray(new JObject { ["$gt"] = new JArray(new JObject { ["$count"] = "s.views" }, 0) });
            and.Add(scope.Count == 1 ? scope[0] : new JObject { ["$or"] = scope });

            var columns = new JObject
            {
                ["uid"] = "s.externalId",
                ["cat"] = Coalesce(map.CategoryKeys),
            };
            if (map.NameKeys.Count > 0) columns["nm"] = Coalesce(map.NameKeys);
            foreach (var kv in map.ColumnByParameter)
                columns[kv.Value] = Coalesce(map.KeysByParameter[kv.Key]);

            return new JObject { ["query"] = new JObject { ["$and"] = and }, ["columns"] = columns };
        }

        private static string Prop(string key) => "s.props." + key;

        /// <summary>SQL string constant: single-quoted, embedded quotes doubled.</summary>
        internal static string Quote(string s) => "'" + (s ?? string.Empty).Replace("'", "''") + "'";

        private static JToken Coalesce(IList<string> keys)
        {
            if (keys.Count == 1) return Prop(keys[0]);
            return new JObject { ["$coalesce"] = new JArray(keys.Select(Prop)) };
        }

        // ── Parsing ────────────────────────────────────────────────────────────

        /// <summary>NDJSON (one field per line), or a JSON array of the same objects.</summary>
        internal static List<AccIndexField> ParseFields(string text)
        {
            var list = new List<AccIndexField>();
            foreach (var o in EnumerateJsonObjects(text))
            {
                string key = (string)o["key"];
                if (string.IsNullOrEmpty(key)) continue;
                list.Add(new AccIndexField
                {
                    Key = key,
                    Category = (string)o["category"] ?? string.Empty,
                    Name = (string)o["name"] ?? string.Empty,
                    Type = (string)o["type"] ?? string.Empty,
                });
            }
            return list;
        }

        /// <summary>Result rows into <paramref name="read"/>. Accepts the aliased shape this
        /// client asks for, and the raw index-row shape ({externalId, props:{key:value}}) in
        /// case the service ignores the column projection.</summary>
        internal static void ParseRows(string text, AccPropertyFieldMap map, AccDocumentRead read)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in EnumerateJsonObjects(text))
            {
                var el = new AccElementRecord { DocumentName = read.DocumentName, VersionUrn = read.VersionUrn };
                if (o["props"] is JObject props)
                {
                    el.ExternalId = (string)o["externalId"] ?? string.Empty;
                    el.Category = FirstValue(props, map.CategoryKeys);
                    el.Name = FirstValue(props, map.NameKeys);
                    foreach (var kv in map.KeysByParameter)
                    {
                        string v = FirstValue(props, kv.Value, out bool present);
                        if (present) el.Values[kv.Key] = v;
                    }
                }
                else
                {
                    el.ExternalId = AsText(o["uid"] ?? o["externalId"]);
                    el.Category = AsText(o["cat"]);
                    el.Name = AsText(o["nm"]);
                    foreach (var kv in map.ColumnByParameter)
                    {
                        var t = o[kv.Value];
                        if (t != null && t.Type != JTokenType.Null) el.Values[kv.Key] = AsText(t);
                    }
                }
                // A row the service returned twice is one element, not two - otherwise it would
                // show up as its own duplicate tag.
                string identity = string.IsNullOrEmpty(el.ExternalId) ? null : el.ExternalId;
                if (identity != null && !seen.Add(identity)) continue;
                read.Elements.Add(el);
            }
        }

        private static string FirstValue(JObject props, IList<string> keys) => FirstValue(props, keys, out _);

        private static string FirstValue(JObject props, IList<string> keys, out bool present)
        {
            present = false;
            foreach (var k in keys)
            {
                var t = props[k];
                if (t == null || t.Type == JTokenType.Null) continue;
                present = true;
                return AsText(t);
            }
            return string.Empty;
        }

        internal static string AsText(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return string.Empty;
            if (t is JValue v)
            {
                if (v.Value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
                return v.Value?.ToString() ?? string.Empty;
            }
            return t.ToString(Formatting.None);
        }

        private static IEnumerable<JObject> EnumerateJsonObjects(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                foreach (var t in JArray.Parse(trimmed)) if (t is JObject o) yield return o;
                yield break;
            }
            using var sr = new StringReader(text);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                // A malformed line throws: a half-parsed result must not read as a smaller model.
                yield return JObject.Parse(line);
            }
        }

        /// <summary>gzip → UTF-8 text; plain bytes pass through (a redirect target or proxy
        /// may already have decompressed).</summary>
        internal static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var ms = new MemoryStream(bytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var sr = new StreamReader(gz, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static long ReadLong(JToken t)
        {
            if (t == null) return 0;
            if (t.Type == JTokenType.Integer) return (long)t;
            return long.TryParse((string)t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        // ── Polling ────────────────────────────────────────────────────────────

        /// <summary>Waited time is ACCUMULATED from the intervals asked for, not read off a
        /// clock, so the bound holds under the test delay hook as it does in production.</summary>
        private sealed class PollBudget
        {
            private readonly AccModelPropertiesOptions _o;
            public TimeSpan Waited;
            public PollBudget(AccModelPropertiesOptions o) { _o = o; }
            public bool Exhausted => Waited >= _o.Timeout;
            public TimeSpan Next(JToken job, int attempt)
            {
                TimeSpan wait = TimeSpan.FromSeconds(Math.Min(_o.MaxPoll.TotalSeconds, _o.MinPoll.TotalSeconds * Math.Pow(1.5, attempt)));
                var retryAt = job?["retryAt"];
                if (retryAt != null && retryAt.Type != JTokenType.Null &&
                    DateTimeOffset.TryParse(AsText(retryAt), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                {
                    var hint = at - DateTimeOffset.UtcNow;
                    if (hint > TimeSpan.Zero) wait = hint;
                }
                if (wait < _o.MinPoll) wait = _o.MinPoll;
                if (wait > _o.MaxPoll) wait = _o.MaxPoll;
                return wait;
            }
        }

        private static async Task<AccFetchResult<JObject>> PollAsync(
            AccCredentials creds, string statusUrl, JObject first, PollBudget budget, string what)
        {
            JObject job = first;
            for (int attempt = 0; ; attempt++)
            {
                string state = ((string)job?["state"] ?? string.Empty).Trim().ToUpperInvariant();
                if (state == "FINISHED") return AccFetchResult<JObject>.Success(job, empty: false);
                if (state == "FAILED")
                    return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, job, 200,
                        $"the {what} job FAILED in ACC: {DescribeErrors(job)}");
                if (state != "PROCESSING" && state != "RUNNING" && state != "QUEUED" && state != "PENDING")
                    return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, job, 200,
                        $"the {what} job reported an unrecognised state '{state}'");
                if (budget.Exhausted)
                    return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, job, 200,
                        $"the {what} job was still {state} after {budget.Waited.TotalMinutes:F1} min - gave up (not a result)");

                var wait = budget.Next(job, attempt);
                budget.Waited += wait;
                await AccHttp.DelayHook(wait).ConfigureAwait(false);

                var resp = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Get, statusUrl), creds),
                    creds, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                    return AccFetchResult<JObject>.Failure(resp.Classify(), null, resp.Status, $"{what} status: " + resp.Describe());
                try { job = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<JObject>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                        $"{what} status was not JSON: " + ex.Message);
                }
            }
        }

        private static string DescribeErrors(JObject job)
        {
            var errs = job?["errors"] as JArray;
            if (errs == null || errs.Count == 0) return "no reason given";
            return string.Join("; ", errs.OfType<JObject>().Select(e =>
                string.Join(" - ", new[] { (string)e["type"], (string)e["title"], (string)e["detail"] }.Where(x => !string.IsNullOrEmpty(x)))).Take(3));
        }

        // ── HTTP ───────────────────────────────────────────────────────────────

        /// <summary>A POST to the index service is a read (batch-status is lazy and returns the
        /// same indexId for the same input; a query is keyed the same way), so it may retry.</summary>
        private static async Task<AccFetchResult<JToken>> PostJsonAsync(AccCredentials creds, string url, JObject body)
        {
            string payload = body.ToString(Formatting.None);
            var resp = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Post, url)
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, creds),
                creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                StingLog.Warn($"AccModelProperties POST {resp.Status}: {url} - {resp.Describe()}");
                return AccFetchResult<JToken>.Failure(resp.Classify(), null, resp.Status, resp.Describe());
            }
            try { return AccFetchResult<JToken>.Success(JToken.Parse(resp.Body), empty: false); }
            catch (Exception ex)
            {
                return AccFetchResult<JToken>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    "the response was not JSON: " + ex.Message);
            }
        }

        /// <summary>Resource downloads (fields, results). The documented 303 to a signed
        /// location is followed by HttpClient, which drops the Authorization header on the way.</summary>
        private static Task<AccHttpResponse> GetBytesAsync(AccCredentials creds, string url)
            => AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Get, url), creds),
                creds, idempotent: true, timeout: TimeSpan.FromMinutes(10), readBytes: true);

        private static AccDocumentRead Failed(string urn, string name, AccFetchStatus status, int http, string detail)
            => new AccDocumentRead
            {
                DocumentName = name ?? string.Empty, VersionUrn = urn ?? string.Empty,
                Status = AccDocumentReadStatus.Failed, FetchStatus = status, HttpStatus = http,
                Detail = detail ?? string.Empty,
            };

        private static AccDocumentRead Tag(AccDocumentRead r, string indexId) { r.IndexId = indexId ?? string.Empty; return r; }
    }

    /// <summary>One line of an index's fields resource.</summary>
    public sealed class AccIndexField
    {
        public string Key { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>Which index keys carry which STING parameter, the Revit category and the name.</summary>
    public sealed class AccPropertyFieldMap
    {
        public List<string> CategoryKeys { get; } = new List<string>();
        public List<string> NameKeys { get; } = new List<string>();
        /// <summary>Parameter name → every key whose field NAME equals it (see file header).</summary>
        public Dictionary<string, List<string>> KeysByParameter { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        /// <summary>Parameter name → the column alias used in the query (v0, v1 …).</summary>
        public Dictionary<string, string> ColumnByParameter { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> AbsentParameters { get; } = new List<string>();

        public static AccPropertyFieldMap Build(IEnumerable<AccIndexField> fields, IEnumerable<string> parameterNames)
        {
            var map = new AccPropertyFieldMap();
            var list = (fields ?? Enumerable.Empty<AccIndexField>()).ToList();
            foreach (var f in list)
            {
                if (string.Equals(f.Category, AccModelProperties.CategoryFieldCategory, StringComparison.Ordinal) &&
                    string.Equals(f.Name, AccModelProperties.RevitCategoryFieldName, StringComparison.Ordinal))
                    map.CategoryKeys.Add(f.Key);
                else if (string.Equals(f.Category, AccModelProperties.NameFieldCategory, StringComparison.Ordinal))
                    map.NameKeys.Add(f.Key);
            }
            int col = 0;
            foreach (var p in (parameterNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal))
            {
                // Internal categories (__name__, __category__ …) are Viewer bookkeeping, never
                // a user parameter, even if one happened to share a name.
                var keys = list.Where(f => string.Equals((f.Name ?? string.Empty).Trim(), p, StringComparison.Ordinal) &&
                                           !(f.Category ?? string.Empty).StartsWith("__", StringComparison.Ordinal))
                               .Select(f => f.Key).Distinct(StringComparer.Ordinal).ToList();
                if (keys.Count == 0) { map.AbsentParameters.Add(p); continue; }
                map.KeysByParameter[p] = keys;
                map.ColumnByParameter[p] = "v" + (col++).ToString(CultureInfo.InvariantCulture);
            }
            return map;
        }
    }
}
