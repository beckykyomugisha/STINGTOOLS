using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Planscape.API.Controllers;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Security;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.Services.Aps;

namespace Planscape.Tests;

/// <summary>
/// Server-side ACC / APS integration: sync status honesty, idempotent issue map,
/// rotating-refresh persistence, OAuth state sealing + replay, token release
/// gate, at-rest token encryption, webhook signature.
///
/// Every fixture is built WITH a tenant context and seeds real rows (the global
/// filter falls back to Guid.Empty and would otherwise match nothing), and each
/// assertion is on data read back through a FRESH context, not the tracked copy.
/// </summary>
public partial class AccServerIntegrationTests
{
    static AccServerIntegrationTests()
    {
        // First call wins process-wide (see PlatformTokenProtection.Configure);
        // whichever cipher is live is used for both writes and reads.
        PlatformTokenProtection.Configure(new DataProtectionSecretCipher(
            new EphemeralDataProtectionProvider().CreateProtector("acc-server-tests")));
        ApsRetry.Delay = (_, _) => Task.CompletedTask;
    }

    // ── fixture ─────────────────────────────────────────────────────────────

    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "acme";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Body)> Calls = new();
        public Func<HttpRequestMessage, string?, HttpResponseMessage> Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Calls) Calls.Add((request.Method, Uri.UnescapeDataString(request.RequestUri!.ToString()), body));
            return Respond(request, body);
        }
    }

    private sealed class Factory : IHttpClientFactory
    {
        private readonly Handler _h;
        public Factory(Handler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private sealed class ConnectorFactory : IPlatformConnectorFactory
    {
        private readonly IPlatformConnector _c;
        public ConnectorFactory(IPlatformConnector c) => _c = c;
        public IPlatformConnector GetConnector(PlatformType platform) => _c;
    }

    private static HttpResponseMessage Json(HttpStatusCode code, object body)
        => new(code) { Content = new StringContent(JToken.FromObject(body).ToString(), Encoding.UTF8, "application/json") };

    private sealed class Fx
    {
        public readonly string DbName = "acc-" + Guid.NewGuid().ToString("N");
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid ProjectId = Guid.NewGuid();
        public readonly Guid ConnId = Guid.NewGuid();
        public readonly Handler Http = new();
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Acc:ClientId"] = "cid",
            ["Acc:ClientSecret"] = "secret",
            ["Acc:CallbackUrl"] = "https://planscape.test/api/acc/oauth/callback",
            ["Aps:BaseUrl"] = "https://aps.test",
        }).Build();

        public PlanscapeDbContext Db(bool withTenant = true)
        {
            var opts = new DbContextOptionsBuilder<PlanscapeDbContext>().UseInMemoryDatabase(DbName).Options;
            return withTenant ? new PlanscapeDbContext(opts, new HttpContextAccessor(), new FixedTenant(TenantId)) : new PlanscapeDbContext(opts);
        }

        public AccSyncService Service(PlanscapeDbContext db)
        {
            var f = new Factory(Http);
            var connector = new AccConnector(Config, f, NullLogger<AccConnector>.Instance);
            return new AccSyncService(db, new ConnectorFactory(connector), f, NullLogger<AccSyncService>.Instance, Config);
        }

        public async Task SeedAsync(string? configJson = "{\"accIssueSubtypeId\":\"sub-1\"}", int openIssues = 2,
            string access = "a1", string refresh = "r1", DateTime? expires = null)
        {
            using var db = Db();
            db.Projects.Add(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", Code = "P" });
            db.PlatformConnections.Add(new PlatformConnection
            {
                Id = ConnId, TenantId = TenantId, ProjectId = ProjectId, Platform = PlatformType.ACC,
                Name = "ACC", ExternalProjectId = "b.acc-proj", IsActive = true,
                AccessToken = access, RefreshToken = refresh,
                TokenExpiresAt = expires ?? DateTime.UtcNow.AddHours(1),
                ConfigJson = configJson,
            });
            for (int i = 0; i < openIssues; i++)
                db.Issues.Add(new BimIssue
                {
                    TenantId = TenantId, ProjectId = ProjectId, IssueCode = $"CLASH-{i:000}",
                    Title = $"Issue {i}", Status = "OPEN", CreatedAt = DateTime.UtcNow.AddMinutes(i),
                });
            await db.SaveChangesAsync();
        }

        public async Task<PlatformConnection> ReadConnAsync()
        {
            using var db = Db();
            return await db.PlatformConnections.AsNoTracking().SingleAsync(c => c.Id == ConnId);
        }
    }

    /// <summary>ACC stub: POST issues succeed for the first <paramref name="okPosts"/> calls.</summary>
    private static void StubAcc(Handler h, int okPosts, bool openCountOk = true, bool readBackOk = true)
    {
        int posts = 0;
        h.Respond = (req, _) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Post && url.EndsWith("/construction/issues/v1/projects/acc-proj/issues"))
            {
                int n = Interlocked.Increment(ref posts);
                return n <= okPosts
                    ? Json(HttpStatusCode.Created, new { id = $"acc-{n}", status = "open" })
                    : Json(HttpStatusCode.BadRequest, new { detail = "rejected" });
            }
            if (req.Method == HttpMethod.Patch && url.Contains("/construction/issues/v1/projects/acc-proj/issues/"))
                return Json(HttpStatusCode.OK, new { id = url.Substring(url.LastIndexOf('/') + 1) });
            if (req.Method == HttpMethod.Get && url.Contains("filter[status]=open"))
                return openCountOk
                    ? Json(HttpStatusCode.OK, new { pagination = new { limit = 1, offset = 0, totalResults = 7 }, results = new object[0] })
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (req.Method == HttpMethod.Get && url.Contains("filter[id]="))
            {
                if (!readBackOk) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                var ids = url.Substring(url.IndexOf("filter[id]=") + 11).Split('&')[0].Split(',');
                var results = ids.Select((id, i) => new { id, status = i == 0 ? "closed" : "open" }).ToArray();
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = results.Length }, results });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    // ── 1 / 2: status honesty ───────────────────────────────────────────────

    [Fact]
    public async Task All_pushes_ok_is_OK_with_counts()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 2);
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.True(r.Success);
        Assert.Equal(2, r.Pushed);
        Assert.Equal(7, r.PulledOpen);
        Assert.Equal(1, r.MappedClosedInAcc);
        Assert.Equal(AccSyncService.StatusOk, (await fx.ReadConnAsync()).LastSyncStatus);
    }

    // ── C10: Planscape edits reach ACC ─────────────────────────────────────

    [Fact]
    public async Task A_mapped_issue_edited_in_Planscape_is_patched_to_ACC_once()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);   // creates acc-1
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Patch);

        using (var db = fx.Db())
        {
            var i = await db.Issues.SingleAsync();
            i.Title = "Retitled in Planscape"; i.Status = "CLOSED"; i.UpdatedAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        fx.Http.Calls.Clear();
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(1, r.Updated);
        var patch = Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Patch);
        Assert.EndsWith("/issues/acc-1", patch.Url);
        var body = JObject.Parse(patch.Body!);
        Assert.Equal("Retitled in Planscape", (string?)body["title"]);
        // acc-1 read back as "closed" (StubAcc's first id); Planscape closed it too, so the status goes.
        Assert.Equal("closed", (string?)body["status"]);

        // Nothing changed since: no second PATCH.
        fx.Http.Calls.Clear();
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(0, r.Updated);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task An_issue_ACC_closed_is_never_reopened_from_Planscape()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);   // read-back reports acc-1 closed
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        using (var db = fx.Db())
        {
            var i = await db.Issues.SingleAsync();
            i.Description = "more detail"; i.UpdatedAt = DateTime.UtcNow.AddMinutes(5);   // still OPEN in Planscape
            await db.SaveChangesAsync();
        }
        fx.Http.Calls.Clear();
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        var body = JObject.Parse(Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Patch).Body!);
        Assert.Null(body["status"]);                     // withheld: would reopen
        Assert.Equal("more detail", (string?)body["description"]);
        Assert.Equal(1, r.Diverged);
        Assert.Contains("closed in ACC", r.Error);
    }

    [Fact]
    public async Task A_legacy_mapping_gets_a_baseline_not_a_mass_patch()
    {
        var fx = new Fx();
        Guid issueId = Guid.NewGuid();
        await fx.SeedAsync(openIssues: 0, configJson: "{\"accIssueSubtypeId\":\"sub-1\",\"accIssueMap\":{\"" + issueId + "\":\"acc-9\"}}");
        using (var seed = fx.Db())
        {
            seed.Issues.Add(new BimIssue { Id = issueId, TenantId = fx.TenantId, ProjectId = fx.ProjectId, IssueCode = "RFI-0001",
                Title = "Old", Status = "OPEN", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        StubAcc(fx.Http, okPosts: 10);
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(0, r.Updated);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Patch);
        var cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.NotNull(cfg[AccSyncService.KeyIssuePushedAt]?[issueId.ToString()]);
    }

    [Theory]
    [InlineData("OPEN", "closed", true)]
    [InlineData("OPEN", "open", false)]
    [InlineData("CLOSED", "closed", false)]
    [InlineData("OPEN", null, false)]
    public void The_update_plan_withholds_only_a_reopen(string planscape, string? acc, bool withheld)
    {
        var d = AccIssueUpdatePlan.Plan("t", "d", planscape, acc);
        Assert.Equal(withheld, d.StatusWithheld);
        Assert.Equal(withheld, d.Body["status"] == null);
    }

    [Fact]
    public async Task Issues_that_came_from_ACC_are_linked_never_pushed_again()
    {
        // The duplicate loop: plugin imports an ACC issue -> Planscape server -> this sync
        // would POST it to ACC as a NEW issue -> the next import brings the copy back.
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        using (var seed = fx.Db())
        {
            seed.Issues.Add(new BimIssue
            {
                TenantId = fx.TenantId, ProjectId = fx.ProjectId, IssueCode = "RFI-0100", Title = "From ACC (with id)",
                Status = "OPEN", CreatedAt = DateTime.UtcNow, Source = "acc", CustomFields = "{\"accIssueId\":\"acc-original-7\"}",
            });
            seed.Issues.Add(new BimIssue
            {
                TenantId = fx.TenantId, ProjectId = fx.ProjectId, IssueCode = "RFI-0101", Title = "From ACC (no id)",
                Status = "OPEN", CreatedAt = DateTime.UtcNow, Source = "acc",
            });
            await seed.SaveChangesAsync();
        }
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(1, r.Pushed);                                   // only the Planscape-born issue
        int posts = fx.Http.Calls.Count(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/issues"));
        Assert.Equal(1, posts);
        var map = (JObject)JObject.Parse((await fx.ReadConnAsync()).ConfigJson!)[AccSyncService.KeyIssueMap]!;
        Assert.Contains(map.Properties(), p => (string?)p.Value == "acc-original-7");   // linked, so read-back covers it
    }

    [Fact]
    public async Task Some_pushes_failing_is_PARTIAL_not_success()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 3);
        StubAcc(fx.Http, okPosts: 1);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        Assert.False(r.Success);
        Assert.Equal(1, r.Pushed);
        Assert.Equal(2, r.Failed);
        var stored = await fx.ReadConnAsync();
        Assert.Equal(AccSyncService.StatusPartial, stored.LastSyncStatus);
        Assert.Contains("2 of 3 push(es) failed", stored.LastSyncError);
    }

    [Fact]
    public async Task Every_push_failing_is_FAILED()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 2);
        StubAcc(fx.Http, okPosts: 0);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Equal(0, r.Pushed);
        Assert.Equal(AccSyncService.StatusFailed, (await fx.ReadConnAsync()).LastSyncStatus);
    }

    [Fact]
    public async Task Open_count_read_failure_is_FAILED_and_count_unknown_not_zero()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 0);
        StubAcc(fx.Http, okPosts: 10, openCountOk: false);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Null(r.PulledOpen);                       // unknown — never 0
        Assert.Contains("Open-count pull failed", r.Error);
        // idempotent GET was retried (bounded), not given up after one attempt
        Assert.Equal(ApsRetry.DefaultMaxAttempts, fx.Http.Calls.Count(c => c.Url.Contains("filter[status]")));
    }

    [Fact]
    public async Task Status_read_back_failure_is_PARTIAL()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10, readBackOk: false);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        Assert.Null(r.MappedClosedInAcc);
    }

    [Fact]
    public async Task Push_uses_issues_v1_project_path_and_documented_body()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        var post = fx.Http.Calls.Single(c => c.Method == HttpMethod.Post);
        Assert.Equal("https://aps.test/construction/issues/v1/projects/acc-proj/issues", post.Url);   // no "b.", no /containers/
        var body = JObject.Parse(post.Body!);
        Assert.Equal("sub-1", (string?)body["issueSubtypeId"]);
        Assert.Equal("open", (string?)body["status"]);
        Assert.Null(body["issueTypeId"]);
    }

    // ── 9: idempotency ──────────────────────────────────────────────────────

    [Fact]
    public async Task Issue_map_is_persisted_after_each_push_even_if_the_run_dies()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 3);
        int posts = 0;
        fx.Http.Respond = (req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                if (Interlocked.Increment(ref posts) == 2) throw new OperationCanceledException("host shutting down mid-run");
                return Json(HttpStatusCode.Created, new { id = $"acc-{posts}" });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        using (var db = fx.Db())
        {
            // Cancellation (host shutdown) is the one failure that escapes the
            // per-push catch and aborts the run part-way — the crash case.
            var svc = fx.Service(db);
            var conn = await db.PlatformConnections.SingleAsync();
            try { await svc.SyncConnectionAsync(conn, CancellationToken.None); } catch { /* the crash */ }
        }

        var stored = await fx.ReadConnAsync();
        var map = (JObject)JObject.Parse(stored.ConfigJson!)[AccSyncService.KeyIssueMap]!;
        Assert.Single(map);                                    // the push that landed is remembered
        Assert.Equal("acc-1", (string?)map.Properties().Single().Value);

        // A re-run pushes only the two not yet mapped — no duplicate of acc-1.
        posts = 10;
        fx.Http.Calls.Clear();
        StubAcc(fx.Http, okPosts: 10);
        using (var db = fx.Db())
            await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(2, fx.Http.Calls.Count(c => c.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Corrupt_config_fails_loudly_and_pushes_nothing()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{not json", openIssues: 2);
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Contains("ConfigJson", r.Error);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public void Client_config_PUT_keeps_the_server_owned_issue_map()
    {
        var stored = "{\"accIssueSubtypeId\":\"sub-1\",\"accIssueMap\":{\"i1\":\"acc-1\"},\"accIssueStatus\":{\"acc-1\":\"open\"}}";
        // Client sends a stale copy with the map dropped and a forged entry.
        var (merged, err) = AccSyncService.MergeClientConfig(stored, "{\"accIssueSubtypeId\":\"sub-2\",\"accIssueMap\":{}}");
        Assert.Null(err);
        var j = JObject.Parse(merged!);
        Assert.Equal("sub-2", (string?)j["accIssueSubtypeId"]);
        Assert.Equal("acc-1", (string?)j["accIssueMap"]!["i1"]);
        Assert.Equal("open", (string?)j["accIssueStatus"]!["acc-1"]);

        var (m2, e2) = AccSyncService.MergeClientConfig("{broken", "{}");
        Assert.Null(m2);
        Assert.NotNull(e2);
    }

    // ── 3 / 4: rotating refresh token ───────────────────────────────────────

    private static void StubToken(Handler h, Func<string, HttpResponseMessage> onRefresh)
    {
        h.Respond = (req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/authentication/v2/token"))
            {
                var rt = body!.Split('&').Select(p => p.Split('=')).First(p => p[0] == "refresh_token")[1];
                return onRefresh(Uri.UnescapeDataString(rt));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    [Fact]
    public async Task Rotated_refresh_token_is_saved_immediately()
    {
        var fx = new Fx();
        await fx.SeedAsync(expires: DateTime.UtcNow.AddMinutes(-1));
        StubToken(fx.Http, rt => rt == "r1"
            ? Json(HttpStatusCode.OK, new { access_token = "a2", refresh_token = "r2", expires_in = 3600 })
            : Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" }));

        using var db = fx.Db();
        var (token, err) = await fx.Service(db).GetFreshAccessTokenAsync(fx.ProjectId);
        Assert.Null(err);
        Assert.Equal("a2", token);

        // No further SaveChanges by the caller: the rotation is already on disk.
        var stored = await fx.ReadConnAsync();
        Assert.Equal("r2", stored.RefreshToken);
        Assert.Equal("a2", stored.AccessToken);
    }

    [Fact]
    public async Task Loser_of_a_refresh_race_adopts_the_winners_token()
    {
        var fx = new Fx();
        await fx.SeedAsync(expires: DateTime.UtcNow.AddMinutes(-1));
        StubToken(fx.Http, rt => rt == "r1"
            ? Json(HttpStatusCode.OK, new { access_token = "a2", refresh_token = "r2", expires_in = 3600 })
            : Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" }));

        using var loserDb = fx.Db();
        var loserConn = await loserDb.PlatformConnections.SingleAsync();   // holds stale r1

        using (var winnerDb = fx.Db())
            Assert.Null((await fx.Service(winnerDb).GetFreshAccessTokenAsync(fx.ProjectId)).Error);

        // r1 is now spent: ACC answers invalid_grant. The loser must adopt a2/r2.
        StubToken(fx.Http, _ => Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" }));
        var outcome = await AccTokenRefresher.EnsureFreshAsync(loserDb,
            new AccConnector(fx.Config, new Factory(fx.Http), NullLogger<AccConnector>.Instance),
            loserConn, null, CancellationToken.None);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("a2", loserConn.AccessToken);
        Assert.Equal("r2", loserConn.RefreshToken);
    }

    // ── 8: tokens encrypted at rest ─────────────────────────────────────────

    [Fact]
    public async Task Tokens_are_encrypted_at_rest_and_legacy_plaintext_still_reads()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(conn).Options;
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();

        using (var db = new PlanscapeDbContext(opts, new HttpContextAccessor(), new FixedTenant(tenant)))
        {
            db.Database.EnsureCreated();
            db.Tenants.Add(new Tenant { Id = tenant, Name = "T", Slug = "t-" + tenant.ToString("N")[..8] });
            db.Projects.Add(new Project { Id = project, TenantId = tenant, Name = "P", Code = "P" });
            db.PlatformConnections.Add(new PlatformConnection
            {
                Id = newId, TenantId = tenant, ProjectId = project, Platform = PlatformType.ACC,
                Name = "ACC", AccessToken = "access-plain", RefreshToken = "refresh-plain",
            });
            await db.SaveChangesAsync();
        }

        // Raw column: ciphertext, not the token.
        string RawRefresh(Guid id)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RefreshToken FROM PlatformConnections WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
            var v = cmd.ExecuteScalar() as string;
            if (v != null) return v;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id", id);
            return (string)cmd.ExecuteScalar()!;
        }
        var raw = RawRefresh(newId);
        Assert.StartsWith(PlatformTokenProtection.Prefix, raw);
        Assert.DoesNotContain("refresh-plain", raw);

        // A legacy row written as plaintext before the converter existed (raw SQL
        // bypasses the converter) — it must still read, and re-encrypt on write.
        using (var db = new PlanscapeDbContext(opts, new HttpContextAccessor(), new FixedTenant(tenant)))
        {
            var p = await db.PlatformConnections.AsNoTracking().SingleAsync(c => c.Id == newId);
            Assert.Equal("refresh-plain", p.RefreshToken);    // round-trips through the cipher
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE PlatformConnections SET RefreshToken = 'legacy-refresh' WHERE Id = {newId}");
            var legacy = await db.PlatformConnections.SingleAsync(c => c.Id == newId);
            Assert.Equal("legacy-refresh", legacy.RefreshToken);
            legacy.RefreshToken = "rotated";
            await db.SaveChangesAsync();
        }
        Assert.StartsWith(PlatformTokenProtection.Prefix, RawRefresh(newId));

        // Undecryptable ciphertext reads back as itself — detectably unreadable
        // ("reconnect"), never as a token and never as a silent empty string.
        var unreadable = PlatformTokenProtection.Unprotect(PlatformTokenProtection.Prefix + "garbage");
        Assert.True(PlatformTokenProtection.IsUnreadable(unreadable));
        Assert.False(string.IsNullOrEmpty(unreadable));
    }

    // ── 5 / 6: OAuth state + reconnect ──────────────────────────────────────

    private static AccOAuthState NewState(IDataProtectionProvider dp, IReplayGuard guard) => new(dp, guard);

    [Fact]
    public async Task OAuth_state_is_sealed_single_use_and_tamper_evident()
    {
        var dp = new EphemeralDataProtectionProvider();
        var guard = new TestReplayGuard();
        var s = NewState(dp, guard);
        var t = Guid.NewGuid(); var p = Guid.NewGuid(); var u = Guid.NewGuid();

        var state = s.Issue(t, p, u);
        Assert.DoesNotContain(t.ToString("N"), state);            // not the old readable string

        var (payload, err) = await s.RedeemAsync(state, default);
        Assert.Equal(AccOAuthState.RedeemError.None, err);
        Assert.Equal((t, p, u), (payload!.TenantId, payload.ProjectId, payload.UserId));

        Assert.Equal(AccOAuthState.RedeemError.Replayed, (await s.RedeemAsync(state, default)).Error);

        var tampered = state[..^4] + (state[^4] == 'A' ? "B" : "A") + state[^3..];
        Assert.Equal(AccOAuthState.RedeemError.Invalid, (await s.RedeemAsync(tampered, default)).Error);

        // The legacy forgeable shape is rejected outright.
        Assert.Equal(AccOAuthState.RedeemError.Invalid,
            (await s.RedeemAsync($"{t:N}.{u:N}.{p:N}.{Guid.NewGuid():N}", default)).Error);

        // A state minted by a different key ring (another deployment) is invalid.
        var foreign = NewState(new EphemeralDataProtectionProvider(), guard).Issue(t, p, u);
        Assert.Equal(AccOAuthState.RedeemError.Invalid, (await s.RedeemAsync(foreign, default)).Error);
    }

    [Fact]
    public async Task OAuth_replay_store_outage_fails_closed()
    {
        var guard = new TestReplayGuard();
        var s = NewState(new EphemeralDataProtectionProvider(), guard);
        var state = s.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        guard.SimulateOutage = true;
        await Assert.ThrowsAnyAsync<Exception>(() => s.RedeemAsync(state, default));
    }

    [Fact]
    public async Task Callback_reconnect_updates_the_existing_row_without_a_tenant_context()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accIssueMap\":{\"i1\":\"acc-1\"}}", access: "old-a", refresh: "old-r");
        fx.Http.Respond = (req, body) => req.RequestUri!.AbsolutePath.EndsWith("/authentication/v2/token")
            ? Json(HttpStatusCode.OK, new { access_token = "new-a", refresh_token = "new-r", expires_in = 3600 })
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        var dp = new EphemeralDataProtectionProvider();
        var st = NewState(dp, new TestReplayGuard());
        var state = st.Issue(fx.TenantId, fx.ProjectId, Guid.NewGuid());

        using var anon = fx.Db(withTenant: false);                 // callback is anonymous
        var ctl = new AccOAuthController(anon, new Factory(fx.Http), fx.Config, st, NullLogger<AccOAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var result = await ctl.Callback("the-code", state, default);
        Assert.IsType<ContentResult>(result);

        using var db = fx.Db();
        var rows = await db.PlatformConnections.AsNoTracking().Where(c => c.ProjectId == fx.ProjectId).ToListAsync();
        var row = Assert.Single(rows);                              // updated, not a second row
        Assert.Equal("new-r", row.RefreshToken);
        Assert.Contains("acc-1", row.ConfigJson);                   // mapping kept across reconnect

        // Same state again: replay refused.
        var again = await ctl.Callback("another-code", state, default);
        Assert.IsType<BadRequestObjectResult>(again);
    }

    [Fact]
    public async Task Callback_does_not_echo_the_APS_error_body()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        fx.Http.Respond = (_, _) => Json(HttpStatusCode.BadRequest, new { error = "invalid_grant", developerMessage = "SECRET-DIAGNOSTIC" });
        var st = NewState(new EphemeralDataProtectionProvider(), new TestReplayGuard());
        using var anon = fx.Db(withTenant: false);
        var ctl = new AccOAuthController(anon, new Factory(fx.Http), fx.Config, st, NullLogger<AccOAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var r = Assert.IsType<ObjectResult>(await ctl.Callback("c", st.Issue(fx.TenantId, fx.ProjectId, Guid.NewGuid()), default));
        Assert.Equal(502, r.StatusCode);
        Assert.DoesNotContain("SECRET-DIAGNOSTIC", JToken.FromObject(r.Value!).ToString());
    }

    // ── 5 / 7 / 11: role gates ──────────────────────────────────────────────

    private static ClaimsPrincipal User(Guid tenant, Guid user, string tenantRole = "Viewer")
        => new(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenant.ToString()),
            new Claim("user_id", user.ToString()),
            new Claim("sub", user.ToString()),
            new Claim("role", tenantRole),
        }, "test"));

    private static async Task<Guid> AddMemberAsync(Fx fx, string projectRole)
    {
        var uid = Guid.NewGuid();
        using var db = fx.Db();
        db.ProjectMembers.Add(new ProjectMember { TenantId = fx.TenantId, ProjectId = fx.ProjectId, UserId = uid, ProjectRole = projectRole, IsActive = true });
        await db.SaveChangesAsync();
        return uid;
    }

    [Theory]
    [InlineData("Viewer", false)]
    [InlineData("ClientGuest", false)]
    [InlineData("Contributor", true)]
    [InlineData("Manager", true)]
    public async Task Shared_ACC_token_is_released_only_to_author_roles(string role, bool allowed)
    {
        var fx = new Fx();
        await fx.SeedAsync();
        var uid = await AddMemberAsync(fx, role);
        using var db = fx.Db();
        var ctl = new AccController(fx.Service(db), db, NullLogger<AccController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(fx.TenantId, uid) } },
        };
        var r = await ctl.GetToken(fx.ProjectId, default);
        if (allowed)
        {
            var ok = Assert.IsType<OkObjectResult>(r);
            Assert.Contains("a1", JToken.FromObject(ok.Value!).ToString());
        }
        else
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
        }
    }

    [Theory]
    [InlineData("Contributor", false)]
    [InlineData("Coordinator", false)]
    [InlineData("Manager", true)]
    public async Task Connect_and_disconnect_need_the_administer_capability(string role, bool allowed)
    {
        var fx = new Fx();
        await fx.SeedAsync();
        var uid = await AddMemberAsync(fx, role);
        using var db = fx.Db();
        var ctl = new AccOAuthController(db, new Factory(fx.Http), fx.Config,
            NewState(new EphemeralDataProtectionProvider(), new TestReplayGuard()), NullLogger<AccOAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(fx.TenantId, uid) } },
        };
        var gate = await ctl.RequireAdministerAsync(fx.ProjectId, default);
        if (allowed) Assert.Null(gate);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(gate).StatusCode);

        var disc = await ctl.Disconnect(fx.ProjectId, default);
        if (allowed) Assert.IsType<NoContentResult>(disc);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(disc).StatusCode);
    }

    [Fact]
    public void Write_capability_expression_agrees_with_in_memory_form()
    {
        var pred = ProjectRoles.CanWriteProjectPredicate.Compile();
        foreach (var role in ProjectRoles.All.Append("PM").Append("Nonsense"))
            Assert.Equal(ProjectRoles.CanWrite(role), pred(new ProjectMember { ProjectRole = role }));
        Assert.False(ProjectRoles.CanWrite(ProjectRoles.Viewer));
        Assert.False(ProjectRoles.CanWrite(ProjectRoles.ClientGuest));
    }

    // ── 15: webhook signature ───────────────────────────────────────────────

    [Fact]
    public void Webhook_signature_is_sha1hash_HMAC_SHA1_over_the_raw_body()
    {
        var body = Encoding.UTF8.GetBytes("{\"hook\":{\"event\":\"dm.version.added\"},\"resourceUrn\":\"urn:x\"}");
        var sig = "sha1hash=" + Convert.ToHexString(HMACSHA1.HashData(Encoding.UTF8.GetBytes("s3cret"), body)).ToLowerInvariant();

        Assert.True(AutodeskWebhooksController.VerifySignature(body, "s3cret", sig));
        Assert.True(AutodeskWebhooksController.VerifySignature(body, "s3cret", sig.ToUpperInvariant().Replace("SHA1HASH=", "sha1hash=")));
        Assert.False(AutodeskWebhooksController.VerifySignature(body, "wrong", sig));
        Assert.False(AutodeskWebhooksController.VerifySignature(body.Append((byte)' ').ToArray(), "s3cret", sig));
        // The old scheme (sha256=, HMAC-SHA256) is not accepted.
        var old = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cret"), body)).ToLowerInvariant();
        Assert.False(AutodeskWebhooksController.VerifySignature(body, "s3cret", old));
        Assert.False(AutodeskWebhooksController.VerifySignature(body, "s3cret", null));
        Assert.False(AutodeskWebhooksController.VerifySignature(body, "s3cret", "sha1hash=zz"));
    }
}
