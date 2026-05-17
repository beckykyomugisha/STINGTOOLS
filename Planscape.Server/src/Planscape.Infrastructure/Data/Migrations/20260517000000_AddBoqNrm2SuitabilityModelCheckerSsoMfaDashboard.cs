using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Omnibus platform-gap migration — adds tables for:
///   • NRM2 BOQ engine (ClassificationSystems, ClassificationCodes, TakeoffRules,
///     QuantityLines, Nrm2Preambles, BoqPreambleAssignments, WorkPackages,
///     BoqBaselines, BoqVariations, BoqDocuments, Nrm2PreliminariesItems)
///   • ISO 19650 suitability state machine (SuitabilityTransitionRules,
///     SuitabilityTransitions)
///   • Solibri-grade model checker (ModelCheckRuleSets, ModelCheckRules,
///     ModelCheckRuns, ModelCheckResults)
///   • SSO + MFA (SsoConfigs, MfaEnrollments, MfaChallenges)
///   • Executive dashboard (KpiSnapshots, CoordinatorWorkloads, DashboardWidgets)
///   • Mobile offline 3D cache manifest (MobileOfflineModelManifests)
/// </remarks>
public partial class AddBoqNrm2SuitabilityModelCheckerSsoMfaDashboard : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        // ── Classification Systems ────────────────────────────────────────────
        mb.CreateTable(
            name: "ClassificationSystems",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                TenantId            = t.Column<Guid>("uuid", nullable: true),
                Code                = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Name                = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Authority           = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                Edition             = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                MeasurementProtocol = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                IsActive            = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                CreatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ClassificationSystems", x => x.Id);
            });

        mb.CreateIndex(
            name: "IX_ClassificationSystems_TenantId",
            table: "ClassificationSystems",
            column: "TenantId");

        // ── Classification Codes ──────────────────────────────────────────────
        mb.CreateTable(
            name: "ClassificationCodes",
            columns: t => new
            {
                Id           = t.Column<Guid>("uuid", nullable: false),
                TenantId     = t.Column<Guid>("uuid", nullable: false),
                SystemId     = t.Column<Guid>("uuid", nullable: false),
                ParentCodeId = t.Column<Guid>("uuid", nullable: true),
                Code         = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Title        = t.Column<string>("character varying(400)", maxLength: 400, nullable: false),
                Description  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Level        = t.Column<int>("integer", nullable: false, defaultValue: 1),
                Path         = t.Column<string>("character varying(400)", maxLength: 400, nullable: false, defaultValue: ""),
                DefaultUnit  = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                IsLeaf       = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                SortOrder    = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IsActive     = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                CreatedAt    = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt    = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ClassificationCodes", x => x.Id);
                t.ForeignKey(
                    name: "FK_ClassificationCodes_ClassificationSystems_SystemId",
                    column: x => x.SystemId,
                    principalTable: "ClassificationSystems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_ClassificationCodes_ClassificationCodes_ParentCodeId",
                    column: x => x.ParentCodeId,
                    principalTable: "ClassificationCodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        mb.CreateIndex(
            name: "IX_ClassificationCodes_SystemId_Code",
            table: "ClassificationCodes",
            columns: new[] { "SystemId", "Code" },
            unique: true);

        mb.CreateIndex(
            name: "IX_ClassificationCodes_SystemId",
            table: "ClassificationCodes",
            column: "SystemId");

        mb.CreateIndex(
            name: "IX_ClassificationCodes_ParentCodeId",
            table: "ClassificationCodes",
            column: "ParentCodeId");

        mb.CreateIndex(
            name: "IX_ClassificationCodes_TenantId",
            table: "ClassificationCodes",
            column: "TenantId");

        // ── Work Packages ─────────────────────────────────────────────────────
        mb.CreateTable(
            name: "WorkPackages",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                TenantId            = t.Column<Guid>("uuid", nullable: false),
                ProjectId           = t.Column<Guid>("uuid", nullable: false),
                Code                = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Name                = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Discipline          = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                SectionPrefixesJson = t.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
                Contractor          = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                EstimatedValue      = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                AwardedValue        = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                Currency            = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                Status              = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "Draft"),
                TenderIssuedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                AwardDate           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                StartOnSite         = t.Column<DateTime>("timestamp with time zone", nullable: true),
                PracticalCompletion = t.Column<DateTime>("timestamp with time zone", nullable: true),
                CreatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy           = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_WorkPackages", x => x.Id);
                t.ForeignKey(
                    name: "FK_WorkPackages_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex(
            name: "IX_WorkPackages_ProjectId_Code",
            table: "WorkPackages",
            columns: new[] { "ProjectId", "Code" },
            unique: true);

        mb.CreateIndex(
            name: "IX_WorkPackages_TenantId",
            table: "WorkPackages",
            column: "TenantId");

        // ── BOQ Documents ─────────────────────────────────────────────────────
        mb.CreateTable(
            name: "BoqDocuments",
            columns: t => new
            {
                Id                              = t.Column<Guid>("uuid", nullable: false),
                TenantId                        = t.Column<Guid>("uuid", nullable: false),
                ProjectId                       = t.Column<Guid>("uuid", nullable: false),
                Name                            = t.Column<string>("character varying(400)", maxLength: 400, nullable: false),
                Description                     = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                PrimaryClassificationSystemId   = t.Column<Guid>("uuid", nullable: false),
                SecondaryClassificationSystemId = t.Column<Guid>("uuid", nullable: true),
                MeasurementProtocol             = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "NRM2_RULES"),
                Currency                        = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                VatTreatment                    = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                ClientName                      = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                Architect                       = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                StructuralEngineer              = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                MepEngineer                     = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                CostManager                     = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                PrincipalContractor             = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                ContractForm                    = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                InsuranceParticulars            = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                DayworkLabourPct                = t.Column<decimal>("numeric(5,2)", precision: 5, scale: 2, nullable: true),
                DayworkMaterialsPct             = t.Column<decimal>("numeric(5,2)", precision: 5, scale: 2, nullable: true),
                DayworkPlantPct                 = t.Column<decimal>("numeric(5,2)", precision: 5, scale: 2, nullable: true),
                LocationFactor                  = t.Column<decimal>("numeric(5,3)", precision: 5, scale: 3, nullable: true),
                PricingBasis                    = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "Lump"),
                Status                          = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "Draft"),
                Revision                        = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                CreatedAt                       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt                       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy                       = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_BoqDocuments", x => x.Id);
                t.ForeignKey(
                    name: "FK_BoqDocuments_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_BoqDocuments_ClassificationSystems_PrimaryClassificationSystemId",
                    column: x => x.PrimaryClassificationSystemId,
                    principalTable: "ClassificationSystems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_BoqDocuments_ClassificationSystems_SecondaryClassificationSystemId",
                    column: x => x.SecondaryClassificationSystemId,
                    principalTable: "ClassificationSystems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex(
            name: "IX_BoqDocuments_ProjectId",
            table: "BoqDocuments",
            column: "ProjectId");

        mb.CreateIndex(
            name: "IX_BoqDocuments_TenantId",
            table: "BoqDocuments",
            column: "TenantId");

        // ── BOQ Baselines ─────────────────────────────────────────────────────
        mb.CreateTable(
            name: "BoqBaselines",
            columns: t => new
            {
                Id               = t.Column<Guid>("uuid", nullable: false),
                TenantId         = t.Column<Guid>("uuid", nullable: false),
                ProjectId        = t.Column<Guid>("uuid", nullable: false),
                Kind             = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Tender"),
                Name             = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description      = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                BaselinedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                LineCount        = t.Column<int>("integer", nullable: false, defaultValue: 0),
                TotalValue       = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                Currency         = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                DocumentRecordId = t.Column<Guid>("uuid", nullable: true),
                Checksum         = t.Column<string>("character varying(64)", maxLength: 64, nullable: true),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy        = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_BoqBaselines", x => x.Id);
                t.ForeignKey(
                    name: "FK_BoqBaselines_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_BoqBaselines_Documents_DocumentRecordId",
                    column: x => x.DocumentRecordId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex(
            name: "IX_BoqBaselines_ProjectId",
            table: "BoqBaselines",
            column: "ProjectId");

        mb.CreateIndex(
            name: "IX_BoqBaselines_TenantId",
            table: "BoqBaselines",
            column: "TenantId");

        // ── Takeoff Rules ─────────────────────────────────────────────────────
        mb.CreateTable(
            name: "TakeoffRules",
            columns: t => new
            {
                Id                   = t.Column<Guid>("uuid", nullable: false),
                TenantId             = t.Column<Guid>("uuid", nullable: false),
                ProjectId            = t.Column<Guid>("uuid", nullable: true),
                ClassificationCodeId = t.Column<Guid>("uuid", nullable: false),
                Name                 = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Priority             = t.Column<int>("integer", nullable: false, defaultValue: 100),
                Enabled              = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                IfcType              = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                Discipline           = t.Column<string>("character varying(10)", maxLength: 10, nullable: true),
                CategoryPattern      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                MaterialPattern      = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                PropertyFiltersJson  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Unit                 = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "nr"),
                QuantityFormula      = t.Column<string>("character varying(400)", maxLength: 400, nullable: false, defaultValue: "geom.count"),
                DescriptionTemplate  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                SpecificationGrade   = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                DeemedIncludedJson   = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                WastePercent         = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                CreatedAt            = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt            = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy            = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_TakeoffRules", x => x.Id);
                t.ForeignKey(
                    name: "FK_TakeoffRules_ClassificationCodes_ClassificationCodeId",
                    column: x => x.ClassificationCodeId,
                    principalTable: "ClassificationCodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_TakeoffRules_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex(
            name: "IX_TakeoffRules_TenantId_ProjectId_Priority",
            table: "TakeoffRules",
            columns: new[] { "TenantId", "ProjectId", "Priority" });

        mb.CreateIndex(
            name: "IX_TakeoffRules_ClassificationCodeId",
            table: "TakeoffRules",
            column: "ClassificationCodeId");

        mb.CreateIndex(
            name: "IX_TakeoffRules_TenantId",
            table: "TakeoffRules",
            column: "TenantId");

        // ── Quantity Lines ────────────────────────────────────────────────────
        mb.CreateTable(
            name: "QuantityLines",
            columns: t => new
            {
                Id                    = t.Column<Guid>("uuid", nullable: false),
                TenantId              = t.Column<Guid>("uuid", nullable: false),
                ProjectId             = t.Column<Guid>("uuid", nullable: false),
                BaselineId            = t.Column<Guid>("uuid", nullable: true),
                ClassificationCodeId  = t.Column<Guid>("uuid", nullable: false),
                TakeoffRuleId         = t.Column<Guid>("uuid", nullable: true),
                WorkPackageId         = t.Column<Guid>("uuid", nullable: true),
                ProjectModelId        = t.Column<Guid>("uuid", nullable: true),
                IfcGlobalId           = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                IfcType               = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                RevitElementId        = t.Column<long>("bigint", nullable: true),
                Level                 = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                Zone                  = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                SectionCode           = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: ""),
                ItemDescription       = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                Unit                  = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "nr"),
                NetQuantity           = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                WastePercent          = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                Quantity              = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                UnitRate              = t.Column<decimal>("numeric(18,4)", precision: 18, scale: 4, nullable: true),
                LineTotal             = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                Currency              = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                LineKind              = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Measured"),
                PricingBasis          = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Lump"),
                EmbodiedCarbonPerUnit = t.Column<double>("double precision", nullable: true),
                EmbodiedCarbonTotal   = t.Column<double>("double precision", nullable: true),
                Notes                 = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt             = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy             = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_QuantityLines", x => x.Id);
                t.ForeignKey(
                    name: "FK_QuantityLines_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_QuantityLines_BoqBaselines_BaselineId",
                    column: x => x.BaselineId,
                    principalTable: "BoqBaselines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                t.ForeignKey(
                    name: "FK_QuantityLines_ClassificationCodes_ClassificationCodeId",
                    column: x => x.ClassificationCodeId,
                    principalTable: "ClassificationCodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_QuantityLines_TakeoffRules_TakeoffRuleId",
                    column: x => x.TakeoffRuleId,
                    principalTable: "TakeoffRules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                t.ForeignKey(
                    name: "FK_QuantityLines_WorkPackages_WorkPackageId",
                    column: x => x.WorkPackageId,
                    principalTable: "WorkPackages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                t.ForeignKey(
                    name: "FK_QuantityLines_ProjectModels_ProjectModelId",
                    column: x => x.ProjectModelId,
                    principalTable: "ProjectModels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex("IX_QuantityLines_ProjectId_BaselineId", "QuantityLines", new[] { "ProjectId", "BaselineId" });
        mb.CreateIndex("IX_QuantityLines_ProjectId_SectionCode", "QuantityLines", new[] { "ProjectId", "SectionCode" });
        mb.CreateIndex("IX_QuantityLines_IfcGlobalId", "QuantityLines", "IfcGlobalId");
        mb.CreateIndex("IX_QuantityLines_ClassificationCodeId", "QuantityLines", "ClassificationCodeId");
        mb.CreateIndex("IX_QuantityLines_WorkPackageId", "QuantityLines", "WorkPackageId");
        mb.CreateIndex("IX_QuantityLines_TenantId", "QuantityLines", "TenantId");

        // ── NRM2 Preambles ────────────────────────────────────────────────────
        mb.CreateTable(
            name: "Nrm2Preambles",
            columns: t => new
            {
                Id             = t.Column<Guid>("uuid", nullable: false),
                TenantId       = t.Column<Guid>("uuid", nullable: false),
                NrmSectionCode = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                Group          = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "General"),
                Title          = t.Column<string>("character varying(400)", maxLength: 400, nullable: false),
                Body           = t.Column<string>("text", nullable: false, defaultValue: ""),
                References     = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                SortOrder      = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IsActive       = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                CreatedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Nrm2Preambles", x => x.Id);
            });

        mb.CreateIndex("IX_Nrm2Preambles_TenantId_NrmSectionCode", "Nrm2Preambles", new[] { "TenantId", "NrmSectionCode" });

        // ── BOQ Preamble Assignments ──────────────────────────────────────────
        mb.CreateTable(
            name: "BoqPreambleAssignments",
            columns: t => new
            {
                Id            = t.Column<Guid>("uuid", nullable: false),
                TenantId      = t.Column<Guid>("uuid", nullable: false),
                ProjectId     = t.Column<Guid>("uuid", nullable: false),
                BoqDocumentId = t.Column<Guid>("uuid", nullable: false),
                PreambleId    = t.Column<Guid>("uuid", nullable: false),
                OverrideBody  = t.Column<string>("text", nullable: true),
                SortOrder     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                CreatedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_BoqPreambleAssignments", x => x.Id);
                t.ForeignKey(
                    name: "FK_BoqPreambleAssignments_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_BoqPreambleAssignments_BoqDocuments_BoqDocumentId",
                    column: x => x.BoqDocumentId,
                    principalTable: "BoqDocuments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_BoqPreambleAssignments_Nrm2Preambles_PreambleId",
                    column: x => x.PreambleId,
                    principalTable: "Nrm2Preambles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_BoqPreambleAssignments_BoqDocumentId", "BoqPreambleAssignments", "BoqDocumentId");
        mb.CreateIndex("IX_BoqPreambleAssignments_TenantId", "BoqPreambleAssignments", "TenantId");

        // ── NRM2 Preliminaries Items ──────────────────────────────────────────
        mb.CreateTable(
            name: "Nrm2PreliminariesItems",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                ProjectId       = t.Column<Guid>("uuid", nullable: false),
                BoqDocumentId   = t.Column<Guid>("uuid", nullable: false),
                Code            = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Description     = t.Column<string>("character varying(400)", maxLength: 400, nullable: false),
                Kind            = t.Column<string>("character varying(30)", maxLength: 30, nullable: false, defaultValue: "Fixed"),
                Unit            = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "sum"),
                Quantity        = t.Column<double>("double precision", nullable: false, defaultValue: 1.0),
                UnitRate        = t.Column<decimal>("numeric(18,4)", precision: 18, scale: 4, nullable: true),
                LineTotal       = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                Currency        = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                DurationWeeks   = t.Column<int>("integer", nullable: true),
                PercentageBase  = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                Percentage      = t.Column<decimal>("numeric(5,2)", precision: 5, scale: 2, nullable: true),
                SortOrder       = t.Column<int>("integer", nullable: false, defaultValue: 0),
                CreatedAt       = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt       = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Nrm2PreliminariesItems", x => x.Id);
                t.ForeignKey(
                    name: "FK_Nrm2PreliminariesItems_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_Nrm2PreliminariesItems_BoqDocuments_BoqDocumentId",
                    column: x => x.BoqDocumentId,
                    principalTable: "BoqDocuments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_Nrm2PreliminariesItems_ProjectId", "Nrm2PreliminariesItems", "ProjectId");
        mb.CreateIndex("IX_Nrm2PreliminariesItems_BoqDocumentId", "Nrm2PreliminariesItems", "BoqDocumentId");
        mb.CreateIndex("IX_Nrm2PreliminariesItems_TenantId", "Nrm2PreliminariesItems", "TenantId");

        // ── BOQ Variations ────────────────────────────────────────────────────
        mb.CreateTable(
            name: "BoqVariations",
            columns: t => new
            {
                Id               = t.Column<Guid>("uuid", nullable: false),
                TenantId         = t.Column<Guid>("uuid", nullable: false),
                ProjectId        = t.Column<Guid>("uuid", nullable: false),
                BaselineId       = t.Column<Guid>("uuid", nullable: false),
                Reference        = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Kind             = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "VO"),
                Title            = t.Column<string>("character varying(400)", maxLength: 400, nullable: false),
                Description      = t.Column<string>("character varying(4000)", maxLength: 4000, nullable: true),
                InstructionRef   = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                BimIssueId       = t.Column<Guid>("uuid", nullable: true),
                NetValue         = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                Currency         = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                Status           = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                SubmittedAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                SubmittedBy      = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                ApprovedAt       = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ApprovedBy       = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                RejectionReason  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                LineDeltaJson    = t.Column<string>("text", nullable: true),
                CreatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy        = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_BoqVariations", x => x.Id);
                t.ForeignKey(
                    name: "FK_BoqVariations_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_BoqVariations_BoqBaselines_BaselineId",
                    column: x => x.BaselineId,
                    principalTable: "BoqBaselines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_BoqVariations_Issues_BimIssueId",
                    column: x => x.BimIssueId,
                    principalTable: "Issues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex("IX_BoqVariations_ProjectId_Reference", "BoqVariations", new[] { "ProjectId", "Reference" }, unique: true);
        mb.CreateIndex("IX_BoqVariations_BaselineId", "BoqVariations", "BaselineId");
        mb.CreateIndex("IX_BoqVariations_TenantId", "BoqVariations", "TenantId");

        // ── Suitability Transition Rules ──────────────────────────────────────
        mb.CreateTable(
            name: "SuitabilityTransitionRules",
            columns: t => new
            {
                Id                      = t.Column<Guid>("uuid", nullable: false),
                TenantId                = t.Column<Guid>("uuid", nullable: false),
                ProjectId               = t.Column<Guid>("uuid", nullable: true),
                FromCode                = t.Column<int>("integer", nullable: false),
                ToCode                  = t.Column<int>("integer", nullable: false),
                AllowedRoles            = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                RequiredApprovalChainId = t.Column<Guid>("uuid", nullable: true),
                PreconditionMask        = t.Column<int>("integer", nullable: false, defaultValue: 0),
                AutoTriggerAfterHours   = t.Column<int>("integer", nullable: true),
                Enabled                 = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                Priority                = t.Column<int>("integer", nullable: false, defaultValue: 100),
                Notes                   = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt               = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt               = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy               = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_SuitabilityTransitionRules", x => x.Id);
                t.ForeignKey(
                    name: "FK_SuitabilityTransitionRules_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_SuitabilityTransitionRules_TenantId_FromCode_ToCode", "SuitabilityTransitionRules", new[] { "TenantId", "FromCode", "ToCode" });
        mb.CreateIndex("IX_SuitabilityTransitionRules_TenantId", "SuitabilityTransitionRules", "TenantId");

        // ── Suitability Transitions ───────────────────────────────────────────
        mb.CreateTable(
            name: "SuitabilityTransitions",
            columns: t => new
            {
                Id               = t.Column<Guid>("uuid", nullable: false),
                TenantId         = t.Column<Guid>("uuid", nullable: false),
                ProjectId        = t.Column<Guid>("uuid", nullable: false),
                DocumentRecordId = t.Column<Guid>("uuid", nullable: false),
                FromCode         = t.Column<int>("integer", nullable: false),
                ToCode           = t.Column<int>("integer", nullable: false),
                RuleId           = t.Column<Guid>("uuid", nullable: true),
                Revision         = t.Column<string>("character varying(20)", maxLength: 20, nullable: true),
                Notes            = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                TriggerSource    = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "User"),
                TriggeredBy      = t.Column<string>("character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                TriggeredAt      = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_SuitabilityTransitions", x => x.Id);
                t.ForeignKey(
                    name: "FK_SuitabilityTransitions_Documents_DocumentRecordId",
                    column: x => x.DocumentRecordId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_SuitabilityTransitions_SuitabilityTransitionRules_RuleId",
                    column: x => x.RuleId,
                    principalTable: "SuitabilityTransitionRules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                t.ForeignKey(
                    name: "FK_SuitabilityTransitions_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_SuitabilityTransitions_DocumentRecordId", "SuitabilityTransitions", "DocumentRecordId");
        mb.CreateIndex("IX_SuitabilityTransitions_TenantId_TriggeredAt", "SuitabilityTransitions", new[] { "TenantId", "TriggeredAt" });
        mb.CreateIndex("IX_SuitabilityTransitions_TenantId", "SuitabilityTransitions", "TenantId");

        // ── Model Check Rule Sets ─────────────────────────────────────────────
        mb.CreateTable(
            name: "ModelCheckRuleSets",
            columns: t => new
            {
                Id          = t.Column<Guid>("uuid", nullable: false),
                TenantId    = t.Column<Guid>("uuid", nullable: false),
                ProjectId   = t.Column<Guid>("uuid", nullable: true),
                Code        = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Name        = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Version     = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "1.0"),
                Schedule    = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                Enabled     = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                Checksum    = t.Column<string>("character varying(64)", maxLength: 64, nullable: true),
                CreatedAt   = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt   = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy   = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ModelCheckRuleSets", x => x.Id);
                t.ForeignKey(
                    name: "FK_ModelCheckRuleSets_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_ModelCheckRuleSets_TenantId", "ModelCheckRuleSets", "TenantId");
        mb.CreateIndex("IX_ModelCheckRuleSets_ProjectId", "ModelCheckRuleSets", "ProjectId");

        // ── Model Check Rules ─────────────────────────────────────────────────
        mb.CreateTable(
            name: "ModelCheckRules",
            columns: t => new
            {
                Id                  = t.Column<Guid>("uuid", nullable: false),
                TenantId            = t.Column<Guid>("uuid", nullable: false),
                RuleSetId           = t.Column<Guid>("uuid", nullable: false),
                Code                = t.Column<string>("character varying(40)", maxLength: 40, nullable: false),
                Name                = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Description         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Kind                = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "PropertyRequired"),
                Severity            = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Major"),
                AppliesToIfcTypes   = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                AppliesToDiscipline = t.Column<string>("character varying(40)", maxLength: 40, nullable: true),
                ParamsJson          = t.Column<string>("text", nullable: false, defaultValue: "{}"),
                AutoAction          = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "None"),
                Enabled             = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                SortOrder           = t.Column<int>("integer", nullable: false, defaultValue: 0),
                CreatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy           = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ModelCheckRules", x => x.Id);
                t.ForeignKey(
                    name: "FK_ModelCheckRules_ModelCheckRuleSets_RuleSetId",
                    column: x => x.RuleSetId,
                    principalTable: "ModelCheckRuleSets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_ModelCheckRules_RuleSetId", "ModelCheckRules", "RuleSetId");
        mb.CreateIndex("IX_ModelCheckRules_TenantId", "ModelCheckRules", "TenantId");

        // ── Model Check Runs ──────────────────────────────────────────────────
        mb.CreateTable(
            name: "ModelCheckRuns",
            columns: t => new
            {
                Id                   = t.Column<Guid>("uuid", nullable: false),
                TenantId             = t.Column<Guid>("uuid", nullable: false),
                ProjectId            = t.Column<Guid>("uuid", nullable: false),
                RuleSetId            = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId       = t.Column<Guid>("uuid", nullable: true),
                StartedAt            = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CompletedAt          = t.Column<DateTime>("timestamp with time zone", nullable: true),
                Status               = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Queued"),
                TotalRulesEvaluated  = t.Column<int>("integer", nullable: false, defaultValue: 0),
                TotalElementsChecked = t.Column<int>("integer", nullable: false, defaultValue: 0),
                FindingsCount        = t.Column<int>("integer", nullable: false, defaultValue: 0),
                CriticalCount        = t.Column<int>("integer", nullable: false, defaultValue: 0),
                MajorCount           = t.Column<int>("integer", nullable: false, defaultValue: 0),
                MinorCount           = t.Column<int>("integer", nullable: false, defaultValue: 0),
                InfoCount            = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ErrorMessage         = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                TriggeredBy          = t.Column<string>("character varying(40)", maxLength: 40, nullable: false, defaultValue: "manual"),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ModelCheckRuns", x => x.Id);
                t.ForeignKey(
                    name: "FK_ModelCheckRuns_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                t.ForeignKey(
                    name: "FK_ModelCheckRuns_ModelCheckRuleSets_RuleSetId",
                    column: x => x.RuleSetId,
                    principalTable: "ModelCheckRuleSets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_ModelCheckRuns_ProjectModels_ProjectModelId",
                    column: x => x.ProjectModelId,
                    principalTable: "ProjectModels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex("IX_ModelCheckRuns_ProjectId_StartedAt", "ModelCheckRuns", new[] { "ProjectId", "StartedAt" });
        mb.CreateIndex("IX_ModelCheckRuns_TenantId", "ModelCheckRuns", "TenantId");

        // ── Model Check Results ───────────────────────────────────────────────
        mb.CreateTable(
            name: "ModelCheckResults",
            columns: t => new
            {
                Id             = t.Column<Guid>("uuid", nullable: false),
                TenantId       = t.Column<Guid>("uuid", nullable: false),
                ProjectId      = t.Column<Guid>("uuid", nullable: false),
                RunId          = t.Column<Guid>("uuid", nullable: false),
                RuleId         = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId = t.Column<Guid>("uuid", nullable: true),
                IfcGlobalId    = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                IfcType        = t.Column<string>("character varying(100)", maxLength: 100, nullable: true),
                ElementName    = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                Level          = t.Column<string>("character varying(80)", maxLength: 80, nullable: true),
                Severity       = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Major"),
                Message        = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                Suggestion     = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                Status         = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                BimIssueId     = t.Column<Guid>("uuid", nullable: true),
                DetectedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
                ResolvedAt     = t.Column<DateTime>("timestamp with time zone", nullable: true),
                ResolvedBy     = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ModelCheckResults", x => x.Id);
                t.ForeignKey("FK_ModelCheckResults_Projects_ProjectId", x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_ModelCheckResults_ModelCheckRuns_RunId", x => x.RunId, "ModelCheckRuns", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_ModelCheckResults_ModelCheckRules_RuleId", x => x.RuleId, "ModelCheckRules", "Id", onDelete: ReferentialAction.Restrict);
                t.ForeignKey("FK_ModelCheckResults_Issues_BimIssueId", x => x.BimIssueId, "Issues", "Id", onDelete: ReferentialAction.SetNull);
                t.ForeignKey("FK_ModelCheckResults_ProjectModels_ProjectModelId", x => x.ProjectModelId, "ProjectModels", "Id", onDelete: ReferentialAction.SetNull);
            });

        mb.CreateIndex("IX_ModelCheckResults_ProjectId_Status", "ModelCheckResults", new[] { "ProjectId", "Status" });
        mb.CreateIndex("IX_ModelCheckResults_RunId", "ModelCheckResults", "RunId");
        mb.CreateIndex("IX_ModelCheckResults_TenantId", "ModelCheckResults", "TenantId");

        // ── SSO Configs ───────────────────────────────────────────────────────
        mb.CreateTable(
            name: "SsoConfigs",
            columns: t => new
            {
                Id                         = t.Column<Guid>("uuid", nullable: false),
                TenantId                   = t.Column<Guid>("uuid", nullable: false),
                Name                       = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
                Protocol                   = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "OIDC"),
                Enabled                    = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                EmailDomains               = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                RequireSso                 = t.Column<bool>("boolean", nullable: false, defaultValue: false),
                OidcIssuer                 = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcClientId               = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcClientSecretEncrypted  = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                OidcAuthorizationEndpoint  = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcTokenEndpoint          = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcUserInfoEndpoint       = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcJwksUri                = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                OidcScopes                 = t.Column<string>("character varying(200)", maxLength: 200, nullable: false, defaultValue: "openid profile email"),
                SamlEntityId               = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                SamlSsoUrl                 = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                SamlSloUrl                 = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                SamlIdpCertificate         = t.Column<string>("text", nullable: true),
                SamlSpCertificateEncrypted = t.Column<string>("text", nullable: true),
                AttributeMapJson           = t.Column<string>("text", nullable: true),
                GroupRoleMapJson           = t.Column<string>("text", nullable: true),
                ScimEndpoint               = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                ScimBearerTokenEncrypted   = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt                  = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt                  = t.Column<DateTime>("timestamp with time zone", nullable: false),
                CreatedBy                  = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                LastSuccessfulLoginAt      = t.Column<DateTime>("timestamp with time zone", nullable: true),
                LastFailedLoginAt          = t.Column<DateTime>("timestamp with time zone", nullable: true),
                LastFailureReason          = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_SsoConfigs", x => x.Id);
                t.ForeignKey("FK_SsoConfigs_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_SsoConfigs_TenantId", "SsoConfigs", "TenantId");

        // ── MFA Enrollments ───────────────────────────────────────────────────
        mb.CreateTable(
            name: "MfaEnrollments",
            columns: t => new
            {
                Id                = t.Column<Guid>("uuid", nullable: false),
                TenantId          = t.Column<Guid>("uuid", nullable: false),
                UserId            = t.Column<Guid>("uuid", nullable: false),
                Method            = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "TOTP"),
                SecretEncrypted   = t.Column<string>("character varying(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                RecoveryCodesJson = t.Column<string>("text", nullable: false, defaultValue: "[]"),
                EnrolledAt        = t.Column<DateTime>("timestamp with time zone", nullable: false),
                LastVerifiedAt    = t.Column<DateTime>("timestamp with time zone", nullable: true),
                RevokedAt         = t.Column<DateTime>("timestamp with time zone", nullable: true),
                DeviceLabel       = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_MfaEnrollments", x => x.Id);
                t.ForeignKey("FK_MfaEnrollments_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_MfaEnrollments_UserId", "MfaEnrollments", "UserId");
        mb.CreateIndex("IX_MfaEnrollments_TenantId", "MfaEnrollments", "TenantId");

        // ── MFA Challenges ────────────────────────────────────────────────────
        mb.CreateTable(
            name: "MfaChallenges",
            columns: t => new
            {
                Id            = t.Column<Guid>("uuid", nullable: false),
                TenantId      = t.Column<Guid>("uuid", nullable: false),
                UserId        = t.Column<Guid>("uuid", nullable: false),
                EnrollmentId  = t.Column<Guid>("uuid", nullable: true),
                Method        = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "TOTP"),
                Succeeded     = t.Column<bool>("boolean", nullable: false),
                ClientIp      = t.Column<string>("character varying(50)", maxLength: 50, nullable: true),
                UserAgent     = t.Column<string>("character varying(400)", maxLength: 400, nullable: true),
                FailureReason = t.Column<string>("character varying(200)", maxLength: 200, nullable: true),
                CreatedAt     = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_MfaChallenges", x => x.Id);
                t.ForeignKey("FK_MfaChallenges_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_MfaChallenges_UserId_CreatedAt", "MfaChallenges", new[] { "UserId", "CreatedAt" });
        mb.CreateIndex("IX_MfaChallenges_TenantId", "MfaChallenges", "TenantId");

        // ── KPI Snapshots ─────────────────────────────────────────────────────
        mb.CreateTable(
            name: "KpiSnapshots",
            columns: t => new
            {
                Id                         = t.Column<Guid>("uuid", nullable: false),
                TenantId                   = t.Column<Guid>("uuid", nullable: false),
                ProjectId                  = t.Column<Guid>("uuid", nullable: false),
                SnapshotDate               = t.Column<DateTime>("timestamp with time zone", nullable: false),
                IssuesOpen                 = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssuesOverdue              = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssuesCreatedThisWeek      = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssuesResolvedThisWeek     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssueAgeAvgDays            = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                IssueSlaCompliancePct      = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                ClashesOpen                = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ClashesCritical            = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ClashesResolvedThisWeek    = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ModelCheckFindingsOpen     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ModelCheckFindingsCritical = t.Column<int>("integer", nullable: false, defaultValue: 0),
                LastModelCheckAt           = t.Column<DateTime>("timestamp with time zone", nullable: true),
                DocumentsTotal             = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DocumentsWip               = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DocumentsShared            = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DocumentsPublished         = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DocumentsOverdueReview     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                DocumentApprovalAvgHours   = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                TagCompliancePct           = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                WarningsPct                = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                BoqTotalValue              = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                BoqCommittedValue          = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                BoqActualValue             = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                BoqForecastValue           = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                VariationsNetValue         = t.Column<decimal>("numeric(18,2)", precision: 18, scale: 2, nullable: true),
                VariationsCount            = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ProgrammeProgressPct       = t.Column<double>("double precision", nullable: false, defaultValue: 0.0),
                ProgrammeMilestonesDue     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                ProgrammeMilestonesAtRisk  = t.Column<int>("integer", nullable: false, defaultValue: 0),
                EmbodiedCarbonKgCo2e       = t.Column<double>("double precision", nullable: true),
                EmbodiedCarbonPerM2        = t.Column<double>("double precision", nullable: true),
                Currency                   = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "GBP"),
                CreatedAt                  = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_KpiSnapshots", x => x.Id);
                t.ForeignKey("FK_KpiSnapshots_Projects_ProjectId", x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_KpiSnapshots_ProjectId_SnapshotDate", "KpiSnapshots", new[] { "ProjectId", "SnapshotDate" }, unique: true);
        mb.CreateIndex("IX_KpiSnapshots_TenantId", "KpiSnapshots", "TenantId");

        // ── Coordinator Workloads ─────────────────────────────────────────────
        mb.CreateTable(
            name: "CoordinatorWorkloads",
            columns: t => new
            {
                Id                     = t.Column<Guid>("uuid", nullable: false),
                TenantId               = t.Column<Guid>("uuid", nullable: false),
                UserId                 = t.Column<Guid>("uuid", nullable: false),
                WeekStarting           = t.Column<DateTime>("timestamp with time zone", nullable: false),
                OpenIssuesAssigned     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                OpenIssuesCritical     = t.Column<int>("integer", nullable: false, defaultValue: 0),
                OpenIssuesMajor        = t.Column<int>("integer", nullable: false, defaultValue: 0),
                OpenIssuesOverdue      = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssuesResolvedThisWeek = t.Column<int>("integer", nullable: false, defaultValue: 0),
                IssuesCreatedThisWeek  = t.Column<int>("integer", nullable: false, defaultValue: 0),
                OpenClashesAssigned    = t.Column<int>("integer", nullable: false, defaultValue: 0),
                OpenModelCheckFindings = t.Column<int>("integer", nullable: false, defaultValue: 0),
                PendingApprovalsCount  = t.Column<int>("integer", nullable: false, defaultValue: 0),
                WorkloadIndex          = t.Column<int>("integer", nullable: false, defaultValue: 0),
                LoadBand               = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Balanced"),
                CreatedAt              = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_CoordinatorWorkloads", x => x.Id);
                t.ForeignKey("FK_CoordinatorWorkloads_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_CoordinatorWorkloads_UserId_WeekStarting", "CoordinatorWorkloads", new[] { "UserId", "WeekStarting" }, unique: true);
        mb.CreateIndex("IX_CoordinatorWorkloads_TenantId", "CoordinatorWorkloads", "TenantId");

        // ── Dashboard Widgets ─────────────────────────────────────────────────
        mb.CreateTable(
            name: "DashboardWidgets",
            columns: t => new
            {
                Id         = t.Column<Guid>("uuid", nullable: false),
                TenantId   = t.Column<Guid>("uuid", nullable: false),
                UserId     = t.Column<Guid>("uuid", nullable: true),
                ProjectId  = t.Column<Guid>("uuid", nullable: true),
                Kind       = t.Column<string>("character varying(60)", maxLength: 60, nullable: false, defaultValue: "KpiCard"),
                Title      = t.Column<string>("character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                ConfigJson = t.Column<string>("text", nullable: false, defaultValue: "{}"),
                SortOrder  = t.Column<int>("integer", nullable: false, defaultValue: 0),
                GridCol    = t.Column<int>("integer", nullable: true),
                GridRow    = t.Column<int>("integer", nullable: true),
                GridWidth  = t.Column<int>("integer", nullable: true),
                GridHeight = t.Column<int>("integer", nullable: true),
                Pinned     = t.Column<bool>("boolean", nullable: false, defaultValue: true),
                CreatedAt  = t.Column<DateTime>("timestamp with time zone", nullable: false),
                UpdatedAt  = t.Column<DateTime>("timestamp with time zone", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                t.ForeignKey("FK_DashboardWidgets_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_DashboardWidgets_Projects_ProjectId", x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_DashboardWidgets_TenantId_UserId", "DashboardWidgets", new[] { "TenantId", "UserId" });
        mb.CreateIndex("IX_DashboardWidgets_TenantId", "DashboardWidgets", "TenantId");

        // ── Mobile Offline Model Manifests ────────────────────────────────────
        mb.CreateTable(
            name: "MobileOfflineModelManifests",
            columns: t => new
            {
                Id              = t.Column<Guid>("uuid", nullable: false),
                TenantId        = t.Column<Guid>("uuid", nullable: false),
                ProjectId       = t.Column<Guid>("uuid", nullable: false),
                UserId          = t.Column<Guid>("uuid", nullable: false),
                ProjectModelId  = t.Column<Guid>("uuid", nullable: false),
                DeviceId        = t.Column<string>("character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                SceneNodeId     = t.Column<Guid>("uuid", nullable: false),
                ContentHash     = t.Column<string>("character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                CachedSizeBytes = t.Column<long>("bigint", nullable: false, defaultValue: 0L),
                Format          = t.Column<string>("character varying(10)", maxLength: 10, nullable: false, defaultValue: "Glb"),
                Tier            = t.Column<string>("character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                FirstCachedAt   = t.Column<DateTime>("timestamp with time zone", nullable: false),
                LastAccessedAt  = t.Column<DateTime>("timestamp with time zone", nullable: false),
                LastSyncedAt    = t.Column<DateTime>("timestamp with time zone", nullable: false),
                IsStale         = t.Column<bool>("boolean", nullable: false, defaultValue: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_MobileOfflineModelManifests", x => x.Id);
                t.ForeignKey("FK_MobileOfflineModelManifests_Projects_ProjectId", x => x.ProjectId, "Projects", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_MobileOfflineModelManifests_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_MobileOfflineModelManifests_ProjectModels_ProjectModelId", x => x.ProjectModelId, "ProjectModels", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_MobileOfflineModelManifests_SceneNodes_SceneNodeId", x => x.SceneNodeId, "SceneNodes", "Id", onDelete: ReferentialAction.Cascade);
            });

        mb.CreateIndex("IX_MobileOfflineModelManifests_UserId_DeviceId_ProjectModelId", "MobileOfflineModelManifests", new[] { "UserId", "DeviceId", "ProjectModelId" });
        mb.CreateIndex("IX_MobileOfflineModelManifests_SceneNodeId", "MobileOfflineModelManifests", "SceneNodeId");
        mb.CreateIndex("IX_MobileOfflineModelManifests_TenantId", "MobileOfflineModelManifests", "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("MobileOfflineModelManifests");
        mb.DropTable("DashboardWidgets");
        mb.DropTable("CoordinatorWorkloads");
        mb.DropTable("KpiSnapshots");
        mb.DropTable("MfaChallenges");
        mb.DropTable("MfaEnrollments");
        mb.DropTable("SsoConfigs");
        mb.DropTable("ModelCheckResults");
        mb.DropTable("ModelCheckRuns");
        mb.DropTable("ModelCheckRules");
        mb.DropTable("ModelCheckRuleSets");
        mb.DropTable("SuitabilityTransitions");
        mb.DropTable("SuitabilityTransitionRules");
        mb.DropTable("BoqVariations");
        mb.DropTable("Nrm2PreliminariesItems");
        mb.DropTable("BoqPreambleAssignments");
        mb.DropTable("Nrm2Preambles");
        mb.DropTable("QuantityLines");
        mb.DropTable("TakeoffRules");
        mb.DropTable("BoqBaselines");
        mb.DropTable("BoqDocuments");
        mb.DropTable("WorkPackages");
        mb.DropTable("ClassificationCodes");
        mb.DropTable("ClassificationSystems");
    }
}
