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
//  Distribution groups — server-canonical.
//
//  These two methods existed only as no-op stubs in Core/MergeRecoveryStubs.cs
//  (List returned an empty list, Create returned false), so the BCC editor in
//  SitePhotosAdminSubTab looked functional but could neither read nor write a
//  group: "No distribution groups yet" was hard-coded behaviour, not a fact
//  about the project. The server side has been real all along —
//  DistributionGroupsController plus the DistributionGroup /
//  DistributionGroupMember entities — so this is wiring, not new capability.
//
//  Create now returns the created group rather than a bool. The caller already
//  wrote `if (grp == null)` to detect failure; against a bool that comparison is
//  always false, and CS0472 is in the project's NoWarn list, so the error path
//  was unreachable and a failed create looked like a success.
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

    /// <summary>One row in a group. Exactly one of <see cref="UserId"/> (a real
    /// project member) or <see cref="ExternalEmail"/> (someone outside the
    /// project) is set — the server rejects a member with neither.</summary>
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

    /// <summary>GET the project's distribution groups. Empty list on any failure
    /// (LastError set) — never null, because callers read .Count directly.</summary>
    public async Task<List<DistributionGroupDto>> ListDistributionGroupsAsync(Guid projectId)
    {
        var list = new List<DistributionGroupDto>();
        if (projectId == Guid.Empty) { LastError = "No project linked."; return list; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return list;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/distribution-groups").ConfigureAwait(false);
            if (!resp.ok) { LastError = resp.body; return list; }
            var parsed = JsonConvert.DeserializeObject<List<DistributionGroupDto>>(resp.body);
            if (parsed != null) list = parsed;
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Warn($"ListDistributionGroups: {ex.Message}"); }
        return list;
    }

    /// <summary>POST a new distribution group. Returns the created group, or null
    /// on failure with LastError set (409 when the name is already in use, 403
    /// when the caller is not a curator).</summary>
    public async Task<DistributionGroupDto?> CreateDistributionGroupAsync(
        Guid projectId,
        string name,
        string? description = null,
        string? kind = null,
        bool? includeInDailyDigest = null,
        bool? forceRedacted = null)
    {
        if (projectId == Guid.Empty) { LastError = "No project linked."; return null; }
        if (string.IsNullOrWhiteSpace(name)) { LastError = "Group name is required."; return null; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return null;
        try
        {
            var resp = await PostJsonAsync($"/api/projects/{projectId}/distribution-groups", new
            {
                name = name.Trim(),
                description,
                kind = kind ?? "Internal",
                includeInDailyDigest,
                forceRedacted,
            }).ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = resp.status == 409
                    ? $"A group named \"{name.Trim()}\" already exists."
                    : resp.status == 403
                        ? "You need PM, Admin or Owner role on this project to manage distribution groups."
                        : resp.body;
                return null;
            }
            return JsonConvert.DeserializeObject<DistributionGroupDto>(resp.body);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StingLog.Warn($"CreateDistributionGroup: {ex.Message}");
            return null;
        }
    }

    /// <summary>GET one group's members. Empty list on failure (LastError set).</summary>
    public async Task<List<DistributionGroupMemberDto>> ListDistributionGroupMembersAsync(Guid projectId, Guid groupId)
    {
        var list = new List<DistributionGroupMemberDto>();
        if (projectId == Guid.Empty || groupId == Guid.Empty) { LastError = "No project/group."; return list; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return list;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/distribution-groups/{groupId}").ConfigureAwait(false);
            if (!resp.ok) { LastError = resp.body; return list; }
            // GetOne answers { group, members } — we only want the members half.
            var members = JObject.Parse(resp.body)["members"] as JArray;
            if (members != null)
                list = members.ToObject<List<DistributionGroupMemberDto>>() ?? list;
        }
        catch (Exception ex) { LastError = ex.Message; StingLog.Warn($"ListDistributionGroupMembers: {ex.Message}"); }
        return list;
    }

    /// <summary>
    /// POST a member into a group. Pass <paramref name="userId"/> for a real
    /// project member; pass <paramref name="externalEmail"/> for someone outside
    /// the project. The server rejects a call with neither.
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
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return false;
        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/distribution-groups/{groupId}/members", new
                {
                    userId,
                    externalEmail = string.IsNullOrWhiteSpace(externalEmail) ? null : externalEmail.Trim(),
                    displayName,
                    disciplineFilter,
                }).ConfigureAwait(false);
            if (!resp.ok)
            {
                LastError = resp.status == 403
                    ? "You need PM, Admin or Owner role on this project to manage distribution groups."
                    : resp.body;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StingLog.Warn($"AddDistributionGroupMember: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The recipient names/emails behind a named group, for callers that still
    /// work in flat recipient strings (the transmittal dialog). Empty when the
    /// group is unknown or unreachable — callers must not silently substitute a
    /// local list, which is the drift this replaced.
    /// </summary>
    public async Task<List<string>> ResolveDistributionGroupRecipientsAsync(Guid projectId, string groupName)
    {
        var outp = new List<string>();
        if (string.IsNullOrWhiteSpace(groupName)) return outp;
        var groups = await ListDistributionGroupsAsync(projectId).ConfigureAwait(false);
        var grp = groups.FirstOrDefault(g =>
            string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (grp == null) { LastError = $"No distribution group named \"{groupName}\"."; return outp; }

        var members = await ListDistributionGroupMembersAsync(projectId, grp.Id).ConfigureAwait(false);
        foreach (var m in members)
        {
            string label = m.Display ?? m.Email ?? m.ExternalEmail ?? "";
            if (!string.IsNullOrWhiteSpace(label)) outp.Add(label.Trim());
        }
        return outp.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
