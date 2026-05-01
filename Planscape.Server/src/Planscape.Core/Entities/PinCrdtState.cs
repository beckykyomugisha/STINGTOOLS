namespace Planscape.Core.Entities;

/// <summary>
/// S6.3 — server-side CRDT mailbox for collaborative pin / issue
/// editing. Two coordinators editing the same issue at the same time
/// produces a stream of Yjs (or Automerge) update bytes; the server
/// stores every update and re-broadcasts it through SignalR so other
/// clients converge.
///
/// We persist updates as opaque base64 blobs so the .NET side never
/// needs to understand the CRDT format — we're just a relay + a
/// durable log. A periodic compaction job (S6.3b) merges N updates
/// into one snapshot so the log doesn't grow forever.
/// </summary>
public class PinCrdtUpdate : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>
    /// Document key. For issue-level CRDTs this is "issue:&lt;guid&gt;";
    /// for project-level pin sets, "project:&lt;guid&gt;:pins". Index lives
    /// on this column so the relay can fan out efficiently.
    /// </summary>
    public string DocKey { get; set; } = "";

    /// <summary>Yjs / Automerge update bytes, base64 — opaque.</summary>
    public string UpdateBase64 { get; set; } = "";

    /// <summary>Set when this row is a snapshot (compacted) rather than a single update.</summary>
    public bool IsSnapshot { get; set; }

    public Guid? AuthorUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
