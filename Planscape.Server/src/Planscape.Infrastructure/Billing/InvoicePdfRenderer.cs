using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Planscape.Infrastructure.Billing;

/// <summary>
/// S2.5.1 — Unicode-safe invoice PDF renderer powered by QuestPDF
/// (Community license, free for our scale). Replaces the hand-rolled
/// ASCII-only PDF emitter from S2.5; emits proper UTF-8 with embedded
/// fonts so tenant names in Cyrillic / Arabic / Amharic / Swahili
/// (well — Swahili uses Latin) all render correctly.
///
/// API surface kept identical to the original InvoicePdfRenderer so
/// BillingController doesn't need to change.
/// </summary>
public class InvoicePdfRenderer
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;

    static InvoicePdfRenderer()
    {
        // Required by QuestPDF's licensing engine.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public InvoicePdfRenderer(PlanscapeDbContext db, IFileStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<string> RenderAndStoreAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");
        var tenant  = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == invoice.TenantId, ct);

        var pdfBytes = Document.Create(c => Compose(c, invoice, tenant)).GeneratePdf();
        await using var ms = new MemoryStream(pdfBytes);
        var path = await _storage.SaveAsync(
            tenantSlug:  "t_" + invoice.TenantId.ToString("N"),
            projectCode: "billing",
            fileName:    $"{invoice.InvoiceNumber}.pdf",
            content:     ms, ct: ct);

        var tracking = await _db.Invoices.FirstAsync(i => i.Id == invoiceId, ct);
        tracking.PdfStoragePath = path;
        await _db.SaveChangesAsync(ct);
        return path;
    }

    private static void Compose(IDocumentContainer doc, Invoice inv, Tenant tenant)
    {
        doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("INVOICE").FontSize(22).Bold();
                    col.Item().Text(inv.InvoiceNumber).FontSize(12).FontColor(Colors.Grey.Darken2);
                });
                row.ConstantItem(160).AlignRight().Column(col =>
                {
                    col.Item().Text("Planscape Ltd").Bold();
                    col.Item().Text("Kampala, Uganda").FontColor(Colors.Grey.Darken1);
                    col.Item().Text("hello@planscape.app").FontColor(Colors.Grey.Darken1);
                });
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Spacing(12);

                col.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Bill to").FontColor(Colors.Grey.Darken1).FontSize(9);
                        c.Item().Text(tenant.Name).Bold();
                        c.Item().Text(tenant.ContactEmail).FontColor(Colors.Grey.Darken2);
                    });
                    r.ConstantItem(180).Column(c =>
                    {
                        c.Item().Row(rr => { rr.RelativeItem().Text("Issued"); rr.RelativeItem().AlignRight().Text($"{inv.IssuedAt:yyyy-MM-dd}"); });
                        c.Item().Row(rr => { rr.RelativeItem().Text("Due");    rr.RelativeItem().AlignRight().Text($"{inv.DueAt:yyyy-MM-dd}"); });
                        c.Item().Row(rr => { rr.RelativeItem().Text("Period"); rr.RelativeItem().AlignRight().Text($"{inv.PeriodStart:yyyy-MM} → {inv.PeriodEnd:yyyy-MM}"); });
                    });
                });

                col.Item().Element(e => e.LineHorizontal(0.5f));

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(120); });
                    t.Header(h =>
                    {
                        h.Cell().Text("Item").Bold();
                        h.Cell().AlignRight().Text("Amount").Bold();
                    });
                    t.Cell().Text($"{tenant.Plan} plan ({inv.PeriodStart:MMM yyyy})");
                    t.Cell().AlignRight().Text(FormatAmount(inv.AmountMinorUnits, inv.Currency));
                });

                col.Item().Element(e => e.LineHorizontal(0.5f));

                col.Item().AlignRight().Column(c =>
                {
                    c.Item().Width(220).Row(r => { r.RelativeItem().Text("Subtotal"); r.RelativeItem().AlignRight().Text(FormatAmount(inv.AmountMinorUnits, inv.Currency)); });
                    c.Item().Width(220).Row(r => { r.RelativeItem().Text("Tax");      r.RelativeItem().AlignRight().Text(FormatAmount(inv.TaxMinorUnits,    inv.Currency)); });
                    c.Item().Width(220).Row(r => { r.RelativeItem().Text("Total").Bold(); r.RelativeItem().AlignRight().Text(FormatAmount(inv.TotalMinorUnits, inv.Currency)).Bold(); });
                });

                if (!string.IsNullOrEmpty(inv.PurchaseOrderRef))
                    col.Item().Text($"Purchase order: {inv.PurchaseOrderRef}").FontColor(Colors.Grey.Darken1);

                col.Item().PaddingTop(12).Text(TaxFooter(inv.Currency)).FontColor(Colors.Grey.Darken1).FontSize(9);
            });

            page.Footer().AlignCenter().Text(t => { t.Span("Status: ").FontColor(Colors.Grey.Darken1); t.Span(inv.Status.ToString()).Bold(); });
        });
    }

    private static string FormatAmount(long minor, string currency)
    {
        var zeroDecimal = currency.Equals("UGX", StringComparison.OrdinalIgnoreCase)
                       || currency.Equals("RWF", StringComparison.OrdinalIgnoreCase);
        var divisor = zeroDecimal ? 1m : 100m;
        var major = minor / divisor;
        return zeroDecimal
            ? $"{currency} {major:N0}"
            : $"{currency} {major:N2}";
    }

    private static string TaxFooter(string currency) => currency.ToUpperInvariant() switch
    {
        "UGX" => "URA TIN: <to be configured>     Mobile-money: 256-XXX-XXXX",
        "KES" => "KRA PIN: <to be configured>     M-Pesa Paybill: 999999",
        "TZS" => "TRA TIN: <to be configured>",
        "RWF" => "RRA TIN: <to be configured>",
        "NGN" => "FIRS TIN: <to be configured>",
        "ZAR" => "SARS VAT: <to be configured>",
        _      => "VAT registered: <to be configured>",
    };
}
