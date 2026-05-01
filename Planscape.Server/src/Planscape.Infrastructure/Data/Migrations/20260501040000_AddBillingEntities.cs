using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// S2.1 — adds <c>Subscriptions</c>, <c>Invoices</c>, <c>Payments</c> tables.
/// Provider-agnostic schema (Stripe + Flutterwave wired in S2.2/S2.3 share
/// the same shape). Amounts stored in minor units to dodge IEEE-754 rounding.
/// </remarks>
public partial class AddBillingEntities : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "Subscriptions",
            columns: t => new
            {
                Id                     = t.Column<Guid>("uuid", nullable: false),
                TenantId               = t.Column<Guid>("uuid", nullable: false),
                Provider               = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                ProviderCustomerId     = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                ProviderSubscriptionId = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                Plan                   = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                Currency               = t.Column<string>("character varying(3)", maxLength: 3, nullable: false),
                Cycle                  = t.Column<int>("integer", nullable: false),
                PriceMinorUnits        = t.Column<long>("bigint", nullable: false),
                Status                 = t.Column<int>("integer", nullable: false),
                CurrentPeriodStart     = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CurrentPeriodEnd       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CancelledAt            = t.Column<DateTime>("timestamp with time zone", nullable: true),
                CreatedAt              = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt              = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t => t.PrimaryKey("PK_Subscriptions", x => x.Id));
        mb.CreateIndex("IX_Subscriptions_TenantId_Status", "Subscriptions", new[] { "TenantId", "Status" });
        mb.CreateIndex("IX_Subscriptions_ProviderSubscriptionId", "Subscriptions", "ProviderSubscriptionId");

        mb.CreateTable(
            name: "Invoices",
            columns: t => new
            {
                Id                = t.Column<Guid>("uuid", nullable: false),
                TenantId          = t.Column<Guid>("uuid", nullable: false),
                SubscriptionId    = t.Column<Guid>("uuid", nullable: false),
                Provider          = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                ProviderInvoiceId = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                InvoiceNumber     = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Currency          = t.Column<string>("character varying(3)", maxLength: 3, nullable: false),
                AmountMinorUnits  = t.Column<long>("bigint", nullable: false),
                TaxMinorUnits     = t.Column<long>("bigint", nullable: false),
                TotalMinorUnits   = t.Column<long>("bigint", nullable: false),
                IssuedAt          = t.Column<DateTime>("timestamp with time zone", nullable: false),
                DueAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                PeriodStart       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                PeriodEnd         = t.Column<DateTime>("timestamp with time zone", nullable: false),
                Status            = t.Column<int>("integer", nullable: false),
                PdfStoragePath    = t.Column<string>("character varying(500)", maxLength: 500, nullable: true),
                PurchaseOrderRef  = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_Invoices", x => x.Id));
        mb.CreateIndex("IX_Invoices_TenantId_InvoiceNumber", "Invoices", new[] { "TenantId", "InvoiceNumber" }, unique: true);
        mb.CreateIndex("IX_Invoices_SubscriptionId", "Invoices", "SubscriptionId");
        mb.CreateIndex("IX_Invoices_Status", "Invoices", "Status");

        mb.CreateTable(
            name: "Payments",
            columns: t => new
            {
                Id                    = t.Column<Guid>("uuid", nullable: false),
                TenantId              = t.Column<Guid>("uuid", nullable: false),
                InvoiceId             = t.Column<Guid>("uuid", nullable: false),
                Provider              = t.Column<string>("character varying(20)", maxLength: 20, nullable: false),
                ProviderTransactionId = t.Column<string>("character varying(120)", maxLength: 120, nullable: false),
                ProviderEventId       = t.Column<string>("character varying(120)", maxLength: 120, nullable: true),
                Currency              = t.Column<string>("character varying(3)", maxLength: 3, nullable: false),
                AmountMinorUnits      = t.Column<long>("bigint", nullable: false),
                Status                = t.Column<int>("integer", nullable: false),
                FailureCode           = t.Column<string>("text", nullable: true),
                FailureMessage        = t.Column<string>("text", nullable: true),
                CreatedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CompletedAt           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                MethodSuffix          = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                MethodKind            = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
            },
            constraints: t => t.PrimaryKey("PK_Payments", x => x.Id));
        mb.CreateIndex("IX_Payments_InvoiceId", "Payments", "InvoiceId");
        mb.CreateIndex("IX_Payments_Provider_ProviderEventId",
            "Payments", new[] { "Provider", "ProviderEventId" }, unique: true,
            filter: "\"ProviderEventId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("Payments");
        mb.DropTable("Invoices");
        mb.DropTable("Subscriptions");
    }
}
