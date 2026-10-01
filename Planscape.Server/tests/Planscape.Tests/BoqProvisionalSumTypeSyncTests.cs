using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planscape.API.Controllers;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// DSCH-44 — the plugin now sends each provisional sum's NRM2 2.9.1 type
/// (Defined / Undefined / NotDeclared) and PC sums as "PcSum". The server must
/// store the type, refuse anything that is not one of the three tokens or that
/// sits on a non-PS line, and drop the type when a line stops being a PS.
/// </summary>
public class BoqProvisionalSumTypeSyncTests
{
    private sealed class FixedTenant : ITenantContext
    {
        public FixedTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
        public string TenantSlug => "t";
        public LicenseTier Tier => LicenseTier.Professional;
        public bool MimEnabled => false;
    }

    private sealed record World(string Db, Guid Tenant, Guid Project, Guid Baseline, Guid Code);

    // EF InMemory, not SQLite: the endpoint ends with a decimal Sum (the baseline
    // total) that the SQLite provider cannot translate.
    private static PlanscapeDbContext NewContext(string db, Guid tenantId)
        => new(new DbContextOptionsBuilder<PlanscapeDbContext>().UseInMemoryDatabase(db).Options,
               httpContextAccessor: null!, tenantContext: new FixedTenant(tenantId));

    private static World NewWorld()
    {
        var conn = "boq-ps-" + Guid.NewGuid().ToString("N");
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var system = Guid.NewGuid();
        var code = Guid.NewGuid();
        var baseline = Guid.NewGuid();
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
            ctx.ClassificationSystems.Add(new ClassificationSystem { Id = system, TenantId = tenant, Code = "NRM2", Name = "NRM2" });
            ctx.ClassificationCodes.Add(new ClassificationCode { Id = code, TenantId = tenant, SystemId = system, Code = "41", Title = "Prov" });
            ctx.BoqBaselines.Add(new BoqBaseline { Id = baseline, TenantId = tenant, ProjectId = project, Name = "Tender" });
            ctx.SaveChanges();
        }
        return new World(conn, tenant, project, baseline, code);
    }

    private static BoqController NewController(World w, PlanscapeDbContext db) => new(db, NullLogger<BoqController>.Instance, storage: null!)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", w.Tenant.ToString()) }, "test")),
            },
        },
    };

    private static UpsertQuantityLineRequest Line(World w, string guid, string lineKind, string? psType)
        => new(w.Code, null, null, null, guid, "IfcFurniture", "1", "L1", "Z1", "41", "Sum", "item",
               1, 0, 1000m, "UGX", lineKind, "Remeasure", null,
               QuantityIsFinal: true, PayloadSchemaVersion: 2, ProvisionalSumType: psType);

    private static async Task<IActionResult> Push(World w, params UpsertQuantityLineRequest[] lines)
    {
        using var db = NewContext(w.Db, w.Tenant);
        return await NewController(w, db).UpsertQuantityLines(w.Project, w.Baseline, lines.ToList());
    }

    private static QuantityLine Stored(World w, string guid)
    {
        using var db = NewContext(w.Db, w.Tenant);
        return db.QuantityLines.AsNoTracking().Single(l => l.IfcGlobalId == guid);
    }

    [Theory]
    [InlineData("Defined")]
    [InlineData("Undefined")]
    [InlineData("NotDeclared")]
    public async Task A_Provisional_Sum_Stores_Its_Type(string psType)
    {
        var w = NewWorld();
        Assert.IsType<OkObjectResult>(await Push(w, Line(w, "PS1", "ProvisionalSum", psType)));
        var stored = Stored(w, "PS1");
        Assert.Equal("ProvisionalSum", stored.LineKind);
        Assert.Equal(psType, stored.ProvisionalSumType);
    }

    [Fact]
    public async Task A_PC_Sum_Is_Stored_As_PcSum_With_No_Type()
    {
        var w = NewWorld();
        Assert.IsType<OkObjectResult>(await Push(w, Line(w, "PC1", "PcSum", null)));
        var stored = Stored(w, "PC1");
        Assert.Equal("PcSum", stored.LineKind);
        Assert.Null(stored.ProvisionalSumType);
    }

    [Theory]
    [InlineData("ProvisionalSum", "defined")]       // exact tokens only
    [InlineData("ProvisionalSum", "NOT DECLARED")]  // the bill marker, not the wire token
    [InlineData("PcSum", "Defined")]                // a type on a non-PS line
    [InlineData("Measured", "Undefined")]
    public async Task A_Bad_Type_Is_Refused_And_Nothing_Is_Stored(string lineKind, string psType)
    {
        var w = NewWorld();
        Assert.IsType<BadRequestObjectResult>(await Push(w, Line(w, "X1", lineKind, psType)));
        using var db = NewContext(w.Db, w.Tenant);
        Assert.Empty(db.QuantityLines.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Re_Push_Updates_The_Type_And_A_Line_That_Stops_Being_A_PS_Loses_It()
    {
        var w = NewWorld();
        await Push(w, Line(w, "PS2", "ProvisionalSum", "NotDeclared"));
        await Push(w, Line(w, "PS2", "ProvisionalSum", "Defined"));
        Assert.Equal("Defined", Stored(w, "PS2").ProvisionalSumType);

        await Push(w, Line(w, "PS2", "PcSum", null));
        var stored = Stored(w, "PS2");
        Assert.Equal("PcSum", stored.LineKind);
        Assert.Null(stored.ProvisionalSumType);
    }
}
