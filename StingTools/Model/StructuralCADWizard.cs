// ============================================================================
// StructuralCADWizard.cs — Compact Single-Page DWG-to-Structural BIM Dialog
//
// Replaces the 5-page wizard with a single compact WPF dialog that shows
// all settings at once. Features:
//   - DWG import picker with layer analysis DataGrid
//   - Dropdown-based layer-to-element mapping
//   - Auto-detect column sizes from geometry (circles + rectangles)
//   - Level configuration (base + top)
//   - Per-element-type property groups with auto-detect option
//   - Custom tag family picker
//   - Construction logic options (beam/slab/column relationships)
//   - Shape-aware type creation from detected geometry
//
// Built in C# (no XAML) following existing StingTools WPF pattern.
// ============================================================================

using System;
using System.Collections.Generic;
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
    #region Layer Analysis Data

    /// <summary>Row in the layer analysis DataGrid.</summary>
    public class LayerRow
    {
        public bool Selected { get; set; }
        public string Name { get; set; }
        public int Entities { get; set; }
        public int Lines { get; set; }
        public int Arcs { get; set; }
        public string AutoDetect { get; set; }
        public int ConfidencePct { get; set; }
        public string MapTo { get; set; } = "";

        /// <summary>Display for layer dropdowns.</summary>
        public override string ToString() => $"{Name} ({Entities} ent.)";
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

    /// <summary>
    /// Compact single-page WPF dialog for structural DWG-to-BIM conversion.
    /// All settings visible at once — no page navigation required.
    /// </summary>
    public class StructuralDWGDialog : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly Document _doc;
        private readonly StructuralCADPipeline _pipeline;
        private ImportInstance _selectedImport;
        private StructuralExtractionResult _extraction;
        private readonly List<LayerRow> _layerRows = new();
        private readonly List<DetectedElementGroup> _detectedGroups = new();

        // UI elements needing refresh
        private ComboBox _cmbImport;
        private DataGrid _layerGrid;
        private ComboBox _cmbColumnLayer, _cmbBeamLayer, _cmbWallLayer;
        private ComboBox _cmbSlabLayer, _cmbFoundationLayer, _cmbGridLayer;
        private ComboBox _cmbBaseLevel, _cmbTopLevel;
        private TextBlock _txtStatus, _txtDetectedSizes;
        private CheckBox _chkAutoDetectSizes, _chkAutoTag, _chkAutoSeq;
        private CheckBox _chkCreateColumns, _chkCreateBeams, _chkCreateWalls;
        private CheckBox _chkCreateSlabs, _chkCreateFoundations, _chkCreateGrids;
        private CheckBox _chkBeamsOnWalls, _chkBeamsJoinSlabs, _chkColumnsToSlabs;
        private TextBox _tbTagPrefix, _tbColHeight, _tbBeamDepth, _tbBeamWidth;
        private TextBox _tbWallHeight, _tbWallThick, _tbSlabThick, _tbFdnDepth;
        private ComboBox _cmbNumberScheme, _cmbTagFamily;
        private TextBlock _txtPreviewTags;
        private StackPanel _detectedSizesPanel;

        // ── Results ──────────────────────────────────────────────────────
        public bool Confirmed { get; private set; }
        public ImportInstance SelectedImport => _selectedImport;
        public HashSet<string> SelectedLayers { get; private set; } = new();

        // Element creation flags
        public bool CreateColumns => _chkCreateColumns?.IsChecked == true;
        public bool CreateBeams => _chkCreateBeams?.IsChecked == true;
        public bool CreateWalls => _chkCreateWalls?.IsChecked == true;
        public bool CreateSlabs => _chkCreateSlabs?.IsChecked == true;
        public bool CreateFoundations => _chkCreateFoundations?.IsChecked == true;
        public bool CreateGrids => _chkCreateGrids?.IsChecked == true;

        // Dimensions
        public double ColumnHeightMm => ParseDbl(_tbColHeight, 3000);
        public double BeamDepthMm => ParseDbl(_tbBeamDepth, 450);
        public double BeamWidthMm => ParseDbl(_tbBeamWidth, 250);
        public double WallHeightMm => ParseDbl(_tbWallHeight, 3000);
        public double WallThicknessMm => ParseDbl(_tbWallThick, 200);
        public double SlabThicknessMm => ParseDbl(_tbSlabThick, 200);
        public double FoundationDepthMm => ParseDbl(_tbFdnDepth, 600);
        public bool AutoDetectSizes => _chkAutoDetectSizes?.IsChecked == true;
        public bool AutoTag => _chkAutoTag?.IsChecked == true;
        public bool AutoSequence => _chkAutoSeq?.IsChecked == true;
        public string TagPrefix => _tbTagPrefix?.Text ?? "";
        public string BaseLevelName => _cmbBaseLevel?.SelectedItem?.ToString();
        public string TopLevelName => _cmbTopLevel?.SelectedItem?.ToString();
        public string NumberingScheme => _cmbNumberScheme?.SelectedItem?.ToString() ?? "By Level";
        public string SelectedTagFamily => _cmbTagFamily?.SelectedItem?.ToString();

        // Construction logic
        public bool BeamsOnWalls => _chkBeamsOnWalls?.IsChecked == true;
        public bool BeamsJoinSlabs => _chkBeamsJoinSlabs?.IsChecked == true;
        public bool ColumnsToSlabs => _chkColumnsToSlabs?.IsChecked == true;

        // Layer mappings
        public string ColumnLayerName => GetLayerFromCombo(_cmbColumnLayer);
        public string BeamLayerName => GetLayerFromCombo(_cmbBeamLayer);
        public string WallLayerName => GetLayerFromCombo(_cmbWallLayer);
        public string SlabLayerName => GetLayerFromCombo(_cmbSlabLayer);
        public string FoundationLayerName => GetLayerFromCombo(_cmbFoundationLayer);
        public string GridLayerName => GetLayerFromCombo(_cmbGridLayer);

        public List<DetectedElementGroup> DetectedGroups => _detectedGroups;

        // ── Theme colors ─────────────────────────────────────────────────
        private static readonly SolidColorBrush HeaderBg = new(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush SectionBg = new(Color.FromRgb(0xF8, 0xF8, 0xFC));
        private static readonly SolidColorBrush BorderClr = new(Color.FromRgb(0xD0, 0xD0, 0xD8));
        private static readonly SolidColorBrush LabelFg = new(Color.FromRgb(0x33, 0x33, 0x44));

        // ── Constructor ──────────────────────────────────────────────────
        public StructuralDWGDialog(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _pipeline = new StructuralCADPipeline(doc);

            Title = "STING Structural DWG-to-BIM";
            Width = 920; Height = 740;
            MinWidth = 800; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA));

            BuildLayout();
            LoadImports();
            LoadLevels();
            LoadTagFamilies();
        }

        // ── Main Layout ──────────────────────────────────────────────────
        private void BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });   // footer

            // ── Header ──
            var header = new Border { Background = HeaderBg, Padding = new Thickness(16, 8, 16, 8) };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text = "Structural DWG-to-BIM Conversion",
                FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            _txtStatus = new TextBlock
            {
                Text = "Select a DWG import and analyze layers",
                Foreground = AccentBrush, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtStatus, 1);
            headerGrid.Children.Add(_txtStatus);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Scrollable content ──
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12, 8, 12, 8)
            };
            var mainStack = new StackPanel();

            // Section 1: DWG Import + Layer Analysis
            mainStack.Children.Add(BuildDWGSection());
            // Section 2: Layer-to-Element Mapping
            mainStack.Children.Add(BuildMappingSection());
            // Section 3: Level Configuration + Element Properties (side by side)
            mainStack.Children.Add(BuildPropertiesSection());
            // Section 4: Construction Logic + Tagging
            mainStack.Children.Add(BuildTaggingSection());

            scroll.Content = mainStack;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)),
                BorderBrush = BorderClr, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var footerInfo = new TextBlock
            {
                FontSize = 10, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "Select DWG import → Analyze → Map layers → Convert"
            };
            footerGrid.Children.Add(footerInfo);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var btnExecute = MakeBtn("Convert to BIM", OnExecute, AccentBrush, Brushes.White, true);
            btnExecute.MinWidth = 140;
            var btnCancel = MakeBtn("Cancel", (s, e) => { Confirmed = false; Close(); });
            btnPanel.Children.Add(btnExecute);
            btnPanel.Children.Add(btnCancel);
            Grid.SetColumn(btnPanel, 1);
            footerGrid.Children.Add(btnPanel);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // ══════════════════════════════════════════════════════════════════
        // SECTION 1: DWG Import + Layer Analysis
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildDWGSection()
        {
            var section = MakeSection("DWG IMPORT & LAYER ANALYSIS");
            var stack = new StackPanel { Margin = new Thickness(8) };

            // Row: Import picker + Analyze button
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row1.Children.Add(MakeLbl("DWG Import:", 0));
            _cmbImport = new ComboBox { Margin = new Thickness(4, 2, 4, 2), FontSize = 11 };
            _cmbImport.SelectionChanged += OnImportChanged;
            Grid.SetColumn(_cmbImport, 1);
            row1.Children.Add(_cmbImport);

            var btnAnalyze = MakeBtn("Analyze Layers", OnAnalyze, AccentBrush, Brushes.White, true);
            Grid.SetColumn(btnAnalyze, 2);
            row1.Children.Add(btnAnalyze);
            stack.Children.Add(row1);

            // Quick filter buttons
            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2)
            };
            filterRow.Children.Add(MakeSmallBtn("Select All", OnSelectAll));
            filterRow.Children.Add(MakeSmallBtn("Select None", OnSelectNone));
            filterRow.Children.Add(MakeSmallBtn("Structural Only", OnSelectStructural, AccentBrush));
            filterRow.Children.Add(MakeSmallBtn("Auto-Map Layers", OnAutoMap, new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))));
            stack.Children.Add(filterRow);

            // Layer analysis DataGrid
            _layerGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                MaxHeight = 180,
                MinHeight = 100,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                BorderBrush = BorderClr,
                BorderThickness = new Thickness(1),
                SelectionMode = DataGridSelectionMode.Single,
                RowHeight = 22,
            };

            // Columns
            var selCol = new DataGridCheckBoxColumn
            {
                Header = "", Binding = new System.Windows.Data.Binding("Selected") { Mode = BindingMode.TwoWay },
                Width = 28
            };
            _layerGrid.Columns.Add(selCol);
            _layerGrid.Columns.Add(MakeTextCol("Layer Name", "Name", 150));
            _layerGrid.Columns.Add(MakeTextCol("Entities", "Entities", 55));
            _layerGrid.Columns.Add(MakeTextCol("Lines", "Lines", 50));
            _layerGrid.Columns.Add(MakeTextCol("Arcs", "Arcs", 45));
            _layerGrid.Columns.Add(MakeTextCol("Auto-Detect", "AutoDetect", 90));
            _layerGrid.Columns.Add(MakeTextCol("Conf.", "ConfidencePct", 45));

            // MapTo dropdown column
            var mapCol = new DataGridComboBoxColumn
            {
                Header = "Map To",
                Width = 100,
                SelectedItemBinding = new System.Windows.Data.Binding("MapTo") { Mode = BindingMode.TwoWay },
                ItemsSource = new[] { "", "Column", "Beam", "Wall", "Slab", "Foundation", "Grid" }
            };
            _layerGrid.Columns.Add(mapCol);

            _layerGrid.ItemsSource = _layerRows;
            stack.Children.Add(_layerGrid);

            // Detected sizes display
            _detectedSizesPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _txtDetectedSizes = new TextBlock
            {
                FontSize = 10, Foreground = Brushes.Gray,
                Text = "Analyze DWG to detect element sizes"
            };
            _detectedSizesPanel.Children.Add(_txtDetectedSizes);
            stack.Children.Add(_detectedSizesPanel);

            section.Child = stack;
            return section;
        }

        // ══════════════════════════════════════════════════════════════════
        // SECTION 2: Layer-to-Element Mapping (dropdowns)
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildMappingSection()
        {
            var section = MakeSection("ELEMENT-LAYER MAPPING");
            var grid = new Grid { Margin = new Thickness(8) };

            // 3 columns of 2 element types each
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); // spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); // spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            _cmbColumnLayer = MakeLayerCombo();
            _cmbBeamLayer = MakeLayerCombo();
            _cmbWallLayer = MakeLayerCombo();
            _cmbSlabLayer = MakeLayerCombo();
            _cmbFoundationLayer = MakeLayerCombo();
            _cmbGridLayer = MakeLayerCombo();

            var col = MakeMapRow("Column Layer:", _cmbColumnLayer,
                out _chkCreateColumns, "Circles/rectangles → columns");
            Grid.SetRow(col, 0); Grid.SetColumn(col, 0);
            grid.Children.Add(col);

            var beam = MakeMapRow("Beam Layer:", _cmbBeamLayer,
                out _chkCreateBeams, "Lines between columns → beams");
            Grid.SetRow(beam, 0); Grid.SetColumn(beam, 2);
            grid.Children.Add(beam);

            var wall = MakeMapRow("Wall Layer:", _cmbWallLayer,
                out _chkCreateWalls, "Parallel line pairs → walls");
            Grid.SetRow(wall, 0); Grid.SetColumn(wall, 4);
            grid.Children.Add(wall);

            var slab = MakeMapRow("Slab Layer:", _cmbSlabLayer,
                out _chkCreateSlabs, "Closed loops → floor slabs");
            Grid.SetRow(slab, 1); Grid.SetColumn(slab, 0);
            grid.Children.Add(slab);

            var fdn = MakeMapRow("Foundation:", _cmbFoundationLayer,
                out _chkCreateFoundations, "Footings below columns");
            Grid.SetRow(fdn, 1); Grid.SetColumn(fdn, 2);
            grid.Children.Add(fdn);

            var grd = MakeMapRow("Grid Layer:", _cmbGridLayer,
                out _chkCreateGrids, "Long axis-aligned lines → grids");
            Grid.SetRow(grd, 1); Grid.SetColumn(grd, 4);
            grid.Children.Add(grd);

            section.Child = grid;
            return section;
        }

        private StackPanel MakeMapRow(string label, ComboBox combo,
            out CheckBox chk, string tooltip)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            chk = new CheckBox
            {
                Content = label, IsChecked = true,
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = LabelFg, ToolTip = tooltip,
                Margin = new Thickness(0, 0, 0, 2)
            };
            panel.Children.Add(chk);
            panel.Children.Add(combo);
            return panel;
        }

        private ComboBox MakeLayerCombo()
        {
            return new ComboBox
            {
                FontSize = 10, Height = 24,
                Margin = new Thickness(0, 0, 0, 0),
                IsEditable = true // allow typing to filter
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // SECTION 3: Level Config + Element Properties
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPropertiesSection()
        {
            var section = MakeSection("LEVELS & ELEMENT PROPERTIES");
            var outerGrid = new Grid { Margin = new Thickness(8) };
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Level config
            var levelPanel = new StackPanel();
            levelPanel.Children.Add(MakeSectionLabel("LEVEL CONFIGURATION"));
            levelPanel.Children.Add(MakeFieldRow("Base Level:", out _cmbBaseLevel));
            levelPanel.Children.Add(MakeFieldRow("Top Level:", out _cmbTopLevel));

            _chkAutoDetectSizes = new CheckBox
            {
                Content = "Auto-detect sizes from geometry",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 8, 0, 4),
                ToolTip = "Detect column dimensions from circles/rectangles.\nGroup by size to create appropriate types."
            };
            levelPanel.Children.Add(_chkAutoDetectSizes);
            outerGrid.Children.Add(levelPanel);

            // Right: Element properties in compact grid
            var propsGrid = new Grid();
            propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Column props
            _tbColHeight = new TextBox();
            var colProps = MakePropsGroup("COLUMN", new[]
            {
                ("Height (mm):", "3000", _tbColHeight)
            });
            Grid.SetColumn(colProps, 0);
            propsGrid.Children.Add(colProps);

            // Beam props
            _tbBeamDepth = new TextBox();
            _tbBeamWidth = new TextBox();
            var beamProps = MakePropsGroup("BEAM", new[]
            {
                ("Depth (mm):", "450", _tbBeamDepth),
                ("Width (mm):", "250", _tbBeamWidth)
            });
            Grid.SetColumn(beamProps, 2);
            propsGrid.Children.Add(beamProps);

            // Wall + Slab + Foundation (stacked)
            var rightStack = new StackPanel();
            var wallRow = MakeMiniPropRow("Wall", "Height:", "3000", out _tbWallHeight,
                "Thick:", "200", out _tbWallThick);
            rightStack.Children.Add(wallRow);

            var slabRow = new StackPanel { Orientation = Orientation.Horizontal };
            slabRow.Children.Add(MakeMiniField("Slab Thick:", "200", out _tbSlabThick));
            slabRow.Children.Add(MakeMiniField("Fdn Depth:", "600", out _tbFdnDepth));
            rightStack.Children.Add(slabRow);

            Grid.SetColumn(rightStack, 4);
            propsGrid.Children.Add(rightStack);

            Grid.SetColumn(propsGrid, 2);
            outerGrid.Children.Add(propsGrid);

            section.Child = outerGrid;
            return section;
        }

        // ══════════════════════════════════════════════════════════════════
        // SECTION 4: Construction Logic + Tagging
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildTaggingSection()
        {
            var section = MakeSection("CONSTRUCTION LOGIC & TAGGING");
            var outerGrid = new Grid { Margin = new Thickness(8) };
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Construction Logic
            var logicPanel = new StackPanel();
            logicPanel.Children.Add(MakeSectionLabel("CONSTRUCTION RELATIONSHIPS"));

            _chkBeamsOnWalls = new CheckBox
            {
                Content = "Beams rest on top of walls",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = "Beam bottom aligns with wall top level"
            };
            _chkBeamsJoinSlabs = new CheckBox
            {
                Content = "Beams connect to slabs",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = "Beam top aligns with slab soffit"
            };
            _chkColumnsToSlabs = new CheckBox
            {
                Content = "Columns stop at slab levels",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = "Column top constrained to next slab level"
            };
            logicPanel.Children.Add(_chkBeamsOnWalls);
            logicPanel.Children.Add(_chkBeamsJoinSlabs);
            logicPanel.Children.Add(_chkColumnsToSlabs);

            logicPanel.Children.Add(new TextBlock
            {
                Text = "Construction sequence: Foundation → Column → Beam → Slab",
                FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 6, 0, 0), FontStyle = FontStyles.Italic
            });
            outerGrid.Children.Add(logicPanel);

            // Right: Tagging options
            var tagPanel = new StackPanel();
            tagPanel.Children.Add(MakeSectionLabel("STING ISO 19650 TAGGING"));

            _chkAutoTag = new CheckBox
            {
                Content = "Auto-tag all created elements",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4)
            };
            _chkAutoSeq = new CheckBox
            {
                Content = "Auto-assign SEQ numbers per type",
                IsChecked = true, FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4)
            };
            tagPanel.Children.Add(_chkAutoTag);
            tagPanel.Children.Add(_chkAutoSeq);

            // Tag prefix
            var prefixRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            prefixRow.Children.Add(new TextBlock { Text = "Tag prefix:", FontSize = 11, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _tbTagPrefix = new TextBox { Width = 80, FontSize = 11, Height = 22 };
            prefixRow.Children.Add(_tbTagPrefix);
            tagPanel.Children.Add(prefixRow);

            // Numbering scheme
            var numRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            numRow.Children.Add(new TextBlock { Text = "Numbering:", FontSize = 11, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _cmbNumberScheme = new ComboBox { Width = 160, FontSize = 11, Height = 24 };
            _cmbNumberScheme.Items.Add("By Level (L01-COL-0001)");
            _cmbNumberScheme.Items.Add("By Grid (A1-COL-0001)");
            _cmbNumberScheme.Items.Add("Sequential (COL-0001)");
            _cmbNumberScheme.Items.Add("By Zone (Z01-COL-0001)");
            _cmbNumberScheme.SelectedIndex = 0;
            numRow.Children.Add(_cmbNumberScheme);
            tagPanel.Children.Add(numRow);

            // Tag family picker
            var famRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            famRow.Children.Add(new TextBlock { Text = "Tag family:", FontSize = 11, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _cmbTagFamily = new ComboBox { Width = 160, FontSize = 11, Height = 24 };
            _cmbTagFamily.Items.Add("(Default STING tags)");
            _cmbTagFamily.SelectedIndex = 0;
            famRow.Children.Add(_cmbTagFamily);
            tagPanel.Children.Add(famRow);

            // Tag preview
            _txtPreviewTags = new TextBlock
            {
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Text = "Column: S-BLD1-Z01-L01-STR-SUP-COL-0001\n" +
                       "Beam:   S-BLD1-Z01-L01-STR-SUP-BM-0001"
            };
            tagPanel.Children.Add(_txtPreviewTags);

            Grid.SetColumn(tagPanel, 2);
            outerGrid.Children.Add(tagPanel);

            section.Child = outerGrid;
            return section;
        }

        // ══════════════════════════════════════════════════════════════════
        // DATA LOADING
        // ══════════════════════════════════════════════════════════════════

        private void LoadImports()
        {
            var imports = CADToModelEngine.FindImportInstances(_doc);
            foreach (var imp in imports)
            {
                string name = "DWG Import";
                try { name = imp.Name ?? $"Import #{imp.Id.Value}"; }
                catch (Exception ex) { StingLog.Warn($"Import name: {ex.Message}"); }
                _cmbImport.Items.Add(new ComboBoxItem { Content = name, Tag = imp });
            }
            if (_cmbImport.Items.Count > 0) _cmbImport.SelectedIndex = 0;
        }

        private void LoadLevels()
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            foreach (var lev in levels)
            {
                _cmbBaseLevel.Items.Add(lev.Name);
                _cmbTopLevel.Items.Add(lev.Name);
            }
            if (levels.Count > 0) _cmbBaseLevel.SelectedIndex = 0;
            if (levels.Count > 1) _cmbTopLevel.SelectedIndex = levels.Count - 1;
            else if (levels.Count > 0) _cmbTopLevel.SelectedIndex = 0;
        }

        private void LoadTagFamilies()
        {
            try
            {
                foreach (Family fam in new FilteredElementCollector(_doc)
                    .OfClass(typeof(Family)).Cast<Family>())
                {
                    // Include annotation/tag families
                    if (fam.FamilyCategory == null) continue;
                    var catId = fam.FamilyCategory.Id.Value;
                    // Tag categories in Revit
                    bool isTag = fam.FamilyCategory.Name.Contains("Tag") ||
                        catId == (int)BuiltInCategory.OST_StructuralColumnTags ||
                        catId == (int)BuiltInCategory.OST_StructuralFramingTags ||
                        catId == (int)BuiltInCategory.OST_WallTags ||
                        catId == (int)BuiltInCategory.OST_FloorTags ||
                        catId == (int)BuiltInCategory.OST_StructuralFoundationTags ||
                        catId == (int)BuiltInCategory.OST_MultiCategoryTags;
                    if (isTag)
                        _cmbTagFamily.Items.Add(fam.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ══════════════════════════════════════════════════════════════════

        private void OnImportChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cmbImport.SelectedItem is ComboBoxItem item && item.Tag is ImportInstance imp)
                _selectedImport = imp;
        }

        private void OnAnalyze(object sender, RoutedEventArgs e)
        {
            if (_selectedImport == null) { _txtStatus.Text = "No DWG import selected"; return; }

            try
            {
                _txtStatus.Text = "Analyzing DWG layers...";
                Mouse.OverrideCursor = Cursors.Wait;

                var manifest = _pipeline.ExtractLayerManifest(_selectedImport);

                // Also run geometry extraction for shape detection
                _extraction = _pipeline.ExtractStructuralGeometry(_selectedImport);

                // Build layer rows
                _layerRows.Clear();
                var lineCountsByLayer = CountLinesByLayer(_selectedImport);
                var arcCountsByLayer = CountArcsByLayer(_selectedImport);

                foreach (var kvp in manifest.OrderByDescending(x => x.Value.Confidence)
                    .ThenByDescending(x => x.Value.Count))
                {
                    bool isStruct = kvp.Value.Confidence > 0;
                    string autoMap = "";
                    if (isStruct)
                    {
                        var cls = kvp.Value.Classification;
                        if (cls.Contains("Column")) autoMap = "Column";
                        else if (cls.Contains("Beam") || cls.Contains("Framing")) autoMap = "Beam";
                        else if (cls.Contains("Wall") || cls.Contains("Shear") || cls.Contains("Core") || cls.Contains("Retaining")) autoMap = "Wall";
                        else if (cls.Contains("Slab") || cls.Contains("Floor")) autoMap = "Slab";
                        else if (cls.Contains("Footing") || cls.Contains("Foundation") || cls.Contains("Pad") || cls.Contains("Strip") || cls.Contains("Raft") || cls.Contains("Pile")) autoMap = "Foundation";
                        else if (cls.Contains("Grid")) autoMap = "Grid";
                    }

                    _layerRows.Add(new LayerRow
                    {
                        Selected = isStruct,
                        Name = kvp.Key,
                        Entities = kvp.Value.Count,
                        Lines = lineCountsByLayer.GetValueOrDefault(kvp.Key),
                        Arcs = arcCountsByLayer.GetValueOrDefault(kvp.Key),
                        AutoDetect = isStruct ? kvp.Value.Classification : "(Unmapped)",
                        ConfidencePct = (int)(kvp.Value.Confidence * 100),
                        MapTo = autoMap
                    });
                }

                _layerGrid.Items.Refresh();
                RefreshLayerCombos();
                DetectElementSizes();

                _txtStatus.Text = $"Found {_layerRows.Count} layers, {manifest.Values.Sum(v => v.Count)} entities";
            }
            catch (Exception ex)
            {
                StingLog.Error("DWG analysis failed", ex);
                _txtStatus.Text = $"Analysis failed: {ex.Message}";
            }
            finally { Mouse.OverrideCursor = null; }
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _layerRows) r.Selected = true;
            _layerGrid.Items.Refresh();
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var r in _layerRows) r.Selected = false;
            _layerGrid.Items.Refresh();
        }

        private void OnSelectStructural(object sender, RoutedEventArgs e)
        {
            foreach (var r in _layerRows)
                r.Selected = StructuralLayerClassifier.IsStructuralLayer(r.Name);
            _layerGrid.Items.Refresh();
        }

        private void OnAutoMap(object sender, RoutedEventArgs e)
        {
            foreach (var r in _layerRows)
            {
                if (r.ConfidencePct > 0 && !string.IsNullOrEmpty(r.AutoDetect))
                {
                    r.Selected = true;
                    string cls = r.AutoDetect;
                    if (cls.Contains("Column")) r.MapTo = "Column";
                    else if (cls.Contains("Beam") || cls.Contains("Framing")) r.MapTo = "Beam";
                    else if (cls.Contains("Wall") || cls.Contains("Shear") || cls.Contains("Core")) r.MapTo = "Wall";
                    else if (cls.Contains("Slab") || cls.Contains("Floor")) r.MapTo = "Slab";
                    else if (cls.Contains("Footing") || cls.Contains("Found") || cls.Contains("Pad") || cls.Contains("Strip") || cls.Contains("Raft")) r.MapTo = "Foundation";
                    else if (cls.Contains("Grid")) r.MapTo = "Grid";
                }
            }
            _layerGrid.Items.Refresh();
            RefreshLayerCombos();
        }

        // ══════════════════════════════════════════════════════════════════
        // SHAPE DETECTION & AUTO-SIZING
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyzes extracted DWG geometry to detect element sizes.
        /// Groups circles by radius → round column sizes.
        /// Groups rectangles by width×depth → rectangular column sizes.
        /// Measures parallel line pairs → wall thicknesses.
        /// Measures closed loops → slab boundaries with area.
        /// </summary>
        private void DetectElementSizes()
        {
            _detectedGroups.Clear();
            if (_extraction == null) return;

            // ── Round columns from circles ──
            if (_extraction.Circles.Count > 0)
            {
                // Group by diameter (rounded to nearest 25mm for tolerance)
                var circleGroups = _extraction.Circles
                    .GroupBy(c => Math.Round(c.DiameterMm / 25.0) * 25)
                    .OrderByDescending(g => g.Count());

                foreach (var g in circleGroups)
                {
                    double dia = g.Key;
                    _detectedGroups.Add(new DetectedElementGroup
                    {
                        ElementType = "Column",
                        SizeLabel = $"\u00f8{dia:F0}mm",
                        Count = g.Count(),
                        WidthMm = dia,
                        DepthMm = dia,
                        IsCircular = true
                    });
                }
            }

            // ── Rectangular columns from rectangles ──
            if (_extraction.Rectangles.Count > 0)
            {
                // Group by size (nearest 25mm)
                var rectGroups = _extraction.Rectangles
                    .GroupBy(r =>
                    {
                        double w = Math.Round(r.WidthMm / 25.0) * 25;
                        double d = Math.Round(r.DepthMm / 25.0) * 25;
                        return (Math.Min(w, d), Math.Max(w, d)); // normalize orientation
                    })
                    .OrderByDescending(g => g.Count());

                foreach (var g in rectGroups)
                {
                    _detectedGroups.Add(new DetectedElementGroup
                    {
                        ElementType = "Column",
                        SizeLabel = $"{g.Key.Item1:F0}\u00d7{g.Key.Item2:F0}mm",
                        Count = g.Count(),
                        WidthMm = g.Key.Item1,
                        DepthMm = g.Key.Item2,
                        IsCircular = false
                    });
                }
            }

            // ── Beam spans from beam lines ──
            if (_extraction.BeamLines.Count > 0)
            {
                var beamLengths = _extraction.BeamLines
                    .Select(b => b.Start.DistanceTo(b.End) * Units.FeetToMm)
                    .OrderBy(l => l).ToList();

                double minSpan = beamLengths.First();
                double maxSpan = beamLengths.Last();
                double avgSpan = beamLengths.Average();

                _detectedGroups.Add(new DetectedElementGroup
                {
                    ElementType = "Beam",
                    SizeLabel = $"Spans {minSpan / 1000:F1}m–{maxSpan / 1000:F1}m (avg {avgSpan / 1000:F1}m)",
                    Count = _extraction.BeamLines.Count,
                    WidthMm = BeamWidthMm,
                    DepthMm = AutoSizeBeamDepth(avgSpan)
                });
            }

            // ── Wall thicknesses from detected walls ──
            if (_extraction.Walls.Count > 0)
            {
                var wallGroups = _extraction.Walls
                    .GroupBy(w => Math.Round(w.ThicknessFt * Units.FeetToMm / 25) * 25)
                    .OrderByDescending(g => g.Count());

                foreach (var g in wallGroups)
                {
                    _detectedGroups.Add(new DetectedElementGroup
                    {
                        ElementType = "Wall",
                        SizeLabel = $"{g.Key:F0}mm thick",
                        Count = g.Count(),
                        ThicknessMm = g.Key
                    });
                }
            }

            // ── Slab boundaries ──
            if (_extraction.SlabBoundaries.Count > 0)
            {
                _detectedGroups.Add(new DetectedElementGroup
                {
                    ElementType = "Slab",
                    SizeLabel = $"{_extraction.SlabBoundaries.Count} boundary loops detected",
                    Count = _extraction.SlabBoundaries.Count,
                    ThicknessMm = SlabThicknessMm
                });
            }

            // ── Grid lines ──
            if (_extraction.GridLines.Count > 0)
            {
                int hCount = _extraction.GridLines.Count(g => g.IsHorizontal);
                int vCount = _extraction.GridLines.Count - hCount;
                _detectedGroups.Add(new DetectedElementGroup
                {
                    ElementType = "Grid",
                    SizeLabel = $"{vCount} vertical + {hCount} horizontal",
                    Count = _extraction.GridLines.Count
                });
            }

            // Update display
            RefreshDetectedSizes();
        }

        /// <summary>
        /// Auto-size beam depth from span using BS EN 1992 span/depth ratios.
        /// Simply-supported: L/20, Continuous: L/26, Cantilever: L/8.
        /// Returns depth in mm, rounded to nearest 25mm.
        /// </summary>
        private static double AutoSizeBeamDepth(double spanMm)
        {
            // Default simply-supported ratio
            double depth = spanMm / 20.0;
            // Round up to nearest 25mm, minimum 200mm
            depth = Math.Max(200, Math.Ceiling(depth / 25.0) * 25);
            return depth;
        }

        private void RefreshDetectedSizes()
        {
            if (_detectedGroups.Count == 0)
            {
                _txtDetectedSizes.Text = "No structural elements detected in selected layers";
                return;
            }

            var parts = new List<string>();
            foreach (var g in _detectedGroups)
            {
                parts.Add($"{g.ElementType}: {g.Count}\u00d7 {g.SizeLabel}");
            }
            _txtDetectedSizes.Text = "Detected: " + string.Join("  |  ", parts);
            _txtDetectedSizes.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }

        private void RefreshLayerCombos()
        {
            var layerNames = _layerRows.Select(r => r.Name).ToList();
            layerNames.Insert(0, "(none)");

            foreach (var combo in new[] { _cmbColumnLayer, _cmbBeamLayer, _cmbWallLayer,
                _cmbSlabLayer, _cmbFoundationLayer, _cmbGridLayer })
            {
                var prev = combo.Text;
                combo.Items.Clear();
                foreach (var name in layerNames) combo.Items.Add(name);
                if (!string.IsNullOrEmpty(prev) && layerNames.Contains(prev))
                    combo.Text = prev;
            }

            // Auto-set from mapped layers
            foreach (var row in _layerRows.Where(r => !string.IsNullOrEmpty(r.MapTo)))
            {
                switch (row.MapTo)
                {
                    case "Column": if (string.IsNullOrEmpty(_cmbColumnLayer.Text) || _cmbColumnLayer.Text == "(none)") _cmbColumnLayer.Text = row.Name; break;
                    case "Beam": if (string.IsNullOrEmpty(_cmbBeamLayer.Text) || _cmbBeamLayer.Text == "(none)") _cmbBeamLayer.Text = row.Name; break;
                    case "Wall": if (string.IsNullOrEmpty(_cmbWallLayer.Text) || _cmbWallLayer.Text == "(none)") _cmbWallLayer.Text = row.Name; break;
                    case "Slab": if (string.IsNullOrEmpty(_cmbSlabLayer.Text) || _cmbSlabLayer.Text == "(none)") _cmbSlabLayer.Text = row.Name; break;
                    case "Foundation": if (string.IsNullOrEmpty(_cmbFoundationLayer.Text) || _cmbFoundationLayer.Text == "(none)") _cmbFoundationLayer.Text = row.Name; break;
                    case "Grid": if (string.IsNullOrEmpty(_cmbGridLayer.Text) || _cmbGridLayer.Text == "(none)") _cmbGridLayer.Text = row.Name; break;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GEOMETRY COUNTING HELPERS
        // ══════════════════════════════════════════════════════════════════

        private Dictionary<string, int> CountLinesByLayer(ImportInstance import)
        {
            var counts = new Dictionary<string, int>();
            try
            {
                WalkGeometryForCounts(import.get_Geometry(new Options()), counts, countLines: true);
            }
            catch (Exception ex) { StingLog.Warn($"CountLines: {ex.Message}"); }
            return counts;
        }

        private Dictionary<string, int> CountArcsByLayer(ImportInstance import)
        {
            var counts = new Dictionary<string, int>();
            try
            {
                WalkGeometryForCounts(import.get_Geometry(new Options()), counts, countLines: false);
            }
            catch (Exception ex) { StingLog.Warn($"CountArcs: {ex.Message}"); }
            return counts;
        }

        private void WalkGeometryForCounts(GeometryElement geom,
            Dictionary<string, int> counts, bool countLines, int depth = 0)
        {
            if (geom == null || depth > 10) return;
            foreach (var obj in geom)
            {
                if (obj is GeometryInstance gi)
                {
                    WalkGeometryForCounts(gi.GetInstanceGeometry(), counts, countLines, depth + 1);
                    continue;
                }

                bool match = countLines ? obj is Line : obj is Arc;
                if (!match) continue;

                string layer = "(unnamed)";
                try
                {
                    if (obj.GraphicsStyleId != ElementId.InvalidElementId)
                    {
                        var gs = _doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                        if (gs?.GraphicsStyleCategory != null)
                            layer = gs.GraphicsStyleCategory.Name;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed layer name: {ex.Message}"); }

                counts[layer] = counts.GetValueOrDefault(layer) + 1;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // EXECUTE
        // ══════════════════════════════════════════════════════════════════

        private void OnExecute(object sender, RoutedEventArgs e)
        {
            if (_selectedImport == null)
            {
                _txtStatus.Text = "Select a DWG import first";
                _txtStatus.Foreground = Brushes.Red;
                return;
            }

            // Build selected layers from both DataGrid selections and combo selections
            SelectedLayers.Clear();
            foreach (var row in _layerRows.Where(r => r.Selected))
                SelectedLayers.Add(row.Name);

            // Also add explicitly mapped layers
            foreach (var name in new[] { ColumnLayerName, BeamLayerName, WallLayerName,
                SlabLayerName, FoundationLayerName, GridLayerName })
            {
                if (!string.IsNullOrEmpty(name) && name != "(none)")
                    SelectedLayers.Add(name);
            }

            if (SelectedLayers.Count == 0 && !CreateColumns && !CreateBeams &&
                !CreateWalls && !CreateSlabs && !CreateFoundations && !CreateGrids)
            {
                _txtStatus.Text = "Select at least one layer or element type";
                _txtStatus.Foreground = Brushes.Red;
                return;
            }

            Confirmed = true;
            Close();
        }

        /// <summary>
        /// Gets the mapping of element types to their assigned layer names.
        /// Used by the pipeline to filter geometry by layer per element type.
        /// </summary>
        public Dictionary<string, string> GetLayerMappings()
        {
            var map = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ColumnLayerName) && ColumnLayerName != "(none)")
                map["Column"] = ColumnLayerName;
            if (!string.IsNullOrEmpty(BeamLayerName) && BeamLayerName != "(none)")
                map["Beam"] = BeamLayerName;
            if (!string.IsNullOrEmpty(WallLayerName) && WallLayerName != "(none)")
                map["Wall"] = WallLayerName;
            if (!string.IsNullOrEmpty(SlabLayerName) && SlabLayerName != "(none)")
                map["Slab"] = SlabLayerName;
            if (!string.IsNullOrEmpty(FoundationLayerName) && FoundationLayerName != "(none)")
                map["Foundation"] = FoundationLayerName;
            if (!string.IsNullOrEmpty(GridLayerName) && GridLayerName != "(none)")
                map["Grid"] = GridLayerName;

            // Also include DataGrid MapTo assignments
            foreach (var row in _layerRows.Where(r => r.Selected && !string.IsNullOrEmpty(r.MapTo)))
            {
                if (!map.ContainsKey(row.MapTo))
                    map[row.MapTo] = row.Name;
                else
                    map[row.MapTo] += "|" + row.Name; // pipe-delimited multi-layer
            }

            return map;
        }

        // ══════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a section panel with a colored header title and a content area.
        /// Callers add their content to the returned SectionPanel.Content property.
        /// </summary>
        private static SectionPanel MakeSection(string title)
        {
            return new SectionPanel(title);
        }

        /// <summary>Panel with a header and content area for compact section layout.</summary>
        private class SectionPanel : Border
        {
            private readonly ContentControl _content;

            public SectionPanel(string title)
            {
                BorderBrush = BorderClr;
                BorderThickness = new Thickness(1);
                CornerRadius = new CornerRadius(4);
                Background = SectionBg;
                Margin = new Thickness(0, 0, 0, 8);

                var header = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF0)),
                    Padding = new Thickness(8, 4, 8, 4),
                    CornerRadius = new CornerRadius(4, 4, 0, 0)
                };
                header.Child = new TextBlock
                {
                    Text = title, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = HeaderBg
                };

                _content = new ContentControl();
                var dock = new DockPanel();
                DockPanel.SetDock(header, Dock.Top);
                dock.Children.Add(header);
                dock.Children.Add(_content);
                base.Child = dock;
            }

            /// <summary>Sets the content of this section panel.</summary>
            public new UIElement Child
            {
                get => _content.Content as UIElement;
                set => _content.Content = value;
            }
        }

        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x88)),
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private static TextBlock MakeLbl(string text, int col)
        {
            var lbl = new TextBlock
            {
                Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = LabelFg
            };
            Grid.SetColumn(lbl, col);
            return lbl;
        }

        private static Button MakeBtn(string text, RoutedEventHandler handler,
            SolidColorBrush bg = null, SolidColorBrush fg = null, bool bold = false)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 90, Height = 28,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 11
            };
            if (bg != null) btn.Background = bg;
            if (fg != null) btn.Foreground = fg;
            if (bold) btn.FontWeight = FontWeights.Bold;
            btn.Click += handler;
            return btn;
        }

        private static Button MakeSmallBtn(string text, RoutedEventHandler handler,
            SolidColorBrush bg = null)
        {
            var btn = new Button
            {
                Content = text, Height = 22, MinWidth = 70,
                FontSize = 10, Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(6, 1, 6, 1)
            };
            if (bg != null) { btn.Background = bg; btn.Foreground = Brushes.White; }
            btn.Click += handler;
            return btn;
        }

        private static DataGridTextColumn MakeTextCol(string header, string binding, int width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = width,
                IsReadOnly = true
            };
        }

        private static StackPanel MakeFieldRow(string label, out ComboBox combo)
        {
            combo = new ComboBox { FontSize = 11, Height = 24, Margin = new Thickness(0, 2, 0, 2) };
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = LabelFg });
            row.Children.Add(combo);
            return row;
        }

        private static StackPanel MakePropsGroup(string title,
            (string Label, string Default, TextBox Tb)[] fields, int labelWidth = 85)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            var titleBorder = new Border
            {
                Background = AccentBrush, Padding = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            titleBorder.Child = new TextBlock
            {
                Text = title, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            panel.Children.Add(titleBorder);

            foreach (var (label, defaultVal, tb) in fields)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11, Width = 85,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = LabelFg
                });
                tb.Text = defaultVal;
                tb.Width = 60;
                tb.FontSize = 11;
                tb.Height = 22;
                tb.TextChanged += (s, e) => ValidateNumericInput(tb);
                row.Children.Add(tb);
                panel.Children.Add(row);
            }

            return panel;
        }

        private static StackPanel MakeMiniPropRow(string title,
            string lbl1, string def1, out TextBox tb1,
            string lbl2, string def2, out TextBox tb2)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var titleBlock = new TextBlock
            {
                Text = title + ":", FontSize = 10, FontWeight = FontWeights.Bold,
                Width = 40, VerticalAlignment = VerticalAlignment.Center,
                Foreground = LabelFg
            };
            panel.Children.Add(titleBlock);
            panel.Children.Add(MakeMiniField(lbl1, def1, out tb1));
            panel.Children.Add(MakeMiniField(lbl2, def2, out tb2));
            return panel;
        }

        private static StackPanel MakeMiniField(string label, string defaultVal, out TextBox tb)
        {
            tb = new TextBox { Text = defaultVal, Width = 50, FontSize = 10, Height = 20 };
            TextBox localTb = tb;
            localTb.TextChanged += (s, e) => ValidateNumericInput(localTb);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0), Foreground = LabelFg
            });
            panel.Children.Add(tb);
            return panel;
        }

        private static void ValidateNumericInput(TextBox tb)
        {
            if (double.TryParse(tb.Text, out double val) && val > 0)
                tb.Background = Brushes.White;
            else
                tb.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0xDD));
        }

        private static double ParseDbl(TextBox tb, double fallback)
        {
            if (tb != null && double.TryParse(tb.Text, out double val) && val > 0)
                return val;
            return fallback;
        }

        private static string GetLayerFromCombo(ComboBox combo)
        {
            return combo?.Text?.Trim() ?? "";
        }

        // ── Static Show ──────────────────────────────────────────────────

        /// <summary>
        /// Shows the dialog and returns the configured instance.
        /// Check dialog.Confirmed before using results.
        /// </summary>
        public static StructuralDWGDialog Show(Document doc)
        {
            var dlg = new StructuralDWGDialog(doc);
            dlg.ShowDialog();
            return dlg;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // BACKWARDS COMPATIBILITY: Keep old class name as alias
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Legacy alias for the old wizard — redirects to the new compact dialog.
    /// </summary>
    public class StructuralCADWizard : StructuralDWGDialog
    {
        public StructuralCADWizard(Document doc) : base(doc) { }

        public new static StructuralDWGDialog Show(Document doc)
        {
            return StructuralDWGDialog.Show(doc);
        }
    }
}
