using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// T3 — COBie V2.4 handover pack. The Revit plugin already produces the
/// bulk of the COBie data directly from the model; this controller builds
/// the server-side handover bundle so office users (web dashboard, FM
/// operators) can pull a COBie spreadsheet without opening Revit.
///
/// Sheets covered in v1 of this controller (matches the plugin):
///   Instruction, Facility, Floor, Space, Type, Component, System, Zone,
///   Contact, Attribute, Job, Resource, Document, Spare.
///
/// Content source:
///   - Component / Type / System / Zone / Space / Floor / Facility:
///     derived from TaggedElement + Project + compliance data
///   - Contact: ProjectMembers + Tenant contacts
///   - Attribute: custom fields + STING tag tokens
///   - Job: MaintenanceTask (Planscape.MIM)
///   - Resource: Asset (Planscape.MIM) with tag integration
///   - Document: DocumentRecord with CDE=PUBLISHED
///   - Spare: placeholder; plugin-generated sheet is more complete
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/cobie")]
[Authorize]
public class CobieController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public CobieController(PlanscapeDbContext db) => _db = db;

    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tid) ? tid : Guid.Empty;
        if (tenantId == Guid.Empty) return Forbid();

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound();

        using var workbook = new XLWorkbook();
        var now = DateTime.UtcNow.ToString("o");
        var creator = User.FindFirst("display_name")?.Value ?? "Planscape";

        // ── Instruction sheet ────────────────────────────────────────
        var instruction = workbook.Worksheets.Add("Instruction");
        instruction.Cell(1, 1).Value = "Planscape COBie V2.4 Handover";
        instruction.Cell(2, 1).Value = "Generated";     instruction.Cell(2, 2).Value = now;
        instruction.Cell(3, 1).Value = "Project";       instruction.Cell(3, 2).Value = project.Name;
        instruction.Cell(4, 1).Value = "Project code";  instruction.Cell(4, 2).Value = project.Code;
        instruction.Cell(5, 1).Value = "Creator";       instruction.Cell(5, 2).Value = creator;

        // ── Facility / Floor / Space (project-level) ────────────────
        var facility = AddSheet(workbook, "Facility",
            "Name", "CreatedBy", "CreatedOn", "Category", "ProjectName", "SiteName", "LinearUnits", "AreaUnits", "VolumeUnits", "CurrencyUnit", "AreaMeasurement", "ExternalSystem", "ExternalProjectObject", "ExternalProjectIdentifier", "ExternalSiteObject", "ExternalSiteIdentifier", "ExternalFacilityObject", "ExternalFacilityIdentifier", "Description", "ProjectDescription", "SiteDescription", "Phase");
        facility.Cell(2, 1).Value = project.Name;
        facility.Cell(2, 2).Value = creator;
        facility.Cell(2, 3).Value = now;
        facility.Cell(2, 4).Value = "OmniClass 11-11 00 00";
        facility.Cell(2, 5).Value = project.Name;
        facility.Cell(2, 7).Value = "metres";
        facility.Cell(2, 8).Value = "square-metres";
        facility.Cell(2, 9).Value = "cubic-metres";
        facility.Cell(2, 10).Value = "GBP";

        AddSheet(workbook, "Floor", "Name", "CreatedBy", "CreatedOn", "Category", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "Elevation", "Height");
        AddSheet(workbook, "Space", "Name", "CreatedBy", "CreatedOn", "Category", "FloorName", "Description", "ExtSystem", "ExtObject", "ExtIdentifier", "RoomTag", "UsableHeight", "GrossArea", "NetArea");

        // ── Type / Component (element-driven) ────────────────────────
        var elements = await _db.TaggedElements.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .Take(50_000)
            .ToListAsync(ct);

        var typeSheet = AddSheet(workbook, "Type",
            "Name", "CreatedBy", "CreatedOn", "Category", "Description", "AssetType",
            "Manufacturer", "ModelNumber", "WarrantyGuarantorParts", "WarrantyDurationParts",
            "WarrantyGuarantorLabor", "WarrantyDurationLabor", "WarrantyDurationUnit",
            "ExtSystem", "ExtObject", "ExtIdentifier", "ReplacementCost", "ExpectedLife",
            "DurationUnit", "NominalLength", "NominalWidth", "NominalHeight", "ModelReference",
            "Shape", "Size", "Color", "Finish", "Grade", "Material", "Constituents",
            "Features", "AccessibilityPerformance", "CodePerformance", "SustainabilityPerformance");
        var componentSheet = AddSheet(workbook, "Component",
            "Name", "CreatedBy", "CreatedOn", "TypeName", "Space", "Description", "ExtSystem",
            "ExtObject", "ExtIdentifier", "SerialNumber", "InstallationDate",
            "WarrantyStartDate", "TagNumber", "BarCode", "AssetIdentifier");
        var typeRow = 2; var compRow = 2;
        var typeIndex = new HashSet<string>();
        foreach (var e in elements)
        {
            var typeName = string.IsNullOrEmpty(e.FamilyName) ? e.CategoryName : $"{e.FamilyName} - {e.CategoryName}";
            if (typeIndex.Add(typeName))
            {
                typeSheet.Cell(typeRow, 1).Value = typeName;
                typeSheet.Cell(typeRow, 2).Value = creator;
                typeSheet.Cell(typeRow, 3).Value = now;
                typeSheet.Cell(typeRow, 4).Value = e.CategoryName;
                typeSheet.Cell(typeRow, 14).Value = "Planscape";
                typeSheet.Cell(typeRow, 16).Value = typeName;
                typeRow++;
            }
            componentSheet.Cell(compRow, 1).Value = e.Tag1;
            componentSheet.Cell(compRow, 2).Value = creator;
            componentSheet.Cell(compRow, 3).Value = now;
            componentSheet.Cell(compRow, 4).Value = typeName;
            componentSheet.Cell(compRow, 7).Value = "Planscape";
            componentSheet.Cell(compRow, 9).Value = e.UniqueId;
            componentSheet.Cell(compRow, 13).Value = e.Tag1;
            componentSheet.Cell(compRow, 15).Value = $"{project.Code}-{e.Tag1}";
            compRow++;
        }

        // ── System + Zone (derived from tag tokens) ─────────────────
        var systemSheet = AddSheet(workbook, "System", "Name", "CreatedBy", "CreatedOn", "Category", "ComponentNames", "ExtSystem", "ExtObject", "ExtIdentifier", "Description");
        var systemIdx = elements
            .Where(e => !string.IsNullOrEmpty(e.Sys))
            .GroupBy(e => e.Sys)
            .OrderBy(g => g.Key);
        int r = 2;
        foreach (var g in systemIdx)
        {
            systemSheet.Cell(r, 1).Value = g.Key;
            systemSheet.Cell(r, 2).Value = creator;
            systemSheet.Cell(r, 3).Value = now;
            systemSheet.Cell(r, 5).Value = string.Join(',', g.Take(20).Select(x => x.Tag1));
            systemSheet.Cell(r, 6).Value = "Planscape";
            r++;
        }
        AddSheet(workbook, "Zone", "Name", "CreatedBy", "CreatedOn", "Category", "SpaceNames", "ExtSystem", "ExtObject", "ExtIdentifier", "Description");

        // ── Contact (project members + tenant) ──────────────────────
        var contacts = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.IsActive)
            .Join(_db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { u.Email, u.DisplayName, u.Iso19650Role, m.ProjectRole })
            .ToListAsync(ct);
        var contactSheet = AddSheet(workbook, "Contact",
            "Email", "CreatedBy", "CreatedOn", "Category", "Company", "Phone", "ExtSystem",
            "ExtObject", "ExtIdentifier", "GivenName", "FamilyName", "Street", "PostalBox",
            "Town", "StateRegion", "PostalCode", "Country", "Department", "OrganizationCode",
            "GivenTitle");
        r = 2;
        foreach (var c in contacts)
        {
            contactSheet.Cell(r, 1).Value = c.Email;
            contactSheet.Cell(r, 2).Value = creator;
            contactSheet.Cell(r, 3).Value = now;
            contactSheet.Cell(r, 4).Value = c.ProjectRole;
            contactSheet.Cell(r, 7).Value = "Planscape";
            var parts = (c.DisplayName ?? "").Split(' ', 2);
            contactSheet.Cell(r, 10).Value = parts.Length > 0 ? parts[0] : "";
            contactSheet.Cell(r, 11).Value = parts.Length > 1 ? parts[1] : "";
            contactSheet.Cell(r, 18).Value = c.Iso19650Role;
            r++;
        }

        // ── Attribute (per-component STING tokens) ──────────────────
        var attributeSheet = AddSheet(workbook, "Attribute",
            "Name", "CreatedBy", "CreatedOn", "Category", "SheetName", "RowName", "Value", "Unit", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "AllowedValues");
        r = 2;
        foreach (var e in elements.Take(10_000))
        {
            void AddAttr(string name, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                attributeSheet.Cell(r, 1).Value = name;
                attributeSheet.Cell(r, 2).Value = creator;
                attributeSheet.Cell(r, 3).Value = now;
                attributeSheet.Cell(r, 5).Value = "Component";
                attributeSheet.Cell(r, 6).Value = e.Tag1;
                attributeSheet.Cell(r, 7).Value = value;
                attributeSheet.Cell(r, 9).Value = "Planscape";
                r++;
            }
            AddAttr("Discipline", e.Disc);
            AddAttr("Level",      e.Level);
            AddAttr("System",     e.Sys);
            AddAttr("Function",   e.Func);
            AddAttr("Product",    e.Prod);
            AddAttr("Grid",       e.GridRef);
            AddAttr("Room",       e.RoomName);
        }

        // ── Job (Planscape.MIM MaintenanceTask via Asset → Project) ─
        var jobs = await (from j in _db.MaintenanceTasks.AsNoTracking()
                          join a in _db.Assets.AsNoTracking() on j.AssetId equals a.Id
                          where a.ProjectId == projectId
                          select new { j, a })
                         .Take(10_000).ToListAsync(ct);
        var jobSheet = AddSheet(workbook, "Job",
            "Name", "CreatedBy", "CreatedOn", "Category", "Status", "TypeName", "Description",
            "Duration", "DurationUnit", "Start", "TaskStartUnit", "Frequency", "FrequencyUnit",
            "ExtSystem", "ExtObject", "ExtIdentifier", "TaskNumber", "Priors", "ResourceNames");
        r = 2;
        foreach (var row in jobs)
        {
            var j = row.j; var a = row.a;
            jobSheet.Cell(r, 1).Value = j.Title;
            jobSheet.Cell(r, 2).Value = creator;
            jobSheet.Cell(r, 3).Value = now;
            jobSheet.Cell(r, 4).Value = j.StandardReference ?? "";
            jobSheet.Cell(r, 5).Value = j.Status;
            jobSheet.Cell(r, 6).Value = a.AssetTag;
            jobSheet.Cell(r, 7).Value = j.Description;
            jobSheet.Cell(r, 10).Value = j.NextDueDate?.ToString("o") ?? "";
            jobSheet.Cell(r, 12).Value = j.FrequencyDays;
            jobSheet.Cell(r, 13).Value = "days";
            jobSheet.Cell(r, 14).Value = "Planscape";
            jobSheet.Cell(r, 17).Value = j.TaskCode;
            r++;
        }

        // ── Resource (Planscape.MIM Asset) ─────────────────────────
        var resources = await _db.Assets.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Take(5_000)
            .ToListAsync(ct);
        var resourceSheet = AddSheet(workbook, "Resource",
            "Name", "CreatedBy", "CreatedOn", "Category", "ExtSystem", "ExtObject", "ExtIdentifier", "Description");
        r = 2;
        foreach (var a in resources)
        {
            resourceSheet.Cell(r, 1).Value = string.IsNullOrEmpty(a.AssetName) ? a.AssetTag : a.AssetName;
            resourceSheet.Cell(r, 2).Value = creator;
            resourceSheet.Cell(r, 3).Value = now;
            resourceSheet.Cell(r, 4).Value = a.CategoryName;
            resourceSheet.Cell(r, 5).Value = "Planscape";
            resourceSheet.Cell(r, 7).Value = a.Id.ToString();
            resourceSheet.Cell(r, 8).Value = a.AssetTag;
            r++;
        }

        // ── Document (PUBLISHED CDE state) ─────────────────────────
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.CdeStatus == "PUBLISHED")
            .Take(5_000)
            .ToListAsync(ct);
        var docSheet = AddSheet(workbook, "Document",
            "Name", "CreatedBy", "CreatedOn", "Category", "ApprovalBy", "Stage", "SheetName", "RowName", "Directory", "File", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "Reference");
        r = 2;
        foreach (var d in docs)
        {
            docSheet.Cell(r, 1).Value = d.FileName;
            docSheet.Cell(r, 2).Value = creator;
            docSheet.Cell(r, 3).Value = now;
            docSheet.Cell(r, 4).Value = d.DocumentType;
            docSheet.Cell(r, 5).Value = d.UploadedBy;
            docSheet.Cell(r, 6).Value = d.SuitabilityCode;
            docSheet.Cell(r, 9).Value = d.FilePath ?? "";
            docSheet.Cell(r, 10).Value = d.FileName;
            docSheet.Cell(r, 11).Value = "Planscape";
            docSheet.Cell(r, 13).Value = d.Id.ToString();
            docSheet.Cell(r, 14).Value = d.Description;
            docSheet.Cell(r, 15).Value = d.Revision;
            r++;
        }

        // ── Spare (placeholder; plugin produces the richer sheet) ─
        AddSheet(workbook, "Spare", "Name", "CreatedBy", "CreatedOn", "Category", "TypeName", "Suppliers", "ExtSystem", "ExtObject", "ExtIdentifier", "SetNumber", "PartNumber", "Description");

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"cobie-{project.Code}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static IXLWorksheet AddSheet(XLWorkbook wb, string name, params string[] headers)
    {
        var ws = wb.Worksheets.Add(name);
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        return ws;
    }
}
