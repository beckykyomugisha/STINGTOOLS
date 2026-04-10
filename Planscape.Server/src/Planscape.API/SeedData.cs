using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API;

/// <summary>
/// Seeds the development database with a demo tenant, admin user, and sample project.
/// Idempotent — safe to run on every startup.
/// </summary>
public static class SeedData
{
    public static async Task SeedAsync(PlanscapeDbContext db)
    {
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
            CompliancePercent = 72.5,
            ContainerCompliancePercent = 68.0,
            TotalElements = 12450,
            TaggedElements = 9026,
            WarningCount = 47,
            RagStatus = "AMBER"
        };
        db.Projects.Add(project);

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
