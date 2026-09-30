// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueSync.cs — S6.4 (N-G8).
//
// Autodesk Construction Cloud (ACC) Issues round-trip, and the token handling every
// other ACC client reuses.
//
// Transport: AccHttp (one set of retry rules for every ACC call). Credentials:
// AccCredentialStore (DPAPI-protected machine file, atomic save, cross-process refresh
// lock). The project's container ids come from the project's acc_settings.json
// (AccProjectScope), never from the machine file alone.

using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Empty for a PKCE (public, "Desktop app") APS client: no secret is ever
        /// stored on the workstation. Set for a "Traditional Web App" (confidential) client.</summary>
        public string ClientSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiry { get; set; }
        /// <summary>When the current refresh token was issued. Drives the "sign-in lapses in N
        /// days" warning and the background keep-alive (see AccSignInLifetime).</summary>
        public DateTime RefreshTokenIssuedAt { get; set; }
        public string HubId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        /// <summary>ACC Model-Coordination container id. Falls back to ProjectId when empty.</summary>
        public string CoordContainerId { get; set; } = string.Empty;
        /// <summary>ACC issue type id for this container, resolved by name (see
        /// <see cref="AccIssueSync.EnsureIssueTypeAsync"/>) or set in acc_settings.json.</summary>
        public string IssueTypeId { get; set; } = string.Empty;
        /// <summary>Subtype of <see cref="IssueTypeId"/>. Only ever sent with that type.</summary>
        public string IssueSubtypeId { get; set; } = string.Empty;
        /// <summary>Multiply ACC clash 'dist' by this to get millimetres (default 1000 = metres).</summary>
        public double DistToMm { get; set; } = 1000.0;
        /// <summary>ACC Docs folder URN to upload into. Project-scoped: set it in acc_settings.json.</summary>
        public string FolderUrn { get; set; } = string.Empty;
        /// <summary>US (default), EMEA or AUS.</summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>A PKCE client carries no secret; token requests send client_id in the body.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsPublicClient => string.IsNullOrWhiteSpace(ClientSecret);

        /// <summary>Coordination container, falling back to the Issues/ProjectId container when unset.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string CoordContainer => string.IsNullOrEmpty(CoordContainerId) ? ProjectId : CoordContainerId;

        // JsonIgnore: a computed value is not a credential. Serialising it also threw on a
        // never-set expiry (DateTime.MinValue.AddMinutes(-5) is out of range).
        [Newtonsoft.Json.JsonIgnore]
        public bool IsStale => string.IsNullOrEmpty(AccessToken)
            || AccessTokenExpiry <= DateTime.MinValue.AddMinutes(5)
            || DateTime.UtcNow >= AccessTokenExpiry.AddMinutes(-5);

        // IM-18 bookkeeping, never serialised: where the project values came from, and what
        // the machine file held, so a save restores the file's values instead of copying a
        // project's ids into it. See AccProjectScope.
        [Newtonsoft.Json.JsonIgnore] public AccProjectScopeSource ProjectScope { get; set; } = AccProjectScopeSource.None;
        [Newtonsoft.Json.JsonIgnore] public string FileProjectId { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileCoordContainerId { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileIssueTypeId { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileIssueSubtypeId { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileHubId { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileFolderUrn { get; set; }
        [Newtonsoft.Json.JsonIgnore] public double? FileDistToMm { get; set; }
        [Newtonsoft.Json.JsonIgnore] public string FileRegion { get; set; }
        /// <summary>True once the project scope has been applied, so ToMachineFile knows the
        /// File* values are meaningful.</summary>
        [Newtonsoft.Json.JsonIgnore] public bool ScopeApplied { get; set; }
        /// <summary>The ACC project id this machine's file still holds from before settings were
        /// per project. Shown (never used) so a person can adopt it explicitly.</summary>
        [Newtonsoft.Json.JsonIgnore] public string LegacyProjectId { get; set; } = string.Empty;
    }

    public sealed class AccIssue
    {
        public string Id { get; set; } = string.Empty;
        /// <summary>The human number ACC shows (#123).</summary>
        public string DisplayId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "open";
        /// <summary>ACC issue subtype id (Issues v1 files an issue under a subtype).</summary>
        public string IssueType { get; set; } = string.Empty;
        public string AssignedToUserId { get; set; } = string.Empty;
        /// <summary>user, company or role — required by ACC whenever an assignee is set.</summary>
        public string AssignedToType { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public string LocationDescription { get; set; } = string.Empty;
        /// <summary>Custom attribute values (Issues v1 customAttributes). Sent on create only
        /// when non-empty; read from every pulled issue.</summary>
        public List<AccCustomAttributeValue> CustomAttributes { get; set; } = new List<AccCustomAttributeValue>();
        /// <summary>Root cause id (Issues v1 rootCauseId); empty = none.</summary>
        public string RootCauseId { get; set; } = string.Empty;
        /// <summary>Statuses THIS user may move the issue to, as ACC reported them on a read.
        /// Null when ACC did not say — never an empty list standing in for "unknown".</summary>
        public List<string> PermittedStatuses { get; set; }
        /// <summary>Fields THIS user may edit, as ACC reported them. Null when not reported.</summary>
        public List<string> PermittedAttributes { get; set; }
    }

    /// <summary>One custom attribute value on an ACC issue.</summary>
    public sealed class AccCustomAttributeValue
    {
        public string AttributeDefinitionId { get; set; } = string.Empty;
        /// <summary>The value as ACC carries it (string or number); null = no value.</summary>
        public JToken Value { get; set; }
        public string ValueText => Value == null || Value.Type == JTokenType.Null ? string.Empty : Value.ToString();
    }

    /// <summary>An issue custom attribute definition (GET …/issue-attribute-definitions).</summary>
    public sealed class AccIssueAttributeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>list / text / paragraph / numeric.</summary>
        public string DataType { get; set; } = string.Empty;
    }

    /// <summary>A root cause (GET …/issue-root-cause-categories?include=rootcauses).</summary>
    public sealed class AccRootCause
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CategoryTitle { get; set; } = string.Empty;
    }

    /// <summary>How a write to an existing ACC issue (PATCH, comment) ended.</summary>
    public sealed class AccWriteResult
    {
        public bool Ok { get; set; }
        public int HttpStatus { get; set; }
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public string Detail { get; set; } = string.Empty;
        /// <summary>The parsed response body on success (the updated issue / the comment).</summary>
        public JObject Response { get; set; }
        /// <summary>True when the request may have been applied although no success came back
        /// (no response, or a gateway 5xx). The caller must NOT advance any local record on
        /// the assumption either way, and must say so.</summary>
        public bool Ambiguous => !Ok && (HttpStatus == 0 || HttpStatus == 502 || HttpStatus == 503 || HttpStatus == 504);
    }

    /// <summary>How pushing one issue ended. <see cref="Id"/> is set only on success.</summary>
    public sealed class AccPushResult
    {
        public string Id { get; set; }
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public int HttpStatus { get; set; }
        public string Detail { get; set; } = string.Empty;
        public bool Ok => !string.IsNullOrEmpty(Id);
    }

    public static class AccIssueSync
    {
        internal const string DefaultHost = "https://developer.api.autodesk.com";
        private static string _host = DefaultHost;

        /// <summary>Test seam: point every ACC client at a loopback listener. Pass null to
        /// restore the real APS host.</summary>
        internal static void OverrideHostForTests(string host)
            => _host = string.IsNullOrEmpty(host) ? DefaultHost : host.TrimEnd('/');

        internal static string Host => _host;

        /// <summary>Test seam for the back-off wait (forwards to the shared transport).</summary>
        internal static Func<TimeSpan, Task> DelayHook
        {
            get => AccHttp.DelayHook;
            set => AccHttp.DelayHook = value;
        }

        internal static string AuthUrl   => _host + "/authentication/v2/token";
        private static string IssuesBase => _host + "/construction/issues/v1/projects";

        /// <summary>…/construction/issues/v1/projects/{bare GUID}.</summary>
        private static string ProjectUrl(AccCredentials c) => $"{IssuesBase}/{AccIds.ForAcc(c.ProjectId)}";

        // ── Authentication ───────────────────────────────────────────────────

        private static readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        /// <summary>True when a usable access token is available. Kept for callers that only
        /// need yes/no; use <see cref="EnsureAuthDetailedAsync"/> to say WHY not.</summary>
        public static async Task<bool> EnsureAuthAsync(AccCredentials creds)
            => (await EnsureAuthDetailedAsync(creds).ConfigureAwait(false)).Ok;

        /// <summary>
        /// Make sure <paramref name="creds"/> carries a usable access token, refreshing it when
        /// stale (or always, with <paramref name="force"/> — used after a 401).
        ///
        /// Two Revit processes share one refresh token, and APS invalidates a refresh token
        /// once it has been used. So the refresh runs under a cross-process lock file, and
        /// inside it the machine file is re-read: if another process has already rotated the
        /// token, its result is adopted instead of spending a dead refresh token.
        /// </summary>
        public static async Task<AccAuthOutcome> EnsureAuthDetailedAsync(AccCredentials creds, bool force = false)
        {
            if (creds == null) return AccAuthOutcome.Rejected("no ACC credentials were supplied");
            if (!force && !creds.IsStale) return AccAuthOutcome.Success();

            await _tokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && !creds.IsStale) return AccAuthOutcome.Success();

                using (var fileLock = await AccCredentialStore.AcquireRefreshLockAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
                {
                    if (fileLock == null)
                        StingLog.Warn("AccIssueSync: could not take the credentials refresh lock in 20 s — refreshing without it");

                    string rejectedAccessToken = force ? creds.AccessToken : null;
                    if (TryAdoptFromMachineFile(creds, rejectedAccessToken)) return AccAuthOutcome.Success();

                    if (string.IsNullOrEmpty(creds.RefreshToken))
                        return AccAuthOutcome.Rejected(
                            "this machine is not signed in to Autodesk (no refresh token) — use 'Sign in with Autodesk' on the ACC card");
                    if (string.IsNullOrEmpty(creds.ClientId))
                        return AccAuthOutcome.Rejected("no APS Client ID is configured — enter it on the ACC card");

                    var form = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("refresh_token", creds.RefreshToken),
                    };
                    var tok = await TokenRequestAsync(creds, form, CancellationToken.None).ConfigureAwait(false);
                    if (!tok.Ok) return tok.Outcome;

                    ApplyTokenResponse(creds, tok.Json);
                    string warn = PersistAfterRefresh(creds);
                    return AccAuthOutcome.Success(warn);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("AccIssueSync.EnsureAuth failed", ex);
                return AccAuthOutcome.Unreachable("the token refresh did not complete: " + ex.Message);
            }
            finally { _tokenLock.Release(); }
        }

        /// <summary>If another process rotated the token since <paramref name="creds"/> was
        /// loaded, take its result. Returns true when an adopted access token is usable now.</summary>
        private static bool TryAdoptFromMachineFile(AccCredentials creds, string rejectedAccessToken)
        {
            AccCredentials file;
            try { file = AccCredentialStore.Load(out _); }
            catch (Exception ex) { StingLog.Warn("AccIssueSync: machine credentials unreadable during refresh: " + ex.Message); return false; }
            if (file == null || string.IsNullOrEmpty(file.RefreshToken)) return false;
            if (!string.Equals(file.ClientId, creds.ClientId, StringComparison.Ordinal)) return false;

            bool rotatedElsewhere = !string.Equals(file.RefreshToken, creds.RefreshToken, StringComparison.Ordinal);
            if (!rotatedElsewhere) return false;

            creds.RefreshToken = file.RefreshToken;
            creds.RefreshTokenIssuedAt = file.RefreshTokenIssuedAt;
            bool freshAccess = !file.IsStale && !string.Equals(file.AccessToken, rejectedAccessToken, StringComparison.Ordinal);
            if (freshAccess)
            {
                creds.AccessToken = file.AccessToken;
                creds.AccessTokenExpiry = file.AccessTokenExpiry;
                StingLog.Info("AccIssueSync: adopted a token another STING session had already refreshed");
                return true;
            }
            StingLog.Info("AccIssueSync: another STING session rotated the refresh token — refreshing with the current one");
            return false;
        }

        internal sealed class TokenResult
        {
            public bool Ok;
            public JObject Json;
            public AccAuthOutcome Outcome;
        }

        /// <summary>POST to the APS token endpoint. Confidential clients authenticate with
        /// HTTP Basic; a PKCE (public) client sends client_id in the body and no secret.
        /// A 400/401 is Autodesk refusing the grant (sign in again); anything else is
        /// "could not reach Autodesk" (try again) — the two were one bool before.</summary>
        internal static async Task<TokenResult> TokenRequestAsync(
            AccCredentials creds, List<KeyValuePair<string, string>> form, CancellationToken ct)
        {
            var fields = new List<KeyValuePair<string, string>>(form);
            if (creds.IsPublicClient) fields.Add(new KeyValuePair<string, string>("client_id", creds.ClientId));

            var resp = await AccHttp.SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, AuthUrl) { Content = new FormUrlEncodedContent(fields) };
                if (!creds.IsPublicClient)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.ClientId}:{creds.ClientSecret}")));
                return req;
            }, creds: null, idempotent: false, ct: ct, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            if (resp.Status == 0)
                return new TokenResult { Outcome = AccAuthOutcome.Unreachable("could not reach the Autodesk sign-in service: " + resp.Error) };
            if (!resp.IsSuccess)
            {
                string code = TryReadError(resp.Body);
                StingLog.Warn($"AccIssueSync: token endpoint returned {resp.Status} {code}");
                if (resp.Status == 400 || resp.Status == 401)
                    return new TokenResult
                    {
                        Outcome = AccAuthOutcome.Rejected(
                            $"Autodesk refused the sign-in (HTTP {resp.Status}{(code.Length > 0 ? ", " + code : "")}). " +
                            "The refresh token has expired or was already used — sign in again on the ACC card. " +
                            "An unused sign-in lapses after about " + AccSignInLifetime.RefreshTokenLifetime.TotalDays + " days.")
                    };
                return new TokenResult { Outcome = AccAuthOutcome.Unreachable($"the Autodesk sign-in service answered HTTP {resp.Status}") };
            }
            JObject json;
            try { json = JObject.Parse(resp.Body); }
            catch (Exception ex) { return new TokenResult { Outcome = AccAuthOutcome.Unreachable("the token response was not JSON: " + ex.Message) }; }
            if (string.IsNullOrEmpty((string)json["access_token"]))
                return new TokenResult { Outcome = AccAuthOutcome.Unreachable("the token response carried no access_token") };
            return new TokenResult { Ok = true, Json = json, Outcome = AccAuthOutcome.Success() };
        }

        internal static void ApplyTokenResponse(AccCredentials creds, JObject json)
        {
            creds.AccessToken = (string)json["access_token"] ?? creds.AccessToken;
            string newRefresh = (string)json["refresh_token"];
            if (!string.IsNullOrEmpty(newRefresh))
            {
                creds.RefreshToken = newRefresh;
                creds.RefreshTokenIssuedAt = DateTime.UtcNow;
            }
            int expiresIn = (int?)json["expires_in"] ?? 3600;
            creds.AccessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
        }

        /// <summary>Save after a refresh. A failure here is serious: the rotated refresh token
        /// now exists only in memory. Returned as a warning the caller must show.</summary>
        private static string PersistAfterRefresh(AccCredentials creds)
        {
            if (SaveCredentials(creds, out string err)) return string.Empty;
            string msg = "The Autodesk token was refreshed but could not be saved (" + err + "). It works for this " +
                         "Revit session only — the next session will need 'Sign in with Autodesk' again.";
            StingLog.Error("AccIssueSync: " + msg);
            return msg;
        }

        private static string TryReadError(string body)
        {
            try
            {
                var j = JObject.Parse(body ?? "");
                return ((string)(j["error"] ?? j["errorCode"] ?? j["code"]) ?? string.Empty).Trim();
            }
            catch (Exception) { return string.Empty; }
        }

        // ── Issue type ───────────────────────────────────────────────────────

        /// <summary>
        /// Resolve the issue type + subtype STING files its issues under, for THIS container.
        ///
        /// A project setting (issueTypeId / issueSubtypeId in acc_settings.json) or an
        /// already-resolved value wins. Otherwise the container's ACTIVE types are fetched and
        /// matched BY NAME ("clash", then "coordination"). If nothing matches by name, nothing
        /// is chosen: the old "first type offered" fallback filed clash issues under whatever
        /// the project admin happened to create first (often "Design" or "Safety"), which is
        /// the wrong person's queue. The failure names every type so the fix is one setting.
        /// </summary>
        public static async Task<AccFetchResult<string>> ResolveIssueTypeAsync(AccCredentials creds)
        {
            if (creds == null) return AccFetchResult<string>.Failure(AccFetchStatus.NotFound, "", 0, "no credentials");
            if (!string.IsNullOrEmpty(creds.IssueTypeId) && !string.IsNullOrEmpty(creds.IssueSubtypeId))
                return AccFetchResult<string>.Success(creds.IssueSubtypeId, empty: false);

            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Get,
                    $"{ProjectUrl(creds)}/issue-types?include=subtypes&limit=100"), creds),
                creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
            {
                var st = resp.Classify();
                return AccFetchResult<string>.Failure(st, "", resp.Status, "listing ACC issue types: " + resp.Describe());
            }

            JArray results = AccFetchOutcome.FindArray(resp.Body, "results");
            if (results == null)
                return AccFetchResult<string>.Failure(AccFetchStatus.TransportFailed, "", resp.Status,
                    "the issue-types response carried no 'results' array");

            var active = results.Where(t => t["isActive"] == null || (bool?)t["isActive"] != false).ToList();
            var choice = IssueTypeChooser.Choose(active, creds.IssueTypeId);
            if (!choice.Ok)
                return AccFetchResult<string>.Failure(AccFetchStatus.NotFound, "", resp.Status, choice.Reason);

            // Remembered on these credentials for the rest of THIS run only. It is NOT persisted:
            // on project-scoped credentials ToMachineFile writes back the machine file's own
            // type ids, and the project's settings file needs a Document (the Revit side)
            // to be written. So an unconfigured project re-resolves by name on every run -
            // correct (inactive types skipped, never by position), just one extra read. To pin
            // it, set issueTypeId / issueSubtypeId in the project's ACC settings (the BCC card).
            creds.IssueTypeId = choice.TypeId;
            creds.IssueSubtypeId = choice.SubtypeId;
            SaveCredentials(creds, out _);   // persists the refreshed token (project-scoped: not the type ids)
            StingLog.Info($"AccIssueSync: filing issues as '{choice.TypeTitle} / {choice.SubtypeTitle}' ({choice.SubtypeId}).");
            return AccFetchResult<string>.Success(choice.SubtypeId, empty: false);
        }

        /// <summary>Back-compat wrapper.</summary>
        public static async Task<bool> EnsureIssueTypeAsync(AccCredentials creds)
            => (await ResolveIssueTypeAsync(creds).ConfigureAwait(false)).Succeeded;

        // ── Push ─────────────────────────────────────────────────────────────

        /// <summary>Push a STING-originated issue to ACC. Returns the new issue id, or null.
        /// Use <see cref="PushIssueDetailedAsync"/> to report why a push failed.</summary>
        public static async Task<string> PushIssueAsync(AccCredentials creds, AccIssue issue)
            => (await PushIssueDetailedAsync(creds, issue).ConfigureAwait(false)).Id;

        public static async Task<AccPushResult> PushIssueDetailedAsync(AccCredentials creds, AccIssue issue)
        {
            var result = new AccPushResult();
            string subtype = !string.IsNullOrEmpty(issue.IssueType) ? issue.IssueType : creds.IssueSubtypeId;
            if (string.IsNullOrEmpty(subtype))
            {
                var t = await ResolveIssueTypeAsync(creds).ConfigureAwait(false);
                if (!t.Succeeded)
                {
                    result.Status = t.Status;
                    result.HttpStatus = t.HttpStatus;
                    result.Detail = "no ACC issue type to file under: " + t.Detail;
                    return result;
                }
                subtype = t.Value;
            }

            string payload = BuildIssueBody(issue, subtype).ToString();

            // Not idempotent: a create that timed out may have happened. AccHttp retries it
            // only on 429 (never processed) and 503-with-Retry-After, never on a bare 5xx.
            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Post, $"{ProjectUrl(creds)}/issues")
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, creds),
                creds, idempotent: false).ConfigureAwait(false);

            result.HttpStatus = resp.Status;
            if (!resp.IsSuccess)
            {
                result.Status = resp.Classify();
                result.Detail = resp.Describe() + (resp.Status == 429 ? " — Autodesk rate-limited every attempt" : "") +
                                (resp.Status == 400 ? " — ACC rejected the issue: " + Trim(resp.Body) : "");
                StingLog.Warn($"AccIssueSync.PushIssue failed: {result.Detail}");
                return result;
            }
            try
            {
                result.Id = (string)JObject.Parse(resp.Body)["id"];
                result.Status = string.IsNullOrEmpty(result.Id) ? AccFetchStatus.TransportFailed : AccFetchStatus.Ok;
                if (string.IsNullOrEmpty(result.Id)) result.Detail = "ACC accepted the issue but returned no id";
            }
            catch (Exception ex)
            {
                result.Status = AccFetchStatus.TransportFailed;
                result.Detail = "the create-issue response was not JSON: " + ex.Message;
            }
            return result;
        }

        /// <summary>The Issues v1 create body (POST …/projects/{id}/issues; required: title,
        /// issueSubtypeId, status — the type follows from the subtype, so issueTypeId is not
        /// sent). Only fields with a value are sent: an empty string where ACC expects an id is
        /// a rejection. Lengths are capped to the documented limits (title 100, description
        /// 1000, locationDetails 250) so a long triage rationale cannot fail the whole push.</summary>
        internal static JObject BuildIssueBody(AccIssue issue, string subtypeId)
        {
            var body = new JObject
            {
                ["title"] = Cap(issue.Title, 100),
                ["status"] = string.IsNullOrEmpty(issue.Status) ? "open" : issue.Status,
                ["issueSubtypeId"] = subtypeId,
            };
            if (!string.IsNullOrEmpty(issue.Description)) body["description"] = Cap(issue.Description, 1000);
            if (!string.IsNullOrEmpty(issue.LocationDescription)) body["locationDetails"] = Cap(issue.LocationDescription, 250);
            if (!string.IsNullOrEmpty(issue.AssignedToUserId) && !string.IsNullOrEmpty(issue.AssignedToType))
            {
                body["assignedTo"] = issue.AssignedToUserId;
                body["assignedToType"] = issue.AssignedToType;
            }
            if (issue.DueDate.HasValue) body["dueDate"] = issue.DueDate.Value.ToString("yyyy-MM-dd");
            var attrs = (issue.CustomAttributes ?? new List<AccCustomAttributeValue>())
                .Where(a => a != null && !string.IsNullOrEmpty(a.AttributeDefinitionId) &&
                            a.Value != null && a.Value.Type != JTokenType.Null)
                .ToList();
            if (attrs.Count > 0)
                body["customAttributes"] = new JArray(attrs.Select(a => new JObject
                {
                    ["attributeDefinitionId"] = a.AttributeDefinitionId,
                    ["value"] = a.Value.DeepClone(),
                }));
            if (!string.IsNullOrEmpty(issue.RootCauseId)) body["rootCauseId"] = issue.RootCauseId;
            return body;
        }

        // ── Two-way: read one issue, change it, comment on it ───────────────

        /// <summary>GET …/issues/{id}. A read, so retried like every other read.</summary>
        public static async Task<AccFetchResult<AccIssue>> GetIssueAsync(AccCredentials creds, string issueId)
        {
            if (creds == null || string.IsNullOrWhiteSpace(issueId))
                return AccFetchResult<AccIssue>.Failure(AccFetchStatus.NotFound, null, 0, "no credentials or no issue id");
            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Get,
                    $"{ProjectUrl(creds)}/issues/{Uri.EscapeDataString(issueId.Trim())}"), creds),
                creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return AccFetchResult<AccIssue>.Failure(resp.Classify(), null, resp.Status,
                    $"reading ACC issue {issueId}: {resp.Describe()}");
            try
            {
                var j = JObject.Parse(resp.Body);
                if (string.IsNullOrEmpty((string)j["id"]))
                    return AccFetchResult<AccIssue>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                        $"the response for ACC issue {issueId} carried no 'id'");
                var r = AccFetchResult<AccIssue>.Success(ParseIssue(j), empty: false);
                r.HttpStatus = resp.Status;
                return r;
            }
            catch (Exception ex)
            {
                return AccFetchResult<AccIssue>.Failure(AccFetchStatus.TransportFailed, null, resp.Status,
                    $"the response for ACC issue {issueId} was not JSON: {ex.Message}");
            }
        }

        /// <summary>PATCH …/issues/{id} with the given fields (status, assignedTo, …).
        ///
        /// NOT idempotent for our purposes: a PATCH that timed out or met a gateway 5xx may
        /// have been applied, and ACC records every change in the issue's activity log under
        /// the user's name. AccHttp therefore retries it only when the server said it was not
        /// processed (429, 503 with Retry-After); an ambiguous failure is reported as such.</summary>
        public static async Task<AccWriteResult> PatchIssueAsync(AccCredentials creds, string issueId, JObject fields)
        {
            if (creds == null || string.IsNullOrWhiteSpace(issueId) || fields == null || !fields.HasValues)
                return new AccWriteResult { Detail = "nothing to change (no credentials, issue id or fields)" };
            string payload = fields.ToString(Newtonsoft.Json.Formatting.None);
            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(new HttpMethod("PATCH"),
                    $"{ProjectUrl(creds)}/issues/{Uri.EscapeDataString(issueId.Trim())}")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, creds),
                creds, idempotent: false).ConfigureAwait(false);
            return ToWriteResult(resp, "changing ACC issue " + issueId);
        }

        /// <summary>POST …/issues/{id}/comments. Not idempotent: a retried comment is a
        /// duplicate comment in front of the whole project.</summary>
        public static async Task<AccWriteResult> AddCommentAsync(AccCredentials creds, string issueId, string text)
        {
            if (creds == null || string.IsNullOrWhiteSpace(issueId) || string.IsNullOrWhiteSpace(text))
                return new AccWriteResult { Detail = "nothing to comment (no credentials, issue id or text)" };
            string payload = new JObject { ["body"] = Cap(text.Trim(), 10000) }.ToString(Newtonsoft.Json.Formatting.None);
            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Post,
                    $"{ProjectUrl(creds)}/issues/{Uri.EscapeDataString(issueId.Trim())}/comments")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") }, creds),
                creds, idempotent: false).ConfigureAwait(false);
            return ToWriteResult(resp, "commenting on ACC issue " + issueId);
        }

        private static AccWriteResult ToWriteResult(AccHttpResponse resp, string what)
        {
            var r = new AccWriteResult { HttpStatus = resp.Status };
            if (!resp.IsSuccess)
            {
                r.Status = resp.Classify();
                string why = resp.Status switch
                {
                    403 => " — ACC refused: this user may not make that change (permittedStatuses / permittedAttributes)",
                    409 => " — ACC reported a conflict: the issue changed underneath this request",
                    429 => $" — Autodesk rate-limited all {resp.Attempts} attempt(s)",
                    400 or 422 => " — ACC rejected the change: " + Trim(resp.Body),
                    _ => "",
                };
                r.Detail = $"{what}: {resp.Describe()}{why}";
                if (resp.Status == 0 || resp.Status == 502 || resp.Status == 503 || resp.Status == 504)
                    r.Detail += " — the change MAY have been applied; check the issue in ACC before retrying";
                StingLog.Warn("AccIssueSync: " + r.Detail);
                return r;
            }
            r.Ok = true;
            r.Status = AccFetchStatus.Ok;
            try { r.Response = string.IsNullOrWhiteSpace(resp.Body) ? new JObject() : JObject.Parse(resp.Body); }
            catch (Exception) { r.Response = new JObject(); }
            return r;
        }

        /// <summary>The container's issue custom attribute definitions (all pages).</summary>
        public static async Task<AccFetchResult<List<AccIssueAttributeDefinition>>> GetAttributeDefinitionsAsync(AccCredentials creds)
        {
            var list = new List<AccIssueAttributeDefinition>();
            int offset = 0;
            for (int page = 0; page < 20; page++)
            {
                int off = offset;
                var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Get,
                        $"{ProjectUrl(creds)}/issue-attribute-definitions?limit=200&offset={off}"), creds),
                    creds, idempotent: true).ConfigureAwait(false);
                if (!resp.IsSuccess)
                    return AccFetchResult<List<AccIssueAttributeDefinition>>.Failure(resp.Classify(), list, resp.Status,
                        "listing ACC issue custom attributes: " + resp.Describe());
                JArray results = AccFetchOutcome.FindArray(resp.Body, "results");
                if (results == null)
                    return AccFetchResult<List<AccIssueAttributeDefinition>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                        "the issue-attribute-definitions response carried no 'results' array");
                foreach (var t in results)
                    list.Add(new AccIssueAttributeDefinition
                    {
                        Id = (string)t["id"] ?? string.Empty,
                        Title = (string)t["title"] ?? string.Empty,
                        DataType = (string)t["dataType"] ?? string.Empty,
                    });
                int? total = (int?)(results.Parent?.Parent as JObject)?["pagination"]?["totalResults"];
                if (results.Count == 0 || (total.HasValue ? list.Count >= total.Value : results.Count < 200))
                    return AccFetchResult<List<AccIssueAttributeDefinition>>.Success(list, list.Count == 0);
                offset += results.Count;
            }
            return AccFetchResult<List<AccIssueAttributeDefinition>>.Failure(AccFetchStatus.TransportFailed, list, 200,
                "stopped at the 20-page cap reading custom attribute definitions");
        }

        /// <summary>The container's root causes, flattened from their categories.</summary>
        public static async Task<AccFetchResult<List<AccRootCause>>> GetRootCausesAsync(AccCredentials creds)
        {
            var list = new List<AccRootCause>();
            var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Get,
                    $"{ProjectUrl(creds)}/issue-root-cause-categories?include=rootcauses&limit=200"), creds),
                creds, idempotent: true).ConfigureAwait(false);
            if (!resp.IsSuccess)
                return AccFetchResult<List<AccRootCause>>.Failure(resp.Classify(), list, resp.Status,
                    "listing ACC root causes: " + resp.Describe());
            JArray results = AccFetchOutcome.FindArray(resp.Body, "results");
            if (results == null)
                return AccFetchResult<List<AccRootCause>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                    "the issue-root-cause-categories response carried no 'results' array");
            foreach (var cat in results)
            {
                if (cat["isActive"] != null && (bool?)cat["isActive"] == false) continue;
                foreach (var rc in (cat["rootCauses"] as JArray) ?? new JArray())
                {
                    if (rc["isActive"] != null && (bool?)rc["isActive"] == false) continue;
                    list.Add(new AccRootCause
                    {
                        Id = (string)rc["id"] ?? string.Empty,
                        Title = (string)rc["title"] ?? string.Empty,
                        CategoryTitle = (string)cat["title"] ?? string.Empty,
                    });
                }
            }
            return AccFetchResult<List<AccRootCause>>.Success(list, list.Count == 0);
        }

        /// <summary>Read an issue from Issues v1 (camelCase). The pre-v1 snake_case names are
        /// read as a fallback so a payload from either shape maps.</summary>
        internal static AccIssue ParseIssue(JToken t) => new AccIssue
        {
            Id = (string)t["id"] ?? string.Empty,
            DisplayId = (string)t["displayId"] ?? string.Empty,
            Title = (string)t["title"] ?? string.Empty,
            Description = (string)t["description"] ?? string.Empty,
            Status = (string)t["status"] ?? "open",
            IssueType = (string)(t["issueSubtypeId"] ?? t["issueTypeId"] ?? t["issue_type_id"]) ?? string.Empty,
            AssignedToUserId = (string)(t["assignedTo"] ?? t["assigned_to"]) ?? string.Empty,
            AssignedToType = (string)t["assignedToType"] ?? string.Empty,
            DueDate = ReadDate(t["dueDate"]),
            CreatedAt = ReadDate(t["createdAt"]) ?? DateTime.UtcNow,
            UpdatedAt = ReadDate(t["updatedAt"]),
            LocationDescription = (string)(t["locationDetails"] ?? t["location_description"]) ?? string.Empty,
            RootCauseId = (string)t["rootCauseId"] ?? string.Empty,
            CustomAttributes = ((t["customAttributes"] as JArray) ?? new JArray())
                .Where(a => a is JObject && !string.IsNullOrEmpty((string)a["attributeDefinitionId"]))
                .Select(a => new AccCustomAttributeValue
                {
                    AttributeDefinitionId = (string)a["attributeDefinitionId"],
                    Value = a["value"]?.DeepClone(),
                }).ToList(),
            PermittedStatuses = StringList(t["permittedStatuses"]),
            PermittedAttributes = StringList(t["permittedAttributes"]),
        };

        /// <summary>A JSON string array as a list; null when absent or not an array, so "ACC
        /// did not say" stays distinguishable from "ACC said nothing is permitted".</summary>
        private static List<string> StringList(JToken t)
            => t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).ToList() : null;

        private static DateTime? ReadDate(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Date) return ((DateTime)t).ToUniversalTime();
            return DateTime.TryParse((string)t, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? d : (DateTime?)null;
        }

        // ── Pull ─────────────────────────────────────────────────────────────

        /// <summary>Pull the full issue set, following pagination. A PARTIAL read is a
        /// FAILURE: only a run that saw the last page is Ok/EmptyOk; rows read before a break
        /// are still in Value for diagnostics, and Detail names how many pages succeeded.</summary>
        /// <param name="updatedSince">When set, only issues updated at or after this instant
        /// (filter[updatedAt]=&lt;from&gt;..). A successful filtered read is complete FOR THAT
        /// WINDOW only — the caller must not treat an issue it did not return as deleted.</param>
        public static async Task<AccFetchResult<List<AccIssue>>> PullIssuesAsync(
            AccCredentials creds, int pageSize = 100, int maxPages = 200, DateTime? updatedSince = null)
        {
            string filter = updatedSince.HasValue ? "&" + UpdatedSinceQuery(updatedSince.Value) : string.Empty;
            var list = new List<AccIssue>();
            var auth = await EnsureAuthDetailedAsync(creds).ConfigureAwait(false);
            if (!auth.Ok)
                return AccFetchResult<List<AccIssue>>.Failure(auth.Status, list, 0,
                    auth.Detail + " — so no issue was read at all");

            int offset = 0, pagesRead = 0;
            int? total = null;
            for (int page = 0; page < maxPages; page++)
            {
                int off = offset;
                var resp = await AccHttp.SendAsync(() => WithRegion(new HttpRequestMessage(HttpMethod.Get,
                        $"{ProjectUrl(creds)}/issues?limit={pageSize}&offset={off}{filter}"), creds),
                    creds, idempotent: true).ConfigureAwait(false);

                if (!resp.IsSuccess)
                {
                    var st = resp.Classify();
                    StingLog.Warn($"AccIssueSync.PullIssues {resp.Status} at offset {offset}");
                    return AccFetchResult<List<AccIssue>>.Failure(st, list, resp.Status,
                        $"the issue list failed at page {pagesRead + 1} (offset {offset}) after " +
                        $"{pagesRead} page(s) succeeded — {resp.Describe()}" +
                        (resp.Status == 429 ? $" (Autodesk rate-limited the request on all {resp.Attempts} attempts)" : "") + ". " +
                        $"The {list.Count} issue(s) already read are an INCOMPLETE set and must not be reconciled against.");
                }

                JObject j;
                try { j = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<List<AccIssue>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                        $"page {pagesRead + 1} of the issue list was not valid JSON ({ex.Message}) after {pagesRead} page(s) succeeded");
                }

                var results = j["results"] as JArray;
                if (results == null)
                    return AccFetchResult<List<AccIssue>>.Failure(AccFetchStatus.TransportFailed, list, resp.Status,
                        $"page {pagesRead + 1} of the issue list carried no 'results' array — the " +
                        "construction/issues/v1 payload shape has changed");

                foreach (var t in results) list.Add(ParseIssue(t));
                pagesRead++;
                total = (int?)j["pagination"]?["totalResults"] ?? total;

                bool lastPage = total.HasValue ? list.Count >= total.Value || results.Count == 0 : results.Count < pageSize;
                if (lastPage) return AccFetchResult<List<AccIssue>>.Success(list, list.Count == 0);
                offset += results.Count;
            }

            return AccFetchResult<List<AccIssue>>.Failure(AccFetchStatus.TransportFailed, list, 200,
                $"stopped at the {maxPages}-page cap with {pagesRead} page(s) read and no final page — the " +
                "container holds more issues than this read covered, so the set is INCOMPLETE");
        }

        /// <summary>The Issues v1 updated-since filter: filter[updatedAt]=&lt;ISO UTC&gt;.. (an
        /// open-ended range). Milliseconds and a Z suffix, the form the API documents.</summary>
        internal static string UpdatedSinceQuery(DateTime sinceUtc)
        {
            var u = sinceUtc.Kind == DateTimeKind.Local ? sinceUtc.ToUniversalTime() : sinceUtc;
            string iso = u.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            return "filter[updatedAt]=" + Uri.EscapeDataString(iso + "..");
        }

        /// <summary>True when an ACC issue status is finished work. Routed through the one
        /// status vocabulary the rest of STING uses, so "closed in ACC" and "closed in STING"
        /// cannot disagree.</summary>
        public static bool IsClosedStatus(string status)
            => !string.IsNullOrEmpty(status) && IssueStatusNormalizer.IsTerminal(IssueStatusNormalizer.NormalizeAcc(status));

        // ── Credentials file ─────────────────────────────────────────────────

        public static string CredentialsPath => AccCredentialStore.CredentialsPath;

        public static AccCredentials LoadCredentials()
        {
            try
            {
                var c = AccCredentialStore.Load(out string warning);
                if (!string.IsNullOrEmpty(warning)) StingLog.Warn("AccIssueSync.LoadCredentials: " + warning);
                return c;
            }
            catch (Exception ex)
            {
                StingLog.Warn("AccIssueSync.LoadCredentials: " + ex.Message);
                return new AccCredentials();
            }
        }

        /// <summary>The JSON the machine credentials file receives. Project-scoped values that
        /// came from the project's settings are replaced by what the file already held - a
        /// project's ids never leak into the machine-wide file. Revit-free, so it is unit-tested.</summary>
        public static JObject ToMachineFile(AccCredentials c)
        {
            var j = JObject.FromObject(c);
            if (c.ScopeApplied)
            {
                // Project values came from the project (or are blank because the project has
                // none); either way the machine file keeps exactly what it had.
                j["ProjectId"] = c.FileProjectId ?? string.Empty;
                j["CoordContainerId"] = c.FileCoordContainerId ?? string.Empty;
                j["IssueTypeId"] = c.FileIssueTypeId ?? string.Empty;
                j["IssueSubtypeId"] = c.FileIssueSubtypeId ?? string.Empty;
                j["HubId"] = c.FileHubId ?? string.Empty;
                j["FolderUrn"] = c.FileFolderUrn ?? string.Empty;
                j["DistToMm"] = c.FileDistToMm ?? 1000.0;
                j["Region"] = c.FileRegion ?? string.Empty;
            }
            return j;
        }

        public static void SaveCredentials(AccCredentials c) => SaveCredentials(c, out _);

        public static bool SaveCredentials(AccCredentials c, out string error)
        {
            bool ok = AccCredentialStore.Save(ToMachineFile(c), out error);
            if (!ok) StingLog.Warn("AccIssueSync.SaveCredentials: " + error);
            return ok;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        internal static HttpRequestMessage WithRegion(HttpRequestMessage req, AccCredentials c)
        {
            AccIds.ApplyRegion(req, c?.Region);
            return req;
        }

        private static string Cap(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s.Substring(0, max - 1) + "…";
        private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(0, 300) : s);
    }

    /// <summary>
    /// Keeps the Autodesk sign-in alive. A refresh token lapses after 15 days unused and the
    /// KUT coordination cycle runs every 14, so one late cycle would otherwise mean a failed
    /// run and an interactive sign-in. When a model opens and the refresh token is older than
    /// <see cref="AccSignInLifetime.KeepAliveAfter"/>, it is refreshed in the background (the
    /// refresh issues a new token and restarts the 15-day clock). Network only — no Revit
    /// API — so it never touches the UI thread; at most one attempt per 6 hours per session.
    /// </summary>
    public static class AccTokenKeepAlive
    {
        private static long _lastAttemptTicks;

        public static void MaybeRefreshInBackground()
        {
            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _lastAttemptTicks);
            if (now - last < TimeSpan.FromHours(6).Ticks) return;
            if (Interlocked.CompareExchange(ref _lastAttemptTicks, now, last) != last) return;

            Task.Run(async () =>
            {
                try
                {
                    var c = AccIssueSync.LoadCredentials();
                    if (!AccSignInLifetime.ShouldKeepAlive(c, DateTime.UtcNow)) return;
                    var r = await AccIssueSync.EnsureAuthDetailedAsync(c, force: true).ConfigureAwait(false);
                    if (r.Ok) StingLog.Info("ACC keep-alive: sign-in renewed. " + AccSignInLifetime.Describe(c, DateTime.UtcNow));
                    else StingLog.Warn($"ACC keep-alive: could not renew the Autodesk sign-in ({r.Status}): {r.Detail}");
                }
                catch (Exception ex) { StingLog.Warn("ACC keep-alive: " + ex.Message); }
            });
        }
    }

    /// <summary>Pick an issue type/subtype by NAME. Pure, so the rule is tested.</summary>
    public static class IssueTypeChooser
    {
        public sealed class Choice
        {
            public bool Ok;
            public string TypeId = "", TypeTitle = "", SubtypeId = "", SubtypeTitle = "", Reason = "";
        }

        private static readonly string[] Preferred = { "clash", "coordination" };

        /// <param name="types">Active issue types, each with a 'subtypes' array.</param>
        /// <param name="configuredTypeId">A type id the project configured without a subtype, or empty.</param>
        public static Choice Choose(IList<JToken> types, string configuredTypeId)
        {
            types = types ?? new List<JToken>();
            if (types.Count == 0)
                return new Choice { Reason = "the ACC project has no active issue types — ask the project admin to enable one" };

            JToken type = null;
            if (!string.IsNullOrEmpty(configuredTypeId))
            {
                type = types.FirstOrDefault(t => string.Equals((string)t["id"], configuredTypeId, StringComparison.OrdinalIgnoreCase));
                if (type == null)
                    return new Choice { Reason = $"the configured issueTypeId '{configuredTypeId}' is not an active type here. " + Offered(types) };
            }
            else
            {
                foreach (var word in Preferred)
                {
                    type = types.FirstOrDefault(t => Title(t).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (type != null) break;
                }
                // Also accept a type whose SUBTYPE is called clash (e.g. "Coordination > Clash"
                // is caught above; "Quality > Clash" is caught here).
                if (type == null)
                    type = types.FirstOrDefault(t => Subtypes(t).Any(s => Title(s).IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0));
                if (type == null)
                    return new Choice
                    {
                        Reason = "no ACC issue type is named Clash or Coordination, and STING will not file clash issues " +
                                 "under an unrelated type. Set issueTypeId / issueSubtypeId in the project's ACC settings " +
                                 "(BIM Coordination Center > ACC). " + Offered(types)
                    };
            }

            var subs = Subtypes(type).Where(s => s["isActive"] == null || (bool?)s["isActive"] != false).ToList();
            JToken sub = null;
            foreach (var word in Preferred)
            {
                sub = subs.FirstOrDefault(s => Title(s).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                if (sub != null) break;
            }
            if (sub == null && subs.Count == 1) sub = subs[0];   // the type's only subtype is not a guess
            if (sub == null)
                return new Choice
                {
                    Reason = $"issue type '{Title(type)}' has {subs.Count} subtypes and none is named Clash or Coordination — " +
                             "set issueSubtypeId in the project's ACC settings. Subtypes: " +
                             string.Join(", ", subs.Select(s => $"'{Title(s)}' [{(string)s["id"]}]"))
                };

            return new Choice
            {
                Ok = true,
                TypeId = (string)type["id"] ?? "",
                TypeTitle = Title(type),
                SubtypeId = (string)sub["id"] ?? "",
                SubtypeTitle = Title(sub),
            };
        }

        private static string Title(JToken t) => (string)t?["title"] ?? string.Empty;
        private static IEnumerable<JToken> Subtypes(JToken t) => (t?["subtypes"] as JArray) ?? new JArray();
        private static string Offered(IList<JToken> types) =>
            "Types offered: " + string.Join(", ", types.Select(t => $"'{Title(t)}' [{(string)t["id"]}]"));
    }
}
