using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterModels — POCO models for the STING Export Centre.
    //
    //  Backs the WPF dialog (StingExportCenterDialog) and the export pipeline
    //  (ExportCenterEngine). All persistent state goes here, JSON-serialised
    //  into project_config.json under "ExportCenter".
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>UI mode toggle. Controls which features and columns are shown.</summary>
    public enum ExportCenterMode
    {
        /// <summary>ISO 19650-driven BIM coordinator surface (full features).</summary>
        BIM,
        /// <summary>Plain English non-ISO surface (reduced columns, friendly naming).</summary>
        Simple,
    }

    /// <summary>Whether the SELECT panel lists sheets or non-sheet views.</summary>
    public enum ExportSelectionKind { Sheets, Views }

    /// <summary>Active output formats (bit flags so multiple can be combined).</summary>
    [Flags]
    public enum ExportFormats
    {
        None  = 0,
        PDF   = 1 << 0,
        DWG   = 1 << 1,
        IFC   = 1 << 2,
        NWC   = 1 << 3,
        DGN   = 1 << 4,
        DWF   = 1 << 5,
        Image = 1 << 6,
        XML   = 1 << 7,
    }

    /// <summary>How combined PDF output is grouped.</summary>
    public enum PdfCombineMode
    {
        OnePerSheet,
        OnePerDiscipline,
        OnePerSet,
        CustomGroups,
    }

    /// <summary>How DWG output is grouped — drives the multi-layout pipeline.</summary>
    public enum DwgOutputMode
    {
        OnePerSheet,
        AllInOneMultiLayout,
        ModelSpaceOnly,
        CustomGroups,
    }

    /// <summary>Where the export lands.</summary>
    public enum ExportDestination
    {
        LocalFolder,
        PlanscapeCde,
        Both,
    }

    /// <summary>Filename collision strategy.</summary>
    public enum FilenameConflictMode { Skip, Overwrite, AutoRename, Ask }

    /// <summary>Document approval suitability code (BS EN ISO 19650-2 §A.2).</summary>
    public enum SuitabilityCode { S0, S1, S2, S3, S4, S6, S7, A1, A2, A3, AB, B1, B2, B3, CR }

    // ── PDF settings ────────────────────────────────────────────────────────────

    public class PdfExportSettings
    {
        public PdfCombineMode CombineMode { get; set; } = PdfCombineMode.OnePerSheet;
        public bool AddBookmarks { get; set; } = true;
        public bool NestBookmarksByDiscipline { get; set; } = true;
        public bool NestBookmarksByLevel { get; set; }
        public string BookmarkLabelTemplate { get; set; } = "{SheetNumber} - {SheetTitle}";

        public string DocumentTitleTemplate { get; set; } = "{ProjectName} — {DrawingSet}";
        public string DocumentAuthorTemplate { get; set; } = "{OriginatorCode}";
        public string DocumentSubjectTemplate { get; set; } = "{Discipline} Drawing Package";
        public string DocumentKeywordsTemplate { get; set; } = "{ProjectCode},{Revision}";

        public string HiddenLineMode { get; set; } = "Auto";   // Vector / Raster / Auto
        public string ColourScheme { get; set; } = "Colour";   // Colour / Greyscale / BlackAndWhite
        public int RasterDpi { get; set; } = 300;
        public string PaperPlacement { get; set; } = "Centre"; // Centre / Offset
        public double OffsetXmm { get; set; }
        public double OffsetYmm { get; set; }
        public string Zoom { get; set; } = "Fit";
        public int ZoomPercent { get; set; } = 100;

        public string Printer { get; set; } = "RevitNative";   // RevitNative / PDF24 / SystemDefault

        public bool ApplyWatermark { get; set; }
        public string WatermarkText { get; set; } = "DRAFT";
        public string WatermarkPosition { get; set; } = "DiagonalCentre";
        public int WatermarkOpacityPct { get; set; } = 30;
        public int WatermarkFontSize { get; set; } = 96;
        public string WatermarkColourHex { get; set; } = "#999999";

        /// <summary>User-defined groups (CustomGroups mode). Map of group name → list of sheet ids (as strings).</summary>
        public Dictionary<string, List<string>> CustomGroups { get; set; } = new();

        /// <summary>Optional explicit merge order overriding sheet-number sort.</summary>
        public List<string> MergeOrderSheetIds { get; set; } = new();
    }

    // ── DWG settings ────────────────────────────────────────────────────────────

    public class DwgExportSettings
    {
        public DwgOutputMode OutputMode { get; set; } = DwgOutputMode.OnePerSheet;
        public string ExportSetupName { get; set; } = "<in-session>";
        public string LayerMappingMode { get; set; } = "ByCategory";  // ByCategory / Standard / Custom
        public string LayerStandard { get; set; } = "AIA";            // AIA / BS1192 / Custom
        public string LayerCustomMappingFile { get; set; }
        public string LineworkSource { get; set; } = "Revit";         // Revit / ExportSetup
        public string CoordinateSystem { get; set; } = "Project";     // Project / Shared / Survey
        public string LinkedModelMode { get; set; } = "Embed";        // Embed / XRefs / Ignore
        public bool BindRasterImages { get; set; }
        public bool RunAuditAfterExport { get; set; }
        public string DwgVersion { get; set; } = "AC2018";            // AC2018 / AC2013 / AC2010 / AC2007 / AC2004

        public string LayoutNameTemplate { get; set; } = "{SheetNumber}";
        public Dictionary<string, List<string>> CustomGroups { get; set; } = new();

        /// <summary>Whether to fall back to individual files if the multi-layout merge tool is unavailable.</summary>
        public bool FallbackOnMergeFailure { get; set; } = true;
    }

    // ── IFC / NWC / Image / DGN / DWF / XML ─────────────────────────────────────

    public class IfcExportSettings
    {
        public string ExportSetupName { get; set; } = "<in-session>";
        public string Schema { get; set; } = "IFC4";            // IFC2x3 / IFC4 / IFC4x3
        public string PhaseName { get; set; }
        public bool ExportLinkedModels { get; set; }
        public string Classification { get; set; } = "None";    // Uniclass / OmniClass / None
        public string Geometry { get; set; } = "BRep";          // Solid / BRep / Triangulated
        public string CoordinateOrigin { get; set; } = "Project"; // Project / Survey / Internal
    }

    public class NwcExportSettings
    {
        public string Scope { get; set; } = "Selected";          // Entire / Selected / CurrentView
        public string CoordinateSystem { get; set; } = "Project";
        public bool ExportElementIdsForClash { get; set; } = true;
    }

    public class ImageExportSettings
    {
        public string Format { get; set; } = "PNG";              // JPEG / PNG / TIFF
        public int Dpi { get; set; } = 300;
        public string ColourDepth { get; set; } = "RGB24";       // RGB24 / Grey8
        public int JpegQuality { get; set; } = 85;
        public bool TransparentBackground { get; set; }
    }

    public class DgnExportSettings
    {
        public string Version { get; set; } = "V8";
        public string CoordinateSystem { get; set; } = "Project";
    }

    public class DwfExportSettings
    {
        public bool IncludeRoomBoundaries { get; set; } = true;
        public bool IncludeMarkupGeometry { get; set; } = true;
        public bool DwfX { get; set; } = true;
    }

    public class XmlExportSettings
    {
        public string Scope { get; set; } = "Selected";          // Selected / ProjectInfoOnly
        public bool IncludeProjectInfo { get; set; } = true;
        public string Format { get; set; } = "GroupedBySheet";   // FlatList / GroupedBySheet
        public List<string> ParameterGroups { get; set; } = new()
        {
            "Identity", "Location", "Dimensions", "Revisions",
        };
    }

    // ── Output settings (path + naming) ─────────────────────────────────────────

    public class OutputSettings
    {
        public ExportDestination Destination { get; set; } = ExportDestination.LocalFolder;
        public string LocalFolder { get; set; } = "";
        public bool OpenFolderWhenDone { get; set; } = true;
        public bool CreateFolderIfMissing { get; set; } = true;
        public bool SplitByFormatSubFolder { get; set; }
        public bool SplitByDisciplineSubFolder { get; set; }

        // CDE only
        public string CdeStateOnUpload { get; set; } = "Shared";
        public SuitabilityCode CdeSuitability { get; set; } = SuitabilityCode.S2;
        public bool CdeAutoRegister { get; set; } = true;
        public bool CdeTriggerWorkflow { get; set; }
        public string CdeWorkflowPreset { get; set; } = "deliverable_issue_default";
        public bool CdeNotifyTeam { get; set; } = true;

        // Naming — default to ISO 19650-2 full template; ExportCenterEngine
        // auto-populates {ProjectCode} / {Originator} / {Volume} / {Level} /
        // {Type} / {Role} / {Suitability} / {Revision} from ProjectInformation,
        // sheet STING_* params, and the stamped DrawingType (Phase 113).
        public string NamingTemplate { get; set; } =
            "{ProjectCode}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{SheetNumber}-{Suitability}-{Revision}";
        public string NamingSeparator { get; set; } = "-";
        public FilenameConflictMode ConflictMode { get; set; } = FilenameConflictMode.AutoRename;
        public string IllegalCharReplacement { get; set; } = "-";

        // Report
        public bool GenerateReport { get; set; } = true;
        public string ReportFormat { get; set; } = "XLSX";       // XLSX / CSV
        public bool OpenReportWhenDone { get; set; }
    }

    /// <summary>Persistent saved selection — names + a list of sheet/view ElementIds (as strings to survive across docs).</summary>
    public class ExportSavedSet
    {
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ExportSelectionKind Kind { get; set; } = ExportSelectionKind.Sheets;
        public List<string> ElementIds { get; set; } = new();
        public string FilterText { get; set; }
        public string SortColumn { get; set; }
        public bool SortAscending { get; set; } = true;
        public bool BuiltIn { get; set; }
    }

    /// <summary>A complete dialog state, named, persistable, importable, shareable.</summary>
    public class ExportProfile
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool BuiltIn { get; set; }

        public ExportCenterMode Mode { get; set; } = ExportCenterMode.BIM;
        public ExportFormats Formats { get; set; } = ExportFormats.PDF;

        public PdfExportSettings Pdf { get; set; } = new();
        public DwgExportSettings Dwg { get; set; } = new();
        public IfcExportSettings Ifc { get; set; } = new();
        public NwcExportSettings Nwc { get; set; } = new();
        public ImageExportSettings Image { get; set; } = new();
        public DgnExportSettings Dgn { get; set; } = new();
        public DwfExportSettings Dwf { get; set; } = new();
        public XmlExportSettings Xml { get; set; } = new();

        public OutputSettings Output { get; set; } = new();

        /// <summary>The set name used as the default selection when this profile is loaded.</summary>
        public string DefaultSetName { get; set; } = "All Sheets";
    }

    /// <summary>A scheduled export job persisted in project_config.json.</summary>
    public class ScheduledExport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ProfileName { get; set; }
        public string SetName { get; set; }
        public DateTime NextRunUtc { get; set; }
        public string Repeat { get; set; } = "Once"; // Once / Daily / Weekly / Monthly
        public List<DayOfWeek> WeeklyDays { get; set; } = new();
        public bool Enabled { get; set; } = true;
        public DateTime? LastRunUtc { get; set; }
        public string LastResult { get; set; }
    }

    /// <summary>Top-level container persisted under "ExportCenter" inside project_config.json.</summary>
    public class ExportCenterState
    {
        public ExportCenterMode Mode { get; set; } = ExportCenterMode.BIM;
        public string LastProfile { get; set; }
        public List<ExportProfile> Profiles { get; set; } = new();
        public List<ExportSavedSet> SavedSets { get; set; } = new();
        public List<ScheduledExport> ScheduledExports { get; set; } = new();
        /// <summary>Opt-in: run any due <see cref="ScheduledExports"/> automatically on
        /// DocumentSaved. Off by default so exported files never appear unexpectedly;
        /// the manual ExportCenterRunSchedulesCommand path is always available.</summary>
        public bool EnableSaveTriggeredSchedules { get; set; }
        public List<string> RecentFolders { get; set; } = new();
        public string LastOutputFolder { get; set; }
        public string LastNamingTemplate { get; set; }
        public string OdaLibraryPath { get; set; }
        public bool? AutocadComAvailable { get; set; }

        // ── Built-in profile factory ────────────────────────────────────────────

        public static List<ExportProfile> BuildBuiltInProfiles()
        {
            var list = new List<ExportProfile>();

            list.Add(new ExportProfile
            {
                Name = "Default — PDF only",
                BuiltIn = true,
                Mode = ExportCenterMode.Simple,
                Formats = ExportFormats.PDF,
            });

            list.Add(new ExportProfile
            {
                Name = "Full Issue Package — PDF + DWG + IFC",
                BuiltIn = true,
                Mode = ExportCenterMode.BIM,
                Formats = ExportFormats.PDF | ExportFormats.DWG | ExportFormats.IFC,
                Output = new OutputSettings { SplitByFormatSubFolder = true },
            });

            list.Add(new ExportProfile
            {
                Name = "Client Presentation — Combined PDF, Greyscale",
                BuiltIn = true,
                Mode = ExportCenterMode.Simple,
                Formats = ExportFormats.PDF,
                Pdf = new PdfExportSettings
                {
                    CombineMode = PdfCombineMode.OnePerSet,
                    AddBookmarks = true,
                    NestBookmarksByDiscipline = true,
                    ColourScheme = "Greyscale",
                },
            });

            list.Add(new ExportProfile
            {
                Name = "Contractor Package — Individual PDFs + DWG with Layouts",
                BuiltIn = true,
                Mode = ExportCenterMode.BIM,
                Formats = ExportFormats.PDF | ExportFormats.DWG,
                Pdf = new PdfExportSettings { CombineMode = PdfCombineMode.OnePerDiscipline },
                Dwg = new DwgExportSettings { OutputMode = DwgOutputMode.AllInOneMultiLayout },
            });

            list.Add(new ExportProfile
            {
                Name = "BIM Model Exchange — IFC + NWC",
                BuiltIn = true,
                Mode = ExportCenterMode.BIM,
                Formats = ExportFormats.IFC | ExportFormats.NWC,
            });

            list.Add(new ExportProfile
            {
                Name = "Simple — PDF only (non-ISO)",
                BuiltIn = true,
                Mode = ExportCenterMode.Simple,
                Formats = ExportFormats.PDF,
                Output = new OutputSettings { NamingTemplate = "{SheetNumber} - {SheetTitle}" },
            });

            list.Add(new ExportProfile
            {
                Name = "ISO 19650 — PDF + DWG",
                BuiltIn = true,
                Mode = ExportCenterMode.BIM,
                Formats = ExportFormats.PDF | ExportFormats.DWG,
                Output = new OutputSettings { NamingTemplate = ExportNamingPresets.Iso19650Full },
            });

            return list;
        }

        // ── Built-in saved-set factory ──────────────────────────────────────────

        public static List<ExportSavedSet> BuildBuiltInSets()
        {
            var built = new List<ExportSavedSet>();
            void S(string n) => built.Add(new ExportSavedSet { Name = n, BuiltIn = true });

            S("All Sheets");
            S("All Views");
            S("By Discipline: Architectural");
            S("By Discipline: Mechanical");
            S("By Discipline: Electrical");
            S("By Discipline: Plumbing");
            S("By Discipline: Structural");
            S("Issued Sheets");
            S("Revised This Week");
            S("Currently Opened");
            return built;
        }
    }

    // ── Naming preset catalogue (Simple-mode quick presets) ─────────────────────

    public static class ExportNamingPresets
    {
        // ISO 19650-2 file naming: <Project>-<Originator>-<Volume>-<Level>-<Type>-<Role>-<Number>[-<Status>-<Revision>]
        // Tokens are resolved per-sheet by ExportCenterEngine.BuildTokenContext.
        public const string Iso19650Full    = "{ProjectCode}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{SheetNumber}-{Suitability}-{Revision}";
        public const string Iso19650Compact = "{Originator}-{SheetNumber}-{Suitability}{Revision}";

        // Default preset name used when no profile-specific preset is saved.
        public const string DefaultKey = "ISO 19650 (full)";

        public static readonly Dictionary<string, string> SimpleMode = new()
        {
            { "ISO 19650 (full)",       Iso19650Full },
            { "ISO 19650 (compact)",    Iso19650Compact },
            { "US Standard",            "{SheetNumber} - {SheetTitle}" },
            { "UK Basic",               "{SheetNumber}_{SheetTitle}_Rev{Revision}" },
            { "Australia",              "{ProjectCode}-{SheetNumber}-{SheetTitle}" },
            { "Africa / General",       "{ProjectName}_{SheetNumber}_{Revision}_{Date:yyyyMMdd}" },
        };

        public static readonly Dictionary<string, string> BimMode = new()
        {
            { "ISO 19650 (full)",       Iso19650Full },
            { "ISO 19650 (compact)",    Iso19650Compact },
            { "Issue + Date",           "{ProjectCode}-{SheetNumber}-{SheetTitle}-{Suitability}{Revision}_{Date:yyyyMMdd}" },
            { "Drawing No. only",       "{SheetNumber}" },
        };
    }

    /// <summary>Per-result row produced by the export pipeline (used in the report and post-export log).</summary>
    public class ExportResultRow
    {
        public string SheetId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetTitle { get; set; }
        public string Format { get; set; }
        public string OutputPath { get; set; }
        public long FileSizeBytes { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public TimeSpan Duration => FinishedUtc - StartedUtc;
    }

    /// <summary>Aggregate result from one export run.</summary>
    public class ExportRunResult
    {
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime FinishedUtc { get; set; }
        public ExportProfile Profile { get; set; }
        public List<ExportResultRow> Rows { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool Cancelled { get; set; }

        public int Success => Rows.FindAll(r => r.Success).Count;
        public int Failed  => Rows.FindAll(r => !r.Success).Count;
        public long TotalBytes
        {
            get { long t = 0; foreach (var r in Rows) t += r.FileSizeBytes; return t; }
        }
    }

    /// <summary>A pre-flight problem found before export starts.</summary>
    public class ExportPreflightIssue
    {
        public enum Severity { Info, Warning, Error }
        public Severity Level { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Hint { get; set; }
    }
}
