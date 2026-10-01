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

    // ── D8: a paged list that outruns its guard is INCOMPLETE, never a partial OK ──
    [Fact]
    public async Task A_DM_list_that_never_ends_is_incomplete_not_ok()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 0);
        int n = 0;
        fx.Http.Respond = (req, _) =>
        {
            int i = Interlocked.Increment(ref n);
            return Json(HttpStatusCode.OK, new
            {
                data = new[] { new { id = $"b.hub-{i}", attributes = new { name = $"Hub {i}" } } },
                links = new { next = new { href = $"https://aps.test/project/v1/hubs?page[number]={i + 1}" } },
            });
        };
        using var db = fx.Db();
        var (hubs, err) = await fx.Service(db).ListHubsAsync(fx.ProjectId, CancellationToken.None);
        Assert.Null(hubs);
        Assert.Contains("INCOMPLETE", err);
        Assert.Equal(100, n);
    }

    // ── D2: a create with no answer is verified, never re-posted blind ─────

    /// <summary>ACC stub where the issue POST times out; the createdAt search returns
    /// <paramref name="existing"/> (title → id), or fails when <paramref name="searchOk"/> is false.</summary>
    private static void StubTimeoutThenSearch(Handler h, Dictionary<string, string> existing, bool searchOk = true)
    {
        h.Respond = (req, _) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Post && url.EndsWith("/issues"))
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
            if (req.Method == HttpMethod.Get && url.Contains("filter[createdAt]="))
            {
                if (!searchOk) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                var results = existing.Select(kv => new { id = kv.Value, title = kv.Key }).ToArray();
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = results.Length }, results });
            }
            if (req.Method == HttpMethod.Get && url.Contains("filter[status]=open"))
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 1, offset = 0, totalResults = 0 }, results = new object[0] });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    [Fact]
    public async Task A_timed_out_create_is_reported_not_thrown_and_is_linked_once_ACC_shows_it()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubTimeoutThenSearch(fx.Http, new Dictionary<string, string>());
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);   // must not throw
        Assert.Equal(0, r.Pushed);
        Assert.Equal(1, r.Failed);
        Assert.Contains("outcome unknown", r.Failures![0]);
        var cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.Single((JObject)cfg[AccSyncService.KeyIssuePendingVerify]!);

        // The create had landed: the next sync finds it by title and links it — no second POST.
        StubTimeoutThenSearch(fx.Http, new Dictionary<string, string> { ["Issue 0"] = "acc-landed" });
        fx.Http.Calls.Clear();
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Post);
        cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.Contains(((JObject)cfg[AccSyncService.KeyIssueMap]!).Properties(), p => (string?)p.Value == "acc-landed");
        Assert.Empty((JObject)cfg[AccSyncService.KeyIssuePendingVerify]!);
    }

    [Fact]
    public async Task An_unclear_create_proven_absent_is_posted_again_and_an_unchecked_one_is_not()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubTimeoutThenSearch(fx.Http, new Dictionary<string, string>());
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        // ACC cannot be searched: the issue stays pending and is NOT posted.
        StubTimeoutThenSearch(fx.Http, new Dictionary<string, string>(), searchOk: false);
        fx.Http.Calls.Clear();
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Post);

        // Search works and shows nothing: proven absent, so it is posted again.
        StubAcc(fx.Http, okPosts: 10);
        var prev = fx.Http.Respond;
        fx.Http.Respond = (req, body) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Get && url.Contains("filter[createdAt]="))
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = 0 }, results = new object[0] });
            return prev(req, body);
        };
        fx.Http.Calls.Clear();
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(1, r.Pushed);
        Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]
    [InlineData(403, false)]
    public void Unclear_create_statuses(int status, bool unclear)
        => Assert.Equal(unclear, AccSyncService.IsUnclearCreateStatus(status));

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
        // E2: only what changed goes. acc-1 already reads "closed" in ACC (StubAcc's first id)
        // and Planscape closed it too, so there is no status to send; the description did
        // not change, so it is not sent either.
        Assert.Null(body["status"]);
        Assert.Null(body["description"]);
        Assert.Equal(0, r.Diverged);

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
        Assert.Null(body["status"]);                     // Planscape's status did not change: never sent
        Assert.Null(body["title"]);                      // nor did the title
        Assert.Equal("more detail", (string?)body["description"]);
    }

    // F3: a withheld status used to advance pushedAt, so the divergence was reported once and
    // then vanished, and Planscape's status was never sent even after ACC came back.
    [Fact]
    public async Task A_withheld_status_is_reported_every_sync_and_sent_once_ACC_returns()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);   // read-back reports acc-1 "closed": ACC moved it since the push
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        using (var db = fx.Db())
        {
            var i = await db.Issues.SingleAsync();
            i.Status = "RESOLVED"; i.UpdatedAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        AccSyncService.AccSyncReport r;
        fx.Http.Calls.Clear();
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(1, r.Diverged);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Patch);

        // Nothing changed: the divergence is still there, so it is still reported.
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(1, r.Diverged);

        // ACC goes back to "open" (what was pushed). The next read-back records it...
        var orig = fx.Http.Respond;
        fx.Http.Respond = (req, x) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Get && url.Contains("filter[id]="))
            {
                var ids = url.Substring(url.IndexOf("filter[id]=") + 11).Split('&')[0].Split(',');
                var results = ids.Select(id => new { id, status = "open" }).ToArray();
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = results.Length }, results });
            }
            return orig!(req, x);
        };
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        // ...and the sync after that sends Planscape's status.
        fx.Http.Calls.Clear();
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        var patch = Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Patch);
        Assert.Equal("completed", (string?)JObject.Parse(patch.Body!)["status"]);
        Assert.Equal(0, r.Diverged);
    }

    /// <summary>Wraps the ACC stub so every id read-back reports <paramref name="status"/>
    /// (or fails when null).</summary>
    private static void ReadBackAs(Handler h, string? status)
    {
        var inner = h.Respond;
        h.Respond = (req, x) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Get && url.Contains("filter[id]="))
            {
                if (status == null) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                var ids = url.Substring(url.IndexOf("filter[id]=") + 11).Split('&')[0].Split(',');
                var results = ids.Select(id => new { id, status }).ToArray();
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = results.Length }, results });
            }
            return inner(req, x);
        };
    }

    // F4: the status guard used the PREVIOUS sweep's read-back. ACC moving the issue since
    // then was invisible, and the PATCH overwrote the assignee's change.
    [Fact]
    public async Task A_status_is_judged_against_ACC_now_not_the_last_sweep()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);
        ReadBackAs(fx.Http, "open");                        // last sweep: ACC as pushed
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        using (var db = fx.Db())
        {
            var i = await db.Issues.SingleAsync();
            i.Status = "CLOSED"; i.UpdatedAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        ReadBackAs(fx.Http, "in_progress");                 // ...but the assignee has started it since
        fx.Http.Calls.Clear();
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Patch);
        Assert.Equal(1, r.Diverged);
    }

    [Fact]
    public async Task When_ACC_cannot_be_read_now_the_status_is_withheld_and_said()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);
        ReadBackAs(fx.Http, "open");
        using (var db = fx.Db()) await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        using (var db = fx.Db())
        {
            var i = await db.Issues.SingleAsync();
            i.Status = "CLOSED"; i.Title = "Retitled"; i.UpdatedAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        ReadBackAs(fx.Http, null);                          // the read fails
        fx.Http.Calls.Clear();
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        var body = JObject.Parse(Assert.Single(fx.Http.Calls, c => c.Method == HttpMethod.Patch).Body!);
        Assert.Null(body["status"]);                        // never sent blind
        Assert.Equal("Retitled", (string?)body["title"]);   // the rest still goes
        Assert.Contains(r.Failures!, f => f.Contains("status not sent"));
    }

    // AUT-3: a status read-back that stopped at its page cap fell through, and every mapped id
    // not yet read was recorded "not_found" - as if the ACC issue had been deleted.
    [Fact]
    public async Task A_read_back_that_hits_its_page_cap_is_an_error_not_not_found()
    {
        var fx = new Fx();
        await fx.SeedAsync(openIssues: 1);
        StubAcc(fx.Http, okPosts: 10);
        var inner = fx.Http.Respond;
        fx.Http.Respond = (req, x) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.Method == HttpMethod.Get && url.Contains("filter[id]="))
            {
                var page = Enumerable.Range(0, 100).Select(i => new { id = "other-" + Guid.NewGuid().ToString("N"), status = "open" }).ToArray();
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = 1_000_000 }, results = page });
            }
            return inner(req, x);
        };
        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Null(r.MappedStatusRead);                       // not "1 read, 1 not_found"
        Assert.Contains("INCOMPLETE", r.Error);
        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        var cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.DoesNotContain("not_found", cfg[AccSyncService.KeyIssueStatus]?.ToString() ?? "");
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

    // E2: the plan sends only what changed in Planscape and never undoes ACC's own status moves.
    [Theory]
    // previous Planscape status, new Planscape status, ACC's last status, expected status sent (null = none), withheld
    [InlineData("OPEN", "OPEN", "in_progress", null, false)]        // Planscape unchanged: ACC's in_progress kept
    [InlineData("OPEN", "CLOSED", "open", "closed", false)]         // ACC still as pushed: the close goes
    [InlineData("OPEN", "CLOSED", "in_progress", null, true)]       // ACC moved since: withheld, reported
    [InlineData("OPEN", "IN_PROGRESS", "open", "in_progress", false)] // IN_PROGRESS is its own status (was 'open')
    [InlineData("OPEN", "closed", "open", "closed", false)]         // case-insensitive
    [InlineData("OPEN", "RESOLVED", "completed", null, false)]      // ACC already agrees: nothing to send
    [InlineData("CLOSED", "OPEN", "closed", null, true)]            // never reopen an ACC close
    [InlineData("OPEN", "WEIRD", "open", null, false)]              // unknown status never sent
    public void The_update_plan_sends_status_only_when_safe(string prev, string now, string acc, string? sent, bool withheld)
    {
        var previous = AccIssueUpdatePlan.Snapshot.Of("t", "d", prev);
        var d = AccIssueUpdatePlan.Plan(previous, "t", "d", now, acc);
        Assert.Equal(sent, (string?)d.Body["status"]);
        Assert.Equal(withheld, d.StatusWithheld);
        Assert.Null(d.Body["title"]);
        Assert.Null(d.Body["description"]);
    }

    [Fact]
    public void Unchanged_text_is_not_resent_and_a_changed_title_is()
    {
        var previous = AccIssueUpdatePlan.Snapshot.Of("Duct clash L2", "Move the duct", "OPEN");
        Assert.Empty(AccIssueUpdatePlan.Plan(previous, "Duct clash L2", "Move the duct", "OPEN", "open").Body);
        var d = AccIssueUpdatePlan.Plan(previous, "Duct clash L2 (grid C4)", "Move the duct", "OPEN", "open");
        Assert.Equal("Duct clash L2 (grid C4)", (string?)d.Body["title"]);
        Assert.Single(d.Body);
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
        // A host shutdown cancels the run's token. (A cancellation with the token NOT cancelled
        // is an HttpClient timeout, which D2 handles as an unclear create instead.)
        using var shutdown = new CancellationTokenSource();
        fx.Http.Respond = (req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                if (Interlocked.Increment(ref posts) == 2)
                {
                    shutdown.Cancel();
                    throw new OperationCanceledException("host shutting down mid-run", shutdown.Token);
                }
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
            try { await svc.SyncConnectionAsync(conn, shutdown.Token); } catch { /* the crash */ }
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

    // D5: a long push outlived its token, and every later request was counted as a 401 rejection.
    [Fact]
    public async Task A_token_expiring_part_way_through_a_push_is_refreshed_not_failed()
    {
        var fx = new Fx();
        // Fresh at the start (5-minute buffer + 2 s), stale after the slow first POST.
        await fx.SeedAsync(openIssues: 2, expires: DateTime.UtcNow.AddMinutes(5).AddSeconds(2));
        int posts = 0, refreshes = 0;
        fx.Http.Respond = (req, body) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (req.RequestUri!.AbsolutePath.EndsWith("/authentication/v2/token"))
            {
                Interlocked.Increment(ref refreshes);
                return Json(HttpStatusCode.OK, new { access_token = "a2", refresh_token = "r2", expires_in = 3600 });
            }
            if (req.Method == HttpMethod.Post && url.EndsWith("/issues"))
            {
                int n = Interlocked.Increment(ref posts);
                if (n == 1) Thread.Sleep(3000);
                // A request still carrying the expired token would be refused.
                bool newToken = req.Headers.Authorization?.Parameter == "a2";
                return n == 1 || newToken
                    ? Json(HttpStatusCode.Created, new { id = $"acc-{n}" })
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            if (req.Method == HttpMethod.Get && url.Contains("filter[status]=open"))
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 1, offset = 0, totalResults = 2 }, results = new object[0] });
            if (req.Method == HttpMethod.Get && url.Contains("filter[id]="))
                return Json(HttpStatusCode.OK, new { pagination = new { limit = 100, offset = 0, totalResults = 0 }, results = new object[0] });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        AccSyncService.AccSyncReport r;
        using (var db = fx.Db()) r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);

        Assert.Equal(2, r.Pushed);
        Assert.Equal(0, r.Failed);
        Assert.True(refreshes >= 1);
        Assert.Equal("r2", (await fx.ReadConnAsync()).RefreshToken);
    }

    // D6: the rotation was saved under the request's abort token; a closed tab after APS rotated
    // left the database with the invalidated refresh token.
    [Fact]
    public async Task A_rotation_is_saved_even_when_the_caller_cancels_after_APS_rotated()
    {
        var fx = new Fx();
        await fx.SeedAsync(expires: DateTime.UtcNow.AddMinutes(-1));
        using var aborted = new CancellationTokenSource();
        StubToken(fx.Http, rt =>
        {
            aborted.Cancel();   // the user closes the tab while APS answers
            return Json(HttpStatusCode.OK, new { access_token = "a2", refresh_token = "r2", expires_in = 3600 });
        });

        using (var db = fx.Db())
        {
            try { await fx.Service(db).GetFreshAccessTokenAsync(fx.ProjectId, aborted.Token); }
            catch (OperationCanceledException) { /* the caller may see its own cancellation */ }
        }

        var stored = await fx.ReadConnAsync();
        Assert.Equal("r2", stored.RefreshToken);   // APS's rotation is not lost
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

    // AUT-6: creating a webhook needs data:create; the default grant now carries it.
    [Fact]
    public async Task The_default_ACC_grant_asks_for_data_create_so_webhooks_can_be_created()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        var uid = await AddMemberAsync(fx, "Manager");
        var cfg = new ConfigurationBuilder().AddConfiguration(fx.Config)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Acc:ClientId"] = "cid" }).Build();
        using var db = fx.Db();
        var ctl = new AccOAuthController(db, new Factory(fx.Http), cfg,
            NewState(new EphemeralDataProtectionProvider(), new TestReplayGuard()), NullLogger<AccOAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(fx.TenantId, uid) } },
        };
        var r = await ctl.Start(fx.ProjectId, default);
        var url = (string)JObject.FromObject(Assert.IsType<OkObjectResult>(r).Value!)["authorizeUrl"]!;
        var scope = Uri.UnescapeDataString(url.Split("scope=")[1].Split('&')[0]).Split(' ');
        Assert.Contains("data:read", scope);
        Assert.Contains("data:write", scope);
        Assert.Contains("data:create", scope);
        Assert.Equal(AccOAuthController.DefaultScopes.Split(' ').OrderBy(s => s), scope.OrderBy(s => s));
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
