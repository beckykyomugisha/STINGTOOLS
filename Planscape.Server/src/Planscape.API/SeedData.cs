using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API;

/// <summary>
/// Seeds the development database with a demo tenant, admin user, and sample project.
/// Idempotent — safe to run on every startup. Refuses to run in Production.
/// </summary>
public static class SeedData
{
    // S2 — defence-in-depth: refuse to seed in Production even if Program.cs's
    // IsDevelopment() guard is bypassed. The demo admin (admin@planscape.demo /
    // admin123) and Premium licence key were a back-door if Production ever
    // started against an empty DB.
    public static async Task SeedAsync(PlanscapeDbContext db, IHostEnvironment env,
        IFileStorageService? storage = null)
    {
        if (env.IsProduction())
        {
            // Allow explicit opt-in for staging / smoke runs via env var.
            var allow = Environment.GetEnvironmentVariable("PLANSCAPE_ALLOW_DEMO_SEED");
            if (!string.Equals(allow, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        // Bypass the global tenant query filter. Every find-or-create probe
        // below queries an ITenantScoped entity (Tenant/AppUser/LicenseKey/
        // Project), and TenantContext.TenantId returns Guid.Empty at startup
        // (no HTTP context). Without bypass, the probes return null even when
        // the rows exist, and the subsequent Add() trips the unique
        // constraint on the *actual* row in the database.
        db.BypassTenantFilter = true;

        // Idempotent guard. Don't compare against ANY tenant — PlatformTenantSeeder
        // also creates a tenant on first start (the platform 'planscape' tenant for
        // operator accounts), so 'any tenant exists' would skip the demo seed
        // entirely. Probe for the demo admin specifically.
        if (await db.Users.AnyAsync(u => u.Email == "admin@planscape.demo")) return;

        // ── Demo Tenant ──
        // Find-or-create. DemoSandboxJob (Hangfire recurring) also owns
        // the Slug="demo" tenant — if it has already minted the row this
        // boot, attach the demo admin to that tenant rather than inserting
        // a duplicate (which would trip IX_Tenants_Slug). When neither
        // seeder has run yet we create it ourselves.
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "demo");
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Name = "Planscape Demo",
                Slug = "demo",
                Tier = LicenseTier.Premium,
                MaxUsers = 50,
                MaxProjects = 20,
                MimEnabled = true
            };
            db.Tenants.Add(tenant);
        }

        // ── Admin User ──
        var admin = new AppUser
        {
            TenantId = tenant.Id,
            Email = "admin@planscape.demo",
            DisplayName = "BIM Coordinator",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 12),
            Role = UserRole.Admin,
            Iso19650Role = "A" // Appointing Party
        };
        db.Users.Add(admin);

        // ── Demo License Key (find-or-create) ──
        var license = await db.LicenseKeys.FirstOrDefaultAsync(l => l.Key == "PLANSCAPE-DEMO-2026-PREMIUM");
        if (license == null)
        {
            license = new LicenseKey
            {
                TenantId = tenant.Id,
                Key = "PLANSCAPE-DEMO-2026-PREMIUM",
                Tier = LicenseTier.Premium,
                MaxActivations = 10,
                MimEnabled = true,
                ExpiresAt = DateTime.UtcNow.AddYears(1)
            };
            db.LicenseKeys.Add(license);
        }

