#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.BIMManager;

// ─────────────────────────────────────────────────────────────────────────────
//  Distribution groups — server-canonical, and the ONLY file that owns them.
//
//  These calls existed only as no-op stubs in Core/MergeRecoveryStubs.cs (List
//  returned an empty list, Create returned false), so the BCC editor in
//  SitePhotosAdminSubTab looked functional but could neither read nor write a
//  group: "No distribution groups yet" was hard-coded behaviour, not a fact
//  about the project. The server side has been real all along —
//  DistributionGroupsController plus the DistributionGroup /
//  DistributionGroupMember entities — so this is wiring, not new capability.
//
//  WHY EVERYTHING LIVES HERE. #550 landed its own implementation of List and
//  Create inside PlanscapeServerClient.SitePhotos.cs. Both files extend the same
//  partial class, so two implementations of the same members is a duplicate-member
//  compile error that no text merge can settle. They are consolidated here, beside
//  the member-management calls that exist only here, so one file owns the surface.
//
//  THE CONTRACT, which is the substantive half of that consolidation:
//  a nullable return means NULL IS FAILURE and LastError is set; an empty
//  collection means the server answered and there is genuinely nothing there.
//  #550 established that rule for the albums path and the owner verified it in
//  Revit (M1: a failed list shows a visible error, not an empty list). Every read
//  below follows it. Returning `new List<T>()` on failure — which an earlier draft
//  of this file did for three of these calls — reintroduces exactly the ambiguity
//  #550 removed, in a different pane.
//
//  Create returns the created group rather than a bool. The caller already wrote
//  `if (grp == null)` to detect failure; against a bool that comparison is always
//  false, so the error path was unreachable and a failed create looked like a
//  success. CS0472 was suppressed when that shipped; #589 has since unsuppressed
//  it, and main emits exactly one CS0472 — at that call site. This change clears it.
//
//  Routes (all already exist server-side):
//    GET    /api/projects/{projectId}/distribution-groups
//    POST   /api/projects/{projectId}/distribution-groups
//    GET    /api/projects/{projectId}/distribution-groups/{groupId}
//    POST   /api/projects/{projectId}/distribution-groups/{groupId}/members
//    DELETE /api/projects/{projectId}/distribution-groups/{groupId}/members/{memberId}
//
//  Create/AddMember/Delete are curator-only server-side (PM/Admin/Owner) and
//  answer 403; callers surface LastError rather than failing silently.
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class PlanscapeServerClient
{
    /// <summary>One distribution group as returned by DistributionGroupsController.</summary>
    public sealed class DistributionGroupDto
    {
        [JsonProperty("id")]                   public Guid   Id   { get; set; }
        [JsonProperty("projectId")]            public Guid   ProjectId { get; set; }
        [JsonProperty("name")]                 public string Name { get; set; } = "";
        [JsonProperty("description")]          public string? Description { get; set; }
        [JsonProperty("kind")]                 public string? Kind { get; set; }   // Client | Internal | Mixed
        [JsonProperty("memberCount")]          public int    MemberCount { get; set; }
        [JsonProperty("includeInDailyDigest")] public bool   IncludeInDailyDigest { get; set; }
        [JsonProperty("forceRedacted")]        public bool   ForceRedacted { get; set; }
    }

    /// <summary>One row in a group. Exactly one of UserId (a real project member) or
    /// ExternalEmail (someone outside the project) is set — the server rejects a
    /// member with neither.</summary>
    public sealed class DistributionGroupMemberDto
    {
        [JsonProperty("id")]               public Guid    Id { get; set; }
        [JsonProperty("userId")]           public Guid?   UserId { get; set; }
        [JsonProperty("externalEmail")]    public string? ExternalEmail { get; set; }
        [JsonProperty("display")]          public string? Display { get; set; }
        [JsonProperty("email")]            public string? Email { get; set; }
        [JsonProperty("disciplineFilter")] public string? DisciplineFilter { get; set; }

        public bool IsProjectMember => UserId.HasValue && UserId.Value != Guid.Empty;
        public string Label => Display ?? Email ?? ExternalEmail ?? "(unnamed)";
    }

    /// <summary>DistributionGroup.ValidKinds — kept in step with the entity.</summary>
    private static readonly string[] ValidDistributionKinds =
        { "Client", "Internal", "Mixed" };

    /// <summary>
    /// <c>GET /api/projects/{projectId}/distribution-groups</c>.
    /// <b>Null means the load FAILED</b> (LastError set); an empty list means the
    /// project genuinely has no groups. Callers must render those differently.
    /// </summary>
    public async Task<List<DistributionGroupDto>?> ListDistributionGroupsAsync(Guid projectId)
    {
        if (projectId == Guid.Empty) { LastError = "No project linked."; return null; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) { LastError ??= "Not connected."; return null; }
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/distribution-groups").ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = $"Distribution group load failed ({resp.status}): {Trim(resp.body)}";
                return null;
            }
            var parsed = JsonConvert.DeserializeObject<List<DistributionGroupDto>>(resp.body);
            if (parsed == null)
            {
                // A 200 whose body will not parse is a failure, not "no groups".
                LastError = "Distribution group load returned an unreadable body.";
                return null;
            }
            LastError = null;
            return parsed;
        }
        catch (Exception ex)
        {
            LastError = $"Distribution group load failed: {ex.Message}";
            StingLog.Error("ListDistributionGroupsAsync failed", ex);
            return null;
        }
    }

    /// <summary>
    /// <c>POST /api/projects/{projectId}/distribution-groups</c>, then one
    /// <c>POST {groupId}/members</c> per recipient — members are a separate route,
    /// not a field on create.
    /// <para>Returns the created group, or <b>null on failure</b> with LastError set
    /// (409 when the name is in use, 403 when the caller is not a curator).</para>
    /// <para>Partial success is deliberately NOT null: if the group was created but
    /// some recipients could not be added, the group exists, so the DTO is returned
    /// AND LastError names the ones that failed. Callers must therefore check
    /// LastError even on a non-null result. The alternatives were worse — returning
    /// null loses a group that really was created, and clearing LastError claims a
    /// clean create that was not one.</para>
    /// </summary>
    public async Task<DistributionGroupDto?> CreateDistributionGroupAsync(
        Guid projectId,
        string name,
        IEnumerable<string>? recipients = null,
        string? kind = null,
        string? description = null,
        bool? includeInDailyDigest = null,
        bool? forceRedacted = null)
    {
        if (projectId == Guid.Empty) { LastError = "No project linked."; return null; }
        if (string.IsNullOrWhiteSpace(name)) { LastError = "Group name is required."; return null; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) { LastError ??= "Not connected."; return null; }

        var groupKind = string.IsNullOrWhiteSpace(kind) ? "Internal" : kind!;
        if (!ValidDistributionKinds.Contains(groupKind))
        {
            LastError = $"Invalid distribution group kind '{groupKind}'. "
                      + $"Allowed: {string.Join(", ", ValidDistributionKinds)}.";
            return null;
        }

        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/distribution-groups", new
            {
                name = name.Trim(),
                description,
                kind = groupKind,
                includeInDailyDigest,
                forceRedacted,
            }).ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = resp.status == 409
                    ? $"A distribution group named '{name.Trim()}' already exists."
                    : resp.status == 403
                        ? "You need PM, Admin or Owner role on this project to manage distribution groups."
                        : $"Distribution group create failed ({resp.status}): {Trim(resp.body)}";
                return null;
            }

            var created = JsonConvert.DeserializeObject<DistributionGroupDto>(resp.body);
            if (created == null)
            {
                LastError = "Distribution group create returned an unreadable body.";
                return null;
            }

            var emails = recipients?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToArray()
                         ?? Array.Empty<string>();
            if (created.Id == Guid.Empty || emails.Length == 0) { LastError = null; return created; }

            var failed = new List<string>();
            foreach (var email in emails)
            {
                var m = await PostJsonAsync(
                    $"/api/projects/{projectId}/distribution-groups/{created.Id}/members",
                    new { externalEmail = email }).ConfigureAwait(false);
                if (!m.ok) failed.Add(email);
            }

            LastError = failed.Count == 0
                ? null
                : $"Group '{created.Name}' was created, but {failed.Count} of {emails.Length} "
                  + $"recipients could not be added: {string.Join(", ", failed.Take(5))}"
                  + (failed.Count > 5 ? ", …" : "");
            return created;
        }
        catch (Exception ex)
        {
            LastError = $"Distribution group create failed: {ex.Message}";
            StingLog.Error("CreateDistributionGroupAsync failed", ex);
            return null;
        }
    }

    /// <summary>
    /// <c>GET .../distribution-groups/{groupId}</c>, members half only.
    /// <b>Null means the load FAILED</b> (LastError set); an empty list means the
    /// group genuinely has no members.
    /// </summary>
    public async Task<List<DistributionGroupMemberDto>?> ListDistributionGroupMembersAsync(
        Guid projectId, Guid groupId)
    {
        if (projectId == Guid.Empty || groupId == Guid.Empty) { LastError = "No project/group."; return null; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) { LastError ??= "Not connected."; return null; }
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/distribution-groups/{groupId}")
                .ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = $"Group member load failed ({resp.status}): {Trim(resp.body)}";
                return null;
            }
            // GetOne answers { group, members } — we only want the members half.
            var members = JObject.Parse(resp.body)["members"] as JArray;
            if (members == null)
            {
                // An absent "members" key is a shape we do not understand, not an
                // empty group.
                LastError = "Group member load returned an unreadable body.";
                return null;
            }
            LastError = null;
            return members.ToObject<List<DistributionGroupMemberDto>>()
                   ?? new List<DistributionGroupMemberDto>();
        }
        catch (Exception ex)
        {
            LastError = $"Group member load failed: {ex.Message}";
            StingLog.Error("ListDistributionGroupMembersAsync failed", ex);
            return null;
        }
    }

    /// <summary>
    /// POST a member into a group. Pass <paramref name="userId"/> for a real project
    /// member; pass <paramref name="externalEmail"/> for someone outside the project.
    /// The server rejects a call with neither. False means it failed (LastError set) —
    /// a mutation has no "empty" state to be confused with.
    /// </summary>
    public async Task<bool> AddDistributionGroupMemberAsync(
        Guid projectId, Guid groupId,
        Guid? userId = null,
        string? externalEmail = null,
        string? displayName = null,
        string? disciplineFilter = null)
    {
        if (projectId == Guid.Empty || groupId == Guid.Empty) { LastError = "No project/group."; return false; }
        if (!userId.HasValue && string.IsNullOrWhiteSpace(externalEmail))
        {
            LastError = "Pick a project member, or give an external email address.";
            return false;
        }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) { LastError ??= "Not connected."; return false; }
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/distribution-groups/{groupId}/members", new
                {
                    userId,
                    externalEmail = string.IsNullOrWhiteSpace(externalEmail) ? null : externalEmail!.Trim(),
                    displayName,
                    disciplineFilter,
                }).ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = resp.status == 403
                    ? "You need PM, Admin or Owner role on this project to manage distribution groups."
                    : $"Add group member failed ({resp.status}): {Trim(resp.body)}";
                return false;
            }
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Add group member failed: {ex.Message}";
            StingLog.Error("AddDistributionGroupMemberAsync failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The recipient labels behind a named group, for callers that still work in flat
    /// recipient strings (the transmittal dialog).
    /// <para><b>Null means it could not be resolved</b> — the group list failed, the
    /// named group does not exist, or its members failed to load — with LastError
    /// saying which. An empty list means the group exists and has no members. Callers
    /// must not silently substitute a local list; that substitution is the drift this
    /// replaced.</para>
    /// </summary>
    public async Task<List<string>?> ResolveDistributionGroupRecipientsAsync(Guid projectId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) { LastError = "No group name given."; return null; }

        var groups = await ListDistributionGroupsAsync(projectId).ConfigureAwait(false);
        if (groups == null) return null;   // LastError already set by the list call

        var grp = groups.FirstOrDefault(g =>
            string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (grp == null) { LastError = $"No distribution group named \"{groupName}\"."; return null; }

        var members = await ListDistributionGroupMembersAsync(projectId, grp.Id).ConfigureAwait(false);
        if (members == null) return null;  // LastError already set

        var outp = new List<string>();
        foreach (var m in members)
        {
            string label = m.Display ?? m.Email ?? m.ExternalEmail ?? "";
            if (!string.IsNullOrWhiteSpace(label)) outp.Add(label.Trim());
        }
        LastError = null;
        return outp.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
