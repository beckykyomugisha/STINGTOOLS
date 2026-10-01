using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Planscape.API.Controllers;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Security;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Tests;

/// <summary>
/// Follow-ups to the server-side ACC fix (535c425fc): one ACC code path through
/// PlatformController, durable DataProtection key store, unreadable-token status,
/// webhook dedupe / tenancy / signature, and APS webhook registration.
/// </summary>
public partial class AccServerIntegrationTests
{
    // ── shared helpers ──────────────────────────────────────────────────────

    private static IConfiguration WithSettings(IConfiguration baseConfig, Dictionary<string, string?> extra)
        => new ConfigurationBuilder().AddConfiguration(baseConfig).AddInMemoryCollection(extra).Build();

    private static HttpContext Ctx(Guid tenant, Guid user)
        => new DefaultHttpContext { User = User(tenant, user) };

    private static PlatformController Platform(Fx fx, PlanscapeDbContext db, Guid user)
    {
        var f = new Factory(fx.Http);
        var connector = new AccConnector(fx.Config, f, NullLogger<AccConnector>.Instance);
        return new PlatformController(db, new ConnectorFactory(connector), fx.Service(db))
        {
            ControllerContext = new ControllerContext { HttpContext = Ctx(fx.TenantId, user) },
        };
    }

    // ── 1: PlatformController routes ACC through AccSyncService ─────────────

