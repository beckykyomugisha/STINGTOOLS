// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccProjectMembers.cs
//
// Who is on the ACC project, which company each person represents, and which roles they
// hold — read so an escalation can be assigned by an EMAIL or a role/company NAME instead
// of an opaque id, and so imported issues can show the assignee's name.
//
// SOURCE (APS docs, read 2026-09-30):
//   GET construction/admin/v1/projects/{projectId}/users?limit=200&offset=N
//   scope account:read; 2- or 3-legged ("user context optional"); pages of at most 200
//   with pagination.totalResults. Each result carries id (Forma id), autodeskId, email,
//   name, companyId, companyName, roles[{id,name}], status.
//   PERMISSION: the reference does not say a plain member may call it. The Admin API is an
//   administrator surface, so a 403 for a non-admin is EXPECTED and is reported as
//   "needs Project/Account Admin", never as "the project has no members". UNCONFIRMED live.
//   Companies and roles are DERIVED from the user list (companyId/companyName, roles[]),
//   because the dedicated company endpoint (hq/v1/.../projects/{id}/companies) is
//   2-legged "app only" — it needs a Custom Integration this plug-in does not have.
//
// WHICH ID ISSUES WANTS. POST construction/issues/v1/.../issues documents assignedTo as
// "The Autodesk ID of the member, role or company". For a USER that is autodeskId (not the
// Forma id), and that is what a resolved user becomes. For a ROLE or COMPANY the Admin
// role/company id is used; that it is the id Issues accepts is UNCONFIRMED (the Issues
// reference itself says "we do not currently provide endpoints to find" assignable ids).
//
// RESOLUTION RULES (never a wrong assignee):
//   * type user + email       → the ONE active member with that email; its autodeskId.
//   * type user + id          → a member whose autodeskId or Forma id matches; its autodeskId.
//   * type user + name        → the ONE member with exactly that name (case-insensitive).
//   * type company/role + id  → a company/role with that id.
//   * type company/role + name→ the ONE company/role with exactly that name.
//   * zero matches → refused;  two or more → refused as ambiguous, naming them.
//   * member list unreadable (403 etc.): an id is sent as configured (the old behaviour —
//     ACC itself rejects an unknown id); an email or a name is REFUSED, because nothing
//     could turn it into an id.
//
// Revit-free and log-free; linked by StingTools.Acc.Tests.

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
    public sealed class AccProjectUser
    {
        public string Id { get; set; } = string.Empty;          // Forma (Admin) id
        public string AutodeskId { get; set; } = string.Empty;  // what Issues' assignedTo wants
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CompanyId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<AccNamedId> Roles { get; set; } = new List<AccNamedId>();
    }

    public sealed class AccNamedId
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Users, plus the companies and roles derived from them.</summary>
    public sealed class AccProjectDirectory
    {
        public List<AccProjectUser> Users { get; set; } = new List<AccProjectUser>();
        public List<AccNamedId> Companies { get; set; } = new List<AccNamedId>();
        public List<AccNamedId> Roles { get; set; } = new List<AccNamedId>();

        public static AccProjectDirectory From(List<AccProjectUser> users)
        {
            var d = new AccProjectDirectory { Users = users ?? new List<AccProjectUser>() };
            var companies = new Dictionary<string, AccNamedId>(StringComparer.OrdinalIgnoreCase);
            var roles = new Dictionary<string, AccNamedId>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in d.Users)
            {
                if (!string.IsNullOrEmpty(u.CompanyId) && !companies.ContainsKey(u.CompanyId))
                    companies[u.CompanyId] = new AccNamedId { Id = u.CompanyId, Name = u.CompanyName ?? "" };
                foreach (var r in u.Roles)
                    if (!string.IsNullOrEmpty(r.Id) && !roles.ContainsKey(r.Id)) roles[r.Id] = r;
            }
            d.Companies = companies.Values.ToList();
            d.Roles = roles.Values.ToList();
            return d;
        }

        /// <summary>The display name for an assignee id of the given type, or empty.</summary>
        public string NameFor(string id, string type)
        {
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            id = id.Trim();
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "user":
                    var u = Users.FirstOrDefault(x => Eq(x.AutodeskId, id) || Eq(x.Id, id));
                    return u == null ? "" : (u.Name.Length > 0 ? u.Name : u.Email);
                case "company": return Companies.FirstOrDefault(x => Eq(x.Id, id))?.Name ?? "";
                case "role": return Roles.FirstOrDefault(x => Eq(x.Id, id))?.Name ?? "";
                default:
                    // Untyped (older rows): try every kind, accept only an unambiguous hit.
                    var names = new[] { NameFor(id, "user"), NameFor(id, "company"), NameFor(id, "role") }
                        .Where(n => n.Length > 0).Distinct().ToList();
                    return names.Count == 1 ? names[0] : "";
            }
        }

        internal static bool Eq(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Outcome of turning a configured assignee into what ACC Issues needs.</summary>
    public sealed class AccAssigneeResolution
    {
        public bool Ok { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>Why it was refused, or (when Ok) a note such as "sent as configured".</summary>
        public string Reason { get; set; } = string.Empty;
        /// <summary>Ok, but NOT checked against the member list (it could not be read).</summary>
        public bool Unverified { get; set; }

        public string Describe() => Ok
            ? $"{Type} '{(DisplayName.Length > 0 ? DisplayName : Id)}'" + (DisplayName.Length > 0 ? $" [{Id}]" : "") +
              (Reason.Length > 0 ? " — " + Reason : "")
            : "REFUSED: " + Reason;
    }

    public static class AccProjectMembers
    {
        private sealed class CacheEntry { public AccProjectDirectory Dir; public DateTime At; }
        private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _gate = new object();
        /// <summary>A session cache: members change rarely, and one clash cycle may resolve an
        /// assignee and name dozens of issues. Only a SUCCESSFUL read is cached.</summary>
        public static TimeSpan CacheLifetime { get; set; } = TimeSpan.FromMinutes(30);

        internal static void ClearCache() { lock (_gate) _cache.Clear(); }

        /// <summary>Every member of the project (all pages). A failed or partial read is a failure.</summary>
        public static async Task<AccFetchResult<AccProjectDirectory>> GetDirectoryAsync(AccCredentials creds, bool useCache = true)
        {
            creds = creds ?? new AccCredentials();
            string pid = AccIds.ForAcc(creds.ProjectId);
            if (pid.Length == 0)
                return AccFetchResult<AccProjectDirectory>.Failure(AccFetchStatus.NotFound, new AccProjectDirectory(), 0,
                    "no ACC project is configured for this model");

            string key = pid + "|" + AccIds.NormaliseRegion(creds.Region);
            if (useCache)
                lock (_gate)
                    if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheLifetime)
                        return AccFetchResult<AccProjectDirectory>.Success(hit.Dir, hit.Dir.Users.Count == 0);

            var users = new List<AccProjectUser>();
            int offset = 0;
            const int limit = 200;
            for (int page = 0; page < 200; page++)
            {
                string url = $"{AccIssueSync.Host}/construction/admin/v1/projects/{Uri.EscapeDataString(pid)}/users?limit={limit}&offset={offset}";
                var resp = await AccHttp.SendAsync(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    return AccIssueSync.WithRegion(req, creds);
                }, creds, idempotent: true, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                if (resp.Auth != null && !resp.Auth.Ok)
                    return AccFetchResult<AccProjectDirectory>.Failure(resp.Auth.Status, new AccProjectDirectory(), 0, resp.Auth.Detail);
                if (!resp.IsSuccess)
                {
                    string why = resp.Status == 403 ? AccProjectDetailsClient.AdminForbidden : resp.Describe();
                    return AccFetchResult<AccProjectDirectory>.Failure(resp.Classify(), new AccProjectDirectory(), resp.Status,
                        "listing the ACC project members failed" + (page > 0 ? $" on page {page + 1} (the list is INCOMPLETE)" : "") + ": " + why);
                }

                JObject j;
                try { j = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<AccProjectDirectory>.Failure(AccFetchStatus.TransportFailed, new AccProjectDirectory(),
                        resp.Status, "the member list was not valid JSON: " + ex.Message);
                }
                var results = j["results"] as JArray;
                if (results == null)
                    return AccFetchResult<AccProjectDirectory>.Failure(AccFetchStatus.TransportFailed, new AccProjectDirectory(),
                        resp.Status, "the member list carried no 'results' array — the construction/admin/v1 payload shape has changed");

                foreach (var t in results) users.Add(ParseUser(t));
                int? total = (int?)j["pagination"]?["totalResults"];
                offset += results.Count;
                bool last = total.HasValue ? offset >= total.Value || results.Count == 0 : results.Count < limit;
                if (last)
                {
                    var dir = AccProjectDirectory.From(users);
                    lock (_gate) _cache[key] = new CacheEntry { Dir = dir, At = DateTime.UtcNow };
                    return AccFetchResult<AccProjectDirectory>.Success(dir, users.Count == 0);
                }
            }
            return AccFetchResult<AccProjectDirectory>.Failure(AccFetchStatus.TransportFailed, new AccProjectDirectory(), 200,
                "stopped after 200 pages without reaching the last — the member list is INCOMPLETE");
        }

        private static AccProjectUser ParseUser(JToken t)
        {
            var u = new AccProjectUser
            {
                Id = (string)t["id"] ?? "",
                AutodeskId = (string)t["autodeskId"] ?? "",
                Email = (string)t["email"] ?? "",
                Name = (string)t["name"] ?? "",
                CompanyId = (string)t["companyId"] ?? "",
                CompanyName = (string)t["companyName"] ?? "",
                Status = (string)t["status"] ?? "",
            };
            if (u.Name.Length == 0)
                u.Name = (((string)t["firstName"] ?? "") + " " + ((string)t["lastName"] ?? "")).Trim();
            if (t["roles"] is JArray roles)
                foreach (var r in roles)
                    if (!string.IsNullOrEmpty((string)r["id"]))
                        u.Roles.Add(new AccNamedId { Id = (string)r["id"], Name = (string)r["name"] ?? "" });
            return u;
        }

        private static readonly Regex Guidish = new Regex(@"^[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}$");
        private static readonly Regex AutodeskIdish = new Regex(@"^[A-Za-z0-9]{8,40}$");

        /// <summary>Does the configured value look like an id rather than a name/email?</summary>
        public static bool LooksLikeId(string value, string type)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Contains("@") || value.Contains(" ")) return false;
            if (Guidish.IsMatch(value)) return true;
            return string.Equals(type, "user", StringComparison.OrdinalIgnoreCase) && AutodeskIdish.IsMatch(value) && value.Any(char.IsDigit);
        }

        /// <summary>Resolve a configured assignee. <paramref name="directory"/> is the member
        /// read (null/failed = unreadable, with <paramref name="directoryFailure"/> saying why).</summary>
        public static AccAssigneeResolution Resolve(string value, string type, AccProjectDirectory directory, string directoryFailure = "")
        {
            value = (value ?? "").Trim();
            type = (type ?? "").Trim().ToLowerInvariant();
            if (value.Length == 0) return new AccAssigneeResolution { Ok = false, Reason = "no assignee is configured" };
            bool email = value.Contains("@");
            if (type.Length == 0 && email) type = "user";
            if (type != "user" && type != "company" && type != "role")
                return new AccAssigneeResolution { Ok = false, Reason = $"assignee type '{type}' is not user, company or role" };
            if (email && type != "user")
                return new AccAssigneeResolution { Ok = false, Reason = $"'{value}' is an email, but the assignee type is '{type}' — an email names a user" };

            bool idLike = !email && LooksLikeId(value, type);
            if (directory == null)
            {
                if (idLike)
                    return new AccAssigneeResolution
                    {
                        Ok = true, Id = value, Type = type, Unverified = true,
                        Reason = "sent as configured — it could not be checked against the project's members" +
                                 (string.IsNullOrEmpty(directoryFailure) ? "" : " (" + directoryFailure + ")"),
                    };
                return new AccAssigneeResolution
                {
                    Ok = false, Type = type,
                    Reason = $"'{value}' is a {(email ? "email" : "name")}, and the project's member list could not be read to turn it into an id" +
                             (string.IsNullOrEmpty(directoryFailure) ? "" : ": " + directoryFailure) +
                             ". Configure the ACC id instead, or sign in as a Project Admin.",
                };
            }

            if (type == "user")
            {
                var live = directory.Users.Where(u => !string.Equals(u.Status, "deleted", StringComparison.OrdinalIgnoreCase)).ToList();
                List<AccProjectUser> hits;
                string how;
                if (email) { hits = live.Where(u => AccProjectDirectory.Eq(u.Email, value)).ToList(); how = "email"; }
                else
                {
                    hits = live.Where(u => AccProjectDirectory.Eq(u.AutodeskId, value) || AccProjectDirectory.Eq(u.Id, value)).ToList();
                    how = "id";
                    if (hits.Count == 0 && !idLike) { hits = live.Where(u => AccProjectDirectory.Eq(u.Name, value)).ToList(); how = "name"; }
                }
                if (hits.Count == 0)
                    return Refuse($"no project member has the {how} '{value}' ({live.Count} member(s) checked)", type);
                if (hits.Count > 1)
                    return Refuse($"'{value}' matches {hits.Count} project members ({string.Join(", ", hits.Take(5).Select(h => h.Email.Length > 0 ? h.Email : h.Name))}) — ambiguous; configure an email or id", type);
                var hit = hits[0];
                if (string.IsNullOrWhiteSpace(hit.AutodeskId))
                    return Refuse($"member '{(hit.Name.Length > 0 ? hit.Name : hit.Email)}' has no Autodesk ID yet (status '{hit.Status}') — ACC Issues cannot be assigned to them", type);
                if (!string.Equals(hit.Status, "active", StringComparison.OrdinalIgnoreCase) && hit.Status.Length > 0)
                    return Refuse($"member '{(hit.Name.Length > 0 ? hit.Name : hit.Email)}' is '{hit.Status}' in the project, not active", type);
                return new AccAssigneeResolution { Ok = true, Id = hit.AutodeskId, Type = "user", DisplayName = hit.Name.Length > 0 ? hit.Name : hit.Email };
            }

            var pool = type == "company" ? directory.Companies : directory.Roles;
            var byId = pool.Where(x => AccProjectDirectory.Eq(x.Id, value)).ToList();
            var matches = byId.Count > 0 ? byId : pool.Where(x => AccProjectDirectory.Eq(x.Name, value)).ToList();
            if (matches.Count == 0)
                return Refuse($"no {type} in this project has the {(idLike ? "id" : "name")} '{value}'" +
                              (pool.Count > 0 ? $". Known: {string.Join(", ", pool.Take(10).Select(x => x.Name))}" : $" (no {type} was found on any member)"), type);
            if (matches.Count > 1)
                return Refuse($"'{value}' matches {matches.Count} {type}s ({string.Join(", ", matches.Select(m => m.Id))}) — ambiguous; configure the id", type);
            return new AccAssigneeResolution { Ok = true, Id = matches[0].Id, Type = type, DisplayName = matches[0].Name };
        }

        private static AccAssigneeResolution Refuse(string why, string type)
            => new AccAssigneeResolution { Ok = false, Type = type, Reason = why };

        /// <summary>Read the members (cached) and resolve in one call.</summary>
        public static async Task<AccAssigneeResolution> ResolveAsync(AccCredentials creds, string value, string type)
        {
            var dir = await GetDirectoryAsync(creds).ConfigureAwait(false);
            return dir.Succeeded ? Resolve(value, type, dir.Value) : Resolve(value, type, null, dir.Detail);
        }
    }
}
