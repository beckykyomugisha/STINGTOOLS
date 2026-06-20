// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueSync.cs — S6.4 (N-G8).
//
// Autodesk Construction Cloud (ACC) Issues round-trip.
//
// Push BCF issues from STING to ACC and pull ACC Issues back.
// Requires OAuth 2.0 three-legged flow; access token refresh is
// handled automatically. Rate-limit 429 responses are retried with
// exponential back-off.
//
// MVP scope: raw HttpClient + Newtonsoft.Json. A future phase may
// swap in the Autodesk Platform Services (APS) SDK once it ships a
// stable .NET 8 package.
//
// Credentials live in %APPDATA%\Planscape\acc_credentials.json so
// they never touch project files or source control.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class AccCredentials
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiry { get; set; }
        public string HubId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;

        // N-G2: ACC rejects an issue create with an empty issue_type_id. When
        // unset, AccIssueSync.EnsureIssueTypeAsync fetches the project's issue
        // types once and caches a Clash/default type (+ first subtype) here.
        public string IssueTypeId { get; set; } = string.Empty;
        public string IssueSubtypeId { get; set; } = string.Empty;

        public bool IsStale => string.IsNullOrEmpty(AccessToken) || DateTime.UtcNow >= AccessTokenExpiry.AddMinutes(-5);
    }

    public sealed class AccIssue
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "open";
        public string IssueType { get; set; } = string.Empty;
        public string AssignedToUserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string LocationDescription { get; set; } = string.Empty;
    }

    public static class AccIssueSync
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string AuthUrl   = "https://developer.api.autodesk.com/authentication/v2/token";
        private const string IssuesUrl = "https://developer.api.autodesk.com/construction/issues/v1";

        /// <summary>
        /// Ensure the access token is fresh; refresh via
        /// refresh_token grant if expired. Thread-safe via semaphore.
        /// </summary>
        private static readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        public static async Task<bool> EnsureAuthAsync(AccCredentials creds)
        {
            if (!creds.IsStale) return true;
            await _tokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!creds.IsStale) return true;
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", creds.RefreshToken ?? string.Empty),
                });
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{creds.ClientId}:{creds.ClientSecret}"));
                var req = new HttpRequestMessage(HttpMethod.Post, AuthUrl) { Content = form };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"AccIssueSync: token refresh returned {(int)resp.StatusCode}");
                    return false;
                }
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                creds.AccessToken        = (string)json["access_token"] ?? creds.AccessToken;
                creds.RefreshToken       = (string)json["refresh_token"] ?? creds.RefreshToken;
                int expiresIn            = (int?)json["expires_in"] ?? 3600;
                creds.AccessTokenExpiry  = DateTime.UtcNow.AddSeconds(expiresIn);
                SaveCredentials(creds);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("AccIssueSync.EnsureAuth failed", ex);
                return false;
            }
            finally { _tokenLock.Release(); }
        }

        /// <summary>
        /// N-G2: Resolve a usable issue_type_id for the container. Order:
        /// already-cached <see cref="AccCredentials.IssueTypeId"/> → fetch the
        /// project's issue types once (GET issue-types) and pick a
        /// Clash/Coordination type (else the first), caching the id (+ first
        /// subtype). Idempotent; cached on the credentials + persisted.
        /// </summary>
        public static async Task<bool> EnsureIssueTypeAsync(AccCredentials creds)
        {
            if (!string.IsNullOrEmpty(creds.IssueTypeId)) return true;
            if (!await EnsureAuthAsync(creds).ConfigureAwait(false)) return false;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{IssuesUrl}/containers/{creds.ProjectId}/issue-types?include=subtypes&limit=200");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"AccIssueSync.EnsureIssueType returned {(int)resp.StatusCode}");
                    return false;
                }
                var j = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                var results = j["results"] as JArray ?? new JArray();
                if (results.Count == 0) { StingLog.Warn("AccIssueSync.EnsureIssueType: container returned no issue types."); return false; }

                JToken chosen = null;
                foreach (var t in results)
                {
                    string title = ((string)t["title"] ?? string.Empty).ToLowerInvariant();
                    if (title.Contains("clash") || title.Contains("coordination")) { chosen = t; break; }
                }
                chosen ??= results[0];
                creds.IssueTypeId = (string)chosen["id"] ?? string.Empty;
                var subs = chosen["subtypes"] as JArray;
                if (subs != null && subs.Count > 0)
                    creds.IssueSubtypeId = (string)subs[0]["id"] ?? string.Empty;
                SaveCredentials(creds);
                StingLog.Info($"AccIssueSync: resolved issue type '{(string)chosen["title"]}' ({creds.IssueTypeId}).");
                return !string.IsNullOrEmpty(creds.IssueTypeId);
            }
            catch (Exception ex) { StingLog.Error("AccIssueSync.EnsureIssueType failed", ex); return false; }
        }

        /// <summary>Push a STING-originated issue to ACC.</summary>
        public static async Task<string> PushIssueAsync(AccCredentials creds, AccIssue issue)
        {
            if (!await EnsureAuthAsync(creds).ConfigureAwait(false)) return null;

            // N-G2: an empty issue_type_id is rejected by ACC. Prefer an explicit
            // type on the issue, else the credentials' configured/cached type,
            // else fetch + cache one from the project.
            string typeId = !string.IsNullOrEmpty(issue.IssueType) ? issue.IssueType : creds.IssueTypeId;
            if (string.IsNullOrEmpty(typeId))
            {
                await EnsureIssueTypeAsync(creds).ConfigureAwait(false);
                typeId = creds.IssueTypeId;
            }
            if (string.IsNullOrEmpty(typeId))
                StingLog.Warn("AccIssueSync.PushIssue: no issue_type_id resolved — ACC will reject. " +
                              "Set IssueTypeId in acc_credentials.json or ensure the container exposes issue types.");

            var body = new JObject
            {
                ["title"]               = issue.Title,
                ["description"]         = issue.Description,
                ["status"]              = issue.Status,
                ["issue_type_id"]       = typeId,
                ["location_description"]= issue.LocationDescription,
            };
            if (!string.IsNullOrEmpty(creds.IssueSubtypeId))
                body["issue_subtype_id"] = creds.IssueSubtypeId;
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{IssuesUrl}/containers/{creds.ProjectId}/issues")
            { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if ((int)resp.StatusCode == 429)
                {
                    int wait = 1 << attempt;
                    StingLog.Warn($"ACC 429 — retrying in {wait}s");
                    await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"AccIssueSync.PushIssue returned {(int)resp.StatusCode}");
                    return null;
                }
                var j = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                return (string)j["id"];
            }
            return null;
        }

        /// <summary>Pull the latest issue set from ACC.</summary>
        public static async Task<List<AccIssue>> PullIssuesAsync(AccCredentials creds, int pageSize = 100)
        {
            var list = new List<AccIssue>();
            if (!await EnsureAuthAsync(creds).ConfigureAwait(false)) return list;
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{IssuesUrl}/containers/{creds.ProjectId}/issues?limit={pageSize}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return list;
            var j = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            foreach (var t in j["results"] ?? new JArray())
            {
                list.Add(new AccIssue
                {
                    Id                  = (string)t["id"] ?? string.Empty,
                    Title               = (string)t["title"] ?? string.Empty,
                    Description         = (string)t["description"] ?? string.Empty,
                    Status              = (string)t["status"] ?? "open",
                    IssueType           = (string)t["issue_type_id"] ?? string.Empty,
                    AssignedToUserId    = (string)t["assigned_to"] ?? string.Empty,
                    LocationDescription = (string)t["location_description"] ?? string.Empty,
                });
            }
            return list;
        }

        public static string CredentialsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Planscape", "acc_credentials.json");

        public static AccCredentials LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredentialsPath)) return new AccCredentials();
                var j = JObject.Parse(File.ReadAllText(CredentialsPath));
                return j.ToObject<AccCredentials>() ?? new AccCredentials();
            }
            catch (Exception ex)
            {
                StingLog.Warn("AccIssueSync.LoadCredentials: " + ex.Message);
                return new AccCredentials();
            }
        }

        public static void SaveCredentials(AccCredentials c)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CredentialsPath)!);
                File.WriteAllText(CredentialsPath, JObject.FromObject(c).ToString());
            }
            catch (Exception ex) { StingLog.Warn("AccIssueSync.SaveCredentials: " + ex.Message); }
        }
    }
}