    [Fact]
    public async Task Platform_sync_for_ACC_records_the_honest_PARTIAL_status_not_a_count_only_OK()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 3);
        StubAcc(fx.Http, okPosts: 1);
        var uid = await AddMemberAsync(fx, "Contributor");

        using var db = fx.Db();
        var r = await Platform(fx, db, uid).Sync(fx.ProjectId, fx.ConnId, default);

        var ok = Assert.IsType<OkObjectResult>(r);
        var report = Assert.IsType<AccSyncService.AccSyncReport>(ok.Value);
        Assert.Equal(AccSyncService.StatusPartial, report.Status);
        Assert.Equal(1, report.Pushed);
        // The issue push actually ran (the old generic path only did a GET count).
        Assert.Equal(3, fx.Http.Calls.Count(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/issues")));
        var stored = await fx.ReadConnAsync();
        Assert.Equal(AccSyncService.StatusPartial, stored.LastSyncStatus);
        Assert.Contains(AccSyncService.KeyIssueMap, stored.ConfigJson);
    }

    [Fact]
    public async Task Platform_sync_for_ACC_takes_the_same_write_gate_as_acc_sync()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAcc(fx.Http, okPosts: 10);
        var uid = await AddMemberAsync(fx, "Viewer");
        using var db = fx.Db();
        var r = await Platform(fx, db, uid).Sync(fx.ProjectId, fx.ConnId, default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
        Assert.Empty(fx.Http.Calls);
        Assert.Null((await fx.ReadConnAsync()).LastSyncStatus);
    }

    [Fact]
    public async Task Platform_test_for_ACC_refreshes_through_the_refresher_and_persists_the_rotation()
    {
        var fx = new Fx();
        await fx.SeedAsync(expires: DateTime.UtcNow.AddMinutes(-1));
        fx.Http.Respond = (req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/authentication/v2/token"))
                return body!.Contains("refresh_token=r1")
                    ? Json(HttpStatusCode.OK, new { access_token = "a2", refresh_token = "r2", expires_in = 3600 })
                    : Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
            if (req.RequestUri!.AbsolutePath.EndsWith("/project/v1/hubs"))
                return Json(HttpStatusCode.OK, new { data = new object[0] });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var uid = await AddMemberAsync(fx, "Contributor");

        using var db = fx.Db();
        var r = await Platform(fx, db, uid).TestConnection(fx.ProjectId, fx.ConnId, default);
        var result = Assert.IsType<PlatformTestResult>(Assert.IsType<OkObjectResult>(r.Result).Value);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, fx.Http.Calls.Count(c => c.Url.EndsWith("/authentication/v2/token")));   // refreshed once, by the refresher
        Assert.Equal("r2", (await fx.ReadConnAsync()).RefreshToken);                            // already on disk
    }

    // ── 2: unreadable token → RECONNECT_REQUIRED, never an exception or "" ───

    private const string Garbage = PlatformTokenProtection.Prefix + "not-decryptable-under-this-key-ring";

    [Fact]
    public async Task Undecryptable_tokens_make_the_sync_RECONNECT_REQUIRED_and_touch_nothing()
    {
        var fx = new Fx();
        await fx.SeedAsync(access: Garbage, refresh: Garbage, expires: DateTime.UtcNow.AddHours(1));
        StubAcc(fx.Http, okPosts: 10);

        using (var db = fx.Db())
        {
            var conn = await db.PlatformConnections.AsNoTracking().SingleAsync();
            Assert.True(PlatformTokenProtection.IsUnreadable(conn.AccessToken));   // not null, not ""
            Assert.True(AccTokenRefresher.TokensUnreadable(conn));
        }

        using (var db = fx.Db())
        {
            var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
            Assert.Equal(AccSyncService.StatusReconnect, r.Status);
            Assert.Contains("reconnect ACC", r.Error);
        }
        Assert.Empty(fx.Http.Calls);                                 // no token endpoint, no ACC call with ciphertext
        var stored = await fx.ReadConnAsync();
        Assert.Equal(AccSyncService.StatusReconnect, stored.LastSyncStatus);
        Assert.Equal(Garbage, stored.RefreshToken);                  // ciphertext kept: restoring the ring restores it

        // The plugin's token endpoint refuses rather than handing out ciphertext.
        var uid = await AddMemberAsync(fx, "Contributor");
        using (var db = fx.Db())
        {
            var ctl = new AccController(fx.Service(db), db, NullLogger<AccController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Ctx(fx.TenantId, uid) },
            };
            var t = await ctl.GetToken(fx.ProjectId, default);
            var nf = Assert.IsType<NotFoundObjectResult>(t);
            Assert.DoesNotContain(Garbage, JToken.FromObject(nf.Value!).ToString());
        }
    }

    [Fact]
    public async Task Invalid_grant_is_RECONNECT_REQUIRED_not_a_generic_failure()
    {
        var fx = new Fx();
        await fx.SeedAsync(expires: DateTime.UtcNow.AddMinutes(-1));
        StubToken(fx.Http, _ => Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" }));
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(AccSyncService.StatusReconnect, r.Status);
    }

    [Fact]
    public async Task Platform_create_refuses_a_client_supplied_ciphertext_token()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        var uid = await AddMemberAsync(fx, "Manager");
        using var db = fx.Db();
        var r = await Platform(fx, db, uid).Update(fx.ProjectId, fx.ConnId,
            new UpdatePlatformConnectionRequest { RefreshToken = Garbage });
        Assert.IsType<BadRequestObjectResult>(r.Result);
        Assert.Equal("r1", (await fx.ReadConnAsync()).RefreshToken);
    }

    // ── 2: key-store selection ──────────────────────────────────────────────

    private static IConfiguration Cfg(params (string Key, string? Value)[] kv)
        => new ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    [Fact]
    public void Key_store_defaults_to_the_database_and_honours_KeysPath()
    {
        const string db = "Host=x;Database=y";
        Assert.Equal(DataProtectionKeyStore.Kind.Database,
            DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db))).Kind);
        Assert.Equal(DataProtectionKeyStore.Kind.FileSystem,
            DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db), ("DataProtection:KeysPath", "/app/keys"))).Kind);
        Assert.Equal(DataProtectionKeyStore.Kind.Database,
            DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db), ("DataProtection:KeysPath", "/app/keys"),
                ("DataProtection:KeyStore", "database"))).Kind);

        // Ephemeral only when nothing durable exists or it is asked for — and then it says why.
        var none = DataProtectionKeyStore.Select(Cfg());
        Assert.Equal(DataProtectionKeyStore.Kind.Ephemeral, none.Kind);
        Assert.NotNull(none.Problem);
        Assert.Equal(DataProtectionKeyStore.Kind.Ephemeral,
            DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db), ("DataProtection:KeyStore", "ephemeral"))).Kind);
        // Misconfiguration does not silently pick something else durable-looking.
        var badFs = DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db), ("DataProtection:KeyStore", "filesystem")));
        Assert.Equal(DataProtectionKeyStore.Kind.Ephemeral, badFs.Kind);
        Assert.Contains("KeysPath", badFs.Problem);
        Assert.Equal(DataProtectionKeyStore.Kind.Ephemeral,
            DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", db), ("DataProtection:KeyStore", "redis"))).Kind);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel l, Microsoft.Extensions.Logging.EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f)
            => Entries.Add((l, f(s, ex)));
    }

    [Fact]
    public void Ephemeral_key_store_in_Production_logs_an_ERROR()
    {
        var log = new CapturingLogger();
        DataProtectionKeyStore.LogChoice(log, DataProtectionKeyStore.Select(Cfg()), isProduction: true);
        Assert.Contains(log.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("EPHEMERAL"));

        var log2 = new CapturingLogger();
        DataProtectionKeyStore.LogChoice(log2, DataProtectionKeyStore.Select(Cfg(("ConnectionStrings:Default", "Host=x"))), isProduction: true);
        Assert.DoesNotContain(log2.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Contains(log2.Entries, e => e.Message.Contains("DataProtectionKeys"));
    }

    [Fact]
    public void Database_key_store_survives_a_restart_and_is_shared_between_processes()
    {
        string dbName = "dpkeys-" + Guid.NewGuid().ToString("N");
        var config = Cfg(("ConnectionStrings:Default", "Host=unused"));

        ServiceProvider Boot()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<PlanscapeDbContext>(o => o.UseInMemoryDatabase(dbName));
            Assert.Equal(DataProtectionKeyStore.Kind.Database, DataProtectionKeyStore.Configure(services, config).Kind);
            return services.BuildServiceProvider();
        }

        string cipher;
        using (var api = Boot())
            cipher = api.GetRequiredService<IDataProtectionProvider>().CreateProtector("platform-connection-tokens.v1").Protect("refresh-token");

        // A new process (restart, or the worker) with the same database reads it.
        using (var worker = Boot())
            Assert.Equal("refresh-token",
                worker.GetRequiredService<IDataProtectionProvider>().CreateProtector("platform-connection-tokens.v1").Unprotect(cipher));

        using (var check = Boot())
        using (var scope = check.CreateScope())
            Assert.NotEmpty(scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>().DataProtectionKeys.ToList());

        // Control: an ephemeral ring (the old default) cannot read it — that is the bug.
        Assert.ThrowsAny<CryptographicException>(() =>
            new EphemeralDataProtectionProvider().CreateProtector("platform-connection-tokens.v1").Unprotect(cipher));
    }

    // ── 3: webhook receiver — dedupe, tenancy, signature ────────────────────

    private sealed class RecordingHub : IHubContext<NotificationHub>
    {
        public readonly List<(string Target, string Method)> Sends = new();
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new NoGroups();
        public RecordingHub() => Clients = new HubClients(this);

        private sealed class Proxy : IClientProxy
        {
            private readonly RecordingHub _h; private readonly string _t;
            public Proxy(RecordingHub h, string t) { _h = h; _t = t; }
            public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
            { lock (_h.Sends) _h.Sends.Add((_t, method)); return Task.CompletedTask; }
        }
        private sealed class HubClients : IHubClients
        {
            private readonly RecordingHub _h;
            public HubClients(RecordingHub h) => _h = h;
            public IClientProxy All => new Proxy(_h, "ALL");
            public IClientProxy AllExcept(IReadOnlyList<string> e) => new Proxy(_h, "ALL");
            public IClientProxy Client(string c) => new Proxy(_h, "client:" + c);
            public IClientProxy Clients(IReadOnlyList<string> c) => new Proxy(_h, "clients");
            public IClientProxy Group(string g) => new Proxy(_h, "group:" + g);
            public IClientProxy GroupExcept(string g, IReadOnlyList<string> e) => new Proxy(_h, "group:" + g);
            public IClientProxy Groups(IReadOnlyList<string> g) => new Proxy(_h, "groups");
            public IClientProxy User(string u) => new Proxy(_h, "user:" + u);
            public IClientProxy Users(IReadOnlyList<string> u) => new Proxy(_h, "users");
        }
        private sealed class NoGroups : IGroupManager
        {
            public Task AddToGroupAsync(string c, string g, CancellationToken ct = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string c, string g, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    private const string HookSecret = "hook-s3cret";

    private sealed class Receiver
    {
        public readonly RecordingHub Hub = new();
        private readonly Fx _fx;
        private readonly string? _secret;
        public Receiver(Fx fx, string? secret = HookSecret) { _fx = fx; _secret = secret; }

        public async Task<(IActionResult Result, JObject? Body)> PostAsync(string json, Guid? connectionId,
            string? deliveryId = null, string? signature = "auto")
        {
            var raw = Encoding.UTF8.GetBytes(json);
            var ctx = new DefaultHttpContext();
            ctx.Request.Body = new MemoryStream(raw);
            if (signature == "auto")
                signature = "sha1hash=" + Convert.ToHexString(HMACSHA1.HashData(Encoding.UTF8.GetBytes(HookSecret), raw)).ToLowerInvariant();
            if (signature != null) ctx.Request.Headers["x-adsk-signature"] = signature;
            if (deliveryId != null) ctx.Request.Headers["x-adsk-delivery-id"] = deliveryId;

            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Autodesk:WebhookSecret"] = _secret,
            }).Build();
            using var db = _fx.Db(withTenant: false);                   // anonymous endpoint: no tenant context
            var guard = new ApsWebhookDeliveryGuard(db, NullLogger<ApsWebhookDeliveryGuard>.Instance);
            var ctl = new AutodeskWebhooksController(db, Hub, cfg, guard, NullLogger<AutodeskWebhooksController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx },
            };
            var r = await ctl.Event(connectionId, default);
            JObject? body = r is ObjectResult o && o.Value != null ? JObject.FromObject(o.Value) : null;
            return (r, body);
        }
    }

    private static string VersionAdded(string urn, string? hookId = null, string? accProject = null)
        => new JObject
        {
            ["version"] = "1.0",
            ["resourceUrn"] = urn,
            ["hook"] = new JObject { ["hookId"] = hookId ?? "h-1", ["event"] = "dm.version.added", ["system"] = "data" },
            ["payload"] = accProject == null ? new JObject() : new JObject { ["projectId"] = accProject },
        }.ToString(Newtonsoft.Json.Formatting.None);

    /// <summary>Two tenants in ONE database, each with an ACC connection and a document carrying the same URN.</summary>
    private static async Task<(Guid DocA, Guid ConnB, Guid TenantB, Guid ProjectB, Guid DocB)> SeedTwoTenantsAsync(Fx fx, string urn, bool sameAccProject)
    {
        await fx.SeedAsync(openIssues: 0);
        Guid tenantB = Guid.NewGuid(), projectB = Guid.NewGuid(), connB = Guid.NewGuid(), docA = Guid.NewGuid(), docB = Guid.NewGuid();
        using var db = fx.Db(withTenant: false);
        db.Projects.Add(new Project { Id = projectB, TenantId = tenantB, Name = "B", Code = "B" });
        db.PlatformConnections.Add(new PlatformConnection
        {
            Id = connB, TenantId = tenantB, ProjectId = projectB, Platform = PlatformType.ACC, Name = "ACC-B",
            ExternalProjectId = sameAccProject ? "b.acc-proj" : "other-proj", IsActive = true,
        });
        string history = $"[{{\"urn\":\"{urn}\"}}]";
        db.Documents.Add(new DocumentRecord { Id = docA, TenantId = fx.TenantId, ProjectId = fx.ProjectId, FileName = "a.rvt", CdeStatus = "WIP", StatusHistoryJson = history });
        db.Documents.Add(new DocumentRecord { Id = docB, TenantId = tenantB, ProjectId = projectB, FileName = "b.rvt", CdeStatus = "WIP", StatusHistoryJson = history });
        await db.SaveChangesAsync();
        return (docA, connB, tenantB, projectB, docB);
    }

    private static async Task<DocumentRecord> ReadDocAsync(Fx fx, Guid id)
    {
        using var db = fx.Db(withTenant: false);
        return await db.Documents.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == id);
    }

    // AUT-5: the documented Reviews close event reaches the project's group (and only it).
    // Payload copied from the APS "Creating a Webhook (Forma Reviews)" tutorial, read 2026-10-01.
    [Fact]
    public async Task Webhook_review_closed_is_broadcast_to_the_project_and_nothing_is_applied()
    {
        var fx = new Fx();
        var (docA, _, _, projectB, _) = await SeedTwoTenantsAsync(fx, "urn:shared", sameAccProject: false);
        var rx = new Receiver(fx);
        string json = new JObject
        {
            ["version"] = "1.0",
            ["resourceUrn"] = "a4a3613c-c9dd-4e59-9d38-7b5a9857db9d",
            ["hook"] = new JObject { ["hookId"] = "h-r", ["event"] = "review.closed-1.0", ["system"] = "autodesk.construction.reviews",
                                     ["scope"] = new JObject { ["project"] = "acc-proj" } },
            ["payload"] = new JObject { ["roundNum"] = 1, ["sequenceId"] = "16", ["status"] = "CLOSED" },
        }.ToString(Newtonsoft.Json.Formatting.None);

        var (r, _) = await rx.PostAsync(json, fx.ConnId, deliveryId: "d-rev-1");

        Assert.IsType<OkObjectResult>(r);
        var send = Assert.Single(rx.Hub.Sends);
        Assert.Equal($"group:project-{fx.ProjectId}", send.Target);
        Assert.Equal("acc.review.closed", send.Method);
        Assert.Equal("WIP", (await ReadDocAsync(fx, docA)).CdeStatus);       // reported, never applied here
    }

    [Fact]
    public async Task Webhook_duplicate_delivery_is_processed_once()
    {
        var fx = new Fx();
        var (docA, _, _, _, _) = await SeedTwoTenantsAsync(fx, "urn:v1", sameAccProject: false);
        var rx = new Receiver(fx);

        var (r1, b1) = await rx.PostAsync(VersionAdded("urn:v1"), fx.ConnId, deliveryId: "d-42");
        Assert.IsType<OkObjectResult>(r1);
        Assert.Null(b1!["duplicate"]);
        var firstStamp = (await ReadDocAsync(fx, docA)).UpdatedAt;
        Assert.NotNull(firstStamp);

        var (r2, b2) = await rx.PostAsync(VersionAdded("urn:v1"), fx.ConnId, deliveryId: "d-42");
        Assert.IsType<OkObjectResult>(r2);
        Assert.True((bool?)b2!["duplicate"]);
        Assert.Equal(firstStamp, (await ReadDocAsync(fx, docA)).UpdatedAt);   // not re-applied
        Assert.Single(rx.Hub.Sends);                                            // one broadcast, not two

        // A different delivery id is a new event.
        await rx.PostAsync(VersionAdded("urn:v1"), fx.ConnId, deliveryId: "d-43");
        Assert.Equal(2, rx.Hub.Sends.Count);
    }

    [Fact]
    public async Task Webhook_event_for_tenant_A_never_touches_tenant_B_or_broadcasts_outside_As_project()
    {
        var fx = new Fx();
        var (docA, _, _, projectB, docB) = await SeedTwoTenantsAsync(fx, "urn:shared", sameAccProject: false);
        var rx = new Receiver(fx);

        await rx.PostAsync(VersionAdded("urn:shared"), fx.ConnId, deliveryId: "d-1");

        Assert.NotNull((await ReadDocAsync(fx, docA)).UpdatedAt);
        Assert.Null((await ReadDocAsync(fx, docB)).UpdatedAt);                 // same URN, other tenant: untouched
        var send = Assert.Single(rx.Hub.Sends);
        Assert.Equal($"group:project-{fx.ProjectId}", send.Target);
        Assert.DoesNotContain(rx.Hub.Sends, s => s.Target == "ALL" || s.Target == $"group:project-{projectB}");

        // Unpinned (payload fallback) with an ACC project only A has: still A only.
        await rx.PostAsync(VersionAdded("urn:shared", accProject: "b.acc-proj"), connectionId: null, deliveryId: "d-2");
        Assert.Null((await ReadDocAsync(fx, docB)).UpdatedAt);
        Assert.All(rx.Hub.Sends, s => Assert.Equal($"group:project-{fx.ProjectId}", s.Target));
        Assert.Equal(2, rx.Hub.Sends.Count);
    }

    [Fact]
    public async Task Webhook_payload_fallback_that_matches_two_tenants_touches_neither()
    {
        var fx = new Fx();
        var (docA, _, _, _, docB) = await SeedTwoTenantsAsync(fx, "urn:shared", sameAccProject: true);
        var rx = new Receiver(fx);

        var (r, body) = await rx.PostAsync(VersionAdded("urn:shared", accProject: "b.acc-proj"), connectionId: null, deliveryId: "d-9");
        Assert.IsType<OkObjectResult>(r);
        Assert.False((bool?)body!["matched"]);
        Assert.Null((await ReadDocAsync(fx, docA)).UpdatedAt);
        Assert.Null((await ReadDocAsync(fx, docB)).UpdatedAt);
        Assert.Empty(rx.Hub.Sends);
    }

    [Fact]
    public async Task Webhook_unsigned_or_wrongly_signed_or_secretless_is_rejected_and_changes_nothing()
    {
        var fx = new Fx();
        var (docA, _, _, _, _) = await SeedTwoTenantsAsync(fx, "urn:v1", sameAccProject: false);
        var rx = new Receiver(fx);
        string json = VersionAdded("urn:v1");

        Assert.IsType<UnauthorizedObjectResult>((await rx.PostAsync(json, fx.ConnId, "d-1", signature: null)).Result);
        var wrong = "sha1hash=" + Convert.ToHexString(HMACSHA1.HashData(Encoding.UTF8.GetBytes("not-the-secret"), Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        Assert.IsType<UnauthorizedObjectResult>((await rx.PostAsync(json, fx.ConnId, "d-2", signature: wrong)).Result);

        var noSecret = new Receiver(fx, secret: null);
        Assert.Equal(503, Assert.IsType<ObjectResult>((await noSecret.PostAsync(json, fx.ConnId, "d-3")).Result).StatusCode);

        Assert.Null((await ReadDocAsync(fx, docA)).UpdatedAt);
        Assert.Empty(rx.Hub.Sends);
        Assert.Empty(noSecret.Hub.Sends);
        // A rejected delivery is not remembered: a correctly signed redelivery still applies.
        await rx.PostAsync(json, fx.ConnId, "d-1");
        Assert.NotNull((await ReadDocAsync(fx, docA)).UpdatedAt);
    }

    [Fact]
    public async Task Webhook_from_a_hook_the_pinned_connection_did_not_register_is_ignored()
    {
        var fx = new Fx();
        var (docA, _, _, _, _) = await SeedTwoTenantsAsync(fx, "urn:v1", sameAccProject: false);
        using (var db = fx.Db())
        {
            var c = await db.PlatformConnections.SingleAsync(x => x.Id == fx.ConnId);
            c.ConfigJson = "{\"accIssueSubtypeId\":\"sub-1\",\"accWebhookHooks\":[{\"system\":\"data\",\"event\":\"dm.version.added\",\"hookId\":\"ours\",\"scopeKey\":\"folder\",\"scopeValue\":\"f\"}]}";
            await db.SaveChangesAsync();
        }
        var rx = new Receiver(fx);
        var (_, body) = await rx.PostAsync(VersionAdded("urn:v1", hookId: "someone-elses"), fx.ConnId, "d-1");
        Assert.False((bool?)body!["matched"]);
        Assert.Null((await ReadDocAsync(fx, docA)).UpdatedAt);

        await rx.PostAsync(VersionAdded("urn:v1", hookId: "ours"), fx.ConnId, "d-2");
        Assert.NotNull((await ReadDocAsync(fx, docA)).UpdatedAt);
    }

    // ── 4: APS webhook registration ─────────────────────────────────────────

    private static AccWebhookService Hooks(Fx fx, PlanscapeDbContext db, IConfiguration cfg)
    {
        var f = new Factory(fx.Http);
        var connector = new AccConnector(cfg, f, NullLogger<AccConnector>.Instance);
        return new AccWebhookService(db, new ConnectorFactory(connector), f, NullLogger<AccWebhookService>.Instance, cfg);
    }

    /// <summary>APS stub: secret PUT → 404 then POST → 201; hook create → 201 + Location; delete → 204.</summary>
    private static void StubWebhooks(Handler h, Func<string, HttpStatusCode>? createStatus = null)
    {
        int n = 0;
        h.Respond = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Put && path == "/webhooks/v1/tokens/@me") return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (req.Method == HttpMethod.Post && path == "/webhooks/v1/tokens") return new HttpResponseMessage(HttpStatusCode.Created);
            if (req.Method == HttpMethod.Post && path.EndsWith("/hooks"))
            {
                var code = createStatus?.Invoke(path) ?? HttpStatusCode.Created;
                var resp = new HttpResponseMessage(code);
                if (code == HttpStatusCode.Created)
                    resp.Headers.Location = new Uri($"https://aps.test{path}/hook-{Interlocked.Increment(ref n)}");
                return resp;
            }
            if (req.Method == HttpMethod.Delete && path.Contains("/hooks/")) return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    [Fact]
    public async Task Subscribe_sets_the_secret_creates_pinned_hooks_and_records_their_ids()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accIssueSubtypeId\":\"sub-1\",\"accRegion\":\"EMEA\"}");
        StubWebhooks(fx.Http);
        var cfg = WithSettings(fx.Config, new() { ["Autodesk:WebhookSecret"] = HookSecret });

        AccWebhookService.Result r;
        using (var db = fx.Db())
            r = await Hooks(fx, db, cfg).SubscribeAsync(fx.ProjectId, new[] { "urn:adsk.wipprod:fs.folder:co.F1" }, default);

        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.Empty(r.Errors);

        // Secret set to OUR configured value before any hook exists.
        var calls = fx.Http.Calls;
        int secretAt = calls.FindIndex(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/webhooks/v1/tokens"));
        int firstHook = calls.FindIndex(c => c.Url.EndsWith("/hooks"));
        Assert.True(secretAt >= 0 && secretAt < firstHook);
        Assert.Equal(HookSecret, (string?)JObject.Parse(calls[secretAt].Body!)["token"]);

        var creates = calls.Where(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/hooks")).ToList();
        Assert.Equal(5, creates.Count);   // 2 issue + 1 review (AUT-5) + 2 DM
        Assert.Contains(creates, c => c.Url == "https://aps.test/webhooks/v1/systems/autodesk.construction.reviews/events/review.closed-1.0/hooks");
        Assert.Contains(creates, c => c.Url == "https://aps.test/webhooks/v1/systems/autodesk.construction.issues/events/issue.created-1.0/hooks");
        Assert.Contains(creates, c => c.Url == "https://aps.test/webhooks/v1/systems/autodesk.construction.issues/events/issue.updated-1.0/hooks");
        Assert.Contains(creates, c => c.Url == "https://aps.test/webhooks/v1/systems/data/events/dm.version.added/hooks");
        foreach (var c in creates)
        {
            var body = JObject.Parse(c.Body!);
            Assert.Equal($"https://planscape.test/api/webhooks/autodesk/event?connectionId={fx.ConnId}", (string?)body["callbackUrl"]);
            var scope = (JObject)body["scope"]!;
            if (c.Url.Contains("construction.issues") || c.Url.Contains("construction.reviews"))
                Assert.Equal("acc-proj", (string?)scope["project"]);   // bare id; "project" is the documented scope name for both
            else Assert.Equal("urn:adsk.wipprod:fs.folder:co.F1", (string?)scope["folder"]);
        }

        var stored = await fx.ReadConnAsync();
        var hooks = AccWebhookService.ReadHooks(JObject.Parse(stored.ConfigJson!));
        Assert.Equal(5, hooks.Count);
        Assert.All(hooks, h => Assert.StartsWith("hook-", h.HookId));

        // Idempotent: a second run creates nothing new.
        fx.Http.Calls.Clear();
        using (var db = fx.Db())
            Assert.Equal(AccSyncService.StatusOk, (await Hooks(fx, db, cfg).SubscribeAsync(fx.ProjectId, new[] { "urn:adsk.wipprod:fs.folder:co.F1" }, default)).Status);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Url.EndsWith("/hooks"));

        // A client PUT of ConfigJson cannot drop the recorded hooks.
        var (merged, _) = AccSyncService.MergeClientConfig(stored.ConfigJson, "{\"accIssueSubtypeId\":\"sub-2\"}");
        Assert.Equal(5, AccWebhookService.ReadHooks(JObject.Parse(merged!)).Count);

        // Unsubscribe deletes each recorded hook and clears the record.
        fx.Http.Calls.Clear();
        using (var db = fx.Db())
            Assert.Equal(AccSyncService.StatusOk, (await Hooks(fx, db, cfg).UnsubscribeAsync(fx.ProjectId, default)).Status);
        Assert.Equal(5, fx.Http.Calls.Count(c => c.Method == HttpMethod.Delete));
        Assert.Contains(fx.Http.Calls, c => c.Method == HttpMethod.Delete
            && c.Url.StartsWith("https://aps.test/webhooks/v1/systems/data/events/dm.version.added/hooks/hook-"));
        Assert.Empty(AccWebhookService.ReadHooks(JObject.Parse((await fx.ReadConnAsync()).ConfigJson!)));
    }

    [Fact]
    public async Task Subscribe_partial_failure_is_PARTIAL_and_only_real_hooks_are_recorded()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubWebhooks(fx.Http, p => p.Contains("issue.updated") ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
        var cfg = WithSettings(fx.Config, new() { ["Autodesk:WebhookSecret"] = HookSecret });
        using var db = fx.Db();
        var r = await Hooks(fx, db, cfg).SubscribeAsync(fx.ProjectId, Array.Empty<string>(), default);   // [] = no DM hooks
        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        Assert.Equal("none", r.FolderSource);
        Assert.Single(r.Errors);
        var hooks = AccWebhookService.ReadHooks(JObject.Parse((await fx.ReadConnAsync()).ConfigJson!));
        // The refused issue.updated hook is not recorded; the two that APS created are.
        Assert.Equal(2, hooks.Count);
        Assert.Contains(hooks, h => h.Event == "issue.created-1.0");
        Assert.Contains(hooks, h => h.Event == "review.closed-1.0" && h.System == AccWebhookService.SystemReviews);
        Assert.DoesNotContain(hooks, h => h.Event == "issue.updated-1.0");
    }

    [Fact]
    public async Task Subscribe_without_a_secret_or_https_callback_fails_before_calling_APS()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubWebhooks(fx.Http);
        using (var db = fx.Db())
        {
            var r = await Hooks(fx, db, fx.Config).SubscribeAsync(fx.ProjectId, null, default);   // no Autodesk:WebhookSecret
            Assert.Equal(AccSyncService.StatusFailed, r.Status);
            Assert.Contains("WebhookSecret", r.Errors[0]);
        }
        var httpCfg = WithSettings(fx.Config, new()
        {
            ["Autodesk:WebhookSecret"] = HookSecret,
            ["Autodesk:WebhookCallbackUrl"] = "http://insecure.test/api/webhooks/autodesk/event",
        });
        using (var db = fx.Db())
            Assert.Equal(AccSyncService.StatusFailed, (await Hooks(fx, db, httpCfg).SubscribeAsync(fx.ProjectId, null, default)).Status);
        Assert.Empty(fx.Http.Calls);
    }

    [Theory]
    [InlineData("Contributor", false)]
    [InlineData("Manager", true)]
    public async Task Webhook_subscription_needs_the_administer_capability(string role, bool allowed)
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubWebhooks(fx.Http);
        var uid = await AddMemberAsync(fx, role);
        var cfg = WithSettings(fx.Config, new() { ["Autodesk:WebhookSecret"] = HookSecret });
        using var db = fx.Db();
        var ctl = new AccWebhookSubscriptionsController(Hooks(fx, db, cfg), db)
        {
            ControllerContext = new ControllerContext { HttpContext = Ctx(fx.TenantId, uid) },
        };
        var r = await ctl.Subscribe(fx.ProjectId, new AccWebhookSubscriptionsController.SubscribeRequest(null), default);
        if (allowed) Assert.IsType<OkObjectResult>(r);
        else
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
            Assert.Empty(fx.Http.Calls);
        }
    }

    [Fact]
    public void Callback_url_prefers_explicit_config_and_falls_back_to_the_OAuth_origin()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"https://api.example/api/webhooks/autodesk/event?connectionId={id}",
            AccWebhookService.CallbackUrl(Cfg(("Acc:CallbackUrl", "https://api.example/api/acc/oauth/callback")), id));
        Assert.Equal($"https://hooks.example/in?x=1&connectionId={id}",
            AccWebhookService.CallbackUrl(Cfg(("Autodesk:WebhookCallbackUrl", "https://hooks.example/in?x=1")), id));
        Assert.Null(AccWebhookService.CallbackUrl(Cfg(("Acc:CallbackUrl", "http://api.example/cb")), id));
        Assert.Null(AccWebhookService.CallbackUrl(Cfg(), id));
    }
}
