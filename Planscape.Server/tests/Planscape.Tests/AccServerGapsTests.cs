using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Planscape.API.Controllers;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.Tests;

/// <summary>
/// ACC-SRV-5..8 + reconnect listing: APS secret create/update fallback, event
/// validation, 409 hook adoption, default and browsable DM folders, atomic
/// webhook delivery claims, DataProtection key encryption at rest.
/// </summary>
public partial class AccServerIntegrationTests
{
    // ── ACC-SRV-5: secret token create ↔ update ─────────────────────────────

    /// <summary>APS stub with scripted secret responses; hooks 201 + Location; topFolders from <paramref name="topFolders"/>.</summary>
    private static void StubAps(Handler h, HttpStatusCode post, HttpStatusCode put,
        Func<string, HttpStatusCode>? createStatus = null, Func<HttpRequestMessage, HttpResponseMessage?>? extra = null)
    {
        int n = 0;
        h.Respond = (req, _) =>
        {
            var hit = extra?.Invoke(req);
            if (hit != null) return hit;
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path == "/webhooks/v1/tokens") return new HttpResponseMessage(post);
            if (req.Method == HttpMethod.Put && path == "/webhooks/v1/tokens/@me") return new HttpResponseMessage(put);
            if (req.Method == HttpMethod.Post && path.EndsWith("/hooks"))
            {
                var code = createStatus?.Invoke(path) ?? HttpStatusCode.Created;
                var resp = new HttpResponseMessage(code);
                if (code == HttpStatusCode.Created)
                    resp.Headers.Location = new Uri($"https://aps.test{path}/hook-{Interlocked.Increment(ref n)}");
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    private static IConfiguration HookCfg(Fx fx, params (string Key, string? Value)[] extra)
    {
        var d = new Dictionary<string, string?> { ["Autodesk:WebhookSecret"] = HookSecret };
        foreach (var (k, v) in extra) d[k] = v;
        return WithSettings(fx.Config, d);
    }

    [Fact]
    public async Task Secret_create_answering_already_exists_falls_back_to_update_and_records_it()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.BadRequest, put: HttpStatusCode.NoContent);
        using (var db = fx.Db())
        {
            var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, Array.Empty<string>(), default);
            Assert.Equal(AccSyncService.StatusOk, r.Status);
            Assert.Equal("PUT /webhooks/v1/tokens/@me (updated)", r.SecretSetBy);
        }
        // POST first, then PUT, both with our secret, both before any hook.
        var secretCalls = fx.Http.Calls.Where(c => c.Url.Contains("/webhooks/v1/tokens")).ToList();
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Put }, secretCalls.Select(c => c.Method));
        Assert.All(secretCalls, c => Assert.Equal(HookSecret, (string?)JObject.Parse(c.Body!)["token"]));
        var cfg = JObject.Parse((await fx.ReadConnAsync()).ConfigJson!);
        Assert.Equal("PUT /webhooks/v1/tokens/@me (updated)", (string?)cfg[AccWebhookService.KeySecretSetBy]!["method"]);
        // Server-owned: a client PUT cannot drop it.
        var (merged, _) = AccSyncService.MergeClientConfig(cfg.ToString(), "{}");
        Assert.NotNull(JObject.Parse(merged!)[AccWebhookService.KeySecretSetBy]);
    }

    [Fact]
    public async Task Secret_create_succeeding_does_not_call_update()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.InternalServerError);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, Array.Empty<string>(), default);
        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.Equal("POST /webhooks/v1/tokens (created)", r.SecretSetBy);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Secret_when_neither_call_works_fails_with_both_codes_and_creates_no_hook()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.NotFound, put: HttpStatusCode.NotFound);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, null, default);
        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Contains("HTTP 404", r.Errors[0]);
        Assert.Contains("POST tokens", r.Errors[0]);
        Assert.Contains("PUT tokens/@me", r.Errors[0]);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Url.EndsWith("/hooks"));
    }

    [Fact]
    public async Task Secret_create_forbidden_is_not_retried_as_an_update()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.Forbidden, put: HttpStatusCode.NoContent);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, null, default);
        Assert.Equal(AccSyncService.StatusFailed, r.Status);
        Assert.Contains("data:write", r.Errors[0]);
        Assert.DoesNotContain(fx.Http.Calls, c => c.Method == HttpMethod.Put);
    }

    // ── ACC-SRV-5: event names validated against the APS list ───────────────

    [Fact]
    public void Event_names_are_checked_against_the_APS_event_list()
    {
        var (valid, unknown) = AccWebhookService.ResolveEvents(Cfg(), AccWebhookService.SystemData, AccWebhookService.DataEvents);
        Assert.Equal(AccWebhookService.DataEvents, valid);   // both defaults are in the spec's enum
        Assert.Empty(unknown);
        (valid, unknown) = AccWebhookService.ResolveEvents(Cfg(), AccWebhookService.SystemIssues, AccWebhookService.IssueEvents);
        Assert.Equal(AccWebhookService.IssueEvents, valid);
        Assert.Empty(unknown);

        (valid, unknown) = AccWebhookService.ResolveEvents(
            Cfg(("Autodesk:WebhookEvents:data", "dm.version.added, dm.version.bogus ,dm.folder.added")),
            AccWebhookService.SystemData, AccWebhookService.DataEvents);
        Assert.Equal(new[] { "dm.version.added", "dm.folder.added" }, valid);
        Assert.Equal(new[] { "dm.version.bogus" }, unknown);
    }

    [Fact]
    public async Task An_unknown_configured_event_is_reported_and_the_rest_still_subscribe()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent);
        var cfg = HookCfg(fx, ("Autodesk:WebhookEvents:data", "dm.version.added,dm.version.bogus"));
        using var db = fx.Db();
        var r = await Hooks(fx, db, cfg).SubscribeAsync(fx.ProjectId, new[] { "urn:f1" }, default);
        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.Contains(r.Warnings!, w => w.Contains("dm.version.bogus"));
        Assert.DoesNotContain(fx.Http.Calls, c => c.Url.Contains("dm.version.bogus"));
        Assert.Contains(r.Hooks, h => h.Event == "dm.version.added" && h.ScopeValue == "urn:f1");
        Assert.Equal(3, r.Hooks.Count);   // 2 issue + 1 DM
    }

    // ── ACC-SRV-5: 409 "hook already exists" → adopt when it is ours ────────

    [Fact]
    public async Task Hook_conflict_adopts_the_existing_hook_when_its_callback_is_ours()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        string ours = $"https://planscape.test/api/webhooks/autodesk/event?connectionId={fx.ConnId}";
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent,
            createStatus: p => p.Contains("issue.created") ? HttpStatusCode.Conflict : HttpStatusCode.Created,
            extra: req =>
            {
                if (req.Method != HttpMethod.Get || !req.RequestUri!.AbsolutePath.EndsWith("/issue.created-1.0/hooks")) return null;
                var q = Uri.UnescapeDataString(req.RequestUri.Query);
                if (!q.Contains("scopeName=project") || !q.Contains("scopeValue=acc-proj")) return new HttpResponseMessage(HttpStatusCode.BadRequest);
                if (!q.Contains("pageState"))
                    return Json(HttpStatusCode.OK, new
                    {
                        links = new { next = "/systems/autodesk.construction.issues/events/issue.created-1.0/hooks?pageState=P2&scopeName=project&scopeValue=acc-proj" },
                        data = new[] { new { hookId = "someone-else", callbackUrl = "https://other.example/cb" } },
                    });
                return Json(HttpStatusCode.OK, new { links = new { next = (string?)null }, data = new[] { new { hookId = "existing-1", callbackUrl = ours } } });
            });
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, Array.Empty<string>(), default);
        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.Contains(r.Hooks, h => h.Event == "issue.created-1.0" && h.HookId == "existing-1");
        Assert.Contains(r.Warnings!, w => w.Contains("adopted hook existing-1"));
        Assert.Contains(fx.Http.Calls, c => c.Url.Contains("pageState=P2"));   // second page followed
    }

    [Fact]
    public async Task Hook_conflict_with_a_foreign_callback_is_reported_not_recorded()
    {
        var fx = new Fx();
        await fx.SeedAsync();
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent,
            createStatus: p => p.Contains("issue.created") ? HttpStatusCode.Conflict : HttpStatusCode.Created,
            extra: req => req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/hooks")
                ? Json(HttpStatusCode.OK, new { data = new[] { new { hookId = "theirs", callbackUrl = "https://other.example/cb" } } })
                : null);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, Array.Empty<string>(), default);
        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        Assert.Contains(r.Errors, e => e.Contains("already exists"));
        Assert.DoesNotContain(r.Hooks, h => h.HookId == "theirs");
    }

    // ── ACC-SRV-6: default top folders + folder browser ─────────────────────

    private static HttpResponseMessage? TopFoldersStub(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (req.Method != HttpMethod.Get) return null;
        if (path == "/project/v1/hubs/b.hub-1/projects/b.acc-proj/topFolders")
            return Json(HttpStatusCode.OK, new
            {
                data = new object[]
                {
                    new { type = "folders", id = "urn:adsk.wipprod:fs.folder:co.PF", attributes = new { name = "Project Files", displayName = "Project Files", hidden = false, objectCount = 4 } },
                    new { type = "folders", id = "urn:adsk.wipprod:fs.folder:co.HID", attributes = new { name = "hidden-sys", hidden = true } },
                    new { type = "items", id = "urn:item", attributes = new { name = "not a folder" } },
                },
            });
        return null;
    }

    [Fact]
    public async Task Subscribe_without_folders_defaults_to_the_project_top_folders_and_reports_them()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accIssueSubtypeId\":\"sub-1\",\"accHubId\":\"b.hub-1\"}");
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent, extra: TopFoldersStub);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, null, default);

        Assert.Equal(AccSyncService.StatusOk, r.Status);
        Assert.Equal("project top folders", r.FolderSource);
        Assert.Equal(new[] { "urn:adsk.wipprod:fs.folder:co.PF" }, r.Folders);            // hidden + items excluded
        var top = Assert.Single(fx.Http.Calls, c => c.Url.Contains("/topFolders"));
        Assert.Contains("projectFilesOnly=true", top.Url);
        Assert.Equal(2, r.Hooks.Count(h => h.System == AccWebhookService.SystemData && h.ScopeValue == "urn:adsk.wipprod:fs.folder:co.PF"));
    }

    [Fact]
    public async Task Subscribe_without_folders_and_without_a_hub_still_creates_issue_hooks_and_says_why_not_DM()
    {
        var fx = new Fx();
        await fx.SeedAsync();   // no accHubId
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).SubscribeAsync(fx.ProjectId, null, default);
        Assert.Equal(AccSyncService.StatusPartial, r.Status);
        Assert.Contains(r.Errors, e => e.Contains("hub") && e.Contains("folderUrns"));
        Assert.Equal(2, r.Hooks.Count);
        Assert.All(r.Hooks, h => Assert.Equal(AccWebhookService.SystemIssues, h.System));
    }

    [Fact]
    public async Task Folder_browser_lists_top_folders_and_follows_subfolder_pages()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accHubId\":\"b.hub-1\"}");
        StubAps(fx.Http, post: HttpStatusCode.OK, put: HttpStatusCode.NoContent, extra: req =>
        {
            var hit = TopFoldersStub(req);
            if (hit != null) return hit;
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (!url.Contains("/data/v1/projects/b.acc-proj/folders/urn:adsk.wipprod:fs.folder:co.PF/contents")) return null;
            Assert.Contains("filter[type]=folders", url);
            return url.Contains("page[number]=1")
                ? Json(HttpStatusCode.OK, new { links = new { }, data = new[] { new { type = "folders", id = "urn:sub-2", attributes = new { displayName = "Sub 2" } } } })
                : Json(HttpStatusCode.OK, new
                {
                    links = new { next = new { href = "https://aps.test/data/v1/projects/b.acc-proj/folders/urn:adsk.wipprod:fs.folder:co.PF/contents?filter[type]=folders&page[number]=1" } },
                    data = new[] { new { type = "folders", id = "urn:sub-1", attributes = new { displayName = "Sub 1" } } },
                });
        });

        using var db = fx.Db();
        var hooks = Hooks(fx, db, HookCfg(fx));
        var top = await hooks.ListFoldersAsync(fx.ProjectId, null, default);
        Assert.Equal(AccSyncService.StatusOk, top.Status);
        Assert.Equal(2, top.Folders.Count);                                     // hidden one listed, flagged
        Assert.Contains(top.Folders, f => f.Hidden && f.Urn.EndsWith("HID"));
        Assert.DoesNotContain(fx.Http.Calls, c => c.Url.Contains("projectFilesOnly=true"));   // browsing shows everything

        var sub = await hooks.ListFoldersAsync(fx.ProjectId, "urn:adsk.wipprod:fs.folder:co.PF", default);
        Assert.Equal(AccSyncService.StatusOk, sub.Status);
        Assert.Equal(new[] { "Sub 1", "Sub 2" }, sub.Folders.Select(f => f.Name));
    }

    [Fact]
    public async Task Folder_browser_with_undecryptable_tokens_is_RECONNECT_REQUIRED_and_calls_nothing()
    {
        var fx = new Fx();
        await fx.SeedAsync(configJson: "{\"accHubId\":\"b.hub-1\"}", access: Garbage, refresh: Garbage);
        using var db = fx.Db();
        var r = await Hooks(fx, db, HookCfg(fx)).ListFoldersAsync(fx.ProjectId, null, default);
        Assert.Equal(AccSyncService.StatusReconnect, r.Status);
        Assert.Empty(fx.Http.Calls);
    }

    // ── ACC-SRV-7: atomic delivery claim ────────────────────────────────────

    [Fact]
    public async Task Two_parallel_deliveries_of_one_id_are_processed_once()
    {
        var fx = new Fx();
        var (docA, _, _, _, _) = await SeedTwoTenantsAsync(fx, "urn:v1", sameAccProject: false);
        var rx = new Receiver(fx);

        for (int round = 0; round < 5; round++)
        {
            string id = $"par-{round}";
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => rx.PostAsync(VersionAdded("urn:v1"), fx.ConnId, deliveryId: id))));
            Assert.Equal(1, results.Count(x => x.Body!["duplicate"] == null));
            Assert.Equal(7, results.Count(x => (bool?)x.Body!["duplicate"] == true));
        }
        Assert.Equal(5, rx.Hub.Sends.Count);   // one broadcast per delivery id
        Assert.NotNull((await ReadDocAsync(fx, docA)).UpdatedAt);
    }

    [Fact]
    public async Task Delivery_claims_are_atomic_on_a_relational_database_and_releasable()
    {
        string file = Path.Combine(Path.GetTempPath(), $"aps-deliveries-{Guid.NewGuid():N}.db");
        string cs = $"Data Source={file};Pooling=False";
        try
        {
            PlanscapeDbContext Ctx() => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseSqlite(cs).Options);
            using (var db = Ctx()) db.Database.EnsureCreated();

            // 16 contexts on 16 connections racing for one id: exactly one wins.
            var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                using var db = Ctx();
                return await new ApsWebhookDeliveryGuard(db, NullLogger<ApsWebhookDeliveryGuard>.Instance).TryClaimAsync("race-1", default);
            })));
            Assert.Equal(1, claims.Count(c => c == ApsWebhookDeliveryGuard.Claim.Claimed));
            Assert.Equal(15, claims.Count(c => c == ApsWebhookDeliveryGuard.Claim.Duplicate));

            using (var db = Ctx())
            {
                var g = new ApsWebhookDeliveryGuard(db, NullLogger<ApsWebhookDeliveryGuard>.Instance);
                await g.ReleaseAsync("race-1", default);                                    // processing failed
                Assert.Equal(ApsWebhookDeliveryGuard.Claim.Claimed, await g.TryClaimAsync("race-1", default));   // redelivery applies
                Assert.Equal(ApsWebhookDeliveryGuard.Claim.Duplicate, await g.TryClaimAsync("race-1", default));
                Assert.Equal(1, await db.ApsWebhookDeliveries.CountAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    // ── ACC-SRV-8: DataProtection key encryption at rest ────────────────────

    private static (string Base64, X509Certificate2 Cert) MakePfx(string password, string cn = "CN=planscape-dp-test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return (Convert.ToBase64String(cert.Export(X509ContentType.Pfx, password)), cert);
    }

    [Fact]
    public void Key_encryption_selection_covers_none_valid_and_every_broken_configuration()
    {
        var none = DataProtectionKeyStore.SelectKeyEncryption(Cfg());
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.None, none.Kind);
        Assert.Null(none.Certificate);

        var (b64, cert) = MakePfx("pw");
        var ok = DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", b64), ("DataProtection:CertificatePassword", "pw")));
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Certificate, ok.Kind);
        Assert.Equal(cert.Thumbprint, ok.Certificate!.Thumbprint);
        Assert.Contains(cert.Thumbprint, ok.Description);

        var wrongPw = DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", b64), ("DataProtection:CertificatePassword", "nope")));
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid, wrongPw.Kind);
        Assert.Contains("CertificatePassword", wrongPw.Problem);

        var notB64 = DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", "%%%not base64%%%")));
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid, notB64.Kind);
        Assert.Contains("base64", notB64.Problem);

        // Public certificate only: keys encrypted with it could never be read back.
        var publicOnly = Convert.ToBase64String(cert.Export(X509ContentType.Cert));
        var noKey = DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", publicOnly)));
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid, noKey.Kind);
        Assert.Contains("private key", noKey.Problem);

        using var ec = ECDsa.Create();
        var ecCert = new CertificateRequest("CN=ec", ec, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var notRsa = DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", Convert.ToBase64String(ecCert.Export(X509ContentType.Pfx, "pw"))),
            ("DataProtection:CertificatePassword", "pw")));
        Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid, notRsa.Kind);
        Assert.Contains("RSA", notRsa.Problem);

        if (!OperatingSystem.IsWindows())
            Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid,
                DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateThumbprint", "ABCDEF"))).Kind);
        else
            Assert.Equal(DataProtectionKeyStore.KeyEncryptionKind.Invalid,   // no such cert in the store
                DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateThumbprint", "00112233445566778899AABBCCDDEEFF00112233"))).Kind);
    }

    [Fact]
    public void Missing_key_encryption_in_Production_logs_a_WARNING_naming_the_setting_and_broken_config_an_ERROR()
    {
        var log = new CapturingLogger();
        DataProtectionKeyStore.LogKeyEncryption(log, DataProtectionKeyStore.SelectKeyEncryption(Cfg()), isProduction: true);
        var line = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, line.Level);
        Assert.Contains("DataProtection__CertificateBase64", line.Message);

        log.Entries.Clear();
        DataProtectionKeyStore.LogKeyEncryption(log, DataProtectionKeyStore.SelectKeyEncryption(Cfg()), isProduction: false);
        Assert.Empty(log.Entries);

        log.Entries.Clear();
        DataProtectionKeyStore.LogKeyEncryption(log,
            DataProtectionKeyStore.SelectKeyEncryption(Cfg(("DataProtection:CertificateBase64", "%%%"))), isProduction: true);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public void Keys_written_with_a_certificate_are_encrypted_at_rest_and_readable_after_rotation()
    {
        var dir = Directory.CreateTempSubdirectory("planscape-dp-").FullName;
        try
        {
            var (oldB64, _) = MakePfx("pw", "CN=old");
            var (newB64, _) = MakePfx("pw", "CN=new");

            IDataProtector Provider(params (string Key, string? Value)[] kv)
            {
                var services = new ServiceCollection();
                var dp = services.AddDataProtection().SetApplicationName("planscape-test").PersistKeysToFileSystem(new DirectoryInfo(dir));
                var enc = DataProtectionKeyStore.SelectKeyEncryption(Cfg(kv));
                DataProtectionKeyStore.ApplyKeyEncryption(dp, enc);
                return services.BuildServiceProvider().GetDataProtector("acc-tokens");
            }

            string ciphertext = Provider(("DataProtection:CertificateBase64", oldB64), ("DataProtection:CertificatePassword", "pw")).Protect("refresh-token");

            var xml = File.ReadAllText(Assert.Single(Directory.GetFiles(dir, "*.xml")));
            Assert.Contains("encryptedSecret", xml);          // written through the certificate encryptor
            Assert.DoesNotContain("<value>", xml);            // no plaintext master key

            // Rotation: the new certificate encrypts, the old one is still accepted for reading.
            var rotated = Provider(("DataProtection:CertificateBase64", newB64), ("DataProtection:CertificatePassword", "pw"),
                ("DataProtection:PreviousCertificatesBase64", oldB64));
            Assert.Equal("refresh-token", rotated.Unprotect(ciphertext));

            // Without the certificate the ring cannot be read — the encryption is real.
            Assert.ThrowsAny<CryptographicException>(() => Provider().Unprotect(ciphertext));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // ── reconnect listing ───────────────────────────────────────────────────

    [Fact]
    public async Task Reconnect_list_names_undecryptable_connections_with_the_exact_fix_and_respects_the_admin_gate()
    {
        var fx = new Fx();
        await fx.SeedAsync(access: Garbage, refresh: Garbage);
        // A healthy connection on another project of the same tenant is not listed.
        Guid healthyProject = Guid.NewGuid();
        using (var db = fx.Db())
        {
            db.Projects.Add(new Project { Id = healthyProject, TenantId = fx.TenantId, Name = "Healthy", Code = "H" });
            db.PlatformConnections.Add(new PlatformConnection
            {
                TenantId = fx.TenantId, ProjectId = healthyProject, Platform = PlatformType.ACC, Name = "ok",
                ExternalProjectId = "x", IsActive = true, AccessToken = "a", RefreshToken = "r", TokenExpiresAt = DateTime.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        }

        async Task<JObject> ListAs(Guid uid, string tenantRole)
        {
            using var db = fx.Db();
            var ctl = new AccReconnectController(db)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(fx.TenantId, uid, tenantRole) } },
            };
            return JObject.FromObject(Assert.IsType<OkObjectResult>(await ctl.List(default)).Value!);
        }

        var admin = await ListAs(Guid.NewGuid(), "Admin");
        Assert.Equal(1, (int)admin["count"]!);
        var entry = (JObject)admin["connections"]![0]!;
        Assert.Equal(fx.ConnId, (Guid)entry["ConnectionId"]!);
        Assert.Contains("cannot be recovered", (string?)entry["Reason"]);
        Assert.Contains($"/api/acc/oauth/start?projectId={fx.ProjectId}", (string?)entry["Action"]);
        Assert.DoesNotContain(Garbage, admin.ToString());   // never echoes ciphertext

        Assert.Equal(1, (int)(await ListAs(await AddMemberAsync(fx, "Manager"), "Viewer"))["count"]!);
        Assert.Equal(0, (int)(await ListAs(await AddMemberAsync(fx, "Contributor"), "Viewer"))["count"]!);
    }

    [Fact]
    public async Task Reconnect_required_errors_tell_the_user_what_to_do()
    {
        var fx = new Fx();
        await fx.SeedAsync(access: Garbage, refresh: Garbage);
        StubAcc(fx.Http, okPosts: 10);
        using var db = fx.Db();
        var r = await fx.Service(db).SyncProjectAsync(fx.ProjectId);
        Assert.Equal(AccSyncService.StatusReconnect, r.Status);
        Assert.Contains("/api/acc/oauth/start?projectId=", r.Error);
        Assert.Contains("cannot be recovered", r.Error);
        Assert.True((await fx.ReadConnAsync()).LastSyncError!.Length <= 1000);
    }
}
