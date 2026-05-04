// ============================================================================
// StructuralCADWizard.cs — Single-Page WPF Dialog for Structural DWG-to-BIM
//
// Complete rewrite: single scrollable page with 5 sections:
//   1. DWG IMPORT & LAYER ANALYSIS — DWG selector, Analyze Layers button,
//      DataGrid with Layer Name/Entities/Lines/Arcs/Auto-Detect/Conf./Map To
//   2. ELEMENT-LAYER MAPPING — 6 ComboBox dropdowns (Column/Beam/Wall/Slab/
//      Foundation/Grid) populated from analyzed DWG layers
//   3. LEVELS & ELEMENT PROPERTIES — Base/Top level, column/beam dimensions,
//      wall/slab/foundation config, auto-detect sizes checkbox
//   4. CONSTRUCTION LOGIC & TAGGING — Construction relationships (beams on
//      walls, beams connect to slabs, columns stop at slab soffit, structural
//      wall checkbox), STING ISO 19650 tagging options
//   5. NUMBERING — Smart numbering with category, parameter, template,
//      group/element enumeration, preview
//
// Fixes:
//   - Layer grid now populated with DWG layer data on Analyze
//   - "Map To" column has ComboBox dropdown with Revit categories
//   - All buttons functional (Select All/None/Structural Only/Auto-Map)
//   - Structural wall checkbox added
//   - Footing/slab configuration added
//   - Columns stop at slab soffit (not slab level)
//   - Smart numbering engine
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.Model
{
    #region Layer Row Data Model

    /// <summary>Row data for the layer analysis DataGrid.</summary>
    public class LayerRowData : INotifyPropertyChanged
    {
        public string LayerName { get; set; }
        public int Entities { get; set; }
        public int Lines { get; set; }
        public int Arcs { get; set; }
        public string AutoDetect { get; set; }
        public double Confidence { get; set; }
        public string ConfidenceText => Confidence > 0 ? $"{Confidence * 100:F0}%" : "—";

        private bool _selected = true;
        public bool Selected
        {
            get => _selected;
            set { _selected = value; OnPropertyChanged(nameof(Selected)); }
        }

        private string _mapTo = "";
        public string MapTo
        {
            get => _mapTo;
            set { _mapTo = value; OnPropertyChanged(nameof(MapTo)); }
        }

        /// <summary>Display for layer dropdowns.</summary>
        public override string ToString() => $"{LayerName} ({Entities} ent.)";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Detected element with auto-sized dimensions from DWG geometry.</summary>
    public class DetectedElementGroup
    {
        public string ElementType { get; set; } // Column, Beam, Wall, Slab, Foundation
        public string SizeLabel { get; set; }    // e.g. "300x300", "ø450", "200 thk"
        public int Count { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double ThicknessMm { get; set; }
        public bool IsCircular { get; set; }
    }

    #endregion

    #region Numbering Engine

    /// <summary>
    /// STING element numbering engine with template-based numbering and grouping.
    /// Supports template-based numbering with group and element enumeration,
    /// multiple numbering styles, and live preview.
    /// </summary>
    public static class NumberingEngine
    {
        // CAD-HIGH-04: Cache grid collection per document to avoid FilteredElementCollector per element
        private static string _cachedGridDocPath;
        private static List<Autodesk.Revit.DB.Grid> _cachedGrids;

        /// <summary>Enumeration style for numbering sequences.</summary>
        public enum EnumStyle
        {
            Numeric,        // 1, 2, 3...
            CapitalLetters, // A, B, C...
            LowerLetters,   // a, b, c...
            CapitalRoman,   // I, II, III...
            LowerRoman      // i, ii, iii...
        }

        /// <summary>Element selection scope.</summary>
        public enum SelectionScope
        {
            AllFromProject,
            AllFromActiveView,
            SelectedElements,
            ByLevel,
            ByWorkset
        }

        /// <summary>Grouping algorithm for numbering.</summary>
        public enum GroupingAlgorithm
        {
            None,           // No grouping — sequential
            ByLevel,        // Group by level
            ByType,         // Group by family type
            ByGridLine,     // Group by nearest grid
            ByLocation,     // Group by spatial proximity
            ByMark          // Group by existing Mark value
        }

        /// <summary>Configuration for a numbering operation.</summary>
        public class NumberingConfig
        {
            public BuiltInCategory Category { get; set; } = BuiltInCategory.OST_StructuralColumns;
            public string ParameterName { get; set; } = "Mark";
            public string Prefix { get; set; } = "GFC";
            public string Separator { get; set; } = "-";
            public string Suffix { get; set; } = "";

            // Group enumeration
            public bool UseGroupEnum { get; set; } = false;
            public EnumStyle GroupStyle { get; set; } = EnumStyle.CapitalLetters;
            public string GroupSeparator { get; set; } = "-";

            // Element enumeration
            public bool UseElementEnum { get; set; } = true;
            public EnumStyle ElementStyle { get; set; } = EnumStyle.Numeric;
            public int StartFrom { get; set; } = 1;
            public int NumberOfDigits { get; set; } = 2;
            public int IncrementBy { get; set; } = 1;

            // Selection & grouping
            public SelectionScope Scope { get; set; } = SelectionScope.AllFromActiveView;
            public GroupingAlgorithm Grouping { get; set; } = GroupingAlgorithm.ByLevel;
            public bool OmitAlreadyNumbered { get; set; } = false;
            public bool ExcludeByFamilyType { get; set; } = false;
            public List<string> ExcludedFamilyTypes { get; set; } = new();
        }

        /// <summary>Generate a preview of the numbering sequence.</summary>
        public static List<string> GeneratePreview(NumberingConfig config, int count = 8)
        {
            var result = new List<string>();
            int current = config.StartFrom;

            for (int i = 0; i < count; i++)
            {
                string num = FormatNumber(current, config.ElementStyle, config.NumberOfDigits);
                string tag = config.Prefix;

                if (config.UseGroupEnum)
                {
                    string groupPart = FormatNumber(1, config.GroupStyle, 0);
                    tag += config.GroupSeparator + groupPart;
                }

                if (config.UseElementEnum)
                    tag += config.Separator + num;

                tag += config.Suffix;
                result.Add(tag);
                current += config.IncrementBy;
            }
            return result;
        }

        /// <summary>Format a number in the specified enumeration style.</summary>
        public static string FormatNumber(int value, EnumStyle style, int digits)
        {
            switch (style)
            {
                case EnumStyle.Numeric:
                    return digits > 0 ? value.ToString().PadLeft(digits, '0') : value.ToString();
                case EnumStyle.CapitalLetters:
                    return ToAlpha(value, true);
                case EnumStyle.LowerLetters:
                    return ToAlpha(value, false);
                case EnumStyle.CapitalRoman:
                    return ToRoman(value).ToUpper();
                case EnumStyle.LowerRoman:
                    return ToRoman(value).ToLower();
                default:
                    return value.ToString();
            }
        }

        private static string ToAlpha(int value, bool upper)
        {
            string result = "";
            while (value > 0)
            {
                value--;
                result = (char)((upper ? 'A' : 'a') + (value % 26)) + result;
                value /= 26;
            }
            return result.Length == 0 ? (upper ? "A" : "a") : result;
        }

        private static string ToRoman(int number)
        {
            if (number <= 0) return "0";
            string[] thousands = { "", "M", "MM", "MMM" };
            string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
            string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
            string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return thousands[Math.Min(number / 1000, 3)]
                 + hundreds[Math.Min((number % 1000) / 100, 9)]
                 + tens[Math.Min((number % 100) / 10, 9)]
                 + ones[Math.Min(number % 10, 9)];
        }

        /// <summary>
        /// Apply numbering to elements in the document.
        /// Returns count of numbered elements.
        /// </summary>
        public static int ApplyNumbering(Document doc, NumberingConfig config,
            IList<ElementId> elementIds = null)
        {
            var collector = elementIds != null
                ? new FilteredElementCollector(doc, elementIds)
                : new FilteredElementCollector(doc).OfCategory(config.Category)
                    .WhereElementIsNotElementType();

            var elements = collector.ToList();

            // Filter excluded family types
            if (config.ExcludeByFamilyType && config.ExcludedFamilyTypes.Count > 0)
            {
                elements = elements.Where(e =>
                {
                    string familyName = ParameterHelpers.GetFamilyName(e);
                    string typeName = ParameterHelpers.GetFamilySymbolName(e);
                    return !config.ExcludedFamilyTypes.Contains($"{familyName}: {typeName}");
                }).ToList();
            }

            // Filter already numbered
            if (config.OmitAlreadyNumbered)
            {
                elements = elements.Where(e =>
                {
                    string val = ParameterHelpers.GetString(e, config.ParameterName);
                    return string.IsNullOrWhiteSpace(val);
                }).ToList();
            }

            if (elements.Count == 0) return 0;

            // Sort elements spatially (by level, then X, then Y)
            elements = SortElementsSpatially(doc, elements);

            // Group elements
            var groups = GroupElements(doc, elements, config.Grouping);

            // WIZ-HIGH-03 FIX: Build existing marks HashSet for collision detection.
            // Prevents duplicate marks when numbering overlaps with pre-existing marks.
            var existingMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var allElements = new FilteredElementCollector(doc)
                    .OfCategory(config.Category)
                    .WhereElementIsNotElementType();
                foreach (var existing in allElements)
                {
                    string mark = existing.LookupParameter(config.ParameterName)?.AsString();
                    if (!string.IsNullOrEmpty(mark)) existingMarks.Add(mark);
                }
            }
            catch (Exception ex) { StingLog.Warn($"NumberingEngine mark scan: {ex.Message}"); }

            int numbered = 0;
            int collisions = 0;
            int groupIndex = 1;

            using (var tx = new Transaction(doc, "STING Number Elements"))
            {
                tx.Start();
                foreach (var group in groups)
                {
                    int elementIndex = config.StartFrom;
                    foreach (var el in group)
                    {
                        string tag = BuildNumberTag(config, groupIndex, elementIndex);

                        // Collision detection: if mark already exists, increment until unique
                        int attempts = 0;
                        while (existingMarks.Contains(tag) && attempts < 100)
                        {
                            elementIndex += config.IncrementBy;
                            tag = BuildNumberTag(config, groupIndex, elementIndex);
                            attempts++;
                            collisions++;
                        }

                        var param = el.LookupParameter(config.ParameterName);
                        if (param != null && !param.IsReadOnly)
                        {
                            param.Set(tag);
                            existingMarks.Add(tag); // Track newly assigned marks
                            numbered++;
                        }
                        elementIndex += config.IncrementBy;
                    }
                    groupIndex++;
                }
                tx.Commit();
            }

            if (collisions > 0)
                StingLog.Info($"NumberingEngine: {collisions} collisions resolved by auto-increment");

            return numbered;
        }

        /// <summary>
        /// Phase-140 P1-E: Apply numbering for every category in <paramref name="perCategory"/>.
        /// Each category is processed independently with its own NumberingConfig — its
        /// prefix, counter, grouping all run in isolation. Returns the total count of
        /// numbered elements summed across categories.
        /// </summary>
        public static int ApplyAllPerCategory(Document doc,
            Dictionary<BuiltInCategory, NumberingConfig> perCategory,
            IList<ElementId> scope = null)
        {
            if (doc == null || perCategory == null || perCategory.Count == 0) return 0;

            int total = 0;
            foreach (var kvp in perCategory)
            {
                var cfg = kvp.Value;
                if (cfg == null) continue;
                cfg.Category = kvp.Key; // defensive — ensure config aligns with key

                IList<ElementId> filtered = null;
                if (scope != null)
                {
                    filtered = scope
                        .Select(id => doc.GetElement(id))
                        .Where(el => el != null && el.Category != null
                            && el.Category.Id.Value == (long)kvp.Key)
                        .Select(el => el.Id)
                        .ToList();
                    if (filtered.Count == 0) continue;
                }

                try
                {
                    int n = ApplyNumbering(doc, cfg, filtered);
                    total += n;
                    StingLog.Info($"NumberingEngine: numbered {n} {kvp.Key} element(s) " +
                        $"with prefix '{cfg.Prefix}'");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"NumberingEngine per-category {kvp.Key}: {ex.Message}");
                }
            }
            return total;
        }

        private static string BuildNumberTag(NumberingConfig config, int groupNum, int elemNum)
        {
            string tag = config.Prefix;

            if (config.UseGroupEnum)
            {
                tag += config.GroupSeparator + FormatNumber(groupNum, config.GroupStyle, 0);
            }

            if (config.UseElementEnum)
            {
                tag += config.Separator + FormatNumber(elemNum, config.ElementStyle, config.NumberOfDigits);
            }

            tag += config.Suffix;
            return tag;
        }

        private static List<Element> SortElementsSpatially(Document doc, List<Element> elements)
        {
            return elements.OrderBy(e =>
            {
                try
                {
                    var lvl = doc.GetElement(e.LevelId) as Level;
                    return lvl?.Elevation ?? 0;
                }
                catch (Exception ex) { StingLog.Warn($"StructuralCADWizard level sort failed: {ex.Message}"); return 0.0; }
            })
            .ThenBy(e =>
            {
                try
                {
                    var loc = e.Location as LocationPoint;
                    return loc?.Point.X ?? 0;
                }
                catch (Exception ex) { StingLog.Warn($"StructuralCADWizard X sort failed: {ex.Message}"); return 0.0; }
            })
            .ThenBy(e =>
            {
                try
                {
                    var loc = e.Location as LocationPoint;
                    return loc?.Point.Y ?? 0;
                }
                catch (Exception ex) { StingLog.Warn($"StructuralCADWizard Y sort failed: {ex.Message}"); return 0.0; }
            }).ToList();
        }

        private static List<List<Element>> GroupElements(Document doc,
            List<Element> elements, GroupingAlgorithm algo)
        {
            if (algo == GroupingAlgorithm.None)
                return new List<List<Element>> { elements };

            var groups = new Dictionary<string, List<Element>>();

            foreach (var el in elements)
            {
                string key = algo switch
                {
                    GroupingAlgorithm.ByLevel => GetLevelKey(doc, el),
                    GroupingAlgorithm.ByType => GetTypeKey(el),
                    GroupingAlgorithm.ByGridLine => GetGridKey(doc, el),
                    GroupingAlgorithm.ByLocation => GetLocationKey(el),
                    GroupingAlgorithm.ByMark => ParameterHelpers.GetString(el, "Mark"),
                    _ => "ALL"
                };

                if (string.IsNullOrEmpty(key)) key = "UNGROUPED";
                if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                groups[key].Add(el);
            }

            return groups.OrderBy(g => g.Key).Select(g => g.Value).ToList();
        }

        private static string GetLevelKey(Document doc, Element el)
        {
            try
            {
                var lvl = doc.GetElement(el.LevelId) as Level;
                return lvl?.Name ?? "No Level";
            }
            catch (Exception ex) { StingLog.Warn($"NumberingEngine.GetLevelKey: {ex.Message}"); return "No Level"; }
        }

        private static string GetTypeKey(Element el)
        {
            try
            {
                string family = ParameterHelpers.GetFamilyName(el);
                string type = ParameterHelpers.GetFamilySymbolName(el);
                return $"{family}: {type}";
            }
            catch (Exception ex) { StingLog.Warn($"NumberingEngine.GetTypeKey: {ex.Message}"); return "Unknown"; }
        }

        private static string GetGridKey(Document doc, Element el)
        {
            try
            {
                var pt = (el.Location as LocationPoint)?.Point;
                if (pt == null) return "Off Grid";

                // CAD-HIGH-04: Use cached grid collection instead of per-element collector
                string docKey = doc.PathName ?? doc.Title ?? "Untitled";
                if (_cachedGrids == null || _cachedGridDocPath != docKey)
                {
                    _cachedGrids = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Grid))
                        .Cast<Autodesk.Revit.DB.Grid>().ToList();
                    _cachedGridDocPath = docKey;
                }
                var grids = _cachedGrids;

                Autodesk.Revit.DB.Grid nearest = null;
                double minDist = double.MaxValue;
                foreach (var g in grids)
                {
                    var curve = g.Curve;
                    var result = curve.Project(pt);
                    if (result != null && result.Distance < minDist)
                    {
                        minDist = result.Distance;
                        nearest = g;
                    }
                }

                return nearest != null && minDist < 3.0 ? nearest.Name : "Off Grid";
            }
            catch (Exception ex) { StingLog.Warn($"NumberingEngine.GetGridKey: {ex.Message}"); return "Off Grid"; }
        }

        /// <summary>
        /// Groups elements by spatial proximity using a grid-based clustering algorithm.
        /// Divides the model space into cells (default 5m × 5m) and assigns elements
        /// to their containing cell. Elements in the same cell are grouped together.
        /// </summary>
        private static string GetLocationKey(Element el)
        {
            try
            {
                XYZ pt = null;
                if (el.Location is LocationPoint lp) pt = lp.Point;
                else if (el.Location is LocationCurve lc) pt = lc.Curve.Evaluate(0.5, true);
                if (pt == null) return "NoLocation";

                // Grid cell size: 5m (~16.4 ft)
                double cellSize = 16.4;
                int cellX = (int)Math.Floor(pt.X / cellSize);
                int cellY = (int)Math.Floor(pt.Y / cellSize);
                return $"Zone_{cellX}_{cellY}";
            }
            catch (Exception ex) { StingLog.Warn($"NumberingEngine.GetLocationKey: {ex.Message}"); return "NoLocation"; }
        }
    }

    #endregion

    #region Structural DWG-to-BIM Dialog

    /// <summary>
    /// Single-page WPF dialog for Structural DWG-to-BIM conversion.
    /// Replaces the old 5-page wizard with a comprehensive scrollable layout.
    /// </summary>
    public class StructuralDWGDialog : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly Document _doc;
        private readonly StructuralCADPipeline _pipeline;
        private ImportInstance _selectedImport;
        private StructuralExtractionResult _extraction;
        private readonly List<DetectedElementGroup> _detectedGroups = new();

        // Layer data
        private ObservableCollection<LayerRowData> _layerRows = new();
        private DataGrid _layerGrid;

        // Element-Layer Mapping combos (populated from DWG layers)
        private ComboBox _cboColumnLayer, _cboBeamLayer, _cboWallLayer;
        private ComboBox _cboSlabLayer, _cboFoundationLayer, _cboGridLayer;
        private CheckBox _chkColumnLayer, _chkBeamLayer, _chkWallLayer;
        private CheckBox _chkSlabLayer, _chkFoundationLayer, _chkGridLayer;

        // Level config
        private ComboBox _cboBaseLevel, _cboTopLevel;
        private CheckBox _chkAutoDetectSizes;
        private readonly List<CheckBox> _repeatLevelCheckboxesCad = new();
        private CheckBox _chkColumnsContinuousCad;

        // Column/Beam dimensions
        private TextBox _txtColHeight, _txtBeamDepth, _txtBeamWidth;
        private TextBox _txtWallHeight, _txtWallThick;
        private TextBox _txtSlabThick, _txtFdnDepth;

        // Construction logic
        private CheckBox _chkBeamsOnWalls, _chkBeamsConnectSlabs;
        private CheckBox _chkColumnsStopAtSoffit, _chkStructuralWall;

        // Phase-78 EaseBit detection controls
        private CheckBox _chkDryRun, _chkExplodeOnImport, _chkDetectOpenings, _chkUseSpatialIndex;
        private TextBox _txtMinWallThickness, _txtMaxWallThickness, _txtParallelDot;
        private TextBox _txtParallelGap, _txtEndpointTol;
        private TextBox _txtMinOpeningWidth, _txtMaxOpeningWidth;
        // Phase-140 accuracy controls
        private TextBox _txtEndpointGap, _txtGridSnapTol, _txtSpanToDepth, _txtBeamDepthMin, _txtBeamDepthMax, _txtDuplicateTol;
        private CheckBox _chkUseSpanToDepth, _chkUseGridLabelMarks, _chkSkipDuplicates, _chkTrimBeamsToColumns, _chkShowWarningsInView;
        // Phase-142 controls
        private TextBox _txtMinColDiam, _txtMaxColDiam, _txtOverlapRatio, _txtStripOversize, _txtBeamSupportTol;
        private CheckBox _chkDetectStripFoundations, _chkMarkCantilevers;
        // Phase-143 controls
        private TextBox _txtRaftMinArea, _txtRoomLabelRadius;
        private CheckBox _chkSeedRoomsFromSlabs, _chkCreateStructuralViews, _chkInferBeamMaterial,
            _chkClassifyFoundations, _chkStampJunctionMarks;
        private CheckBox _chkPerCategoryNumbering;
        // Per-category numbering state — keyed by NumberingCategories[] index. Snapshot of
        // the visible UI state for the previously-selected category, captured on category
        // change so the user can configure each category independently.
        private readonly Dictionary<int, NumberingEngine.NumberingConfig> _numConfigByCatIndex
            = new Dictionary<int, NumberingEngine.NumberingConfig>();
        private int _activeNumCatIndex = 0;

        // Phase-97 per-element size detection + type creation toggles
        private CheckBox _chkDetectSizes_Wall, _chkDetectSizes_Column, _chkDetectSizes_Beam;
        private CheckBox _chkDetectSizes_Foundation, _chkDetectSizes_Slab;
        private CheckBox _chkCreateTypes_Wall, _chkCreateTypes_Column, _chkCreateTypes_Beam;
        private CheckBox _chkCreateTypes_Foundation, _chkCreateTypes_Slab;
        private TextBox _txtColWidthFallback, _txtColDepthFallback, _txtFdnWidthFallback;

        // Tagging
        private CheckBox _chkAutoTag, _chkAutoSeqNumbers;
        private TextBox _txtTagPrefix;
        private ComboBox _cboNumbering, _cboTagFamily;

        // Numbering config
        private ComboBox _cboNumCategory, _cboNumParameter;
        private TextBox _txtNumPrefix, _txtNumSeparator;
