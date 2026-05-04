// ============================================================================
// StructuralDWGWizard.cs — Comprehensive WPF Wizard for DWG-to-Structural BIM
//
// v1.0 — Complete rewrite of the structural CAD automation wizard addressing:
//   - Per-element-type layer mapping with dropdowns
//   - Height/thickness/material configuration per element type
//   - Element joining logic options
//   - Structural detail reading (shear walls, foundations, bracing)
//   - STING tagging/numbering integration
//   - Smart geometry detection with user validation
//   - Type creation from detected line shapes
//
// 7 Pages:
//   1. DWG Selection & Layer Analysis
//   2. Layer-to-Element Mapping (per element type with dropdown)
//   3. Element Properties (height, thickness, material per type)
//   4. Structural Options (joining, shear walls, foundations)
//   5. Tagging & Numbering Configuration
//   6. Detection Preview (elements to be created with counts)
//   7. Summary & Execute
//
// Inspired by: ETLIPS (layer mapping), AGACAD (WPF wizard), Naviate (templates),
//   Graitec BIM Connect (structural intelligence), DiRoots (column mapping UI)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.Model
{
    #region Configuration Result

    /// <summary>
    /// Complete configuration result from the DWG-to-Structural wizard.
    /// Contains all user selections for the modeling engine.
    /// </summary>
    public class StructuralDWGConfig
    {
        public bool Confirmed { get; set; }
        public ImportInstance SelectedImport { get; set; }

        // ── Layer Mapping ──
        /// <summary>Maps element type (Wall/Column/Beam/Slab/Foundation/Bracing/ShearWall/Grid)
        /// to list of DWG layer names assigned to that element type.</summary>
        public Dictionary<string, List<string>> LayerMapping { get; set; } = new();

        // ── Element Properties ──
        public double WallHeightMm { get; set; } = 3000;
        public double WallThicknessMm { get; set; } = 200;
        public string WallMaterial { get; set; } = "Concrete";
        public bool WallIsStructural { get; set; } = true;

        public double ColumnHeightMm { get; set; } = 3000;
        public double ColumnWidthMm { get; set; } = 300;
        public double ColumnDepthMm { get; set; } = 300;
        public string ColumnMaterial { get; set; } = "Concrete";
        public string ColumnShape { get; set; } = "Rectangular"; // Rectangular, Circular

        public double BeamDepthMm { get; set; } = 450;
        public double BeamWidthMm { get; set; } = 250;
        public string BeamMaterial { get; set; } = "Concrete";

        public double SlabThicknessMm { get; set; } = 200;
        public string SlabMaterial { get; set; } = "Concrete";

        public double FoundationDepthMm { get; set; } = 600;
        public double FoundationWidthMm { get; set; } = 1200;
        public string FoundationMaterial { get; set; } = "Concrete";
        public string FoundationType { get; set; } = "Pad"; // Pad, Strip, Raft

        public double ShearWallHeightMm { get; set; } = 3000;
        public double ShearWallThicknessMm { get; set; } = 250;

        public double BracingDepthMm { get; set; } = 200;

        // ── Structural Options ──
        public bool AutoJoinWalls { get; set; } = true;
        public bool AutoJoinColumns { get; set; } = true;
        public bool AutoExtendBeams { get; set; } = true;
        public bool DetectShearWalls { get; set; } = true;
        public bool DetectBracing { get; set; } = false;
        public bool CreateGridLines { get; set; } = true;
        public bool DetectFoundations { get; set; } = true;
        public bool MergeCollinearWalls { get; set; } = true;
        public bool SnapToGrid { get; set; } = true;
        public double SnapToleranceMm { get; set; } = 10;
        public double EndpointToleranceMm { get; set; } = 5;
        public double ParallelLineToleranceMm { get; set; } = 50;

        // ── Level Configuration ──
        public ElementId BaseLevelId { get; set; }
        public ElementId TopLevelId { get; set; }

        // ── Tagging ──
        public bool AutoTag { get; set; } = true;
        public bool AutoNumber { get; set; } = true;
        public string TagPrefix { get; set; } = "";
        public string NumberingScheme { get; set; } = "ByType"; // ByType, ByLevel, Sequential

        // ── Detection ──
        public double MinColumnSizeMm { get; set; } = 150;
        public double MaxColumnSizeMm { get; set; } = 1200;
        public double MinBeamLengthMm { get; set; } = 500;
        public double MinWallLengthMm { get; set; } = 300;
        public double MinSlabAreaM2 { get; set; } = 1.0;

        // ── Type Creation ──
        public bool CreateNewTypes { get; set; } = true;
        public string TypeNamingPrefix { get; set; } = "STING";

        /// <summary>
        /// Phase 77: Validate all dimension properties are within safe ranges.
        /// Returns null if valid, or an error message describing the first invalid value.
        /// </summary>
        public string ValidateDimensions()
        {
            if (WallHeightMm < 500 || WallHeightMm > 15000) return $"Wall height {WallHeightMm}mm is outside valid range (500-15000mm)";
            if (WallThicknessMm < 50 || WallThicknessMm > 2000) return $"Wall thickness {WallThicknessMm}mm is outside valid range (50-2000mm)";
            if (ColumnHeightMm < 500 || ColumnHeightMm > 15000) return $"Column height {ColumnHeightMm}mm is outside valid range (500-15000mm)";
            if (ColumnWidthMm < 100 || ColumnWidthMm > 3000) return $"Column width {ColumnWidthMm}mm is outside valid range (100-3000mm)";
            if (ColumnDepthMm < 100 || ColumnDepthMm > 3000) return $"Column depth {ColumnDepthMm}mm is outside valid range (100-3000mm)";
            if (BeamDepthMm < 100 || BeamDepthMm > 3000) return $"Beam depth {BeamDepthMm}mm is outside valid range (100-3000mm)";
            if (BeamWidthMm < 50 || BeamWidthMm > 1500) return $"Beam width {BeamWidthMm}mm is outside valid range (50-1500mm)";
            if (SlabThicknessMm < 50 || SlabThicknessMm > 1000) return $"Slab thickness {SlabThicknessMm}mm is outside valid range (50-1000mm)";
            if (FoundationDepthMm < 200 || FoundationDepthMm > 5000) return $"Foundation depth {FoundationDepthMm}mm is outside valid range (200-5000mm)";
            if (FoundationWidthMm < 300 || FoundationWidthMm > 5000) return $"Foundation width {FoundationWidthMm}mm is outside valid range (300-5000mm)";
            if (EndpointToleranceMm < 0.1 || EndpointToleranceMm > 100) return $"Endpoint tolerance {EndpointToleranceMm}mm is outside valid range (0.1-100mm)";
            if (ParallelLineToleranceMm < 1 || ParallelLineToleranceMm > 500) return $"Parallel line tolerance {ParallelLineToleranceMm}mm is outside valid range (1-500mm)";
            return null; // All valid
        }
    }

    #endregion

    #region Layer Info

    /// <summary>Information about a DWG layer for mapping UI.</summary>
    public class DWGLayerInfo
    {
        public string Name { get; set; }
        public int EntityCount { get; set; }
        public int LineCount { get; set; }
        public int ArcCount { get; set; }
        public int BlockCount { get; set; }
        public string AutoCategory { get; set; } // Auto-detected category
        public double Confidence { get; set; }
        public bool IsVisible { get; set; } = true;
        public System.Windows.Media.Color LayerColor { get; set; }
    }

    #endregion

    /// <summary>
    /// Comprehensive 7-page WPF wizard for DWG-to-Structural BIM conversion.
    /// Provides full user control over layer mapping, element properties,
    /// joining logic, and tagging integration.
    /// </summary>
    public class StructuralDWGWizard : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly Document _doc;
        private int _currentPage = 0;
        private readonly List<Func<FrameworkElement>> _pageBuilders = new();
        private readonly List<FrameworkElement> _builtPages = new();

        // DWG Analysis
        private ImportInstance _selectedImport;
        private List<DWGLayerInfo> _layers = new();
        #pragma warning disable CS0169
        private StructuralExtractionResult _extraction;
        #pragma warning restore CS0169

        // Result
        public StructuralDWGConfig Config { get; private set; } = new();

        // UI Elements
        private ContentControl _pageHost;
        private Button _btnBack, _btnNext, _btnFinish;
        private TextBlock _statusText, _pageTitle, _pageSubtitle;
        private StackPanel _stepIndicator;

        // Layer mapping controls
        private readonly Dictionary<string, ComboBox> _layerDropdowns = new();
        private readonly Dictionary<string, List<CheckBox>> _layerCheckboxes = new();

        // Property controls
        private readonly Dictionary<string, TextBox> _propTextBoxes = new();
        private readonly Dictionary<string, ComboBox> _propCombos = new();

        // Levels
        private ComboBox _baseLevelCombo, _topLevelCombo;

        private static readonly string[] PageTitles = {
            "DWG Selection & Layer Analysis",
            "Layer-to-Element Mapping",
            "Element Properties",
            "Structural Options",
            "Tagging & Numbering",
            "Detection Preview",
            "Summary & Execute"
        };

        private static readonly string[] PageDescriptions = {
            "Select the imported DWG and analyze its layers",
            "Map each DWG layer to a structural element type",
            "Configure height, thickness, and material for each element type",
            "Set joining rules, detection options, and precision tolerances",
            "Configure STING ISO 19650 tagging and numbering",
            "Preview all elements that will be created",
            "Review settings and start the conversion"
        };

        // Element types for mapping
        public static readonly string[] ElementTypes = {
            "(Skip)", "Wall", "Column", "Beam", "Slab", "Foundation",
            "Shear Wall", "Bracing", "Grid Line", "Stair", "Ramp"
        };

        // Materials
        public static readonly string[] Materials = {
            "Concrete", "Steel", "Timber", "Masonry", "Composite",
            "Reinforced Concrete", "Precast Concrete", "Steel (S275)",
            "Steel (S355)", "Glulam Timber", "CLT", "Blockwork"
        };

        // ── Colors ──────────────────────────────────────────────────────
        private static readonly Color HeaderBg = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color SuccessColor = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color BodyBg = Color.FromRgb(0xF8, 0xF8, 0xFA);
        private static readonly Color CardBg = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color BorderCol = Color.FromRgb(0xDC, 0xDC, 0xE6);
        private static readonly Color TextPrimary = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color TextSecondary = Color.FromRgb(0x66, 0x66, 0x66);

        // ══════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════

        public StructuralDWGWizard(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            Title = "STING Structural DWG-to-BIM Wizard";
            Width = 960;
            Height = 720;
            MinWidth = 800;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = new SolidColorBrush(BodyBg);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Window owner: {ex.Message}"); }

            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { Config.Confirmed = false; Close(); }
            };

            _pageBuilders.Add(BuildPage1_DWGSelection);
            _pageBuilders.Add(BuildPage2_LayerMapping);
            _pageBuilders.Add(BuildPage3_Properties);
            _pageBuilders.Add(BuildPage4_StructuralOptions);
            _pageBuilders.Add(BuildPage5_Tagging);
            _pageBuilders.Add(BuildPage6_Preview);
            _pageBuilders.Add(BuildPage7_Summary);

            for (int i = 0; i < _pageBuilders.Count; i++)
                _builtPages.Add(null);

            BuildLayout();
            ShowPage(0);
        }

        // ══════════════════════════════════════════════════════════════════
        // STATIC SHOW
        // ══════════════════════════════════════════════════════════════════

        public static StructuralDWGConfig Show(Document doc)
        {
            var wizard = new StructuralDWGWizard(doc);
            wizard.ShowDialog();
            return wizard.Config;
        }

        // ══════════════════════════════════════════════════════════════════
        // LAYOUT
        // ══════════════════════════════════════════════════════════════════

        private void BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });   // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });   // Footer

            // ── Header ──
            var header = new Border
            {
                Background = new SolidColorBrush(HeaderBg),
                Padding = new Thickness(20, 12, 20, 12),
            };
            var headerStack = new StackPanel();
            _pageTitle = new TextBlock
            {
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
            };
            _pageSubtitle = new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xD0)),
                Margin = new Thickness(0, 2, 0, 4),
            };
            _stepIndicator = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
            };
            headerStack.Children.Add(_pageTitle);
            headerStack.Children.Add(_pageSubtitle);
            headerStack.Children.Add(_stepIndicator);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Page host ──
            _pageHost = new ContentControl { Margin = new Thickness(20, 12, 20, 8) };
            Grid.SetRow(_pageHost, 1);
            root.Children.Add(_pageHost);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF4)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 8, 20, 8),
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(TextSecondary),
                FontSize = 11,
            };
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnBack = MakeButton("← Back", false);
            _btnBack.Click += (s, e) => NavigateBack();
            _btnNext = MakeButton("Next →", true);
            _btnNext.Click += (s, e) => NavigateNext();
            _btnFinish = MakeButton("★ Build Model", true);
            _btnFinish.Background = new SolidColorBrush(SuccessColor);
            _btnFinish.Click += (s, e) => Execute();
            var btnCancel = MakeButton("Cancel", false);
            btnCancel.Click += (s, e) => { Config.Confirmed = false; Close(); };

            btnPanel.Children.Add(_btnBack);
            btnPanel.Children.Add(_btnNext);
            btnPanel.Children.Add(_btnFinish);
            btnPanel.Children.Add(btnCancel);
            Grid.SetColumn(btnPanel, 1);
            footerGrid.Children.Add(btnPanel);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // ══════════════════════════════════════════════════════════════════
        // NAVIGATION
        // ══════════════════════════════════════════════════════════════════

        private void ShowPage(int index)
        {
            if (index < 0 || index >= _pageBuilders.Count) return;
            _currentPage = index;

            if (_builtPages[index] == null)
                _builtPages[index] = _pageBuilders[index]();

            _pageHost.Content = _builtPages[index];
            _pageTitle.Text = $"Step {index + 1}/{_pageBuilders.Count}: {PageTitles[index]}";
            _pageSubtitle.Text = PageDescriptions[index];

            _btnBack.Visibility = index > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _btnNext.Visibility = index < _pageBuilders.Count - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _btnFinish.Visibility = index == _pageBuilders.Count - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            UpdateStepIndicator();
            UpdateStatus();
        }

        private void NavigateBack() { if (_currentPage > 0) ShowPage(_currentPage - 1); }

        private void NavigateNext()
        {
            string err = ValidateCurrentPage();
            if (err != null)
            {
                MessageBox.Show(err, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CollectCurrentPageData();

            // Rebuild next page if it depends on previous data
            if (_currentPage == 0 || _currentPage == 1)
                _builtPages[_currentPage + 1] = null;
            if (_currentPage == 4) // rebuild preview after tagging config
                _builtPages[5] = null;
            if (_currentPage == 5) // rebuild summary
                _builtPages[6] = null;

            ShowPage(_currentPage + 1);
        }

        private void Execute()
        {
            CollectCurrentPageData();
            Config.Confirmed = true;
            Config.SelectedImport = _selectedImport;
            Close();
        }

        private string ValidateCurrentPage()
        {
            switch (_currentPage)
            {
                case 0: // DWG Selection
                    if (_selectedImport == null)
                        return "Please select a DWG import instance.";
                    if (_layers.Count == 0)
                        return "Please click 'Analyze Layers' to scan the DWG.";
                    break;
                case 1: // Layer Mapping
                    bool anyMapped = false;
                    foreach (var kvp in _layerCheckboxes)
                    {
                        foreach (var cb in kvp.Value)
                        {
                            if (cb.IsChecked == true) { anyMapped = true; break; }
                        }
                        if (anyMapped) break;
                    }
                    if (!anyMapped)
                        return "Please assign at least one layer to an element type.";
                    break;
            }
            return null;
        }

        private void CollectCurrentPageData()
        {
            switch (_currentPage)
            {
                case 1: // Layer Mapping
                    Config.LayerMapping.Clear();
                    foreach (var kvp in _layerCheckboxes)
                    {
                        foreach (var cb in kvp.Value)
                        {
                            if (cb.IsChecked == true)
                            {
                                string elemType = kvp.Key;
                                string layerName = cb.Tag as string ?? "";
                                if (!Config.LayerMapping.ContainsKey(elemType))
                                    Config.LayerMapping[elemType] = new List<string>();
                                Config.LayerMapping[elemType].Add(layerName);
                            }
                        }
                    }
                    break;

                case 2: // Properties
                    CollectProperties();
                    break;

                case 3: // Structural Options
                    CollectOptions();
                    break;

                case 4: // Tagging
                    CollectTagging();
                    break;
            }

            // Level
            if (_baseLevelCombo?.SelectedItem is ComboBoxItem baseItem && baseItem.Tag is ElementId baseId)
                Config.BaseLevelId = baseId;
            if (_topLevelCombo?.SelectedItem is ComboBoxItem topItem && topItem.Tag is ElementId topId)
                Config.TopLevelId = topId;
        }

        private void UpdateStepIndicator()
        {
            _stepIndicator.Children.Clear();
            for (int i = 0; i < PageTitles.Length; i++)
            {
                var circle = new Border
                {
                    Width = 22, Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = i < _currentPage ? new SolidColorBrush(SuccessColor)
                        : i == _currentPage ? new SolidColorBrush(AccentColor)
                        : new SolidColorBrush(Color.FromRgb(0x50, 0x58, 0x80)),
                    Margin = new Thickness(0, 0, 2, 0),
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                };
                _stepIndicator.Children.Add(circle);

                if (i < PageTitles.Length - 1)
                {
                    _stepIndicator.Children.Add(new Border
                    {
                        Width = 16, Height = 2, Margin = new Thickness(0, 0, 2, 0),
                        Background = i < _currentPage ? new SolidColorBrush(SuccessColor)
                            : new SolidColorBrush(Color.FromRgb(0x50, 0x58, 0x80)),
                    });
                }
            }
        }

        private void UpdateStatus()
        {
            int mapped = Config.LayerMapping.Values.Sum(v => v.Count);
            _statusText.Text = $"Import: {(_selectedImport != null ? "Selected" : "None")} | " +
                $"Layers: {_layers.Count} | Mapped: {mapped}";
        }


        // ══════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        private Button MakeButton(string text, bool primary)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 90, Height = 32,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(14, 4, 14, 4),
                FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (primary)
            {
                btn.Background = new SolidColorBrush(AccentColor);
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(AccentColor);
            }
            else
            {
                btn.Background = Brushes.White;
                btn.Foreground = new SolidColorBrush(TextPrimary);
                btn.BorderBrush = new SolidColorBrush(BorderCol);
            }
            return btn;
        }

        private Border MakeCard(string title, UIElement content)
        {
            var stack = new StackPanel();
            if (!string.IsNullOrEmpty(title))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = title.ToUpperInvariant(),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(HeaderBg),
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
            stack.Children.Add(content);

            return new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = stack,
            };
        }

        private static TextBlock MakeLabel(string text, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontSize = 12,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(TextPrimary),
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static TextBox MakeTextBox(string defaultVal, double width = 80)
        {
            return new TextBox
            {
                Text = defaultVal, Width = width, Height = 26,
                FontSize = 12, Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
        }

        private static ComboBox MakeCombo(string[] items, int selectedIndex = 0, double width = 160)
        {
            var combo = new ComboBox
            {
                Width = width, Height = 26, FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
            };
            foreach (var item in items)
                combo.Items.Add(new ComboBoxItem { Content = item });
            if (selectedIndex >= 0 && selectedIndex < items.Length)
                combo.SelectedIndex = selectedIndex;
            return combo;
        }

        private ScrollViewer WrapScroll(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE 1: DWG Selection & Layer Analysis
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPage1_DWGSelection()
        {
            var mainStack = new StackPanel();

            // DWG import selection
            var imports = CADToModelEngine.FindImportInstances(_doc);
            var importCombo = new ComboBox { Height = 28, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var imp in imports)
            {
                string name = "DWG Import";
                try { name = imp.Name ?? $"Import #{imp.Id.Value}"; }
                catch (Exception ex) { StingLog.Warn($"Import name: {ex.Message}"); }
                importCombo.Items.Add(new ComboBoxItem { Content = name, Tag = imp });
            }
            if (imports.Count > 0) { importCombo.SelectedIndex = 0; _selectedImport = imports[0]; }
            importCombo.SelectionChanged += (s, e) =>
            {
                if (importCombo.SelectedItem is ComboBoxItem item && item.Tag is ImportInstance imp)
                    _selectedImport = imp;
            };

            mainStack.Children.Add(MakeCard("Select DWG Import", importCombo));

            // Layer analysis results
            var layerPanel = new StackPanel();
            var layerScroll = new ScrollViewer
            {
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = layerPanel,
            };

            var analyzeBtn = MakeButton("★ Analyze DWG Layers", true);
            analyzeBtn.Margin = new Thickness(0, 0, 0, 10);
            analyzeBtn.Click += (s, e) =>
            {
                if (_selectedImport == null) return;
                _layers.Clear();
                layerPanel.Children.Clear();

                try
                {
                    var geoElem = _selectedImport.get_Geometry(new Options());
                    if (geoElem == null) return;

                    var layerData = new Dictionary<string, DWGLayerInfo>();

                    foreach (var geoObj in geoElem)
                    {
                        if (geoObj is GeometryInstance gi)
                        {
                            foreach (var subGeo in gi.GetInstanceGeometry())
                            {
                                string layer = null;
                                try
                                {
                                    var gStyle = _doc.GetElement(subGeo.GraphicsStyleId) as GraphicsStyle;
                                    layer = gStyle?.GraphicsStyleCategory?.Name;
                                }
                                catch (Exception ex) { StingLog.Warn($"Layer read: {ex.Message}"); }

                                if (string.IsNullOrEmpty(layer)) layer = "(Unknown)";

                                if (!layerData.ContainsKey(layer))
                                    layerData[layer] = new DWGLayerInfo { Name = layer };

                                var info = layerData[layer];
                                info.EntityCount++;
                                if (subGeo is Line || subGeo is PolyLine) info.LineCount++;
                                else if (subGeo is Arc) info.ArcCount++;
                            }
                        }
                    }

                    // Auto-detect categories and confidence
                    foreach (var kvp in layerData)
                    {
                        var info = kvp.Value;
                        string cat = LayerMapper.InferCategory(info.Name);
                        info.AutoCategory = cat ?? "(Unmapped)";
                        info.Confidence = cat != null
                            ? (StructuralLayerClassifier.IsStructuralLayer(info.Name) ? 0.9 : 0.6)
                            : 0.0;
                    }

                    _layers = layerData.Values
                        .OrderByDescending(l => l.Confidence)
                        .ThenByDescending(l => l.EntityCount)
                        .ToList();

                    // Build layer list UI
                    foreach (var layer in _layers)
                    {
                        bool isStruct = layer.Confidence > 0.5;
                        var row = new Grid();
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                        row.Margin = new Thickness(0, 1, 0, 1);

                        var nameTb = new TextBlock
                        {
                            Text = layer.Name, FontSize = 11,
                            FontWeight = isStruct ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = isStruct
                                ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))
                                : new SolidColorBrush(TextSecondary),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetColumn(nameTb, 0);
                        row.Children.Add(nameTb);

                        AddCol(row, 1, $"{layer.EntityCount} ent.");
                        AddCol(row, 2, $"{layer.LineCount} lines");
                        AddCol(row, 3, $"{layer.ArcCount} arcs");
                        AddCol(row, 4, layer.AutoCategory);

                        if (isStruct)
                        {
                            var conf = new TextBlock
                            {
                                Text = $"{layer.Confidence * 100:F0}%",
                                FontSize = 10, Foreground = new SolidColorBrush(SuccessColor),
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetColumn(conf, 5);
                            row.Children.Add(conf);
                        }

                        layerPanel.Children.Add(row);
                    }

                    _statusText.Text = $"Found {_layers.Count} layers, {_layers.Sum(l => l.EntityCount)} entities";
                }
                catch (Exception ex)
                {
                    StingLog.Error("DWG layer analysis failed", ex);
                    layerPanel.Children.Add(new TextBlock { Text = $"Error: {ex.Message}", Foreground = Brushes.Red });
                }
            };

            // Header row
            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            AddCol(headerRow, 0, "Layer Name", true);
            AddCol(headerRow, 1, "Entities", true);
            AddCol(headerRow, 2, "Lines", true);
            AddCol(headerRow, 3, "Arcs", true);
            AddCol(headerRow, 4, "Auto-Detect", true);
            AddCol(headerRow, 5, "Conf.", true);

            var layerCard = new StackPanel();
            layerCard.Children.Add(analyzeBtn);
            layerCard.Children.Add(headerRow);
            layerCard.Children.Add(new Border
            {
                Height = 1, Background = new SolidColorBrush(BorderCol),
                Margin = new Thickness(0, 2, 0, 4),
            });
            layerCard.Children.Add(layerScroll);

            mainStack.Children.Add(MakeCard("DWG Layer Analysis", layerCard));

            // Level selection
            var levelGrid = new Grid();
            levelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            levelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            var baseLevelStack = new StackPanel();
            baseLevelStack.Children.Add(MakeLabel("Base Level:"));
            _baseLevelCombo = new ComboBox { Height = 26, FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var lvl in levels)
                _baseLevelCombo.Items.Add(new ComboBoxItem { Content = lvl.Name, Tag = lvl.Id });
            if (levels.Count > 0) _baseLevelCombo.SelectedIndex = 0;
            baseLevelStack.Children.Add(_baseLevelCombo);
            Grid.SetColumn(baseLevelStack, 0);
            levelGrid.Children.Add(baseLevelStack);

            var topLevelStack = new StackPanel();
            topLevelStack.Children.Add(MakeLabel("Top Level:"));
            _topLevelCombo = new ComboBox { Height = 26, FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var lvl in levels)
                _topLevelCombo.Items.Add(new ComboBoxItem { Content = lvl.Name, Tag = lvl.Id });
            if (levels.Count > 1) _topLevelCombo.SelectedIndex = 1;
            else if (levels.Count > 0) _topLevelCombo.SelectedIndex = 0;
            topLevelStack.Children.Add(_topLevelCombo);
            Grid.SetColumn(topLevelStack, 1);
            levelGrid.Children.Add(topLevelStack);

            mainStack.Children.Add(MakeCard("Level Configuration", levelGrid));

            return WrapScroll(mainStack);
        }

        private static void AddCol(Grid grid, int col, string text, bool bold = false)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 11,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(bold ? TextPrimary : TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }


        // ══════════════════════════════════════════════════════════════════
        // PAGE 2: Layer-to-Element Mapping
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPage2_LayerMapping()
        {
            var mainStack = new StackPanel();

            mainStack.Children.Add(new TextBlock
            {
                Text = "For each element type, select which DWG layers contain that element.\n" +
                       "Layers are pre-selected based on auto-detection. Adjust as needed.",
                FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });

            // Quick actions
            var quickPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var autoMapBtn = MakeButton("Auto-Map All", true);
            autoMapBtn.Click += (s, e) => AutoMapLayers();
            var clearBtn = MakeButton("Clear All", false);
            clearBtn.Click += (s, e) => ClearAllMappings();
            quickPanel.Children.Add(autoMapBtn);
            quickPanel.Children.Add(clearBtn);
            mainStack.Children.Add(quickPanel);

            _layerCheckboxes.Clear();

            // Build a section per element type
            string[] structTypes = { "Wall", "Column", "Beam", "Slab", "Foundation", "Shear Wall", "Bracing", "Grid Line" };
            string[] typeDescriptions = {
                "Parallel lines / closed rectangles representing walls",
                "Small rectangles / circles representing column cross-sections",
                "Lines between columns representing beams/lintels",
                "Closed loops / hatched areas representing floor slabs",
                "Rectangles below columns representing pad/strip foundations",
                "Thick walls with specific markings (shear/core walls)",
                "Diagonal lines between columns (X-bracing, K-bracing)",
                "Long straight lines spanning the drawing (grid/axis lines)"
            };

            for (int t = 0; t < structTypes.Length; t++)
            {
                string elemType = structTypes[t];
                string desc = typeDescriptions[t];

                var typeStack = new StackPanel();
                typeStack.Children.Add(new TextBlock
                {
                    Text = desc, FontSize = 10,
                    Foreground = new SolidColorBrush(TextSecondary),
                    Margin = new Thickness(0, 0, 0, 6),
                });

                // Layer checkboxes for this element type
                var cbList = new List<CheckBox>();
                var layerWrap = new WrapPanel();

                foreach (var layer in _layers)
                {
                    bool autoSelected = IsLayerAutoMapped(layer, elemType);

                    var cb = new CheckBox
                    {
                        Content = $"{layer.Name} ({layer.EntityCount})",
                        IsChecked = autoSelected,
                        Tag = layer.Name,
                        Margin = new Thickness(0, 1, 16, 1),
                        FontSize = 11,
                        MinWidth = 200,
                    };
                    cbList.Add(cb);
                    layerWrap.Children.Add(cb);
                }

                _layerCheckboxes[elemType] = cbList;
                typeStack.Children.Add(layerWrap);

                // Element type icon color
                var iconColor = elemType switch
                {
                    "Wall" => Color.FromRgb(0x42, 0x42, 0x42),
                    "Column" => Color.FromRgb(0xC6, 0x28, 0x28),
                    "Beam" => Color.FromRgb(0x15, 0x65, 0xC0),
                    "Slab" => Color.FromRgb(0x2E, 0x7D, 0x32),
                    "Foundation" => Color.FromRgb(0x6A, 0x1B, 0x9A),
                    "Shear Wall" => Color.FromRgb(0xE6, 0x51, 0x00),
                    "Bracing" => Color.FromRgb(0x00, 0x69, 0x7C),
                    "Grid Line" => Color.FromRgb(0x78, 0x78, 0x78),
                    _ => TextPrimary,
                };

                var cardBorder = MakeCard(elemType, typeStack);
                cardBorder.BorderBrush = new SolidColorBrush(iconColor);
                cardBorder.BorderThickness = new Thickness(3, 1, 1, 1);
                mainStack.Children.Add(cardBorder);
            }

            return WrapScroll(mainStack);
        }

        private bool IsLayerAutoMapped(DWGLayerInfo layer, string elemType)
        {
            string lower = layer.Name.ToLowerInvariant();
            return elemType switch
            {
                "Wall" => lower.Contains("wall") || lower.Contains("mur") || lower.Contains("wand") || lower.Contains("partition"),
                "Column" => lower.Contains("column") || lower.Contains("col") || lower.Contains("pillar") || lower.Contains("stutze"),
                "Beam" => lower.Contains("beam") || lower.Contains("trager") || lower.Contains("lintel") || lower.Contains("poutre"),
                "Slab" => lower.Contains("slab") || lower.Contains("floor") || lower.Contains("dalle") || lower.Contains("deck"),
                "Foundation" => lower.Contains("found") || lower.Contains("footing") || lower.Contains("base") || lower.Contains("pile"),
                "Shear Wall" => lower.Contains("shear") || lower.Contains("core") || lower.Contains("lift") || lower.Contains("stair"),
                "Bracing" => lower.Contains("brac") || lower.Contains("cross") || lower.Contains("diag"),
                "Grid Line" => lower.Contains("grid") || lower.Contains("axis") || lower.Contains("raster") || lower.Contains("centre"),
                _ => false
            };
        }

        private void AutoMapLayers()
        {
            foreach (var kvp in _layerCheckboxes)
            {
                string elemType = kvp.Key;
                foreach (var cb in kvp.Value)
                {
                    string layerName = cb.Tag as string ?? "";
                    var layerInfo = _layers.FirstOrDefault(l => l.Name == layerName);
                    if (layerInfo != null)
                        cb.IsChecked = IsLayerAutoMapped(layerInfo, elemType);
                }
            }
        }

        private void ClearAllMappings()
        {
            foreach (var kvp in _layerCheckboxes)
                foreach (var cb in kvp.Value)
                    cb.IsChecked = false;
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE 3: Element Properties
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPage3_Properties()
        {
            var mainStack = new StackPanel();
            _propTextBoxes.Clear();
            _propCombos.Clear();

            mainStack.Children.Add(new TextBlock
            {
                Text = "Configure default dimensions and materials for each element type.\n" +
                       "These can be overridden per-element during detection preview.",
                FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });

            // Wall properties
            mainStack.Children.Add(BuildPropCard("Wall", new[] {
                ("Height (mm)", "WallHeight", "3000"),
                ("Thickness (mm)", "WallThickness", "200"),
            }, "WallMaterial", "Concrete", new[] {
                ("Structural", "WallStructural", true),
            }));

            // Column properties
            mainStack.Children.Add(BuildPropCard("Column", new[] {
                ("Height (mm)", "ColumnHeight", "3000"),
                ("Width (mm)", "ColumnWidth", "300"),
                ("Depth (mm)", "ColumnDepth", "300"),
            }, "ColumnMaterial", "Concrete", null,
            new[] { ("Shape", "ColumnShape", new[] { "Rectangular", "Circular" }, 0) }));

            // Beam properties
            mainStack.Children.Add(BuildPropCard("Beam", new[] {
                ("Depth (mm)", "BeamDepth", "450"),
                ("Width (mm)", "BeamWidth", "250"),
            }, "BeamMaterial", "Concrete"));

            // Slab properties
            mainStack.Children.Add(BuildPropCard("Slab", new[] {
                ("Thickness (mm)", "SlabThickness", "200"),
            }, "SlabMaterial", "Concrete"));

            // Foundation properties
            var foundTypeCombo = MakeCombo(new[] { "Pad", "Strip", "Raft" }, 0, 120);
            _propCombos["FoundationType"] = foundTypeCombo;
            mainStack.Children.Add(BuildPropCard("Foundation", new[] {
                ("Depth (mm)", "FoundationDepth", "600"),
                ("Width (mm)", "FoundationWidth", "1200"),
            }, "FoundationMaterial", "Concrete", null,
            new[] { ("Type", "FoundationType", new[] { "Pad", "Strip", "Raft" }, 0) }));

            // Shear Wall properties
            mainStack.Children.Add(BuildPropCard("Shear Wall", new[] {
                ("Height (mm)", "ShearWallHeight", "3000"),
                ("Thickness (mm)", "ShearWallThickness", "250"),
            }, "ShearWallMaterial", "Reinforced Concrete"));

            return WrapScroll(mainStack);
        }

        private Border BuildPropCard(string title,
            (string label, string key, string defaultVal)[] textFields,
            string materialKey, string defaultMaterial,
            (string label, string key, bool defaultVal)[] checkFields = null,
            (string label, string key, string[] options, int defIdx)[] comboFields = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;

            // Text fields
            foreach (var (label, key, defaultVal) in textFields)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                var lbl = MakeLabel(label);
                Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
                grid.Children.Add(lbl);

                var tb = MakeTextBox(defaultVal);
                _propTextBoxes[key] = tb;
                Grid.SetRow(tb, row); Grid.SetColumn(tb, 1);
                grid.Children.Add(tb);
                row++;
            }

            // Material dropdown
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var matLabel = MakeLabel("Material:");
            Grid.SetRow(matLabel, row); Grid.SetColumn(matLabel, 0);
            grid.Children.Add(matLabel);

            int matIdx = Array.IndexOf(Materials, defaultMaterial);
            var matCombo = MakeCombo(Materials, matIdx >= 0 ? matIdx : 0, 180);
            _propCombos[materialKey] = matCombo;
            Grid.SetRow(matCombo, row); Grid.SetColumn(matCombo, 1); Grid.SetColumnSpan(matCombo, 2);
            grid.Children.Add(matCombo);
            row++;

            // Combo fields
            if (comboFields != null)
            {
                foreach (var (label, key, options, defIdx) in comboFields)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                    var lbl = MakeLabel(label + ":");
                    Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
                    grid.Children.Add(lbl);

                    var combo = MakeCombo(options, defIdx, 120);
                    _propCombos[key] = combo;
                    Grid.SetRow(combo, row); Grid.SetColumn(combo, 1);
                    grid.Children.Add(combo);
                    row++;
                }
            }

            // Check fields
            if (checkFields != null)
            {
                foreach (var (label, key, defaultVal) in checkFields)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                    var cb = new CheckBox
                    {
                        Content = label, IsChecked = defaultVal, FontSize = 12,
                        Margin = new Thickness(0, 2, 0, 2),
                    };
                    Grid.SetRow(cb, row); Grid.SetColumn(cb, 0); Grid.SetColumnSpan(cb, 3);
                    grid.Children.Add(cb);
                    row++;
                }
            }

            return MakeCard(title, grid);
        }

        private void CollectProperties()
        {
            Config.WallHeightMm = GetDouble("WallHeight", 3000);
            Config.WallThicknessMm = GetDouble("WallThickness", 200);
            Config.WallMaterial = GetComboText("WallMaterial", "Concrete");

            Config.ColumnHeightMm = GetDouble("ColumnHeight", 3000);
            Config.ColumnWidthMm = GetDouble("ColumnWidth", 300);
            Config.ColumnDepthMm = GetDouble("ColumnDepth", 300);
            Config.ColumnMaterial = GetComboText("ColumnMaterial", "Concrete");
            Config.ColumnShape = GetComboText("ColumnShape", "Rectangular");

            Config.BeamDepthMm = GetDouble("BeamDepth", 450);
            Config.BeamWidthMm = GetDouble("BeamWidth", 250);
            Config.BeamMaterial = GetComboText("BeamMaterial", "Concrete");

            Config.SlabThicknessMm = GetDouble("SlabThickness", 200);
            Config.SlabMaterial = GetComboText("SlabMaterial", "Concrete");

            Config.FoundationDepthMm = GetDouble("FoundationDepth", 600);
            Config.FoundationWidthMm = GetDouble("FoundationWidth", 1200);
            Config.FoundationMaterial = GetComboText("FoundationMaterial", "Concrete");
            Config.FoundationType = GetComboText("FoundationType", "Pad");

            Config.ShearWallHeightMm = GetDouble("ShearWallHeight", 3000);
            Config.ShearWallThicknessMm = GetDouble("ShearWallThickness", 250);
        }

        private double GetDouble(string key, double fallback)
        {
            if (_propTextBoxes.TryGetValue(key, out var tb) && double.TryParse(tb.Text, out double val))
                return val;
            return fallback;
        }

        private string GetComboText(string key, string fallback)
        {
            if (_propCombos.TryGetValue(key, out var combo) && combo.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? fallback;
            return fallback;
        }


        // ══════════════════════════════════════════════════════════════════
        // PAGE 4: Structural Options
        // ══════════════════════════════════════════════════════════════════

        private CheckBox _cbAutoJoinWalls, _cbAutoJoinColumns, _cbAutoExtendBeams;
        private CheckBox _cbDetectShearWalls, _cbDetectBracing, _cbCreateGrids;
        private CheckBox _cbDetectFoundations, _cbMergeCollinear, _cbSnapToGrid;
        private CheckBox _cbCreateNewTypes;
        private TextBox _tbSnapTolerance, _tbEndpointTolerance, _tbParallelTolerance;
        private TextBox _tbMinColumnSize, _tbMaxColumnSize, _tbMinBeamLength, _tbMinWallLength;
        private TextBox _tbMinSlabArea, _tbTypePrefix;

        private FrameworkElement BuildPage4_StructuralOptions()
        {
            var mainStack = new StackPanel();

            // ── Joining & Cleanup ──
            var joinStack = new StackPanel();
            _cbAutoJoinWalls = new CheckBox { Content = "Auto-join intersecting walls (T/L/X junctions)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbAutoJoinColumns = new CheckBox { Content = "Auto-join columns to walls at intersections", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbAutoExtendBeams = new CheckBox { Content = "Auto-extend beams to nearest column/wall", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbMergeCollinear = new CheckBox { Content = "Merge collinear wall segments into single walls", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbSnapToGrid = new CheckBox { Content = "Snap element endpoints to grid intersections", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            joinStack.Children.Add(_cbAutoJoinWalls);
            joinStack.Children.Add(_cbAutoJoinColumns);
            joinStack.Children.Add(_cbAutoExtendBeams);
            joinStack.Children.Add(_cbMergeCollinear);
            joinStack.Children.Add(_cbSnapToGrid);
            mainStack.Children.Add(MakeCard("Joining & Cleanup", joinStack));

            // ── Detection Options ──
            var detectStack = new StackPanel();
            _cbDetectShearWalls = new CheckBox { Content = "Detect shear/core walls (thick walls > 200mm near lifts/stairs)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbDetectBracing = new CheckBox { Content = "Detect diagonal bracing between columns", IsChecked = false, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbCreateGrids = new CheckBox { Content = "Create Revit grid lines from long straight lines", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            _cbDetectFoundations = new CheckBox { Content = "Detect foundation pads below columns", IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
            detectStack.Children.Add(_cbDetectShearWalls);
            detectStack.Children.Add(_cbDetectBracing);
            detectStack.Children.Add(_cbCreateGrids);
            detectStack.Children.Add(_cbDetectFoundations);
            mainStack.Children.Add(MakeCard("Detection Intelligence", detectStack));

            // ── Tolerances ──
            var tolGrid = new Grid();
            tolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            tolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            tolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            tolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            tolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            int r = 0;
            _tbSnapTolerance = AddTolRow(tolGrid, r, 0, "Grid snap tolerance:", "10"); r++;
            _tbEndpointTolerance = AddTolRow(tolGrid, r, 0, "Endpoint match tolerance:", "5"); r++;
            _tbParallelTolerance = AddTolRow(tolGrid, r, 0, "Parallel line max gap:", "50");

            r = 0;
            _tbMinColumnSize = AddTolRow(tolGrid, r, 3, "Min column size:", "150"); r++;
            _tbMaxColumnSize = AddTolRow(tolGrid, r, 3, "Max column size:", "1200"); r++;
            _tbMinBeamLength = AddTolRow(tolGrid, r, 3, "Min beam length:", "500");

            mainStack.Children.Add(MakeCard("Precision Tolerances (mm)", tolGrid));

            // ── Detection Thresholds ──
            var threshGrid = new Grid();
            threshGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            threshGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            threshGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            threshGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            threshGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            _tbMinWallLength = AddTolRow(threshGrid, 0, 0, "Min wall length:", "300");
            _tbMinSlabArea = AddTolRow(threshGrid, 0, 3, "Min slab area (m²):", "1.0");

            mainStack.Children.Add(MakeCard("Detection Thresholds", threshGrid));

            // ── Type Creation ──
            var typeStack = new StackPanel();
            _cbCreateNewTypes = new CheckBox
            {
                Content = "Create new Revit types for detected sizes (e.g., 'STING RC Column 300x300')",
                IsChecked = true, Margin = new Thickness(0, 2, 0, 6), FontSize = 12,
            };
            typeStack.Children.Add(_cbCreateNewTypes);
            var prefixRow = new StackPanel { Orientation = Orientation.Horizontal };
            prefixRow.Children.Add(MakeLabel("Type name prefix:"));
            _tbTypePrefix = MakeTextBox("STING", 100);
            prefixRow.Children.Add(_tbTypePrefix);
            typeStack.Children.Add(prefixRow);
            mainStack.Children.Add(MakeCard("Type Creation", typeStack));

            return WrapScroll(mainStack);
        }

        private TextBox AddTolRow(Grid grid, int row, int colOffset, string label, string defaultVal)
        {
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var lbl = MakeLabel(label);
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, colOffset);
            grid.Children.Add(lbl);

            var tb = MakeTextBox(defaultVal);
            Grid.SetRow(tb, row); Grid.SetColumn(tb, colOffset + 1);
            grid.Children.Add(tb);
            return tb;
        }

        private void CollectOptions()
        {
            Config.AutoJoinWalls = _cbAutoJoinWalls?.IsChecked ?? true;
            Config.AutoJoinColumns = _cbAutoJoinColumns?.IsChecked ?? true;
            Config.AutoExtendBeams = _cbAutoExtendBeams?.IsChecked ?? true;
            Config.DetectShearWalls = _cbDetectShearWalls?.IsChecked ?? true;
            Config.DetectBracing = _cbDetectBracing?.IsChecked ?? false;
            Config.CreateGridLines = _cbCreateGrids?.IsChecked ?? true;
            Config.DetectFoundations = _cbDetectFoundations?.IsChecked ?? true;
            Config.MergeCollinearWalls = _cbMergeCollinear?.IsChecked ?? true;
            Config.SnapToGrid = _cbSnapToGrid?.IsChecked ?? true;
            Config.CreateNewTypes = _cbCreateNewTypes?.IsChecked ?? true;

            Config.SnapToleranceMm = GetTbDouble(_tbSnapTolerance, 10);
            Config.EndpointToleranceMm = GetTbDouble(_tbEndpointTolerance, 5);
            Config.ParallelLineToleranceMm = GetTbDouble(_tbParallelTolerance, 50);
            Config.MinColumnSizeMm = GetTbDouble(_tbMinColumnSize, 150);
            Config.MaxColumnSizeMm = GetTbDouble(_tbMaxColumnSize, 1200);
            Config.MinBeamLengthMm = GetTbDouble(_tbMinBeamLength, 500);
            Config.MinWallLengthMm = GetTbDouble(_tbMinWallLength, 300);
            Config.MinSlabAreaM2 = GetTbDouble(_tbMinSlabArea, 1.0);
            Config.TypeNamingPrefix = _tbTypePrefix?.Text ?? "STING";
        }

        private static double GetTbDouble(TextBox tb, double fallback)
        {
            if (tb != null && double.TryParse(tb.Text, out double val)) return val;
            return fallback;
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE 5: Tagging & Numbering
        // ══════════════════════════════════════════════════════════════════

        private CheckBox _cbAutoTag, _cbAutoNumber;
        private TextBox _tbTagPrefix;
        private ComboBox _cbNumberScheme;

        private FrameworkElement BuildPage5_Tagging()
        {
            var mainStack = new StackPanel();

            mainStack.Children.Add(new TextBlock
            {
                Text = "Configure STING ISO 19650 tagging and numbering for created elements.\n" +
                       "All created elements will be tagged with the full 8-segment tag format:\n" +
                       "DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ",
                FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });

            // Auto-tag options
            var tagStack = new StackPanel();
            _cbAutoTag = new CheckBox
            {
                Content = "Auto-tag all created elements (STING ISO 19650 pipeline)",
                IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12,
            };
            _cbAutoNumber = new CheckBox
            {
                Content = "Auto-assign sequential numbers (SEQ) per element type",
                IsChecked = true, Margin = new Thickness(0, 2, 0, 2), FontSize = 12,
            };
            tagStack.Children.Add(_cbAutoTag);
            tagStack.Children.Add(_cbAutoNumber);

            var prefixRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            prefixRow.Children.Add(MakeLabel("Tag prefix override:"));
            _tbTagPrefix = MakeTextBox("", 120);
            _tbTagPrefix.ToolTip = "Leave empty to use project default";
            prefixRow.Children.Add(_tbTagPrefix);
            tagStack.Children.Add(prefixRow);

            var schemeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            schemeRow.Children.Add(MakeLabel("Numbering scheme:"));
            _cbNumberScheme = MakeCombo(new[] { "By Type (S-COL-0001)", "By Level (L01-COL-0001)", "Sequential (0001, 0002...)" }, 0, 220);
            schemeRow.Children.Add(_cbNumberScheme);
            tagStack.Children.Add(schemeRow);

            mainStack.Children.Add(MakeCard("STING Tagging Integration", tagStack));

            // Tag preview
            var previewStack = new StackPanel();
            previewStack.Children.Add(new TextBlock
            {
                Text = "Example tags that will be generated:",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var examples = new[] {
                "Column:     S-BLD1-Z01-L01-STR-SUP-COL-0001",
                "Beam:       S-BLD1-Z01-L01-STR-SUP-BM-0001",
                "Wall:       S-BLD1-Z01-L01-STR-SUP-WL-0001",
                "Slab:       S-BLD1-Z01-L01-STR-SUP-SLB-0001",
                "Foundation: S-BLD1-Z01-B01-STR-SUP-FND-0001",
                "Shear Wall: S-BLD1-Z01-L01-STR-LAT-SHW-0001",
            };

            foreach (var ex in examples)
            {
                previewStack.Children.Add(new TextBlock
                {
                    Text = ex,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }

            mainStack.Children.Add(MakeCard("Tag Preview", previewStack));

            return WrapScroll(mainStack);
        }

        private void CollectTagging()
        {
            Config.AutoTag = _cbAutoTag?.IsChecked ?? true;
            Config.AutoNumber = _cbAutoNumber?.IsChecked ?? true;
            Config.TagPrefix = _tbTagPrefix?.Text ?? "";
            if (_cbNumberScheme?.SelectedIndex == 0) Config.NumberingScheme = "ByType";
            else if (_cbNumberScheme?.SelectedIndex == 1) Config.NumberingScheme = "ByLevel";
            else Config.NumberingScheme = "Sequential";
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE 6: Detection Preview
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPage6_Preview()
        {
            var mainStack = new StackPanel();

            mainStack.Children.Add(new TextBlock
            {
                Text = "Preview of elements that will be created based on your settings.\n" +
                       "Review the counts and adjust layer mappings or properties if needed.",
                FontSize = 11, Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });

            // Element count summary
            var counts = new Dictionary<string, int>();
            foreach (var kvp in Config.LayerMapping)
            {
                int layerCount = kvp.Value.Count;
                int entityCount = 0;
                foreach (var layerName in kvp.Value)
                {
                    var layer = _layers.FirstOrDefault(l => l.Name == layerName);
                    if (layer != null) entityCount += layer.EntityCount;
                }
                counts[kvp.Key] = entityCount;
            }

            var countGrid = new Grid();
            countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;
            countGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            AddCol(countGrid, 0, "Element Type", true);
            AddCol(countGrid, 1, "Layers", true);
            AddCol(countGrid, 2, "Entities", true);
            AddCol(countGrid, 3, "Properties", true);

            foreach (var kvp in Config.LayerMapping)
            {
                row++;
                countGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

                var elemTb = new TextBlock
                {
                    Text = kvp.Key, FontSize = 12, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(elemTb, row); Grid.SetColumn(elemTb, 0);
                countGrid.Children.Add(elemTb);

                var layerTb = new TextBlock
                {
                    Text = kvp.Value.Count.ToString(), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(layerTb, row); Grid.SetColumn(layerTb, 1);
                countGrid.Children.Add(layerTb);

                int entities = counts.TryGetValue(kvp.Key, out int c) ? c : 0;
                var entTb = new TextBlock
                {
                    Text = entities.ToString(), FontSize = 12,
                    Foreground = new SolidColorBrush(entities > 0 ? SuccessColor : Color.FromRgb(0xC6, 0x28, 0x28)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(entTb, row); Grid.SetColumn(entTb, 2);
                countGrid.Children.Add(entTb);

                string props = GetPropsForType(kvp.Key);
                var propTb = new TextBlock
                {
                    Text = props, FontSize = 11,
                    Foreground = new SolidColorBrush(TextSecondary),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(propTb, row); Grid.SetColumn(propTb, 3);
                countGrid.Children.Add(propTb);
            }

            mainStack.Children.Add(MakeCard("Element Summary", countGrid));

            // Options summary
            var optStack = new StackPanel();
            AddOptLine(optStack, "Auto-join walls", Config.AutoJoinWalls);
            AddOptLine(optStack, "Auto-join columns", Config.AutoJoinColumns);
            AddOptLine(optStack, "Auto-extend beams", Config.AutoExtendBeams);
            AddOptLine(optStack, "Merge collinear walls", Config.MergeCollinearWalls);
            AddOptLine(optStack, "Snap to grid", Config.SnapToGrid);
            AddOptLine(optStack, "Detect shear walls", Config.DetectShearWalls);
            AddOptLine(optStack, "Create grid lines", Config.CreateGridLines);
            AddOptLine(optStack, "Create new types", Config.CreateNewTypes);
            AddOptLine(optStack, "Auto-tag (STING)", Config.AutoTag);
            mainStack.Children.Add(MakeCard("Active Options", optStack));

            // Total estimate
            int total = counts.Values.Sum();
            var totalCard = new Border
            {
                Background = total > 0 ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                BorderBrush = total > 0 ? new SolidColorBrush(SuccessColor)
                    : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 10),
            };
            totalCard.Child = new TextBlock
            {
                Text = $"TOTAL: ~{total} DWG entities across {Config.LayerMapping.Count} element types will be processed.\n" +
                       $"Tolerances: Endpoint={Config.EndpointToleranceMm}mm, Snap={Config.SnapToleranceMm}mm, Parallel={Config.ParallelLineToleranceMm}mm",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
            };
            mainStack.Children.Add(totalCard);

            return WrapScroll(mainStack);
        }

        private string GetPropsForType(string elemType)
        {
            return elemType switch
            {
                "Wall" => $"H={Config.WallHeightMm}mm, T={Config.WallThicknessMm}mm, {Config.WallMaterial}",
                "Column" => $"H={Config.ColumnHeightMm}mm, {Config.ColumnWidthMm}x{Config.ColumnDepthMm}mm, {Config.ColumnMaterial}",
                "Beam" => $"D={Config.BeamDepthMm}mm, W={Config.BeamWidthMm}mm, {Config.BeamMaterial}",
                "Slab" => $"T={Config.SlabThicknessMm}mm, {Config.SlabMaterial}",
                "Foundation" => $"{Config.FoundationType}, D={Config.FoundationDepthMm}mm, W={Config.FoundationWidthMm}mm",
                "Shear Wall" => $"H={Config.ShearWallHeightMm}mm, T={Config.ShearWallThicknessMm}mm",
                "Bracing" => $"D={Config.BracingDepthMm}mm",
                "Grid Line" => "Auto-detect from long lines",
                _ => ""
            };
        }

        private static void AddOptLine(StackPanel parent, string label, bool value)
        {
            parent.Children.Add(new TextBlock
            {
                Text = $"  {(value ? "✓" : "✗")}  {label}",
                FontSize = 11, Margin = new Thickness(0, 1, 0, 1),
                Foreground = value ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                    : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            });
        }

        // ══════════════════════════════════════════════════════════════════
        // PAGE 7: Summary & Execute
        // ══════════════════════════════════════════════════════════════════

        private FrameworkElement BuildPage7_Summary()
        {
            var mainStack = new StackPanel();

            mainStack.Children.Add(new TextBlock
            {
                Text = "READY TO BUILD",
                FontSize = 20, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(HeaderBg),
                Margin = new Thickness(0, 0, 0, 8),
            });

            var summaryText = new System.Text.StringBuilder();
            summaryText.AppendLine("═══════════════════════════════════════════════════════════════");
            summaryText.AppendLine("  STING STRUCTURAL DWG-TO-BIM CONVERSION SUMMARY");
            summaryText.AppendLine("═══════════════════════════════════════════════════════════════");
            summaryText.AppendLine();

            summaryText.AppendLine("LAYER MAPPING:");
            foreach (var kvp in Config.LayerMapping)
            {
                summaryText.AppendLine($"  {kvp.Key,-15} ← {string.Join(", ", kvp.Value)}");
            }
            summaryText.AppendLine();

            summaryText.AppendLine("ELEMENT PROPERTIES:");
            summaryText.AppendLine($"  Walls:       {Config.WallHeightMm}mm H × {Config.WallThicknessMm}mm T  ({Config.WallMaterial})");
            summaryText.AppendLine($"  Columns:     {Config.ColumnHeightMm}mm H × {Config.ColumnWidthMm}×{Config.ColumnDepthMm}mm  ({Config.ColumnMaterial}, {Config.ColumnShape})");
            summaryText.AppendLine($"  Beams:       {Config.BeamDepthMm}mm D × {Config.BeamWidthMm}mm W  ({Config.BeamMaterial})");
            summaryText.AppendLine($"  Slabs:       {Config.SlabThicknessMm}mm T  ({Config.SlabMaterial})");
            summaryText.AppendLine($"  Foundations: {Config.FoundationType} — {Config.FoundationDepthMm}mm D × {Config.FoundationWidthMm}mm W  ({Config.FoundationMaterial})");
            summaryText.AppendLine();

            summaryText.AppendLine("OPTIONS:");
            summaryText.AppendLine($"  Auto-join walls:     {(Config.AutoJoinWalls ? "YES" : "NO")}");
            summaryText.AppendLine($"  Auto-join columns:   {(Config.AutoJoinColumns ? "YES" : "NO")}");
            summaryText.AppendLine($"  Auto-extend beams:   {(Config.AutoExtendBeams ? "YES" : "NO")}");
            summaryText.AppendLine($"  Merge collinear:     {(Config.MergeCollinearWalls ? "YES" : "NO")}");
            summaryText.AppendLine($"  Snap to grid:        {(Config.SnapToGrid ? "YES" : "NO")}");
            summaryText.AppendLine($"  Detect shear walls:  {(Config.DetectShearWalls ? "YES" : "NO")}");
            summaryText.AppendLine($"  Create new types:    {(Config.CreateNewTypes ? "YES" : "NO")}");
            summaryText.AppendLine($"  Auto-tag (STING):    {(Config.AutoTag ? "YES" : "NO")}");
            summaryText.AppendLine();

            summaryText.AppendLine("TOLERANCES:");
            summaryText.AppendLine($"  Endpoint:   {Config.EndpointToleranceMm}mm");
            summaryText.AppendLine($"  Snap:       {Config.SnapToleranceMm}mm");
            summaryText.AppendLine($"  Parallel:   {Config.ParallelLineToleranceMm}mm");
            summaryText.AppendLine();
            summaryText.AppendLine("═══════════════════════════════════════════════════════════════");
            summaryText.AppendLine("  Click '★ Build Model' to start the conversion.");
            summaryText.AppendLine("  A progress dialog will show during creation.");
            summaryText.AppendLine("═══════════════════════════════════════════════════════════════");

            var summaryBox = new TextBox
            {
                Text = summaryText.ToString(),
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
                Padding = new Thickness(14),
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            mainStack.Children.Add(summaryBox);
            return WrapScroll(mainStack);
        }
    }
}
