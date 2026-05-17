// StingTools — Drawing Template Manager · Phase 137
//
// DrawingProductionConfigDialog is the pre-production configuration
// surface every batch-production command (PerLevel / Sections /
// ExteriorElevations / InteriorElevations / ScopeBoxes) launches
// before any view or sheet is created. Returns the user's
// confirmed DrawingProductionPreset along with the selected
// drawing-type ids and context labels.
//
// Layout:
//   * Left panel  — drawing-type tree + context list + preset toolbar
//   * Right panel — 4-tab TabControl:
//       Tab 1 General (scope, view creation, scale, rules)
//       Tab 2 VG Overrides (per-DrawingType per-category override grid)
//       Tab 3 Annotation (tag rules, dim rules, decorative)
//       Tab 4 Section / Elevation (context-sensitive)
//   * Footer    — Preview / Save Preset / Cancel / Produce
//
// Compact implementation: the spec calls for a Revit-VG-style cell
// grid in Tab 2; this build uses a simpler DataGrid binding that
// produces the same PresetCategoryOverride list. Cell-level fidelity
// can land in a follow-up.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core.Drawing;
using Visibility = System.Windows.Visibility;
using StingTools.Core;
namespace StingTools.UI
{
    public sealed class DrawingProductionConfigDialog : Window
    {
        public sealed class Result
        {
            public bool Confirmed { get; set; }
            public DrawingProductionPreset Preset { get; set; }
            public List<string> SelectedContexts { get; set; } = new List<string>();
            public List<string> SelectedDrawingTypeIds { get; set; } = new List<string>();
        }

        private readonly Result _result = new Result();
        private readonly List<DrawingType> _types;
        private readonly List<string> _contexts;
        private readonly string _commandType;
        private readonly Document _doc;

        // Tab state
        private TreeView _typesTree;
        private ListBox _contextsList;
        private ComboBox _presetCombo;

        // Tab 1 — general
        private RadioButton _allLevels, _selectedLevels;
        private RadioButton _dupNormal, _dupDetailing, _dupDependent;
        private CheckBox _idempotent, _createSheets, _createPackage, _onlyDefault, _hideUnused;
        // Phase 137 — GRAITEC PowerPack parity toggles
        private CheckBox _hideUnwantedSections, _hideUnwantedRebars, _hideUnwantedTags, _skipEmptyLevels;
        private TextBox _packageId;
        private ComboBox _scaleOverride, _detailLevelOverride;

        // Tab 2 — VG overrides (Phase 137 enhancement: full Revit-style editor)
        // see _vgEditor / _vgData declared near BuildVgTab below.

        // Tab 3 — annotation
        private CheckBox _runAnno, _runTags, _runDims, _runDec, _runSpots;
        private DataGrid _tagGrid, _dimGrid;
        private ObservableCollection<AutoAnnotationRule> _tagRows;
        private ObservableCollection<AutoAnnotationRule> _dimRows;
        private TextBox _northArrowFamily, _scaleBarFamily, _keyPlanFamily, _matchlineMm;
        private ComboBox _northArrowPos, _scaleBarPos, _keyPlanPos;

        // Tab 4 — section
        private RadioButton _sectPerp, _sectNS, _sectEW, _sectCustom;
        private TextBox _sectAngle, _sectSpacing, _sectDepth, _sectFar;
        private CheckBox _sectShowLevels, _sectShowGrids, _sectSegmented;
        private RadioButton _sectAutoManual, _sectAutoGrid, _sectAutoRoom;
        private RadioButton _sectOutSection, _sectOutCallout, _sectOutBoth;

        // Tab 4 — elevation
        private CheckBox _elevN, _elevS, _elevE, _elevW;
        private TextBox _elevOffset, _elevFar, _elevMarker;
        private CheckBox _elevShowLevels, _elevShowGrids, _elev1Plus4;

