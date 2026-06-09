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
//  Server-canonical Project Members + Meeting Recordings + per-meeting invite.
//
//  These back the BCC (WPF) MEETINGS + PLATFORM tabs. The SERVER's project
//  members are the single source of truth — Access Management and Meeting
//  Attendees both read the same /api/projects/{id}/members list. There is no
//  longer a local team_members.json source of record.
//
//  Routes (all already exist server-side):
//    GET  /api/projects/{projectId}/members
//    GET  /api/projects/{projectId}/recordings
//    POST /api/projects/{projectId}/meetings/{meetingId}/invite
// ─────────────────────────────────────────────────────────────────────────────
public sealed partial class PlanscapeServerClient
{
    /// <summary>Canonical project member as returned by ProjectMembersController.GetMembers.
    /// Newtonsoft binds the camelCase API keys case-insensitively.</summary>
    public sealed class ProjectMemberDto
    {
        [JsonProperty("id")]           public Guid    Id           { get; set; } // ProjectMember row id
        [JsonProperty("userId")]       public Guid    UserId       { get; set; } // AppUser id — used for meeting-invite userIds[]
        [JsonProperty("email")]        public string? Email        { get; set; }
        [JsonProperty("displayName")]  public string? DisplayName  { get; set; }
        [JsonProperty("projectRole")]  public string? ProjectRole  { get; set; }
        [JsonProperty("iso19650Role")] public string? Iso19650Role { get; set; }
    }

    /// <summary>One recording row from MeetingsController.GetProjectRecordings (with presigned MinIO URL).</summary>
    public sealed class RecordingDto
    {
        [JsonProperty("id")]             public Guid     Id              { get; set; }
        [JsonProperty("meetingId")]      public Guid?    MeetingId       { get; set; }
        [JsonProperty("kind")]           public string?  Kind            { get; set; }
        [JsonProperty("status")]         public string?  Status          { get; set; }
        [JsonProperty("fileName")]       public string?  FileName        { get; set; }
        [JsonProperty("fileSizeBytes")]  public long?    FileSizeBytes   { get; set; }
        [JsonProperty("durationSeconds")]public double?  DurationSeconds { get; set; }
        [JsonProperty("startedAt")]      public DateTime? StartedAt      { get; set; }
        [JsonProperty("endedAt")]        public DateTime? EndedAt        { get; set; }
        [JsonProperty("meetingTitle")]   public string?  MeetingTitle    { get; set; }
        [JsonProperty("adHoc")]          public bool     AdHoc           { get; set; }
        [JsonProperty("label")]          public string?  Label           { get; set; }
        [JsonProperty("downloadUrl")]    public string?  DownloadUrl     { get; set; } // presigned MinIO GET (6 h)
    }

    /// <summary>Outcome of a per-meeting invite. <see cref="Reachable"/> distinguishes a
    /// transport failure (server down) from a server-side rejection (Ok=false).</summary>
    public sealed class MeetingInviteResult
    {
        public bool   Reachable      { get; set; }
        public bool   Ok             { get; set; }
        public int    Count          { get; set; }
        public int    EmailsSent     { get; set; }
        public bool   EmailConfigured{ get; set; }
        public bool   PushConfigured { get; set; }
        public string DeepLink       { get; set; } = "";
        public string WebUrl         { get; set; } = "";
        public string Message        { get; set; } = "";
    }

    /// <summary>GET the canonical project members. Empty list on any failure (LastError set).</summary>
    public async Task<List<ProjectMemberDto>> GetProjectMembersAsync(Guid projectId)
    {
        var list = new List<ProjectMemberDto>();
        if (projectId == Guid.Empty) { LastError = "No project linked."; return list; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return list;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/members").ConfigureAwait(false);
            if (!resp.ok) { LastError = resp.body; return list; }
            var parsed = JsonConvert.DeserializeObject<List<ProjectMemberDto>>(resp.body);
            if (parsed != null) list = parsed;
        }
        catch (Exception ex) { LastError = ex.Message; }
        return list;
    }

    /// <summary>GET every recording for the project (newest first), each with a presigned
    /// downloadUrl when a stored file exists. Empty on failure.</summary>
    public async Task<List<RecordingDto>> GetProjectRecordingsAsync(Guid projectId)
    {
        var list = new List<RecordingDto>();
        if (projectId == Guid.Empty) { LastError = "No project linked."; return list; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false)) return list;
        try
        {
            var resp = await GetAsync($"/api/projects/{projectId}/recordings").ConfigureAwait(false);
            if (!resp.ok) { LastError = resp.body; return list; }
            var arr = JObject.Parse(resp.body)["recordings"] as JArray;
            if (arr != null)
                list = arr.ToObject<List<RecordingDto>>() ?? list;
        }
        catch (Exception ex) { LastError = ex.Message; }
        return list;
    }

    /// <summary>POST a per-meeting invite to genuine project members (by userId). The server
    /// fans out in-app (SignalR) + push (FCM if configured) + email (when sendEmail and SMTP/Resend
    /// configured), and returns the tap-to-join deep link + web fallback URL.</summary>
    public async Task<MeetingInviteResult> InviteToMeetingAsync(
        Guid projectId, Guid meetingId, IEnumerable<Guid> userIds, string? message, bool sendEmail)
    {
        var r = new MeetingInviteResult();
        if (projectId == Guid.Empty || meetingId == Guid.Empty)
        { r.Message = "No linked project / meeting."; return r; }
        if (!await EnsureAuthenticatedAsync().ConfigureAwait(false))
        { r.Message = LastError ?? "Not connected to Planscape server."; return r; }

        var ids = (userIds ?? Enumerable.Empty<Guid>()).Where(g => g != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) { r.Reachable = true; r.Ok = false; r.Message = "No project-member invitees selected."; return r; }

        try
        {
            var resp = await PostJsonAsync(
                $"/api/projects/{projectId}/meetings/{meetingId}/invite",
                new { userIds = ids, message, sendEmail }).ConfigureAwait(false);

            r.Reachable = true;
            if (!resp.ok) { r.Ok = false; r.Message = resp.body; return r; }

            var o = JObject.Parse(resp.body);
            r.Ok              = true;
            r.Count           = o["count"]?.Value<int>() ?? ids.Length;
            r.EmailsSent      = o["emailsSent"]?.Value<int>() ?? 0;
            r.EmailConfigured = o["emailConfigured"]?.Value<bool>() ?? false;
            r.PushConfigured  = o["pushConfigured"]?.Value<bool>() ?? false;
            r.DeepLink        = o["deepLink"]?.Value<string>() ?? "";
            r.WebUrl          = o["webUrl"]?.Value<string>() ?? "";
            return r;
        }
        catch (Exception ex) { r.Reachable = false; r.Message = ex.Message; return r; }
    }
}