#pragma warning disable CS0169 // '_txtNumSuffix' is reserved for future use
        private TextBox _txtNumSuffix;
#pragma warning restore CS0169
        private CheckBox _chkGroupEnum, _chkElementEnum;
        private ComboBox _cboGroupStyle, _cboElementStyle;
        private TextBox _txtStartFrom, _txtDigits, _txtIncrement;
        private TextBlock _txtPreview;
        private CheckBox _chkOmitAlreadyNumbered;

        // Status
        private TextBlock _statusBar;

        // Result
        public bool Confirmed { get; private set; }
        public DWGConversionConfig GetConfig() => BuildConfig();

        // ── Map To categories ──
        private static readonly string[] MapToCategories = {
            "", "Column", "Beam", "Wall", "Slab", "Foundation",
            "Roof", "Stair", "Ramp", "PadFoundation", "Pile",
            "RetainingWall", "Bracing",
            "Grid", "Annotation", "Dimension", "Other", "Skip"
        };

        // ── Numbering categories ──
        private static readonly (string Name, BuiltInCategory Cat)[] NumberingCategories = {
            ("Structural Columns", BuiltInCategory.OST_StructuralColumns),
            ("Structural Framing", BuiltInCategory.OST_StructuralFraming),
            ("Structural Foundations", BuiltInCategory.OST_StructuralFoundation),
            ("Walls", BuiltInCategory.OST_Walls),
            ("Floors", BuiltInCategory.OST_Floors),
            ("Columns", BuiltInCategory.OST_Columns),
            ("Doors", BuiltInCategory.OST_Doors),
            ("Windows", BuiltInCategory.OST_Windows),
            ("Rooms", BuiltInCategory.OST_Rooms),
            ("Sheets", BuiltInCategory.OST_Sheets),
            ("Views", BuiltInCategory.OST_Views),
        };

        // ── Constructor ──────────────────────────────────────────────────
        public StructuralDWGDialog(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _pipeline = new StructuralCADPipeline(doc);

            Title = "STING Structural DWG-to-BIM Conversion";
            Width = 980;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.White;

            BuildLayout();
        }

        // ── Colour helpers ────────────────────────────────────────────────
        private static Brush FZ(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        // ── Colours ──────────────────────────────────────────────────────
        private static readonly Brush AccentOrange   = FZ(0xE8, 0x91, 0x2D);
        private static readonly Brush DarkBlue       = FZ(0x1A, 0x23, 0x7E);
        private static readonly Brush LightBg        = FZ(0xFA, 0xF7, 0xF2);
        private static readonly Brush SectionBorder  = FZ(0xE8, 0x91, 0x2D); // same as AccentOrange
        private static readonly Brush HeaderBg       = FZ(0x1A, 0x23, 0x7E); // same as DarkBlue
        private static readonly Brush SubtitleOrange = FZ(0xFF, 0xA7, 0x26);
        private static readonly Brush FooterBg       = FZ(0xF0, 0xF0, 0xF0);
        private static readonly Brush FooterBorder   = FZ(0xCC, 0xCC, 0xCC);
        private static readonly Brush AltRowBg       = FZ(0xFA, 0xF7, 0xF2); // same as LightBg
        private static readonly Brush GreenFg        = FZ(0x00, 0x80, 0x00);

        // ── Layout ───────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });

            // ── Header ──
            var header = new Border
            {
                Background = HeaderBg,
                Padding = new Thickness(16, 8, 16, 8),
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerGrid.Children.Add(new TextBlock
            {
                Text = "Structural DWG-to-BIM Conversion",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            });
            var subtitleText = new TextBlock
            {
                Text = "Select a DWG import and analyze layers",
                FontSize = 11, Foreground = SubtitleOrange,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(subtitleText, 1);
            headerGrid.Children.Add(subtitleText);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Content (scrollable) ──
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 8, 16, 8),
            };
            var content = new StackPanel();

            content.Children.Add(BuildSection1_DWGImport());
            content.Children.Add(BuildSection2_ElementLayerMapping());
            content.Children.Add(BuildSection3_LevelsAndProperties());
            content.Children.Add(BuildSection4_ConstructionLogic());
            content.Children.Add(BuildSectionPerElementSizing());
            content.Children.Add(BuildSectionEaseBitDetection());
            content.Children.Add(BuildSection5_Numbering());

            scrollViewer.Content = content;
            Grid.SetRow(scrollViewer, 1);
            root.Children.Add(scrollViewer);

            // ── Footer ──
            var footer = new Border
            {
                Background = FooterBg,
                BorderBrush = FooterBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 6, 16, 6),
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusBar = new TextBlock
            {
                Text = "Select DWG import → Analyze → Map layers → Convert",
                Foreground = Brushes.Gray, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            footerGrid.Children.Add(_statusBar);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            // Phase-140 P3-D: dry-run preview button — stays in the dialog so users
            // can adjust tolerances and re-run detection without dismissing.
            var btnDryRun = MakeBtn("Re-analyse (dry-run)", OnDryRunPreview);
            btnDryRun.MinWidth = 160;
            btnDryRun.ToolTip = "Run extraction + detection with current settings and " +
                "report what WOULD be created. No Revit transactions are opened. " +
                "Adjust tolerances and click again to iterate.";
            var btnConvert = MakeBtn("Convert to BIM", OnConvert);
            btnConvert.Background = AccentOrange;
            btnConvert.Foreground = Brushes.White;
            btnConvert.FontWeight = FontWeights.Bold;
            btnConvert.MinWidth = 140;
            var btnCancel = MakeBtn("Cancel", (s, e) => { Confirmed = false; Close(); });
            btnPanel.Children.Add(btnDryRun);
            btnPanel.Children.Add(btnConvert);
            btnPanel.Children.Add(btnCancel);
            Grid.SetColumn(btnPanel, 1);
            footerGrid.Children.Add(btnPanel);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // ── Section 1: DWG Import & Layer Analysis ───────────────────────

        private FrameworkElement BuildSection1_DWGImport()
        {
            var section = MakeSection("DWG IMPORT & LAYER ANALYSIS");
            var stack = (StackPanel)((Border)section).Child;

            // DWG Import selector row
            var importRow = new Grid();
            importRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            importRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            importRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            importRow.Children.Add(MakeLabel("DWG Import:", 0));

            var cboImport = new ComboBox { Margin = new Thickness(4, 2, 4, 2) };
            var imports = CADToModelEngine.FindImportInstances(_doc);
            foreach (var imp in imports)
            {
                string name = "DWG Import";
                try { name = imp.Name ?? $"Import #{imp.Id.Value}"; }
                catch (Exception ex) { StingLog.Warn($"Import name: {ex.Message}"); }
                cboImport.Items.Add(new ComboBoxItem { Content = name, Tag = imp });
            }
            if (cboImport.Items.Count > 0)
            {
                cboImport.SelectedIndex = 0;
                _selectedImport = imports[0];
            }
            cboImport.SelectionChanged += (s, e) =>
            {
                if (cboImport.SelectedItem is ComboBoxItem item && item.Tag is ImportInstance imp)
                    _selectedImport = imp;
            };
            Grid.SetColumn(cboImport, 1);
            importRow.Children.Add(cboImport);

            var btnAnalyze = MakeBtn("Analyze Layers", OnAnalyzeLayers);
            btnAnalyze.Background = AccentOrange;
            btnAnalyze.Foreground = Brushes.White;
            btnAnalyze.FontWeight = FontWeights.Bold;
            Grid.SetColumn(btnAnalyze, 2);
            importRow.Children.Add(btnAnalyze);

            stack.Children.Add(importRow);

            // Button bar
            var btnBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            var btnSelectAll = MakeSmallBtn("Select All", OnSelectAll);
            var btnSelectNone = MakeSmallBtn("Select None", OnSelectNone);
            var btnStructOnly = MakeSmallBtn("Structural Only", OnStructuralOnly);
            btnStructOnly.Background = AccentOrange;
            btnStructOnly.Foreground = Brushes.White;
            var btnAutoMap = MakeSmallBtn("Auto-Map Layers", OnAutoMapLayers);
            btnAutoMap.Background = AccentOrange;
            btnAutoMap.Foreground = Brushes.White;
            btnBar.Children.Add(btnSelectAll);
            btnBar.Children.Add(btnSelectNone);
            btnBar.Children.Add(btnStructOnly);
            btnBar.Children.Add(btnAutoMap);
            stack.Children.Add(btnBar);

            // Layer DataGrid
            _layerGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = AltRowBg,
                MinHeight = 160,
                MaxHeight = 240,
                Margin = new Thickness(0, 4, 0, 4),
                ItemsSource = _layerRows,
                IsReadOnly = false,
            };

            // Columns
            _layerGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "✓", Binding = new System.Windows.Data.Binding("Selected") { Mode = BindingMode.TwoWay },
                Width = 30,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Layer Name", Binding = new System.Windows.Data.Binding("LayerName"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Entities", Binding = new System.Windows.Data.Binding("Entities"), Width = 60, IsReadOnly = true,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Lines", Binding = new System.Windows.Data.Binding("Lines"), Width = 50, IsReadOnly = true,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Arcs", Binding = new System.Windows.Data.Binding("Arcs"), Width = 50, IsReadOnly = true,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Auto-Detect", Binding = new System.Windows.Data.Binding("AutoDetect"), Width = 90, IsReadOnly = true,
            });
            _layerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Conf.", Binding = new System.Windows.Data.Binding("ConfidenceText"), Width = 50, IsReadOnly = true,
            });

            // Map To column with ComboBox
            var mapToCol = new DataGridComboBoxColumn
            {
                Header = "Map To",
                SelectedItemBinding = new System.Windows.Data.Binding("MapTo") { Mode = BindingMode.TwoWay },
                Width = 100,
            };
            mapToCol.ItemsSource = MapToCategories;
            _layerGrid.Columns.Add(mapToCol);

            stack.Children.Add(_layerGrid);

            // Hint text
            stack.Children.Add(new TextBlock
            {
                Text = "Analyze DWG to detect element sizes",
                FontSize = 10, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 2, 0, 0),
            });

            return section;
        }

        // ── Section 2: Element-Layer Mapping ─────────────────────────────

        private FrameworkElement BuildSection2_ElementLayerMapping()
        {
            var section = MakeSection("ELEMENT-LAYER MAPPING");
            var stack = (StackPanel)((Border)section).Child;

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            // Row 0: Column, Beam, Wall
            (_chkColumnLayer, _cboColumnLayer) = MakeLayerMappingCell("Column Layer:", 0, 0, grid);
            (_chkBeamLayer, _cboBeamLayer) = MakeLayerMappingCell("Beam Layer:", 0, 1, grid);
            (_chkWallLayer, _cboWallLayer) = MakeLayerMappingCell("Wall Layer:", 0, 2, grid);

            // Row 1: Slab, Foundation, Grid
            (_chkSlabLayer, _cboSlabLayer) = MakeLayerMappingCell("Slab Layer:", 1, 0, grid);
            (_chkFoundationLayer, _cboFoundationLayer) = MakeLayerMappingCell("Foundation:", 1, 1, grid);
            (_chkGridLayer, _cboGridLayer) = MakeLayerMappingCell("Grid Layer:", 1, 2, grid);

            stack.Children.Add(grid);
            return section;
        }

        private (CheckBox chk, ComboBox cbo) MakeLayerMappingCell(string label, int row, int col, Grid parent)
        {
            var cellStack = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };

            var chk = new CheckBox
            {
                Content = label, IsChecked = true,
                FontWeight = FontWeights.SemiBold, FontSize = 11,
                Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 2),
            };
            cellStack.Children.Add(chk);

            var cbo = new ComboBox { Margin = new Thickness(0, 2, 0, 2), MinHeight = 24 };
            // Will be populated after layer analysis
            cellStack.Children.Add(cbo);

            Grid.SetRow(cellStack, row);
            Grid.SetColumn(cellStack, col);
            parent.Children.Add(cellStack);

            return (chk, cbo);
        }

        // ── Section 3: Levels & Element Properties ───────────────────────

        private FrameworkElement BuildSection3_LevelsAndProperties()
        {
            var section = MakeSection("LEVELS & ELEMENT PROPERTIES");
            var stack = (StackPanel)((Border)section).Child;

            var mainGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column: Level config
            var levelStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            levelStack.Children.Add(new TextBlock
            {
                Text = "LEVEL CONFIGURATION", FontWeight = FontWeights.Bold,
                FontSize = 11, Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 6),
            });

            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();

            levelStack.Children.Add(MakeLabel("Base Level:", -1));
            _cboBaseLevel = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var lev in levels) _cboBaseLevel.Items.Add(lev.Name);
            if (_cboBaseLevel.Items.Count > 0) _cboBaseLevel.SelectedIndex = 0;
            levelStack.Children.Add(_cboBaseLevel);

            levelStack.Children.Add(MakeLabel("Top Level:", -1));
            _cboTopLevel = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var lev in levels) _cboTopLevel.Items.Add(lev.Name);
            if (_cboTopLevel.Items.Count > 1) _cboTopLevel.SelectedIndex = _cboTopLevel.Items.Count - 1;
            else if (_cboTopLevel.Items.Count > 0) _cboTopLevel.SelectedIndex = 0;
            levelStack.Children.Add(_cboTopLevel);

            _chkAutoDetectSizes = new CheckBox
            {
                Content = "Auto-detect sizes from geometry",
                IsChecked = true, Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
            };
            levelStack.Children.Add(_chkAutoDetectSizes);

            // Repeat to other levels
            levelStack.Children.Add(new TextBlock
            {
                Text = "REPEAT TO OTHER LEVELS",
                FontWeight = FontWeights.Bold, FontSize = 10,
                Foreground = DarkBlue, Margin = new Thickness(0, 12, 0, 4),
            });
            levelStack.Children.Add(BuildRepeatLevelsDropdown(levels));
            _chkColumnsContinuousCad = new CheckBox
            {
                Content = "Columns continuous through repeat levels",
                Margin = new Thickness(0, 0, 0, 0), FontSize = 10,
                ToolTip = "OFF (default): a fresh column is created at each repeat level " +
                    "(stacked, NOT analytically continuous in Revit's analytical model). " +
                    "ON: only a single column is created on the base level, sized tall " +
                    "enough to span every repeat level. Neither produces a truly continuous " +
                    "analytical column — for that, set Top Constraint = top level on each " +
                    "column manually after import.",
            };
            levelStack.Children.Add(_chkColumnsContinuousCad);

            // Phase-140 note: column heights now derived from level-to-level spacing
            // when repeat levels are configured.
            levelStack.Children.Add(new TextBlock
            {
                Text = "Note: column heights at repeat levels are derived from " +
                    "level-to-level spacing, not the BEAM/COLUMN Height field.",
                FontStyle = FontStyles.Italic, FontSize = 9,
                Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });

            Grid.SetColumn(levelStack, 0);
            mainGrid.Children.Add(levelStack);

            // Middle column: Column & Beam
            var colBeamStack = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };

            // Column badge
            var colBadge = new Border
            {
                Background = AccentOrange, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
            };
            colBadge.Child = new TextBlock
            {
                Text = "COLUMN", FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, FontSize = 11,
            };
            colBeamStack.Children.Add(colBadge);

            var colRow = MakeDimRow("Height (mm):", "3000", out _txtColHeight);
            colBeamStack.Children.Add(colRow);

            // Beam badge
            var beamBadge = new Border
            {
                Background = AccentOrange, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 6),
            };
            beamBadge.Child = new TextBlock
            {
                Text = "BEAM", FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, FontSize = 11,
            };
            colBeamStack.Children.Add(beamBadge);

            colBeamStack.Children.Add(MakeDimRow("Depth (mm):", "450", out _txtBeamDepth));
            colBeamStack.Children.Add(MakeDimRow("Width (mm):", "250", out _txtBeamWidth));

            Grid.SetColumn(colBeamStack, 1);
            mainGrid.Children.Add(colBeamStack);

            // Right column: Wall, Slab, Foundation
            var wallSlabStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };

            wallSlabStack.Children.Add(MakeDimRow2("Wall:", "Height:", "3000", out _txtWallHeight, "Thick:", "200", out _txtWallThick));
            _chkStructuralWall = new CheckBox
            {
                Content = "Wall is structural (unchecked = architectural)",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Controls whether detected walls are created as " +
                    "Structural Walls (load-bearing) or Architectural Walls.",
            };
            wallSlabStack.Children.Add(_chkStructuralWall);
            wallSlabStack.Children.Add(MakeDimRow("Slab Thick:", "200", out _txtSlabThick));
            wallSlabStack.Children.Add(MakeDimRow("Fdn Depth:", "600", out _txtFdnDepth));

            Grid.SetColumn(wallSlabStack, 2);
            mainGrid.Children.Add(wallSlabStack);

            stack.Children.Add(mainGrid);
            return section;
        }

        // ── Section 4: Construction Logic & Tagging ──────────────────────

        private FrameworkElement BuildSection4_ConstructionLogic()
        {
            var section = MakeSection("CONSTRUCTION LOGIC & TAGGING");
            var stack = (StackPanel)((Border)section).Child;

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Construction Relationships
            var leftStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            leftStack.Children.Add(new TextBlock
            {
                Text = "CONSTRUCTION RELATIONSHIPS", FontWeight = FontWeights.Bold,
                FontSize = 11, Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 6),
            });

            _chkBeamsOnWalls = new CheckBox
            {
                Content = "Beams rest on top of walls", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = GreenFg,
            };
            _chkBeamsConnectSlabs = new CheckBox
            {
                Content = "Beams connect to slabs", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = GreenFg,
            };
            _chkColumnsStopAtSoffit = new CheckBox
            {
                Content = "Columns stop at slab soffit (not slab level)", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = GreenFg,
            };

            leftStack.Children.Add(_chkBeamsOnWalls);
            leftStack.Children.Add(_chkBeamsConnectSlabs);
            leftStack.Children.Add(_chkColumnsStopAtSoffit);

            leftStack.Children.Add(new TextBlock
            {
                Text = "Construction sequence: Foundation → Column → Beam → Slab",
                FontSize = 10, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0),
            });

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // Right: STING ISO 19650 Tagging
            var rightStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            rightStack.Children.Add(new TextBlock
            {
                Text = "STING ISO 19650 TAGGING", FontWeight = FontWeights.Bold,
                FontSize = 11, Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 6),
            });

            _chkAutoTag = new CheckBox
            {
                Content = "Auto-tag all created elements", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
            };
            _chkAutoSeqNumbers = new CheckBox
            {
                Content = "Auto-assign SEQ numbers per type", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
            };
            rightStack.Children.Add(_chkAutoTag);
            rightStack.Children.Add(_chkAutoSeqNumbers);

            rightStack.Children.Add(MakeLabel("Tag prefix:", -1));
            _txtTagPrefix = new TextBox { Text = "", Width = 100, Margin = new Thickness(0, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            rightStack.Children.Add(_txtTagPrefix);

            rightStack.Children.Add(MakeLabel("Numbering:", -1));
            _cboNumbering = new ComboBox { Margin = new Thickness(0, 2, 0, 4), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            _cboNumbering.Items.Add("By Level (L01-COL-0001)");
            _cboNumbering.Items.Add("By Grid (A1-COL-0001)");
            _cboNumbering.Items.Add("Sequential (COL-0001)");
            _cboNumbering.SelectedIndex = 0;
            rightStack.Children.Add(_cboNumbering);

            rightStack.Children.Add(MakeLabel("Tag family:", -1));
            _cboTagFamily = new ComboBox { Margin = new Thickness(0, 2, 0, 4), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            _cboTagFamily.Items.Add("(Default STING tags)");
            // Populate from document tag families
            try
            {
                var tagFamilies = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family.FamilyCategory?.Id.Value == (int)BuiltInCategory.OST_StructuralColumnTags
                              || fs.Family.FamilyCategory?.Id.Value == (int)BuiltInCategory.OST_StructuralFramingTags)
                    .Select(fs => fs.Family.Name)
                    .Distinct().Take(10);
                foreach (var fam in tagFamilies) _cboTagFamily.Items.Add(fam);
            }
            catch (Exception ex) { StingLog.Warn($"Tag family scan: {ex.Message}"); }
            _cboTagFamily.SelectedIndex = 0;
            rightStack.Children.Add(_cboTagFamily);

            // Tag preview
            rightStack.Children.Add(new TextBlock
            {
                Text = "Column: S-BLD1-Z01-L01-STR-SUP-COL-0001\n" +
                       "Beam:   S-BLD1-Z01-L01-STR-SUP-BM-0001",
                FontSize = 10, Foreground = AccentOrange, FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 4, 0, 0),
            });

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            stack.Children.Add(grid);
            return section;
        }

        // ── Section 4a: Per-element size detection + type creation ─────
        // Phase 97. For WALLS, COLUMNS, BEAMS, FOUNDATIONS, SLABS — when
        // "Detect sizes from DWG" is on, the pipeline reads the actual
        // measured dimensions out of the DWG (parallel-pair gap for walls
        // and beams; rectangle / block bounding box for columns and
        // foundations). When "Create new Revit types to match" is on AND
        // no existing type matches within tolerance, the closest family
        // type is duplicated and resized (e.g. "RC Column 325×275") so the
        // Revit model geometrically matches the DWG. When either flag is
        // off, the wizard's fixed default dimension is used instead and
        // the closest existing type is reused as-is.

        private FrameworkElement BuildSectionPerElementSizing()
        {
            var section = MakeSection("PER-ELEMENT SIZE DETECTION & TYPE CREATION");
            var stack = (StackPanel)((Border)section).Child;

            // Legend row
            stack.Children.Add(new TextBlock
            {
                Text = "  ① Detect sizes from DWG   │   ② Create new Revit types to match",
                FontSize = 10, FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6),
            });

            // 5-row grid: Element | Detect | Create | Fallback dims
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });  // label
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // detect chk
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // create chk
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // notes
            for (int i = 0; i < 6; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header row
            AddSizingHeader(grid, 0);

            // Walls
            AddSizingRow(grid, 1, "Walls",
                out _chkDetectSizes_Wall, out _chkCreateTypes_Wall,
                "Thickness from parallel-line gap; duplicates wall type to match compound structure.");

            // Columns
            AddSizingRow(grid, 2, "Columns",
                out _chkDetectSizes_Column, out _chkCreateTypes_Column,
                "Width×Depth from rectangle bbox (rect columns) or diameter (round columns).");

            // Beams
            AddSizingRow(grid, 3, "Beams",
                out _chkDetectSizes_Beam, out _chkCreateTypes_Beam,
                "Width from parallel-pair gap; depth from wizard default (DWG plans rarely show beam depth).");

            // Foundations
            AddSizingRow(grid, 4, "Foundations",
                out _chkDetectSizes_Foundation, out _chkCreateTypes_Foundation,
                "Plan size from column bbox × 1.5× oversize* or parsed from block name (e.g. 'PAD 1500x1500').");

            // Slabs
            AddSizingRow(grid, 5, "Slabs",
                out _chkDetectSizes_Slab, out _chkCreateTypes_Slab,
                "Thickness uses wizard default (flat DWG plans carry no thickness); duplicates floor type to match.");

            stack.Children.Add(grid);

            // Fallback defaults row — only shown for elements where the
            // "Detect sizes" flag might be off. Columns and Foundations are
            // the only ones whose fallback isn't already exposed in Section 3.
            var fallbackLabel = new TextBlock
            {
                Text = "Fallback defaults (used when Detect is off):",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4),
            };
            stack.Children.Add(fallbackLabel);

            var fallbackGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 6; i++)
                fallbackGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddFallback(fallbackGrid, 0, "Column W (mm):", out _txtColWidthFallback, "300");
            AddFallback(fallbackGrid, 2, "Column D (mm):", out _txtColDepthFallback, "300");
            AddFallback(fallbackGrid, 4, "Foundation W (mm):", out _txtFdnWidthFallback, "1200");
            stack.Children.Add(fallbackGrid);

            // EC7 disclaimer footer
            stack.Children.Add(new TextBlock
            {
                Text = "* The 1.5× pad oversize is a heuristic for first-pass automation, " +
                    "not a code-compliant sizing rule. EC7 §6.5 covers verification " +
                    "against sliding for spread foundations — bearing capacity and " +
                    "settlement still require structural-engineer review against soil " +
                    "class, load combinations, and serviceability checks.",
                FontStyle = FontStyles.Italic, FontSize = 9,
                Foreground = Brushes.DarkGray, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });

            return section;
        }

        private void AddSizingHeader(Grid grid, int row)
        {
            var h1 = new TextBlock
            {
                Text = "Element", FontWeight = FontWeights.Bold, FontSize = 11,
                Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(h1, row); Grid.SetColumn(h1, 0); grid.Children.Add(h1);

            var h2 = new TextBlock
            {
                Text = "①", FontWeight = FontWeights.Bold, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Detect sizes from DWG",
            };
            Grid.SetRow(h2, row); Grid.SetColumn(h2, 1); grid.Children.Add(h2);

            var h3 = new TextBlock
            {
                Text = "②", FontWeight = FontWeights.Bold, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Create new Revit types to match detected sizes",
            };
            Grid.SetRow(h3, row); Grid.SetColumn(h3, 2); grid.Children.Add(h3);

            var h4 = new TextBlock
            {
                Text = "Notes", FontWeight = FontWeights.Bold, FontSize = 11,
                Foreground = DarkBlue, Margin = new Thickness(8, 0, 0, 4),
            };
            Grid.SetRow(h4, row); Grid.SetColumn(h4, 3); grid.Children.Add(h4);
        }

        private void AddSizingRow(Grid grid, int row, string label,
            out CheckBox chkDetect, out CheckBox chkCreate, string notes)
        {
            var lbl = new TextBlock
            {
                Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2),
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);

            chkDetect = new CheckBox
            {
                IsChecked = true, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = $"Detect {label.ToLower()} sizes from DWG geometry. When off, use wizard defaults.",
            };
            Grid.SetRow(chkDetect, row); Grid.SetColumn(chkDetect, 1); grid.Children.Add(chkDetect);

            chkCreate = new CheckBox
            {
                IsChecked = true, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = $"Create new Revit types for detected {label.ToLower()} sizes. When off, reuse the closest existing type.",
            };
            Grid.SetRow(chkCreate, row); Grid.SetColumn(chkCreate, 2); grid.Children.Add(chkCreate);

            var notesText = new TextBlock
            {
                Text = notes, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(8, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(notesText, row); Grid.SetColumn(notesText, 3); grid.Children.Add(notesText);
        }

        private void AddFallback(Grid grid, int col, string label, out TextBox tb, string defaultVal)
        {
            var lbl = new TextBlock
            {
                Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 4, 2),
            };
            Grid.SetRow(lbl, 0); Grid.SetColumn(lbl, col); grid.Children.Add(lbl);

            tb = new TextBox
            {
                Text = defaultVal, Width = 70, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
            };
            Grid.SetRow(tb, 0); Grid.SetColumn(tb, col + 1); grid.Children.Add(tb);
        }

        // ── Section 4b: EaseBit-style detection knobs ──────────────────
        // Phase 78. Surfaces the EaseBit-inspired config knobs that were
        // previously only editable via project_config.json or code. The row
        // layout mirrors Section 3 so the wizard reads consistently.

        private FrameworkElement BuildSectionEaseBitDetection()
        {
            var section = MakeSection("DETECTION");
            var stack = (StackPanel)((Border)section).Child;

            // Top row: three toggle checkboxes (Dry-Run, Explode, Detect Openings)
            var toggleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 6),
            };
            _chkDryRun = new CheckBox
            {
                Content = "Dry-run (report only, no elements created)",
                IsChecked = false, Margin = new Thickness(0, 0, 16, 0), FontSize = 11,
                ToolTip = "Runs the extraction + detection passes and reports the counts " +
                    "without creating any Revit elements. Lets you sanity-check layer " +
                    "mapping before committing to the full run.",
            };
            _chkExplodeOnImport = new CheckBox
            {
                Content = "Explode DWG blocks before extraction",
                IsChecked = false, Margin = new Thickness(0, 0, 16, 0), FontSize = 11,
                ToolTip = "Fully explodes nested DWG block references so geometry " +
                    "hidden inside blocks surfaces onto its host layer. Useful when " +
                    "the source DWG nests walls/columns inside xref blocks.",
            };
            _chkDetectOpenings = new CheckBox
            {
                Content = "Detect door/window openings in walls",
                IsChecked = false, Margin = new Thickness(0, 0, 16, 0), FontSize = 11,
                ToolTip = "Scans the DWG for door/window/opening blocks that fall on " +
                    "created walls and cuts rectangular voids through those walls.",
            };
            _chkUseSpatialIndex = new CheckBox
            {
                Content = "Use spatial index (faster on >500 lines)",
                IsChecked = true, FontSize = 11,
                ToolTip = "Uses a uniform grid spatial index instead of O(n²) nested " +
                    "loops for parallel-pair detection. Always safe to leave on.",
            };
            toggleRow.Children.Add(_chkDryRun);
            toggleRow.Children.Add(_chkExplodeOnImport);
            toggleRow.Children.Add(_chkDetectOpenings);
            toggleRow.Children.Add(_chkUseSpatialIndex);
            stack.Children.Add(toggleRow);

            // Knob grid — two columns of label/value pairs.
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddKnobRow(grid, 0, 0, "Min wall thickness (mm):", out _txtMinWallThickness, "50",
                "Parallel line pairs closer than this are treated as false positives " +
                "(dimension chains, construction lines). Pairs below this distance " +
                "are counted in the rejected total on the result.");
            AddKnobRow(grid, 0, 2, "Max wall thickness (mm):", out _txtMaxWallThickness, "500",
                "Parallel line pairs farther than this are never treated as a single " +
                "wall — prevents accidental pairing across a corridor.");

            AddKnobRow(grid, 1, 0, "Parallelism tolerance (cos θ):", out _txtParallelDot, "0.98",
                "Dot-product threshold for the parallel-line check. " +
                "0.98 ≈ allows up to 11° skew, 0.995 ≈ 5.7° skew, 1.0 = exact " +
                "parallel only. Lower values accept more lines as 'parallel'.");
            AddKnobRow(grid, 1, 2, "Parallel pair max gap (mm):", out _txtParallelGap, "500",
                "Global hard cap on the perpendicular distance between two parallel lines " +
                "that the spatial index will consider as a pair. Applies to ALL pair " +
                "detection (walls, beams). NOT a longitudinal end-to-end gap — see " +
                "'Endpoint gap' for that. Distinct from 'Max wall thickness' which is " +
                "wall-specific.");

            AddKnobRow(grid, 2, 0, "Line snap tolerance (mm):", out _txtEndpointTol, "5",
                "Two line endpoints within this distance are treated as the same " +
                "vertex for merge/join operations. Used during topology cleanup.");
            AddKnobRow(grid, 2, 2, "Endpoint gap bridge (mm):", out _txtEndpointGap, "50",
                "Wall continuity recovery — when two parallel-pair lines have endpoints " +
                "within this longitudinal gap, the shorter line is virtually extended " +
                "to close the gap before the centreline is computed. Recovers walls " +
                "where the draughtsperson left a small gap at corners. Set to 0 to disable.");

            AddKnobRow(grid, 3, 0, "Min opening width (mm):", out _txtMinOpeningWidth, "400",
                "Minimum gap along a wall to count as an opening. Smaller gaps are " +
                "ignored.");
            AddKnobRow(grid, 3, 2, "Max opening width (mm):", out _txtMaxOpeningWidth, "3000",
                "Maximum gap along a wall to count as an opening. Larger gaps are " +
                "assumed to be two separate wall segments rather than one wall with a hole.");

            stack.Children.Add(grid);

            // ── Phase-140 accuracy controls ──
            var p140Header = new TextBlock
            {
                Text = "ACCURACY (Phase-140)",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 10, 0, 4),
            };
            stack.Children.Add(p140Header);

            // Toggle row: skip duplicates, trim beams to columns, show warnings, grid label marks
            var p140ToggleRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            _chkSkipDuplicates = new CheckBox
            {
                Content = "Skip duplicates",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Before creating each detected element, check for an existing " +
                    "Revit element of the same category within Duplicate-check tolerance. " +
                    "Skips creation if found, preventing pile-up on re-import.",
            };
            _chkTrimBeamsToColumns = new CheckBox
            {
                Content = "Trim beams to column faces",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "After column placement, move each beam endpoint that lands on a " +
                    "column from the column centroid to the column face (+25 mm cover). " +
                    "Produces correct connection geometry; analytical model offsets work.",
            };
            _chkShowWarningsInView = new CheckBox
            {
                Content = "Show structural warnings in view",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "After post-creation load-path analysis, places TextNote markers " +
                    "(prefix '⚠ STING-STRUCT:') in the active view at each warning. Wraps " +
                    "in its own sub-transaction so warnings don't roll back the conversion.",
            };
            _chkUseGridLabelMarks = new CheckBox
            {
                Content = "Use grid labels as column marks",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "When grid lines are detected and a column snaps to a grid " +
                    "intersection, set Mark to '{vert}/{horiz}' (e.g. 'A/1'). Non-snapped " +
                    "columns fall through to sequential numbering.",
            };
            _chkUseSpanToDepth = new CheckBox
            {
                Content = "Span-proportional beam depth",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Derives each beam's depth from span / span-to-depth ratio, " +
                    "clamped to [Beam depth min, Beam depth max], rounded to nearest 25 mm, " +
                    "floored at the wizard's BEAM Depth value. When off, every beam uses " +
                    "the wizard BEAM Depth value.",
            };
            p140ToggleRow.Children.Add(_chkSkipDuplicates);
            p140ToggleRow.Children.Add(_chkTrimBeamsToColumns);
            p140ToggleRow.Children.Add(_chkShowWarningsInView);
            p140ToggleRow.Children.Add(_chkUseGridLabelMarks);
            p140ToggleRow.Children.Add(_chkUseSpanToDepth);
            stack.Children.Add(p140ToggleRow);

            // Knob grid (Phase-140) — span-to-depth, beam clamps, grid snap, duplicate tol
            var p140Grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 4; i++)
                p140Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++)
                p140Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddKnobRow(p140Grid, 0, 0, "Span/depth ratio:", out _txtSpanToDepth, "15.0",
                "Beam depth = span / this ratio. 15 ≈ concrete framing; 20-25 for steel " +
                "I-sections. Only used when 'Span-proportional beam depth' is checked.");
            AddKnobRow(p140Grid, 0, 2, "Grid snap tol (mm):", out _txtGridSnapTol, "100",
                "Column centres within this distance of a detected grid intersection are " +
                "moved to the intersection. Set to 0 to disable grid snapping.");

            AddKnobRow(p140Grid, 1, 0, "Beam depth min (mm):", out _txtBeamDepthMin, "250",
                "Lower clamp on derived beam depth. Practical minimum for a structural beam.");
            AddKnobRow(p140Grid, 1, 2, "Beam depth max (mm):", out _txtBeamDepthMax, "1200",
                "Upper clamp on derived beam depth.");

            AddKnobRow(p140Grid, 2, 0, "Duplicate-check tol (mm):", out _txtDuplicateTol, "50",
                "Treat a detected element as a duplicate of an existing element when " +
                "their reference points are within this distance. Only applied when " +
                "'Skip duplicates' is on.");

            stack.Children.Add(p140Grid);

            // ── Phase-142 detection knobs ───────────────────────────────
            var p142Header = new TextBlock
            {
                Text = "ACCURACY (Phase-142)",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 10, 0, 4),
            };
            stack.Children.Add(p142Header);

            // Toggle row
            var p142ToggleRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            _chkDetectStripFoundations = new CheckBox
            {
                Content = "Detect strip foundations under walls",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Synthesise strip foundations along every detected wall " +
                    "centreline. Foundation extends past the wall by 'Strip oversize' " +
                    "per side. Created as structural Floors at the base level.",
            };
            _chkMarkCantilevers = new CheckBox
            {
                Content = "Mark cantilever / free beams in Comments",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Stamp the Comments parameter with 'STING: Cantilever (start)' / " +
                    "'(end)' / 'STING: Free beam (no support)' for beams that lack " +
                    "column or wall support at one or both ends. Lets you find them in " +
                    "schedules.",
            };
            p142ToggleRow.Children.Add(_chkDetectStripFoundations);
            p142ToggleRow.Children.Add(_chkMarkCantilevers);
            stack.Children.Add(p142ToggleRow);

            // Knob grid
            var p142Grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 4; i++)
                p142Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++)
                p142Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddKnobRow(p142Grid, 0, 0, "Min column diameter (mm):", out _txtMinColDiam, "150",
                "Lower bound for DetectCircularColumns. Circles smaller than this are " +
                "rejected (could be holes, drilled penetrations, dimension circles).");
            AddKnobRow(p142Grid, 0, 2, "Max column diameter (mm):", out _txtMaxColDiam, "1500",
                "Upper bound for DetectCircularColumns. Circles larger than this are " +
                "rejected (could be tank outlines, equipment).");

            AddKnobRow(p142Grid, 1, 0, "Beam overlap ratio:", out _txtOverlapRatio, "0.5",
                "Minimum longitudinal overlap as a fraction of the shorter line. 0.5 = 50% " +
                "(legacy default); lower values pair short connection stubs and diagonal " +
                "bracing that the legacy detector misses. Walls use ratio - 0.1.");
            AddKnobRow(p142Grid, 1, 2, "Strip oversize (mm/side):", out _txtStripOversize, "150",
                "Strip foundation extends this distance beyond each face of the wall it " +
                "supports. Engineering rule of thumb; verify against soil class.");

            AddKnobRow(p142Grid, 2, 0, "Beam-support tol (mm):", out _txtBeamSupportTol, "200",
                "Beam endpoints within this distance of a column footprint or wall " +
                "centreline are classified as supported; outside, as cantilever / free.");

            stack.Children.Add(p142Grid);

            // ── Phase-143 post-processing toggles ───────────────────────
            var p143Header = new TextBlock
            {
                Text = "POST-PROCESSING (Phase-143)",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 10, 0, 4),
            };
            stack.Children.Add(p143Header);

            var p143ToggleRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            _chkSeedRoomsFromSlabs = new CheckBox
            {
                Content = "Seed rooms from slab centroids",
                IsChecked = false, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "After slab creation, place a Revit Room at each slab centroid " +
                    "(skipping points inside a slab void). Skips points already inside an " +
                    "existing room. Default OFF — enable for greenfield projects.",
            };
            _chkCreateStructuralViews = new CheckBox
            {
                Content = "Create structural views after conversion",
                IsChecked = false, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "After conversion, create one StructuralPlan ViewPlan per level " +
                    "that received elements. Looks up the corporate 'S-PLAN' DrawingType " +
                    "via the Phase-113 registry and applies it. Default OFF.",
            };
            _chkInferBeamMaterial = new CheckBox
            {
                Content = "Infer beam material (steel / concrete)",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Heuristically classify each detected beam as steel I-section " +
                    "(single centreline) or concrete rectangle (parallel pair). Adds " +
                    "STING:Material= suffix to LayerName for type-matching downstream.",
            };
            _chkClassifyFoundations = new CheckBox
            {
                Content = "Classify foundations (pad / raft / pile cap)",
                IsChecked = true, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Split detected rectangular foundations into Pad / Raft / PileCap " +
                    "based on plan area + clustering. Rafts route to slab creation " +
                    "(structural floor); pads + pile caps stay as pad foundations.",
            };
            _chkStampJunctionMarks = new CheckBox
            {
                Content = "Stamp junction marks on participating elements",
                IsChecked = false, Margin = new Thickness(0, 0, 14, 4), FontSize = 11,
                ToolTip = "Append 'J:T' / 'J:L' / 'J:X' / 'J:S' (T/L/Cross/Splice) to the " +
                    "Mark of every column and beam that participates in a junction. " +
                    "Lets you find junction participants via schedule filters. Default OFF.",
            };
            p143ToggleRow.Children.Add(_chkSeedRoomsFromSlabs);
            p143ToggleRow.Children.Add(_chkCreateStructuralViews);
            p143ToggleRow.Children.Add(_chkInferBeamMaterial);
            p143ToggleRow.Children.Add(_chkClassifyFoundations);
            p143ToggleRow.Children.Add(_chkStampJunctionMarks);
            stack.Children.Add(p143ToggleRow);

            // Knob grid (Phase-143)
            var p143Grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 4; i++)
                p143Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 1; i++)
                p143Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddKnobRow(p143Grid, 0, 0, "Raft min area (m²):", out _txtRaftMinArea, "4.0",
                "Foundation rectangles with plan area ≥ this are classified as rafts " +
                "instead of isolated pads. Default 4 m² (≈ 2 m × 2 m).");
            AddKnobRow(p143Grid, 0, 2, "Room label search (mm):", out _txtRoomLabelRadius, "3000",
                "Maximum distance from a slab centroid that a layer-text label can sit " +
                "and still be used as the seeded room's Name.");
            stack.Children.Add(p143Grid);

            return section;
        }

        /// <summary>Inserts a labelled TextBox into the knob grid at (row, col).</summary>
        private void AddKnobRow(Grid grid, int row, int col, string label,
            out TextBox tb, string defaultVal, string tooltip)
        {
            var lbl = new TextBlock
            {
                Text = label, FontSize = 11, Margin = new Thickness(0, 3, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip,
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, col);
            grid.Children.Add(lbl);

            tb = new TextBox
            {
                Text = defaultVal, Width = 80, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = tooltip,
            };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col + 1);
            grid.Children.Add(tb);
        }

        // ── Section 5: Smart Numbering ──────────────────────────────────

        private FrameworkElement BuildSection5_Numbering()
        {
            var section = MakeSection("ELEMENT NUMBERING");
            var stack = (StackPanel)((Border)section).Child;

            // Top row: Category + Parameter
            var topRow = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var catStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            catStack.Children.Add(MakeLabel("Category:", -1));
            _cboNumCategory = new ComboBox { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var (name, _) in NumberingCategories)
                _cboNumCategory.Items.Add(name);
            _cboNumCategory.SelectedIndex = 0;
            _cboNumCategory.SelectionChanged += (s, e) =>
            {
                // Phase-140 P1-E: snapshot the previous category's UI state and
                // restore the newly-selected category's state (if previously edited).
                if (_chkPerCategoryNumbering?.IsChecked == true)
                {
                    int prev = _activeNumCatIndex;
                    if (prev >= 0 && prev < NumberingCategories.Length)
                        _numConfigByCatIndex[prev] = SnapshotCurrentNumberingUI();
                    int next = _cboNumCategory.SelectedIndex;
                    _activeNumCatIndex = next;
                    if (next >= 0 && _numConfigByCatIndex.TryGetValue(next, out var nextCfg))
                        ApplyNumberingUIFrom(nextCfg);
                    else if (next >= 0 && next < NumberingCategories.Length)
                        ApplyNumberingUIFrom(DefaultPerCategoryConfig(NumberingCategories[next].Cat));
                }
                UpdateNumberingPreview();
            };
            catStack.Children.Add(_cboNumCategory);
            Grid.SetColumn(catStack, 0);
            topRow.Children.Add(catStack);

            var paramStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            paramStack.Children.Add(MakeLabel("Parameter:", -1));
            _cboNumParameter = new ComboBox { Margin = new Thickness(0, 2, 0, 2) };
            _cboNumParameter.Items.Add("Mark");
            _cboNumParameter.Items.Add("Comments");
            _cboNumParameter.Items.Add("ASS_SEQ_NUM_TXT");
            _cboNumParameter.Items.Add("ASS_TAG_1");
            _cboNumParameter.SelectedIndex = 0;
            paramStack.Children.Add(_cboNumParameter);
            Grid.SetColumn(paramStack, 1);
            topRow.Children.Add(paramStack);

            stack.Children.Add(topRow);

            // Template for numbering
            stack.Children.Add(new TextBlock
            {
                Text = "Template for numbering", FontWeight = FontWeights.SemiBold,
                FontSize = 11, Margin = new Thickness(0, 4, 0, 4),
            });

            var templateGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            // 7 columns: Text | Separator | GroupEnum | Text | ElementEnum | Text | Suffix
            for (int i = 0; i < 7; i++)
                templateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int c = 0;

            // Prefix text
            var prefixStack = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            prefixStack.Children.Add(new TextBlock { Text = "Text", FontSize = 9, Foreground = Brushes.Gray });
            _txtNumPrefix = new TextBox { Text = "GFC", Width = 60, Margin = new Thickness(0, 2, 0, 0) };
            _txtNumPrefix.TextChanged += (s, e) => UpdateNumberingPreview();
            prefixStack.Children.Add(_txtNumPrefix);
            Grid.SetColumn(prefixStack, c++);
            templateGrid.Children.Add(prefixStack);

            // Separator
            var sepStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            sepStack.Children.Add(new TextBlock { Text = "Sep", FontSize = 9, Foreground = Brushes.Gray });
            _txtNumSeparator = new TextBox { Text = "-", Width = 30, Margin = new Thickness(0, 2, 0, 0) };
            _txtNumSeparator.TextChanged += (s, e) => UpdateNumberingPreview();
            sepStack.Children.Add(_txtNumSeparator);
            Grid.SetColumn(sepStack, c++);
            templateGrid.Children.Add(sepStack);

            // Group enumeration
            var groupStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            _chkGroupEnum = new CheckBox
            {
                Content = "Enumeration for groups", FontSize = 10,
                Margin = new Thickness(0, 0, 0, 2),
            };
            _chkGroupEnum.Checked += (s, e) => UpdateNumberingPreview();
            _chkGroupEnum.Unchecked += (s, e) => UpdateNumberingPreview();
            groupStack.Children.Add(_chkGroupEnum);
            _cboGroupStyle = new ComboBox { Width = 160, Margin = new Thickness(0, 2, 0, 0) };
            _cboGroupStyle.Items.Add("Capital Letters (A, B, C...)");
            _cboGroupStyle.Items.Add("Numeric (1, 2, 3...)");
            _cboGroupStyle.Items.Add("Lower Letters (a, b, c...)");
            _cboGroupStyle.Items.Add("Capital Romans (I, II, III...)");
            _cboGroupStyle.Items.Add("Lower Romans (i, ii, iii...)");
            _cboGroupStyle.SelectedIndex = 0;
            _cboGroupStyle.SelectionChanged += (s, e) => UpdateNumberingPreview();
            groupStack.Children.Add(_cboGroupStyle);
            Grid.SetColumn(groupStack, c++);
            templateGrid.Children.Add(groupStack);

            // Spacer
            var spacer1 = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            spacer1.Children.Add(new TextBlock { Text = "Text", FontSize = 9, Foreground = Brushes.Gray });
            spacer1.Children.Add(new TextBox { Text = "", Width = 30, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(spacer1, c++);
            templateGrid.Children.Add(spacer1);

            // Element enumeration
            var elemStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            _chkElementEnum = new CheckBox
            {
                Content = "Enumeration for elements", FontSize = 10, IsChecked = true,
                Margin = new Thickness(0, 0, 0, 2),
            };
            _chkElementEnum.Checked += (s, e) => UpdateNumberingPreview();
            _chkElementEnum.Unchecked += (s, e) => UpdateNumberingPreview();
            elemStack.Children.Add(_chkElementEnum);
            _cboElementStyle = new ComboBox { Width = 160, Margin = new Thickness(0, 2, 0, 0) };
            _cboElementStyle.Items.Add("Numeric (1, 2, 3...)");
            _cboElementStyle.Items.Add("Capital Letters (A, B, C...)");
            _cboElementStyle.Items.Add("Lower Letters (a, b, c...)");
            _cboElementStyle.Items.Add("Capital Romans (I, II, III...)");
            _cboElementStyle.Items.Add("Lower Romans (i, ii, iii...)");
            _cboElementStyle.SelectedIndex = 0;
            _cboElementStyle.SelectionChanged += (s, e) => UpdateNumberingPreview();
            elemStack.Children.Add(_cboElementStyle);

            // Start from, Digits, Increment
            var numSettingsGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            numSettingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            numSettingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            numSettingsGrid.RowDefinitions.Add(new RowDefinition());
            numSettingsGrid.RowDefinitions.Add(new RowDefinition());
            numSettingsGrid.RowDefinitions.Add(new RowDefinition());

            numSettingsGrid.Children.Add(MakeGridLabel("Start from:", 0, 0));
            _txtStartFrom = new TextBox { Text = "1", Margin = new Thickness(4, 1, 0, 1) };
            _txtStartFrom.TextChanged += (s, e) => UpdateNumberingPreview();
            Grid.SetRow(_txtStartFrom, 0); Grid.SetColumn(_txtStartFrom, 1);
            numSettingsGrid.Children.Add(_txtStartFrom);

            numSettingsGrid.Children.Add(MakeGridLabel("Number of digits:", 1, 0));
            _txtDigits = new TextBox { Text = "2", Margin = new Thickness(4, 1, 0, 1) };
            _txtDigits.TextChanged += (s, e) => UpdateNumberingPreview();
            Grid.SetRow(_txtDigits, 1); Grid.SetColumn(_txtDigits, 1);
            numSettingsGrid.Children.Add(_txtDigits);

            numSettingsGrid.Children.Add(MakeGridLabel("Increment by:", 2, 0));
            _txtIncrement = new TextBox { Text = "1", Margin = new Thickness(4, 1, 0, 1) };
            _txtIncrement.TextChanged += (s, e) => UpdateNumberingPreview();
            Grid.SetRow(_txtIncrement, 2); Grid.SetColumn(_txtIncrement, 1);
            numSettingsGrid.Children.Add(_txtIncrement);

            elemStack.Children.Add(numSettingsGrid);
            Grid.SetColumn(elemStack, c++);
            templateGrid.Children.Add(elemStack);

            stack.Children.Add(templateGrid);

            // Preview
            stack.Children.Add(new TextBlock
            {
                Text = "Preview of numbering", FontWeight = FontWeights.SemiBold,
                FontSize = 11, Margin = new Thickness(0, 8, 0, 4),
            });
            _txtPreview = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"), FontSize = 12,
                Foreground = DarkBlue, Margin = new Thickness(0, 0, 0, 4),
            };
            stack.Children.Add(_txtPreview);

            // Options
            _chkOmitAlreadyNumbered = new CheckBox
            {
                Content = "Omit elements which already have assigned value to the chosen parameter",
                FontSize = 10, Margin = new Thickness(0, 8, 0, 4),
            };
            stack.Children.Add(_chkOmitAlreadyNumbered);

            // Phase-140 P1-E: per-category numbering toggle
            _chkPerCategoryNumbering = new CheckBox
            {
                Content = "Number every structural category independently (Phase-140)",
                FontSize = 10, Margin = new Thickness(0, 4, 0, 4),
                IsChecked = true,
                ToolTip = "When ON, every structural category (Columns, Framing, Walls, Floors, " +
                    "Foundations) is numbered with its own prefix and counter. Switch the " +
                    "Category dropdown to configure each one — your edits are remembered. " +
                    "Defaults: COL-, BM-, W-, SL-, FDN-. When OFF, only the visible category " +
                    "is numbered.",
            };
            stack.Children.Add(_chkPerCategoryNumbering);

            stack.Children.Add(new TextBlock
            {
                Text = "Tip: with the toggle on, switch the Category dropdown above to set up " +
                    "each structural category independently. The ELEMENT NUMBERING template " +
                    "shown is for the active category only.",
                FontStyle = FontStyles.Italic, FontSize = 9, Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
            });

            // Initialise the active-category index to whatever the dropdown shows now.
            _activeNumCatIndex = _cboNumCategory?.SelectedIndex ?? 0;

            UpdateNumberingPreview();
            return section;
        }

        // ── Phase-140 P1-E helpers ────────────────────────────────────────

        /// <summary>Snapshot the visible numbering UI into a NumberingConfig.</summary>
        private NumberingEngine.NumberingConfig SnapshotCurrentNumberingUI()
        {
            return BuildNumberingConfig();
        }

        /// <summary>Restore the visible numbering UI from a NumberingConfig snapshot.</summary>
        private void ApplyNumberingUIFrom(NumberingEngine.NumberingConfig cfg)
        {
            if (cfg == null) return;
            if (_txtNumPrefix != null) _txtNumPrefix.Text = cfg.Prefix ?? "";
            if (_txtNumSeparator != null) _txtNumSeparator.Text = cfg.Separator ?? "-";
            if (_chkGroupEnum != null) _chkGroupEnum.IsChecked = cfg.UseGroupEnum;
            if (_chkElementEnum != null) _chkElementEnum.IsChecked = cfg.UseElementEnum;
            if (_txtStartFrom != null) _txtStartFrom.Text = cfg.StartFrom.ToString();
            if (_txtDigits != null) _txtDigits.Text = cfg.NumberOfDigits.ToString();
            if (_txtIncrement != null) _txtIncrement.Text = cfg.IncrementBy.ToString();
            if (_chkOmitAlreadyNumbered != null) _chkOmitAlreadyNumbered.IsChecked = cfg.OmitAlreadyNumbered;
            // Note: ParameterName/Category dropdowns aren't restored here because
            // Category is what just changed and ParameterName defaults to "Mark" for
            // every structural category in our default configs.
        }

        /// <summary>Reasonable default NumberingConfig per structural category.</summary>
        private static NumberingEngine.NumberingConfig DefaultPerCategoryConfig(BuiltInCategory cat)
        {
            string prefix = cat switch
            {
                BuiltInCategory.OST_StructuralColumns    => "COL",
                BuiltInCategory.OST_StructuralFraming    => "BM",
                BuiltInCategory.OST_Walls                => "W",
                BuiltInCategory.OST_Floors               => "SL",
                BuiltInCategory.OST_StructuralFoundation => "FDN",
                BuiltInCategory.OST_Doors                => "DR",
                BuiltInCategory.OST_Windows              => "WIN",
                BuiltInCategory.OST_Rooms                => "RM",
                BuiltInCategory.OST_Sheets               => "S",
                BuiltInCategory.OST_Views                => "V",
                _                                        => "EL",
            };
            return new NumberingEngine.NumberingConfig
            {
                Category = cat,
                ParameterName = "Mark",
                Prefix = prefix,
                Separator = "-",
                UseGroupEnum = false,
                UseElementEnum = true,
                StartFrom = 1,
                NumberOfDigits = 3,
                IncrementBy = 1,
                ElementStyle = NumberingEngine.EnumStyle.Numeric,
                Grouping = NumberingEngine.GroupingAlgorithm.ByLevel,
            };
        }

        // ── Event Handlers ───────────────────────────────────────────────

        private void OnAnalyzeLayers(object sender, RoutedEventArgs e)
        {
            if (_selectedImport == null)
            {
                _statusBar.Text = "⚠ No DWG import selected.";
                _statusBar.Foreground = Brushes.Red;
                return;
            }

            try
            {
                _statusBar.Text = "Analyzing DWG layers...";
                _statusBar.Foreground = Brushes.Gray;

                var manifest = _pipeline.ExtractLayerManifest(_selectedImport);
                _layerRows.Clear();

                // Also run extraction to get detailed entity counts
                _extraction = _pipeline.ExtractStructuralGeometry(_selectedImport);

                // Build layer row data with entity breakdown
                var layerEntityCounts = new Dictionary<string, (int lines, int arcs)>();

                // Count lines and arcs per layer from extraction
                if (_extraction != null)
                {
                    foreach (var bl in _extraction.BeamLines ?? new List<DetectedBeam>())
                    {
                        string ln = bl.LayerName ?? "";
                        if (!layerEntityCounts.ContainsKey(ln)) layerEntityCounts[ln] = (0, 0);
                        var cur = layerEntityCounts[ln];
                        layerEntityCounts[ln] = (cur.lines + 1, cur.arcs);
                    }
                    foreach (var circ in _extraction.Circles ?? new List<DetectedCircle>())
                    {
                        string ln = circ.LayerName ?? "";
                        if (!layerEntityCounts.ContainsKey(ln)) layerEntityCounts[ln] = (0, 0);
                        var cur = layerEntityCounts[ln];
                        layerEntityCounts[ln] = (cur.lines, cur.arcs + 1);
                    }
                }

                var sorted = manifest.OrderByDescending(kvp => kvp.Value.Confidence)
                    .ThenByDescending(kvp => kvp.Value.Count);

                foreach (var kvp in sorted)
                {
                    layerEntityCounts.TryGetValue(kvp.Key, out var counts);

                    string autoDetect = kvp.Value.Classification ?? "";
                    string mapTo = InferMapTo(autoDetect, kvp.Value.Confidence);

                    _layerRows.Add(new LayerRowData
                    {
                        LayerName = kvp.Key,
                        Entities = kvp.Value.Count,
                        Lines = counts.lines > 0 ? counts.lines : kvp.Value.Count,
                        Arcs = counts.arcs,
                        AutoDetect = autoDetect,
                        Confidence = kvp.Value.Confidence,
                        Selected = kvp.Value.Confidence > 0,
                        MapTo = mapTo,
                    });
                }

                // Populate Element-Layer Mapping dropdowns
                PopulateLayerMappingDropdowns();

                int structCount = _layerRows.Count(r => r.Confidence > 0);
                _statusBar.Text = $"✓ Found {_layerRows.Count} layers ({structCount} structural). " +
                    $"Scale: {_extraction?.DetectedScaleFactor:F4}";
                _statusBar.Foreground = GreenFg;
            }
            catch (Exception ex)
            {
                StingLog.Error("Layer analysis failed", ex);
                _statusBar.Text = $"✗ Analysis failed: {ex.Message}";
                _statusBar.Foreground = Brushes.Red;
            }
        }

        private string InferMapTo(string classification, double confidence)
        {
            if (confidence <= 0 || string.IsNullOrEmpty(classification)) return "";
            string lower = classification.ToLower();
            if (lower.Contains("column") || lower.Contains("col")) return "Column";
            if (lower.Contains("beam") || lower.Contains("framing") || lower.Contains("lintel")) return "Beam";
            if (lower.Contains("wall") || lower.Contains("shear")) return "Wall";
            if (lower.Contains("slab") || lower.Contains("floor")) return "Slab";
            if (lower.Contains("foundation") || lower.Contains("footing")) return "Foundation";
            if (lower.Contains("grid") || lower.Contains("axis")) return "Grid";
            if (lower.Contains("dimension") || lower.Contains("text") || lower.Contains("annotation")) return "Annotation";
            // Phase 71: Additional structural element detection
            if (lower.Contains("roof") || lower.Contains("truss")) return "Roof";
            if (lower.Contains("stair") || lower.Contains("step") || lower.Contains("flight")) return "Stair";
            if (lower.Contains("ramp") || lower.Contains("slope")) return "Ramp";
            if (lower.Contains("pad") || lower.Contains("base")) return "PadFoundation";
            if (lower.Contains("pile") || lower.Contains("bore")) return "Pile";
            if (lower.Contains("retaining") || lower.Contains("retain")) return "RetainingWall";
            if (lower.Contains("brace") || lower.Contains("bracing") || lower.Contains("diag")) return "Bracing";
            return "";
        }

        private void PopulateLayerMappingDropdowns()
        {
            var allLayers = _layerRows.Select(r => r.LayerName).ToList();

            foreach (var cbo in new[] { _cboColumnLayer, _cboBeamLayer, _cboWallLayer,
                _cboSlabLayer, _cboFoundationLayer, _cboGridLayer })
            {
                cbo.Items.Clear();
                cbo.Items.Add("(auto-detect)");
                foreach (var layer in allLayers)
                    cbo.Items.Add(layer);
                cbo.SelectedIndex = 0;
            }

            // Auto-select best matching layers
            foreach (var row in _layerRows)
            {
                switch (row.MapTo)
                {
                    case "Column":
                        SelectLayerInCombo(_cboColumnLayer, row.LayerName);
                        break;
                    case "Beam":
                        SelectLayerInCombo(_cboBeamLayer, row.LayerName);
                        break;
                    case "Wall":
                        SelectLayerInCombo(_cboWallLayer, row.LayerName);
                        break;
                    case "Slab":
                        SelectLayerInCombo(_cboSlabLayer, row.LayerName);
                        break;
                    case "Foundation":
                        SelectLayerInCombo(_cboFoundationLayer, row.LayerName);
                        break;
                    case "Grid":
                        SelectLayerInCombo(_cboGridLayer, row.LayerName);
                        break;
                }
            }
        }

        private void SelectLayerInCombo(ComboBox cbo, string layerName)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i].ToString() == layerName)
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var row in _layerRows) row.Selected = true;
            _layerGrid.Items.Refresh();
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var row in _layerRows) row.Selected = false;
            _layerGrid.Items.Refresh();
        }

        private void OnStructuralOnly(object sender, RoutedEventArgs e)
        {
            foreach (var row in _layerRows)
                row.Selected = row.Confidence > 0;
            _layerGrid.Items.Refresh();
        }

        private void OnAutoMapLayers(object sender, RoutedEventArgs e)
        {
            foreach (var row in _layerRows)
            {
                row.MapTo = InferMapTo(row.AutoDetect, row.Confidence);
            }
            _layerGrid.Items.Refresh();
            PopulateLayerMappingDropdowns();
            _statusBar.Text = "✓ Auto-mapped layers based on classification.";
        }

        private void UpdateNumberingPreview()
        {
            try
            {
                var config = BuildNumberingConfig();
                var preview = NumberingEngine.GeneratePreview(config, 8);
                _txtPreview.Text = string.Join("   ", preview);
            }
            catch (Exception ex)
            {
                _txtPreview.Text = $"(error: {ex.Message})";
            }
        }

        private NumberingEngine.NumberingConfig BuildNumberingConfig()
        {
            var config = new NumberingEngine.NumberingConfig
            {
                Prefix = _txtNumPrefix?.Text ?? "GFC",
                Separator = _txtNumSeparator?.Text ?? "-",
                UseGroupEnum = _chkGroupEnum?.IsChecked == true,
                UseElementEnum = _chkElementEnum?.IsChecked == true,
            };

            if (_cboGroupStyle?.SelectedIndex >= 0)
            {
                config.GroupStyle = _cboGroupStyle.SelectedIndex switch
                {
                    0 => NumberingEngine.EnumStyle.CapitalLetters,
                    1 => NumberingEngine.EnumStyle.Numeric,
                    2 => NumberingEngine.EnumStyle.LowerLetters,
                    3 => NumberingEngine.EnumStyle.CapitalRoman,
                    4 => NumberingEngine.EnumStyle.LowerRoman,
                    _ => NumberingEngine.EnumStyle.CapitalLetters,
                };
            }

            if (_cboElementStyle?.SelectedIndex >= 0)
            {
                config.ElementStyle = _cboElementStyle.SelectedIndex switch
                {
                    0 => NumberingEngine.EnumStyle.Numeric,
                    1 => NumberingEngine.EnumStyle.CapitalLetters,
                    2 => NumberingEngine.EnumStyle.LowerLetters,
                    3 => NumberingEngine.EnumStyle.CapitalRoman,
                    4 => NumberingEngine.EnumStyle.LowerRoman,
                    _ => NumberingEngine.EnumStyle.Numeric,
                };
            }

            int.TryParse(_txtStartFrom?.Text, out int start);
            config.StartFrom = start > 0 ? start : 1;
            int.TryParse(_txtDigits?.Text, out int digits);
            config.NumberOfDigits = digits > 0 ? digits : 2;
            int.TryParse(_txtIncrement?.Text, out int inc);
            config.IncrementBy = inc > 0 ? inc : 1;

            if (_cboNumCategory?.SelectedIndex >= 0 && _cboNumCategory.SelectedIndex < NumberingCategories.Length)
                config.Category = NumberingCategories[_cboNumCategory.SelectedIndex].Cat;

            config.ParameterName = _cboNumParameter?.SelectedItem?.ToString() ?? "Mark";
            config.OmitAlreadyNumbered = _chkOmitAlreadyNumbered?.IsChecked == true;

            return config;
        }

        private void OnConvert(object sender, RoutedEventArgs e)
        {
            if (_selectedImport == null)
            {
                _statusBar.Text = "⚠ No DWG import selected. Click Analyze Layers first.";
                _statusBar.Foreground = Brushes.Red;
                return;
            }

            if (_layerRows.Count == 0)
            {
                _statusBar.Text = "⚠ No layers analyzed. Click Analyze Layers first.";
                _statusBar.Foreground = Brushes.Red;
                return;
            }

            Confirmed = true;
            Close();
        }

        /// <summary>
        /// Phase-140 P3-D — Run extraction + detection in dry-run mode without
        /// closing the dialog. Updates the status bar with a per-element-type
        /// summary so the user can adjust tolerances and re-run.
        /// </summary>
        private void OnDryRunPreview(object sender, RoutedEventArgs e)
        {
            if (_selectedImport == null)
            {
                _statusBar.Text = "⚠ No DWG import selected. Click Analyze Layers first.";
                _statusBar.Foreground = Brushes.Red;
                return;
            }
            if (_layerRows.Count == 0)
            {
                _statusBar.Text = "⚠ No layers analyzed. Click Analyze Layers first.";
                _statusBar.Foreground = Brushes.Red;
                return;
            }

            try
            {
                _statusBar.Text = "Running dry-run preview…";
                _statusBar.Foreground = Brushes.Gray;
                Mouse.OverrideCursor = Cursors.Wait;

                var cfg = BuildConfig();
                cfg.DryRun = true; // Force, regardless of the dry-run checkbox state

                var result = _pipeline.RunFullPipelineWithConfig(_selectedImport, cfg);
                if (result == null)
                {
                    _statusBar.Text = "⚠ Dry-run produced no result.";
                    _statusBar.Foreground = Brushes.Red;
                    return;
                }

                int total = result.WallsCreated + result.ColumnsCreated
                          + result.BeamsCreated + result.SlabsCreated
                          + result.FootingsCreated + result.OpeningsDetected;

                string summary = $"DRY-RUN: {total} element(s) WOULD be created — " +
                    $"{result.WallsCreated} walls, " +
                    $"{result.ColumnsCreated} columns, " +
                    $"{result.BeamsCreated} beams, " +
                    $"{result.SlabsCreated} slabs, " +
                    $"{result.FootingsCreated} foundations" +
                    (result.OpeningsDetected > 0 ? $", {result.OpeningsDetected} openings" : "") +
                    (result.WallsRejectedByThickness > 0
                        ? $"  (rejected: {result.WallsRejectedByThickness} pairs out of [Min,Max] wall thickness)"
                        : "");

                _statusBar.Text = summary;
                _statusBar.Foreground = total > 0 ? Brushes.DarkGreen : Brushes.Goldenrod;

                // Surface up to 3 warnings as a tooltip on the status bar
                if (result.Warnings != null && result.Warnings.Count > 0)
                {
                    _statusBar.ToolTip = string.Join(Environment.NewLine,
                        result.Warnings.Take(20));
                }
                else
                {
                    _statusBar.ToolTip = null;
                }
            }
            catch (Exception ex)
            {
                _statusBar.Text = $"⚠ Dry-run failed: {ex.Message}";
                _statusBar.Foreground = Brushes.Red;
                StingLog.Error("OnDryRunPreview", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // ── Build Config ─────────────────────────────────────────────────

        private DWGConversionConfig BuildConfig()
        {
            var config = new DWGConversionConfig();

            // Selected layers
            config.SelectedLayers = new HashSet<string>(
                _layerRows.Where(r => r.Selected).Select(r => r.LayerName));

            // Layer-to-element mapping
            config.LayerMapping = new Dictionary<string, string>();
            foreach (var row in _layerRows.Where(r => !string.IsNullOrEmpty(r.MapTo)))
                config.LayerMapping[row.LayerName] = row.MapTo;

            // Specific layer assignments
            config.ColumnLayer = GetComboSelection(_cboColumnLayer);
            config.BeamLayer = GetComboSelection(_cboBeamLayer);
            config.WallLayer = GetComboSelection(_cboWallLayer);
            config.SlabLayer = GetComboSelection(_cboSlabLayer);
            config.FoundationLayer = GetComboSelection(_cboFoundationLayer);
            config.GridLayer = GetComboSelection(_cboGridLayer);

            // Element creation flags
            config.CreateColumns = _chkColumnLayer?.IsChecked == true;
            config.CreateBeams = _chkBeamLayer?.IsChecked == true;
            config.CreateWalls = _chkWallLayer?.IsChecked == true;
            config.CreateSlabs = _chkSlabLayer?.IsChecked == true;
            config.CreateFoundations = _chkFoundationLayer?.IsChecked == true;
            config.CreateGrids = _chkGridLayer?.IsChecked == true;

            // Levels
            config.BaseLevelName = _cboBaseLevel?.SelectedItem?.ToString();
            config.TopLevelName = _cboTopLevel?.SelectedItem?.ToString();
            config.AutoDetectSizes = _chkAutoDetectSizes?.IsChecked == true;

            // Repeat levels
            config.RepeatToLevelNames.Clear();
            foreach (var cb in _repeatLevelCheckboxesCad)
            {
                if (cb.IsChecked == true && cb.Tag is string lvlName)
                {
                    if (lvlName != config.BaseLevelName)
                        config.RepeatToLevelNames.Add(lvlName);
                }
            }
            config.ColumnsContinuousThrough = _chkColumnsContinuousCad?.IsChecked == true;

            // Dimensions
            double.TryParse(_txtColHeight?.Text, out double colH);
            config.ColumnHeightMm = colH > 0 ? colH : 3000;
            double.TryParse(_txtBeamDepth?.Text, out double beamD);
            config.BeamDepthMm = beamD > 0 ? beamD : 450;
            double.TryParse(_txtBeamWidth?.Text, out double beamW);
            config.BeamWidthMm = beamW > 0 ? beamW : 250;
            double.TryParse(_txtWallHeight?.Text, out double wallH);
            config.WallHeightMm = wallH > 0 ? wallH : 3000;
            double.TryParse(_txtWallThick?.Text, out double wallT);
            config.WallThicknessMm = wallT > 0 ? wallT : 200;
            double.TryParse(_txtSlabThick?.Text, out double slabT);
            config.SlabThicknessMm = slabT > 0 ? slabT : 200;
            double.TryParse(_txtFdnDepth?.Text, out double fdnD);
            config.FoundationDepthMm = fdnD > 0 ? fdnD : 600;

            // Construction logic
            config.BeamsRestOnWalls = _chkBeamsOnWalls?.IsChecked == true;
            config.BeamsConnectToSlabs = _chkBeamsConnectSlabs?.IsChecked == true;
            config.ColumnsStopAtSoffit = _chkColumnsStopAtSoffit?.IsChecked == true;
            config.CreateStructuralWalls = _chkStructuralWall?.IsChecked == true;

            // Phase-97 per-element size detection + type creation toggles
            config.DetectSizes_Wall       = _chkDetectSizes_Wall?.IsChecked != false;
            config.DetectSizes_Column     = _chkDetectSizes_Column?.IsChecked != false;
            config.DetectSizes_Beam       = _chkDetectSizes_Beam?.IsChecked != false;
            config.DetectSizes_Foundation = _chkDetectSizes_Foundation?.IsChecked != false;
            config.DetectSizes_Slab       = _chkDetectSizes_Slab?.IsChecked != false;
            config.CreateNewTypes_Wall       = _chkCreateTypes_Wall?.IsChecked != false;
            config.CreateNewTypes_Column     = _chkCreateTypes_Column?.IsChecked != false;
            config.CreateNewTypes_Beam       = _chkCreateTypes_Beam?.IsChecked != false;
            config.CreateNewTypes_Foundation = _chkCreateTypes_Foundation?.IsChecked != false;
            config.CreateNewTypes_Slab       = _chkCreateTypes_Slab?.IsChecked != false;
            if (double.TryParse(_txtColWidthFallback?.Text, out double colWFb) && colWFb > 0)
                config.ColumnWidthMm = colWFb;
            if (double.TryParse(_txtColDepthFallback?.Text, out double colDFb) && colDFb > 0)
                config.ColumnDepthMm = colDFb;
            if (double.TryParse(_txtFdnWidthFallback?.Text, out double fdnWFb) && fdnWFb > 0)
                config.FoundationWidthMm = fdnWFb;

            // Phase-78 EaseBit detection knobs
            config.DryRun = _chkDryRun?.IsChecked == true;
            config.ExplodeOnImport = _chkExplodeOnImport?.IsChecked == true;
            config.DetectOpenings = _chkDetectOpenings?.IsChecked == true;
            config.UseSpatialIndex = _chkUseSpatialIndex?.IsChecked != false;
            if (double.TryParse(_txtMinWallThickness?.Text, out double minWT) && minWT > 0)
                config.MinWallThicknessMm = minWT;
            if (double.TryParse(_txtMaxWallThickness?.Text, out double maxWT) && maxWT > 0)
                config.MaxWallThicknessMm = maxWT;
            if (double.TryParse(_txtParallelDot?.Text, out double pDot) && pDot > 0)
                config.ParallelDotTolerance = pDot;
            if (double.TryParse(_txtParallelGap?.Text, out double pGap) && pGap > 0)
                config.ParallelLineToleranceMm = pGap;
            if (double.TryParse(_txtEndpointTol?.Text, out double eTol) && eTol > 0)
                config.EndpointToleranceMm = eTol;
            if (double.TryParse(_txtMinOpeningWidth?.Text, out double minOW) && minOW > 0)
                config.MinOpeningWidthMm = minOW;
            if (double.TryParse(_txtMaxOpeningWidth?.Text, out double maxOW) && maxOW > 0)
                config.MaxOpeningWidthMm = maxOW;

            // Phase-140 accuracy knobs
            if (double.TryParse(_txtEndpointGap?.Text, out double endGap) && endGap >= 0)
                config.EndpointGapToleranceMm = endGap;
            if (double.TryParse(_txtGridSnapTol?.Text, out double gridSnap) && gridSnap >= 0)
                config.GridSnapToleranceMm = gridSnap;
            if (double.TryParse(_txtSpanToDepth?.Text, out double s2d) && s2d > 0)
                config.SpanToDepthRatio = s2d;
            if (double.TryParse(_txtBeamDepthMin?.Text, out double bdMin) && bdMin > 0)
                config.BeamDepthMinMm = bdMin;
            if (double.TryParse(_txtBeamDepthMax?.Text, out double bdMax) && bdMax > 0)
                config.BeamDepthMaxMm = bdMax;
            if (double.TryParse(_txtDuplicateTol?.Text, out double dupTol) && dupTol >= 0)
                config.DuplicateCheckToleranceMm = dupTol;
            config.UseSpanToDepthRatio = _chkUseSpanToDepth?.IsChecked != false;
            config.UseGridLabelsAsMarks = _chkUseGridLabelMarks?.IsChecked != false;
            config.SkipDuplicates = _chkSkipDuplicates?.IsChecked != false;
            config.TrimBeamsToColumnFaces = _chkTrimBeamsToColumns?.IsChecked != false;
            config.ShowStructuralWarningsInView = _chkShowWarningsInView?.IsChecked != false;

            // Phase-142
            if (double.TryParse(_txtMinColDiam?.Text, out double minCD) && minCD > 0)
                config.MinColumnDiameterMm = minCD;
            if (double.TryParse(_txtMaxColDiam?.Text, out double maxCD) && maxCD > 0)
                config.MaxColumnDiameterMm = maxCD;
            if (double.TryParse(_txtOverlapRatio?.Text, out double overlap) && overlap > 0)
                config.BeamOverlapMinRatio = overlap;
            if (double.TryParse(_txtStripOversize?.Text, out double stripOver) && stripOver >= 0)
                config.StripFndOversizeMm = stripOver;
            if (double.TryParse(_txtBeamSupportTol?.Text, out double bSupTol) && bSupTol > 0)
                config.BeamSupportToleranceMm = bSupTol;
            config.DetectStripFoundations = _chkDetectStripFoundations?.IsChecked != false;
            config.MarkCantileverBeams = _chkMarkCantilevers?.IsChecked != false;

            // Phase-143
            config.SeedRoomsFromSlabs = _chkSeedRoomsFromSlabs?.IsChecked == true;
            config.CreateStructuralViewsAfterConversion = _chkCreateStructuralViews?.IsChecked == true;
            config.InferBeamMaterial = _chkInferBeamMaterial?.IsChecked != false;
            config.ClassifyFoundations = _chkClassifyFoundations?.IsChecked != false;
            config.StampJunctionMarks = _chkStampJunctionMarks?.IsChecked == true;
            if (double.TryParse(_txtRaftMinArea?.Text, out double raftA) && raftA > 0)
                config.RaftMinAreaM2 = raftA;
            if (double.TryParse(_txtRoomLabelRadius?.Text, out double rmR) && rmR > 0)
                config.RoomLabelSearchRadiusMm = rmR;

            // Tagging
            config.AutoTag = _chkAutoTag?.IsChecked == true;
            config.AutoSeqNumbers = _chkAutoSeqNumbers?.IsChecked == true;
            config.TagPrefix = _txtTagPrefix?.Text ?? "";
            config.NumberingMode = _cboNumbering?.SelectedIndex ?? 0;

            // Numbering — visible (active) category
            config.NumberingConfig = BuildNumberingConfig();

            // Phase-140 P1-E: per-category numbering. Snapshot the visible UI into
            // its category slot, then fill in defaults for any structural category
            // the user didn't explicitly configure. NumberingEngine.ApplyAllPerCategory
            // iterates this dictionary at execution time.
            config.NumberingPerCategory.Clear();
            if (_chkPerCategoryNumbering?.IsChecked == true)
            {
                // Snapshot the active category as a fresh config (preserves its prefix etc.).
                if (_activeNumCatIndex >= 0 && _activeNumCatIndex < NumberingCategories.Length)
                {
                    var activeCfg = BuildNumberingConfig();
                    activeCfg.Category = NumberingCategories[_activeNumCatIndex].Cat;
                    _numConfigByCatIndex[_activeNumCatIndex] = activeCfg;
                }
                // Add every structural category — explicit edits win over defaults.
                var structuralCats = new[]
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_StructuralFoundation,
                };
                foreach (var cat in structuralCats)
                {
                    int idx = Array.FindIndex(NumberingCategories, t => t.Cat == cat);
                    NumberingEngine.NumberingConfig cfg;
                    if (idx >= 0 && _numConfigByCatIndex.TryGetValue(idx, out cfg))
                    {
                        cfg.Category = cat; // Defensive
                        config.NumberingPerCategory[cat] = cfg;
                    }
                    else
                    {
                        config.NumberingPerCategory[cat] = DefaultPerCategoryConfig(cat);
                    }
                }
            }

            return config;
        }

        private string GetComboSelection(ComboBox cbo)
        {
            var sel = cbo?.SelectedItem?.ToString();
            return sel == "(auto-detect)" ? "" : sel ?? "";
        }

        // ── UI Helpers ───────────────────────────────────────────────────

        private Border MakeSection(string title)
        {
            var border = new Border
            {
                BorderBrush = SectionBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(2),
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title, FontWeight = FontWeights.Bold, FontSize = 12,
                Foreground = AccentOrange, Margin = new Thickness(0, 0, 0, 6),
            });

            border.Child = stack;
            return border;
        }

        private Button MakeBtn(string text, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 90, Height = 30,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
            };
            if (bg != null) btn.Background = bg;
            if (fg != null) btn.Foreground = fg;
            if (bold) btn.FontWeight = FontWeights.Bold;
            btn.Click += handler;
            return btn;
        }

        private Button MakeSmallBtn(string text, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 70, Height = 24,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 10,
            };
            btn.Click += handler;
            return btn;
        }

        private TextBlock MakeLabel(string text, int col)
        {
            var lbl = new TextBlock
            {
                Text = text, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            if (col >= 0) Grid.SetColumn(lbl, col);
            return lbl;
        }

        private TextBlock MakeGridLabel(string text, int row, int col)
        {
            var lbl = new TextBlock
            {
                Text = text, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 4, 1),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, col);
            return lbl;
        }

        private StackPanel MakeDimRow(string label, string defaultVal, out TextBox textBox)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = label, Width = 90, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            textBox = new TextBox { Text = defaultVal, Width = 70, Margin = new Thickness(4, 0, 0, 0) };
            row.Children.Add(textBox);
            return row;
        }

        private StackPanel MakeDimRow2(string category, string lbl1, string val1, out TextBox txt1,
            string lbl2, string val2, out TextBox txt2)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = category, FontWeight = FontWeights.SemiBold,
                Width = 40, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = lbl1, Width = 50, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            });
            txt1 = new TextBox { Text = val1, Width = 55, Margin = new Thickness(2, 0, 8, 0) };
            row.Children.Add(txt1);
            row.Children.Add(new TextBlock
            {
                Text = lbl2, Width = 40, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            });
            txt2 = new TextBox { Text = val2, Width = 55, Margin = new Thickness(2, 0, 0, 0) };
            row.Children.Add(txt2);
            return row;
        }

        // Multi-select dropdown of project levels. Populates
        // _repeatLevelCheckboxesCad so BuildConfig() can read the picked
        // names without changing its loop.
        private FrameworkElement BuildRepeatLevelsDropdown(List<Level> levels)
        {
            _repeatLevelCheckboxesCad.Clear();

            var btn = new ToggleButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 4),
                MinHeight = 22,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            var btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var summary = new TextBlock
            {
                Text = "(none selected)", FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(summary, 0); btnGrid.Children.Add(summary);
            var glyph = new TextBlock
            {
                Text = "▾", FontSize = 10, Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray,
            };
            Grid.SetColumn(glyph, 1); btnGrid.Children.Add(glyph);
            btn.Content = btnGrid;

            var list = new StackPanel { Margin = new Thickness(6) };
            foreach (var lvl in levels)
            {
                var cb = new CheckBox
                {
                    Content = lvl.Name, Tag = lvl.Name,
                    Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                };
                _repeatLevelCheckboxesCad.Add(cb);
                list.Children.Add(cb);
            }

            var popup = new Popup
            {
                PlacementTarget = btn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            var popupBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)),
                BorderThickness = new Thickness(1),
                MinWidth = 180,
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 220,
                Content = list,
            };
            popupBorder.Child = scroll;
            popup.Child = popupBorder;

            void UpdateSummary()
            {
                var picked = _repeatLevelCheckboxesCad
                    .Where(c => c.IsChecked == true)
                    .Select(c => c.Tag as string)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                if (picked.Count == 0) summary.Text = "(none selected)";
                else if (picked.Count == _repeatLevelCheckboxesCad.Count) summary.Text = "All levels";
                else if (picked.Count <= 3) summary.Text = string.Join(", ", picked);
                else summary.Text = $"{picked.Count} levels selected";
            }
            foreach (var cb in _repeatLevelCheckboxesCad)
            {
                cb.Checked += (_, __) => UpdateSummary();
                cb.Unchecked += (_, __) => UpdateSummary();
            }

            btn.Checked   += (_, __) => popup.IsOpen = true;
            btn.Unchecked += (_, __) => popup.IsOpen = false;
            popup.Closed  += (_, __) => btn.IsChecked = false;

            UpdateSummary();

            var host = new StackPanel();
            host.Children.Add(btn);
            host.Children.Add(popup);
            return host;
        }
    }

    #endregion

    #region DWG Conversion Config

    /// <summary>Configuration for DWG-to-BIM conversion, built from the wizard dialog.</summary>
    public class DWGConversionConfig
    {
        // Layer selection
        public HashSet<string> SelectedLayers { get; set; } = new();
        public Dictionary<string, string> LayerMapping { get; set; } = new();

        // Specific layer assignments
        public string ColumnLayer { get; set; } = "";
        public string BeamLayer { get; set; } = "";
        public string WallLayer { get; set; } = "";
        public string SlabLayer { get; set; } = "";
        public string FoundationLayer { get; set; } = "";
        public string GridLayer { get; set; } = "";
        // Phase 71: Additional structural element layer assignments
        public string RoofLayer { get; set; } = "";
        public string StairLayer { get; set; } = "";
        public string RampLayer { get; set; } = "";
        public string PadFoundationLayer { get; set; } = "";
        public string PileLayer { get; set; } = "";
        public string RetainingWallLayer { get; set; } = "";
        public string BracingLayer { get; set; } = "";

        // Element creation flags
        public bool CreateColumns { get; set; } = true;
        public bool CreateBeams { get; set; } = true;
        public bool CreateWalls { get; set; } = true;
        public bool CreateSlabs { get; set; } = true;
        public bool CreateFoundations { get; set; } = true;
        public bool CreateGrids { get; set; } = true;
        // Phase 71: Additional structural element creation flags
        public bool CreateRoofs { get; set; } = false;
        public bool CreateStairs { get; set; } = false;
        public bool CreateRamps { get; set; } = false;
        public bool CreatePadFoundations { get; set; } = true;
        public bool CreatePiles { get; set; } = false;
        public bool CreateRetainingWalls { get; set; } = false;
        public bool CreateBracing { get; set; } = false;

        // Levels
        public string BaseLevelName { get; set; }
        public string TopLevelName { get; set; }
        public bool AutoDetectSizes { get; set; } = true;

        // Repeat/copy to other levels
        public List<string> RepeatToLevelNames { get; set; } = new();
        public bool ColumnsContinuousThrough { get; set; } = false;

        // Dimensions
        public double ColumnHeightMm { get; set; } = 3000;
        /// <summary>Fallback column width (mm) used when DetectSizes_Column is false
        /// OR no rectangle could be paired out of the DWG.</summary>
        public double ColumnWidthMm { get; set; } = 300;
        /// <summary>Fallback column depth (mm) used when DetectSizes_Column is false.</summary>
        public double ColumnDepthMm { get; set; } = 300;
        public double BeamDepthMm { get; set; } = 450;
        public double BeamWidthMm { get; set; } = 250;
        public double WallHeightMm { get; set; } = 3000;
        public double WallThicknessMm { get; set; } = 200;
        public double SlabThicknessMm { get; set; } = 200;
        public double FoundationDepthMm { get; set; } = 600;
        /// <summary>Fallback foundation pad width (mm) when DetectSizes_Foundation is false
        /// OR the DWG block/rectangle had no measurable footprint.</summary>
        public double FoundationWidthMm { get; set; } = 1200;
        // Phase 71: Additional structural element dimensions
        public double RoofThicknessMm { get; set; } = 250;
        public double RoofPitchDegrees { get; set; } = 15;
        public double StairRiserMm { get; set; } = 175;         // BS 5395 max 220mm
        public double StairGoingMm { get; set; } = 250;         // BS 5395 min 220mm
        public double StairWidthMm { get; set; } = 1000;        // BS 5395 min 800mm
        public double RampGradient { get; set; } = 1.0 / 12.0;  // BS 8300 max 1:12
        public double RampWidthMm { get; set; } = 1500;         // BS 8300 min 1500mm
        public double PadFoundationWidthMm { get; set; } = 1200;
        public double PadFoundationDepthMm { get; set; } = 600;
        public double PileDepthMm { get; set; } = 12000;        // Typical bored pile
        public double PileDiameterMm { get; set; } = 600;
        public double RetainingWallHeightMm { get; set; } = 2500;
        public double RetainingWallThicknessMm { get; set; } = 300;
        public double BracingAngleDegrees { get; set; } = 45;

        // ── Per-element size detection from DWG ──
        // When the Detect*Sizes flag is true, the pipeline reads the element's
        // dimensions directly from the DWG (parallel-line gap for walls/beams,
        // rectangle bounding box for columns/foundations). When false, it
        // falls back to the fixed default (*Mm) values above for every element
        // of that category regardless of what the DWG shows.
        public bool DetectSizes_Wall { get; set; } = true;
        public bool DetectSizes_Column { get; set; } = true;
        public bool DetectSizes_Beam { get; set; } = true;
        public bool DetectSizes_Foundation { get; set; } = true;
        public bool DetectSizes_Slab { get; set; } = true;

        // ── Per-element Revit type creation ──
        // When CreateNewTypes_X is true AND no exact-matching type exists, the
        // pipeline duplicates the closest-matching family type and resizes it
        // to the detected dimensions (e.g. "RC Column 325x275"). When false,
        // the closest existing type is reused as-is even if its size doesn't
        // match the DWG exactly — useful for office templates that enforce a
        // fixed size catalogue.
        public bool CreateNewTypes_Wall { get; set; } = true;
        public bool CreateNewTypes_Column { get; set; } = true;
        public bool CreateNewTypes_Beam { get; set; } = true;
        public bool CreateNewTypes_Foundation { get; set; } = true;
        public bool CreateNewTypes_Slab { get; set; } = true;

        // Construction logic
        public bool BeamsRestOnWalls { get; set; } = true;
        public bool BeamsConnectToSlabs { get; set; } = true;
        public bool ColumnsStopAtSoffit { get; set; } = true;
        public bool CreateStructuralWalls { get; set; } = true;
        public bool AutoJoinWalls { get; set; } = true;

        // ── Phase-78 EaseBit-style detection knobs ──
        /// <summary>Run detection pipeline only — report counts without creating elements.</summary>
        public bool DryRun { get; set; } = false;

        /// <summary>Minimum detected wall thickness (mm). Parallel line pairs closer than this
        /// are treated as false positives (dimension chains, construction lines).</summary>
        public double MinWallThicknessMm { get; set; } = 50;

        /// <summary>Maximum detected wall thickness (mm). Parallel line pairs farther than this
        /// are rejected to prevent accidental pairing with walls on the opposite side of a corridor.</summary>
        public double MaxWallThicknessMm { get; set; } = 500;

        /// <summary>Dot-product threshold for the parallel-line check.
        /// 0.98 ≈ ±11°, 0.995 ≈ ±5.7°. Lower values accept more lines as "parallel".</summary>
        public double ParallelDotTolerance { get; set; } = 0.98;

        /// <summary>Parallel-line max gap (mm). Pairs with perpendicular distance greater than
        /// this are never considered as a wall pair regardless of the min/max wall thickness.</summary>
        public double ParallelLineToleranceMm { get; set; } = 500;

        /// <summary>Endpoint match tolerance (mm). Two line endpoints within this distance
        /// are treated as the same vertex for merge/join operations.</summary>
        public double EndpointToleranceMm { get; set; } = 5;

        /// <summary>Use grid-based spatial index instead of O(n²) nested loops for
        /// parallel-pair and rectangle detection. Recommended for DWGs with many entities.</summary>
        public bool UseSpatialIndex { get; set; } = true;

        /// <summary>Detect openings (doors, windows, generic cut-outs) as gaps in collinear
        /// wall-layer segments after the main wall pipeline completes.</summary>
        public bool DetectOpenings { get; set; } = false;

        /// <summary>Minimum gap along a wall to count as an opening (mm).</summary>
        public double MinOpeningWidthMm { get; set; } = 400;

        /// <summary>Maximum gap along a wall to count as an opening (mm). Larger gaps are
        /// assumed to be two separate wall segments rather than one continuous wall with a hole.</summary>
        public double MaxOpeningWidthMm { get; set; } = 3000;

        /// <summary>Explode nested DWG block references before layer extraction. Surfaces
        /// geometry hidden inside blocks onto its host layer.</summary>
        public bool ExplodeOnImport { get; set; } = false;

        // ── Phase-140 accuracy knobs ─────────────────────────────────────

        /// <summary>Snap detected column centres to nearest grid intersection within this
        /// distance (mm). Set to 0 to disable. Defaults to 100 mm.</summary>
        public double GridSnapToleranceMm { get; set; } = 100;

        /// <summary>When true, beam depth is derived from span/SpanToDepthRatio
        /// (clamped to [BeamDepthMinMm, BeamDepthMaxMm], rounded to nearest 25 mm,
        /// floored at BeamDepthMm). When false, every beam uses BeamDepthMm.</summary>
        public bool UseSpanToDepthRatio { get; set; } = true;

        /// <summary>Span-to-depth ratio used to derive beam depth. 15 ≈ concrete framing,
        /// 20-25 for steel I-sections.</summary>
        public double SpanToDepthRatio { get; set; } = 15.0;

        /// <summary>Lower clamp on derived beam depth (mm).</summary>
        public double BeamDepthMinMm { get; set; } = 250;

        /// <summary>Upper clamp on derived beam depth (mm).</summary>
        public double BeamDepthMaxMm { get; set; } = 1200;

        /// <summary>If grid lines are detected and a column snaps to a grid intersection,
        /// use the grid labels as the column Mark (e.g. "A/1"). Sequential numbering
        /// applies to non-snapped columns regardless.</summary>
        public bool UseGridLabelsAsMarks { get; set; } = true;

        /// <summary>Wall endpoint bridging tolerance (mm). Pairs whose endpoints have a
        /// gap ≤ this are extended to close the gap during centreline computation.</summary>
        public double EndpointGapToleranceMm { get; set; } = 50;

        /// <summary>Treat a detected element as a duplicate of an existing element when
        /// their reference points are within this distance (mm). Skip duplicates when
        /// applied with the wizard's "Skip duplicates" toggle.</summary>
        public double DuplicateCheckToleranceMm { get; set; } = 50;

        /// <summary>When true, skip creating a new element if an existing element of the
        /// same category sits within DuplicateCheckToleranceMm of its insertion point.</summary>
        public bool SkipDuplicates { get; set; } = true;

        /// <summary>Move beam endpoints from junction centroids out to the face of the
        /// connecting column (column half-width + 25 mm cover). No-op for beams that
        /// don't reach a column.</summary>
        public bool TrimBeamsToColumnFaces { get; set; } = true;

        /// <summary>After post-creation load-path analysis, place TextNote markers in
        /// the active view at each warning location with prefix "⚠ STING-STRUCT:".</summary>
        public bool ShowStructuralWarningsInView { get; set; } = true;

        // ── Phase-141 detection knobs (column-circle classifier + junction warnings) ──

        /// <summary>Minimum detected circular-column diameter (mm). Circles smaller than this
        /// are rejected as columns by <c>DetectCircularColumns</c>. Defaults to 150 mm
        /// (matches the legacy `MinColumnSizeMm` constant).</summary>
        public double MinColumnDiameterMm { get; set; } = 150;

        /// <summary>Maximum detected circular-column diameter (mm). Circles larger than this
        /// are rejected as columns. Defaults to 1500 mm (matches `MaxColumnSizeMm`).</summary>
        public double MaxColumnDiameterMm { get; set; } = 1500;

        /// <summary>After extraction, place TextNote markers in the active view at each
        /// junction whose classification contains "WARNING" (e.g. beam intersection
        /// without a column) or "Free end" — surfaces the data that <c>DetectJunctions</c>
        /// has always produced but the legacy pipeline only used for the summary string.</summary>
        public bool ShowJunctionWarningsInView { get; set; } = true;

        // ── Phase-142 detection / construction logic ──

        /// <summary>Minimum longitudinal overlap ratio for parallel-pair detection
        /// (walls + beams). 0.5 ≈ 50% — the legacy default; lower values pair short
        /// connection stubs and diagonal bracing that the legacy detector misses.
        /// Range [0.1, 1.0]. </summary>
        public double BeamOverlapMinRatio { get; set; } = 0.5;

        /// <summary>When true, run <c>BeamSupportClassifier</c> after detection and
        /// stamp cantilever beams' instance Comments parameter "Cantilever (start|end)".
        /// Lets the analytical engineer find them quickly without reading the warning log.</summary>
        public bool MarkCantileverBeams { get; set; } = true;

        /// <summary>Tolerance (mm) for the beam-end → column / wall support classifier.
        /// Endpoints within this distance of a column footprint or wall centreline are
        /// classified as supported; outside, as cantilever / free.</summary>
        public double BeamSupportToleranceMm { get; set; } = 200;

        /// <summary>When <see cref="ColumnsContinuousThrough"/> is on, set the column's
        /// Top Constraint to the top level instead of stacking discrete column elements
        /// at each repeat level. Produces a single analytically-continuous column.</summary>
        public bool UseTopConstraintForContinuousColumns { get; set; } = true;

        /// <summary>Strip foundation oversize (mm per side) — the foundation extends
        /// this distance beyond each face of the wall it supports. Used by
        /// <c>StripFoundationDetector</c>.</summary>
        public double StripFndOversizeMm { get; set; } = 150;

        /// <summary>Detect strip foundations under structural walls and create them as
        /// structural floors along the wall centrelines.</summary>
        public bool DetectStripFoundations { get; set; } = true;

        // ── Phase-143 post-processing knobs ──

        /// <summary>After slabs are created, seed Revit Rooms at each slab centroid
        /// (skipping points that fall inside a slab void). Re-uses any text on the
        /// slab layer near the centroid as the room name.</summary>
        public bool SeedRoomsFromSlabs { get; set; } = false;

        /// <summary>When seeding rooms, the maximum distance (mm) from a slab
        /// centroid that a layer-text label can sit and still be used as the
        /// room's Name. Larger values risk picking up a label from a neighbouring
        /// slab.</summary>
        public double RoomLabelSearchRadiusMm { get; set; } = 3000;

        /// <summary>After conversion, create one structural ViewPlan per level that
        /// received elements. Uses Phase-113 DrawingTypeRegistry to look up the
        /// corporate "S-PLAN" drawing type and apply via DrawingTypePresentation.</summary>
        public bool CreateStructuralViewsAfterConversion { get; set; } = false;

        /// <summary>Heuristically classify beams as steel I-section vs concrete
        /// rectangle. Pure-line beams (single centreline detection) hint steel;
        /// parallel-pair beams (with a measured width) hint concrete. The hint is
        /// passed to <c>StructuralTypeFactory.FindOrCreateBeamType</c> for type
        /// matching. Only affects beams whose detected width was inferred from
        /// the wizard fallback (no parallel-line evidence).</summary>
        public bool InferBeamMaterial { get; set; } = true;

        /// <summary>Foundation classifier mode. When on, detected foundations are
        /// classified as Pad / Raft / Pile cap based on plan-area + clustering
        /// heuristics. Pads route to <c>CreatePadFoundations</c>; rafts route to
        /// <c>CreateSlabsFromBoundaries</c> (structural floor); pile caps stay as
        /// pad foundations but are stamped with "STING: PileCap" Comments for
        /// downstream connection design.</summary>
        public bool ClassifyFoundations { get; set; } = true;

        /// <summary>Plan-area threshold (m²) above which a detected rectangular
        /// foundation is classified as a raft instead of an isolated pad. Default
        /// 4 m² (≈ 2 m × 2 m).</summary>
        public double RaftMinAreaM2 { get; set; } = 4.0;

        /// <summary>Stamp the Mark parameter of every column and beam participating
        /// in each detected junction with a junction-type tag (e.g. "J:T-junction"
        /// when nothing is set, appended otherwise). Lets engineers find junction
        /// participants via schedules without traversing the analytical graph.</summary>
        public bool StampJunctionMarks { get; set; } = false;

        // Tagging
        public bool AutoTag { get; set; } = true;
        public bool AutoSeqNumbers { get; set; } = true;
        public string TagPrefix { get; set; } = "";
        public int NumberingMode { get; set; } = 0;

        // Numbering — single category (legacy) and per-category (Phase-140)
        public NumberingEngine.NumberingConfig NumberingConfig { get; set; } = new();

        /// <summary>Per-category numbering configurations. When populated, each category
        /// is numbered independently using its own NumberingConfig. Falls back to
        /// NumberingConfig when empty.</summary>
        public Dictionary<BuiltInCategory, NumberingEngine.NumberingConfig> NumberingPerCategory { get; set; }
            = new Dictionary<BuiltInCategory, NumberingEngine.NumberingConfig>();
    }

    #endregion
}