        public DrawingProductionConfigDialog(List<DrawingType> availableTypes, List<string> contextLabels, string commandType, Document doc)
        {
            _types = availableTypes ?? new List<DrawingType>();
            _contexts = contextLabels ?? new List<string>();
            _commandType = commandType ?? "Generic";
            _doc = doc;

            Title = $"Configure Drawing Production — {_commandType}";
            Width = 1050;
            MinHeight = 650;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // ThemeManager.ApplyTheme is keyed by theme name, not by Window.
            // Skip auto-theming here — the dialog inherits Revit's host theme.
            BuildLayout();
        }

        public Result ShowAndWait()
        {
            ShowDialog();
            return _result;
        }

        private void BuildLayout()
        {
            var root = new DockPanel { LastChildFill = true };

            // ── Footer ──
            var footer = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            footer.Children.Add(MakeButton("Preview (estimate)", OnPreview));
            footer.Children.Add(MakeButton("Save Preset",        OnSavePreset));
            footer.Children.Add(MakeButton("Cancel",             OnCancel));
            footer.Children.Add(MakeButton("Produce",            OnProduce, primary: true));
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Body grid ──
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(BuildLeftPanel());
            var split = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(split, 1);
            grid.Children.Add(split);
            grid.Children.Add(BuildRightTabs());

            root.Children.Add(grid);
            Content = root;

            LoadDefaults();
        }

        private UIElement BuildLeftPanel()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            Grid.SetColumn(stack, 0);

            stack.Children.Add(MakeHeading("Drawing Types to Produce"));
            _typesTree = new TreeView { Height = 220, Margin = new Thickness(0,0,0,8) };
            foreach (var dt in _types)
            {
                var top = new TreeViewItem { Header = MakeTypeHeader(dt), Tag = dt };
                top.Items.Add(new TreeViewItem { Header = "Configuration 1 (default)", Tag = dt });
                _typesTree.Items.Add(top);
            }
            stack.Children.Add(_typesTree);

            stack.Children.Add(MakeHeading("Contexts to Include"));
            _contextsList = new ListBox { Height = 180, SelectionMode = SelectionMode.Multiple, Margin = new Thickness(0,0,0,8) };
            foreach (var c in _contexts)
            {
                var cb = new CheckBox { Content = c, Tag = c, IsChecked = true };
                _contextsList.Items.Add(cb);
            }
            stack.Children.Add(_contextsList);

