using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Document sync — the delta query
/// (<c>GET /api/projects/{id}/documents/changed-since</c>).
///
/// This is the reconnect fallback and the initial-link path. It is the only
/// server surface that decides what ends up written to an Author's local disk,
/// which is why the tests below care most about two things:
///
///   • it is never WIDER than the documents list a user can already see, and
///   • the timestamp contract actually works, because a Companion chains one
///     call's <c>serverTimeUtc</c> into the next call's <c>since</c> forever.
///     A subtle error there does not fail loudly — it silently stops syncing.
/// </summary>
public class DocumentChangedSinceTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public DocumentChangedSinceTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    private const string Base = "/api/projects/66666666-6666-6666-6666-666666666666/documents";

    // ── Initial sync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task No_since_returns_everything_currently_visible()
    {
        // The initial-link case from the spec: "since unset ⇒ everything
        // currently visible", not an empty delta.
        SeedDocument("CS-INITIAL-A.pdf", DateTime.UtcNow.AddDays(-30));
        SeedDocument("CS-INITIAL-B.pdf", DateTime.UtcNow.AddMinutes(-1));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = await FileNamesAsync(client, $"{Base}/changed-since");

        Assert.Contains("CS-INITIAL-A.pdf", names);
        Assert.Contains("CS-INITIAL-B.pdf", names);
    }

    // ── Delta semantics ───────────────────────────────────────────────────────

    [Fact]
    public async Task Since_excludes_documents_that_have_not_changed()
    {
        var old = SeedDocument("CS-OLD.pdf", DateTime.UtcNow.AddHours(-2));
        var cutoff = DateTime.UtcNow.AddHours(-1);
        SeedDocument("CS-NEW.pdf", DateTime.UtcNow.AddMinutes(-5));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = await FileNamesAsync(client, $"{Base}/changed-since?since={Iso(cutoff)}");

        Assert.Contains("CS-NEW.pdf", names);
        Assert.DoesNotContain("CS-OLD.pdf", names);
        Assert.NotEqual(Guid.Empty, old.Id);
    }

    [Fact]
    public async Task Since_uses_UpdatedAt_in_preference_to_UploadedAt()
    {
        // A document uploaded long ago but transitioned five minutes ago IS a
        // change the Companion must pull. Keying on UploadedAt alone would mean a
        // CDE transition never reaches a disk that was already in sync — the
        // single most likely way this endpoint could be quietly wrong.
        SeedDocument("CS-TRANSITIONED.pdf",
            uploadedAt: DateTime.UtcNow.AddDays(-10),
            updatedAt: DateTime.UtcNow.AddMinutes(-5));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = await FileNamesAsync(client,
            $"{Base}/changed-since?since={Iso(DateTime.UtcNow.AddHours(-1))}");

        Assert.Contains("CS-TRANSITIONED.pdf", names);
    }

    [Fact]
    public async Task Results_are_ordered_oldest_change_first()
    {
        // The Companion pages by taking the last item's changedAt as the next
        // `since`. That only works if the newest is last.
        SeedDocument("CS-ORDER-1.pdf", DateTime.UtcNow.AddMinutes(-30));
        SeedDocument("CS-ORDER-2.pdf", DateTime.UtcNow.AddMinutes(-20));
        SeedDocument("CS-ORDER-3.pdf", DateTime.UtcNow.AddMinutes(-10));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetJsonAsync(client,
            $"{Base}/changed-since?since={Iso(DateTime.UtcNow.AddHours(-1))}");
        var stamps = body.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("fileName").GetString()!.StartsWith("CS-ORDER-"))
            .Select(i => i.GetProperty("changedAt").GetDateTime())
            .ToList();

        Assert.Equal(3, stamps.Count);
        Assert.Equal(stamps.OrderBy(s => s), stamps);
    }

    [Fact]
    public async Task A_future_since_returns_nothing_rather_than_everything()
    {
        // Clock skew on a laptop is normal. "Since tomorrow" must be an empty
        // delta, not a fallback to the initial-sync behaviour — that would make a
        // skewed machine re-download the whole project on every reconnect.
        SeedDocument("CS-FUTURE.pdf", DateTime.UtcNow.AddMinutes(-5));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetJsonAsync(client,
            $"{Base}/changed-since?since={Iso(DateTime.UtcNow.AddDays(1))}");

        Assert.Equal(0, body.GetProperty("count").GetInt32());
    }

    // ── The timestamp contract ────────────────────────────────────────────────

    [Fact]
    public async Task Response_carries_a_server_clock_for_the_caller_to_chain()
    {
        // The Companion must never use its own clock to build the next `since`.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetJsonAsync(client, $"{Base}/changed-since");
        var serverTime = body.GetProperty("serverTimeUtc").GetDateTime();

        Assert.True(serverTime > DateTime.UtcNow.AddMinutes(-5),
            "serverTimeUtc should be roughly now");
        Assert.True(serverTime <= DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task Chaining_serverTimeUtc_into_the_next_since_loses_nothing()
    {
        // The actual loop the Companion runs, end to end: sync, remember the
        // server's clock, sync again, and a document written in between must
        // appear in the second call.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await GetJsonAsync(client, $"{Base}/changed-since");
        var checkpoint = first.GetProperty("serverTimeUtc").GetDateTime();

        SeedDocument("CS-CHAIN.pdf", DateTime.UtcNow.AddSeconds(5));

        var names = await FileNamesAsync(client, $"{Base}/changed-since?since={Iso(checkpoint)}");
        Assert.Contains("CS-CHAIN.pdf", names);
    }

    [Fact]
    public async Task hasMore_flags_a_full_page_so_the_caller_keeps_going()
    {
        // Without this the tail of a large backlog is silently dropped: the
        // Companion would take one page, store the newest timestamp and believe
        // it had caught up.
        for (var i = 0; i < 3; i++)
            SeedDocument($"CS-PAGE-{i}.pdf", DateTime.UtcNow.AddMinutes(-10 + i));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var full = await GetJsonAsync(client, $"{Base}/changed-since?limit=1");
        Assert.Equal(1, full.GetProperty("count").GetInt32());
        Assert.True(full.GetProperty("hasMore").GetBoolean());

        var roomy = await GetJsonAsync(client, $"{Base}/changed-since?limit=2000");
        Assert.False(roomy.GetProperty("hasMore").GetBoolean());
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Never_returns_a_document_the_documents_list_does_not()
    {
        // The invariant that actually matters, expressed as a comparison rather
        // than as a hard-coded expectation: sync writes files to disk, so its
        // surface must be a SUBSET of what the same caller can already see.
        //
        // Stated as a subset check on purpose. ProjectMemberAcl.ResolveAsync
        // currently hard-codes its three allow-list columns to null (a deliberate
        // migration-safety choice — see its comment), so no narrowing happens for
        // anyone today. Asserting "PUBLISHED is filtered out" would therefore be
        // asserting a behaviour that does not exist. This assertion holds now AND
        // keeps holding the day those columns are read for real, because both
        // endpoints route through the same helper.
        SeedDocument("CS-SUBSET-WIP.pdf", DateTime.UtcNow.AddMinutes(-5), cdeStatus: "WIP");
        SeedDocument("CS-SUBSET-PUB.pdf", DateTime.UtcNow.AddMinutes(-5), cdeStatus: "PUBLISHED");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var listed = await GetJsonAsync(client, $"{Base}?pageSize=500");
        var listedNames = listed.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("fileName").GetString()!).ToHashSet();

        var syncNames = await FileNamesAsync(client, $"{Base}/changed-since?limit=2000");

        Assert.NotEmpty(syncNames);
        Assert.All(syncNames, n => Assert.Contains(n, listedNames));
    }

    [Fact]
    public async Task Another_firm_cannot_read_the_delta()
    {
        // Two-firm isolation on the sync surface. The other tenant's admin is a
        // legitimate, authenticated user — they simply must not be able to enumerate
        // this project's documents by any route.
        SeedDocument("CS-ISOLATED.pdf", DateTime.UtcNow.AddMinutes(-5));
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");

        var res = await client.GetAsync($"{Base}/changed-since");

        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"expected the project to be invisible to another firm, got {(int)res.StatusCode}");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var res = await _factory.CreateClient().GetAsync($"{Base}/changed-since");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Iso(DateTime utc) =>
        Uri.EscapeDataString(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O"));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<string>> FileNamesAsync(HttpClient client, string url)
    {
        var body = await GetJsonAsync(client, url);
        return body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("fileName").GetString()!)
            .ToList();
    }

    private DocumentRecord SeedDocument(
        string fileName, DateTime uploadedAt, DateTime? updatedAt = null, string cdeStatus = "SHARED")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        var existing = db.Documents.FirstOrDefault(d => d.FileName == fileName);
        if (existing != null) return existing;

        var doc = new DocumentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ProjectId = TestData.ProjectId,
            FileName = fileName,
            DocumentType = "DR",
            CdeStatus = cdeStatus,
            SuitabilityCode = cdeStatus == "PUBLISHED" ? "S4" : "S3",
            Revision = "P01",
            UploadedBy = "Test Admin",
            UploadedAt = uploadedAt,
            UpdatedAt = updatedAt,
            ScanStatus = "CLEAN",
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }
}
