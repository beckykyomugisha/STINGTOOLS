// StingTools — Drawing Template Manager · Excel Round-Trip
//
// Bidirectional Excel ↔ JSON exchange for the corporate Drawing Type
// catalogue and the View Style Pack library. Power users and BIM
// managers can export everything to a structured workbook, edit values
// (with validation dropdowns and live colour swatches), and import
// back into the project's _BIM_COORD/ override files. The runtime
// caches are invalidated on import so changes take effect without a
// Revit restart.
//
// Wired from the DOCS tab (DrawingTypes_ExportExcel /
// DrawingTypes_ImportExcel button tags) via StingCommandHandler.
//
// Workbook layout: 8 sheets (DrawingTypes, StylePacks, VgOverrides,
// FilterRules, Slots, TitleBlockParams, Routing, _Legend hidden).
// Colour cells in VgOverrides + FilterRules render the resolved hex
// value as the cell background fill — instant visual swatch.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;

namespace StingTools.BIMManager
{
    #region ── File-format POCOs (mirror STING_VIEW_STYLE_PACKS.json) ──

    // The runtime ViewStylePackLibrary class uses different JSON keys
    // than the on-disk file (drift documented in CLAUDE.md). The Excel
    // round-trip needs to preserve the actual file shape, so it uses
    // these POCOs that match the file 1:1. Mirrors the editor-side
    // model in DrawingTypeEditorDialog.cs.
    internal sealed class StylePackDoc
    {
        [JsonProperty("schemaVersion", NullValueHandling = NullValueHandling.Ignore)] public string SchemaVersion { get; set; }
        [JsonProperty("name",          NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        [JsonProperty("description",   NullValueHandling = NullValueHandling.Ignore)] public string Description { get; set; }
        [JsonProperty("namespace",     NullValueHandling = NullValueHandling.Ignore)] public string Namespace { get; set; }
        [JsonProperty("lastUpdated",   NullValueHandling = NullValueHandling.Ignore)] public string LastUpdated { get; set; }
        [JsonProperty("stylePacks")] public List<StylePackEntry> StylePacks { get; set; } = new();
        [JsonProperty("routing", NullValueHandling = NullValueHandling.Ignore)] public List<StylePackRoutingRule> Routing { get; set; }
    }

    internal sealed class StylePackEntry
    {
        [JsonProperty("id")]                                                                public string Id { get; set; }
        [JsonProperty("name",          NullValueHandling = NullValueHandling.Ignore)]      public string Name { get; set; }
        [JsonProperty("description",   NullValueHandling = NullValueHandling.Ignore)]      public string Description { get; set; }
        [JsonProperty("extends",       NullValueHandling = NullValueHandling.Ignore)]      public string Extends { get; set; }
        [JsonProperty("origin",        NullValueHandling = NullValueHandling.Ignore)]      public string Origin { get; set; }
        [JsonProperty("viewTemplate",  NullValueHandling = NullValueHandling.Ignore)]      public string ViewTemplate { get; set; }
        [JsonProperty("detailLevel",   NullValueHandling = NullValueHandling.Ignore)]      public string DetailLevel { get; set; }
        [JsonProperty("scaleHint",     NullValueHandling = NullValueHandling.Ignore)]      public string ScaleHint { get; set; }
        [JsonProperty("colorScheme",   NullValueHandling = NullValueHandling.Ignore)]      public string ColorScheme { get; set; }
        [JsonProperty("appearance",    NullValueHandling = NullValueHandling.Ignore)]      public StylePackAppearance Appearance { get; set; }
        [JsonProperty("filterRules",   NullValueHandling = NullValueHandling.Ignore)]      public List<StylePackFilterRule> FilterRules { get; set; }
        [JsonProperty("vgOverrides",   NullValueHandling = NullValueHandling.Ignore)]      public Dictionary<string, StylePackVgOverride> VgOverrides { get; set; }
        [JsonProperty("tagColorScheme",   NullValueHandling = NullValueHandling.Ignore)]   public string TagColorScheme { get; set; }
        [JsonProperty("defaultTagStyle",  NullValueHandling = NullValueHandling.Ignore)]   public string DefaultTagStyle { get; set; }
        [JsonProperty("categoryTagStyles",NullValueHandling = NullValueHandling.Ignore)]   public Dictionary<string, string> CategoryTagStyles { get; set; }
        [JsonProperty("templateMode",  NullValueHandling = NullValueHandling.Ignore)]      public string TemplateMode { get; set; }
        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]      public List<string> ManagedFields { get; set; }
        [JsonProperty("discipline",    NullValueHandling = NullValueHandling.Ignore)]      public string Discipline { get; set; }
        [JsonProperty("visualStyle",   NullValueHandling = NullValueHandling.Ignore)]      public string VisualStyle { get; set; }
        [JsonProperty("phaseFilter",   NullValueHandling = NullValueHandling.Ignore)]      public string PhaseFilter { get; set; }
        [JsonProperty("checksum",      NullValueHandling = NullValueHandling.Ignore)]      public string Checksum { get; set; }
    }

    internal sealed class StylePackAppearance
    {
        [JsonProperty("lineWeightScale",   NullValueHandling = NullValueHandling.Ignore)] public double? LineWeightScale { get; set; }
        [JsonProperty("textStyleName",     NullValueHandling = NullValueHandling.Ignore)] public string TextStyleName { get; set; }
        [JsonProperty("dimensionStyleName",NullValueHandling = NullValueHandling.Ignore)] public string DimensionStyleName { get; set; }
        [JsonProperty("hatchPalette",      NullValueHandling = NullValueHandling.Ignore)] public string HatchPalette { get; set; }
    }

    internal sealed class StylePackFilterRule
    {
        [JsonProperty("name")]         public string Name { get; set; }
        [JsonProperty("visible")]      public bool   Visible { get; set; } = true;
        [JsonProperty("halftone")]     public bool   Halftone { get; set; }
        [JsonProperty("projColor",    NullValueHandling = NullValueHandling.Ignore)] public string ProjColor { get; set; }
        [JsonProperty("projWeight",   NullValueHandling = NullValueHandling.Ignore)] public int?   ProjWeight { get; set; }
        [JsonProperty("cutColor",     NullValueHandling = NullValueHandling.Ignore)] public string CutColor { get; set; }
        [JsonProperty("cutWeight",    NullValueHandling = NullValueHandling.Ignore)] public int?   CutWeight { get; set; }
        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int?   Transparency { get; set; }
    }

    internal sealed class StylePackVgOverride
    {
        [JsonProperty("visible",      NullValueHandling = NullValueHandling.Ignore)] public bool?   Visible { get; set; }
        [JsonProperty("halftone",     NullValueHandling = NullValueHandling.Ignore)] public bool?   Halftone { get; set; }
        [JsonProperty("projColor",    NullValueHandling = NullValueHandling.Ignore)] public string  ProjColor { get; set; }
        [JsonProperty("projWeight",   NullValueHandling = NullValueHandling.Ignore)] public int?    ProjWeight { get; set; }
        [JsonProperty("cutColor",     NullValueHandling = NullValueHandling.Ignore)] public string  CutColor { get; set; }
        [JsonProperty("cutWeight",    NullValueHandling = NullValueHandling.Ignore)] public int?    CutWeight { get; set; }
        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }
    }

    internal sealed class StylePackRoutingRule
    {
        [JsonProperty("purpose",      NullValueHandling = NullValueHandling.Ignore)] public string Purpose { get; set; }
        [JsonProperty("discipline",   NullValueHandling = NullValueHandling.Ignore)] public string Discipline { get; set; }
        [JsonProperty("stylePackId",  NullValueHandling = NullValueHandling.Ignore)] public string StylePackId { get; set; }
    }

    #endregion

    #region ── Validation + change-tracking models ──

    public enum ImportSeverity { Error, Warning }

    public sealed class ImportValidationResult
    {
        public string Sheet { get; set; }
        public int Row { get; set; }
        public string Column { get; set; }
        public ImportSeverity Severity { get; set; }
        public string Message { get; set; }
        public override string ToString() =>
            $"[{Severity}] {Sheet}!R{Row}C{Column}: {Message}";
    }

