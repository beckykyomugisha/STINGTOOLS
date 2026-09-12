// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccProjectDiscovery.cs
//
// Why this file exists: nothing in the plugin could ever ANSWER "which ACC project am
// I connected to?" — every client took a container id it was handed and used it. So
// configuring ACC began by asking an ACC project administrator to read a GUID out of a
// URL, which made a two-minute setup step depend on another person's calendar. On KUT
// that dependency sat on the critical path of a live verification already overdue.
//
// This lists the hubs and projects the SIGNED-IN USER can already see, using the
// account:read scope the sign-in has always requested. It grants no access anybody did
// not already have — a 3-legged token acts as its user — it only lets that user read
// back what they can reach instead of transcribing it.
//
// Revit-free and log-free, so StingTools.Acc.Tests links it directly and the failure
// classification is provable without a Revit session. Logging stays in the callers.
//
// ONE THING IT DELIBERATELY DOES NOT DO: transform the id. APS returns project ids in
// the form the Data Management API uses, and whether the Issues container wants exactly
// that form is not something this file is entitled to assume. It reports the id
// verbatim. If a container id turns out to be wrong, AccPullClashesCommand already names
// it — a silently "corrected" id would be the wrong-federation failure that looks like a
// clean one.
//
// NETWORK CODE — the endpoint shapes are the documented APS Data Management ones
// (`GET /project/v1/hubs`, `GET /project/v1/hubs/{hubId}/projects`, JSON:API `data[]`
// with `id` + `attributes.name`), exercised here only against a loopback listener.
// Confirm against a live tenant before the engagement leans on it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>An ACC/BIM 360 hub the signed-in user can see.</summary>
    public sealed class AccHub
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public override string ToString() => string.IsNullOrEmpty(Name) ? Id : $"{Name}  [{Id}]";
    }

    /// <summary>A project inside a hub, carrying the hub it came from so a flat list stays unambiguous.</summary>
    public sealed class AccProject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HubId { get; set; } = string.Empty;
        public string HubName { get; set; } = string.Empty;
        public override string ToString() =>
            string.IsNullOrEmpty(HubName) ? $"{Name}  [{Id}]" : $"{HubName} / {Name}  [{Id}]";
    }

    public static class AccProjectDiscovery
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point at a loopback listener. Production never calls this.</summary>
        internal static void OverrideHostForTests(string host)
            => _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');

        private static string ProjectBase => _host + "/project/v1";

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>Hubs the signed-in user can see. `GET /project/v1/hubs`.</summary>
        public static async Task<AccFetchResult<List<AccHub>>> ListHubsAsync(AccCredentials creds)
        {
            var got = await GetArrayAsync(creds, $"{ProjectBase}/hubs").ConfigureAwait(false);
            if (!got.Succeeded)
                return AccFetchResult<List<AccHub>>.Failure(got.Status, new List<AccHub>(), got.HttpStatus, got.Detail);

            var hubs = new List<AccHub>();
            foreach (var t in got.Value)
            {
                string id = (string)t["id"] ?? string.Empty;
                if (id.Length == 0) continue;           // unidentifiable: skipped, never invented
                var attrs = t["attributes"] as JObject;
                hubs.Add(new AccHub
                {
                    Id = id,
                    Name = (string)(attrs?["name"]) ?? string.Empty,
                    Region = (string)(attrs?["region"]) ?? string.Empty,
                });
            }
            return AccFetchResult<List<AccHub>>.Success(hubs, hubs.Count == 0);
        }

        /// <summary>Projects in one hub. `GET /project/v1/hubs/{hubId}/projects`.</summary>
        public static async Task<AccFetchResult<List<AccProject>>> ListProjectsAsync(
            AccCredentials creds, string hubId, string hubName = "")
        {
            if (string.IsNullOrWhiteSpace(hubId))
                return AccFetchResult<List<AccProject>>.Failure(AccFetchStatus.NotFound,
                    new List<AccProject>(), 0, "no hub id was supplied");

            string url = $"{ProjectBase}/hubs/{Uri.EscapeDataString(hubId)}/projects";
            var got = await GetArrayAsync(creds, url).ConfigureAwait(false);
            if (!got.Succeeded)
                return AccFetchResult<List<AccProject>>.Failure(got.Status, new List<AccProject>(),
                    got.HttpStatus, got.Detail);

            var projects = new List<AccProject>();
            foreach (var t in got.Value)
            {
                string id = (string)t["id"] ?? string.Empty;
                if (id.Length == 0) continue;           // unidentifiable: skipped, never invented
                var attrs = t["attributes"] as JObject;
                projects.Add(new AccProject
                {
                    Id = id,                            // verbatim — see the header
                    Name = (string)(attrs?["name"]) ?? string.Empty,
                    HubId = hubId,
                    HubName = hubName ?? string.Empty,
                });
            }
            return AccFetchResult<List<AccProject>>.Success(projects, projects.Count == 0);
        }

        /// <summary>
        /// Every project across every visible hub, flattened.
        ///
        /// A hub that fails to list fails the WHOLE call. The caller is choosing a project
        /// from this list, so a silently short one says "your project is not in ACC" — the
        /// partial-answer-presented-as-complete shape, which is worth refusing rather than
        /// smoothing over.
        /// </summary>
        public static async Task<AccFetchResult<List<AccProject>>> ListAllProjectsAsync(AccCredentials creds)
        {
            var hubs = await ListHubsAsync(creds).ConfigureAwait(false);
            if (!hubs.Succeeded)
                return AccFetchResult<List<AccProject>>.Failure(hubs.Status, new List<AccProject>(),
                    hubs.HttpStatus, hubs.Detail);

            var all = new List<AccProject>();
            foreach (var hub in hubs.Value)
            {
                var got = await ListProjectsAsync(creds, hub.Id, hub.Name).ConfigureAwait(false);
                if (!got.Succeeded)
                    return AccFetchResult<List<AccProject>>.Failure(got.Status, new List<AccProject>(),
                        got.HttpStatus,
                        $"hub '{(string.IsNullOrEmpty(hub.Name) ? hub.Id : hub.Name)}' [{hub.Id}] could not be " +
                        $"listed: {got.Detail}. No partial list is returned — a short list would read as " +
                        $"'your project is not in ACC'.");
                all.AddRange(got.Value);
            }
            return AccFetchResult<List<AccProject>>.Success(all, all.Count == 0);
        }

        // ── transport ───────────────────────────────────────────────────────────

        /// <summary>GET a JSON:API document and hand back its `data` array, classified.
        /// A 200 whose body carries no `data` array is TransportFailed, not an empty
        /// account — that is exactly the shape a changed path or a proxy login page
        /// returns.</summary>
        private static async Task<AccFetchResult<JArray>> GetArrayAsync(AccCredentials creds, string url)
        {
            if (!await AccIssueSync.EnsureAuthAsync(creds).ConfigureAwait(false))
                return AccFetchResult<JArray>.Failure(AccFetchStatus.AuthFailed, new JArray(), 0,
                    "no ACC access token could be obtained — sign in again on the ACC card");

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                int status = (int)resp.StatusCode;
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                var kind = AccFetchOutcome.ClassifyArrayBody(status, body, "data");
                if (kind != AccFetchStatus.Ok && kind != AccFetchStatus.EmptyOk)
                    return AccFetchResult<JArray>.Failure(kind, new JArray(), status,
                        AccFetchOutcome.Describe(kind, status));

                var arr = AccFetchOutcome.FindArray(body, "data") ?? new JArray();
                return AccFetchResult<JArray>.Success(arr, arr.Count == 0);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
            {
                // HttpStatus 0 says the request never completed, so a transport failure is
                // attributable rather than inferred from a payload check downstream.
                return AccFetchResult<JArray>.Failure(AccFetchStatus.TransportFailed, new JArray(), 0,
                    "the request did not complete: " + ex.Message);
            }
        }
    }
}
