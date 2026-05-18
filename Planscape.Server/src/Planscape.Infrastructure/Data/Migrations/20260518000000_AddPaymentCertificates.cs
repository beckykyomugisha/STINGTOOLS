using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <summary>
/// Adds the PaymentCertificates table for Phase 184k / P5.1 contract
/// administration. Backs the mobile payment-cert detail/sign workflow
/// (Phase 184i / P7) and the plugin Cost_RunWorkflow preset that pushes
/// monthly valuations from Revit.
/// </summary>
public partial class AddPaymentCertificates : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "PaymentCertificates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                CertNumber = table.Column<int>(type: "integer", nullable: false),
                ContractRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "NEC4"),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                ValuationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IssuedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "GBP"),
                ContractorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                EmployerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                ProjectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                RetentionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false, defaultValue: 3.0m),
                HalfRetentionAtPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false, defaultValue: 100.0m),
                EffectiveRetentionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false, defaultValue: 3.0m),
                GrossValuation = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                RetentionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                OtherDeductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                NetThisCert = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                VatPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false, defaultValue: 20.0m),
                VatAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                TotalPayable = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                SupersededByCertNumber = table.Column<int>(type: "integer", nullable: true),
                SignedByContractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ContractorSignedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignedByEmployer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EmployerSignedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                SovJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentCertificates", x => x.Id);
                table.ForeignKey(
                    name: "FK_PaymentCertificates_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex(
            name: "IX_PaymentCertificates_ProjectId_ContractRef_CertNumber",
            table: "PaymentCertificates",
            columns: new[] { "ProjectId", "ContractRef", "CertNumber" },
            unique: true);

        mb.CreateIndex(
            name: "IX_PaymentCertificates_ProjectId_Status",
            table: "PaymentCertificates",
            columns: new[] { "ProjectId", "Status" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "PaymentCertificates");
    }
}
