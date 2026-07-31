using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// Document sync — the push half of the local-disk sync pipeline described in
/// <c>docs/superpowers/specs/2026-07-31-document-sync-design.md</c>.
///
/// Tells every Planscape Companion connected for a project that a document
/// changed, the moment a coordinator transitions its CDE state or mints a new
/// revision. Polling exists only as the reconnect fallback
/// (<c>GET /api/projects/{id}/documents/changed-since</c>) — this hub is the
/// normal path.
///
/// <para><b>Why a separate hub rather than reusing NotificationHub.</b> That hub
/// already carries a <c>DocumentUpdated</c> event, but scoped to
/// <c>project-{id}-cde-{state}</c> groups a web client joins per CDE state it is
/// looking at. A Companion does not have a "state it is looking at" — it wants
/// everything the user is allowed to see, for the whole session, across the
/// project. Bending the existing groups to that shape would make a UI-driven
/// subscription model serve a background daemon, and both would get worse.</para>
///
/// <para><b>The payload is deliberately a notification, not the data.</b> It says
/// "something about document X changed" and nothing about file contents or
/// permissions. The Companion answers by calling <c>changed-since</c>, which
/// re-runs the caller's own ACL server-side. That way a push can never be the
/// thing that widens what a client sees — the hub group is scoped per project,
/// but per-user CDE/discipline/suitability narrowing lives in one place
/// (<c>ProjectMemberAcl</c>) and stays there.</para>
///
/// Client usage (Planscape.Companion):
///   conn.On&lt;object&gt;("DocumentChanged", _ =&gt; queue.RequestSync(projectId));
///   await conn.InvokeAsync("JoinProject", projectId);
/// </summary>
[Authorize]
public class DocumentSyncHub : Hub
{
    private const string AuthKey = "docsync_projects";
    private readonly PlanscapeDbContext _db;

    public DocumentSyncHub(PlanscapeDbContext db) => _db = db;

    internal static string Group(Guid projectId) => $"docsync:{projectId}";

    /// <summary>
    /// Projects this connection passed the tenant check for. Cached per-connection
    /// so a reconnect storm doesn't become a DB hit per message — same reasoning
    /// as <see cref="MeetingHub"/>'s authorised-session set.
    /// </summary>
    private HashSet<Guid> Authorized =>
        Context.Items.TryGetValue(AuthKey, out var v) && v is HashSet<Guid> set
            ? set
            : (HashSet<Guid>)(Context.Items[AuthKey] = new HashSet<Guid>());

    /// <summary>
    /// Subscribe to a project's document changes.
    ///
    /// Gated by <see cref="HubTenantGuard.OwnsProjectAsync"/> for the reason that
    /// guard exists: a hub connection has no HttpContext, so the DbContext tenant
    /// query filter resolves to an empty TenantId and cannot be relied on. Without
    /// the explicit claims check, any authenticated user could join another firm's
    /// project group by guessing a GUID and receive its document traffic.
    ///
    /// Returns silently on refusal rather than throwing — a hub exception
    /// surfaces to the client as an opaque transport error, and there is nothing
    /// the caller can usefully do about "that project is not yours". The caller
    /// simply receives no events.
    /// </summary>
    public async Task JoinProject(string projectId)
    {
        if (!Guid.TryParse(projectId, out var pid)) return;
        if (!await HubTenantGuard.OwnsProjectAsync(Context.User, _db, pid)) return;

        Authorized.Add(pid);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(pid));
    }

    /// <summary>
    /// Unsubscribe. Deliberately NOT tenant-gated: leaving a group you are not in
    /// is a no-op, and refusing to let someone leave is not a security property.
    /// </summary>
    public async Task LeaveProject(string projectId)
    {
        if (!Guid.TryParse(projectId, out var pid)) return;
        Authorized.Remove(pid);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(pid));
    }

    /// <summary>
    /// Liveness probe for the Companion's reconnect logic. Returns the server's
    /// UTC clock so a client can detect meaningful drift before it uses its own
    /// clock to build a <c>changed-since</c> query — a Companion whose clock runs
    /// fast would otherwise ask for changes "since" a future instant and silently
    /// receive nothing.
    /// </summary>
    public Task<DateTime> ServerTimeUtc() => Task.FromResult(DateTime.UtcNow);

    /// <summary>
    /// Server-side push. Called from the controllers at the two points a document
    /// actually changes: a CDE transition (<c>DocumentsController</c>) and a minted
    /// revision (<c>DocumentRevisionsController</c>).
    ///
    /// Static + <see cref="IHubContext{THub}"/> because the caller is a controller,
    /// not a hub connection — the same shape as
    /// <see cref="MeetingHub.NotifyRoomChanged"/>.
    /// </summary>
    public static Task NotifyDocumentChanged(
        IHubContext<DocumentSyncHub> hub, Guid projectId, object payload)
        => hub.Clients.Group(Group(projectId)).SendAsync("DocumentChanged", payload);

    /// <summary>
    /// The <c>DocumentChanged</c> payload. A notification, not the document — see
    /// the class remarks. <paramref name="kind"/> is <c>cde_transition</c> or
    /// <c>revision</c>; the Companion treats both identically today and the field
    /// exists so a log line can say which happened.
    /// </summary>
    public static object Payload(Guid projectId, Guid documentId, string kind, string? cdeStatus = null)
        => new
        {
            projectId,
            documentId,
            kind,
            cdeStatus,
            changedAtUtc = DateTime.UtcNow,
        };
}
