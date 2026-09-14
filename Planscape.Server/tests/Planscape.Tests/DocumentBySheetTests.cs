using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// Sheet lookup — <c>GET /api/projects/{id}/documents/by-sheet</c>.
///
/// This is what a scanned title-block QR calls. Before it existed, the mobile
/// scanner could parse the code the plugin stamps and had nothing to ask: there
/// is no sheet entity on this server, so a sheet number lives only inside an ISO
/// 19650 document NAME.
///
/// Two things matter more than the matching itself, and both are about what a
/// person standing on a site holding a printed drawing is told:
///
///   • ORDER. A sheet number matches every revision ever issued. If a superseded
///     WIP drawing surfaced above the published one, the scan would be worse
///     than useless — it would be actively misleading.
///   • An EMPTY answer must stay empty. A fabricated row someone can act on is
///     worse than a blank screen, and this codebase has shipped that mistake.
/// </summary>
public class DocumentBySheetTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public DocumentBySheetTests(PlanscapeWebApplicationFactory factory) => _factory = factory;

    private const string Base = "/api/projects/66666666-6666-6666-6666-666666666666/documents";

    // ── Matching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_sheet_number_matches_the_document_whose_name_carries_it()
    {
        Seed("BS-PRJ-PLNS-ZZ-XX-DR-A-BSM101.pdf", "PUBLISHED", "P03", DateTime.UtcNow.AddDays(-1));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BSM101");

        Assert.Equal(1, body.GetProperty("matched").GetInt32());
        Assert.Contains("BSM101", Names(body)[0]);
    }

    [Fact]
    public async Task A_sheet_number_that_matches_nothing_returns_an_empty_list_not_a_placeholder()
    {
        // The whole point. An invented stand-in row would send someone to build
        // from a drawing that does not exist.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BS-NO-SUCH-SHEET-9999");

        Assert.Equal(0, body.GetProperty("matched").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("  ")]
    public async Task Too_short_a_query_is_refused_rather_than_matching_everything(string number)
    {
        // A one-character LIKE would return the entire register and present its
        // first row as "your sheet". Refusing is the honest answer.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync($"{Base}/by-sheet?number={Uri.EscapeDataString(number)}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── Ordering: what a person on site should build from ─────────────────────

    [Fact]
    public async Task Published_outranks_shared_which_outranks_wip()
    {
        Seed("BS-ORDER-WIP-BSM201.pdf", "WIP", "P01", DateTime.UtcNow);                    // newest
        Seed("BS-ORDER-SHARED-BSM201.pdf", "SHARED", "P02", DateTime.UtcNow.AddDays(-1));
        Seed("BS-ORDER-PUB-BSM201.pdf", "PUBLISHED", "C01", DateTime.UtcNow.AddDays(-5));  // oldest
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = Names(await GetAsync(client, $"{Base}/by-sheet?number=BSM201"));

        // The PUBLISHED drawing is the OLDEST here on purpose: if the sort keyed on
        // date first, the WIP one would come top and a site operative would build
        // from an unissued drawing.
        Assert.Equal("BS-ORDER-PUB-BSM201.pdf", names[0]);
        Assert.Equal("BS-ORDER-SHARED-BSM201.pdf", names[1]);
        Assert.Equal("BS-ORDER-WIP-BSM201.pdf", names[2]);
    }

    [Fact]
    public async Task Within_one_cde_state_the_newest_comes_first()
    {
        Seed("BS-NEWEST-BSM301.pdf", "PUBLISHED", "C02", DateTime.UtcNow);
        Seed("BS-OLDEST-BSM301.pdf", "PUBLISHED", "C01", DateTime.UtcNow.AddDays(-10));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = Names(await GetAsync(client, $"{Base}/by-sheet?number=BSM301"));

        Assert.Equal("BS-NEWEST-BSM301.pdf", names[0]);
    }

    [Fact]
    public async Task An_unknown_cde_state_sorts_last_not_first()
    {
        // An unrecognised status is not evidence of being authoritative. Sorting it
        // first would let a bad data row outrank a published drawing.
        Seed("BS-WEIRD-BSM401.pdf", "SOMETHING_ELSE", "X", DateTime.UtcNow);
        Seed("BS-PUB-BSM401.pdf", "PUBLISHED", "C01", DateTime.UtcNow.AddDays(-9));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var names = Names(await GetAsync(client, $"{Base}/by-sheet?number=BSM401"));

        Assert.Equal("BS-PUB-BSM401.pdf", names[0]);
    }

    // ── The scanned revision ──────────────────────────────────────────────────

    [Fact]
    public async Task A_matching_revision_narrows_the_answer_and_says_so()
    {
        Seed("BS-REV-P01-BSM501.pdf", "PUBLISHED", "P01", DateTime.UtcNow.AddDays(-3));
        Seed("BS-REV-P02-BSM501.pdf", "PUBLISHED", "P02", DateTime.UtcNow);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BSM501&revision=P01");

        Assert.True(body.GetProperty("revisionNarrowed").GetBoolean());
        Assert.Equal(1, body.GetProperty("matched").GetInt32());
        Assert.Equal("BS-REV-P01-BSM501.pdf", Names(body)[0]);
    }

    [Fact]
    public async Task A_revision_that_matches_nothing_keeps_the_wider_answer_and_flags_it()
    {
        // The printed drawing says P09; the register has never heard of P09. Narrowing
        // to nothing would tell a site operative the drawing does not exist, when the
        // truth is that the print in their hand is superseded — a different, and far
        // more useful, thing to be told. `revisionNarrowed: false` is how the client
        // knows to say so.
        Seed("BS-STALE-P01-BSM601.pdf", "PUBLISHED", "P01", DateTime.UtcNow);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BSM601&revision=P09");

        Assert.False(body.GetProperty("revisionNarrowed").GetBoolean());
        Assert.Equal(1, body.GetProperty("matched").GetInt32());
        Assert.Equal("P09", body.GetProperty("requestedRevision").GetString());
    }

    [Fact]
    public async Task No_revision_at_all_is_not_treated_as_a_failed_narrow()
    {
        // A QR stamped before a revision existed carries no ?r=. That must report
        // revisionNarrowed:false WITHOUT the client warning about a superseded print.
        // requestedRevision being null is how the two are told apart.
        Seed("BS-NOREV-BSM701.pdf", "PUBLISHED", "P01", DateTime.UtcNow);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BSM701");

        Assert.False(body.GetProperty("revisionNarrowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("requestedRevision").ValueKind);
    }

    // ── Scoping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_lookup_is_scoped_to_the_project_in_the_route()
    {
        // A sheet number is only unique within a project. BSM801 exists in another
        // project; asking this one must not find it.
        SeedInProject(Guid.Parse("77777777-7777-7777-7777-777777777777"),
                      "BS-OTHERPROJECT-BSM801.pdf", "PUBLISHED", "C01");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await GetAsync(client, $"{Base}/by-sheet?number=BSM801");

        Assert.Equal(0, body.GetProperty("matched").GetInt32());
    }

    [Fact]
    public async Task The_lookup_requires_authentication()
    {
        var anon = _factory.CreateClient();

        var res = await anon.GetAsync($"{Base}/by-sheet?number=BSM101");

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401/403 for an unauthenticated scan lookup, got {(int)res.StatusCode}");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private static List<string> Names(JsonElement body) =>
        body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("fileName").GetString()!)
            .ToList();

    private DocumentRecord Seed(string fileName, string cdeStatus, string revision, DateTime uploadedAt)
        => SeedInProject(TestData.ProjectId, fileName, cdeStatus, revision, uploadedAt);

    private DocumentRecord SeedInProject(
        Guid projectId, string fileName, string cdeStatus, string revision, DateTime? uploadedAt = null)
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
            ProjectId = projectId,
            FileName = fileName,
            DocumentType = "DR",
            CdeStatus = cdeStatus,
            SuitabilityCode = cdeStatus == "PUBLISHED" ? "S4" : "S3",
            Revision = revision,
            UploadedBy = "Test Admin",
            UploadedAt = uploadedAt ?? DateTime.UtcNow,
            ScanStatus = "CLEAN",
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }
}