            stack.Children.Add(MakeHeading("Preset"));
            var presetRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0,0,0,4) };
            _presetCombo = new ComboBox();
            _presetCombo.Items.Add("— New —");
            try
            {
                foreach (var p in ProductionPresetRegistry.Load(_doc))
                    _presetCombo.Items.Add(p.Name ?? p.Id);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            _presetCombo.SelectedIndex = 0;
            presetRow.Children.Add(_presetCombo);
            stack.Children.Add(presetRow);

            return stack;
        }

        private FrameworkElement MakeTypeHeader(DrawingType dt)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var cb = new CheckBox { IsChecked = true, Tag = dt, VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = dt?.Name ?? dt?.Id ?? "(unnamed)", Margin = new Thickness(4,0,0,0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(cb);
            sp.Children.Add(tb);
            return sp;
        }

        private UIElement BuildRightTabs()
        {
            var tabs = new TabControl();
            Grid.SetColumn(tabs, 2);

            tabs.Items.Add(new TabItem { Header = "General",    Content = BuildGeneralTab() });
            tabs.Items.Add(new TabItem { Header = "VG Overrides", Content = BuildVgTab() });
            tabs.Items.Add(new TabItem { Header = "Annotation", Content = BuildAnnotationTab() });

            var t4 = new TabItem { Header = "Section / Elevation", Content = BuildSectionElevTab() };
            if (_commandType == "PerLevel" || _commandType == "ScopeBoxes") t4.Visibility = Visibility.Collapsed;
            tabs.Items.Add(t4);

            return tabs;
        }

        // ── Tab 1: General ──
        private UIElement BuildGeneralTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };

            sp.Children.Add(MakeCardHeader("Scope"));
            _allLevels = new RadioButton { Content = "All levels", IsChecked = true, GroupName = "lvlScope", Margin = new Thickness(0,2,0,2) };
            _selectedLevels = new RadioButton { Content = "Selected levels (use left context list)", GroupName = "lvlScope", Margin = new Thickness(0,2,0,2) };
            sp.Children.Add(_allLevels);
            sp.Children.Add(_selectedLevels);

            sp.Children.Add(MakeCardHeader("View Creation"));
            _dupNormal     = new RadioButton { Content = "Duplicate",                IsChecked = true, GroupName = "dup", Margin = new Thickness(0,2,0,2) };
            _dupDetailing  = new RadioButton { Content = "Duplicate with Detailing", GroupName = "dup", Margin = new Thickness(0,2,0,2) };
            _dupDependent  = new RadioButton { Content = "Duplicate as Dependent",   GroupName = "dup", Margin = new Thickness(0,2,0,2) };
            sp.Children.Add(_dupNormal);
            sp.Children.Add(_dupDetailing);
            sp.Children.Add(_dupDependent);
            _idempotent    = new CheckBox { Content = "Skip if a view already exists for this context", IsChecked = true, Margin = new Thickness(0,4,0,2) };
            _createSheets  = new CheckBox { Content = "Create sheets",            IsChecked = true, Margin = new Thickness(0,2,0,2) };
            _createPackage = new CheckBox { Content = "Create drawing package",   IsChecked = true, Margin = new Thickness(0,2,0,2) };
            sp.Children.Add(_idempotent);
            sp.Children.Add(_createSheets);
            sp.Children.Add(_createPackage);
            sp.Children.Add(MakeLabel("Package id:"));
            _packageId = new TextBox { Margin = new Thickness(0,2,0,4) };
            sp.Children.Add(_packageId);

            sp.Children.Add(MakeCardHeader("Scale and Detail"));
            sp.Children.Add(MakeLabel("Scale override:"));
            _scaleOverride = MakeCombo(new[] { "None", "1:20", "1:50", "1:100", "1:200", "1:500" });
            sp.Children.Add(_scaleOverride);
            sp.Children.Add(MakeLabel("Detail level override:"));
            _detailLevelOverride = MakeCombo(new[] { "By View", "Coarse", "Medium", "Fine" });
            sp.Children.Add(_detailLevelOverride);

            sp.Children.Add(MakeCardHeader("Generation Rules"));
            _onlyDefault = new CheckBox { Content = "Generate views only for default configuration", Margin = new Thickness(0,2,0,2) };
            _hideUnused  = new CheckBox { Content = "Hide categories with no visible elements in view", Margin = new Thickness(0,2,0,2) };
            sp.Children.Add(_onlyDefault);
            sp.Children.Add(_hideUnused);
            // Phase 137 — GRAITEC PowerPack "Customize Drawings" parity:
            //   * Hide unwanted sections — strips section heads/markers off the produced VG
            //   * Hide unwanted rebars   — strips rebar tags / location lines off the produced VG
            //   * Hide unwanted tags     — strips tag annotations from the produced view
            //   * Skip empty levels      — when on, levels with no model elements get no view
            _hideUnwantedSections = new CheckBox { Content = "Hide unwanted sections (GRAITEC parity)", Margin = new Thickness(0,2,0,2) };
            _hideUnwantedRebars   = new CheckBox { Content = "Hide unwanted rebars (GRAITEC parity)",   Margin = new Thickness(0,2,0,2) };
            _hideUnwantedTags     = new CheckBox { Content = "Hide unwanted tags",                       Margin = new Thickness(0,2,0,2) };
            _skipEmptyLevels      = new CheckBox { Content = "Skip levels with no model elements",       IsChecked = true, Margin = new Thickness(0,2,0,2) };
            sp.Children.Add(_hideUnwantedSections);
            sp.Children.Add(_hideUnwantedRebars);
            sp.Children.Add(_hideUnwantedTags);
            sp.Children.Add(_skipEmptyLevels);

            return sv;
        }

        // ── Tab 2: VG Overrides (compact) ──
        private RevitVgEditor _vgEditor;
        private Dictionary<string, PresetCategoryOverride> _vgData;

        private UIElement BuildVgTab()
        {
            var dp = new DockPanel { LastChildFill = true, Margin = new Thickness(8) };
            var topBar = new TextBlock {
                Text = "VG Overrides — full Revit Visibility/Graphics dialog parity. Every model + annotation category " +
                       "from the active project is pre-populated, with subcategories indented underneath. " +
                       "Set per-cell overrides; empty cells inherit from the view template.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,6),
                Foreground = new SolidColorBrush(Colors.Gray)
            };
            DockPanel.SetDock(topBar, Dock.Top);
            dp.Children.Add(topBar);

            _vgData = new Dictionary<string, PresetCategoryOverride>(StringComparer.OrdinalIgnoreCase);
            _vgEditor = new RevitVgEditor(_doc, _vgData);
            dp.Children.Add(_vgEditor.Build());
            return dp;
        }

        // ── Tab 3: Annotation ──
        private UIElement BuildAnnotationTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };

            sp.Children.Add(MakeCardHeader("Run Annotation"));
            _runAnno  = new CheckBox { Content = "Run annotation after view creation", IsChecked = true };
            _runTags  = new CheckBox { Content = "Auto-tag elements",                 IsChecked = true };
            _runDims  = new CheckBox { Content = "Auto-dimension (grids, levels)",    IsChecked = true };
            _runDec   = new CheckBox { Content = "Decorative (north arrow, scale bar)", IsChecked = true };
            _runSpots = new CheckBox { Content = "Spot elevations / coordinates",     IsChecked = false };
            sp.Children.Add(_runAnno);
            sp.Children.Add(_runTags);
            sp.Children.Add(_runDims);
            sp.Children.Add(_runDec);
            sp.Children.Add(_runSpots);

            sp.Children.Add(MakeCardHeader("Tag Rules"));
            _tagRows = new ObservableCollection<AutoAnnotationRule>();
            _tagGrid = new DataGrid {
                ItemsSource = _tagRows, AutoGenerateColumns = false,
                CanUserAddRows = false, CanUserDeleteRows = true,
                Height = 180
            };
            _tagGrid.Columns.Add(MakeTextCol("Category", "Category"));
            _tagGrid.Columns.Add(MakeTextCol("Tag Family", "TagFamily"));
            _tagGrid.Columns.Add(MakeTextCol("Leader",     "LeaderStyle"));
            _tagGrid.Columns.Add(MakeTextCol("Depth",      "Tag7Depth"));
            _tagGrid.Columns.Add(MakeTextCol("Density",    "DensityMode"));
            _tagGrid.Columns.Add(MakeBoolCol("Skip Tagged","SkipIfTagged"));
            sp.Children.Add(_tagGrid);
            var addTag = new Button { Content = "+ Add Tag Rule", Margin = new Thickness(0,4,0,4), HorizontalAlignment = HorizontalAlignment.Left };
            addTag.Click += (s,e) => _tagRows.Add(new AutoAnnotationRule { RuleType = "AutoTag", Category = "*", SkipIfTagged = true, DensityMode = "All" });
            sp.Children.Add(addTag);

            sp.Children.Add(MakeCardHeader("Dim Rules"));
            _dimRows = new ObservableCollection<AutoAnnotationRule>();
            _dimGrid = new DataGrid {
                ItemsSource = _dimRows, AutoGenerateColumns = false,
                CanUserAddRows = false, CanUserDeleteRows = true,
                Height = 120
            };
            _dimGrid.Columns.Add(MakeTextCol("Type",   "RuleType"));
            _dimGrid.Columns.Add(MakeTextCol("Target", "Category"));
            _dimGrid.Columns.Add(MakeTextCol("Style",  "TagFamily"));
            sp.Children.Add(_dimGrid);
            var addDim = new Button { Content = "+ Add Dim Rule", Margin = new Thickness(0,4,0,4), HorizontalAlignment = HorizontalAlignment.Left };
            addDim.Click += (s,e) => _dimRows.Add(new AutoAnnotationRule { RuleType = "AutoDim", Category = "Grids" });
            sp.Children.Add(addDim);

            sp.Children.Add(MakeCardHeader("Decorative Annotation"));
            _northArrowFamily = AddTextRow(sp, "North Arrow family:");
            _northArrowPos = MakeCombo(new[] { "BottomLeft", "BottomRight", "TopLeft", "TopRight" });
            sp.Children.Add(_northArrowPos);
            _scaleBarFamily = AddTextRow(sp, "Scale Bar family:");
            _scaleBarPos = MakeCombo(new[] { "BottomLeft", "BottomRight", "TopLeft", "TopRight" });
            sp.Children.Add(_scaleBarPos);
            _keyPlanFamily = AddTextRow(sp, "Key Plan family:");
            _keyPlanPos = MakeCombo(new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" });
            sp.Children.Add(_keyPlanPos);
            _matchlineMm = AddTextRow(sp, "Matchline offset (mm, 0 = off):");

            return sv;
        }

        // ── Tab 4: Section / Elevation (context-sensitive) ──
        private UIElement BuildSectionElevTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };

            bool isSection = _commandType == "Sections";
            bool isElev    = _commandType == "ExteriorElevations" || _commandType == "InteriorElevations";

            if (isSection)
            {
                sp.Children.Add(MakeCardHeader("Cutting Direction"));
                _sectPerp   = new RadioButton { Content = "Perpendicular to walls",  IsChecked = true,  GroupName = "cut" };
                _sectNS     = new RadioButton { Content = "North-South + East-West",                    GroupName = "cut" };
                _sectEW     = new RadioButton { Content = "East-West only",                              GroupName = "cut" };
                _sectCustom = new RadioButton { Content = "Custom angle",                                GroupName = "cut" };
                sp.Children.Add(_sectPerp);
                sp.Children.Add(_sectNS);
                sp.Children.Add(_sectEW);
                sp.Children.Add(_sectCustom);
                _sectAngle = AddTextRow(sp, "Custom angle (°):");

                sp.Children.Add(MakeCardHeader("Section Geometry"));
                _sectSpacing = AddTextRow(sp, "Spacing (mm):");      _sectSpacing.Text = "5000";
                _sectDepth   = AddTextRow(sp, "Section depth (mm):"); _sectDepth.Text   = "10000";
                _sectFar     = AddTextRow(sp, "Far clip (mm):");      _sectFar.Text     = "10000";
                _sectSegmented = new CheckBox { Content = "Segmented / jogged section" };
                sp.Children.Add(_sectSegmented);

                sp.Children.Add(MakeCardHeader("Auto-Placement"));
                _sectAutoManual = new RadioButton { Content = "Manual selection (pick in model)", IsChecked = true, GroupName = "auto" };
                _sectAutoGrid   = new RadioButton { Content = "Along grid lines",                                GroupName = "auto" };
                _sectAutoRoom   = new RadioButton { Content = "Per room",                                        GroupName = "auto" };
                sp.Children.Add(_sectAutoManual);
                sp.Children.Add(_sectAutoGrid);
                sp.Children.Add(_sectAutoRoom);

                sp.Children.Add(MakeCardHeader("Annotation"));
                _sectShowLevels = new CheckBox { Content = "Show & annotate Levels", IsChecked = true };
                _sectShowGrids  = new CheckBox { Content = "Show & annotate Grids",  IsChecked = true };
                sp.Children.Add(_sectShowLevels);
                sp.Children.Add(_sectShowGrids);

                sp.Children.Add(MakeCardHeader("Output"));
                _sectOutSection = new RadioButton { Content = "Sections only",     IsChecked = true, GroupName = "out" };
                _sectOutCallout = new RadioButton { Content = "Callouts only",                       GroupName = "out" };
                _sectOutBoth    = new RadioButton { Content = "Sections + callout references",       GroupName = "out" };
                sp.Children.Add(_sectOutSection);
                sp.Children.Add(_sectOutCallout);
                sp.Children.Add(_sectOutBoth);
            }
            else if (isElev)
            {
                sp.Children.Add(MakeCardHeader("Faces to Produce"));
                _elevN = new CheckBox { Content = "North", IsChecked = true };
                _elevS = new CheckBox { Content = "South", IsChecked = true };
                _elevE = new CheckBox { Content = "East",  IsChecked = true };
                _elevW = new CheckBox { Content = "West",  IsChecked = true };
                sp.Children.Add(_elevN);
                sp.Children.Add(_elevS);
                sp.Children.Add(_elevE);
                sp.Children.Add(_elevW);

                sp.Children.Add(MakeCardHeader("Geometry"));
                _elevOffset = AddTextRow(sp, "Offset from footprint (mm):"); _elevOffset.Text = "3000";
                _elevFar    = AddTextRow(sp, "Far clip (mm):");              _elevFar.Text    = "30000";

                sp.Children.Add(MakeCardHeader("Marker"));
                _elevMarker = AddTextRow(sp, "Elevation marker family (blank = default):");

                sp.Children.Add(MakeCardHeader("Sheet Layout"));
                _elev1Plus4 = new CheckBox { Content = "Place all 4 elevations on one 1+4 sheet (requires 4-slot DrawingType)", IsChecked = true };
                sp.Children.Add(_elev1Plus4);

                sp.Children.Add(MakeCardHeader("Annotation"));
                _elevShowLevels = new CheckBox { Content = "Show & annotate Levels", IsChecked = true };
                _elevShowGrids  = new CheckBox { Content = "Show & annotate Grids",  IsChecked = true };
                sp.Children.Add(_elevShowLevels);
                sp.Children.Add(_elevShowGrids);
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = "(Tab not used for " + _commandType + ".)", Margin = new Thickness(0,8,0,0) });
            }

            return sv;
        }

        // ── Footer handlers ──
        private void OnCancel(object s, RoutedEventArgs e)
        {
            _result.Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void OnPreview(object s, RoutedEventArgs e)
        {
            int contexts = _contextsList.Items.OfType<CheckBox>().Count(c => c.IsChecked == true);
            int types = _typesTree.Items.OfType<TreeViewItem>().Count(t => GetItemChecked(t));
            int est = contexts * Math.Max(types, 1);
            MessageBox.Show(this,
                $"Estimated production:\n{est} views over {contexts} context(s) × {types} drawing type(s).",
                "Preview", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSavePreset(object s, RoutedEventArgs e)
        {
            try
            {
                var preset = CollectPreset();
                if (string.IsNullOrEmpty(preset.Name))
                    preset.Name = $"Preset {DateTime.UtcNow:yyyyMMdd-HHmmss}";
                var existing = ProductionPresetRegistry.Load(_doc) ?? new List<DrawingProductionPreset>();
                existing.RemoveAll(p => string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
                existing.Add(preset);
                ProductionPresetRegistry.Save(_doc, existing);
                MessageBox.Show(this, $"Saved preset '{preset.Name}'.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Save failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnProduce(object s, RoutedEventArgs e)
        {
            try
            {
                _result.Preset = CollectPreset();
                _result.SelectedDrawingTypeIds = _typesTree.Items.OfType<TreeViewItem>()
                    .Where(GetItemChecked)
                    .Select(t => (t.Tag as DrawingType)?.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
                _result.SelectedContexts = _contextsList.Items.OfType<CheckBox>()
                    .Where(c => c.IsChecked == true)
                    .Select(c => c.Content?.ToString())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();

                if (_result.SelectedDrawingTypeIds.Count == 0 || _result.SelectedContexts.Count == 0)
                {
                    MessageBox.Show(this, "Pick at least one drawing type and one context.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _result.Confirmed = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Produce failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private DrawingProductionPreset CollectPreset()
        {
            var preset = new DrawingProductionPreset
            {
                Id = $"preset-{Guid.NewGuid():N}".Substring(0, 16),
                CommandType = _commandType,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                CreatedBy = "STING",
                CreateSheets = _createSheets?.IsChecked == true,
                CreatePackage = _createPackage?.IsChecked == true,
                PackageId = _packageId?.Text,
                General = new ProductionGeneralSettings
                {
                    DuplicateOption = _dupDetailing?.IsChecked == true ? "DuplicateWithDetailing"
                        : _dupDependent?.IsChecked == true ? "DuplicateAsDependent" : "Duplicate",
                    Idempotent = _idempotent?.IsChecked == true,
                    RunAnnotation = _runAnno?.IsChecked == true,
                    HideUnwantedCats = _hideUnused?.IsChecked == true,
                    GenerateOnlyDefault = _onlyDefault?.IsChecked == true,
                    HideUnwantedSections = _hideUnwantedSections?.IsChecked == true,
                    HideUnwantedRebars   = _hideUnwantedRebars?.IsChecked == true,
                    HideUnwantedTags     = _hideUnwantedTags?.IsChecked == true,
                    SkipEmptyLevels      = _skipEmptyLevels?.IsChecked != false,
                    ScaleOverride = ParseScale(_scaleOverride?.Text),
                    DetailLevelOverride = (_detailLevelOverride?.Text == "By View") ? null : _detailLevelOverride?.Text
                }
            };

            // VG: collapse all rows under a wildcard "*" key — Part 5 commands
            // can re-key per drawing type. Only persist rows the user actually
            // touched (any non-default field set), not the whole catalogue.
            if (_vgData != null && _vgData.Count > 0)
            {
                var list = _vgData.Values.Where(IsOverrideMeaningful).ToList();
                if (list.Count > 0) preset.VgOverrides["*"] = list;
            }

            // Annotation
            var pack = new AnnotationRulePack
            {
                Rules = _tagRows?.Concat(_dimRows ?? new ObservableCollection<AutoAnnotationRule>())
                                .Where(r => !string.IsNullOrEmpty(r.Category))
                                .ToList() ?? new List<AutoAnnotationRule>(),
                NorthArrowFamily   = _northArrowFamily?.Text,
                NorthArrowPosition = _northArrowPos?.Text,
                ScaleBarFamily     = _scaleBarFamily?.Text,
                ScaleBarPosition   = _scaleBarPos?.Text,
                KeyPlanFamily      = _keyPlanFamily?.Text,
                KeyPlanPosition    = _keyPlanPos?.Text,
                MatchlineOffsetMm  = double.TryParse(_matchlineMm?.Text, out var mm) && mm > 0 ? (double?)mm : null
            };
            preset.AnnotationOverrides["*"] = pack;

            // Section / Elevation config
            if (_commandType == "Sections" && _sectPerp != null)
            {
                preset.SectionConfig = new SectionProductionConfig
                {
                    CuttingDirection = _sectNS?.IsChecked == true ? "NorthSouth"
                                     : _sectEW?.IsChecked == true ? "EastWest"
                                     : _sectCustom?.IsChecked == true ? "CustomAngle"
                                     : "Perpendicular",
                    CustomAngleDeg = double.TryParse(_sectAngle?.Text, out var a) ? (double?)a : null,
                    SpacingMm = double.TryParse(_sectSpacing?.Text, out var sp1) ? sp1 : 5000,
                    DepthMm   = double.TryParse(_sectDepth?.Text,   out var dp) ? dp : 10000,
                    FarClipMm = double.TryParse(_sectFar?.Text,     out var fc) ? fc : 10000,
                    ShowLevels = _sectShowLevels?.IsChecked == true,
                    ShowGrids  = _sectShowGrids?.IsChecked == true,
                    SegmentedView = _sectSegmented?.IsChecked == true,
                    AutoPlace = _sectAutoGrid?.IsChecked == true ? "AlongGridLines"
                              : _sectAutoRoom?.IsChecked == true ? "PerRoom"
                              : "ManualSelection",
                    CalloutMode = _sectOutCallout?.IsChecked == true ? "Callout"
                                : _sectOutBoth?.IsChecked    == true ? "Both"
                                : "Section"
                };
            }
            if ((_commandType == "ExteriorElevations" || _commandType == "InteriorElevations") && _elevN != null)
            {
                var faces = new List<string>();
                if (_elevN?.IsChecked == true) faces.Add("North");
                if (_elevS?.IsChecked == true) faces.Add("South");
                if (_elevE?.IsChecked == true) faces.Add("East");
                if (_elevW?.IsChecked == true) faces.Add("West");
                preset.ElevationConfig = new ElevationProductionConfig
                {
                    FacesTo = faces,
                    OffsetMm = double.TryParse(_elevOffset?.Text, out var off) ? off : 3000,
                    FarClipMm = double.TryParse(_elevFar?.Text, out var ef) ? ef : 30000,
                    ShowLevels = _elevShowLevels?.IsChecked == true,
                    ShowGrids = _elevShowGrids?.IsChecked == true,
                    UseOneFourViewSheet = _elev1Plus4?.IsChecked == true,
                    MarkerFamily = _elevMarker?.Text
                };
            }

            return preset;
        }

        private void LoadDefaults()
        {
            // Seed annotation rules with one wildcard row so the user has
            // something to edit rather than an empty grid.
            if (_tagRows != null && _tagRows.Count == 0)
                _tagRows.Add(new AutoAnnotationRule { RuleType = "AutoTag", Category = "Rooms", SkipIfTagged = true, DensityMode = "All" });
            if (_dimRows != null && _dimRows.Count == 0)
                _dimRows.Add(new AutoAnnotationRule { RuleType = "AutoDim", Category = "Grids" });
        }

        // ── Helpers ──
        private static int? ParseScale(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "None") return null;
            if (s.StartsWith("1:")) s = s.Substring(2);
            return int.TryParse(s, out var n) ? (int?)n : null;
        }

        private bool GetItemChecked(TreeViewItem it)
        {
            try
            {
                if (it.Header is StackPanel sp)
                    foreach (var ch in sp.Children)
                        if (ch is CheckBox cb) return cb.IsChecked == true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static Button MakeButton(string text, RoutedEventHandler click, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4, 0, 0, 0), MinWidth = 100 };
            if (primary) b.FontWeight = FontWeights.Bold;
            b.Click += click;
            return b;
        }

        private static TextBlock MakeHeading(string s) =>
            new TextBlock { Text = s, FontWeight = FontWeights.Bold, Margin = new Thickness(0,8,0,4) };

        private static TextBlock MakeCardHeader(string s) =>
            new TextBlock { Text = s, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0,12,0,4) };

        private static TextBlock MakeLabel(string s) =>
            new TextBlock { Text = s, Margin = new Thickness(0,4,0,2) };

        private static ComboBox MakeCombo(string[] items)
        {
            var cb = new ComboBox { Margin = new Thickness(0,2,0,4), MinWidth = 140 };
            foreach (var i in items) cb.Items.Add(i);
            cb.SelectedIndex = 0;
            return cb;
        }

        private static TextBox AddTextRow(StackPanel sp, string label)
        {
            sp.Children.Add(MakeLabel(label));
            var tb = new TextBox { Margin = new Thickness(0,2,0,4) };
            sp.Children.Add(tb);
            return tb;
        }

        private static DataGridTextColumn MakeTextCol(string header, string path) =>
            new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } };

        private static DataGridCheckBoxColumn MakeBoolCol(string header, string path) =>
            new DataGridCheckBoxColumn { Header = header, Binding = new System.Windows.Data.Binding(path) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } };

        /// <summary>
        /// True when the override carries at least one non-null cell. The VG
        /// editor pre-populates every category in the project but persisting
        /// the entire catalogue would bloat preset JSON; we only keep rows
        /// the user actually touched.
        /// </summary>
        private static bool IsOverrideMeaningful(PresetCategoryOverride o)
        {
            if (o == null) return false;
            return o.Visible.HasValue || o.Halftone.HasValue
                || !string.IsNullOrEmpty(o.ProjLineColor) || o.ProjLineWeight.HasValue || !string.IsNullOrEmpty(o.ProjLinePattern)
                || !string.IsNullOrEmpty(o.SurfFgColor)   || !string.IsNullOrEmpty(o.SurfFgPattern)
                || !string.IsNullOrEmpty(o.SurfBgColor)   || !string.IsNullOrEmpty(o.SurfBgPattern)
                || o.Transparency.HasValue
                || !string.IsNullOrEmpty(o.CutLineColor)  || o.CutLineWeight.HasValue || !string.IsNullOrEmpty(o.CutLinePattern)
                || !string.IsNullOrEmpty(o.CutFgColor)    || !string.IsNullOrEmpty(o.CutFgPattern)
                || !string.IsNullOrEmpty(o.CutBgColor)    || !string.IsNullOrEmpty(o.CutBgPattern)
                || !string.IsNullOrEmpty(o.DetailLevel);
        }
    }
}
