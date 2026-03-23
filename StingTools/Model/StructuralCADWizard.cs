// ============================================================================
// StructuralCADWizard.cs — WPF Multi-Page Wizard for Structural CAD Automation
//
// Provides a comprehensive setup dialog before running the CAD-to-structural
// pipeline. Pages:
//   1. Prerequisites Check — shows loaded families, levels, DWG status
//   2. DWG Selection — pick import instance, preview structural layers
//   3. Configuration — element types to create, default sizes, level mapping
//   4. Type Mapping — review detected elements → family type assignments
//   5. Summary — review all settings before execution
//
// Built in C# (no XAML) following existing StingWizardDialog pattern.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using StingTools.Core;
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Brushes = System.Windows.Media.Brushes;
using FontWeights = System.Windows.FontWeights;
using FontWeight = System.Windows.FontWeight;
using Thickness = System.Windows.Thickness;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace StingTools.Model
{
    /// <summary>
    /// Multi-page WPF wizard for structural CAD-to-BIM automation.
    /// Guides user through prerequisites, DWG selection, configuration,
    /// and type mapping before executing the conversion pipeline.
    /// </summary>
    public class StructuralCADWizard : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly Document _doc;
        private readonly StructuralCADPipeline _pipeline;
        private int _currentPage = 0;
        private readonly List<FrameworkElement> _pages = new();

        // User selections
        private ImportInstance _selectedImport;
        private PrerequisiteCheckResult _prereqResult;
        private StructuralExtractionResult _extraction;

        // Configuration
        public bool CreateColumns { get; private set; } = true;
        public bool CreateBeams { get; private set; } = true;
        public bool CreateSlabs { get; private set; } = true;
        public bool CreateGrids { get; private set; } = true;
        public double DefaultBeamDepthMm { get; private set; } = 450;
        public double DefaultSlabThickMm { get; private set; } = 200;
        public double DefaultStoreyHeightMm { get; private set; } = 3600;
        public string SelectedLevel { get; private set; }
        public bool Confirmed { get; private set; }
        public double EndpointToleranceMm { get; private set; } = 5;
        public double MinColumnSizeMm { get; private set; } = 150;
        public double MinBeamLengthMm { get; private set; } = 500;
        public ImportInstance SelectedImport => _selectedImport;

        // ── UI Elements ──────────────────────────────────────────────────
        private ContentControl _pageHost;
        private Button _btnBack, _btnNext, _btnFinish;
        private TextBlock _statusText, _pageTitle;
        private StackPanel _stepIndicator;

        private static readonly string[] PageTitles = {
            "Prerequisites Check",
            "DWG Selection & Preview",
            "Configuration",
            "Type Mapping Review",
            "Summary & Execute"
        };

        // ── Constructor ──────────────────────────────────────────────────

        public StructuralCADWizard(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _pipeline = new StructuralCADPipeline(doc);
            _prereqResult = _pipeline.CheckPrerequisites();

            Title = "STING Structural CAD Automation Wizard";
            Width = 820;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF5, 0xF5));

            BuildLayout();
            BuildPages();
            ShowPage(0);
        }

        // ── Layout ───────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });

            // ── Header with step indicator ──
            var header = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28)),
                Padding = new Thickness(16, 8, 16, 8),
            };
            var headerStack = new StackPanel();
            _pageTitle = new TextBlock
            {
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
            };
            _stepIndicator = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            headerStack.Children.Add(_pageTitle);
            headerStack.Children.Add(_stepIndicator);
            header.Child = headerStack;
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Page host ──
            _pageHost = new ContentControl { Margin = new Thickness(16) };
            WpfGrid.SetRow(_pageHost, 1);
            root.Children.Add(_pageHost);

            // ── Footer with navigation ──
            var footer = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
            };
            var footerGrid = new WpfGrid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
            WpfGrid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnBack = MakeButton("← Back", OnBack);
            _btnNext = MakeButton("Next →", OnNext);
            _btnFinish = MakeButton("★ Execute", OnFinish);
            _btnFinish.Background = new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28));
            _btnFinish.Foreground = Brushes.White;

            var btnCancel = MakeButton("Cancel", (s, e) => { Confirmed = false; Close(); });
            btnPanel.Children.Add(_btnBack);
            btnPanel.Children.Add(_btnNext);
            btnPanel.Children.Add(_btnFinish);
            btnPanel.Children.Add(btnCancel);
            WpfGrid.SetColumn(btnPanel, 1);
            footerGrid.Children.Add(btnPanel);

            footer.Child = footerGrid;
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private Button MakeButton(string text, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 90, Height = 32,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 12,
            };
            btn.Click += handler;
            return btn;
        }

        // ── Pages ────────────────────────────────────────────────────────

        private void BuildPages()
        {
            _pages.Add(BuildPrerequisitesPage());
            _pages.Add(BuildDWGSelectionPage());
            _pages.Add(BuildConfigPage());
            _pages.Add(BuildTypeMappingPage());
            _pages.Add(BuildSummaryPage());
        }

        private ScrollViewer WrapInScroll(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content,
            };
        }

        // ── Page 1: Prerequisites ────────────────────────────────────────

        private FrameworkElement BuildPrerequisitesPage()
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "Checking project readiness for structural automation...",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
            });

            var statusBox = new TextBox
            {
                Text = _prereqResult.GetStatusText(),
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4)),
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 300,
            };

            stack.Children.Add(statusBox);

            // Family catalog summary
            _pipeline.TypeFactory.BuildCatalog();
            var catalogBox = new TextBox
            {
                Text = _pipeline.TypeFactory.GetCatalogSummary(),
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Background = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x9C, 0xDC, 0xFE)),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 150,
            };
            stack.Children.Add(new TextBlock { Text = "LOADED STRUCTURAL FAMILIES:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
            stack.Children.Add(catalogBox);

            return WrapInScroll(stack);
        }

        // ── Page 2: DWG Selection ────────────────────────────────────────

        // Stores the user's layer checkbox selections
        private readonly Dictionary<string, CheckBox> _layerCheckboxes = new();

        private FrameworkElement BuildDWGSelectionPage()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Select the DWG import, then choose which layers to convert:",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
            });

            var imports = CADToModelEngine.FindImportInstances(_doc);
            if (imports.Count > 0)
            {
                _selectedImport = imports[0];

                var listBox = new ListBox { MinHeight = 80, MaxHeight = 100, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var imp in imports)
                {
                    string name = "DWG Import";
                    try { name = imp.Name ?? $"Import #{imp.Id.Value}"; }
                    catch (Exception ex) { StingLog.Warn($"Import name: {ex.Message}"); }
                    listBox.Items.Add(new ListBoxItem { Content = name, Tag = imp });
                }
                listBox.SelectedIndex = 0;
                listBox.SelectionChanged += (s, e) =>
                {
                    if (listBox.SelectedItem is ListBoxItem item && item.Tag is ImportInstance imp)
                        _selectedImport = imp;
                };
                stack.Children.Add(listBox);

                // Scale detection display
                var scaleText = new TextBlock
                {
                    Text = "DWG Scale: (click Analyze to detect)",
                    FontSize = 11, Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 4, 0, 8),
                };
                stack.Children.Add(scaleText);

                // Layer selection panel — MISS-01 FIX
                var layerPanel = new StackPanel();
                var layerBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 4, 0, 8),
                    MaxHeight = 250,
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = layerPanel,
                    },
                };

                var layerHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                layerHeader.Children.Add(new TextBlock
                {
                    Text = "LAYER SELECTION:", FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 12, 0),
                });
                var selectAllBtn = MakeButton("Select All", (s, e) =>
                {
                    foreach (var cb in _layerCheckboxes.Values) cb.IsChecked = true;
                });
                selectAllBtn.Height = 22; selectAllBtn.MinWidth = 70; selectAllBtn.FontSize = 10;
                var selectNoneBtn = MakeButton("Select None", (s, e) =>
                {
                    foreach (var cb in _layerCheckboxes.Values) cb.IsChecked = false;
                });
                selectNoneBtn.Height = 22; selectNoneBtn.MinWidth = 70; selectNoneBtn.FontSize = 10;
                var selectStructBtn = MakeButton("Structural Only", (s, e) =>
                {
                    foreach (var kvp in _layerCheckboxes)
                        kvp.Value.IsChecked = StructuralLayerClassifier.IsStructuralLayer(kvp.Key);
                });
                selectStructBtn.Height = 22; selectStructBtn.MinWidth = 90; selectStructBtn.FontSize = 10;
                selectStructBtn.Background = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0x91, 0x2D));
                selectStructBtn.Foreground = Brushes.White;
                layerHeader.Children.Add(selectAllBtn);
                layerHeader.Children.Add(selectNoneBtn);
                layerHeader.Children.Add(selectStructBtn);
                stack.Children.Add(layerHeader);

                // Analyze button — populates layer checkboxes
                var analyzeBtn = MakeButton("★ Analyze DWG Layers", (s, e) =>
                {
                    if (_selectedImport == null) return;

                    var manifest = _pipeline.ExtractLayerManifest(_selectedImport);
                    layerPanel.Children.Clear();
                    _layerCheckboxes.Clear();

                    // Sort: structural layers first, then by entity count
                    var sorted = manifest.OrderByDescending(kvp => kvp.Value.Confidence)
                        .ThenByDescending(kvp => kvp.Value.Count);

                    foreach (var kvp in sorted)
                    {
                        bool isStruct = kvp.Value.Confidence > 0;
                        var cb = new CheckBox
                        {
                            Content = $"{kvp.Key}  —  {kvp.Value.Count} entities  →  {kvp.Value.Classification}" +
                                (isStruct ? $" ({kvp.Value.Confidence * 100:F0}%)" : ""),
                            IsChecked = isStruct, // Auto-select structural layers
                            Margin = new Thickness(0, 1, 0, 1),
                            FontSize = 11,
                            FontWeight = isStruct ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = isStruct
                                ? new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28))
                                : Brushes.Gray,
                        };
                        _layerCheckboxes[kvp.Key] = cb;
                        layerPanel.Children.Add(cb);
                    }

                    // Detect scale
                    var extraction = _pipeline.ExtractStructuralGeometry(_selectedImport);
                    _extraction = extraction;
                    double scale = extraction.DetectedScaleFactor;
                    string scaleDesc = Math.Abs(scale - 1.0) < 0.001 ? "1:1 (no conversion)"
                        : Math.Abs(scale - 0.00328) < 0.001 ? "ft → internal (imperial DWG)"
                        : $"{scale:F4} (custom)";
                    scaleText.Text = $"DWG Scale: {scaleDesc}";
                    scaleText.Foreground = Math.Abs(scale - 1.0) < 0.001 ? Brushes.Green : Brushes.Orange;
                });

                analyzeBtn.Background = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0x91, 0x2D));
                analyzeBtn.Foreground = Brushes.White;
                analyzeBtn.FontWeight = FontWeights.Bold;
                stack.Children.Add(analyzeBtn);
                stack.Children.Add(layerBorder);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No imported DWG files found.\nLink a structural DWG first.",
                    Foreground = Brushes.Red, FontWeight = FontWeights.Bold,
                });
            }

            return WrapInScroll(stack);
        }

        // ── Page 3: Configuration ────────────────────────────────────────

        private FrameworkElement BuildConfigPage()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Configure structural conversion settings:",
                FontSize = 12, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // Element type checkboxes
            var chkCols = new CheckBox { Content = "Create Columns (from circles + rectangles)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var chkBeams = new CheckBox { Content = "Create Beams (from centerlines on beam layers)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var chkSlabs = new CheckBox { Content = "Create Slabs (from slab boundary loops)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
            var chkGrids = new CheckBox { Content = "Create Grid Lines (from long axis-aligned lines)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };

            chkCols.Checked += (s, e) => CreateColumns = true;
            chkCols.Unchecked += (s, e) => CreateColumns = false;
            chkBeams.Checked += (s, e) => CreateBeams = true;
            chkBeams.Unchecked += (s, e) => CreateBeams = false;
            chkSlabs.Checked += (s, e) => CreateSlabs = true;
            chkSlabs.Unchecked += (s, e) => CreateSlabs = false;
            chkGrids.Checked += (s, e) => CreateGrids = true;
            chkGrids.Unchecked += (s, e) => CreateGrids = false;

            stack.Children.Add(new TextBlock { Text = "ELEMENTS TO CREATE:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(chkCols);
            stack.Children.Add(chkBeams);
            stack.Children.Add(chkSlabs);
            stack.Children.Add(chkGrids);

            // Dimension inputs
            stack.Children.Add(new TextBlock { Text = "DEFAULT DIMENSIONS:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });

            stack.Children.Add(MakeInputRow("Default beam depth (mm):", "450", v => DefaultBeamDepthMm = v));
            stack.Children.Add(MakeInputRow("Default slab thickness (mm):", "200", v => DefaultSlabThickMm = v));
            stack.Children.Add(MakeInputRow("Default storey height (mm):", "3600", v => DefaultStoreyHeightMm = v));

            // Detection tolerances
            stack.Children.Add(new TextBlock { Text = "DETECTION TOLERANCES:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });
            stack.Children.Add(MakeInputRow("Endpoint snap tolerance (mm):", "5", v => EndpointToleranceMm = v));
            stack.Children.Add(MakeInputRow("Min column size (mm):", "150", v => MinColumnSizeMm = v));
            stack.Children.Add(MakeInputRow("Min beam length (mm):", "500", v => MinBeamLengthMm = v));

            // Level selection
            stack.Children.Add(new TextBlock { Text = "TARGET LEVEL:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });
            var levelCombo = new ComboBox { MinWidth = 200, Margin = new Thickness(0, 4, 0, 4) };
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            foreach (var lev in levels)
                levelCombo.Items.Add(lev.Name);
            if (levelCombo.Items.Count > 0) levelCombo.SelectedIndex = 0;
            levelCombo.SelectionChanged += (s, e) =>
                SelectedLevel = levelCombo.SelectedItem?.ToString();
            stack.Children.Add(levelCombo);

            return WrapInScroll(stack);
        }

        private StackPanel MakeInputRow(string label, string defaultVal, Action<double> setter)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label, Width = 220, VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBox { Text = defaultVal, Width = 100, Margin = new Thickness(4, 0, 0, 0) };
            tb.TextChanged += (s, e) =>
            {
                if (double.TryParse(tb.Text, out double val) && val > 0)
                {
                    setter(val);
                    tb.Background = Brushes.White;
                }
                else
                {
                    tb.Background = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0xCC));
                }
            };
            row.Children.Add(tb);
            return row;
        }

        // ── Page 4: Type Mapping Review ──────────────────────────────────

        private FrameworkElement BuildTypeMappingPage()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Review detected elements and their matched family types.\n" +
                    "Types will be auto-created if no exact match is found:",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
            });

            var mappingBox = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Background = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4)),
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 380,
                Text = "(Type mappings will appear after DWG analysis on page 2)",
            };

            // Auto-populate when page shown
            mappingBox.Tag = "mapping";
            stack.Children.Add(mappingBox);
            stack.Tag = mappingBox; // Store reference for refresh

            return WrapInScroll(stack);
        }

        private void RefreshTypeMappingPage()
        {
            if (_pages.Count <= 3) return;
            var scroll = _pages[3] as ScrollViewer;
            var stack = scroll?.Content as StackPanel;
            var mappingBox = stack?.Tag as TextBox;
            if (mappingBox == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("DETECTED ELEMENT → FAMILY TYPE MAPPING");
            sb.AppendLine("══════════════════════════════════════════════════════");

            if (_extraction != null)
            {
                // Columns from circles
                sb.AppendLine($"\n  ROUND COLUMNS ({_extraction.Circles.Count} detected):");
                foreach (var c in _extraction.Circles.Take(10))
                {
                    var match = _pipeline.TypeFactory.FindOrCreateColumnType(c.DiameterMm, c.DiameterMm);
                    sb.AppendLine($"    ø{c.DiameterMm:F0}mm → {match.FamilyName}: {match.TypeName} [{match.MatchMethod}]");
                }

                // Columns from rectangles
                sb.AppendLine($"\n  RECTANGULAR COLUMNS ({_extraction.Rectangles.Count} detected):");
                // Group by unique size to avoid redundant lookups
                var rectSizes = _extraction.Rectangles
                    .GroupBy(r => $"{r.WidthMm:F0}×{r.DepthMm:F0}")
                    .Take(10);
                foreach (var g in rectSizes)
                {
                    var r = g.First();
                    var match = _pipeline.TypeFactory.FindOrCreateColumnType(r.WidthMm, r.DepthMm);
                    sb.AppendLine($"    {g.Key}mm ({g.Count()}×) → {match.FamilyName}: {match.TypeName} [{match.MatchMethod}]");
                }

                // Beams
                sb.AppendLine($"\n  BEAMS ({_extraction.BeamLines.Count} detected):");
                var beamMatch = _pipeline.TypeFactory.FindOrCreateBeamType(DefaultBeamDepthMm);
                sb.AppendLine($"    Default {DefaultBeamDepthMm:F0}mm deep → {beamMatch.FamilyName}: {beamMatch.TypeName} [{beamMatch.MatchMethod}]");

                // Walls
                sb.AppendLine($"\n  WALLS ({_extraction.Walls.Count} detected):");
                var wallSizes = _extraction.Walls.GroupBy(w => $"{w.ThicknessFt * Units.FeetToMm:F0}mm").Take(5);
                foreach (var g in wallSizes)
                    sb.AppendLine($"    {g.Key} thick ({g.Count()}×)");

                // Slabs
                sb.AppendLine($"\n  SLABS ({_extraction.SlabBoundaries.Count} detected):");
                var slabMatch = _pipeline.TypeFactory.FindOrCreateFloorType(DefaultSlabThickMm);
                sb.AppendLine($"    Default {DefaultSlabThickMm:F0}mm thick → {slabMatch.FamilyName}: {slabMatch.TypeName} [{slabMatch.MatchMethod}]");

                sb.AppendLine($"\n  GRID LINES ({_extraction.GridLines.Count} detected)");
            }
            else
            {
                sb.AppendLine("\n  ⚠ No DWG analysis performed yet.");
                sb.AppendLine("    Go back to page 2 and click 'Analyze Structural Layers'.");
            }

            mappingBox.Text = sb.ToString();
        }

        // ── Page 5: Summary ──────────────────────────────────────────────

        private FrameworkElement BuildSummaryPage()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Review settings and click 'Execute' to run the conversion:",
                FontSize = 12, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
            });

            var summaryBox = new TextBox
            {
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Background = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6A, 0x99, 0x55)),
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 350,
            };
            summaryBox.Tag = "summary";
            stack.Children.Add(summaryBox);
            stack.Tag = summaryBox;

            return WrapInScroll(stack);
        }

        private void RefreshSummaryPage()
        {
            if (_pages.Count <= 4) return;
            var scroll = _pages[4] as ScrollViewer;
            var stack = scroll?.Content as StackPanel;
            var summaryBox = stack?.Tag as TextBox;
            if (summaryBox == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("STRUCTURAL CAD AUTOMATION — EXECUTION SUMMARY");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"  DWG Import:       {(_selectedImport != null ? "Selected" : "NONE")}");
            sb.AppendLine($"  Target Level:     {SelectedLevel ?? "(default)"}");
            sb.AppendLine();
            sb.AppendLine("  ELEMENTS TO CREATE:");
            sb.AppendLine($"    Columns:        {(CreateColumns ? "YES" : "NO")}{(_extraction != null ? $" ({_extraction.Circles.Count + _extraction.Rectangles.Count} detected)" : "")}");
            sb.AppendLine($"    Beams:          {(CreateBeams ? "YES" : "NO")}{(_extraction != null ? $" ({_extraction.BeamLines.Count} detected)" : "")}");
            sb.AppendLine($"    Slabs:          {(CreateSlabs ? "YES" : "NO")}{(_extraction != null ? $" ({_extraction.SlabBoundaries.Count} detected)" : "")}");
            sb.AppendLine($"    Grid Lines:     {(CreateGrids ? "YES" : "NO")}{(_extraction != null ? $" ({_extraction.GridLines.Count} detected)" : "")}");
            sb.AppendLine();
            sb.AppendLine("  DEFAULT DIMENSIONS:");
            sb.AppendLine($"    Beam depth:     {DefaultBeamDepthMm}mm");
            sb.AppendLine($"    Slab thickness: {DefaultSlabThickMm}mm");
            sb.AppendLine($"    Storey height:  {DefaultStoreyHeightMm}mm");
            sb.AppendLine();
            sb.AppendLine($"  Type catalog:     {_pipeline.TypeFactory.CatalogSize} loaded types");
            sb.AppendLine();
            sb.AppendLine("  Click 'Execute' to start the conversion pipeline.");

            summaryBox.Text = sb.ToString();
        }

        // ── Navigation ───────────────────────────────────────────────────

        private void ShowPage(int idx)
        {
            _currentPage = Math.Max(0, Math.Min(idx, _pages.Count - 1));
            _pageHost.Content = _pages[_currentPage];
            _pageTitle.Text = $"Step {_currentPage + 1} of {_pages.Count}: {PageTitles[_currentPage]}";

            _btnBack.IsEnabled = _currentPage > 0;
            _btnNext.Visibility = _currentPage < _pages.Count - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _btnFinish.Visibility = _currentPage == _pages.Count - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            _statusText.Text = _currentPage < _pages.Count - 1
                ? "Configure settings, then click Next →"
                : "Review and click Execute to start conversion";

            UpdateStepIndicator();

            // Refresh data pages
            if (_currentPage == 3) RefreshTypeMappingPage();
            if (_currentPage == 4) RefreshSummaryPage();
        }

        private void UpdateStepIndicator()
        {
            _stepIndicator.Children.Clear();
            for (int i = 0; i < _pages.Count; i++)
            {
                var circle = new Border
                {
                    Width = 22, Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = i == _currentPage
                        ? Brushes.White
                        : i < _currentPage
                            ? new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0xCC))
                            : new SolidColorBrush(WpfColor.FromRgb(0x88, 0x44, 0x44)),
                    Margin = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 10,
                        Foreground = i == _currentPage
                            ? new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28))
                            : Brushes.White,
                    }
                };
                _stepIndicator.Children.Add(circle);
            }
        }

        private void OnBack(object sender, RoutedEventArgs e) => ShowPage(_currentPage - 1);
        private void OnNext(object sender, RoutedEventArgs e) => ShowPage(_currentPage + 1);

        /// <summary>Gets the user-selected layer names from the wizard checkboxes.</summary>
        public HashSet<string> GetSelectedLayers()
        {
            var selected = new HashSet<string>();
            foreach (var kvp in _layerCheckboxes)
            {
                if (kvp.Value.IsChecked == true)
                    selected.Add(kvp.Key);
            }
            return selected;
        }

        private void OnFinish(object sender, RoutedEventArgs e)
        {
            // Bug#13 FIX: Validate prerequisites before execution
            if (_selectedImport == null)
            {
                _statusText.Text = "ERROR: No DWG import selected. Go back to Step 2.";
                _statusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }
            if (!CreateColumns && !CreateBeams && !CreateSlabs && !CreateGrids)
            {
                _statusText.Text = "ERROR: At least one element type must be enabled.";
                _statusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }
            Confirmed = true;
            Close();
        }

        // ── Static Show ──────────────────────────────────────────────────

        /// <summary>
        /// Shows the wizard dialog and returns the configured wizard instance.
        /// Check wizard.Confirmed before using results.
        /// </summary>
        public static StructuralCADWizard Show(Document doc)
        {
            var wizard = new StructuralCADWizard(doc);
            wizard.ShowDialog();
            return wizard;
        }
    }
}
