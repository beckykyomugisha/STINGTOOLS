using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// #552 — the approval gate credited a COMPLETED chain from one revision to every
/// revision after it.
///
/// <c>DocumentsController.CheckApprovalGate</c> scopes the LEGACY path by
/// <c>RevisionSnapshot</c>, but the CHAIN path had no such field, so it matched on
/// <c>(DocumentId, Transition)</c> alone. The two are OR'd, so the unscoped branch won:
/// a chain completed against P01 satisfied SHARED-&gt;PUBLISHED for P02 as well, while
/// the scoped legacy check beside it correctly refused. The bypass left a COMPLETED
/// chain in the audit trail, so it did not look like a bypass afterwards.
///
/// The worse framing, and the reason this is worth a test rather than a comment: it is
/// not "an approval is missing", it is "an approval for DIFFERENT CONTENT is being
/// credited to this content". Whoever approved P01 ends up recorded as having sanctioned
/// something they never saw.
///
/// These tests drive the real HTTP endpoint (<c>PUT .../documents/{id}/state</c>) rather
/// than the predicate in isolation, because the gate is only reached through the whole
/// transition path — role check, ACL, state machine — and a test that skipped those
/// could pass while the real call never got as far as the gate.
///
/// <para><b>Both halves of the NULL contract are asserted.</b> The legacy-NULL case is
/// not incidental compatibility, it is the deliberate backfill policy chosen in #552:
/// treating NULL as stale would close every historical hole the day it shipped and
/// simultaneously block every live document sitting on an already-completed chain. A
/// future "tidy-up" that makes NULL mean stale must fail
/// <see cref="Legacy_null_snapshot_chain_still_satisfies_the_gate"/> loudly.</para>
/// </summary>
public class ApprovalChainRevisionScopeTests : IClassFixture<PlanscapeWebApplicationFactory>
{
    private readonly PlanscapeWebApplicationFactory _factory;

    public ApprovalChainRevisionScopeTests(PlanscapeWebApplicationFactory factory)
        => _factory = factory;

    private static string StateUrl(Guid docId)
        => $"/api/projects/{TestData.ProjectId}/documents/{docId}/state";

    // ── The hole #552 describes ───────────────────────────────────────────────

    [Fact]
    public async Task Chain_completed_against_an_earlier_revision_does_NOT_satisfy_a_later_one()
    {
        // The exact scenario from the issue: approved at P01, reworked to P02,
        // published again with nobody having looked at P02.
        var doc = SeedDocument("AC-STALE-CHAIN.pdf", revision: "P02");
        SeedCompletedChain(doc.Id, revisionSnapshot: "P01");

        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PutAsJsonAsync(StateUrl(doc.Id),
            new { newState = "PUBLISHED", suitabilityCode = "S4", revision = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // Assert on the reason, not just the status — a 400 from the state machine
        // or the ACL would otherwise look identical to a 400 from the gate, and this
        // test would pass for the wrong reason.
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("requires an approved DocumentApproval", body);
    }

    [Fact]
    public async Task Chain_completed_against_the_CURRENT_revision_does_satisfy_the_gate()
    {
        // The other half. Without this, "scope the query" could be satisfied by a
        // predicate that rejects everything, and the suite would still be green
        // while approvals stopped working entirely.
        var doc = SeedDocument("AC-MATCHING-CHAIN.pdf", revision: "P02");
        SeedCompletedChain(doc.Id, revisionSnapshot: "P02");

        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PutAsJsonAsync(StateUrl(doc.Id),
            new { newState = "PUBLISHED", suitabilityCode = "S4", revision = (string?)null });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("PUBLISHED", CurrentStatus(doc.Id));
    }

    // ── The backfill policy, asserted as policy ───────────────────────────────

    [Fact]
    public async Task Legacy_null_snapshot_chain_still_satisfies_the_gate()
    {
        // Chains that completed before RevisionSnapshot existed carry NULL, and NULL
        // must keep meaning "matches any revision" — mirroring how DocumentApproval
        // has always handled its own null case.
        //
        // This is the test that stops this fix from becoming an outage. If NULL were
        // treated as stale, every document currently sitting on a completed chain
        // would need a fresh approval round before it could publish again — sprung on
        // live projects with no warning, on the day of a routine deploy.
        var doc = SeedDocument("AC-LEGACY-NULL-CHAIN.pdf", revision: "P07");
        SeedCompletedChain(doc.Id, revisionSnapshot: null);

        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PutAsJsonAsync(StateUrl(doc.Id),
            new { newState = "PUBLISHED", suitabilityCode = "S4", revision = (string?)null });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("PUBLISHED", CurrentStatus(doc.Id));
    }

    // ── The control: no chain at all ──────────────────────────────────────────

    [Fact]
    public async Task No_chain_and_no_approval_is_still_refused()
    {
        // Proves the gate is actually being exercised by this setup. Without it, a
        // scoping bug that made the query match nothing would be indistinguishable
        // from a scoping bug that made it match everything.
        var doc = SeedDocument("AC-NO-APPROVAL.pdf", revision: "P01");

        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.PutAsJsonAsync(StateUrl(doc.Id),
            new { newState = "PUBLISHED", suitabilityCode = "S4", revision = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── The stamp itself ──────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_chain_stamps_the_document_revision_at_that_moment()
    {
        // The snapshot is taken at OPEN, not at COMPLETED, because the point of the
        // field is to record what the approvers were looking at when the round began.
        var doc = SeedDocument("AC-STAMP-AT-CREATE.pdf", revision: "P03");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.PostAsJsonAsync(
            $"/api/projects/{TestData.ProjectId}/documents/{doc.Id}/approval-chain",
            new
            {
                transition = "SHARED->PUBLISHED",
                description = "revision stamp test",
                stages = new[]
                {
                    new { mode = "PARALLEL", label = "Review",
                          requiredApprovers = new[] { TestData.MemberUserId } }
                }
            });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        var chain = db.ApprovalChains.Single(c => c.DocumentId == doc.Id);

        // NOT-NULL is the assertion that matters here. A vacuous pass on this suite
        // would be a chain row that was never written at all, so prove the row exists
        // AND carries the revision.
        Assert.NotNull(chain.RevisionSnapshot);
        Assert.Equal("P03", chain.RevisionSnapshot);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    private DocumentRecord SeedDocument(string fileName, string revision)
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
            CdeStatus = "SHARED",
            SuitabilityCode = "S3",
            Revision = revision,
            UploadedBy = "Test Admin",
            UploadedAt = DateTime.UtcNow.AddDays(-1),
            ScanStatus = "CLEAN",
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }

    private void SeedCompletedChain(Guid docId, string? revisionSnapshot)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.ApprovalChains.Add(new ApprovalChain
        {
            Id = Guid.NewGuid(),
            TenantId = TestData.TenantId,
            ProjectId = TestData.ProjectId,
            DocumentId = docId,
            Transition = "SHARED->PUBLISHED",
            Status = "COMPLETED",
            CreatedBy = "Test Admin",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            CompletedAt = DateTime.UtcNow.AddDays(-1),
            RevisionSnapshot = revisionSnapshot,
        });
        db.SaveChanges();
    }

    private string CurrentStatus(Guid docId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        return db.Documents.Single(d => d.Id == docId).CdeStatus;
    }
}
