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
                catch { return 0.0; }
            })
            .ThenBy(e =>
            {
                try
                {
                    var loc = e.Location as LocationPoint;
                    return loc?.Point.X ?? 0;
                }
                catch { return 0.0; }
            })
            .ThenBy(e =>
            {
                try
                {
                    var loc = e.Location as LocationPoint;
                    return loc?.Point.Y ?? 0;
                }
                catch { return 0.0; }
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
    public class StructuralCADWizard : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly Document _doc;
        private readonly StructuralCADPipeline _pipeline;
        private ImportInstance _selectedImport;
        private StructuralExtractionResult _extraction;

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

        public StructuralCADWizard(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _pipeline = new StructuralCADPipeline(doc);

            Title = "STING Structural DWG-to-BIM Conversion";
            Width = 980;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

            BuildLayout();
        }

        // ── Colours ──────────────────────────────────────────────────────
        private static readonly Brush AccentOrange = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly Brush DarkBlue = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly Brush LightBg = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2));
        private static readonly Brush SectionBorder = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x7E));

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
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26)),
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
            content.Children.Add(BuildSection5_Numbering());

            scrollViewer.Content = content;
            Grid.SetRow(scrollViewer, 1);
            root.Children.Add(scrollViewer);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
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
            var btnConvert = MakeBtn("Convert to BIM", OnConvert);
            btnConvert.Background = AccentOrange;
            btnConvert.Foreground = Brushes.White;
            btnConvert.FontWeight = FontWeights.Bold;
            btnConvert.MinWidth = 140;
            var btnCancel = MakeBtn("Cancel", (s, e) => { Confirmed = false; Close(); });
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
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2)),
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
            _repeatLevelCheckboxesCad.Clear();
            var repeatWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var lvl in levels)
            {
                var cb = new CheckBox
                {
                    Content = lvl.Name, Tag = lvl.Name,
                    Margin = new Thickness(0, 0, 8, 4), FontSize = 10,
                };
                _repeatLevelCheckboxesCad.Add(cb);
                repeatWrap.Children.Add(cb);
            }
            levelStack.Children.Add(repeatWrap);
            _chkColumnsContinuousCad = new CheckBox
            {
                Content = "Columns continuous through repeat levels",
                Margin = new Thickness(0, 0, 0, 0), FontSize = 10,
            };
            levelStack.Children.Add(_chkColumnsContinuousCad);

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
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)),
            };
            _chkBeamsConnectSlabs = new CheckBox
            {
                Content = "Beams connect to slabs", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)),
            };
            _chkColumnsStopAtSoffit = new CheckBox
            {
                Content = "Columns stop at slab soffit (not slab level)", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)),
            };
            _chkStructuralWall = new CheckBox
            {
                Content = "Create as Structural Walls (not architectural)", IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)),
            };

            leftStack.Children.Add(_chkBeamsOnWalls);
            leftStack.Children.Add(_chkBeamsConnectSlabs);
            leftStack.Children.Add(_chkColumnsStopAtSoffit);
            leftStack.Children.Add(_chkStructuralWall);

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
            _cboNumCategory.SelectionChanged += (s, e) => UpdateNumberingPreview();
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

            UpdateNumberingPreview();
            return section;
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
                    foreach (var bl in _extraction.BeamLines ?? new List<ExtractedLine>())
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
                    var counts = layerEntityCounts.ContainsKey(kvp.Key)
                        ? layerEntityCounts[kvp.Key] : (lines: 0, arcs: 0);

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
                _statusBar.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
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

            // Tagging
            config.AutoTag = _chkAutoTag?.IsChecked == true;
            config.AutoSeqNumbers = _chkAutoSeqNumbers?.IsChecked == true;
            config.TagPrefix = _txtTagPrefix?.Text ?? "";
            config.NumberingMode = _cboNumbering?.SelectedIndex ?? 0;

            // Numbering
            config.NumberingConfig = BuildNumberingConfig();

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
        public double BeamDepthMm { get; set; } = 450;
        public double BeamWidthMm { get; set; } = 250;
        public double WallHeightMm { get; set; } = 3000;
        public double WallThicknessMm { get; set; } = 200;
        public double SlabThicknessMm { get; set; } = 200;
        public double FoundationDepthMm { get; set; } = 600;
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

        // Construction logic
        public bool BeamsRestOnWalls { get; set; } = true;
        public bool BeamsConnectToSlabs { get; set; } = true;
        public bool ColumnsStopAtSoffit { get; set; } = true;
        public bool CreateStructuralWalls { get; set; } = true;
        public bool AutoJoinWalls { get; set; } = true;

        // Tagging
        public bool AutoTag { get; set; } = true;
        public bool AutoSeqNumbers { get; set; } = true;
        public string TagPrefix { get; set; } = "";
        public int NumberingMode { get; set; } = 0;

        // Numbering
        public NumberingEngine.NumberingConfig NumberingConfig { get; set; } = new();
    }

    #endregion
}
