using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// TRACK C5/C6 — the federated geometry-delta endpoint.
///
/// THE DEFECTS
/// -----------
/// • **Non-atomic.** Deletes committed immediately via ExecuteUpdateAsync; the
///   GLB store and SaveChangesAsync came afterwards, with no transaction. A
///   failure in between left the deletions applied and the additions lost —
///   the worst possible partial state, because the server model is then MISSING
///   geometry the source still has, and no retry re-sends the deletions: they
///   are already marked, so the next delta's `!e.IsDeleted` filter excludes them.
/// • **Non-idempotent.** A delta is a replayable write — the plugin now
///   re-queues and re-sends after a failure (C1), and a client that times out
///   mid-upload cannot tell whether the server applied it.
/// • **Silent catches.** A malformed `deletedIds` payload dropped every
///   tombstone while the GLB half still applied, and a malformed GLB produced
///   zero nodes; both answered HTTP 200 and looked like "a delta that changed
///   nothing".
///
/// These run on SQLite — a REAL relational provider, so `BeginTransactionAsync`
/// is a real transaction and the rollback assertion below means something. (EF
/// InMemory raises TransactionIgnoredWarning, which EF escalates to an
/// exception, so it could not exercise this path at all.)
/// </summary>
public class FederatedDeltaReliabilityTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private static PlanscapeDbContext NewContext(SqliteConnection conn, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    // ── doubles ─────────────────────────────────────────────────────────────

    private sealed class MemoryFormFile : IFormFile
    {
        private readonly byte[] _bytes;
        public MemoryFormFile(byte[] bytes, string name) { _bytes = bytes; Name = name; FileName = name; }
        public string ContentType { get; set; } = "application/octet-stream";
        public string ContentDisposition { get; set; } = "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => _bytes.Length;
        public string Name { get; set; }
        public string FileName { get; set; }
        public void CopyTo(Stream target) => target.Write(_bytes, 0, _bytes.Length);
        public Task CopyToAsync(Stream target, CancellationToken ct = default)
            => target.WriteAsync(_bytes, 0, _bytes.Length, ct);
        public Stream OpenReadStream() => new MemoryStream(_bytes);
    }

    /// <summary>Storage that can be told to fail, to force a mid-delta failure.</summary>
    private sealed class StubStorage : IFileStorageService
    {
        private readonly bool _throwOnSave;
        public StubStorage(bool throwOnSave = false) => _throwOnSave = throwOnSave;

        public Task<string> SaveScopedAsync(Guid t, Guid p, string f, Stream c, CancellationToken ct = default)
            => _throwOnSave
                ? throw new IOException("object store unavailable")
                : Task.FromResult($"t_{t:N}/{p:N}/{f}");

        private static Exception No() => new NotSupportedException("not used by these tests");
        public Task<string> SaveAsync(string t, string p, string f, Stream c, CancellationToken ct = default) => throw No();
        public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool b = false) => throw No();
        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool b = false) => throw No();
        public Task<PresignedUpload> GetPresignedPutUrlAsync(string k, string c, TimeSpan v, long m, CancellationToken ct = default) => throw No();
        public Task<string> GetPresignedGetUrlAsync(string k, TimeSpan v, CancellationToken ct = default, bool b = false) => throw No();
        public Task MoveAsync(string s, string d, CancellationToken ct = default, bool b = false) => throw No();
    }

    private sealed class NoopClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopHubClients : IHubClients
    {
        private static readonly NoopClientProxy Proxy = new();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> e) => Proxy;
        public IClientProxy Client(string c) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> c) => Proxy;
        public IClientProxy Group(string g) => Proxy;
        public IClientProxy GroupExcept(string g, IReadOnlyList<string> e) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> g) => Proxy;
        public IClientProxy User(string u) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> u) => Proxy;
    }

    private sealed class NoopHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public IHubClients Clients { get; } = new NoopHubClients();
        public IGroupManager Groups { get; } = new NoopGroups();

        private sealed class NoopGroups : IGroupManager
        {
            public Task AddToGroupAsync(string c, string g, CancellationToken ct = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string c, string g, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    // ── fixture ─────────────────────────────────────────────────────────────

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project);

    private const long LiveElementId = 4242;

    private static World NewWorld()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();

        using (var ctx = NewContext(conn, tenant))
        {
            ctx.Database.EnsureCreated();
            ctx.Tenants.Add(new Tenant
            {
                Id = tenant, Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}"[..14],
                ContactEmail = "a@e.com", Tier = LicenseTier.Professional,
                Plan = BillingPlan.Studio, MaxUsers = 50, MaxProjects = 50,
            });
            ctx.Projects.Add(new Project
            {
                Id = project, TenantId = tenant, Name = "Tower",
                Code = $"TW-{Guid.NewGuid():N}"[..8], Status = ProjectStatus.Active,
            });
            // One live element the delta will ask to delete.
            ctx.FederatedElements.Add(new FederatedElement
            {
                TenantId = tenant, ProjectId = project, SourceDocGuid = "revit-plugin",
                Source = "revit-plugin", ElementId = LiveElementId, UniqueId = "uid-4242",
                IsDeleted = false,
            });
            ctx.SaveChanges();
        }
        return new World(conn, tenant, project);
    }

    private static FederatedModelController NewController(
        World w, PlanscapeDbContext db, IFileStorageService storage, string? idemKey = null)
    {
        var ctrl = new FederatedModelController(
            db,
            new NoopHubContext<FederatedModelHub>(),
            new NoopHubContext<NotificationHub>(),
            storage,
            NullLogger<FederatedModelController>.Instance);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", w.Tenant.ToString()),
                new Claim("user_id", Guid.NewGuid().ToString()),
            }, "test")),
        };
        if (idemKey != null) http.Request.Headers["X-Idempotency-Key"] = idemKey;
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    /// <summary>A minimal but structurally valid GLB carrying one node's extras.</summary>
    private static IFormFile Glb(string uniqueId, long elementId)
    {
        string json = "{\"nodes\":[{\"extras\":{\"uniqueId\":\"" + uniqueId +
                      "\",\"elementId\":" + elementId + ",\"category\":\"Walls\"}}]}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        while (jsonBytes.Length % 4 != 0)          // glTF requires 4-byte chunk alignment
            jsonBytes = jsonBytes.Append((byte)0x20).ToArray();

        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("glTF"));           // magic
        bw.Write(2);                                          // version
        bw.Write(12 + 8 + jsonBytes.Length);                  // total length
        bw.Write(jsonBytes.Length);                           // chunk length  (offset 12)
        bw.Write(0x4E4F534A);                                 // chunk type JSON (offset 16)
        bw.Write(jsonBytes);                                  // JSON          (offset 20)
        bw.Flush();
        return new MemoryFormFile(ms.ToArray(), "delta.glb");
    }

    private static IFormFile DeletedIds(string json) =>
        new MemoryFormFile(Encoding.UTF8.GetBytes(json), "deletedIds");

    private static async Task<bool> IsDeletedAsync(World w)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        return await ctx.FederatedElements
            .Where(e => e.ElementId == LiveElementId).Select(e => e.IsDeleted).SingleAsync();
    }

    // ── C6: atomicity ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_failure_after_the_deletes_rolls_the_deletes_back()
    {
        // THE regression. Storage failing mid-delta used to leave the tombstone
        // applied and the geometry lost — and no retry could recover, because
        // the next delta's `!e.IsDeleted` filter skips an already-tombstoned row.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var ctrl = NewController(w, db, new StubStorage(throwOnSave: true));

            await Assert.ThrowsAsync<IOException>(() =>
                ctrl.PostDelta(w.Project, Glb("uid-1", 1), DeletedIds($"[{LiveElementId}]")));

            Assert.False(await IsDeletedAsync(w),
                "the element stayed tombstoned after the delta failed — deletes were not rolled back");
        }
    }

    [Fact]
    public async Task A_successful_delta_applies_both_halves()
    {
        // The mirror case: a rollback that fired unconditionally would satisfy
        // the test above and break the feature.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var ctrl = NewController(w, db, new StubStorage());

            var result = await ctrl.PostDelta(w.Project, Glb("uid-new", 99), DeletedIds($"[{LiveElementId}]"));

            Assert.IsType<OkObjectResult>(result);
            Assert.True(await IsDeletedAsync(w));

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 99 && !e.IsDeleted));
        }
    }

    [Fact]
    public async Task A_deleted_id_beyond_int_range_round_trips_through_the_wire()
    {
        // 64-bit regression on the server half. Revit 2024+ element ids can
        // exceed int.MaxValue and the plugin now sends them as full longs. The
        // endpoint used to deserialize deletedIds into List<int>, which
        // overflow-throws on such an id — caught, logged, and the tombstone
        // silently dropped. FederatedElement.ElementId is already long, so with
        // List<long> the id round-trips: added from the GLB node (GetInt64) and
        // deleted by the same value.
        long bigId = (long)int.MaxValue + 42;   // 2,147,483,689 — negative as int32

        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage())
                    .PostDelta(w.Project, Glb("uid-big", bigId), null);

            using (var seeded = NewContext(w.Conn, w.Tenant))
                Assert.True(
                    await seeded.FederatedElements.AnyAsync(e => e.ElementId == bigId && !e.IsDeleted),
                    "the 64-bit elementId was not stored from the GLB node");

            using (var db2 = NewContext(w.Conn, w.Tenant))
            {
                var result = await NewController(w, db2, new StubStorage())
                    .PostDelta(w.Project, Glb("uid-keep", 7), DeletedIds($"[{bigId}]"));
                Assert.IsType<OkObjectResult>(result);
            }

            using var check = NewContext(w.Conn, w.Tenant);
            var isDeleted = await check.FederatedElements
                .Where(e => e.ElementId == bigId).Select(e => e.IsDeleted).SingleAsync();
            Assert.True(isDeleted,
                "a 64-bit deletedId was not tombstoned — the server truncated or dropped it");
        }
    }

    // ── C6: idempotency ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_replayed_delta_is_a_no_op()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            const string key = "delta-key-1";

            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage(), key)
                    .PostDelta(w.Project, Glb("uid-new", 99), null);

            using var db2 = NewContext(w.Conn, w.Tenant);
            var replay = await NewController(w, db2, new StubStorage(), key)
                .PostDelta(w.Project, Glb("uid-other", 100), null);

            var ok = Assert.IsType<OkObjectResult>(replay);
            Assert.Contains("replayed", ok.Value!.ToString());

            using var check = NewContext(w.Conn, w.Tenant);
            // The replay's payload was NOT applied.
            Assert.False(await check.FederatedElements.AnyAsync(e => e.ElementId == 100));
            // …and the original still is.
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 99));
        }
    }

    [Fact]
    public async Task Two_different_keys_both_apply()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage(), "k1").PostDelta(w.Project, Glb("a", 11), null);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage(), "k2").PostDelta(w.Project, Glb("b", 12), null);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 11));
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 12));
        }
    }

    [Fact]
    public async Task Without_a_key_deltas_still_apply_as_before()
    {
        // Back-compat: an older plugin sends no key and must keep working.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage()).PostDelta(w.Project, Glb("a", 11), null);
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db, new StubStorage()).PostDelta(w.Project, Glb("b", 12), null);

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 11));
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 12));
        }
    }

    // ── C6: malformed input is reported, not swallowed ──────────────────────

    [Fact]
    public async Task A_malformed_deletedIds_payload_does_not_apply_partial_tombstones()
    {
        // The geometry half is still worth applying, but nothing must be
        // tombstoned on the strength of a payload that could not be read.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db, new StubStorage())
                .PostDelta(w.Project, Glb("uid-new", 99), DeletedIds("{ this is not an array }"));

            Assert.IsType<OkObjectResult>(result);
            Assert.False(await IsDeletedAsync(w));

            using var check = NewContext(w.Conn, w.Tenant);
            Assert.True(await check.FederatedElements.AnyAsync(e => e.ElementId == 99));
        }
    }

    [Fact]
    public async Task A_malformed_glb_does_not_take_the_whole_delta_down()
    {
        // Truncated/garbage GLB: the tombstones the caller asked for are still
        // applied, and the endpoint does not 500.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var garbage = new MemoryFormFile(Encoding.UTF8.GetBytes("not a glb at all, really"), "delta.glb");

            var result = await NewController(w, db, new StubStorage())
                .PostDelta(w.Project, garbage, DeletedIds($"[{LiveElementId}]"));

            Assert.IsType<OkObjectResult>(result);
            Assert.True(await IsDeletedAsync(w));
        }
    }
}