    public sealed class ChangeRecord
    {
        public string EntityType { get; set; }
        public string Id { get; set; }
        public string Field { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public override string ToString() =>
            $"{EntityType}/{Id} · {Field}: {OldValue ?? "<null>"} → {NewValue ?? "<null>"}";
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  DrawingTypeExcelEngine — workbook I/O + validation + change tracking
    // ════════════════════════════════════════════════════════════════════════════

    internal static class DrawingTypeExcelEngine
    {
        // ── Enum dropdown lists ──
        internal static readonly string[] PurposeOptions = {
            "Plan","RCP","Section","Elevation","Detail","Schedule","Spool","Coordination","Legend","3D"
        };
        internal static readonly string[] PaperSizeOptions  = { "A0","A1","A2","A3","A4" };
        internal static readonly string[] OrientationOptions= { "Landscape","Portrait" };
        internal static readonly string[] DetailLevelOptions= { "Coarse","Medium","Fine" };
        internal static readonly string[] CropModeOptions   = { "ScopeBox","ScopeBoxOrBbox","TightBbox","RoomBoundary","None" };
        internal static readonly string[] PrintColorOptions = { "Monochrome","PresentationRich","PresentationMono","ClarificationRed" };
        internal static readonly string[] DimStrategyOptions= { "Linear","Ordinate","Chain","None" };
        internal static readonly string[] TemplateModeOpts  = { "managed","external" };
        internal static readonly string[] ViewTypeOptions   = { "Plan","Section","Elevation","3D","ISO","Schedule","Legend","RCP","Detail" };
        internal static readonly string[] BoolOptions       = { "TRUE","FALSE" };

        // ── Discipline reference colours (Task 2 / ISO 13567 + CIBSE) ──
        internal static readonly (string Name, string Hex)[] DisciplineColours = {
            ("Architecture new",       "#000000"),
            ("Architecture existing",  "#808080"),
            ("Structural concrete",    "#C00000"),
            ("Structural steel",       "#0070C0"),
            ("HVAC / ductwork",        "#00B0F0"),
            ("Mechanical pipework",    "#00B050"),
            ("Electrical HV/LV",       "#FFC000"),
            ("Plumbing / sanitary",    "#00FFFF"),
            ("Fire protection",        "#FF0000"),
            ("Low-voltage / data",     "#7030A0"),
            ("Gas",                    "#FFFF00"),
            ("Civil / drainage",       "#C08000"),
            ("Site / topography",      "#008000"),
            ("Annotation / dims",      "#000000"),
        };

        internal static readonly Regex HexRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        // ── Brand styling for sheets ──
        private static readonly XLColor HdrFill   = XLColor.FromHtml("#2F3542");
        private static readonly XLColor RowAltFill= XLColor.FromHtml("#F5F5F5");
        private static readonly XLColor LockedFill= XLColor.FromHtml("#EEEEEE");

        // ──────────────────────────────────────────────────────────────────
        //  ExportWorkbook — build workbook in memory and return stream
        // ──────────────────────────────────────────────────────────────────

        public static MemoryStream ExportWorkbook(DrawingTypeLibrary dtLib, StylePackDoc packLib)
        {
            if (dtLib == null) throw new ArgumentNullException(nameof(dtLib));
            packLib ??= new StylePackDoc();

            var wb = new XLWorkbook();
            try
            {
                wb.Style.Font.FontName = "Calibri";
                wb.Style.Font.FontSize = 10;

                BuildDrawingTypesSheet(wb, dtLib, packLib);
                BuildStylePacksSheet(wb, packLib);
                BuildVgOverridesSheet(wb, packLib);
                BuildFilterRulesSheet(wb, packLib);
                BuildSlotsSheet(wb, dtLib);
                BuildTitleBlockParamsSheet(wb, dtLib);
                BuildRoutingSheet(wb, dtLib);
                BuildLegendSheet(wb);

                var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Position = 0;
                return ms;
            }
            finally
            {
                wb.Dispose();
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Sheet builders
        // ──────────────────────────────────────────────────────────────────

        private static void BuildDrawingTypesSheet(XLWorkbook wb, DrawingTypeLibrary dt, StylePackDoc packLib)
        {
            var ws = wb.AddWorksheet("DrawingTypes");
            string[] headers = {
                "id","name","description","origin","purpose","discipline","phase",
                "paperSize","orientation","scale","detailLevel","viewStylePackId",
                "viewTemplateName","viewportTypeName","sheetNumberPattern","sheetNamePattern",
                "cropMode","cropMarginMm","printColourScheme","printLineWeightScale",
                "printHalftoneLinks","annotationDimensionStrategy","annotationDimensionStyle",
                "annotationDenseUntilScale","checksum"
            };
            WriteHeader(ws, headers);

            int row = 2;
            var packIds = (packLib.StylePacks ?? new()).Select(p => p.Id).Where(s => !string.IsNullOrEmpty(s)).ToList();

            foreach (var d in dt.DrawingTypes ?? new())
            {
                ws.Cell(row, 1).Value  = d.Id ?? "";
                ws.Cell(row, 2).Value  = d.Name ?? "";
                ws.Cell(row, 3).Value  = d.Description ?? "";
                ws.Cell(row, 4).Value  = d.Origin ?? "corporate";
                ws.Cell(row, 5).Value  = d.Purpose ?? "";
                ws.Cell(row, 6).Value  = d.Discipline ?? "";
                ws.Cell(row, 7).Value  = d.Phase ?? "";
                ws.Cell(row, 8).Value  = d.PaperSize ?? "";
                ws.Cell(row, 9).Value  = d.Orientation ?? "";
                ws.Cell(row,10).Value  = d.Scale;
                ws.Cell(row,11).Value  = d.DetailLevel ?? "";
                ws.Cell(row,12).Value  = d.ViewStylePackId ?? "";
                ws.Cell(row,13).Value  = d.ViewTemplateName ?? "";
                ws.Cell(row,14).Value  = d.ViewportTypeName ?? "";
                ws.Cell(row,15).Value  = d.SheetNumberPattern ?? "";
                ws.Cell(row,16).Value  = d.SheetNamePattern ?? "";
                ws.Cell(row,17).Value  = d.Crop?.Kind ?? "";
                if (d.Crop != null) ws.Cell(row,18).Value = d.Crop.MarginMm;
                ws.Cell(row,19).Value  = d.Print?.ColourScheme ?? "";
                if (d.Print?.LineWeightScale.HasValue == true) ws.Cell(row,20).Value = d.Print.LineWeightScale.Value;
                ws.Cell(row,21).Value  = (d.Print?.HalftoneLinks ?? false) ? "TRUE" : "FALSE";
                ws.Cell(row,22).Value  = d.Annotation?.DimensionStrategy ?? "";
                ws.Cell(row,23).Value  = d.Annotation?.DimensionStyle ?? "";
                if (d.Annotation?.DenseUntilScale.HasValue == true) ws.Cell(row,24).Value = d.Annotation.DenseUntilScale.Value;
                ws.Cell(row,25).Value  = d.Checksum ?? "";

                if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                row++;
            }

            // Lock id / origin / checksum columns
            LockColumn(ws, 1, row);
            LockColumn(ws, 4, row);
            LockColumn(ws, 25, row);

            // Dropdowns
            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "E2:E" + last, PurposeOptions);
            AddListValidation(ws, "H2:H" + last, PaperSizeOptions);
            AddListValidation(ws, "I2:I" + last, OrientationOptions);
            AddListValidation(ws, "K2:K" + last, DetailLevelOptions);
            if (packIds.Count > 0) AddListValidation(ws, "L2:L" + last, packIds.ToArray());
            AddListValidation(ws, "Q2:Q" + last, CropModeOptions);
            AddListValidation(ws, "S2:S" + last, PrintColorOptions);
            AddListValidation(ws, "U2:U" + last, BoolOptions);
            AddListValidation(ws, "V2:V" + last, DimStrategyOptions);

            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildStylePacksSheet(XLWorkbook wb, StylePackDoc packs)
        {
            var ws = wb.AddWorksheet("StylePacks");
            string[] headers = {
                "id","name","description","origin","extends","lineWeightScale",
                "textStyle","dimensionStyle","hatchPalette","tagColorScheme",
                "defaultTagStyle","templateMode","discipline","visualStyle","phaseFilter","checksum"
            };
            WriteHeader(ws, headers);

            int row = 2;
            foreach (var p in packs.StylePacks ?? new())
            {
                ws.Cell(row, 1).Value = p.Id ?? "";
                ws.Cell(row, 2).Value = p.Name ?? "";
                ws.Cell(row, 3).Value = p.Description ?? "";
                ws.Cell(row, 4).Value = p.Origin ?? "corporate";
                ws.Cell(row, 5).Value = p.Extends ?? "";
                if (p.Appearance?.LineWeightScale.HasValue == true) ws.Cell(row, 6).Value = p.Appearance.LineWeightScale.Value;
                ws.Cell(row, 7).Value = p.Appearance?.TextStyleName ?? "";
                ws.Cell(row, 8).Value = p.Appearance?.DimensionStyleName ?? "";
                ws.Cell(row, 9).Value = p.Appearance?.HatchPalette ?? "";
                ws.Cell(row,10).Value = p.TagColorScheme ?? "";
                ws.Cell(row,11).Value = p.DefaultTagStyle ?? "";
                ws.Cell(row,12).Value = p.TemplateMode ?? "external";
                ws.Cell(row,13).Value = p.Discipline ?? "";
                ws.Cell(row,14).Value = p.VisualStyle ?? "";
                ws.Cell(row,15).Value = p.PhaseFilter ?? "";
                ws.Cell(row,16).Value = p.Checksum ?? "";

                if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                row++;
            }

            LockColumn(ws, 1, row);
            LockColumn(ws, 4, row);
            LockColumn(ws, 16, row);

            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "L2:L" + last, TemplateModeOpts);

            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildVgOverridesSheet(XLWorkbook wb, StylePackDoc packs)
        {
            var ws = wb.AddWorksheet("VgOverrides");
            string[] headers = {
                "packId","category","projectionLineColor","projectionLineWeight",
                "cutLineColor","cutLineWeight","halftone","transparency"
            };
            WriteHeader(ws, headers);

            int row = 2;
            foreach (var p in packs.StylePacks ?? new())
            {
                if (p.VgOverrides == null) continue;
                foreach (var kv in p.VgOverrides)
                {
                    ws.Cell(row, 1).Value = p.Id ?? "";
                    ws.Cell(row, 2).Value = kv.Key ?? "";
                    ws.Cell(row, 3).Value = kv.Value?.ProjColor ?? "";
                    if (kv.Value?.ProjWeight.HasValue == true) ws.Cell(row, 4).Value = kv.Value.ProjWeight.Value;
                    ws.Cell(row, 5).Value = kv.Value?.CutColor ?? "";
                    if (kv.Value?.CutWeight.HasValue == true) ws.Cell(row, 6).Value = kv.Value.CutWeight.Value;
                    ws.Cell(row, 7).Value = kv.Value?.Halftone.HasValue == true
                                           ? (kv.Value.Halftone.Value ? "TRUE" : "FALSE") : "";
                    if (kv.Value?.Transparency.HasValue == true) ws.Cell(row, 8).Value = kv.Value.Transparency.Value;

                    ApplyHexSwatch(ws.Cell(row, 3));
                    ApplyHexSwatch(ws.Cell(row, 5));

                    if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                    row++;
                }
            }

            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "G2:G" + last, BoolOptions);

            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildFilterRulesSheet(XLWorkbook wb, StylePackDoc packs)
        {
            var ws = wb.AddWorksheet("FilterRules");
            string[] headers = {
                "packId","filterName","visible","halftone",
                "projectionLineColor","projectionLineWeight",
                "cutLineColor","cutLineWeight","transparency"
            };
            WriteHeader(ws, headers);

            int row = 2;
            foreach (var p in packs.StylePacks ?? new())
            {
                if (p.FilterRules == null) continue;
                foreach (var f in p.FilterRules)
                {
                    ws.Cell(row, 1).Value = p.Id ?? "";
                    ws.Cell(row, 2).Value = f.Name ?? "";
                    ws.Cell(row, 3).Value = f.Visible ? "TRUE" : "FALSE";
                    ws.Cell(row, 4).Value = f.Halftone ? "TRUE" : "FALSE";
                    ws.Cell(row, 5).Value = f.ProjColor ?? "";
                    if (f.ProjWeight.HasValue) ws.Cell(row, 6).Value = f.ProjWeight.Value;
                    ws.Cell(row, 7).Value = f.CutColor ?? "";
                    if (f.CutWeight.HasValue)  ws.Cell(row, 8).Value = f.CutWeight.Value;
                    if (f.Transparency.HasValue) ws.Cell(row, 9).Value = f.Transparency.Value;

                    ApplyHexSwatch(ws.Cell(row, 5));
                    ApplyHexSwatch(ws.Cell(row, 7));

                    if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                    row++;
                }
            }

            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "C2:C" + last, BoolOptions);
            AddListValidation(ws, "D2:D" + last, BoolOptions);

            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildSlotsSheet(XLWorkbook wb, DrawingTypeLibrary dt)
        {
            var ws = wb.AddWorksheet("Slots");
            string[] headers = {
                "drawingTypeId","label","viewType","normX","normY","normW","normH",
                "scale","detailLevel","viewTemplate","viewportType","required"
            };
            WriteHeader(ws, headers);

            int row = 2;
            foreach (var d in dt.DrawingTypes ?? new())
            {
                if (d.Slots == null) continue;
                foreach (var s in d.Slots)
                {
                    ws.Cell(row, 1).Value = d.Id ?? "";
                    ws.Cell(row, 2).Value = s.Label ?? "";
                    ws.Cell(row, 3).Value = s.ViewType ?? "";
                    ws.Cell(row, 4).Value = s.NormX;
                    ws.Cell(row, 5).Value = s.NormY;
                    ws.Cell(row, 6).Value = s.NormW;
                    ws.Cell(row, 7).Value = s.NormH;
                    if (s.Scale.HasValue) ws.Cell(row, 8).Value = s.Scale.Value;
                    ws.Cell(row, 9).Value = s.DetailLevel ?? "";
                    ws.Cell(row,10).Value = s.ViewTemplate ?? "";
                    ws.Cell(row,11).Value = s.ViewportType ?? "";
                    ws.Cell(row,12).Value = s.Required ? "TRUE" : "FALSE";

                    if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                    row++;
                }
            }

            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "C2:C" + last, ViewTypeOptions);
            AddListValidation(ws, "I2:I" + last, DetailLevelOptions);
            AddListValidation(ws, "L2:L" + last, BoolOptions);

            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildTitleBlockParamsSheet(XLWorkbook wb, DrawingTypeLibrary dt)
        {
            var ws = wb.AddWorksheet("TitleBlockParams");
            string[] headers = { "drawingTypeId","paramName","valueTemplate" };
            WriteHeader(ws, headers);

            int row = 2;
            foreach (var d in dt.DrawingTypes ?? new())
            {
                if (d.TitleBlockParams == null) continue;
                foreach (var kv in d.TitleBlockParams)
                {
                    ws.Cell(row, 1).Value = d.Id ?? "";
                    ws.Cell(row, 2).Value = kv.Key ?? "";
                    ws.Cell(row, 3).Value = kv.Value ?? "";
                    if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                    row++;
                }
            }
            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildRoutingSheet(XLWorkbook wb, DrawingTypeLibrary dt)
        {
            var ws = wb.AddWorksheet("Routing");
            string[] headers = {
                "ruleIndex","discipline","phase","docType","levelMatches",
                "projectCodeMatches","drawingTypeId"
            };
            WriteHeader(ws, headers);

            int row = 2; int idx = 0;
            foreach (var r in dt.Routing ?? new())
            {
                ws.Cell(row, 1).Value = idx++;
                ws.Cell(row, 2).Value = r.Discipline ?? "";
                ws.Cell(row, 3).Value = r.Phase ?? "";
                ws.Cell(row, 4).Value = r.DocType ?? "";
                ws.Cell(row, 5).Value = r.LevelMatches ?? "";
                ws.Cell(row, 6).Value = r.ProjectCodeMatches ?? "";
                ws.Cell(row, 7).Value = r.DrawingTypeId ?? "";
                if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                row++;
            }
            FinaliseSheet(ws, headers.Length);
        }

        private static void BuildLegendSheet(XLWorkbook wb)
        {
            var ws = wb.AddWorksheet("_Legend");
            ws.Visibility = XLWorksheetVisibility.Hidden;

            ws.Cell(1, 1).Value = "Discipline";
            ws.Cell(1, 2).Value = "Hex";
            ws.Cell(1, 3).Value = "Swatch";
            HeaderStyle(ws.Range(1, 1, 1, 3));
            int row = 2;
            foreach (var (name, hex) in DisciplineColours)
            {
                ws.Cell(row, 1).Value = name;
                ws.Cell(row, 2).Value = hex;
                ws.Cell(row, 3).Value = "  ";
                ApplyHexSwatch(ws.Cell(row, 2));
                ApplyHexSwatch(ws.Cell(row, 3));
                row++;
            }

            row += 2;
            ws.Cell(row, 1).Value = "Line weight (Revit int → mm)";
            HeaderStyle(ws.Range(row, 1, row, 2));
            row++;
            (int lw, double mm)[] lws = { (1,0.05),(2,0.10),(3,0.13),(4,0.18),(5,0.25),(6,0.35),(7,0.50),(8,0.70),(9,1.00),(10,1.40) };
            foreach (var (lw, mm) in lws) { ws.Cell(row, 1).Value = lw; ws.Cell(row, 2).Value = mm + " mm"; row++; }

            row += 2;
            ws.Cell(row, 1).Value = "Enum reference";
            HeaderStyle(ws.Range(row, 1, row, 2));
            row++;
            void Pair(string label, string[] values) { ws.Cell(row, 1).Value = label; ws.Cell(row, 2).Value = string.Join(" | ", values); row++; }
            Pair("purpose",            PurposeOptions);
            Pair("paperSize",          PaperSizeOptions);
            Pair("orientation",        OrientationOptions);
            Pair("detailLevel",        DetailLevelOptions);
            Pair("cropMode",           CropModeOptions);
            Pair("printColourScheme",  PrintColorOptions);
            Pair("dimensionStrategy",  DimStrategyOptions);
            Pair("templateMode",       TemplateModeOpts);
            Pair("viewType",           ViewTypeOptions);
            ws.Columns(1, 3).AdjustToContents();
        }

        // ──────────────────────────────────────────────────────────────────
        //  Workbook-style helpers
        // ──────────────────────────────────────────────────────────────────

        private static void WriteHeader(IXLWorksheet ws, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            HeaderStyle(ws.Range(1, 1, 1, headers.Length));
        }

        private static void HeaderStyle(IXLRange range)
        {
            range.Style.Fill.BackgroundColor = HdrFill;
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        private static void FinaliseSheet(IXLWorksheet ws, int colCount)
        {
            ws.SheetView.FreezeRows(1);
            ws.Columns(1, colCount).AdjustToContents();
        }

        private static void LockColumn(IXLWorksheet ws, int col, int lastRow)
        {
            int last = Math.Max(lastRow - 1, 2);
            for (int r = 2; r <= last; r++)
                ws.Cell(r, col).Style.Fill.BackgroundColor = LockedFill;
        }

        private static void AddListValidation(IXLWorksheet ws, string range, string[] values)
        {
            try
            {
                var v = ws.Range(range).CreateDataValidation();
                v.List(string.Join(",", values));
                v.IgnoreBlanks = true;
            }
            catch (Exception ex) { StingLog.Warn($"DrawingTypeExcel: list validation failed for {range}: {ex.Message}"); }
        }

        private static void ApplyHexSwatch(IXLCell cell)
        {
            try
            {
                var v = cell.GetString();
                if (string.IsNullOrWhiteSpace(v)) return;
                if (!HexRegex.IsMatch(v)) return;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(v);
                // Darken text on near-white fills, lighten on dark fills, so the hex stays legible.
                cell.Style.Font.FontColor = IsLightHex(v) ? XLColor.Black : XLColor.White;
            }
            catch (Exception ex) { StingLog.Warn($"DrawingTypeExcel: swatch fill failed for {cell.Address}: {ex.Message}"); }
        }

        private static bool IsLightHex(string hex)
        {
            try
            {
                int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return (0.2126 * r + 0.7152 * g + 0.0722 * b) > 160;
            }
            catch { return true; }
        }

        // ──────────────────────────────────────────────────────────────────
        //  ValidateImport — verify workbook integrity before any mutation
        // ──────────────────────────────────────────────────────────────────

        public static List<ImportValidationResult> ValidateImport(
            XLWorkbook wb, DrawingTypeLibrary existingDtLib, StylePackDoc existingPackLib)
        {
            var results = new List<ImportValidationResult>();
            if (wb == null) { results.Add(new ImportValidationResult { Sheet = "?", Severity = ImportSeverity.Error, Message = "Workbook is null." }); return results; }

            var dtIds   = ReadIdSet(wb, "DrawingTypes", "id");
            var packIds = ReadIdSet(wb, "StylePacks",   "id");

            // 1+8 — duplicate id detection
            CheckDuplicateIds(wb, "DrawingTypes", "id", results);
            CheckDuplicateIds(wb, "StylePacks",   "id", results);

            // 4 — extends cycle / orphan detection in StylePacks
            CheckExtendsCycles(wb, results);

            // DrawingTypes-row enum + numeric checks
            ValidateDrawingTypesRows(wb, packIds, results);

            // StylePacks-row enum checks
            ValidateStylePacksRows(wb, results);

            // VgOverrides + FilterRules — packId orphan + colour + numeric
            ValidateVgOverridesRows(wb, packIds, results);
            ValidateFilterRulesRows(wb, packIds, results);

            // Slots — drawingTypeId orphan + viewType + numeric ranges
            ValidateSlotsRows(wb, dtIds, results);

            // TitleBlockParams — drawingTypeId orphan
            ValidateTitleBlockRows(wb, dtIds, results);

            // Routing — drawingTypeId orphan
            ValidateRoutingRows(wb, dtIds, results);

            return results;
        }

        private static HashSet<string> ReadIdSet(XLWorkbook wb, string sheetName, string idColName)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ws = wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (ws == null) return ids;
            int idCol = FindHeader(ws, idColName);
            if (idCol < 1) return ids;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= last; r++)
            {
                var v = ws.Cell(r, idCol).GetString().Trim();
                if (!string.IsNullOrEmpty(v)) ids.Add(v);
            }
            return ids;
        }

        private static int FindHeader(IXLWorksheet ws, string headerName)
        {
            int last = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
            for (int c = 1; c <= last; c++)
                if (string.Equals(ws.Cell(1, c).GetString().Trim(), headerName, StringComparison.OrdinalIgnoreCase))
                    return c;
            return -1;
        }

        private static void CheckDuplicateIds(XLWorkbook wb, string sheetName, string idColName, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (ws == null) return;
            int idCol = FindHeader(ws, idColName);
            if (idCol < 1) return;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= last; r++)
            {
                var v = ws.Cell(r, idCol).GetString().Trim();
                if (string.IsNullOrEmpty(v)) continue;
                if (seen.TryGetValue(v, out var prev))
                    results.Add(new ImportValidationResult { Sheet = sheetName, Row = r, Column = idColName, Severity = ImportSeverity.Error,
                        Message = $"Duplicate id '{v}' (also row {prev})." });
                else
                    seen[v] = r;
            }
        }

        private static void CheckExtendsCycles(XLWorkbook wb, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "StylePacks");
            if (ws == null) return;
            int idCol = FindHeader(ws, "id");
            int extCol = FindHeader(ws, "extends");
            if (idCol < 1 || extCol < 1) return;
            var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= last; r++)
            {
                var id  = ws.Cell(r, idCol).GetString().Trim();
                var ext = ws.Cell(r, extCol).GetString().Trim();
                if (!string.IsNullOrEmpty(id)) parent[id] = ext;
            }
            // orphan check
            foreach (var kv in parent)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!parent.ContainsKey(kv.Value))
                    results.Add(new ImportValidationResult { Sheet = "StylePacks", Row = 0, Column = "extends", Severity = ImportSeverity.Error,
                        Message = $"'{kv.Key}' extends '{kv.Value}' which is not present." });
            }
            // cycle detection (DFS)
            foreach (var start in parent.Keys)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cur = start;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (!seen.Add(cur))
                    {
                        results.Add(new ImportValidationResult { Sheet = "StylePacks", Row = 0, Column = "extends", Severity = ImportSeverity.Error,
                            Message = $"Cycle in extends chain at '{cur}' (started from '{start}')." });
                        break;
                    }
                    parent.TryGetValue(cur, out cur);
                }
            }
        }

        private static void ValidateDrawingTypesRows(XLWorkbook wb, HashSet<string> packIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "DrawingTypes");
            if (ws == null) { results.Add(new ImportValidationResult { Sheet = "DrawingTypes", Severity = ImportSeverity.Error, Message = "Sheet missing." }); return; }
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cPurpose   = FindHeader(ws, "purpose");
            int cPaper     = FindHeader(ws, "paperSize");
            int cOrient    = FindHeader(ws, "orientation");
            int cDetail    = FindHeader(ws, "detailLevel");
            int cCrop      = FindHeader(ws, "cropMode");
            int cPrint     = FindHeader(ws, "printColourScheme");
            int cDim       = FindHeader(ws, "annotationDimensionStrategy");
            int cHalftone  = FindHeader(ws, "printHalftoneLinks");
            int cPackId    = FindHeader(ws, "viewStylePackId");
            int cScale     = FindHeader(ws, "scale");
            int cMargin    = FindHeader(ws, "cropMarginMm");
            int cLws       = FindHeader(ws, "printLineWeightScale");

            for (int r = 2; r <= last; r++)
            {
                EnumCheck(ws, r, cPurpose,  "purpose",            PurposeOptions,    results);
                EnumCheck(ws, r, cPaper,    "paperSize",          PaperSizeOptions,  results);
                EnumCheck(ws, r, cOrient,   "orientation",        OrientationOptions,results);
                EnumCheck(ws, r, cDetail,   "detailLevel",        DetailLevelOptions,results);
                EnumCheck(ws, r, cCrop,     "cropMode",           CropModeOptions,   results);
                EnumCheck(ws, r, cPrint,    "printColourScheme",  PrintColorOptions, results);
                EnumCheck(ws, r, cDim,      "annotationDimensionStrategy", DimStrategyOptions, results);
                EnumCheck(ws, r, cHalftone, "printHalftoneLinks", BoolOptions,       results);

                NumberRange(ws, r, cScale,  "scale",         results, 1, 50000);
                NumberRange(ws, r, cMargin, "cropMarginMm",  results, 0, 100000);
                NumberRange(ws, r, cLws,    "printLineWeightScale", results, 0.0, 5.0);

                // viewStylePackId reference check
                if (cPackId > 0)
                {
                    var pid = ws.Cell(r, cPackId).GetString().Trim();
                    if (!string.IsNullOrEmpty(pid) && !packIds.Contains(pid))
                        results.Add(new ImportValidationResult { Sheet = "DrawingTypes", Row = r, Column = "viewStylePackId",
                            Severity = ImportSeverity.Error, Message = $"viewStylePackId '{pid}' not in StylePacks sheet." });
                }
            }
        }

        private static void ValidateStylePacksRows(XLWorkbook wb, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "StylePacks");
            if (ws == null) { results.Add(new ImportValidationResult { Sheet = "StylePacks", Severity = ImportSeverity.Error, Message = "Sheet missing." }); return; }
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cMode = FindHeader(ws, "templateMode");
            int cLws  = FindHeader(ws, "lineWeightScale");
            for (int r = 2; r <= last; r++)
            {
                EnumCheck(ws, r, cMode, "templateMode", TemplateModeOpts, results);
                NumberRange(ws, r, cLws, "lineWeightScale", results, 0.0, 5.0);
            }
        }

        private static void ValidateVgOverridesRows(XLWorkbook wb, HashSet<string> packIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "VgOverrides");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cPack = FindHeader(ws, "packId");
            int cPC   = FindHeader(ws, "projectionLineColor");
            int cCC   = FindHeader(ws, "cutLineColor");
            int cPW   = FindHeader(ws, "projectionLineWeight");
            int cCW   = FindHeader(ws, "cutLineWeight");
            int cHalf = FindHeader(ws, "halftone");
            int cTr   = FindHeader(ws, "transparency");

            for (int r = 2; r <= last; r++)
            {
                if (cPack > 0)
                {
                    var pid = ws.Cell(r, cPack).GetString().Trim();
                    if (!string.IsNullOrEmpty(pid) && !packIds.Contains(pid))
                        results.Add(new ImportValidationResult { Sheet = "VgOverrides", Row = r, Column = "packId",
                            Severity = ImportSeverity.Error, Message = $"packId '{pid}' not in StylePacks sheet." });
                }
                HexCheck(ws, r, cPC, "projectionLineColor", results);
                HexCheck(ws, r, cCC, "cutLineColor",        results);
                NumberRange(ws, r, cPW, "projectionLineWeight", results, 1, 16);
                NumberRange(ws, r, cCW, "cutLineWeight",        results, 1, 16);
                NumberRange(ws, r, cTr, "transparency",         results, 0, 100);
                EnumCheck(ws, r, cHalf, "halftone", BoolOptions, results);
            }
        }

        private static void ValidateFilterRulesRows(XLWorkbook wb, HashSet<string> packIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "FilterRules");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cPack = FindHeader(ws, "packId");
            int cVis  = FindHeader(ws, "visible");
            int cHalf = FindHeader(ws, "halftone");
            int cPC   = FindHeader(ws, "projectionLineColor");
            int cCC   = FindHeader(ws, "cutLineColor");
            int cPW   = FindHeader(ws, "projectionLineWeight");
            int cCW   = FindHeader(ws, "cutLineWeight");
            int cTr   = FindHeader(ws, "transparency");

            for (int r = 2; r <= last; r++)
            {
                if (cPack > 0)
                {
                    var pid = ws.Cell(r, cPack).GetString().Trim();
                    if (!string.IsNullOrEmpty(pid) && !packIds.Contains(pid))
                        results.Add(new ImportValidationResult { Sheet = "FilterRules", Row = r, Column = "packId",
                            Severity = ImportSeverity.Error, Message = $"packId '{pid}' not in StylePacks sheet." });
                }
                EnumCheck(ws, r, cVis,  "visible",  BoolOptions, results);
                EnumCheck(ws, r, cHalf, "halftone", BoolOptions, results);
                HexCheck(ws, r, cPC, "projectionLineColor", results);
                HexCheck(ws, r, cCC, "cutLineColor",        results);
                NumberRange(ws, r, cPW, "projectionLineWeight", results, 1, 16);
                NumberRange(ws, r, cCW, "cutLineWeight",        results, 1, 16);
                NumberRange(ws, r, cTr, "transparency",         results, 0, 100);
            }
        }

        private static void ValidateSlotsRows(XLWorkbook wb, HashSet<string> dtIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Slots");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cDt   = FindHeader(ws, "drawingTypeId");
            int cView = FindHeader(ws, "viewType");
            int cReq  = FindHeader(ws, "required");
            int cDl   = FindHeader(ws, "detailLevel");
            int cSc   = FindHeader(ws, "scale");
            int[] xywh = { FindHeader(ws, "normX"), FindHeader(ws, "normY"), FindHeader(ws, "normW"), FindHeader(ws, "normH") };
            string[] xywhNames = { "normX", "normY", "normW", "normH" };

            for (int r = 2; r <= last; r++)
            {
                if (cDt > 0)
                {
                    var did = ws.Cell(r, cDt).GetString().Trim();
                    if (!string.IsNullOrEmpty(did) && !dtIds.Contains(did))
                        results.Add(new ImportValidationResult { Sheet = "Slots", Row = r, Column = "drawingTypeId",
                            Severity = ImportSeverity.Error, Message = $"drawingTypeId '{did}' not in DrawingTypes sheet." });
                }
                EnumCheck(ws, r, cView, "viewType", ViewTypeOptions, results);
                EnumCheck(ws, r, cReq,  "required", BoolOptions,     results);
                EnumCheck(ws, r, cDl,   "detailLevel", DetailLevelOptions, results);
                NumberRange(ws, r, cSc, "scale", results, 1, 50000);
                for (int i = 0; i < 4; i++) NumberRange(ws, r, xywh[i], xywhNames[i], results, 0.0, 1.0);
            }
        }

        private static void ValidateTitleBlockRows(XLWorkbook wb, HashSet<string> dtIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "TitleBlockParams");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cDt = FindHeader(ws, "drawingTypeId");
            for (int r = 2; r <= last; r++)
            {
                if (cDt < 1) continue;
                var did = ws.Cell(r, cDt).GetString().Trim();
                if (!string.IsNullOrEmpty(did) && !dtIds.Contains(did))
                    results.Add(new ImportValidationResult { Sheet = "TitleBlockParams", Row = r, Column = "drawingTypeId",
                        Severity = ImportSeverity.Error, Message = $"drawingTypeId '{did}' not in DrawingTypes sheet." });
            }
        }

        private static void ValidateRoutingRows(XLWorkbook wb, HashSet<string> dtIds, List<ImportValidationResult> results)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Routing");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cDt = FindHeader(ws, "drawingTypeId");
            for (int r = 2; r <= last; r++)
            {
                if (cDt < 1) continue;
                var did = ws.Cell(r, cDt).GetString().Trim();
                if (string.IsNullOrEmpty(did))
                    results.Add(new ImportValidationResult { Sheet = "Routing", Row = r, Column = "drawingTypeId",
                        Severity = ImportSeverity.Warning, Message = "Routing rule has no drawingTypeId." });
                else if (!dtIds.Contains(did))
                    results.Add(new ImportValidationResult { Sheet = "Routing", Row = r, Column = "drawingTypeId",
                        Severity = ImportSeverity.Error, Message = $"drawingTypeId '{did}' not in DrawingTypes sheet." });
            }
        }

        private static void EnumCheck(IXLWorksheet ws, int r, int col, string name, string[] valid, List<ImportValidationResult> results)
        {
            if (col < 1) return;
            var v = ws.Cell(r, col).GetString().Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (!valid.Any(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase)))
                results.Add(new ImportValidationResult { Sheet = ws.Name, Row = r, Column = name, Severity = ImportSeverity.Error,
                    Message = $"'{v}' is not a valid {name} value (allowed: {string.Join(", ", valid)})." });
        }

        private static void NumberRange(IXLWorksheet ws, int r, int col, string name,
            List<ImportValidationResult> results, double min, double max)
        {
            if (col < 1) return;
            var v = ws.Cell(r, col).GetString().Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (!double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                results.Add(new ImportValidationResult { Sheet = ws.Name, Row = r, Column = name, Severity = ImportSeverity.Error,
                    Message = $"'{v}' is not a valid number for {name}." });
                return;
            }
            if (d < min || d > max)
                results.Add(new ImportValidationResult { Sheet = ws.Name, Row = r, Column = name, Severity = ImportSeverity.Error,
                    Message = $"{name} = {d} is out of range [{min},{max}]." });
        }

        private static void HexCheck(IXLWorksheet ws, int r, int col, string name, List<ImportValidationResult> results)
        {
            if (col < 1) return;
            var v = ws.Cell(r, col).GetString().Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (!HexRegex.IsMatch(v))
                results.Add(new ImportValidationResult { Sheet = ws.Name, Row = r, Column = name, Severity = ImportSeverity.Error,
                    Message = $"'{v}' is not a valid #RRGGBB hex colour for {name}." });
        }

        // ──────────────────────────────────────────────────────────────────
        //  ImportWorkbook — apply edits in-memory and emit ChangeRecords
        // ──────────────────────────────────────────────────────────────────

        public sealed class ImportResult
        {
            public DrawingTypeLibrary UpdatedDtLib { get; set; }
            public StylePackDoc UpdatedPackLib { get; set; }
            public List<ChangeRecord> Changes { get; } = new();
        }

        public static ImportResult ImportWorkbook(XLWorkbook wb,
            DrawingTypeLibrary existingDtLib, StylePackDoc existingPackLib)
        {
            var problems = ValidateImport(wb, existingDtLib, existingPackLib);
            if (problems.Any(p => p.Severity == ImportSeverity.Error))
                throw new InvalidOperationException(
                    "Import blocked by validation errors. Resolve the errors and re-import.");

            var dt = CloneDt(existingDtLib);
            var packs = ClonePackDoc(existingPackLib);
            var changes = new List<ChangeRecord>();

            ApplyDrawingTypesSheet(wb, dt, changes);
            ApplyStylePacksSheet(wb, packs, changes);
            ApplyVgOverridesSheet(wb, packs, changes);
            ApplyFilterRulesSheet(wb, packs, changes);
            ApplySlotsSheet(wb, dt, changes);
            ApplyTitleBlockSheet(wb, dt, changes);
            ApplyRoutingSheet(wb, dt, changes);

            // Recompute checksums where corporate entries have drifted —
            // mirrors DrawingTypeRegistry behaviour: any modification to a
            // corporate row flips its origin to "project" so the corporate
            // baseline file stays pristine on disk.
            FlipModifiedCorporateOriginDt(dt, changes);
            FlipModifiedCorporateOriginPacks(packs, changes);

            return new ImportResult { UpdatedDtLib = dt, UpdatedPackLib = packs, Changes = changes };
        }

        private static DrawingTypeLibrary CloneDt(DrawingTypeLibrary src)
        {
            if (src == null) return new DrawingTypeLibrary();
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<DrawingTypeLibrary>(json) ?? new DrawingTypeLibrary();
        }

        private static StylePackDoc ClonePackDoc(StylePackDoc src)
        {
            if (src == null) return new StylePackDoc();
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<StylePackDoc>(json) ?? new StylePackDoc();
        }

        private static DrawingType GetOrAddDt(DrawingTypeLibrary lib, string id)
        {
            var existing = lib.DrawingTypes.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var fresh = new DrawingType { Id = id, Origin = "project" };
            lib.DrawingTypes.Add(fresh);
            return fresh;
        }

        private static StylePackEntry GetOrAddPack(StylePackDoc lib, string id)
        {
            var existing = lib.StylePacks.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var fresh = new StylePackEntry { Id = id, Origin = "project" };
            lib.StylePacks.Add(fresh);
            return fresh;
        }

        private static void Set<T>(string entityType, string id, string field,
            T oldVal, T newVal, Action<T> setter, List<ChangeRecord> changes)
        {
            var oldStr = oldVal == null ? null : oldVal.ToString();
            var newStr = newVal == null ? null : newVal.ToString();
            if (string.Equals(oldStr, newStr, StringComparison.Ordinal)) return;
            setter(newVal);
            changes.Add(new ChangeRecord { EntityType = entityType, Id = id, Field = field, OldValue = oldStr, NewValue = newStr });
        }

        private static void ApplyDrawingTypesSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "DrawingTypes");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= last; r++)
            {
                var id = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(id)) continue;
                var d = GetOrAddDt(dt, id);

                Set("DrawingType", id, "name",        d.Name,        ws.Cell(r,  2).GetString(), v => d.Name = v, changes);
                Set("DrawingType", id, "description", d.Description, ws.Cell(r,  3).GetString(), v => d.Description = v, changes);
                // origin column (4) deliberately not written — locked
                Set("DrawingType", id, "purpose",     d.Purpose,     ws.Cell(r,  5).GetString(), v => d.Purpose = v, changes);
                Set("DrawingType", id, "discipline",  d.Discipline,  ws.Cell(r,  6).GetString(), v => d.Discipline = v, changes);
                Set("DrawingType", id, "phase",       d.Phase,       ws.Cell(r,  7).GetString(), v => d.Phase = v, changes);
                Set("DrawingType", id, "paperSize",   d.PaperSize,   ws.Cell(r,  8).GetString(), v => d.PaperSize = v, changes);
                Set("DrawingType", id, "orientation", d.Orientation, ws.Cell(r,  9).GetString(), v => d.Orientation = v, changes);
                if (int.TryParse(ws.Cell(r,10).GetString(), out var sc))
                    Set("DrawingType", id, "scale",   d.Scale,       sc, v => d.Scale = v, changes);
                Set("DrawingType", id, "detailLevel",      d.DetailLevel,      ws.Cell(r,11).GetString(), v => d.DetailLevel = v, changes);
                Set("DrawingType", id, "viewStylePackId",  d.ViewStylePackId,  ws.Cell(r,12).GetString(), v => d.ViewStylePackId = v, changes);
                Set("DrawingType", id, "viewTemplateName", d.ViewTemplateName, ws.Cell(r,13).GetString(), v => d.ViewTemplateName = v, changes);
                Set("DrawingType", id, "viewportTypeName", d.ViewportTypeName, ws.Cell(r,14).GetString(), v => d.ViewportTypeName = v, changes);
                Set("DrawingType", id, "sheetNumberPattern", d.SheetNumberPattern, ws.Cell(r,15).GetString(), v => d.SheetNumberPattern = v, changes);
                Set("DrawingType", id, "sheetNamePattern",   d.SheetNamePattern,   ws.Cell(r,16).GetString(), v => d.SheetNamePattern = v, changes);

                d.Crop ??= new DrawingCropStrategy();
                Set("DrawingType", id, "cropMode", d.Crop.Kind, ws.Cell(r,17).GetString(), v => d.Crop.Kind = v, changes);
                if (double.TryParse(ws.Cell(r,18).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mm))
                    Set("DrawingType", id, "cropMarginMm", d.Crop.MarginMm, mm, v => d.Crop.MarginMm = v, changes);

                d.Print ??= new PrintOverride();
                Set("DrawingType", id, "printColourScheme", d.Print.ColourScheme, ws.Cell(r,19).GetString(), v => d.Print.ColourScheme = v, changes);
                if (double.TryParse(ws.Cell(r,20).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lws))
                    Set("DrawingType", id, "printLineWeightScale", d.Print.LineWeightScale, (double?)lws, v => d.Print.LineWeightScale = v, changes);
                bool halftone = string.Equals(ws.Cell(r,21).GetString().Trim(), "TRUE", StringComparison.OrdinalIgnoreCase);
                Set("DrawingType", id, "printHalftoneLinks", d.Print.HalftoneLinks, halftone, v => d.Print.HalftoneLinks = v, changes);

                d.Annotation ??= new AnnotationRulePack();
                Set("DrawingType", id, "annotationDimensionStrategy", d.Annotation.DimensionStrategy, ws.Cell(r,22).GetString(), v => d.Annotation.DimensionStrategy = v, changes);
                Set("DrawingType", id, "annotationDimensionStyle",    d.Annotation.DimensionStyle,    ws.Cell(r,23).GetString(), v => d.Annotation.DimensionStyle = v, changes);
                if (int.TryParse(ws.Cell(r,24).GetString(), out var dus))
                    Set("DrawingType", id, "annotationDenseUntilScale", d.Annotation.DenseUntilScale, (int?)dus, v => d.Annotation.DenseUntilScale = v, changes);

                // checksum column (25) not written — locked
            }
        }

        private static void ApplyStylePacksSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "StylePacks");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= last; r++)
            {
                var id = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(id)) continue;
                var p = GetOrAddPack(packs, id);

                Set("StylePack", id, "name",        p.Name,        ws.Cell(r, 2).GetString(), v => p.Name = v, changes);
                Set("StylePack", id, "description", p.Description, ws.Cell(r, 3).GetString(), v => p.Description = v, changes);
                // origin column (4) locked
                Set("StylePack", id, "extends",     p.Extends,     ws.Cell(r, 5).GetString(), v => p.Extends = v, changes);

                p.Appearance ??= new StylePackAppearance();
                if (double.TryParse(ws.Cell(r, 6).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lws))
                    Set("StylePack", id, "lineWeightScale", p.Appearance.LineWeightScale, (double?)lws, v => p.Appearance.LineWeightScale = v, changes);
                Set("StylePack", id, "textStyleName",      p.Appearance.TextStyleName,      ws.Cell(r, 7).GetString(), v => p.Appearance.TextStyleName = v, changes);
                Set("StylePack", id, "dimensionStyleName", p.Appearance.DimensionStyleName, ws.Cell(r, 8).GetString(), v => p.Appearance.DimensionStyleName = v, changes);
                Set("StylePack", id, "hatchPalette",       p.Appearance.HatchPalette,       ws.Cell(r, 9).GetString(), v => p.Appearance.HatchPalette = v, changes);

                Set("StylePack", id, "tagColorScheme",  p.TagColorScheme,  ws.Cell(r,10).GetString(), v => p.TagColorScheme = v, changes);
                Set("StylePack", id, "defaultTagStyle", p.DefaultTagStyle, ws.Cell(r,11).GetString(), v => p.DefaultTagStyle = v, changes);
                Set("StylePack", id, "templateMode",    p.TemplateMode,    ws.Cell(r,12).GetString(), v => p.TemplateMode = v, changes);
                Set("StylePack", id, "discipline",      p.Discipline,      ws.Cell(r,13).GetString(), v => p.Discipline = v, changes);
                Set("StylePack", id, "visualStyle",     p.VisualStyle,     ws.Cell(r,14).GetString(), v => p.VisualStyle = v, changes);
                Set("StylePack", id, "phaseFilter",     p.PhaseFilter,     ws.Cell(r,15).GetString(), v => p.PhaseFilter = v, changes);
                // checksum (16) locked
            }
        }

        private static void ApplyVgOverridesSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "VgOverrides");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Group rows by packId (full replace per pack)
            var byPack = new Dictionary<string, Dictionary<string, StylePackVgOverride>>(StringComparer.OrdinalIgnoreCase);
            for (int r = 2; r <= last; r++)
            {
                var pid = ws.Cell(r, 1).GetString().Trim();
                var cat = ws.Cell(r, 2).GetString().Trim();
                if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(cat)) continue;

                if (!byPack.TryGetValue(pid, out var dict))
                {
                    dict = new Dictionary<string, StylePackVgOverride>(StringComparer.OrdinalIgnoreCase);
                    byPack[pid] = dict;
                }
                var ov = new StylePackVgOverride();
                var pc = ws.Cell(r, 3).GetString().Trim(); if (!string.IsNullOrEmpty(pc)) ov.ProjColor = pc;
                if (int.TryParse(ws.Cell(r, 4).GetString(), out var pw)) ov.ProjWeight = pw;
                var cc = ws.Cell(r, 5).GetString().Trim(); if (!string.IsNullOrEmpty(cc)) ov.CutColor  = cc;
                if (int.TryParse(ws.Cell(r, 6).GetString(), out var cw)) ov.CutWeight  = cw;
                var ht = ws.Cell(r, 7).GetString().Trim();
                if (!string.IsNullOrEmpty(ht)) ov.Halftone = string.Equals(ht, "TRUE", StringComparison.OrdinalIgnoreCase);
                if (int.TryParse(ws.Cell(r, 8).GetString(), out var tr)) ov.Transparency = tr;

                dict[cat] = ov;
            }

            foreach (var kv in byPack)
            {
                var pack = GetOrAddPack(packs, kv.Key);
                int oldCount = pack.VgOverrides?.Count ?? 0;
                pack.VgOverrides = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "StylePack", Id = kv.Key, Field = "vgOverrides",
                    OldValue = $"{oldCount} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplyFilterRulesSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "FilterRules");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            var byPack = new Dictionary<string, List<StylePackFilterRule>>(StringComparer.OrdinalIgnoreCase);
            for (int r = 2; r <= last; r++)
            {
                var pid  = ws.Cell(r, 1).GetString().Trim();
                var name = ws.Cell(r, 2).GetString().Trim();
                if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(name)) continue;
                if (!byPack.TryGetValue(pid, out var list)) { list = new(); byPack[pid] = list; }
                var f = new StylePackFilterRule {
                    Name     = name,
                    Visible  = string.Equals(ws.Cell(r, 3).GetString().Trim(), "TRUE", StringComparison.OrdinalIgnoreCase),
                    Halftone = string.Equals(ws.Cell(r, 4).GetString().Trim(), "TRUE", StringComparison.OrdinalIgnoreCase),
                };
                var pc = ws.Cell(r, 5).GetString().Trim(); if (!string.IsNullOrEmpty(pc)) f.ProjColor = pc;
                if (int.TryParse(ws.Cell(r, 6).GetString(), out var pw)) f.ProjWeight = pw;
                var cc = ws.Cell(r, 7).GetString().Trim(); if (!string.IsNullOrEmpty(cc)) f.CutColor  = cc;
                if (int.TryParse(ws.Cell(r, 8).GetString(), out var cw)) f.CutWeight  = cw;
                if (int.TryParse(ws.Cell(r, 9).GetString(), out var tr)) f.Transparency = tr;
                list.Add(f);
            }

            foreach (var kv in byPack)
            {
                var pack = GetOrAddPack(packs, kv.Key);
                int oldCount = pack.FilterRules?.Count ?? 0;
                pack.FilterRules = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "StylePack", Id = kv.Key, Field = "filterRules",
                    OldValue = $"{oldCount} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplySlotsSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Slots");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            var byDt = new Dictionary<string, List<DrawingSlot>>(StringComparer.OrdinalIgnoreCase);
            for (int r = 2; r <= last; r++)
            {
                var did = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(did)) continue;
                if (!byDt.TryGetValue(did, out var list)) { list = new(); byDt[did] = list; }

                var s = new DrawingSlot {
                    Label    = ws.Cell(r, 2).GetString(),
                    ViewType = ws.Cell(r, 3).GetString(),
                    Required = string.Equals(ws.Cell(r,12).GetString().Trim(), "TRUE", StringComparison.OrdinalIgnoreCase),
                };
                if (double.TryParse(ws.Cell(r, 4).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) s.NormX = x;
                if (double.TryParse(ws.Cell(r, 5).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) s.NormY = y;
                if (double.TryParse(ws.Cell(r, 6).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)) s.NormW = w;
                if (double.TryParse(ws.Cell(r, 7).GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)) s.NormH = h;
                if (int.TryParse(ws.Cell(r, 8).GetString(), out var sc)) s.Scale = sc;
                var dl = ws.Cell(r, 9).GetString().Trim(); if (!string.IsNullOrEmpty(dl)) s.DetailLevel = dl;
                var vt = ws.Cell(r,10).GetString().Trim(); if (!string.IsNullOrEmpty(vt)) s.ViewTemplate = vt;
                var vp = ws.Cell(r,11).GetString().Trim(); if (!string.IsNullOrEmpty(vp)) s.ViewportType = vp;
                list.Add(s);
            }

            foreach (var kv in byDt)
            {
                var d = GetOrAddDt(dt, kv.Key);
                int oldCount = d.Slots?.Count ?? 0;
                d.Slots = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "DrawingType", Id = kv.Key, Field = "slots",
                    OldValue = $"{oldCount} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplyTitleBlockSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "TitleBlockParams");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            var byDt = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            for (int r = 2; r <= last; r++)
            {
                var did = ws.Cell(r, 1).GetString().Trim();
                var key = ws.Cell(r, 2).GetString().Trim();
                if (string.IsNullOrEmpty(did) || string.IsNullOrEmpty(key)) continue;
                if (!byDt.TryGetValue(did, out var map)) { map = new(StringComparer.OrdinalIgnoreCase); byDt[did] = map; }
                map[key] = ws.Cell(r, 3).GetString();
            }

            foreach (var kv in byDt)
            {
                var d = GetOrAddDt(dt, kv.Key);
                int oldCount = d.TitleBlockParams?.Count ?? 0;
                d.TitleBlockParams = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "DrawingType", Id = kv.Key, Field = "titleBlockParams",
                    OldValue = $"{oldCount} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplyRoutingSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Routing");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            var rules = new List<DrawingRoutingRule>();
            for (int r = 2; r <= last; r++)
            {
                var dt_id = ws.Cell(r, 7).GetString().Trim();
                if (string.IsNullOrEmpty(dt_id)) continue;
                var rule = new DrawingRoutingRule {
                    Discipline         = ws.Cell(r, 2).GetString(),
                    Phase              = ws.Cell(r, 3).GetString(),
                    DocType            = ws.Cell(r, 4).GetString(),
                    LevelMatches       = NullIfEmpty(ws.Cell(r, 5).GetString()),
                    ProjectCodeMatches = NullIfEmpty(ws.Cell(r, 6).GetString()),
                    DrawingTypeId      = dt_id,
                };
                rules.Add(rule);
            }

            int oldCount = dt.Routing?.Count ?? 0;
            dt.Routing = rules;
            changes.Add(new ChangeRecord { EntityType = "Routing", Id = "*", Field = "rules",
                OldValue = $"{oldCount} rules", NewValue = $"{rules.Count} rules" });
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static void FlipModifiedCorporateOriginDt(DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var modifiedIds = new HashSet<string>(
                changes.Where(c => c.EntityType == "DrawingType").Select(c => c.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var d in dt.DrawingTypes ?? new())
            {
                if (string.Equals(d.Origin, "corporate", StringComparison.OrdinalIgnoreCase)
                    && modifiedIds.Contains(d.Id))
                {
                    d.Origin = "project";
                    d.Checksum = null;
                    changes.Add(new ChangeRecord { EntityType = "DrawingType", Id = d.Id, Field = "origin",
                        OldValue = "corporate", NewValue = "project" });
                }
            }
        }

        private static void FlipModifiedCorporateOriginPacks(StylePackDoc packs, List<ChangeRecord> changes)
        {
            var modifiedIds = new HashSet<string>(
                changes.Where(c => c.EntityType == "StylePack").Select(c => c.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var p in packs.StylePacks ?? new())
            {
                if (string.Equals(p.Origin, "corporate", StringComparison.OrdinalIgnoreCase)
                    && modifiedIds.Contains(p.Id))
                {
                    p.Origin = "project";
                    p.Checksum = null;
                    changes.Add(new ChangeRecord { EntityType = "StylePack", Id = p.Id, Field = "origin",
                        OldValue = "corporate", NewValue = "project" });
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  ApplyImport — write JSON files + invalidate runtime cache
        // ──────────────────────────────────────────────────────────────────

        public static void ApplyImport(Document doc,
            DrawingTypeLibrary updatedDtLib, StylePackDoc updatedPacks, string outputDir)
        {
            if (string.IsNullOrEmpty(outputDir))
                throw new ArgumentException("outputDir must not be empty.", nameof(outputDir));
            Directory.CreateDirectory(outputDir);

            // Project override files only contain entries whose origin is "project".
            // Corporate baselines on disk stay untouched — drift on a corporate row
            // flips its origin to "project" first (FlipModifiedCorporateOrigin*).
            var projectDt = new DrawingTypeLibrary {
                Version      = updatedDtLib?.Version ?? 1,
                DrawingTypes = (updatedDtLib?.DrawingTypes ?? new())
                                .Where(d => string.Equals(d.Origin, "project", StringComparison.OrdinalIgnoreCase))
                                .ToList(),
                Routing      = updatedDtLib?.Routing ?? new(),
            };
            var projectPacks = new StylePackDoc {
                SchemaVersion = updatedPacks?.SchemaVersion,
                Name          = updatedPacks?.Name,
                Description   = updatedPacks?.Description,
                Namespace     = updatedPacks?.Namespace,
                LastUpdated   = DateTime.Now.ToString("yyyy-MM-dd"),
                StylePacks    = (updatedPacks?.StylePacks ?? new())
                                .Where(p => string.Equals(p.Origin, "project", StringComparison.OrdinalIgnoreCase))
                                .ToList(),
                Routing       = updatedPacks?.Routing,
            };

            var dtPath   = Path.Combine(outputDir, "drawing_types.json");
            var packPath = Path.Combine(outputDir, "view_style_packs.json");
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore };
            File.WriteAllText(dtPath,   JsonConvert.SerializeObject(projectDt,    settings));
            File.WriteAllText(packPath, JsonConvert.SerializeObject(projectPacks, settings));
            StingLog.Info($"DrawingTypeExcel: wrote {dtPath}");
            StingLog.Info($"DrawingTypeExcel: wrote {packPath}");

            try { DrawingTypeRegistry.Reload(doc); }       catch (Exception ex) { StingLog.Warn($"DrawingTypeRegistry.Reload failed: {ex.Message}"); }
            try { ViewStylePackRegistry.Reload(doc); }     catch (Exception ex) { StingLog.Warn($"ViewStylePackRegistry.Reload failed: {ex.Message}"); }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers used by both export + import
        // ──────────────────────────────────────────────────────────────────

        public static StylePackDoc LoadStylePackDocFromCorporate()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_VIEW_STYLE_PACKS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new StylePackDoc();
                return JsonConvert.DeserializeObject<StylePackDoc>(File.ReadAllText(path)) ?? new StylePackDoc();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypeExcel: corporate style pack load failed — {ex.Message}");
                return new StylePackDoc();
            }
        }

        public static StylePackDoc LoadStylePackDocFromProject(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                var path = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", "view_style_packs.json");
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<StylePackDoc>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypeExcel: project style pack load failed — {ex.Message}");
                return null;
            }
        }

        public static StylePackDoc MergeStylePacks(StylePackDoc baseDoc, StylePackDoc over)
        {
            if (over == null) return baseDoc ?? new StylePackDoc();
            var merged = new StylePackDoc {
                SchemaVersion = over.SchemaVersion ?? baseDoc?.SchemaVersion,
                Name          = over.Name          ?? baseDoc?.Name,
                Description   = over.Description   ?? baseDoc?.Description,
                Namespace     = over.Namespace     ?? baseDoc?.Namespace,
                LastUpdated   = over.LastUpdated   ?? baseDoc?.LastUpdated,
                StylePacks    = new List<StylePackEntry>(baseDoc?.StylePacks ?? new()),
                Routing       = over.Routing ?? baseDoc?.Routing,
            };
            var byId = merged.StylePacks.ToDictionary(p => p.Id ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var p in over.StylePacks ?? new())
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                byId[p.Id] = p;
            }
            merged.StylePacks = byId.Values.ToList();
            return merged;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  DrawingTypeExportExcelCommand — export everything to a .xlsx workbook
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypeExportExcelCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.SafeApp(), ref message);

        public Result Execute(UIApplication app) { string m = ""; return Run(app, ref m); }

        private static Result Run(UIApplication app, ref string message)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                var dtLib = DrawingTypeRegistry.GetLibrary(doc) ?? new DrawingTypeLibrary();

                var corpPacks = DrawingTypeExcelEngine.LoadStylePackDocFromCorporate();
                var projPacks = DrawingTypeExcelEngine.LoadStylePackDocFromProject(doc);
                var packLib   = DrawingTypeExcelEngine.MergeStylePacks(corpPacks, projPacks);

                using var stream = DrawingTypeExcelEngine.ExportWorkbook(dtLib, packLib);
                var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = OutputLocationHelper.GetOutputPath(doc, $"DrawingTypes_Export_{ts}.xlsx");
                File.WriteAllBytes(path, stream.ToArray());

                StingLog.Info($"DrawingTypeExcel: exported {dtLib.DrawingTypes?.Count ?? 0} drawing types and {packLib.StylePacks?.Count ?? 0} style packs to {path}");

                var dlg = new TaskDialog("STING — Drawing Types Excel Export")
                {
                    MainInstruction = "Export complete",
                    MainContent =
                        $"Exported {dtLib.DrawingTypes?.Count ?? 0} drawing types and " +
                        $"{packLib.StylePacks?.Count ?? 0} style packs.\n\n" +
                        $"File: {path}\n\n" +
                        "Sheets: DrawingTypes · StylePacks · VgOverrides · FilterRules · Slots · " +
                        "TitleBlockParams · Routing · _Legend (hidden)\n\n" +
                        "Locked columns (grey fill): id, origin, checksum.\n" +
                        "Colour columns render the hex value as a cell fill swatch.",
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open in Excel");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open file location");
                dlg.CommonButtons = TaskDialogCommonButtons.Close;
                var r = dlg.Show();
                if (r == TaskDialogResult.CommandLink1) TryOpen(path);
                else if (r == TaskDialogResult.CommandLink2) TryOpen(Path.GetDirectoryName(path));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeExportExcel failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING — Drawing Types Excel Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static void TryOpen(string path)
        {
            try { if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { StingLog.Warn($"DrawingTypeExcel: open failed: {ex.Message}"); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  DrawingTypeImportExcelCommand — import edited .xlsx with validation
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypeImportExcelCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.SafeApp(), ref message);

        public Result Execute(UIApplication app) { string m = ""; return Run(app, ref m); }

        private static Result Run(UIApplication app, ref string message)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title       = "Import Drawing Types from Excel",
                    Filter      = "Excel workbook (*.xlsx)|*.xlsx",
                    InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc),
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                var path = dlg.FileName;

                using var wb = new XLWorkbook(path);

                var corpPacks = DrawingTypeExcelEngine.LoadStylePackDocFromCorporate();
                var projPacks = DrawingTypeExcelEngine.LoadStylePackDocFromProject(doc);
                var existingPacks = DrawingTypeExcelEngine.MergeStylePacks(corpPacks, projPacks);
                var existingDt = DrawingTypeRegistry.GetLibrary(doc) ?? new DrawingTypeLibrary();

                var problems = DrawingTypeExcelEngine.ValidateImport(wb, existingDt, existingPacks);
                int errCount  = problems.Count(p => p.Severity == ImportSeverity.Error);
                int warnCount = problems.Count(p => p.Severity == ImportSeverity.Warning);

                if (errCount > 0)
                {
                    var b = StingResultPanel.Create("Drawing Types Import — Validation Errors")
                        .SetSubtitle($"{errCount} error(s) and {warnCount} warning(s) — import blocked.")
                        .AddSection("Errors");
                    foreach (var p in problems.Where(x => x.Severity == ImportSeverity.Error).Take(200))
                        b.Text(p.ToString());
                    if (warnCount > 0)
                    {
                        b.AddSection("Warnings");
                        foreach (var p in problems.Where(x => x.Severity == ImportSeverity.Warning).Take(200))
                            b.Text(p.ToString());
                    }
                    b.Show();
                    return Result.Cancelled;
                }

                if (warnCount > 0)
                {
                    var lines = string.Join("\n", problems.Where(p => p.Severity == ImportSeverity.Warning).Take(20).Select(p => "• " + p.Message));
                    var td = new TaskDialog("STING — Import Warnings")
                    {
                        MainInstruction = $"{warnCount} warning(s)",
                        MainContent = lines + (warnCount > 20 ? $"\n…and {warnCount - 20} more." : "") + "\n\nProceed anyway?",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.Yes,
                    };
                    if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, existingDt, existingPacks);

                var summary = StingResultPanel.Create("Drawing Types Import — Change Summary")
                    .SetSubtitle($"{imp.Changes.Count} change(s) detected. Confirm to write to _BIM_COORD/.");
                foreach (var grp in imp.Changes.GroupBy(c => c.EntityType).OrderBy(g => g.Key))
                {
                    summary.AddSection($"{grp.Key} ({grp.Count()})");
                    foreach (var c in grp.Take(80)) summary.Text(c.ToString());
                    if (grp.Count() > 80) summary.Text($"… and {grp.Count() - 80} more.");
                }
                summary.Show();

                var confirm = new TaskDialog("STING — Apply Import")
                {
                    MainInstruction = "Apply changes?",
                    MainContent = $"Write {imp.Changes.Count} change(s) to _BIM_COORD/drawing_types.json and view_style_packs.json?\n\n" +
                                  "Corporate baseline files on disk are not touched. Modified corporate entries flip to project origin.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                var outDir = ResolveProjectOverrideDir(doc);
                using (var tg = new Transaction(doc, "STING Import Drawing Types"))
                {
                    tg.Start();
                    DrawingTypeExcelEngine.ApplyImport(doc, imp.UpdatedDtLib, imp.UpdatedPackLib, outDir);
                    tg.Commit();
                }

                StingLog.Info($"DrawingTypeExcel: applied {imp.Changes.Count} changes from {path}");

                TaskDialog.Show("STING — Import Complete",
                    $"Wrote project overrides to:\n{outDir}\n\nApplied {imp.Changes.Count} change(s). Registry caches refreshed.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeImportExcel failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING — Drawing Types Excel Import", $"Import failed:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static string ResolveProjectOverrideDir(Document doc)
        {
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
            {
                var d = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD");
                Directory.CreateDirectory(d);
                return d;
            }
            // Headless / detached fallback — write to standard exports directory.
            return OutputLocationHelper.GetOutputDirectory(doc);
        }
    }
}