        // ── Sample Project (find-or-create) ──
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Code == "NHW-2026" && p.TenantId == tenant.Id);
        if (project != null)
        {
            // Project + cascading children already exist from a prior partial run.
            // Skip the rest of the sample data; the demo admin we just attached is enough.
            await db.SaveChangesAsync();
            return;
        }
        project = new Project
        {
            TenantId = tenant.Id,
            Name = "New Hospital Wing",
            Code = "NHW-2026",
            Description = "ISO 19650-compliant BIM project for new hospital wing extension",
            Phase = "Stage 4 - Technical Design",
            TagSeparator = "-",
            SeqNumPad = 4,
            CompliancePercent = 84.0,
            ContainerCompliancePercent = 78.0,
            TotalElements = 12450,
            TaggedElements = 10458,
            WarningCount = 47,
            RagStatus = "AMBER",
            City = "Kampala",
            Country = "Uganda",
            Latitude = 0.3136,
            Longitude = 32.5811,
            Status = ProjectStatus.Active,
            LastSyncAt = DateTime.UtcNow.AddHours(-2),
            // SEED-01 — approx 300m × 300m demo boundary polygon around central London.
            // GeoJSON-style [lat, lon] pairs (closing coordinate repeated).
            BoundaryPolygon = @"[
                [51.5075, -0.1280],
                [51.5075, -0.1240],
                [51.5045, -0.1240],
                [51.5045, -0.1280],
                [51.5075, -0.1280]
            ]"
        };
        db.Projects.Add(project);

        // Phase 169 — extra demo projects so the project map has meaningful
        // pin distribution across East & West Africa. Each gets the admin
        // user as BIM Coordinator + a second seeded role member.
        var entebbe = new Project
        {
            TenantId = tenant.Id,
            Name = "Entebbe Airport Terminal Expansion",
            Code = "EAT-2025",
            Phase = "Execution",
            Status = ProjectStatus.Active,
            CompliancePercent = 91.0,
            RagStatus = "GREEN",
            TotalElements = 8730,
            TaggedElements = 7944,
            City = "Entebbe",
            Country = "Uganda",
            Latitude = 0.0424,
            Longitude = 32.4430,
            LastSyncAt = DateTime.UtcNow.AddHours(-6),
        };
        var lagos = new Project
        {
            TenantId = tenant.Id,
            Name = "Lagos Port Infrastructure",
            Code = "LPI-2026",
            Phase = "Design",
            Status = ProjectStatus.Active,
            CompliancePercent = 42.0,
            RagStatus = "RED",
            TotalElements = 4210,
            TaggedElements = 1768,
            City = "Lagos",
            Country = "Nigeria",
            Latitude = 6.4541,
            Longitude = 3.3947,
            LastSyncAt = DateTime.UtcNow.AddDays(-1),
        };
        var nairobi = new Project
        {
            TenantId = tenant.Id,
            Name = "Nairobi Mixed-Use Development",
            Code = "NMD-2025",
            Phase = "Handover",
            Status = ProjectStatus.Archived,
            CompliancePercent = 98.0,
            RagStatus = "GREEN",
            TotalElements = 21300,
            TaggedElements = 20874,
            City = "Nairobi",
            Country = "Kenya",
            Latitude = -1.2921,
            Longitude = 36.8219,
            LastSyncAt = DateTime.UtcNow.AddDays(-30),
        };
        var accra = new Project
        {
            TenantId = tenant.Id,
            Name = "Accra Commercial Centre",
            Code = "ACC-2024",
            Phase = "Handover",
            Status = ProjectStatus.Archived,
            CompliancePercent = 96.0,
            RagStatus = "GREEN",
            TotalElements = 14920,
            TaggedElements = 14323,
            City = "Accra",
            Country = "Ghana",
            Latitude = 5.6037,
            Longitude = -0.1870,
            LastSyncAt = DateTime.UtcNow.AddDays(-90),
        };
        var darWtp = new Project
        {
            TenantId = tenant.Id,
            Name = "Dar es Salaam Water Treatment Plant",
            Code = "DSM-WTP-2026",
            Phase = "Design",
            Status = ProjectStatus.Active,
            CompliancePercent = 67.0,
            RagStatus = "AMBER",
            TotalElements = 6210,
            TaggedElements = 4161,
            City = "Dar es Salaam",
            Country = "Tanzania",
            Latitude = -6.7924,
            Longitude = 39.2083,
            LastSyncAt = DateTime.UtcNow.AddHours(-18),
        };
        db.Projects.AddRange(entebbe, lagos, nairobi, accra, darWtp);

        // Per-project memberships. admin is BIM Coordinator on every project;
        // a second seeded discipline lead gives the "team: 2 members" chip
        // something to count.
        var allProjects = new[] { project, entebbe, lagos, nairobi, accra, darWtp };
        var leadRoles = new[]
        {
            ("M", "MEP Lead"),
            ("S", "Structural Lead"),
            ("A", "Architectural Lead"),
            ("PM", "Project Manager"),
            ("QS", "Quantity Surveyor"),
            ("BC", "BIM Coordinator"),
        };
        for (var i = 0; i < allProjects.Length; i++)
        {
            var p = allProjects[i];
            db.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant.Id,
                ProjectId = p.Id,
                UserId = admin.Id,
                ProjectRole = "Coordinator",
                Iso19650Role = "BC",
            });
            var (iso, label) = leadRoles[i % leadRoles.Length];
            // Seeded "lead" placeholder user for membership counts. We don't
            // create an AppUser row to keep the demo seed minimal — only the
            // count is read by the dashboard cards.
            db.ProjectMembers.Add(new ProjectMember
            {
                TenantId = tenant.Id,
                ProjectId = p.Id,
                UserId = Guid.NewGuid(),
                ProjectRole = "Contributor",
                Iso19650Role = iso,
                InvitedBy = label,
            });
        }

        // ── Sample Issues ──
        db.Issues.AddRange(
            new BimIssue
            {
                ProjectId = project.Id, IssueCode = "RFI-0001", Type = "RFI",
                Title = "Confirm ceiling height in corridor L02-C01",
                Priority = "MEDIUM", Status = "OPEN", Discipline = "A",
                CreatedBy = admin.DisplayName,
                DueDate = DateTime.UtcNow.AddDays(5)
            },
            new BimIssue
            {
                ProjectId = project.Id, IssueCode = "NCR-0001", Type = "NCR",
                Title = "Duct clash with structural beam at grid E-4",
                Priority = "HIGH", Status = "IN_PROGRESS", Discipline = "M",
                Assignee = "MEP Lead",
                CreatedBy = admin.DisplayName,
                DueDate = DateTime.UtcNow.AddDays(1)
            },
            new BimIssue
            {
                ProjectId = project.Id, IssueCode = "SI-0001", Type = "SI",
                Title = "Missing fire dampers in Zone Z03 risers",
                Priority = "CRITICAL", Status = "OPEN", Discipline = "FP",
                CreatedBy = admin.DisplayName,
                DueDate = DateTime.UtcNow.AddHours(4)
            }
        );

        // ── Sample Documents ──
        db.Documents.AddRange(
            new DocumentRecord
            {
                ProjectId = project.Id,
                FileName = "NHW-STG-XX-ZZ-DR-A-0001-P01.pdf",
                DocumentType = "DR", CdeStatus = "SHARED", SuitabilityCode = "S3",
                Discipline = "A", Revision = "P01", UploadedBy = admin.DisplayName
            },
            new DocumentRecord
            {
                ProjectId = project.Id,
                FileName = "NHW-STG-XX-ZZ-SH-M-0001-P01.xlsx",
                DocumentType = "SH", CdeStatus = "WIP", SuitabilityCode = "S0",
                Discipline = "M", Revision = "P01", UploadedBy = "MEP Lead"
            }
        );

        // ── Sample Workflow Runs ──
        db.WorkflowRuns.AddRange(
            new WorkflowRun
            {
                ProjectId = project.Id, PresetName = "DailyQA",
                UserName = admin.DisplayName,
                StepsPassed = 7, StepsFailed = 1, StepsSkipped = 1,
                DurationMs = 45000, ComplianceBefore = 68.2, ComplianceAfter = 72.5,
                ExecutedAt = DateTime.UtcNow.AddHours(-2)
            },
            new WorkflowRun
            {
                ProjectId = project.Id, PresetName = "MorningHealthCheck",
                UserName = admin.DisplayName,
                StepsPassed = 8, StepsFailed = 0, StepsSkipped = 2,
                DurationMs = 32000, ComplianceBefore = 65.0, ComplianceAfter = 68.2,
                ExecutedAt = DateTime.UtcNow.AddDays(-1)
            }
        );

        // ── Sample Compliance Snapshots ──
        for (int i = 7; i >= 0; i--)
        {
            db.Set<ComplianceSnapshot>().Add(new ComplianceSnapshot
            {
                ProjectId = project.Id,
                CapturedBy = admin.DisplayName,
                CapturedAt = DateTime.UtcNow.AddDays(-i),
                TotalElements = 12450,
                TaggedComplete = 7000 + (i * 300),
                TaggedIncomplete = 2000 - (i * 100),
                Untagged = 3450 - (i * 200),
                FullyResolved = 6500 + (i * 350),
                StaleCount = 15 + i,
                WarningCount = 47 + (i * 3),
                TagPercent = 56 + (i * 2.3),
                StrictPercent = 52 + (i * 2.8),
                ContainerPercent = 50 + (i * 2.5),
                RagStatus = i > 3 ? "RED" : "AMBER"
            });
        }

        // ── Demo 3-D Model (glTF/GLB) ────────────────────────────────────────
        // Seed a minimal but valid GLB so the model viewer works on a fresh
        // database without needing to publish anything from the Revit plugin.
        // Three coloured floor slabs (ground / L1 / L2) in a 10 × 8 m footprint.
        if (storage != null &&
            !await db.ProjectModels.AnyAsync(m => m.ProjectId == project.Id))
        {
            try
            {
                byte[] glbBytes = BuildDemoGlb();
                using var ms = new MemoryStream(glbBytes);
                string path = await storage.SaveScopedAsync(tenant.Id, project.Id,
                    "nhw-demo-building.glb", ms);
                db.ProjectModels.Add(new ProjectModel
                {
                    TenantId      = tenant.Id,
                    ProjectId     = project.Id,
                    Name          = "NHW Demo — 3-Storey Building",
                    Description   = "Auto-seeded demo model (ground floor + L1 + L2 slabs)",
                    Discipline    = "A",
                    FileName      = "nhw-demo-building.glb",
                    Format        = ModelFormat.Glb,
                    StoragePath   = path,
                    FileSizeBytes = glbBytes.Length,
                    ElementCount  = 3,
                    Units         = "m",
                    Revision      = "P01",
                    BoundsMinX    = 0, BoundsMinY = 0, BoundsMinZ = 0,
                    BoundsMaxX    = 10, BoundsMaxY = 7.3, BoundsMaxZ = 8,
                    UploadedBy    = "Planscape Seed",
                });
            }
            catch { /* non-critical — viewer just shows "no models yet" */ }
        }

        await db.SaveChangesAsync();
    }

    // ── Minimal GLB builder ────────────────────────────────────────────────
    // Generates a valid glTF 2.0 binary with three coloured floor slabs:
    //   - Ground floor (grey)  y = 0.0 – 0.3 m
    //   - Level 1 (blue)       y = 3.5 – 3.8 m
    //   - Level 2 (green)      y = 7.0 – 7.3 m
    // Footprint: 10 m (X) × 8 m (Z). Each slab is a closed box mesh.
    private static byte[] BuildDemoGlb()
    {
        const float W = 10f, D = 8f, SH = 0.3f;
        float[] ys = { 0f, 3.5f, 7.0f };

        // Binary buffer layout:
        //   [0   .. 287]  vertices: 3 slabs × 8 verts × 3 floats × 4 bytes = 288 bytes
        //   [288 .. 503]  indices:  3 slabs × 36 uint16 × 2 bytes = 216 bytes
        var bin = new byte[504];

        // Clockwise winding (glTF default back-face culling off — visible from any angle).
        ushort[] faceIdx = { 0,2,1, 0,3,2, 4,5,6, 4,6,7,
                             3,7,6, 3,6,2, 1,0,4, 1,4,5,
                             1,2,6, 1,6,5, 0,4,7, 0,7,3 };

        for (int s = 0; s < 3; s++)
        {
            float y0 = ys[s], y1 = y0 + SH;
            float[] xs  = { 0, W, W, 0, 0, W, W, 0 };
            float[] yv  = { y0,y0,y0,y0, y1,y1,y1,y1 };
            float[] zv  = { 0, 0, D, D,  0, 0, D, D };
            for (int v = 0; v < 8; v++)
            {
                int p = s * 96 + v * 12;
                BitConverter.GetBytes(xs[v]).CopyTo(bin, p);
                BitConverter.GetBytes(yv[v]).CopyTo(bin, p + 4);
                BitConverter.GetBytes(zv[v]).CopyTo(bin, p + 8);
            }
            for (int i = 0; i < 36; i++)
                BitConverter.GetBytes(faceIdx[i]).CopyTo(bin, 288 + s * 72 + i * 2);
        }

        string json =
            "{\"asset\":{\"version\":\"2.0\",\"generator\":\"Planscape Demo Seed\"}," +
            "\"scene\":0,\"scenes\":[{\"name\":\"NHW-2026\",\"nodes\":[0,1,2]}]," +
            "\"nodes\":[{\"name\":\"Ground Floor\",\"mesh\":0}," +
                       "{\"name\":\"Level 1\",\"mesh\":1}," +
                       "{\"name\":\"Level 2\",\"mesh\":2}]," +
            "\"meshes\":[{\"name\":\"GF Slab\",\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":3,\"material\":0}]}," +
                        "{\"name\":\"L1 Slab\",\"primitives\":[{\"attributes\":{\"POSITION\":1},\"indices\":4,\"material\":1}]}," +
                        "{\"name\":\"L2 Slab\",\"primitives\":[{\"attributes\":{\"POSITION\":2},\"indices\":5,\"material\":2}]}]," +
            "\"materials\":[" +
                "{\"name\":\"Concrete\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.55,0.55,0.55,1.0]}}," +
                "{\"name\":\"L1 Blue\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.20,0.47,0.76,1.0]}}," +
                "{\"name\":\"L2 Green\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.18,0.65,0.38,1.0]}}]," +
            "\"accessors\":[" +
                "{\"bufferView\":0,\"componentType\":5126,\"count\":8,\"type\":\"VEC3\",\"max\":[10,0.3,8],\"min\":[0,0,0]}," +
                "{\"bufferView\":1,\"componentType\":5126,\"count\":8,\"type\":\"VEC3\",\"max\":[10,3.8,8],\"min\":[0,3.5,0]}," +
                "{\"bufferView\":2,\"componentType\":5126,\"count\":8,\"type\":\"VEC3\",\"max\":[10,7.3,8],\"min\":[0,7.0,0]}," +
                "{\"bufferView\":3,\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"}," +
                "{\"bufferView\":4,\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"}," +
                "{\"bufferView\":5,\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"}]," +
            "\"bufferViews\":[" +
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":96,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":96,\"byteLength\":96,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":192,\"byteLength\":96,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":288,\"byteLength\":72,\"target\":34963}," +
                "{\"buffer\":0,\"byteOffset\":360,\"byteLength\":72,\"target\":34963}," +
                "{\"buffer\":0,\"byteOffset\":432,\"byteLength\":72,\"target\":34963}]," +
            "\"buffers\":[{\"byteLength\":504}]}";

        byte[] jb = System.Text.Encoding.UTF8.GetBytes(json);
        int jp   = (jb.Length + 3) & ~3;          // JSON padded length (4-byte aligned)
        int total = 12 + 8 + jp + 8 + 504;
        var glb  = new byte[total];

        // GLB header (12 bytes)
        BitConverter.GetBytes(0x46546C67u).CopyTo(glb, 0);   // magic "glTF"
        BitConverter.GetBytes(2u).CopyTo(glb, 4);             // version 2
        BitConverter.GetBytes((uint)total).CopyTo(glb, 8);

        // JSON chunk
        BitConverter.GetBytes((uint)jp).CopyTo(glb, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(glb, 16);  // chunk type "JSON"
        jb.CopyTo(glb, 20);
        for (int i = jb.Length; i < jp; i++) glb[20 + i] = 0x20; // space-pad to alignment

        // Binary chunk
        int bs = 20 + jp;
        BitConverter.GetBytes(504u).CopyTo(glb, bs);
        BitConverter.GetBytes(0x004E4942u).CopyTo(glb, bs + 4); // chunk type "BIN\0"
        bin.CopyTo(glb, bs + 8);

        return glb;
    }
}
