using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Planscape.Core.Entities;
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
    public static async Task SeedAsync(PlanscapeDbContext db, IHostEnvironment env)
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
        if (await db.Tenants.AnyAsync()) return; // Already seeded

        // ── Demo Tenant ──
        var tenant = new Tenant
        {
            Name = "Planscape Demo",
            Slug = "demo",
            Tier = LicenseTier.Premium,
            MaxUsers = 50,
            MaxProjects = 20,
            MimEnabled = true
        };
        db.Tenants.Add(tenant);

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

        // ── Demo License Key ──
        var license = new LicenseKey
        {
            TenantId = tenant.Id,
            Key = "PLANSCAPE-DEMO-2026-PREMIUM",
            Tier = LicenseTier.Premium,
            MaxActivations = 10,
            MimEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        db.LicenseKeys.Add(license);

        // ── Sample Project ──
        var project = new Project
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

        await db.SaveChangesAsync();
    }
}
