namespace Planscape.Infrastructure.SignalR;

/// <summary>
/// G2 (two-firm tenancy) — the single source of truth for a LiveKit room name.
///
/// The room used to be the bare <c>sessionId.ToString()</c>, so with one LiveKit
/// project serving several firms every tenant's rooms sat in one flat namespace and
/// one dashboard/analytics view. A session GUID is unguessable and
/// <c>MeetingRoomController.LiveKitToken</c> gates minting on
/// <c>ProjectInTenant</c>, so that was defence-in-depth rather than a hole — but
/// tenant-scoping the name costs nothing and turns "give this firm its own LiveKit
/// project" from a migration into a rename.
///
/// **This name is load-bearing.** Four features address the same room and must agree
/// exactly or they silently target different rooms: participant tokens
/// (<c>MeetingRoomController</c>), egress recording (<c>LiveKitEgressClient</c>), and
/// the moderation admin calls (<c>LiveKitRoomService</c> — mute/remove). Always build
/// it here; never re-derive it inline.
///
/// Format: <c>t{tenantId:N}-{sessionId:N}</c>, e.g.
/// <c>t3fa85f6457174562b3fc2c963f66afa6-9c1e7d0b4a1f4e2b8c3d5f6a7b8c9d0e</c>.
/// The findings doc wrote this as <c>t{tenantId}-{sessionId}</c>; dashless ("N") GUIDs
/// are used so the single hyphen unambiguously separates the two halves — with dashed
/// GUIDs a reader (or <see cref="TryParse"/>) cannot tell where the tenant ends. 66
/// characters, well inside LiveKit's room-name limit.
///
/// Nothing in the system parses the room name back at runtime (the egress webhook
/// matches on EgressId, and <c>MeetingRecording.StorageKey</c> is stored verbatim), so
/// <see cref="TryParse"/> exists for diagnostics and tests, not for a hot path.
/// </summary>
public static class LiveKitRoom
{
    /// <summary>Build the LiveKit room name for a session in a tenant.</summary>
    public static string Name(Guid tenantId, Guid sessionId) => $"t{tenantId:N}-{sessionId:N}";

    /// <summary>Inverse of <see cref="Name"/>. False for anything not in that exact shape —
    /// including a legacy bare-session-GUID room name from before G2.</summary>
    public static bool TryParse(string? room, out Guid tenantId, out Guid sessionId)
    {
        tenantId = Guid.Empty;
        sessionId = Guid.Empty;
        // 66 = 't' + 32 tenant + '-' + 32 session.
        if (string.IsNullOrEmpty(room) || room.Length != 66 || room[0] != 't') return false;
        var dash = room.IndexOf('-');
        if (dash != 33) return false;
        return Guid.TryParseExact(room.Substring(1, 32), "N", out tenantId)
            && Guid.TryParseExact(room.Substring(34), "N", out sessionId);
    }

    /// <summary>
    /// G1 — the object-storage key an egress recording is written to.
    /// Every other stored file in the system lands under <c>t_{tenantId}/…</c>
    /// (<c>LocalFileStorageService.SaveScopedAsync</c>); recordings landed at the bucket
    /// root keyed only by session GUID, so two firms' recordings interleaved in one flat
    /// namespace. Reads were still authorised through <c>MeetingRecording</c> (which IS
    /// <c>ITenantScoped</c>) so this was never an active leak — but it defeated
    /// bucket-level policy, per-tenant lifecycle/retention rules, and "delete this firm's
    /// data" as one operation.
    ///
    /// Note the session GUID, not <see cref="Name"/>, forms the second segment: the room
    /// name already embeds the tenant, so using it here would render
    /// <c>t_{tenant}/t{tenant}-{session}/…</c>. This keeps the historical
    /// <c>{sessionId}/{timestamp}.{ext}</c> shape intact underneath the new tenant prefix,
    /// which is what the existing docs and any operator muscle-memory describe.
    /// </summary>
    public static string RecordingKey(Guid tenantId, Guid sessionId, DateTime utcNow, string ext)
        => $"t_{tenantId}/{sessionId}/{utcNow:yyyyMMddHHmmss}.{ext}";
}
