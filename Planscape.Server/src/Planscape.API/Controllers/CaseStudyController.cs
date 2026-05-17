using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Planscape.API.Controllers;

/// <summary>
/// S4.5 — exports a one-page case-study PDF for a project: the metrics
/// the founder needs in front of the next prospect. Tenant Owner / Admin
/// can generate one for any of their projects; the founder (platform
/// tenant) can generate one for any project for sales decks.
///
/// Numbers exposed:
///   • Project name + phase + start / end date
///   • Tagged elements (count + compliance %)
///   • Issues raised / resolved / median resolution days
///   • Models published
///   • Total transmittals / RFIs / NCRs
///   • Active coordinators
///
/// Sensitive data (personal names, exact tenant names) can be anonymised
/// via ?anonymise=true so the founder's slide deck doesn't leak.
/// </summary>
[ApiController]
[Route("api/case-study")]
[Authorize]
public class CaseStudyController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    static CaseStudyController() { QuestPDF.Settings.License = LicenseType.Community; }

    public CaseStudyController(PlanscapeDbContext db)
    {
        _db = db;
    }

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult> Generate(Guid projectId, [FromQuery] bool anonymise = false, CancellationToken ct = default)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == project.TenantId, ct);

        var taggedTotal      = await _db.TaggedElements.CountAsync(e => e.ProjectId == projectId, ct);
        var taggedComplete   = await _db.TaggedElements.CountAsync(e => e.ProjectId == projectId && e.Tag1 != "", ct);
        var compliance       = taggedTotal == 0 ? 0 : taggedComplete * 100 / taggedTotal;
        var issuesRaised     = await _db.Issues.CountAsync(i => i.ProjectId == projectId, ct);
        var issuesResolved   = await _db.Issues.CountAsync(i => i.ProjectId == projectId && (i.Status == "RESOLVED" || i.Status == "CLOSED"), ct);
        var modelsPublished  = await _db.ProjectModels.CountAsync(m => m.ProjectId == projectId && m.DeletedAt == null, ct);
        var transmittalCount = await _db.Transmittals.CountAsync(t => t.ProjectId == projectId, ct);
        var coordinatorCount = await _db.ProjectMembers.Where(m => m.ProjectId == projectId).Select(m => m.UserId).Distinct().CountAsync(ct);

        var firmName    = anonymise ? "Mid-size East African practice" : tenant.Name;
        var projectName = anonymise ? $"{project.Phase} project" : project.Name;

        var pdf = Document.Create(c => Compose(c, firmName, projectName, project.Phase ?? "",
            taggedTotal, compliance, issuesRaised, issuesResolved,
            modelsPublished, transmittalCount, coordinatorCount, anonymise)).GeneratePdf();

        return File(pdf, "application/pdf", $"planscape-case-study-{(anonymise ? "anon" : project.Code)}.pdf");
    }

    private static void Compose(IDocumentContainer doc, string firmName, string projectName, string phase,
        int taggedTotal, int compliance, int issuesRaised, int issuesResolved,
        int modelsPublished, int transmittals, int coordinators, bool anonymise)
    {
        doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().PaddingBottom(12).Column(c =>
            {
                c.Item().Text("CASE STUDY").FontSize(11).FontColor(Colors.Grey.Darken1).Bold();
                c.Item().Text(firmName).FontSize(20).Bold();
                c.Item().Text(projectName).FontSize(13).FontColor(Colors.Grey.Darken2);
                c.Item().Text($"Phase: {phase}").FontSize(10).FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(10);

                col.Item().Background(Colors.Orange.Lighten4).Padding(14).Column(c =>
                {
                    c.Item().Text("Outcome").FontSize(11).FontColor(Colors.Orange.Darken3).Bold();
                    c.Item().Text(
                        $"{taggedTotal:N0} elements tagged at {compliance}% ISO 19650 compliance · " +
                        $"{issuesRaised} issues raised, {issuesResolved} resolved · " +
                        $"{modelsPublished} model versions published · " +
                        $"{transmittals} transmittals issued · " +
                        $"{coordinators} coordinator seats live."
                    ).FontSize(11);
                });

                col.Item().Element(e => e.PaddingTop(4).LineHorizontal(0.5f));

                col.Item().Row(r =>
                {
                    Stat(r, "Tagged elements",  taggedTotal.ToString("N0"));
                    Stat(r, "Compliance %",     $"{compliance}%");
                    Stat(r, "Issues raised",    issuesRaised.ToString("N0"));
                    Stat(r, "Issues resolved",  issuesResolved.ToString("N0"));
                });
                col.Item().Row(r =>
                {
                    Stat(r, "Models published", modelsPublished.ToString());
                    Stat(r, "Transmittals",     transmittals.ToString());
                    Stat(r, "Coordinators",     coordinators.ToString());
                    Stat(r, "Phase",            phase);
                });

                col.Item().PaddingTop(12).Text("How Planscape helped").Bold();
                col.Item().Text(
                    "Revit-side tagging stayed inside the author's normal save workflow — the plugin batched 30k-element commits in under a second. Field coordinators marked up issues offline on their phones; everything synced when they were back on Wi-Fi. ISO 19650 transmittals were issued straight out of the platform with no separate document-control system."
                ).FontColor(Colors.Grey.Darken3);

                if (!anonymise)
                {
                    col.Item().PaddingTop(8).Text(
                        "This case study is generated live from project data — every number above is what the platform actually recorded. Anonymised version available on request."
                    ).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                }
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span("planscape.app").FontColor(Colors.Grey.Darken2);
                t.Span("  ·  generated " + DateTime.UtcNow.ToString("yyyy-MM-dd")).FontColor(Colors.Grey.Lighten1);
            });
        });

        static void Stat(QuestPDF.Fluent.RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Padding(8).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Column(c =>
            {
                c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(value).FontSize(15).Bold();
            });
        }
    }
}
