// StingTools — Drawing Template Manager · Excel Round-Trip (Revit-free half)
//
// The workbook model, export, validation and change tracking of the Drawing
// Type / View Style Pack Excel round-trip. No Revit API: the commands, the
// project-override writer and the file loaders live in
// DrawingTypeExcelCommands.cs (the other half of this partial class). Split
// so StingTools.Tags.Tests can export the SHIPPED catalogue to a workbook,
// validate it and import it back — the round-trip that silently failed
// before DTW-179..183.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.BIMManager
{
    #region ── File-format POCOs (mirror STING_VIEW_STYLE_PACKS.json) ──

    // The runtime ViewStylePackLibrary class uses different JSON keys
    // than the on-disk file (drift documented in CLAUDE.md). The Excel
    // round-trip needs to preserve the actual file shape, so it uses
    // these POCOs that match the file 1:1. Mirrors the editor-side
    // model in DrawingTypeEditorDialog.cs.
    //
    // DTW-181: every class carries [JsonExtensionData], so a key this mirror
    // does not model (pack tagFamilies / viewRange / farClipMm / managedFields
    // …, filter-rule surfFgColor / projLinePattern, a VG override's
    // long-form keys) survives the import model instead of being dropped.
    internal sealed class StylePackDoc
    {
        [JsonProperty("schemaVersion", NullValueHandling = NullValueHandling.Ignore)] public string SchemaVersion { get; set; }
        [JsonProperty("name",          NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        [JsonProperty("description",   NullValueHandling = NullValueHandling.Ignore)] public string Description { get; set; }
        [JsonProperty("namespace",     NullValueHandling = NullValueHandling.Ignore)] public string Namespace { get; set; }
        [JsonProperty("lastUpdated",   NullValueHandling = NullValueHandling.Ignore)] public string LastUpdated { get; set; }
        [JsonProperty("stylePacks")] public List<StylePackEntry> StylePacks { get; set; } = new();
        [JsonProperty("routing", NullValueHandling = NullValueHandling.Ignore)] public List<StylePackRoutingRule> Routing { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
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

        // DTW-181: corp-coordination and coord-qa key their rules under the
        // runtime's canonical "filters". This mirror bound only "filterRules",
        // so those 19 rules vanished on import. Write-only alias; canonical
        // output stays "filterRules", which the runtime also reads.
        [JsonProperty("filters", NullValueHandling = NullValueHandling.Ignore)]
        public List<StylePackFilterRule> FiltersAlias
        {
            get => null;
            set { if (value != null && value.Count > 0) FilterRules = value; }
        }

        [JsonProperty("vgOverrides",   NullValueHandling = NullValueHandling.Ignore)]      public Dictionary<string, StylePackVgOverride> VgOverrides { get; set; }
        [JsonProperty("tagColorScheme",   NullValueHandling = NullValueHandling.Ignore)]   public string TagColorScheme { get; set; }
        [JsonProperty("defaultTagStyle",  NullValueHandling = NullValueHandling.Ignore)]   public string DefaultTagStyle { get; set; }
        [JsonProperty("categoryTagStyles",NullValueHandling = NullValueHandling.Ignore)]   public Dictionary<string, string> CategoryTagStyles { get; set; }
        [JsonProperty("templateMode",  NullValueHandling = NullValueHandling.Ignore)]      public string TemplateMode { get; set; }
        [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]      public List<string> ManagedFields { get; set; }
        [JsonProperty("discipline",    NullValueHandling = NullValueHandling.Ignore)]      public string Discipline { get; set; }
        [JsonProperty("visualStyle",   NullValueHandling = NullValueHandling.Ignore)]      public string VisualStyle { get; set; }
        [JsonProperty("phaseFilter",   NullValueHandling = NullValueHandling.Ignore)]      public string PhaseFilter { get; set; }
        // No "checksum": view style packs are deliberately unlocked (DRAW-5).
        // The field was mirrored here from ViewStylePack, always empty, and
        // exported as a locked "checksum" column that implied a lock.

        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    internal sealed class StylePackAppearance
    {
        [JsonProperty("lineWeightScale",   NullValueHandling = NullValueHandling.Ignore)] public double? LineWeightScale { get; set; }
        [JsonProperty("textStyleName",     NullValueHandling = NullValueHandling.Ignore)] public string TextStyleName { get; set; }
        [JsonProperty("dimensionStyleName",NullValueHandling = NullValueHandling.Ignore)] public string DimensionStyleName { get; set; }
        [JsonProperty("hatchPalette",      NullValueHandling = NullValueHandling.Ignore)] public string HatchPalette { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    internal sealed class StylePackFilterRule
    {
        [JsonProperty("name")]         public string Name { get; set; }
        // DTW-181: nullable, as on the runtime StyleFilterRule (V-9). "Not
        // said" defers to the AEC filter registry default; as a bool with a
        // true default, an unset rule came back from the workbook as an
        // explicit visible:true and overrode that default.
        [JsonProperty("visible",      NullValueHandling = NullValueHandling.Ignore)] public bool?  Visible { get; set; }
        [JsonProperty("halftone",     NullValueHandling = NullValueHandling.Ignore)] public bool?  Halftone { get; set; }
        [JsonProperty("projColor",    NullValueHandling = NullValueHandling.Ignore)] public string ProjColor { get; set; }
        [JsonProperty("projWeight",   NullValueHandling = NullValueHandling.Ignore)] public int?   ProjWeight { get; set; }
        [JsonProperty("cutColor",     NullValueHandling = NullValueHandling.Ignore)] public string CutColor { get; set; }
        [JsonProperty("cutWeight",    NullValueHandling = NullValueHandling.Ignore)] public int?   CutWeight { get; set; }
        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int?   Transparency { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        /// <summary>The workbook-visible fields, for change detection.</summary>
        internal string Key => string.Join("|", Name, Visible, Halftone, ProjColor, ProjWeight, CutColor, CutWeight, Transparency);
    }

    /// <summary>
    /// One category's VG override. The shipped packs spell the line fields
    /// both ways — short projColor/projWeight/cutColor/cutWeight and long
    /// projectionLineColor/projectionLineWeight/cutLineColor/cutLineWeight
    /// (107/107/45/45 occurrences) — and the runtime StyleVgOverride reads
    /// both. DTW-181: this mirror modelled only the short form, so every
    /// long-form value exported blank and was then deleted on import. Both
    /// spellings are now modelled; the Effective* accessors read whichever is
    /// set, and the Set* writers keep the spelling the entry already uses.
    /// </summary>
    internal sealed class StylePackVgOverride
    {
        [JsonProperty("visible",      NullValueHandling = NullValueHandling.Ignore)] public bool?   Visible { get; set; }
        [JsonProperty("halftone",     NullValueHandling = NullValueHandling.Ignore)] public bool?   Halftone { get; set; }
        [JsonProperty("projColor",    NullValueHandling = NullValueHandling.Ignore)] public string  ProjColor { get; set; }
        [JsonProperty("projWeight",   NullValueHandling = NullValueHandling.Ignore)] public int?    ProjWeight { get; set; }
        [JsonProperty("cutColor",     NullValueHandling = NullValueHandling.Ignore)] public string  CutColor { get; set; }
        [JsonProperty("cutWeight",    NullValueHandling = NullValueHandling.Ignore)] public int?    CutWeight { get; set; }
        [JsonProperty("projectionLineColor",  NullValueHandling = NullValueHandling.Ignore)] public string ProjectionLineColor { get; set; }
        [JsonProperty("projectionLineWeight", NullValueHandling = NullValueHandling.Ignore)] public int?   ProjectionLineWeight { get; set; }
        [JsonProperty("cutLineColor",         NullValueHandling = NullValueHandling.Ignore)] public string CutLineColor { get; set; }
        [JsonProperty("cutLineWeight",        NullValueHandling = NullValueHandling.Ignore)] public int?   CutLineWeight { get; set; }
        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int?    Transparency { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        [JsonIgnore] internal string EffectiveProjColor  => !string.IsNullOrEmpty(ProjectionLineColor) ? ProjectionLineColor : ProjColor;
        [JsonIgnore] internal int?   EffectiveProjWeight => ProjectionLineWeight ?? ProjWeight;
        [JsonIgnore] internal string EffectiveCutColor   => !string.IsNullOrEmpty(CutLineColor) ? CutLineColor : CutColor;
        [JsonIgnore] internal int?   EffectiveCutWeight  => CutLineWeight ?? CutWeight;

        internal void SetProjColor(string v)  { if (!string.IsNullOrEmpty(ProjectionLineColor)) ProjectionLineColor = v; else ProjColor = v; }
        internal void SetProjWeight(int? v)   { if (ProjectionLineWeight.HasValue) ProjectionLineWeight = v; else ProjWeight = v; }
        internal void SetCutColor(string v)   { if (!string.IsNullOrEmpty(CutLineColor)) CutLineColor = v; else CutColor = v; }
        internal void SetCutWeight(int? v)    { if (CutLineWeight.HasValue) CutLineWeight = v; else CutWeight = v; }

        /// <summary>The workbook-visible values, for change detection.</summary>
        internal string Key => string.Join("|", EffectiveProjColor, EffectiveProjWeight, EffectiveCutColor,
                                           EffectiveCutWeight, Halftone, Transparency);
    }

    internal sealed class StylePackRoutingRule
    {
        [JsonProperty("purpose",      NullValueHandling = NullValueHandling.Ignore)] public string Purpose { get; set; }
        [JsonProperty("discipline",   NullValueHandling = NullValueHandling.Ignore)] public string Discipline { get; set; }
        [JsonProperty("stylePackId",  NullValueHandling = NullValueHandling.Ignore)] public string StylePackId { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
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

    internal static partial class DrawingTypeExcelEngine
    {
        // ── Enum dropdown lists ──
        // Derived, not listed: the hand-written copy lacked Schematic and
        // Clarification, so exporting the shipped catalogue and importing it
        // back failed validation on all ten of those rows.
        internal static readonly string[] PurposeOptions = DrawingPurpose.All;
        internal static readonly string[] PaperSizeOptions  = { "A0","A1","A2","A3","A4" };
        internal static readonly string[] OrientationOptions= { "Landscape","Portrait" };
        internal static readonly string[] DetailLevelOptions= { "Coarse","Medium","Fine" };
        internal static readonly string[] CropModeOptions   = { "ScopeBox","ScopeBoxOrBbox","TightBbox","RoomBoundary","None" };
        // DTW-179: ByDiscipline was missing, so the 17 shipped types that use it
        // failed import. Must equal DrawingTypeValidator.PrintColourSchemes
        // (Revit-bound, so held by a source-parity test rather than referenced).
        internal static readonly string[] PrintColorOptions = { "Monochrome","ByDiscipline","PresentationRich","PresentationMono","ClarificationRed" };
        internal static readonly string[] DimStrategyOptions= { "Linear","Ordinate","Chain","None" };
        internal static readonly string[] TemplateModeOpts  = { "managed","external" };
        // DTW-179: the runtime slot vocabulary itself — the hand-written copy
        // lacked Schematic / Coordination / Drafting (18 shipped slots).
        internal static readonly string[] ViewTypeOptions   = SlotViewTypeCompatibility.KnownSlotViewTypes;
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
                "defaultTagStyle","templateMode","discipline","visualStyle","phaseFilter"
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
                ws.Cell(row,12).Value = p.TemplateMode ?? "";   // DTW-181: blank, not an invented "external"
                ws.Cell(row,13).Value = p.Discipline ?? "";
                ws.Cell(row,14).Value = p.VisualStyle ?? "";
                ws.Cell(row,15).Value = p.PhaseFilter ?? "";

                if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                row++;
            }

            LockColumn(ws, 1, row);
            LockColumn(ws, 4, row);

            int last = Math.Max(row - 1, 2);
            AddListValidation(ws, "L2:L" + last, TemplateModeOpts);

            FinaliseSheet(ws, headers.Length);
        }

        private static string Bool(bool? b) => b.HasValue ? (b.Value ? "TRUE" : "FALSE") : "";

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
                    // DTW-181: whichever spelling the entry uses.
                    ws.Cell(row, 3).Value = kv.Value?.EffectiveProjColor ?? "";
                    if (kv.Value?.EffectiveProjWeight.HasValue == true) ws.Cell(row, 4).Value = kv.Value.EffectiveProjWeight.Value;
                    ws.Cell(row, 5).Value = kv.Value?.EffectiveCutColor ?? "";
                    if (kv.Value?.EffectiveCutWeight.HasValue == true) ws.Cell(row, 6).Value = kv.Value.EffectiveCutWeight.Value;
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
                    ws.Cell(row, 3).Value = Bool(f.Visible);   // blank = not said (DTW-181)
                    ws.Cell(row, 4).Value = Bool(f.Halftone);
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
                "scale","detailLevel","viewTemplate","viewportType","required",
                "purposeTag","slotRef"   // DTW-181: carried, not dropped
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
                    ws.Cell(row,13).Value = s.PurposeTag ?? "";
                    ws.Cell(row,14).Value = s.SlotRef ?? "";

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

        // DTW-180: every predicate the matcher reads, plus origin. The sheet
        // used to carry only discipline/phase/docType/levelMatches/
        // projectCodeMatches, so the 23 shipped rules written with
        // disciplineMatches / docTypeMatches / phaseMatches (and any
        // optionMatches rule) came back as "*" catch-alls.
        internal static readonly string[] RoutingHeaders = {
            "ruleIndex","origin","discipline","phase","docType",
            "disciplineMatches","phaseMatches","docTypeMatches",
            "levelMatches","projectCodeMatches","optionMatches","drawingTypeId"
        };

        private static void BuildRoutingSheet(XLWorkbook wb, DrawingTypeLibrary dt)
        {
            var ws = wb.AddWorksheet("Routing");
            var headers = RoutingHeaders;
            WriteHeader(ws, headers);

            int row = 2; int idx = 0;
            foreach (var r in dt.Routing ?? new())
            {
                if (r == null) continue;
                ws.Cell(row, 1).Value  = idx++;
                ws.Cell(row, 2).Value  = r.Origin ?? "corporate";
                ws.Cell(row, 3).Value  = r.Discipline ?? "";
                ws.Cell(row, 4).Value  = r.Phase ?? "";
                ws.Cell(row, 5).Value  = r.DocType ?? "";
                ws.Cell(row, 6).Value  = r.DisciplineMatches ?? "";
                ws.Cell(row, 7).Value  = r.PhaseMatches ?? "";
                ws.Cell(row, 8).Value  = r.DocTypeMatches ?? "";
                ws.Cell(row, 9).Value  = r.LevelMatches ?? "";
                ws.Cell(row,10).Value  = r.ProjectCodeMatches ?? "";
                ws.Cell(row,11).Value  = r.OptionMatches ?? "";
                ws.Cell(row,12).Value  = r.DrawingTypeId ?? "";
                if (row % 2 == 0) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = RowAltFill;
                row++;
            }
            LockColumn(ws, 2, row);
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; }
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

                // DTW-179: 0 is "not applicable" — the tolerant converter's
                // reading of "scale": "NA" on 31 shipped 3D / schematic types.
                NumberRange(ws, r, cScale,  "scale",         results, 0, 50000);
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

        /// <summary>
        /// DTW-182: read a numeric cell as a number. A number cell is read with
        /// GetDouble — no text round-trip, so no culture. A text cell is parsed
        /// invariant first ("0.25"), then in the current culture ("0,25" on a
        /// de-DE / fr-FR machine). The old path called GetString() on number
        /// cells, which formats in the CURRENT culture, then parsed invariant —
        /// so on a comma-decimal machine every decimal was rejected or ignored.
        /// </summary>
        internal static bool TryReadDouble(IXLCell cell, out double value)
        {
            value = 0;
            if (cell == null || cell.IsEmpty()) return false;
            if (cell.DataType == XLDataType.Number)
            {
                value = cell.GetDouble();
                return true;
            }
            var s = cell.GetString().Trim();
            if (string.IsNullOrEmpty(s)) return false;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>Integer cell (scale, line weight, transparency): a whole number only.</summary>
        internal static bool TryReadInt(IXLCell cell, out int value)
        {
            value = 0;
            if (!TryReadDouble(cell, out var d)) return false;
            if (Math.Abs(d - Math.Round(d)) > 1e-9 || d > int.MaxValue || d < int.MinValue) return false;
            value = (int)Math.Round(d);
            return true;
        }

        private static void NumberRange(IXLWorksheet ws, int r, int col, string name,
            List<ImportValidationResult> results, double min, double max)
        {
            if (col < 1) return;
            var cell = ws.Cell(r, col);
            var v = cell.GetString().Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (!TryReadDouble(cell, out var d))
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

            var result = new ImportResult { UpdatedDtLib = dt, UpdatedPackLib = packs };
            result.Changes.AddRange(changes);
            return result;
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

        // DTW-181 — change tracking. A change is recorded, and the model
        // touched, only when the workbook value DIFFERS from the model value:
        // null and "" are the same (an empty cell), numbers compare as
        // numbers, and a sub-object (crop / print / annotation / appearance)
        // is created only when a value actually has to be written into it.
        // The old Set treated null vs "" as a change and the Apply* methods
        // recorded slots / title-block params / VG / filters / routing as
        // changed unconditionally, so importing an unedited workbook flipped
        // every corporate type and pack to "project" (checksum dropped) and
        // froze the whole catalogue into the override.

        private static string Norm(string v) => string.IsNullOrEmpty(v) ? null : v;

        private static void SetStr(string entityType, string id, string field,
            string oldVal, string newVal, Action<string> setter, List<ChangeRecord> changes)
        {
            oldVal = Norm(oldVal); newVal = Norm(newVal);
            if (string.Equals(oldVal, newVal, StringComparison.Ordinal)) return;
            setter(newVal);
            changes.Add(new ChangeRecord { EntityType = entityType, Id = id, Field = field, OldValue = oldVal, NewValue = newVal });
        }

        private static void SetNum(string entityType, string id, string field,
            double? oldVal, double? newVal, Action<double?> setter, List<ChangeRecord> changes)
        {
            if (oldVal.HasValue == newVal.HasValue
                && (!oldVal.HasValue || Math.Abs(oldVal.Value - newVal.Value) <= 1e-9)) return;
            setter(newVal);
            changes.Add(new ChangeRecord { EntityType = entityType, Id = id, Field = field,
                OldValue = oldVal?.ToString(CultureInfo.InvariantCulture), NewValue = newVal?.ToString(CultureInfo.InvariantCulture) });
        }

        private static void SetBool(string entityType, string id, string field,
            bool oldVal, bool newVal, Action<bool> setter, List<ChangeRecord> changes)
        {
            if (oldVal == newVal) return;
            setter(newVal);
            changes.Add(new ChangeRecord { EntityType = entityType, Id = id, Field = field,
                OldValue = oldVal ? "TRUE" : "FALSE", NewValue = newVal ? "TRUE" : "FALSE" });
        }

        private static string Str(IXLWorksheet ws, int r, int c) => ws.Cell(r, c).GetString();
        private static double? Num(IXLWorksheet ws, int r, int c) => TryReadDouble(ws.Cell(r, c), out var d) ? d : (double?)null;
        private static int? Int(IXLWorksheet ws, int r, int c) => TryReadInt(ws.Cell(r, c), out var i) ? i : (int?)null;
        private static bool? TriBool(IXLWorksheet ws, int r, int c)
        {
            var v = ws.Cell(r, c).GetString().Trim();
            if (string.IsNullOrEmpty(v)) return null;
            return string.Equals(v, "TRUE", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyDrawingTypesSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "DrawingTypes");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            const string E = "DrawingType";

            for (int r = 2; r <= last; r++)
            {
                var id = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(id)) continue;
                var d = GetOrAddDt(dt, id);

                SetStr(E, id, "name",        d.Name,        Str(ws, r,  2), v => d.Name = v, changes);
                SetStr(E, id, "description", d.Description, Str(ws, r,  3), v => d.Description = v, changes);
                // origin column (4) deliberately not written — locked
                SetStr(E, id, "purpose",     d.Purpose,     Str(ws, r,  5), v => d.Purpose = v, changes);
                SetStr(E, id, "discipline",  d.Discipline,  Str(ws, r,  6), v => d.Discipline = v, changes);
                SetStr(E, id, "phase",       d.Phase,       Str(ws, r,  7), v => d.Phase = v, changes);
                SetStr(E, id, "paperSize",   d.PaperSize,   Str(ws, r,  8), v => d.PaperSize = v, changes);
                SetStr(E, id, "orientation", d.Orientation, Str(ws, r,  9), v => d.Orientation = v, changes);
                var sc = Int(ws, r, 10);
                if (sc.HasValue)
                    SetNum(E, id, "scale", d.Scale, sc, v => d.Scale = (int)v.Value, changes);
                SetStr(E, id, "detailLevel",        d.DetailLevel,        Str(ws, r, 11), v => d.DetailLevel = v, changes);
                SetStr(E, id, "viewStylePackId",    d.ViewStylePackId,    Str(ws, r, 12), v => d.ViewStylePackId = v, changes);
                SetStr(E, id, "viewTemplateName",   d.ViewTemplateName,   Str(ws, r, 13), v => d.ViewTemplateName = v, changes);
                SetStr(E, id, "viewportTypeName",   d.ViewportTypeName,   Str(ws, r, 14), v => d.ViewportTypeName = v, changes);
                SetStr(E, id, "sheetNumberPattern", d.SheetNumberPattern, Str(ws, r, 15), v => d.SheetNumberPattern = v, changes);
                SetStr(E, id, "sheetNamePattern",   d.SheetNamePattern,   Str(ws, r, 16), v => d.SheetNamePattern = v, changes);

                SetStr(E, id, "cropMode", d.Crop?.Kind, Str(ws, r, 17), v => (d.Crop ??= new DrawingCropStrategy()).Kind = v, changes);
                var mm = Num(ws, r, 18);
                if (mm.HasValue)
                    SetNum(E, id, "cropMarginMm", d.Crop?.MarginMm, mm, v => (d.Crop ??= new DrawingCropStrategy()).MarginMm = v.Value, changes);

                SetStr(E, id, "printColourScheme", d.Print?.ColourScheme, Str(ws, r, 19), v => (d.Print ??= new PrintOverride()).ColourScheme = v, changes);
                var lws = Num(ws, r, 20);
                if (lws.HasValue)
                    SetNum(E, id, "printLineWeightScale", d.Print?.LineWeightScale, lws, v => (d.Print ??= new PrintOverride()).LineWeightScale = v, changes);
                bool halftone = TriBool(ws, r, 21) ?? false;
                SetBool(E, id, "printHalftoneLinks", d.Print?.HalftoneLinks ?? false, halftone, v => (d.Print ??= new PrintOverride()).HalftoneLinks = v, changes);

                SetStr(E, id, "annotationDimensionStrategy", d.Annotation?.DimensionStrategy, Str(ws, r, 22), v => (d.Annotation ??= new AnnotationRulePack()).DimensionStrategy = v, changes);
                SetStr(E, id, "annotationDimensionStyle",    d.Annotation?.DimensionStyle,    Str(ws, r, 23), v => (d.Annotation ??= new AnnotationRulePack()).DimensionStyle = v, changes);
                var dus = Int(ws, r, 24);
                if (dus.HasValue)
                    SetNum(E, id, "annotationDenseUntilScale", d.Annotation?.DenseUntilScale, dus, v => (d.Annotation ??= new AnnotationRulePack()).DenseUntilScale = (int?)v, changes);

                // checksum column (25) not written — locked
            }
        }

        private static void ApplyStylePacksSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "StylePacks");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            const string E = "StylePack";
            for (int r = 2; r <= last; r++)
            {
                var id = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(id)) continue;
                var p = GetOrAddPack(packs, id);

                SetStr(E, id, "name",        p.Name,        Str(ws, r, 2), v => p.Name = v, changes);
                SetStr(E, id, "description", p.Description, Str(ws, r, 3), v => p.Description = v, changes);
                // origin column (4) locked
                SetStr(E, id, "extends",     p.Extends,     Str(ws, r, 5), v => p.Extends = v, changes);

                var lws = Num(ws, r, 6);
                if (lws.HasValue)
                    SetNum(E, id, "lineWeightScale", p.Appearance?.LineWeightScale, lws, v => (p.Appearance ??= new StylePackAppearance()).LineWeightScale = v, changes);
                SetStr(E, id, "textStyleName",      p.Appearance?.TextStyleName,      Str(ws, r, 7), v => (p.Appearance ??= new StylePackAppearance()).TextStyleName = v, changes);
                SetStr(E, id, "dimensionStyleName", p.Appearance?.DimensionStyleName, Str(ws, r, 8), v => (p.Appearance ??= new StylePackAppearance()).DimensionStyleName = v, changes);
                SetStr(E, id, "hatchPalette",       p.Appearance?.HatchPalette,       Str(ws, r, 9), v => (p.Appearance ??= new StylePackAppearance()).HatchPalette = v, changes);

                SetStr(E, id, "tagColorScheme",  p.TagColorScheme,  Str(ws, r,10), v => p.TagColorScheme = v, changes);
                SetStr(E, id, "defaultTagStyle", p.DefaultTagStyle, Str(ws, r,11), v => p.DefaultTagStyle = v, changes);
                SetStr(E, id, "templateMode",    p.TemplateMode,    Str(ws, r,12), v => p.TemplateMode = v, changes);
                SetStr(E, id, "discipline",      p.Discipline,      Str(ws, r,13), v => p.Discipline = v, changes);
                SetStr(E, id, "visualStyle",     p.VisualStyle,     Str(ws, r,14), v => p.VisualStyle = v, changes);
                SetStr(E, id, "phaseFilter",     p.PhaseFilter,     Str(ws, r,15), v => p.PhaseFilter = v, changes);
                // Column 16 ("checksum") was removed with DRAW-5; an older
                // workbook that still carries it is ignored — it was never read.
            }
        }

        private static void ApplyVgOverridesSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "VgOverrides");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Group rows by packId (full replace per pack). Each row starts from
            // a copy of the pack's existing override for that category, so a
            // key the sheet has no column for (visible, extension data) and the
            // spelling the entry uses are carried through.
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
                var existingPack = packs.StylePacks.FirstOrDefault(p => string.Equals(p.Id, pid, StringComparison.OrdinalIgnoreCase));
                StylePackVgOverride prior = null;
                existingPack?.VgOverrides?.TryGetValue(cat, out prior);
                var ov = prior != null ? CloneJson(prior) : new StylePackVgOverride();
                ov.SetProjColor(Norm(ws.Cell(r, 3).GetString().Trim()));
                ov.SetProjWeight(Int(ws, r, 4));
                ov.SetCutColor(Norm(ws.Cell(r, 5).GetString().Trim()));
                ov.SetCutWeight(Int(ws, r, 6));
                ov.Halftone = TriBool(ws, r, 7);
                ov.Transparency = Int(ws, r, 8);
                dict[cat] = ov;
            }

            foreach (var kv in byPack)
            {
                var pack = GetOrAddPack(packs, kv.Key);
                var old = pack.VgOverrides ?? new Dictionary<string, StylePackVgOverride>();
                bool same = old.Count == kv.Value.Count
                    && old.All(o => kv.Value.TryGetValue(o.Key, out var n) && n != null && o.Value != null && n.Key == o.Value.Key);
                if (same) continue;
                pack.VgOverrides = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "StylePack", Id = kv.Key, Field = "vgOverrides",
                    OldValue = $"{old.Count} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplyFilterRulesSheet(XLWorkbook wb, StylePackDoc packs, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "FilterRules");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;

            var byPack = new Dictionary<string, List<StylePackFilterRule>>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<StylePackFilterRule>(ReferenceEqualityComparer.Instance);
            for (int r = 2; r <= last; r++)
            {
                var pid  = ws.Cell(r, 1).GetString().Trim();
                var name = ws.Cell(r, 2).GetString().Trim();
                if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(name)) continue;
                if (!byPack.TryGetValue(pid, out var list)) { list = new(); byPack[pid] = list; }

                // Start from the pack's existing rule of the same name (first
                // not yet used), so keys the sheet has no column for —
                // surfFgColor, projLinePattern, … — are carried through.
                var existingPack = packs.StylePacks.FirstOrDefault(p => string.Equals(p.Id, pid, StringComparison.OrdinalIgnoreCase));
                var prior = existingPack?.FilterRules?.FirstOrDefault(f => f != null && !used.Contains(f)
                                && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (prior != null) used.Add(prior);
                var f = prior != null ? CloneJson(prior) : new StylePackFilterRule();
                f.Name         = name;
                f.Visible      = TriBool(ws, r, 3);
                f.Halftone     = TriBool(ws, r, 4);
                f.ProjColor    = Norm(ws.Cell(r, 5).GetString().Trim());
                f.ProjWeight   = Int(ws, r, 6);
                f.CutColor     = Norm(ws.Cell(r, 7).GetString().Trim());
                f.CutWeight    = Int(ws, r, 8);
                f.Transparency = Int(ws, r, 9);
                list.Add(f);
            }

            foreach (var kv in byPack)
            {
                var pack = GetOrAddPack(packs, kv.Key);
                var old = pack.FilterRules ?? new List<StylePackFilterRule>();
                if (old.Select(x => x?.Key).SequenceEqual(kv.Value.Select(x => x.Key), StringComparer.Ordinal)) continue;
                pack.FilterRules = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "StylePack", Id = kv.Key, Field = "filterRules",
                    OldValue = $"{old.Count} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static void ApplySlotsSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Slots");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int cPurposeTag = FindHeader(ws, "purposeTag");
            int cSlotRef    = FindHeader(ws, "slotRef");

            var byDt = new Dictionary<string, List<DrawingSlot>>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<DrawingSlot>(ReferenceEqualityComparer.Instance);
            for (int r = 2; r <= last; r++)
            {
                var did = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(did)) continue;
                if (!byDt.TryGetValue(did, out var list)) { list = new(); byDt[did] = list; }

                // Start from the type's existing slot with the same label (first
                // not yet used), so fields with no column are carried through.
                var label = ws.Cell(r, 2).GetString();
                var existing = dt.DrawingTypes.FirstOrDefault(t => string.Equals(t.Id, did, StringComparison.OrdinalIgnoreCase));
                var prior = existing?.Slots?.FirstOrDefault(x => x != null && !used.Contains(x)
                                && string.Equals(Norm(x.Label), Norm(label), StringComparison.Ordinal));
                if (prior != null) used.Add(prior);
                var s = prior != null ? CloneJson(prior) : new DrawingSlot();

                s.Label    = Norm(label) ?? (prior == null ? null : s.Label);
                s.ViewType = Norm(ws.Cell(r, 3).GetString());
                s.Required = TriBool(ws, r, 12) ?? false;
                if (TryReadDouble(ws.Cell(r, 4), out var x)) s.NormX = x;
                if (TryReadDouble(ws.Cell(r, 5), out var y)) s.NormY = y;
                if (TryReadDouble(ws.Cell(r, 6), out var w)) s.NormW = w;
                if (TryReadDouble(ws.Cell(r, 7), out var h)) s.NormH = h;
                s.Scale        = Int(ws, r, 8);
                s.DetailLevel  = Norm(ws.Cell(r, 9).GetString().Trim());
                s.ViewTemplate = Norm(ws.Cell(r,10).GetString().Trim());
                s.ViewportType = Norm(ws.Cell(r,11).GetString().Trim());
                if (cPurposeTag > 0) s.PurposeTag = Norm(ws.Cell(r, cPurposeTag).GetString().Trim());
                if (cSlotRef    > 0) s.SlotRef    = Norm(ws.Cell(r, cSlotRef).GetString().Trim());
                list.Add(s);
            }

            foreach (var kv in byDt)
            {
                var d = GetOrAddDt(dt, kv.Key);
                var old = d.Slots ?? new List<DrawingSlot>();
                if (JsonConvert.SerializeObject(old) == JsonConvert.SerializeObject(kv.Value)) continue;
                d.Slots = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "DrawingType", Id = kv.Key, Field = "slots",
                    OldValue = $"{old.Count} entries", NewValue = $"{kv.Value.Count} entries" });
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
                var old = d.TitleBlockParams ?? new Dictionary<string, string>();
                bool same = old.Count == kv.Value.Count
                    && old.All(o => kv.Value.TryGetValue(o.Key, out var n) && string.Equals(Norm(n), Norm(o.Value), StringComparison.Ordinal));
                if (same) continue;
                d.TitleBlockParams = kv.Value;
                changes.Add(new ChangeRecord { EntityType = "DrawingType", Id = kv.Key, Field = "titleBlockParams",
                    OldValue = $"{old.Count} entries", NewValue = $"{kv.Value.Count} entries" });
            }
        }

        private static T CloneJson<T>(T src) where T : class
            => src == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(src));

        /// <summary>
        /// DTW-180: read every predicate (by header name, so a workbook from
        /// before the extra columns still imports), and keep the corporate /
        /// project split. A row identical (signature + target) to a corporate
        /// rule IS that corporate rule; anything else is the project's. Only a
        /// change to the project's rules is recorded, and only project rules
        /// are written to the override (BuildProjectOverride) — the old code
        /// replaced the routing table wholesale and wrote all 143 corporate
        /// rules into the project file with no origin.
        /// </summary>
        private static void ApplyRoutingSheet(XLWorkbook wb, DrawingTypeLibrary dt, List<ChangeRecord> changes)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name == "Routing");
            if (ws == null) return;
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            int C(string h) => FindHeader(ws, h);
            string Get(int r, int col) => col < 1 ? null : NullIfEmpty(ws.Cell(r, col).GetString().Trim());
            int cDisc = C("discipline"), cPhase = C("phase"), cDoc = C("docType");
            int cDiscRx = C("disciplineMatches"), cPhaseRx = C("phaseMatches"), cDocRx = C("docTypeMatches");
            int cLvl = C("levelMatches"), cProj = C("projectCodeMatches"), cOpt = C("optionMatches");
            int cTarget = C("drawingTypeId");
            if (cTarget < 1) return;

            var corporate = (dt.Routing ?? new()).Where(r => r != null && !r.IsProjectRule).ToList();
            var corporateKeys = new HashSet<string>(corporate.Select(DrawingRoutingMatcher.SignatureWithTarget),
                StringComparer.OrdinalIgnoreCase);
            var oldProject = (dt.Routing ?? new()).Where(r => r != null && r.IsProjectRule).ToList();

            var sheetRules = new List<DrawingRoutingRule>();
            for (int r = 2; r <= last; r++)
            {
                var target = Get(r, cTarget);
                if (target == null) continue;
                var rule = new DrawingRoutingRule {
                    Discipline         = Get(r, cDisc)  ?? "*",
                    Phase              = Get(r, cPhase) ?? "*",
                    DocType            = Get(r, cDoc)   ?? "*",
                    DisciplineMatches  = Get(r, cDiscRx),
                    PhaseMatches       = Get(r, cPhaseRx),
                    DocTypeMatches     = Get(r, cDocRx),
                    LevelMatches       = Get(r, cLvl),
                    ProjectCodeMatches = Get(r, cProj),
                    OptionMatches      = Get(r, cOpt),
                    DrawingTypeId      = target,
                };
                rule.Origin = corporateKeys.Contains(DrawingRoutingMatcher.SignatureWithTarget(rule)) ? "corporate" : "project";
                sheetRules.Add(rule);
            }

            var newProject = sheetRules.Where(r => r.IsProjectRule).ToList();
            bool same = newProject.Count == oldProject.Count
                && newProject.Select(DrawingRoutingMatcher.SignatureWithTarget)
                       .SequenceEqual(oldProject.Select(DrawingRoutingMatcher.SignatureWithTarget), StringComparer.OrdinalIgnoreCase);
            if (same) return;

            // Project rules first (they are prepended at runtime anyway), then
            // the corporate table untouched.
            dt.Routing = newProject.Concat(corporate).ToList();
            changes.Add(new ChangeRecord { EntityType = "Routing", Id = "project", Field = "rules",
                OldValue = $"{oldProject.Count} project rule(s)", NewValue = $"{newProject.Count} project rule(s)" });
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
                    changes.Add(new ChangeRecord { EntityType = "StylePack", Id = p.Id, Field = "origin",
                        OldValue = "corporate", NewValue = "project" });
                }
            }
        }

        /// <summary>
        /// What ApplyImport writes: only project-origin drawing types, only
        /// project routing rules (DTW-180), only project-origin packs. Pure so
        /// the round-trip test can assert an unedited import writes nothing.
        /// </summary>
        public static (DrawingTypeLibrary Types, StylePackDoc Packs) BuildProjectOverride(
            DrawingTypeLibrary updatedDtLib, StylePackDoc updatedPacks)
        {
            static bool IsProject(string origin) => string.Equals(origin, "project", StringComparison.OrdinalIgnoreCase);
            var projectDt = new DrawingTypeLibrary {
                Version      = updatedDtLib?.Version ?? 1,
                DrawingTypes = (updatedDtLib?.DrawingTypes ?? new()).Where(d => d != null && IsProject(d.Origin)).ToList(),
                Routing      = (updatedDtLib?.Routing ?? new()).Where(r => r != null && r.IsProjectRule).ToList(),
            };
            var projectPacks = new StylePackDoc {
                SchemaVersion = updatedPacks?.SchemaVersion,
                Name          = updatedPacks?.Name,
                Description   = updatedPacks?.Description,
                Namespace     = updatedPacks?.Namespace,
                LastUpdated   = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StylePacks    = (updatedPacks?.StylePacks ?? new()).Where(p => p != null && IsProject(p.Origin)).ToList(),
                Routing       = updatedPacks?.Routing,
            };
            return (projectDt, projectPacks);
        }

        /// <summary>
        /// Corporate packs with the project's packs layered on top by id.
        ///
        /// DTW-183: <see cref="StylePackDoc.Routing"/> of the result holds the
        /// PROJECT's own pack routing only. It used to be "project routing, or
        /// else the corporate table", and ApplyImport wrote it to the project
        /// file — freezing all 28 corporate rules there, where
        /// ViewStylePackRegistry.Merge prepends them, duplicated and immune to
        /// later corporate changes. A project rule identical to a corporate
        /// rule is such a frozen copy and is dropped. The corporate table is
        /// not needed here: the workbook does not carry pack routing.
        /// Also first-wins by id (a duplicate id no longer throws).
        /// </summary>
        public static StylePackDoc MergeStylePacks(StylePackDoc baseDoc, StylePackDoc over)
        {
            var corporateRouting = (baseDoc?.Routing ?? new())
                .Where(r => r != null).Select(r => JsonConvert.SerializeObject(r)).ToHashSet(StringComparer.Ordinal);
            var projectRouting = (over?.Routing ?? new())
                .Where(r => r != null && !corporateRouting.Contains(JsonConvert.SerializeObject(r))).ToList();

            var merged = new StylePackDoc {
                SchemaVersion = over?.SchemaVersion ?? baseDoc?.SchemaVersion,
                Name          = over?.Name          ?? baseDoc?.Name,
                Description   = over?.Description   ?? baseDoc?.Description,
                Namespace     = over?.Namespace     ?? baseDoc?.Namespace,
                LastUpdated   = over?.LastUpdated   ?? baseDoc?.LastUpdated,
                StylePacks    = new List<StylePackEntry>(),
                Routing       = projectRouting.Count > 0 ? projectRouting : null,
            };
            var order = new List<string>();
            var byId = new Dictionary<string, StylePackEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in baseDoc?.StylePacks ?? new())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id) || byId.ContainsKey(p.Id)) continue;
                byId[p.Id] = p; order.Add(p.Id);
            }
            foreach (var p in over?.StylePacks ?? new())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;
                if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                if (!byId.ContainsKey(p.Id)) order.Add(p.Id);
                byId[p.Id] = p;
            }
            merged.StylePacks = order.Select(k => byId[k]).ToList();
            return merged;
        }
    }
}
