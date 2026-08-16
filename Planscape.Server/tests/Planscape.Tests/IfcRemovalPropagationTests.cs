using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Tests;

/// <summary>
/// TRACK C3 — deletions reaching the server.
///
/// THE DEFECT
/// ----------
/// The IFC ingest is an UPSERT over the elements it carries, so an element that
/// disappears from the source is simply not mentioned — indistinguishable from
/// "unchanged, partial push". A wall deleted in ArchiCAD therefore stayed on the
/// server forever: visible in the viewer, answering clash and compliance
/// queries, counted in every metric.
///
/// THE PART THAT MATTERS MOST
/// --------------------------
/// Removals are scoped to the host document that reports them. Two hosts
/// contribute to one project; if a full-export diff from the ArchiCAD file could
/// tombstone Revit-contributed elements, this feature would convert a
/// missing-delete bug into a data-loss one. <see cref="A_host_cannot_remove_another_hosts_elements"/>
/// is the test that keeps that honest.
/// </summary>
public class IfcRemovalPropagationTests
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

    private sealed record World(SqliteConnection Conn, Guid Tenant, Guid Project);

    private const string ArchiDoc = "doc-archicad";
    private const string RevitDoc = "doc-revit";
    private const string ArchiGuid = "1ArchiCadGuid00000000A";
    private const string RevitGuid = "2RevitGuid0000000000A";

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

            // One element from each host, each with its attribution mapping.
            foreach (var (guid, host, doc) in new[]
                     {
                         (ArchiGuid, "archicad", ArchiDoc),
                         (RevitGuid, "revit", RevitDoc),
                     })
            {
                ctx.TaggedElements.Add(new TaggedElement
                {
                    Id = Guid.NewGuid(), TenantId = tenant, ProjectId = project,
                    UniqueId = guid, IfcGlobalId = guid, Tag1 = "A", Disc = "A",
                });
                ctx.ExternalElementMappings.Add(new ExternalElementMapping
                {
                    Id = Guid.NewGuid(), TenantId = tenant, ProjectId = project,
                    Host = host, HostDocumentGuid = doc,
                    IfcGlobalId = guid, HostElementId = $"host-{guid}",
                });
            }
            ctx.SaveChanges();
        }
        return new World(conn, tenant, project);
    }

    private static IfcController NewController(World w, PlanscapeDbContext db)
    {
        var ctrl = new IfcController(db, identity: null!, ingest: null!,
                                     logger: NullLogger<IfcController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("tenant_id", w.Tenant.ToString()),
                        new Claim("user_id", Guid.NewGuid().ToString()),
                    }, "test")),
                },
            },
        };
        return ctrl;
    }

    private static IfcIngestRequest Removal(string host, string? doc, params string[] ids)
        => new()
        {
            Host = host,
            HostDocumentGuid = doc,
            Elements = new List<IfcElementDto>(),      // removals-only push
            RemovedGlobalIds = ids.ToList(),
        };

    private static async Task<bool> IsLiveAsync(World w, string guid)
    {
        using var ctx = NewContext(w.Conn, w.Tenant);
        // The global filter already hides soft-deleted rows; asking for it
        // explicitly documents what "live" means here.
        return await ctx.TaggedElements.AnyAsync(e => e.IfcGlobalId == guid && e.DeletedAtUtc == null);
    }

    // ── the fix ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_host_can_remove_an_element_it_contributed()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", ArchiDoc, ArchiGuid));

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.False(await IsLiveAsync(w, ArchiGuid));
        }
    }

    [Fact]
    public async Task A_removals_only_push_is_accepted()
    {
        // A save whose only change was deleting elements has nothing to upsert.
        // Rejecting it as "Elements is empty" would drop exactly the deletions
        // this feature exists to deliver.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", ArchiDoc, ArchiGuid));

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var body = Assert.IsType<IfcIngestResponse>(ok.Value);
            Assert.Equal(1, body.Removed);
        }
    }

    [Fact]
    public async Task An_empty_push_is_still_rejected()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", ArchiDoc));

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    // ── the guard that stops this becoming data loss ────────────────────────

    [Fact]
    public async Task A_host_cannot_remove_another_hosts_elements()
    {
        // THE test. ArchiCAD pushes a full export; every Revit-contributed
        // GlobalId is "absent" from it. If absence were enough, this call would
        // wipe the Revit model.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", ArchiDoc, RevitGuid));

            Assert.True(await IsLiveAsync(w, RevitGuid),
                "an ArchiCAD push tombstoned a Revit-contributed element");
        }
    }

    [Fact]
    public async Task A_different_document_of_the_same_host_cannot_remove_it()
    {
        // Two ArchiCAD files in one project: a full export of file B must not
        // remove file A's elements.
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", "some-other-archicad-doc", ArchiGuid));

            Assert.True(await IsLiveAsync(w, ArchiGuid));
        }
    }

    [Fact]
    public async Task An_unknown_global_id_removes_nothing_and_does_not_error()
    {
        var w = NewWorld();
        using (w.Conn)
        {
            using var db = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db)
                .IngestData(w.Project, Removal("archicad", ArchiDoc, "3NeverSeenBefore00000"));

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(0, Assert.IsType<IfcIngestResponse>(ok.Value).Removed);
            Assert.True(await IsLiveAsync(w, ArchiGuid));
        }
    }

    [Fact]
    public async Task Removing_the_same_element_twice_is_a_no_op()
    {
        // The plugin re-sends on failure, so a repeat is expected traffic.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db).IngestData(w.Project, Removal("archicad", ArchiDoc, ArchiGuid));

            using var db2 = NewContext(w.Conn, w.Tenant);
            var result = await NewController(w, db2)
                .IngestData(w.Project, Removal("archicad", ArchiDoc, ArchiGuid));

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            // Already tombstoned, so nothing left to tombstone.
            Assert.Equal(0, Assert.IsType<IfcIngestResponse>(ok.Value).Removed);
        }
    }

    [Fact]
    public async Task Soft_delete_not_hard_delete()
    {
        // TaggedElement carries tag history, issues and clash references.
        // Hard-deleting would break those and make an accidental deletion in the
        // authoring tool unrecoverable.
        var w = NewWorld();
        using (w.Conn)
        {
            using (var db = NewContext(w.Conn, w.Tenant))
                await NewController(w, db).IngestData(w.Project, Removal("archicad", ArchiDoc, ArchiGuid));

            using var check = NewContext(w.Conn, w.Tenant);
            var row = await check.TaggedElements.IgnoreQueryFilters()
                .SingleAsync(e => e.IfcGlobalId == ArchiGuid);
            Assert.NotNull(row.DeletedAtUtc);
        }
    }
}
