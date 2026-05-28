using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Planscape.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaggedElementId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetTag = table.Column<string>(type: "text", nullable: false),
                    AssetName = table.Column<string>(type: "text", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: true),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    SerialNumber = table.Column<string>(type: "text", nullable: true),
                    BarCode = table.Column<string>(type: "text", nullable: true),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    FamilyName = table.Column<string>(type: "text", nullable: false),
                    UniclassCode = table.Column<string>(type: "text", nullable: true),
                    OmniClassCode = table.Column<string>(type: "text", nullable: true),
                    CobieType = table.Column<string>(type: "text", nullable: true),
                    CobieSpace = table.Column<string>(type: "text", nullable: true),
                    Discipline = table.Column<string>(type: "text", nullable: false),
                    SystemCode = table.Column<string>(type: "text", nullable: false),
                    FunctionCode = table.Column<string>(type: "text", nullable: false),
                    ProductCode = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    LifecycleStatus = table.Column<string>(type: "text", nullable: false),
                    CriticalityRating = table.Column<string>(type: "text", nullable: true),
                    InstallationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommissioningDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedLifeYears = table.Column<int>(type: "integer", nullable: true),
                    ExpectedReplacementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConditionGrade = table.Column<string>(type: "text", nullable: false),
                    ConditionScore = table.Column<double>(type: "double precision", nullable: true),
                    WarrantyProvider = table.Column<string>(type: "text", nullable: true),
                    WarrantyStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WarrantyEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WarrantyDurationMonths = table.Column<int>(type: "integer", nullable: true),
                    CapitalCost = table.Column<decimal>(type: "numeric", nullable: true),
                    ReplacementCost = table.Column<decimal>(type: "numeric", nullable: true),
                    AnnualMaintenanceCost = table.Column<decimal>(type: "numeric", nullable: true),
                    CostCurrency = table.Column<string>(type: "text", nullable: true),
                    Building = table.Column<string>(type: "text", nullable: true),
                    Floor = table.Column<string>(type: "text", nullable: true),
                    Room = table.Column<string>(type: "text", nullable: true),
                    Zone = table.Column<string>(type: "text", nullable: true),
                    SensorId = table.Column<string>(type: "text", nullable: true),
                    DigitalTwinId = table.Column<string>(type: "text", nullable: true),
                    LastSensorReading = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SensorDataJson = table.Column<string>(type: "text", nullable: true),
                    EnergyConsumptionKwh = table.Column<double>(type: "double precision", nullable: true),
                    EmbodiedCarbonKgCo2 = table.Column<double>(type: "double precision", nullable: true),
                    DocumentRefsJson = table.Column<string>(type: "text", nullable: true),
                    SparePartsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    PrevHash = table.Column<string>(type: "text", nullable: true),
                    RowHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationSystems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Authority = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Edition = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    MeasurementProtocol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardWidgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    GridCol = table.Column<int>(type: "integer", nullable: true),
                    GridRow = table.Column<int>(type: "integer", nullable: true),
                    GridWidth = table.Column<int>(type: "integer", nullable: true),
                    GridHeight = table.Column<int>(type: "integer", nullable: true),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalIdRegistry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IfcGlobalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ArchiCadGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RevitUniqueId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TeklaGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    IfcType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ElementName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    NormalizedLevelName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MappingStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MappedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalIdRegistry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthcareAntiLigatureAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomBimId = table.Column<string>(type: "text", nullable: false),
                    RoomName = table.Column<string>(type: "text", nullable: false),
                    FittingType = table.Column<string>(type: "text", nullable: false),
                    Pass = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PhotoBlobId = table.Column<string>(type: "text", nullable: false),
                    GpsLat = table.Column<double>(type: "double precision", nullable: true),
                    GpsLon = table.Column<double>(type: "double precision", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthcareAntiLigatureAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthcareMgasVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Zone = table.Column<string>(type: "text", nullable: false),
                    GasCode = table.Column<string>(type: "text", nullable: false),
                    VerifierName = table.Column<string>(type: "text", nullable: false),
                    VerifierAsse6030Id = table.Column<string>(type: "text", nullable: false),
                    CertReference = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OverallPass = table.Column<bool>(type: "boolean", nullable: false),
                    PassCount = table.Column<int>(type: "integer", nullable: false),
                    FailCount = table.Column<int>(type: "integer", nullable: false),
                    CheckResultsJson = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthcareMgasVerifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthcarePressureLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomBimId = table.Column<string>(type: "text", nullable: false),
                    RoomName = table.Column<string>(type: "text", nullable: false),
                    RoomClass = table.Column<string>(type: "text", nullable: false),
                    DesignRegime = table.Column<string>(type: "text", nullable: false),
                    DesignDeltaPa = table.Column<double>(type: "double precision", nullable: false),
                    LiveDeltaPa = table.Column<double>(type: "double precision", nullable: false),
                    InBand = table.Column<bool>(type: "boolean", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthcarePressureLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthcareRdsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomBimId = table.Column<string>(type: "text", nullable: false),
                    RoomNumber = table.Column<string>(type: "text", nullable: false),
                    RoomName = table.Column<string>(type: "text", nullable: false),
                    RoomClass = table.Column<string>(type: "text", nullable: false),
                    HbnRef = table.Column<string>(type: "text", nullable: false),
                    AdbCode = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: false),
                    DocxRelPath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthcareRdsSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HvacLoadSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemId = table.Column<string>(type: "text", nullable: false),
                    ClimateSiteId = table.Column<string>(type: "text", nullable: false),
                    ClimateSiteLabel = table.Column<string>(type: "text", nullable: false),
                    ConstructionProfile = table.Column<string>(type: "text", nullable: false),
                    RtsClass = table.Column<string>(type: "text", nullable: false),
                    Cooling = table.Column<bool>(type: "boolean", nullable: false),
                    BlockSensibleW = table.Column<double>(type: "double precision", nullable: false),
                    BlockLatentW = table.Column<double>(type: "double precision", nullable: false),
                    BlockHour = table.Column<int>(type: "integer", nullable: false),
                    SumOfPeaksSensibleW = table.Column<double>(type: "double precision", nullable: false),
                    DiversityFactor = table.Column<double>(type: "double precision", nullable: false),
                    ZoneCount = table.Column<int>(type: "integer", nullable: false),
                    ZonesJson = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HvacLoadSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HvacNcSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PathLabel = table.Column<string>(type: "text", nullable: false),
                    ReceiverRoom = table.Column<string>(type: "text", nullable: false),
                    PredictedNc = table.Column<int>(type: "integer", nullable: false),
                    TargetNc = table.Column<int>(type: "integer", nullable: false),
                    PathFlowLs = table.Column<double>(type: "double precision", nullable: false),
                    PathPressureDropPa = table.Column<double>(type: "double precision", nullable: false),
                    OctaveLpJson = table.Column<string>(type: "text", nullable: false),
                    ElementBreakdownJson = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HvacNcSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HvacRefrigerantSizing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefrigerantId = table.Column<string>(type: "text", nullable: false),
                    Leg = table.Column<string>(type: "text", nullable: false),
                    CapacityKw = table.Column<double>(type: "double precision", nullable: false),
                    EquivLengthM = table.Column<double>(type: "double precision", nullable: false),
                    LiftM = table.Column<double>(type: "double precision", nullable: false),
                    HasVerticalRiser = table.Column<bool>(type: "boolean", nullable: false),
                    MaxPressureDropKpa = table.Column<double>(type: "double precision", nullable: false),
                    SubcoolingReserveK = table.Column<double>(type: "double precision", nullable: false),
                    Ok = table.Column<bool>(type: "boolean", nullable: false),
                    SelectedBoreMm = table.Column<double>(type: "double precision", nullable: false),
                    VelocityMs = table.Column<double>(type: "double precision", nullable: false),
                    PressureDropKpa = table.Column<double>(type: "double precision", nullable: false),
                    LiftPenaltyKpa = table.Column<double>(type: "double precision", nullable: false),
                    SatTempDropK = table.Column<double>(type: "double precision", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HvacRefrigerantSizing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HvacSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Inspected = table.Column<int>(type: "integer", nullable: false),
                    Pass = table.Column<int>(type: "integer", nullable: false),
                    Warn = table.Column<int>(type: "integer", nullable: false),
                    Fail = table.Column<int>(type: "integer", nullable: false),
                    TotalKw = table.Column<double>(type: "double precision", nullable: false),
                    WorstValue = table.Column<double>(type: "double precision", nullable: false),
                    Rag = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HvacSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IfcAlignmentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<string>(type: "text", nullable: true),
                    IfcSiteGuid = table.Column<string>(type: "text", nullable: true),
                    LengthUnit = table.Column<string>(type: "text", nullable: true),
                    TrueNorthDegrees = table.Column<double>(type: "double precision", nullable: true),
                    SurveyEasting = table.Column<double>(type: "double precision", nullable: true),
                    SurveyNorthing = table.Column<double>(type: "double precision", nullable: true),
                    SurveyElevation = table.Column<double>(type: "double precision", nullable: true),
                    HasMapConversion = table.Column<bool>(type: "boolean", nullable: false),
                    HasProjectedCrs = table.Column<bool>(type: "boolean", nullable: false),
                    CrsName = table.Column<string>(type: "text", nullable: true),
                    MapConversionScale = table.Column<double>(type: "double precision", nullable: true),
                    MapConversionRotationDeg = table.Column<double>(type: "double precision", nullable: true),
                    GeometryCentroidX = table.Column<double>(type: "double precision", nullable: true),
                    GeometryCentroidY = table.Column<double>(type: "double precision", nullable: true),
                    GeometryCentroidZ = table.Column<double>(type: "double precision", nullable: true),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    FindingsJson = table.Column<string>(type: "text", nullable: false),
                    ValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcAlignmentReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IfcElementSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IfcGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IfcType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Storey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    MinX = table.Column<double>(type: "double precision", nullable: true),
                    MinY = table.Column<double>(type: "double precision", nullable: true),
                    MinZ = table.Column<double>(type: "double precision", nullable: true),
                    MaxX = table.Column<double>(type: "double precision", nullable: true),
                    MaxY = table.Column<double>(type: "double precision", nullable: true),
                    MaxZ = table.Column<double>(type: "double precision", nullable: true),
                    PropertiesHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SnapshotAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadSequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcElementSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderInvoiceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    TaxMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PdfStoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PurchaseOrderRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuesOpen = table.Column<int>(type: "integer", nullable: false),
                    IssuesOverdue = table.Column<int>(type: "integer", nullable: false),
                    IssuesCreatedThisWeek = table.Column<int>(type: "integer", nullable: false),
                    IssuesResolvedThisWeek = table.Column<int>(type: "integer", nullable: false),
                    IssueAgeAvgDays = table.Column<double>(type: "double precision", nullable: false),
                    IssueSlaCompliancePct = table.Column<double>(type: "double precision", nullable: false),
                    ClashesOpen = table.Column<int>(type: "integer", nullable: false),
                    ClashesCritical = table.Column<int>(type: "integer", nullable: false),
                    ClashesResolvedThisWeek = table.Column<int>(type: "integer", nullable: false),
                    ModelCheckFindingsOpen = table.Column<int>(type: "integer", nullable: false),
                    ModelCheckFindingsCritical = table.Column<int>(type: "integer", nullable: false),
                    LastModelCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentsTotal = table.Column<int>(type: "integer", nullable: false),
                    DocumentsWip = table.Column<int>(type: "integer", nullable: false),
                    DocumentsShared = table.Column<int>(type: "integer", nullable: false),
                    DocumentsPublished = table.Column<int>(type: "integer", nullable: false),
                    DocumentsOverdueReview = table.Column<int>(type: "integer", nullable: false),
                    DocumentApprovalAvgHours = table.Column<double>(type: "double precision", nullable: false),
                    TagCompliancePct = table.Column<double>(type: "double precision", nullable: false),
                    WarningsPct = table.Column<double>(type: "double precision", nullable: false),
                    BoqTotalValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    BoqCommittedValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    BoqActualValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    BoqForecastValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    VariationsNetValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    VariationsCount = table.Column<int>(type: "integer", nullable: false),
                    ProgrammeProgressPct = table.Column<double>(type: "double precision", nullable: false),
                    ProgrammeMilestonesDue = table.Column<int>(type: "integer", nullable: false),
                    ProgrammeMilestonesAtRisk = table.Column<int>(type: "integer", nullable: false),
                    EmbodiedCarbonKgCo2e = table.Column<double>(type: "double precision", nullable: true),
                    EmbodiedCarbonPerM2 = table.Column<double>(type: "double precision", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfaChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelCheckRuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Schedule = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCheckRuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelMarkups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Thickness = table.Column<float>(type: "real", nullable: false),
                    PolylinesJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelMarkups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nrm2Preambles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrmSectionCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    References = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nrm2Preambles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nrm2PreliminariesItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoqDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DurationWeeks = table.Column<int>(type: "integer", nullable: true),
                    PercentageBase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Percentage = table.Column<decimal>(type: "numeric(5,3)", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nrm2PreliminariesItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    FailureMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MethodSuffix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MethodKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PenetrationSignoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenetrationControlNumber = table.Column<string>(type: "text", nullable: false),
                    PfvUuid = table.Column<string>(type: "text", nullable: false),
                    HostType = table.Column<string>(type: "text", nullable: false),
                    FireRating = table.Column<string>(type: "text", nullable: false),
                    Certification = table.Column<string>(type: "text", nullable: false),
                    ProductKind = table.Column<string>(type: "text", nullable: false),
                    InstallerName = table.Column<string>(type: "text", nullable: false),
                    InstallerCompany = table.Column<string>(type: "text", nullable: false),
                    InstalledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InspectorName = table.Column<string>(type: "text", nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PhotoBlobId = table.Column<string>(type: "text", nullable: true),
                    GpsLat = table.Column<double>(type: "double precision", nullable: true),
                    GpsLon = table.Column<double>(type: "double precision", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenetrationSignoffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PinCrdtUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocKey = table.Column<string>(type: "text", nullable: false),
                    UpdateBase64 = table.Column<string>(type: "text", nullable: false),
                    IsSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinCrdtUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ElevationM = table.Column<double>(type: "double precision", nullable: true),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    ToolMappingsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectLevels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SceneNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Discipline = table.Column<string>(type: "text", nullable: false),
                    LevelCode = table.Column<string>(type: "text", nullable: true),
                    SystemCode = table.Column<string>(type: "text", nullable: true),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    VertexCount = table.Column<int>(type: "integer", nullable: false),
                    MinX = table.Column<double>(type: "double precision", nullable: false),
                    MinY = table.Column<double>(type: "double precision", nullable: false),
                    MinZ = table.Column<double>(type: "double precision", nullable: false),
                    MaxX = table.Column<double>(type: "double precision", nullable: false),
                    MaxY = table.Column<double>(type: "double precision", nullable: false),
                    MaxZ = table.Column<double>(type: "double precision", nullable: false),
                    Compression = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SsoConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailDomains = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequireSso = table.Column<bool>(type: "boolean", nullable: false),
                    OidcIssuer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OidcClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OidcClientSecretEncrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OidcAuthorizationEndpoint = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OidcTokenEndpoint = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OidcUserInfoEndpoint = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OidcJwksUri = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    OidcScopes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SamlEntityId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SamlSsoUrl = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SamlSloUrl = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SamlIdpCertificate = table.Column<string>(type: "text", nullable: true),
                    SamlSpCertificateEncrypted = table.Column<string>(type: "text", nullable: true),
                    AttributeMapJson = table.Column<string>(type: "text", nullable: true),
                    GroupRoleMapJson = table.Column<string>(type: "text", nullable: true),
                    ScimEndpoint = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ScimBearerTokenEncrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastSuccessfulLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailedLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Cycle = table.Column<int>(type: "integer", nullable: false),
                    PriceMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SuitabilityTransitionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromCode = table.Column<int>(type: "integer", nullable: false),
                    ToCode = table.Column<int>(type: "integer", nullable: false),
                    AllowedRoles = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    RequiredApprovalChainId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreconditionMask = table.Column<int>(type: "integer", nullable: false),
                    AutoTriggerAfterHours = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuitabilityTransitionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SuitabilityTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromCode = table.Column<int>(type: "integer", nullable: false),
                    ToCode = table.Column<int>(type: "integer", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TriggerSource = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TriggeredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuitabilityTransitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContactEmail = table.Column<string>(type: "text", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    MimEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MimTier = table.Column<int>(type: "integer", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    MaxProjects = table.Column<int>(type: "integer", nullable: false),
                    StorageLimitBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrialExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    Plan = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    BillingCycle = table.Column<int>(type: "integer", nullable: false),
                    TrialReminderSentDays = table.Column<int>(type: "integer", nullable: false),
                    PendingErasureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    KeywordExtensionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    BimManagerIso19650RolesJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskCode = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AssignedTo = table.Column<string>(type: "text", nullable: true),
                    FrequencyDays = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StandardReference = table.Column<string>(type: "text", nullable: true),
                    IsStatutory = table.Column<bool>(type: "boolean", nullable: false),
                    RegulatoryBody = table.Column<string>(type: "text", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric", nullable: true),
                    ActualCost = table.Column<decimal>(type: "numeric", nullable: true),
                    EstimatedHours = table.Column<double>(type: "double precision", nullable: true),
                    ActualHours = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsLeaf = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationCodes_ClassificationCodes_ParentCodeId",
                        column: x => x.ParentCodeId,
                        principalTable: "ClassificationCodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClassificationCodes_ClassificationSystems_SystemId",
                        column: x => x.SystemId,
                        principalTable: "ClassificationSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelCheckRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppliesToIfcTypes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    AppliesToDiscipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ParamsJson = table.Column<string>(type: "text", nullable: false),
                    AutoAction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCheckRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCheckRules_ModelCheckRuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "ModelCheckRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelCheckRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalRulesEvaluated = table.Column<int>(type: "integer", nullable: false),
                    TotalElementsChecked = table.Column<int>(type: "integer", nullable: false),
                    FindingsCount = table.Column<int>(type: "integer", nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    MajorCount = table.Column<int>(type: "integer", nullable: false),
                    MinorCount = table.Column<int>(type: "integer", nullable: false),
                    InfoCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TriggeredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCheckRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCheckRuns_ModelCheckRuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "ModelCheckRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BoqPreambleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoqDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreambleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverrideBody = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoqPreambleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoqPreambleAssignments_Nrm2Preambles_PreambleId",
                        column: x => x.PreambleId,
                        principalTable: "Nrm2Preambles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AllowedCdeStates = table.Column<string>(type: "text", nullable: true),
                    AllowedDisciplines = table.Column<string>(type: "text", nullable: true),
                    AllowedSuitabilities = table.Column<string>(type: "text", nullable: true),
                    DefaultProjectRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefaultIso19650Role = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetDataSheetTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AnchorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDataSheetTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetDataSheetTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicenseKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    MimEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MaxActivations = table.Column<int>(type: "integer", nullable: false),
                    CurrentActivations = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedMachineIds = table.Column<string>(type: "text", nullable: true),
                    LastActivatedBy = table.Column<string>(type: "text", nullable: true),
                    LastActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseKeys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Phase = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    TagSeparator = table.Column<string>(type: "text", nullable: false),
                    SeqNumPad = table.Column<int>(type: "integer", nullable: false),
                    TagPrefix = table.Column<string>(type: "text", nullable: true),
                    TagSuffix = table.Column<string>(type: "text", nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    EnforceIso19650Naming = table.Column<bool>(type: "boolean", nullable: false),
                    CustomDeliverableStateMachineJson = table.Column<string>(type: "jsonb", nullable: true),
                    BoundaryPolygon = table.Column<string>(type: "jsonb", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    CompliancePercent = table.Column<double>(type: "double precision", nullable: false),
                    ContainerCompliancePercent = table.Column<double>(type: "double precision", nullable: false),
                    TotalElements = table.Column<int>(type: "integer", nullable: false),
                    TaggedElements = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    RagStatus = table.Column<string>(type: "text", nullable: false),
                    BridgeKeyHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantBrandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HeaderColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupportEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmailFromName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EmailFromAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmailSignature = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBrandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Iso19650Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true),
                    RefreshToken = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IfcGlobalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IfcType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ElementName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Suggestion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BimIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCheckResults_ModelCheckRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ModelCheckRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModelCheckResults_ModelCheckRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ModelCheckRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoqBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BaselinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DocumentRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoqBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoqBaselines_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoqDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PrimaryClassificationSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondaryClassificationSystemId = table.Column<Guid>(type: "uuid", nullable: true),
                    MeasurementProtocol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    VatTreatment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Architect = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StructuralEngineer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MepEngineer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CostManager = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrincipalContractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContractForm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InsuranceParticulars = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DayworkLabourPct = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    DayworkMaterialsPct = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    DayworkPlantPct = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    LocationFactor = table.Column<decimal>(type: "numeric(5,3)", nullable: true),
                    PricingBasis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoqDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoqDocuments_ClassificationSystems_PrimaryClassificationSys~",
                        column: x => x.PrimaryClassificationSystemId,
                        principalTable: "ClassificationSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BoqDocuments_ClassificationSystems_SecondaryClassificationS~",
                        column: x => x.SecondaryClassificationSystemId,
                        principalTable: "ClassificationSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BoqDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoqSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoqSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoqSnapshots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CdeContainers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainerType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CdeContainers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CdeContainers_CdeContainers_ParentContainerId",
                        column: x => x.ParentContainerId,
                        principalTable: "CdeContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CdeContainers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClashAutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    MinSeverity = table.Column<int>(type: "integer", nullable: true),
                    DisciplineA = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DisciplineB = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: true),
                    MinOverlapVolumeMm3 = table.Column<double>(type: "double precision", nullable: true),
                    LevelCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AutoCreateIssue = table.Column<bool>(type: "boolean", nullable: false),
                    AutoAssignTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuePriority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NotifyPush = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyUsers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FireWebhook = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClashAutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClashAutomationRules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalElements = table.Column<int>(type: "integer", nullable: false),
                    TaggedComplete = table.Column<int>(type: "integer", nullable: false),
                    TaggedIncomplete = table.Column<int>(type: "integer", nullable: false),
                    Untagged = table.Column<int>(type: "integer", nullable: false),
                    FullyResolved = table.Column<int>(type: "integer", nullable: false),
                    StaleCount = table.Column<int>(type: "integer", nullable: false),
                    PlaceholderCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    WarningHealthScore = table.Column<int>(type: "integer", nullable: false),
                    TagPercent = table.Column<double>(type: "double precision", nullable: false),
                    StrictPercent = table.Column<double>(type: "double precision", nullable: false),
                    ContainerPercent = table.Column<double>(type: "double precision", nullable: false),
                    RagStatus = table.Column<string>(type: "text", nullable: false),
                    ByDisciplineJson = table.Column<string>(type: "text", nullable: true),
                    ByPhaseJson = table.Column<string>(type: "text", nullable: true),
                    EmptyTokenCountsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceSnapshots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DistributionGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IncludeInDailyDigest = table.Column<bool>(type: "boolean", nullable: false),
                    ForceRedacted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionGroups_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalElementMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IfcGlobalId = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    Host = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HostElementId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HostDocumentGuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HostDisplayLabel = table.Column<string>(type: "text", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IngestionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalElementMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalElementMappings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalElementMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederatedElements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocGuid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ElementId = table.Column<long>(type: "bigint", nullable: false),
                    UniqueId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IfcGuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GlbStoragePath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    MinX = table.Column<float>(type: "real", nullable: false),
                    MinY = table.Column<float>(type: "real", nullable: false),
                    MinZ = table.Column<float>(type: "real", nullable: false),
                    MaxX = table.Column<float>(type: "real", nullable: false),
                    MaxY = table.Column<float>(type: "real", nullable: false),
                    MaxZ = table.Column<float>(type: "real", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederatedElements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederatedElements_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueCustomFieldSchemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<int>(type: "integer", nullable: false),
                    HelpText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueCustomFieldSchemas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueCustomFieldSchemas_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LpsRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedBy = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LpsClass = table.Column<string>(type: "text", nullable: false),
                    RollingSphereRadiusM = table.Column<double>(type: "double precision", nullable: false),
                    MeshSizeM = table.Column<double>(type: "double precision", nullable: false),
                    InspectionIntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    EarthResistanceTargetOhm = table.Column<double>(type: "double precision", nullable: false),
                    GroundFlashDensity = table.Column<double>(type: "double precision", nullable: false),
                    AirTerminalCount = table.Column<int>(type: "integer", nullable: false),
                    DownConductorCount = table.Column<int>(type: "integer", nullable: false),
                    EarthElectrodeCount = table.Column<int>(type: "integer", nullable: false),
                    BondingCount = table.Column<int>(type: "integer", nullable: false),
                    SpdCount = table.Column<int>(type: "integer", nullable: false),
                    KcFactor = table.Column<double>(type: "double precision", nullable: false),
                    SepDistanceViolations = table.Column<int>(type: "integer", nullable: false),
                    AnnualStrikeFrequencyNd = table.Column<double>(type: "double precision", nullable: false),
                    CollectionAreaM2 = table.Column<double>(type: "double precision", nullable: false),
                    RiskR1 = table.Column<double>(type: "double precision", nullable: false),
                    RiskR2 = table.Column<double>(type: "double precision", nullable: false),
                    RiskR3 = table.Column<double>(type: "double precision", nullable: false),
                    RiskR4 = table.Column<double>(type: "double precision", nullable: false),
                    TolerableR1 = table.Column<double>(type: "double precision", nullable: false),
                    TolerableR2 = table.Column<double>(type: "double precision", nullable: false),
                    TolerableR3 = table.Column<double>(type: "double precision", nullable: false),
                    TolerableR4 = table.Column<double>(type: "double precision", nullable: false),
                    RecommendedClass = table.Column<string>(type: "text", nullable: false),
                    ComplianceVerdict = table.Column<string>(type: "text", nullable: false),
                    ComplianceChecksPass = table.Column<int>(type: "integer", nullable: false),
                    ComplianceChecksWarn = table.Column<int>(type: "integer", nullable: false),
                    ComplianceChecksFail = table.Column<int>(type: "integer", nullable: false),
                    LastTestDate = table.Column<string>(type: "text", nullable: false),
                    CertReference = table.Column<string>(type: "text", nullable: false),
                    SpdCoordinationPass = table.Column<int>(type: "integer", nullable: false),
                    SpdCoordinationWarn = table.Column<int>(type: "integer", nullable: false),
                    SpdCoordinationFail = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LpsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LpsRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Meetings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    MeetingType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    MeetingUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Minutes = table.Column<string>(type: "text", nullable: true),
                    MinutesDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotifiedUserIds = table.Column<string>(type: "text", nullable: true),
                    RecurrenceRule = table.Column<string>(type: "text", nullable: true),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meetings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastFiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundWebhooks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutboundWebhooks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "P6SyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivitiesPolled = table.Column<int>(type: "integer", nullable: false),
                    ElementsUpdated = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_P6SyncLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_P6SyncLogs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertNumber = table.Column<int>(type: "integer", nullable: false),
                    ContractRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ValuationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ContractorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmployerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProjectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RetentionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    HalfRetentionAtPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    EffectiveRetentionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    GrossValuation = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RetentionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NetThisCert = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalPayable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SupersededByCertNumber = table.Column<int>(type: "integer", nullable: true),
                    SignedByContractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContractorSignedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedByEmployer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmployerSignedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SovJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "PhotoChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LevelCode = table.Column<string>(type: "text", nullable: true),
                    ZoneCode = table.Column<string>(type: "text", nullable: true),
                    WorkPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoChecklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoChecklists_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalProjectId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RefreshToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "text", nullable: true),
                    LastSyncError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformConnections_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCoordinateSystems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrsEpsgCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CrsName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OriginEasting = table.Column<double>(type: "double precision", nullable: true),
                    OriginNorthing = table.Column<double>(type: "double precision", nullable: true),
                    OriginElevation = table.Column<double>(type: "double precision", nullable: true),
                    TrueNorthDeg = table.Column<double>(type: "double precision", nullable: false),
                    LengthUnit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ReferenceModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCoordinateSystems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCoordinateSystems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RibaStage = table.Column<int>(type: "integer", nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    PlannedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaselineStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaselineFinish = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PercentComplete = table.Column<double>(type: "double precision", nullable: false),
                    PredecessorIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsMilestone = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedMetric = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeqCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterKey = table.Column<string>(type: "text", nullable: false),
                    CurrentValue = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeqCounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeqCounters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CriteriaJson = table.Column<string>(type: "jsonb", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageGates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageGates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncWatermarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastSyncUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ElementCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWatermarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncWatermarks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaggedElements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevitElementId = table.Column<long>(type: "bigint", nullable: false),
                    UniqueId = table.Column<string>(type: "text", nullable: false),
                    Disc = table.Column<string>(type: "text", nullable: false),
                    Loc = table.Column<string>(type: "text", nullable: false),
                    Zone = table.Column<string>(type: "text", nullable: false),
                    Lvl = table.Column<string>(type: "text", nullable: false),
                    Sys = table.Column<string>(type: "text", nullable: false),
                    Func = table.Column<string>(type: "text", nullable: false),
                    Prod = table.Column<string>(type: "text", nullable: false),
                    Seq = table.Column<string>(type: "text", nullable: false),
                    Tag1 = table.Column<string>(type: "text", nullable: false),
                    Tag7 = table.Column<string>(type: "text", nullable: true),
                    Tag7A = table.Column<string>(type: "text", nullable: true),
                    Tag7B = table.Column<string>(type: "text", nullable: true),
                    Tag7C = table.Column<string>(type: "text", nullable: true),
                    Tag7D = table.Column<string>(type: "text", nullable: true),
                    Tag7E = table.Column<string>(type: "text", nullable: true),
                    Tag7F = table.Column<string>(type: "text", nullable: true),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    FamilyName = table.Column<string>(type: "text", nullable: false),
                    TypeName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Rev = table.Column<string>(type: "text", nullable: true),
                    GridRef = table.Column<string>(type: "text", nullable: true),
                    RoomName = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<string>(type: "text", nullable: true),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    IsFullyResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationErrors = table.Column<string>(type: "text", nullable: true),
                    PreviousTag = table.Column<string>(type: "text", nullable: true),
                    TagModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedBy = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    P6ActivityId = table.Column<string>(type: "text", nullable: true),
                    PercentComplete = table.Column<double>(type: "double precision", nullable: true),
                    ActualStart = table.Column<string>(type: "text", nullable: true),
                    ActualFinish = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaggedElements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaggedElements_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TakeoffRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassificationCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IfcType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CategoryPattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MaterialPattern = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PropertyFiltersJson = table.Column<string>(type: "text", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuantityFormula = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DescriptionTemplate = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SpecificationGrade = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DeemedIncludedJson = table.Column<string>(type: "text", nullable: true),
                    WastePercent = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TakeoffRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TakeoffRules_ClassificationCodes_ClassificationCodeId",
                        column: x => x.ClassificationCodeId,
                        principalTable: "ClassificationCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TakeoffRules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transmittals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransmittalCode = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DocumentIdsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SlaDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "text", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedBy = table.Column<string>(type: "text", nullable: true),
                    ResponseNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transmittals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transmittals_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PresetName = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    StepsPassed = table.Column<int>(type: "integer", nullable: false),
                    StepsFailed = table.Column<int>(type: "integer", nullable: false),
                    StepsSkipped = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    ComplianceBefore = table.Column<double>(type: "double precision", nullable: false),
                    ComplianceAfter = table.Column<double>(type: "double precision", nullable: false),
                    StepResultsJson = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedEntityJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SectionPrefixesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Contractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EstimatedValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AwardedValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenderIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AwardDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartOnSite = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PracticalCompletion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkPackages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetDataSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    AnchorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AnchorKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CompletenessPct = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDataSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetDataSheets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetDataSheets_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CoordinatorWorkloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStarting = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenIssuesAssigned = table.Column<int>(type: "integer", nullable: false),
                    OpenIssuesCritical = table.Column<int>(type: "integer", nullable: false),
                    OpenIssuesMajor = table.Column<int>(type: "integer", nullable: false),
                    OpenIssuesOverdue = table.Column<int>(type: "integer", nullable: false),
                    IssuesResolvedThisWeek = table.Column<int>(type: "integer", nullable: false),
                    IssuesCreatedThisWeek = table.Column<int>(type: "integer", nullable: false),
                    OpenClashesAssigned = table.Column<int>(type: "integer", nullable: false),
                    OpenModelCheckFindings = table.Column<int>(type: "integer", nullable: false),
                    PendingApprovalsCount = table.Column<int>(type: "integer", nullable: false),
                    WorkloadIndex = table.Column<int>(type: "integer", nullable: false),
                    LoadBand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoordinatorWorkloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoordinatorWorkloads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DevicePushTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePushTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DevicePushTokens_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DevicePushTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueCode = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Assignee = table.Column<string>(type: "text", nullable: true),
                    AssigneeEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "text", nullable: true),
                    Discipline = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    LinkedElementIds = table.Column<string>(type: "text", nullable: true),
                    BcfGuid = table.Column<string>(type: "text", nullable: true),
                    WatcherUserIds = table.Column<string>(type: "text", nullable: true),
                    CoAssigneeUserIds = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    LocationAccuracy = table.Column<double>(type: "double precision", nullable: true),
                    DeviceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelElementGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ModelX = table.Column<double>(type: "double precision", nullable: true),
                    ModelY = table.Column<double>(type: "double precision", nullable: true),
                    ModelZ = table.Column<double>(type: "double precision", nullable: true),
                    CustomFields = table.Column<string>(type: "jsonb", nullable: true),
                    OptionSetName = table.Column<string>(type: "text", nullable: true),
                    OptionName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Issues_Users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Issues_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MfaEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecretEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RecoveryCodesJson = table.Column<string>(type: "text", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaEnrollments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectRole = table.Column<string>(type: "text", nullable: false),
                    Iso19650Role = table.Column<string>(type: "text", nullable: false),
                    AllowedCdeStates = table.Column<string>(type: "text", nullable: true),
                    AllowedDisciplines = table.Column<string>(type: "text", nullable: true),
                    AllowedSuitabilities = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvitedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ElementMapPath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ElementCount = table.Column<int>(type: "integer", nullable: true),
                    BoundsMinX = table.Column<double>(type: "double precision", nullable: true),
                    BoundsMinY = table.Column<double>(type: "double precision", nullable: true),
                    BoundsMinZ = table.Column<double>(type: "double precision", nullable: true),
                    BoundsMaxX = table.Column<double>(type: "double precision", nullable: true),
                    BoundsMaxY = table.Column<double>(type: "double precision", nullable: true),
                    BoundsMaxZ = table.Column<double>(type: "double precision", nullable: true),
                    Units = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Revision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageMissingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectModels_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectModels_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SiteDiaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Weather = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                    WindSpeedKph = table.Column<double>(type: "double precision", nullable: true),
                    RainfallMm = table.Column<double>(type: "double precision", nullable: true),
                    ManpowerCount = table.Column<int>(type: "integer", nullable: false),
                    ManpowerByTradeJson = table.Column<string>(type: "jsonb", nullable: true),
                    EquipmentJson = table.Column<string>(type: "jsonb", nullable: true),
                    DeliveriesJson = table.Column<string>(type: "jsonb", nullable: true),
                    Narrative = table.Column<string>(type: "text", nullable: true),
                    ChecklistJson = table.Column<string>(type: "jsonb", nullable: true),
                    VisitorsLog = table.Column<string>(type: "text", nullable: true),
                    SafetyIncidents = table.Column<string>(type: "text", nullable: true),
                    DelaysAndDisruption = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    AutoCreatedIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDiaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteDiaries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteDiaries_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ComplianceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RevisionsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MeetingsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SlaBreachesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailDigestEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailDigestHourUtc = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuietHoursStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    QuietHoursEnd = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotificationPreferences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotificationPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    CdeStatus = table.Column<string>(type: "text", nullable: false),
                    SuitabilityCode = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: true),
                    Discipline = table.Column<string>(type: "text", nullable: true),
                    Originator = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: true),
                    UploadedBy = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusHistoryJson = table.Column<string>(type: "text", nullable: true),
                    ScanStatus = table.Column<string>(type: "text", nullable: false),
                    ScanScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScanThreatName = table.Column<string>(type: "text", nullable: true),
                    ContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublishedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetentionExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_CdeContainers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "CdeContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DistributionGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistributionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisciplineFilter = table.Column<string>(type: "text", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistributionGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionGroupMembers_DistributionGroups_DistributionGro~",
                        column: x => x.DistributionGroupId,
                        principalTable: "DistributionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DistributionGroupMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAlbums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DistributionGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoArchiveAfterDays = table.Column<int>(type: "integer", nullable: true),
                    SavedFilterJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAlbums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAlbums_DistributionGroups_DistributionGroupId",
                        column: x => x.DistributionGroupId,
                        principalTable: "DistributionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhotoAlbums_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowedReasonsJson = table.Column<string>(type: "text", nullable: true),
                    DefaultAudienceByReasonJson = table.Column<string>(type: "text", nullable: true),
                    WatermarkLogoPath = table.Column<string>(type: "text", nullable: true),
                    WatermarkFooterTemplate = table.Column<string>(type: "text", nullable: true),
                    WatermarkRequired = table.Column<bool>(type: "boolean", nullable: false),
                    FaceBlurRequired = table.Column<bool>(type: "boolean", nullable: false),
                    PlateBlurRequired = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: true),
                    AutoArchiveAfterHandover = table.Column<bool>(type: "boolean", nullable: false),
                    GeofenceWkt = table.Column<string>(type: "text", nullable: true),
                    OffsiteAudience = table.Column<string>(type: "text", nullable: true),
                    DigestHourLocal = table.Column<int>(type: "integer", nullable: false),
                    DigestDistributionGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalChain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EnforceChecklistOnShiftEnd = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultAlbumByReasonJson = table.Column<string>(type: "text", nullable: true),
                    NdaText = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoPolicies_DistributionGroups_DigestDistributionGroupId",
                        column: x => x.DigestDistributionGroupId,
                        principalTable: "DistributionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhotoPolicies_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeetingActionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Assignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LinkedIssueId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingActionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingActionItems_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MeetingActionItems_Users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MeetingAgendaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    Presenter = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    Decision = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingAgendaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingAgendaItems_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeetingAttendees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttendanceStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingAttendees_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MeetingAttendees_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SavedViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false),
                    ThumbnailB64 = table.Column<string>(type: "text", nullable: true),
                    CapturedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CapturedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedMeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedActionItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedViews_Meetings_LinkedMeetingId",
                        column: x => x.LinkedMeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SavedViews_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedViews_Users_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CostItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    TradeBucket = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ScheduleTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CostItems_ScheduleTasks_ScheduleTaskId",
                        column: x => x.ScheduleTaskId,
                        principalTable: "ScheduleTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StageGateCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageGateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Met = table.Column<bool>(type: "boolean", nullable: false),
                    EvidenceDocId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageGateCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageGateCriteria_StageGates_StageGateId",
                        column: x => x.StageGateId,
                        principalTable: "StageGates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaggedElementId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElementId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConflictType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ServerTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClientTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClientUserName = table.Column<string>(type: "text", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncConflicts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SyncConflicts_TaggedElements_TaggedElementId",
                        column: x => x.TaggedElementId,
                        principalTable: "TaggedElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BoqVariations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaselineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContractForm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "JCT2024"),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    InstructionRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BimIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    NetValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Other"),
                    Liability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Employer"),
                    ReasonDetail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EotDays = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LineDeltaJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoqVariations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoqVariations_BoqBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "BoqBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BoqVariations_Issues_BimIssueId",
                        column: x => x.BimIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BoqVariations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClashRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClashHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ModelAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementAGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ElementAName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ElementAType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DisciplineA = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ModelBId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementBGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ElementBName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ElementBType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DisciplineB = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DistanceMm = table.Column<double>(type: "double precision", nullable: false),
                    CentreX = table.Column<double>(type: "double precision", nullable: false),
                    CentreY = table.Column<double>(type: "double precision", nullable: false),
                    CentreZ = table.Column<double>(type: "double precision", nullable: false),
                    OverlapVolumeMm3 = table.Column<double>(type: "double precision", nullable: false),
                    LevelCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ZoneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AssignedTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    BcfTopicGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DetectedByJobId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClashRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClashRecords_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClashRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MentionedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueComments_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueComments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MobileOfflineModelManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SceneNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CachedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirstCachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileOfflineModelManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileOfflineModelManifests_ProjectModels_ProjectModelId",
                        column: x => x.ProjectModelId,
                        principalTable: "ProjectModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobileOfflineModelManifests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobileOfflineModelManifests_SceneNodes_SceneNodeId",
                        column: x => x.SceneNodeId,
                        principalTable: "SceneNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectModelTransforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranslationX = table.Column<double>(type: "double precision", nullable: false),
                    TranslationY = table.Column<double>(type: "double precision", nullable: false),
                    TranslationZ = table.Column<double>(type: "double precision", nullable: false),
                    RotationDeg = table.Column<double>(type: "double precision", nullable: false),
                    ScaleFactor = table.Column<double>(type: "double precision", nullable: false),
                    IsAutoComputed = table.Column<bool>(type: "boolean", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    AppliedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectModelTransforms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectModelTransforms_ProjectModels_ProjectModelId",
                        column: x => x.ProjectModelId,
                        principalTable: "ProjectModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuantityLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaselineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassificationCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TakeoffRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IfcGlobalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IfcType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RevitElementId = table.Column<long>(type: "bigint", nullable: true),
                    Level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Zone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SectionCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ItemDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NetQuantity = table.Column<double>(type: "double precision", nullable: false),
                    WastePercent = table.Column<double>(type: "double precision", nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    LineKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PricingBasis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmbodiedCarbonPerUnit = table.Column<double>(type: "double precision", nullable: true),
                    EmbodiedCarbonTotal = table.Column<double>(type: "double precision", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuantityLines_BoqBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "BoqBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuantityLines_ClassificationCodes_ClassificationCodeId",
                        column: x => x.ClassificationCodeId,
                        principalTable: "ClassificationCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuantityLines_ProjectModels_ProjectModelId",
                        column: x => x.ProjectModelId,
                        principalTable: "ProjectModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuantityLines_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuantityLines_TakeoffRules_TakeoffRuleId",
                        column: x => x.TakeoffRuleId,
                        principalTable: "TakeoffRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuantityLines_WorkPackages_WorkPackageId",
                        column: x => x.WorkPackageId,
                        principalTable: "WorkPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalChains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Transition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalChains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalChains_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalChains_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Transition = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedBy = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    RevisionSnapshot = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentApprovals_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentApprovals_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentMarkups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousMarkupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShapesJson = table.Column<string>(type: "jsonb", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentMarkups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentMarkups_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentMarkups_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CdeStateAtRevision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuitabilityAtRevision = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommentSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignatureNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WatermarkedFilePath = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    WatermarkStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSignatures_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: true),
                    UploadedBy = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangeDescription = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InformationDeliverables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageGateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    OwnerRole = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SuitabilityTarget = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InformationDeliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InformationDeliverables_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InformationDeliverables_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InformationDeliverables_StageGates_StageGateId",
                        column: x => x.StageGateId,
                        principalTable: "StageGates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IssueAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttachedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueAttachments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueAttachments_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueAudioNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TranscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueAudioNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueAudioNotes_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SiteDiaryAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteDiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Caption = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDiaryAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteDiaryAttachments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteDiaryAttachments_SiteDiaries_SiteDiaryId",
                        column: x => x.SiteDiaryId,
                        principalTable: "SiteDiaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SitePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BlurStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WatermarkApplied = table.Column<bool>(type: "boolean", nullable: false),
                    RedactedFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Caption = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapturedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                    LevelCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ZoneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    WorkPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnchorIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnchorElementGuid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelX = table.Column<double>(type: "double precision", nullable: true),
                    ModelY = table.Column<double>(type: "double precision", nullable: true),
                    ModelZ = table.Column<double>(type: "double precision", nullable: true),
                    ClassifierConfidence = table.Column<double>(type: "double precision", nullable: false),
                    ClassifierSignals = table.Column<string>(type: "jsonb", nullable: true),
                    PairKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WithdrawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawnByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SitePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SitePhotos_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SitePhotos_Issues_AnchorIssueId",
                        column: x => x.AnchorIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SitePhotos_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SitePhotos_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SitePhotos_Users_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequiredApproversJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecisionsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalStages_ApprovalChains_ChainId",
                        column: x => x.ChainId,
                        principalTable: "ApprovalChains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransmittalDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransmittalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CdeStateAtTransmittal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SuitabilityAtTransmittal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FilePathAtTransmittal = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransmittalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransmittalDocuments_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TransmittalDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransmittalDocuments_Transmittals_TransmittalId",
                        column: x => x.TransmittalId,
                        principalTable: "Transmittals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAccessRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistributionGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisibleDisciplines = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinRoleToView = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    VisibleFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VisibleUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiresNdaAcceptance = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAccessRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAccessRules_DistributionGroups_DistributionGroupId",
                        column: x => x.DistributionGroupId,
                        principalTable: "DistributionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhotoAccessRules_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAlbumPhotos",
                columns: table => new
                {
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAlbumPhotos", x => new { x.AlbumId, x.PhotoId });
                    table.ForeignKey(
                        name: "FK_PhotoAlbumPhotos_PhotoAlbums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "PhotoAlbums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAlbumPhotos_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShapesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnnotations_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnnotations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhotoApprovalSignoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoApprovalSignoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoApprovalSignoffs_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoApprovalSignoffs_Users_SignedByUserId",
                        column: x => x.SignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhotoChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DefaultReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsWaived = table.Column<bool>(type: "boolean", nullable: false),
                    WaivedReason = table.Column<string>(type: "text", nullable: true),
                    FulfilledByPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfilledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FulfilledByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoChecklistItems_PhotoChecklists_ChecklistId",
                        column: x => x.ChecklistId,
                        principalTable: "PhotoChecklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoChecklistItems_SitePhotos_FulfilledByPhotoId",
                        column: x => x.FulfilledByPhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhotoNdaAcceptances",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    AcceptedTextSha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoNdaAcceptances", x => new { x.PhotoId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PhotoNdaAcceptances_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoNdaAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: true),
                    Token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ForceRedacted = table.Column<bool>(type: "boolean", nullable: false),
                    MaxFetches = table.Column<int>(type: "integer", nullable: true),
                    FetchCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoShareLinks_PhotoAlbums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "PhotoAlbums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhotoShareLinks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoShareLinks_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhotoVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TranscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoVoiceNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoVoiceNotes_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhotoVoiceNotes_SitePhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "SitePhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessProfiles_TenantId",
                table: "AccessProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessProfiles_TenantId_Name",
                table: "AccessProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_DocumentId_Transition_Status",
                table: "ApprovalChains",
                columns: new[] { "DocumentId", "Transition", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_ProjectId",
                table: "ApprovalChains",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_TenantId",
                table: "ApprovalChains",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalStages_ChainId_Order",
                table: "ApprovalStages",
                columns: new[] { "ChainId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalStages_TenantId",
                table: "ApprovalStages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheets_AuthorUserId",
                table: "AssetDataSheets",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheets_ProjectId_AnchorKind_AnchorKey",
                table: "AssetDataSheets",
                columns: new[] { "ProjectId", "AnchorKind", "AnchorKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheets_ProjectId_Status",
                table: "AssetDataSheets",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheets_TemplateId",
                table: "AssetDataSheets",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheets_TenantId",
                table: "AssetDataSheets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheetTemplates_TenantId_Code",
                table: "AssetDataSheetTemplates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetDataSheetTemplates_TenantId_IsActive",
                table: "AssetDataSheetTemplates",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_AssetTag",
                table: "Assets",
                columns: new[] { "ProjectId", "AssetTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ProjectId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "ProjectId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqBaselines_ProjectId_Kind_BaselinedAt",
                table: "BoqBaselines",
                columns: new[] { "ProjectId", "Kind", "BaselinedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqBaselines_TenantId",
                table: "BoqBaselines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqDocuments_PrimaryClassificationSystemId",
                table: "BoqDocuments",
                column: "PrimaryClassificationSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqDocuments_ProjectId",
                table: "BoqDocuments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqDocuments_SecondaryClassificationSystemId",
                table: "BoqDocuments",
                column: "SecondaryClassificationSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqDocuments_TenantId",
                table: "BoqDocuments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqPreambleAssignments_BoqDocumentId_SortOrder",
                table: "BoqPreambleAssignments",
                columns: new[] { "BoqDocumentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqPreambleAssignments_PreambleId",
                table: "BoqPreambleAssignments",
                column: "PreambleId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqPreambleAssignments_TenantId",
                table: "BoqPreambleAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqSnapshots_ProjectId",
                table: "BoqSnapshots",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqSnapshots_TenantId",
                table: "BoqSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_BaselineId",
                table: "BoqVariations",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_BimIssueId",
                table: "BoqVariations",
                column: "BimIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_ProjectId_BaselineId_Status",
                table: "BoqVariations",
                columns: new[] { "ProjectId", "BaselineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_ProjectId_ContractForm",
                table: "BoqVariations",
                columns: new[] { "ProjectId", "ContractForm" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_ProjectId_Liability",
                table: "BoqVariations",
                columns: new[] { "ProjectId", "Liability" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_ProjectId_Reason",
                table: "BoqVariations",
                columns: new[] { "ProjectId", "Reason" });

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_ProjectId_Reference",
                table: "BoqVariations",
                columns: new[] { "ProjectId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoqVariations_TenantId",
                table: "BoqVariations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CdeContainers_ParentContainerId",
                table: "CdeContainers",
                column: "ParentContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_CdeContainers_ProjectId",
                table: "CdeContainers",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CdeContainers_ProjectId_ParentContainerId",
                table: "CdeContainers",
                columns: new[] { "ProjectId", "ParentContainerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CdeContainers_TenantId",
                table: "CdeContainers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClashAutomationRules_ProjectId_Priority",
                table: "ClashAutomationRules",
                columns: new[] { "ProjectId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ClashAutomationRules_TenantId",
                table: "ClashAutomationRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClashRecords_IssueId",
                table: "ClashRecords",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_ClashRecords_ProjectId_ClashHash",
                table: "ClashRecords",
                columns: new[] { "ProjectId", "ClashHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClashRecords_ProjectId_Status",
                table: "ClashRecords",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClashRecords_TenantId",
                table: "ClashRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCodes_ParentCodeId",
                table: "ClassificationCodes",
                column: "ParentCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCodes_SystemId_Code",
                table: "ClassificationCodes",
                columns: new[] { "SystemId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCodes_SystemId_Path",
                table: "ClassificationCodes",
                columns: new[] { "SystemId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationCodes_TenantId",
                table: "ClassificationCodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationSystems_TenantId",
                table: "ClassificationSystems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationSystems_TenantId_Code",
                table: "ClassificationSystems",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceSnapshots_ProjectId_CapturedAt",
                table: "ComplianceSnapshots",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceSnapshots_TenantId",
                table: "ComplianceSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatorWorkloads_TenantId",
                table: "CoordinatorWorkloads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatorWorkloads_UserId_WeekStarting",
                table: "CoordinatorWorkloads",
                columns: new[] { "UserId", "WeekStarting" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatorWorkloads_WeekStarting",
                table: "CoordinatorWorkloads",
                column: "WeekStarting");

            migrationBuilder.CreateIndex(
                name: "IX_CostItems_ProjectId",
                table: "CostItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CostItems_ProjectId_Code",
                table: "CostItems",
                columns: new[] { "ProjectId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_CostItems_ScheduleTaskId",
                table: "CostItems",
                column: "ScheduleTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_CostItems_TenantId",
                table: "CostItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_TenantId",
                table: "DashboardWidgets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_TenantId_UserId_SortOrder",
                table: "DashboardWidgets",
                columns: new[] { "TenantId", "UserId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DevicePushTokens_TenantId",
                table: "DevicePushTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DevicePushTokens_UserId_Token",
                table: "DevicePushTokens",
                columns: new[] { "UserId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DistributionGroupMembers_DistributionGroupId",
                table: "DistributionGroupMembers",
                column: "DistributionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionGroupMembers_UserId",
                table: "DistributionGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionGroups_ProjectId",
                table: "DistributionGroups",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionGroups_TenantId",
                table: "DistributionGroups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovals_DocumentId_Transition_Status",
                table: "DocumentApprovals",
                columns: new[] { "DocumentId", "Transition", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovals_ProjectId",
                table: "DocumentApprovals",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovals_TenantId",
                table: "DocumentApprovals",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMarkups_CreatedByUserId",
                table: "DocumentMarkups",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMarkups_DocumentId",
                table: "DocumentMarkups",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMarkups_TenantId",
                table: "DocumentMarkups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_DocumentId_CreatedAt",
                table: "DocumentRevisions",
                columns: new[] { "DocumentId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_TenantId",
                table: "DocumentRevisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ContainerId",
                table: "Documents",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId_CdeStatus",
                table: "Documents",
                columns: new[] { "ProjectId", "CdeStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId_Discipline",
                table: "Documents",
                columns: new[] { "ProjectId", "Discipline" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId_UploadedAt",
                table: "Documents",
                columns: new[] { "ProjectId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId",
                table: "Documents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatures_DocumentId",
                table: "DocumentSignatures",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatures_TenantId",
                table: "DocumentSignatures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSignatures_WatermarkStatus",
                table: "DocumentSignatures",
                column: "WatermarkStatus",
                filter: "\"WatermarkStatus\" = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_VersionNumber",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_TenantId",
                table: "DocumentVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalElementMappings_ProjectId_Host_HostElementId",
                table: "ExternalElementMappings",
                columns: new[] { "ProjectId", "Host", "HostElementId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalElementMappings_ProjectId_IfcGlobalId",
                table: "ExternalElementMappings",
                columns: new[] { "ProjectId", "IfcGlobalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalElementMappings_ProjectId_IfcGlobalId_Host_HostDocu~",
                table: "ExternalElementMappings",
                columns: new[] { "ProjectId", "IfcGlobalId", "Host", "HostDocumentGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalElementMappings_TenantId",
                table: "ExternalElementMappings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FederatedElements_ProjectId",
                table: "FederatedElements",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FederatedElements_ProjectId_SourceDocGuid_ElementId",
                table: "FederatedElements",
                columns: new[] { "ProjectId", "SourceDocGuid", "ElementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederatedElements_TenantId",
                table: "FederatedElements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FederatedElements_UniqueId",
                table: "FederatedElements",
                column: "UniqueId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIdRegistry_ProjectId_ArchiCadGuid",
                table: "GlobalIdRegistry",
                columns: new[] { "ProjectId", "ArchiCadGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIdRegistry_ProjectId_IfcGlobalId",
                table: "GlobalIdRegistry",
                columns: new[] { "ProjectId", "IfcGlobalId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIdRegistry_ProjectId_RevitUniqueId",
                table: "GlobalIdRegistry",
                columns: new[] { "ProjectId", "RevitUniqueId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIdRegistry_ProjectId_TeklaGuid",
                table: "GlobalIdRegistry",
                columns: new[] { "ProjectId", "TeklaGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalIdRegistry_TenantId",
                table: "GlobalIdRegistry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareAntiLigatureAudits_ProjectId_CapturedAt",
                table: "HealthcareAntiLigatureAudits",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareAntiLigatureAudits_ProjectId_Pass",
                table: "HealthcareAntiLigatureAudits",
                columns: new[] { "ProjectId", "Pass" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareAntiLigatureAudits_TenantId",
                table: "HealthcareAntiLigatureAudits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareMgasVerifications_ProjectId_CapturedAt",
                table: "HealthcareMgasVerifications",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareMgasVerifications_TenantId",
                table: "HealthcareMgasVerifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthcarePressureLogs_ProjectId_CapturedAt",
                table: "HealthcarePressureLogs",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcarePressureLogs_ProjectId_RoomBimId",
                table: "HealthcarePressureLogs",
                columns: new[] { "ProjectId", "RoomBimId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcarePressureLogs_TenantId",
                table: "HealthcarePressureLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareRdsSnapshots_ProjectId_RoomBimId",
                table: "HealthcareRdsSnapshots",
                columns: new[] { "ProjectId", "RoomBimId" });

            migrationBuilder.CreateIndex(
                name: "IX_HealthcareRdsSnapshots_TenantId",
                table: "HealthcareRdsSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HvacLoadSnapshot_ProjectId_CapturedAt",
                table: "HvacLoadSnapshot",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacLoadSnapshot_ProjectId_SystemId",
                table: "HvacLoadSnapshot",
                columns: new[] { "ProjectId", "SystemId" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacNcSnapshot_ProjectId_CapturedAt",
                table: "HvacNcSnapshot",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacNcSnapshot_ProjectId_PredictedNc",
                table: "HvacNcSnapshot",
                columns: new[] { "ProjectId", "PredictedNc" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacRefrigerantSizing_ProjectId_CapturedAt",
                table: "HvacRefrigerantSizing",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacRefrigerantSizing_ProjectId_Ok",
                table: "HvacRefrigerantSizing",
                columns: new[] { "ProjectId", "Ok" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacRefrigerantSizing_ProjectId_RefrigerantId",
                table: "HvacRefrigerantSizing",
                columns: new[] { "ProjectId", "RefrigerantId" });

            migrationBuilder.CreateIndex(
                name: "IX_HvacSnapshots_TenantId",
                table: "HvacSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcAlignmentReports_ProjectId_ProjectModelId",
                table: "IfcAlignmentReports",
                columns: new[] { "ProjectId", "ProjectModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcAlignmentReports_ProjectId_ValidatedAt",
                table: "IfcAlignmentReports",
                columns: new[] { "ProjectId", "ValidatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcAlignmentReports_TenantId",
                table: "IfcAlignmentReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcElementSnapshots_ProjectId_ProjectModelId_IfcGuid",
                table: "IfcElementSnapshots",
                columns: new[] { "ProjectId", "ProjectModelId", "IfcGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcElementSnapshots_ProjectId_UploadSequence",
                table: "IfcElementSnapshots",
                columns: new[] { "ProjectId", "UploadSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcElementSnapshots_TenantId",
                table: "IfcElementSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_DocumentId",
                table: "InformationDeliverables",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_DueDate",
                table: "InformationDeliverables",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_ProjectId",
                table: "InformationDeliverables",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_ProjectId_Code",
                table: "InformationDeliverables",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_StageGateId",
                table: "InformationDeliverables",
                column: "StageGateId");

            migrationBuilder.CreateIndex(
                name: "IX_InformationDeliverables_Status",
                table: "InformationDeliverables",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status",
                table: "Invoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId",
                table: "Invoices",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId",
                table: "Invoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueAttachments_DocumentId",
                table: "IssueAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAttachments_IssueId_DocumentId",
                table: "IssueAttachments",
                columns: new[] { "IssueId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueAttachments_TenantId",
                table: "IssueAttachments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAudioNotes_DocumentId",
                table: "IssueAudioNotes",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAudioNotes_IssueId",
                table: "IssueAudioNotes",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAudioNotes_IssueId_IdempotencyKey",
                table: "IssueAudioNotes",
                columns: new[] { "IssueId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAudioNotes_TenantId",
                table: "IssueAudioNotes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_AuthorUserId",
                table: "IssueComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_CreatedAt",
                table: "IssueComments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_IssueId",
                table: "IssueComments",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_TenantId",
                table: "IssueComments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueCustomFieldSchemas_ProjectId",
                table: "IssueCustomFieldSchemas",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueCustomFieldSchemas_ProjectId_Key",
                table: "IssueCustomFieldSchemas",
                columns: new[] { "ProjectId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueCustomFieldSchemas_TenantId",
                table: "IssueCustomFieldSchemas",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_AssigneeUserId",
                table: "Issues",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CreatedByUserId",
                table: "Issues",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CustomFields_gin",
                table: "Issues",
                column: "CustomFields")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_DueDate",
                table: "Issues",
                column: "DueDate",
                filter: "\"Status\" NOT IN ('CLOSED','RESOLVED')");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ModelId",
                table: "Issues",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_AssigneeUserId",
                table: "Issues",
                columns: new[] { "ProjectId", "AssigneeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_IssueCode",
                table: "Issues",
                columns: new[] { "ProjectId", "IssueCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_Status",
                table: "Issues",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_TenantId",
                table: "Issues",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiSnapshots_ProjectId_SnapshotDate",
                table: "KpiSnapshots",
                columns: new[] { "ProjectId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiSnapshots_TenantId",
                table: "KpiSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseKeys_Key",
                table: "LicenseKeys",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseKeys_TenantId",
                table: "LicenseKeys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LpsRecords_ProjectId_CapturedAt",
                table: "LpsRecords",
                columns: new[] { "ProjectId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LpsRecords_TenantId",
                table: "LpsRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LpsRecords_TenantId_CapturedAt",
                table: "LpsRecords",
                columns: new[] { "TenantId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_AssetId",
                table: "MaintenanceTasks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingActionItems_AssigneeUserId",
                table: "MeetingActionItems",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingActionItems_MeetingId",
                table: "MeetingActionItems",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingActionItems_MeetingId_Status",
                table: "MeetingActionItems",
                columns: new[] { "MeetingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAgendaItems_MeetingId_OrderIndex",
                table: "MeetingAgendaItems",
                columns: new[] { "MeetingId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAttendees_MeetingId",
                table: "MeetingAttendees",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAttendees_UserId",
                table: "MeetingAttendees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_ProjectId_ScheduledAt",
                table: "Meetings",
                columns: new[] { "ProjectId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_ProjectId_Status",
                table: "Meetings",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_TenantId",
                table: "Meetings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_TenantId",
                table: "MfaChallenges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_UserId_CreatedAt",
                table: "MfaChallenges",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaEnrollments_TenantId",
                table: "MfaEnrollments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaEnrollments_UserId_Method",
                table: "MfaEnrollments",
                columns: new[] { "UserId", "Method" });

            migrationBuilder.CreateIndex(
                name: "IX_MobileOfflineModelManifests_ProjectId",
                table: "MobileOfflineModelManifests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileOfflineModelManifests_ProjectModelId",
                table: "MobileOfflineModelManifests",
                column: "ProjectModelId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileOfflineModelManifests_SceneNodeId",
                table: "MobileOfflineModelManifests",
                column: "SceneNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileOfflineModelManifests_TenantId",
                table: "MobileOfflineModelManifests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileOfflineModelManifests_UserId_DeviceId_ProjectModelId",
                table: "MobileOfflineModelManifests",
                columns: new[] { "UserId", "DeviceId", "ProjectModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckResults_IfcGlobalId",
                table: "ModelCheckResults",
                column: "IfcGlobalId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckResults_ProjectId_Status",
                table: "ModelCheckResults",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckResults_RuleId",
                table: "ModelCheckResults",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckResults_RunId_Severity",
                table: "ModelCheckResults",
                columns: new[] { "RunId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckResults_TenantId",
                table: "ModelCheckResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRules_RuleSetId_Code",
                table: "ModelCheckRules",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRules_RuleSetId_SortOrder",
                table: "ModelCheckRules",
                columns: new[] { "RuleSetId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRules_TenantId",
                table: "ModelCheckRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRuleSets_TenantId",
                table: "ModelCheckRuleSets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRuleSets_TenantId_Code",
                table: "ModelCheckRuleSets",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRuns_ProjectId_StartedAt",
                table: "ModelCheckRuns",
                columns: new[] { "ProjectId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRuns_RuleSetId",
                table: "ModelCheckRuns",
                column: "RuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCheckRuns_TenantId",
                table: "ModelCheckRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelMarkups_ProjectId",
                table: "ModelMarkups",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelMarkups_ProjectId_IdempotencyKey",
                table: "ModelMarkups",
                columns: new[] { "ProjectId", "IdempotencyKey" },
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelMarkups_TenantId",
                table: "ModelMarkups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Nrm2Preambles_TenantId",
                table: "Nrm2Preambles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Nrm2Preambles_TenantId_NrmSectionCode_Group",
                table: "Nrm2Preambles",
                columns: new[] { "TenantId", "NrmSectionCode", "Group" });

            migrationBuilder.CreateIndex(
                name: "IX_Nrm2PreliminariesItems_BoqDocumentId_SortOrder",
                table: "Nrm2PreliminariesItems",
                columns: new[] { "BoqDocumentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Nrm2PreliminariesItems_TenantId",
                table: "Nrm2PreliminariesItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhooks_ProjectId",
                table: "OutboundWebhooks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhooks_TenantId",
                table: "OutboundWebhooks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhooks_TenantId_EventType_IsActive",
                table: "OutboundWebhooks",
                columns: new[] { "TenantId", "EventType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId",
                table: "OutboxMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_P6SyncLogs_ProjectId",
                table: "P6SyncLogs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_P6SyncLogs_TenantId",
                table: "P6SyncLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificates_ProjectId_ContractRef_CertNumber",
                table: "PaymentCertificates",
                columns: new[] { "ProjectId", "ContractRef", "CertNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificates_ProjectId_Status",
                table: "PaymentCertificates",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificates_TenantId",
                table: "PaymentCertificates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderEventId",
                table: "Payments",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"ProviderEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PenetrationSignoffs_TenantId",
                table: "PenetrationSignoffs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAccessRules_DistributionGroupId",
                table: "PhotoAccessRules",
                column: "DistributionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAccessRules_PhotoId",
                table: "PhotoAccessRules",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbumPhotos_PhotoId",
                table: "PhotoAlbumPhotos",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbums_DistributionGroupId",
                table: "PhotoAlbums",
                column: "DistributionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbums_ProjectId",
                table: "PhotoAlbums",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbums_ProjectId_Kind",
                table: "PhotoAlbums",
                columns: new[] { "ProjectId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbums_TenantId",
                table: "PhotoAlbums",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnnotations_CreatedByUserId",
                table: "PhotoAnnotations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnnotations_PhotoId",
                table: "PhotoAnnotations",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoApprovalSignoffs_PhotoId_Stage",
                table: "PhotoApprovalSignoffs",
                columns: new[] { "PhotoId", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoApprovalSignoffs_SignedByUserId",
                table: "PhotoApprovalSignoffs",
                column: "SignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoChecklistItems_ChecklistId",
                table: "PhotoChecklistItems",
                column: "ChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoChecklistItems_FulfilledByPhotoId",
                table: "PhotoChecklistItems",
                column: "FulfilledByPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoChecklists_ProjectId",
                table: "PhotoChecklists",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoChecklists_ProjectId_Status",
                table: "PhotoChecklists",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoChecklists_TenantId",
                table: "PhotoChecklists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoNdaAcceptances_UserId",
                table: "PhotoNdaAcceptances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicies_DigestDistributionGroupId",
                table: "PhotoPolicies",
                column: "DigestDistributionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicies_ProjectId",
                table: "PhotoPolicies",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicies_TenantId",
                table: "PhotoPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoShareLinks_AlbumId",
                table: "PhotoShareLinks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoShareLinks_PhotoId",
                table: "PhotoShareLinks",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoShareLinks_ProjectId",
                table: "PhotoShareLinks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoShareLinks_TenantId",
                table: "PhotoShareLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoShareLinks_Token",
                table: "PhotoShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVoiceNotes_DocumentId",
                table: "PhotoVoiceNotes",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVoiceNotes_PhotoId",
                table: "PhotoVoiceNotes",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoVoiceNotes_TenantId",
                table: "PhotoVoiceNotes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PinCrdtUpdates_TenantId",
                table: "PinCrdtUpdates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformConnections_ProjectId",
                table: "PlatformConnections",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformConnections_TenantId",
                table: "PlatformConnections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformConnections_TenantId_ProjectId_Platform",
                table: "PlatformConnections",
                columns: new[] { "TenantId", "ProjectId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCoordinateSystems_ProjectId",
                table: "ProjectCoordinateSystems",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCoordinateSystems_TenantId",
                table: "ProjectCoordinateSystems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLevels_ProjectId_NormalizedName",
                table: "ProjectLevels",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLevels_ProjectId_SortIndex",
                table: "ProjectLevels",
                columns: new[] { "ProjectId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLevels_TenantId",
                table: "ProjectLevels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_ProjectId_UserId",
                table: "ProjectMembers",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId",
                table: "ProjectMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_UserId",
                table: "ProjectMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModels_ContentHash",
                table: "ProjectModels",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModels_ProjectId",
                table: "ProjectModels",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModels_TenantId",
                table: "ProjectModels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModels_UploadedByUserId",
                table: "ProjectModels",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModelTransforms_ProjectId",
                table: "ProjectModelTransforms",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModelTransforms_ProjectModelId",
                table: "ProjectModelTransforms",
                column: "ProjectModelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModelTransforms_TenantId",
                table: "ProjectModelTransforms",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId",
                table: "Projects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Code",
                table: "Projects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_CreatedById",
                table: "Projects",
                columns: new[] { "TenantId", "CreatedById" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_BaselineId",
                table: "QuantityLines",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_ClassificationCodeId",
                table: "QuantityLines",
                column: "ClassificationCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_IfcGlobalId",
                table: "QuantityLines",
                column: "IfcGlobalId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_ProjectId_BaselineId",
                table: "QuantityLines",
                columns: new[] { "ProjectId", "BaselineId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_ProjectId_SectionCode",
                table: "QuantityLines",
                columns: new[] { "ProjectId", "SectionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_ProjectModelId",
                table: "QuantityLines",
                column: "ProjectModelId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_TakeoffRuleId",
                table: "QuantityLines",
                column: "TakeoffRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_TenantId",
                table: "QuantityLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityLines_WorkPackageId",
                table: "QuantityLines",
                column: "WorkPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_CapturedByUserId",
                table: "SavedViews",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_LinkedActionItemId",
                table: "SavedViews",
                column: "LinkedActionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_LinkedMeetingId",
                table: "SavedViews",
                column: "LinkedMeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_ProjectId_CreatedAt",
                table: "SavedViews",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_TenantId",
                table: "SavedViews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneNodes_TenantId",
                table: "SceneNodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTasks_ProjectId",
                table: "ScheduleTasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTasks_ProjectId_Code",
                table: "ScheduleTasks",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTasks_TenantId",
                table: "ScheduleTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SeqCounters_ProjectId_CounterKey",
                table: "SeqCounters",
                columns: new[] { "ProjectId", "CounterKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeqCounters_TenantId",
                table: "SeqCounters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaries_AuthorUserId",
                table: "SiteDiaries",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaries_ProjectId",
                table: "SiteDiaries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaries_ProjectId_DiaryDate",
                table: "SiteDiaries",
                columns: new[] { "ProjectId", "DiaryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaries_Status",
                table: "SiteDiaries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaries_TenantId",
                table: "SiteDiaries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaryAttachments_DocumentId",
                table: "SiteDiaryAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteDiaryAttachments_SiteDiaryId",
                table: "SiteDiaryAttachments",
                column: "SiteDiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_AnchorElementGuid",
                table: "SitePhotos",
                column: "AnchorElementGuid");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_AnchorIssueId",
                table: "SitePhotos",
                column: "AnchorIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_ApprovedByUserId",
                table: "SitePhotos",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_CapturedAt",
                table: "SitePhotos",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_CapturedByUserId",
                table: "SitePhotos",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_DocumentId",
                table: "SitePhotos",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_PairKey",
                table: "SitePhotos",
                column: "PairKey");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_ProjectId",
                table: "SitePhotos",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_ProjectId_Audience",
                table: "SitePhotos",
                columns: new[] { "ProjectId", "Audience" });

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_ProjectId_PairKey",
                table: "SitePhotos",
                columns: new[] { "ProjectId", "PairKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_ProjectId_Reason",
                table: "SitePhotos",
                columns: new[] { "ProjectId", "Reason" });

            migrationBuilder.CreateIndex(
                name: "IX_SitePhotos_TenantId",
                table: "SitePhotos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoConfigs_TenantId",
                table: "SsoConfigs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGateCriteria_StageGateId",
                table: "StageGateCriteria",
                column: "StageGateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGateCriteria_StageGateId_Key",
                table: "StageGateCriteria",
                columns: new[] { "StageGateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageGateCriteria_TenantId",
                table: "StageGateCriteria",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGates_ProjectId",
                table: "StageGates",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StageGates_ProjectId_StageCode",
                table: "StageGates",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageGates_Status",
                table: "StageGates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StageGates_TenantId",
                table: "StageGates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ProviderSubscriptionId",
                table: "Subscriptions",
                column: "ProviderSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId",
                table: "Subscriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId_Status",
                table: "Subscriptions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityTransitionRules_TenantId",
                table: "SuitabilityTransitionRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityTransitionRules_TenantId_ProjectId_FromCode_ToCo~",
                table: "SuitabilityTransitionRules",
                columns: new[] { "TenantId", "ProjectId", "FromCode", "ToCode" });

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityTransitions_DocumentRecordId_TriggeredAt",
                table: "SuitabilityTransitions",
                columns: new[] { "DocumentRecordId", "TriggeredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityTransitions_ProjectId_TriggeredAt",
                table: "SuitabilityTransitions",
                columns: new[] { "ProjectId", "TriggeredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SuitabilityTransitions_TenantId",
                table: "SuitabilityTransitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_DetectedAt",
                table: "SyncConflicts",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_ProjectId_ElementId",
                table: "SyncConflicts",
                columns: new[] { "ProjectId", "ElementId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_TaggedElementId",
                table: "SyncConflicts",
                column: "TaggedElementId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_TenantId",
                table: "SyncConflicts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncWatermarks_LastSyncUtc",
                table: "SyncWatermarks",
                column: "LastSyncUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SyncWatermarks_ProjectId_DeviceId",
                table: "SyncWatermarks",
                columns: new[] { "ProjectId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncWatermarks_TenantId",
                table: "SyncWatermarks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_Disc",
                table: "TaggedElements",
                column: "Disc");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_IsStale",
                table: "TaggedElements",
                column: "IsStale");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_LastModifiedUtc",
                table: "TaggedElements",
                column: "LastModifiedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_ProjectId_RevitElementId",
                table: "TaggedElements",
                columns: new[] { "ProjectId", "RevitElementId" },
                unique: true,
                filter: "\"RevitElementId\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_ProjectId_UniqueId",
                table: "TaggedElements",
                columns: new[] { "ProjectId", "UniqueId" },
                unique: true,
                filter: "\"UniqueId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_Tag1",
                table: "TaggedElements",
                column: "Tag1");

            migrationBuilder.CreateIndex(
                name: "IX_TaggedElements_TenantId",
                table: "TaggedElements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TakeoffRules_ClassificationCodeId",
                table: "TakeoffRules",
                column: "ClassificationCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TakeoffRules_ProjectId",
                table: "TakeoffRules",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TakeoffRules_TenantId",
                table: "TakeoffRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TakeoffRules_TenantId_ProjectId_Priority",
                table: "TakeoffRules",
                columns: new[] { "TenantId", "ProjectId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_TenantId",
                table: "TenantBrandings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalDocuments_DocumentId",
                table: "TransmittalDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalDocuments_DocumentVersionId",
                table: "TransmittalDocuments",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalDocuments_TransmittalId_DocumentId",
                table: "TransmittalDocuments",
                columns: new[] { "TransmittalId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_ProjectId_TransmittalCode",
                table: "Transmittals",
                columns: new[] { "ProjectId", "TransmittalCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_TenantId",
                table: "Transmittals",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreferences_TenantId",
                table: "UserNotificationPreferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreferences_UserId",
                table: "UserNotificationPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_ProjectId",
                table: "WorkflowRuns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_TenantId",
                table: "WorkflowRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackages_ProjectId_Code",
                table: "WorkPackages",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackages_TenantId",
                table: "WorkPackages",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessProfiles");

            migrationBuilder.DropTable(
                name: "ApprovalStages");

            migrationBuilder.DropTable(
                name: "AssetDataSheets");

            migrationBuilder.DropTable(
                name: "AssetDataSheetTemplates");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BoqDocuments");

            migrationBuilder.DropTable(
                name: "BoqPreambleAssignments");

            migrationBuilder.DropTable(
                name: "BoqSnapshots");

            migrationBuilder.DropTable(
                name: "BoqVariations");

            migrationBuilder.DropTable(
                name: "ClashAutomationRules");

            migrationBuilder.DropTable(
                name: "ClashRecords");

            migrationBuilder.DropTable(
                name: "ComplianceSnapshots");

            migrationBuilder.DropTable(
                name: "CoordinatorWorkloads");

            migrationBuilder.DropTable(
                name: "CostItems");

            migrationBuilder.DropTable(
                name: "DashboardWidgets");

            migrationBuilder.DropTable(
                name: "DevicePushTokens");

            migrationBuilder.DropTable(
                name: "DistributionGroupMembers");

            migrationBuilder.DropTable(
                name: "DocumentApprovals");

            migrationBuilder.DropTable(
                name: "DocumentMarkups");

            migrationBuilder.DropTable(
                name: "DocumentRevisions");

            migrationBuilder.DropTable(
                name: "DocumentSignatures");

            migrationBuilder.DropTable(
                name: "ExternalElementMappings");

            migrationBuilder.DropTable(
                name: "FederatedElements");

            migrationBuilder.DropTable(
                name: "GlobalIdRegistry");

            migrationBuilder.DropTable(
                name: "HealthcareAntiLigatureAudits");

            migrationBuilder.DropTable(
                name: "HealthcareMgasVerifications");

            migrationBuilder.DropTable(
                name: "HealthcarePressureLogs");

            migrationBuilder.DropTable(
                name: "HealthcareRdsSnapshots");

            migrationBuilder.DropTable(
                name: "HvacLoadSnapshot");

            migrationBuilder.DropTable(
                name: "HvacNcSnapshot");

            migrationBuilder.DropTable(
                name: "HvacRefrigerantSizing");

            migrationBuilder.DropTable(
                name: "HvacSnapshots");

            migrationBuilder.DropTable(
                name: "IfcAlignmentReports");

            migrationBuilder.DropTable(
                name: "IfcElementSnapshots");

            migrationBuilder.DropTable(
                name: "InformationDeliverables");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "IssueAttachments");

            migrationBuilder.DropTable(
                name: "IssueAudioNotes");

            migrationBuilder.DropTable(
                name: "IssueComments");

            migrationBuilder.DropTable(
                name: "IssueCustomFieldSchemas");

            migrationBuilder.DropTable(
                name: "KpiSnapshots");

            migrationBuilder.DropTable(
                name: "LicenseKeys");

            migrationBuilder.DropTable(
                name: "LpsRecords");

            migrationBuilder.DropTable(
                name: "MaintenanceTasks");

            migrationBuilder.DropTable(
                name: "MeetingActionItems");

            migrationBuilder.DropTable(
                name: "MeetingAgendaItems");

            migrationBuilder.DropTable(
                name: "MeetingAttendees");

            migrationBuilder.DropTable(
                name: "MfaChallenges");

            migrationBuilder.DropTable(
                name: "MfaEnrollments");

            migrationBuilder.DropTable(
                name: "MobileOfflineModelManifests");

            migrationBuilder.DropTable(
                name: "ModelCheckResults");

            migrationBuilder.DropTable(
                name: "ModelMarkups");

            migrationBuilder.DropTable(
                name: "Nrm2PreliminariesItems");

            migrationBuilder.DropTable(
                name: "OutboundWebhooks");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "P6SyncLogs");

            migrationBuilder.DropTable(
                name: "PaymentCertificates");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PenetrationSignoffs");

            migrationBuilder.DropTable(
                name: "PhotoAccessRules");

            migrationBuilder.DropTable(
                name: "PhotoAlbumPhotos");

            migrationBuilder.DropTable(
                name: "PhotoAnnotations");

            migrationBuilder.DropTable(
                name: "PhotoApprovalSignoffs");

            migrationBuilder.DropTable(
                name: "PhotoChecklistItems");

            migrationBuilder.DropTable(
                name: "PhotoNdaAcceptances");

            migrationBuilder.DropTable(
                name: "PhotoPolicies");

            migrationBuilder.DropTable(
                name: "PhotoShareLinks");

            migrationBuilder.DropTable(
                name: "PhotoVoiceNotes");

            migrationBuilder.DropTable(
                name: "PinCrdtUpdates");

            migrationBuilder.DropTable(
                name: "PlatformConnections");

            migrationBuilder.DropTable(
                name: "ProjectCoordinateSystems");

            migrationBuilder.DropTable(
                name: "ProjectLevels");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "ProjectModelTransforms");

            migrationBuilder.DropTable(
                name: "QuantityLines");

            migrationBuilder.DropTable(
                name: "SavedViews");

            migrationBuilder.DropTable(
                name: "SeqCounters");

            migrationBuilder.DropTable(
                name: "SiteDiaryAttachments");

            migrationBuilder.DropTable(
                name: "SsoConfigs");

            migrationBuilder.DropTable(
                name: "StageGateCriteria");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "SuitabilityTransitionRules");

            migrationBuilder.DropTable(
                name: "SuitabilityTransitions");

            migrationBuilder.DropTable(
                name: "SyncConflicts");

            migrationBuilder.DropTable(
                name: "SyncWatermarks");

            migrationBuilder.DropTable(
                name: "TenantBrandings");

            migrationBuilder.DropTable(
                name: "TransmittalDocuments");

            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");

            migrationBuilder.DropTable(
                name: "ApprovalChains");

            migrationBuilder.DropTable(
                name: "Nrm2Preambles");

            migrationBuilder.DropTable(
                name: "ScheduleTasks");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "SceneNodes");

            migrationBuilder.DropTable(
                name: "ModelCheckRules");

            migrationBuilder.DropTable(
                name: "ModelCheckRuns");

            migrationBuilder.DropTable(
                name: "PhotoChecklists");

            migrationBuilder.DropTable(
                name: "PhotoAlbums");

            migrationBuilder.DropTable(
                name: "SitePhotos");

            migrationBuilder.DropTable(
                name: "BoqBaselines");

            migrationBuilder.DropTable(
                name: "ProjectModels");

            migrationBuilder.DropTable(
                name: "TakeoffRules");

            migrationBuilder.DropTable(
                name: "WorkPackages");

            migrationBuilder.DropTable(
                name: "Meetings");

            migrationBuilder.DropTable(
                name: "SiteDiaries");

            migrationBuilder.DropTable(
                name: "StageGates");

            migrationBuilder.DropTable(
                name: "TaggedElements");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "Transmittals");

            migrationBuilder.DropTable(
                name: "ModelCheckRuleSets");

            migrationBuilder.DropTable(
                name: "DistributionGroups");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "ClassificationCodes");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ClassificationSystems");

            migrationBuilder.DropTable(
                name: "CdeContainers");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
