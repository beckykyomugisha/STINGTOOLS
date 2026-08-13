using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// Phase B (G1 + G2) — two-firm tenancy for the meeting media plane.
///
/// The room name is load-bearing: participant tokens (MeetingRoomController), egress
/// recording (LiveKitEgressClient) and the moderation admin calls (LiveKitRoomService —
/// mute/remove) all address the SAME room, and a mismatch is silent. Nothing throws;
/// the mute just mutes an empty room, or the egress records one. These tests pin the
/// format so a well-meaning tidy-up can't drift one call site away from the others.
/// </summary>
public class LiveKitRoomTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
    private static readonly Guid TenantB = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Session = Guid.Parse("9c1e7d0b-4a1f-4e2b-8c3d-5f6a7b8c9d0e");

    // ── G2: room naming ───────────────────────────────────────────────────────

    [Fact]
    public void Room_name_is_tenant_scoped_and_stable()
    {
        Assert.Equal("t3fa85f6457174562b3fc2c963f66afa6-9c1e7d0b4a1f4e2b8c3d5f6a7b8c9d0e",
                     LiveKitRoom.Name(TenantA, Session));
        // Stable across calls — it is derived, never generated.
        Assert.Equal(LiveKitRoom.Name(TenantA, Session), LiveKitRoom.Name(TenantA, Session));
    }

    [Fact]
    public void Two_firms_sharing_a_session_guid_get_different_rooms()
    {
        // The whole point of G2: one LiveKit project, two firms, no shared namespace.
        Assert.NotEqual(LiveKitRoom.Name(TenantA, Session), LiveKitRoom.Name(TenantB, Session));
    }

    [Fact]
    public void Room_name_no_longer_equals_the_bare_session_guid()
    {
        // Guards the exact regression this change is about — a call site left on
        // sessionId.ToString() would silently address a different room.
        var room = LiveKitRoom.Name(TenantA, Session);
        Assert.NotEqual(Session.ToString(), room);
        Assert.DoesNotContain(Session.ToString(), room);   // dashless, so not a substring either
    }

    [Fact]
    public void Room_name_has_exactly_one_separator_so_the_halves_are_unambiguous()
    {
        var room = LiveKitRoom.Name(TenantA, Session);
        Assert.Equal(1, room.Count(c => c == '-'));
        Assert.StartsWith("t", room);
        Assert.Equal(66, room.Length);          // 't' + 32 + '-' + 32, inside LiveKit's limit
        Assert.DoesNotContain(" ", room);       // room names travel in JSON + JWT claims
    }

    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6", "9c1e7d0b-4a1f-4e2b-8c3d-5f6a7b8c9d0e")]
    [InlineData("00000000-0000-0000-0000-000000000001", "ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void Room_name_round_trips(string tenant, string session)
    {
        var t = Guid.Parse(tenant);
        var s = Guid.Parse(session);
        Assert.True(LiveKitRoom.TryParse(LiveKitRoom.Name(t, s), out var pt, out var ps));
        Assert.Equal(t, pt);
        Assert.Equal(s, ps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9c1e7d0b-4a1f-4e2b-8c3d-5f6a7b8c9d0e")]                       // legacy pre-G2 room
    [InlineData("t3fa85f6457174562b3fc2c963f66afa6")]                          // tenant only
    [InlineData("x3fa85f6457174562b3fc2c963f66afa6-9c1e7d0b4a1f4e2b8c3d5f6a7b8c9d0e")]  // wrong prefix
    [InlineData("t3fa85f6457174562b3fc2c963f66afa6-notaguid00000000000000000000000")]   // bad half
    public void TryParse_rejects_anything_not_in_the_exact_shape(string? room)
    {
        Assert.False(LiveKitRoom.TryParse(room, out _, out _));
    }

    // ── G1: recording storage key ─────────────────────────────────────────────

    [Fact]
    public void Recording_key_is_tenant_prefixed_like_every_other_stored_file()
    {
        var key = LiveKitRoom.RecordingKey(TenantA, Session, new DateTime(2026, 7, 31, 13, 33, 10, DateTimeKind.Utc), "mp4");
        Assert.Equal($"t_{TenantA}/{Session}/20260731133310.mp4", key);
        Assert.StartsWith($"t_{TenantA}/", key);   // matches LocalFileStorageService's convention
    }

    [Fact]
    public void Recording_key_keeps_the_historical_session_slash_timestamp_shape()
    {
        // Only a prefix was added; the documented `{sessionId}/{yyyyMMddHHmmss}.{ext}`
        // tail is intact, so operator muscle-memory and the N2 write-up still hold.
        var key = LiveKitRoom.RecordingKey(TenantA, Session, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), "ogg");
        Assert.EndsWith($"{Session}/20260102030405.ogg", key);
        Assert.Equal(2, key.Count(c => c == '/'));   // exactly tenant/session/file
    }

    [Fact]
    public void Recording_key_does_not_double_embed_the_tenant()
    {
        // The room name already carries the tenant; feeding it in as the middle segment
        // would render t_{tenant}/t{tenant}-{session}/… . The key takes the session GUID.
        var key = LiveKitRoom.RecordingKey(TenantA, Session, DateTime.UtcNow, "mp4");
        Assert.DoesNotContain(LiveKitRoom.Name(TenantA, Session), key);
        Assert.Equal(1, CountOccurrences(key, TenantA.ToString()));
    }

    [Fact]
    public void Two_firms_recordings_no_longer_share_a_flat_namespace()
    {
        var now = new DateTime(2026, 7, 31, 13, 33, 10, DateTimeKind.Utc);
        var a = LiveKitRoom.RecordingKey(TenantA, Session, now, "mp4");
        var b = LiveKitRoom.RecordingKey(TenantB, Session, now, "mp4");
        Assert.NotEqual(a, b);
        // Same session GUID + same second used to collide at the bucket root; now they
        // sit under different tenant prefixes, so bucket-level policy and per-tenant
        // retention/erasure become one operation.
        Assert.NotEqual(a.Split('/')[0], b.Split('/')[0]);
    }

    [Fact]
    public void Recording_key_is_relative_to_the_bucket()
    {
        // Path-style URLs already include the bucket; a leading slash or a "recordings/"
        // prefix here yields /recordings/recordings/… (the bug called out in the client).
        var key = LiveKitRoom.RecordingKey(TenantA, Session, DateTime.UtcNow, "mp4");
        Assert.False(key.StartsWith('/'));
        Assert.DoesNotContain("recordings/", key);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
