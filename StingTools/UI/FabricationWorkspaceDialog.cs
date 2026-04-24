// StingTools v4 MVP — unified Fabrication Workspace dialog.
//
// One WPF modal that replaces the scattered Generate Package /
// Cut List / Isometrics / Weld Map buttons with a single preview
// surface. Live tabs render each action's payload before export so
// the user can review / uncheck individual rows and pick a format.
//
// Threading: opened modally from StingCommandHandler.Execute on the
// Revit API thread, so FilteredElementCollector calls and action
// dispatch are all safe from inside ShowDialog().

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Fabrication;
using StingTools.Core;
using StingTools.Core.Fabrication;

using Button       = System.Windows.Controls.Button;
using CheckBox     = System.Windows.Controls.CheckBox;
using ComboBox     = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using DataGrid            = System.Windows.Controls.DataGrid;
using DataGridTextColumn  = System.Windows.Controls.DataGridTextColumn;
using DataGridCheckBoxColumn = System.Windows.Controls.DataGridCheckBoxColumn;
using Grid         = System.Windows.Controls.Grid;
using RadioButton  = System.Windows.Controls.RadioButton;
using TextBox      = System.Windows.Controls.TextBox;
using Color        = System.Windows.Media.Color;

namespace StingTools.UI
{
    public enum FabAction { GeneratePackage, CutList, WeldMap, Isometrics, Pcf, Maj, BomRollup }
    public enum FabFormat { Csv, Xlsx, Pdf, Revit }

    public class FabricationWorkspaceDialog : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly FabAction _initialAction;

        private FabScopeResult _scope;
        private readonly Dictionary<string, CheckBox> _catChecks
            = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        private RadioButton _rbSel, _rbView, _rbProj;
        private TextBlock   _txtScopeSummary;
        private StackPanel  _catPanel;
        private TextBlock   _txtDiscRollup;
        private TextBlock   _txtTitleBlock;
        private TabControl  _previewTabs;

        // Preview collections (bound to DataGrids)
        private readonly ObservableCollection<CutListRow>      _cutRows  = new ObservableCollection<CutListRow>();
        private readonly ObservableCollection<WeldMapRow>      _weldRows = new ObservableCollection<WeldMapRow>();
        private readonly ObservableCollection<PackageGroupRow> _pkgRows  = new ObservableCollection<PackageGroupRow>();
        private readonly ObservableCollection<IsoSheetRow>     _isoRows  = new ObservableCollection<IsoSheetRow>();

        private DataGrid _cutGrid, _weldGrid, _pkgGrid, _isoGrid, _pcfGrid, _majGrid;
        private TextBlock _cutCount, _weldCount, _pkgCount, _isoCount, _pcfCount, _majCount;

        // Phase 2 — per-system / per-level filter pills
        private WrapPanel _systemPills, _levelPills;
        private readonly HashSet<string> _systemAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _levelAllow  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1 — preset combo
        private ComboBox _cmbPreset;

        // Phase 3 — format combos per action
        private ComboBox _cmbCutFormat, _cmbWeldFormat, _cmbIsoFormat;

        // Phase 5 — PCF / MAJ preview collections
        private readonly ObservableCollection<PcfSystemRow> _pcfRows = new ObservableCollection<PcfSystemRow>();
        private readonly ObservableCollection<MajFabRow>    _majRows = new ObservableCollection<MajFabRow>();

        // Phase 4 — clash pre-flight cache
        private TextBlock _txtClashSummary;

        public FabricationWorkspaceDialog(UIDocument uidoc, FabAction initialAction)
        {
            _uidoc = uidoc;
            _doc = uidoc?.Document;
            _initialAction = initialAction;

            Title = "STING v4 — Fabrication Workspace";
            Width = 980;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.CanResize;

            BuildUi();
            RefreshScope();
            SelectInitialTab();
        }

        private void SelectInitialTab()
        {
            if (_previewTabs == null) return;
            int idx = _initialAction switch
            {
                FabAction.GeneratePackage => 0,
                FabAction.CutList         => 1,
                FabAction.WeldMap         => 2,
                FabAction.Isometrics      => 3,
                FabAction.Pcf             => 4,
                FabAction.Maj             => 5,
                _                         => 0,
            };
            if (_previewTabs.Items.Count > idx) _previewTabs.SelectedIndex = idx;
        }

        // ── Layout ──────────────────────────────────────────────

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 preset bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1 scope radios
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 scope summary
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) }); // 3 category list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 discipline rollup
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 system pills
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6 level pills
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 7 preview tabs
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 8 clash summary
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 9 title block
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 10 actions

            // ─ Scope radios
            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal };
            scopeRow.Children.Add(Hdr("SCOPE"));
            _rbSel  = Radio("Selection",   FabricationOptions.ScopeSelection);
            _rbView = Radio("Active view", FabricationOptions.ScopeActiveView);
            _rbProj = Radio("Project",     FabricationOptions.ScopeProject);
            _rbSel.Checked  += OnScopeChanged;
            _rbView.Checked += OnScopeChanged;
            _rbProj.Checked += OnScopeChanged;
            scopeRow.Children.Add(_rbSel);
            scopeRow.Children.Add(_rbView);
            scopeRow.Children.Add(_rbProj);
            scopeRow.Children.Add(MiniButton("Refresh", (_, __) => RefreshScope()));
            Grid.SetRow(scopeRow, 1);
            root.Children.Add(scopeRow);

            // Row 0 — preset bar (prepended)
            var presetRow = BuildPresetRow();
            Grid.SetRow(presetRow, 0);
            root.Children.Add(presetRow);

            // ─ Scope summary
            _txtScopeSummary = new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 6),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(_txtScopeSummary, 2);
            root.Children.Add(_txtScopeSummary);

            // ─ Category checkboxes (toggle include/exclude per Revit category)
            var catBox = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29)),
            };
            var catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _catPanel = new StackPanel();
            catScroll.Content = _catPanel;
            catBox.Child = catScroll;
            Grid.SetRow(catBox, 3);
            root.Children.Add(catBox);

            // ─ Discipline roll-up
            _txtDiscRollup = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(_txtDiscRollup, 4);
            root.Children.Add(_txtDiscRollup);

            // Rows 5/6 — system/level filter pills
            _systemPills = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            _levelPills  = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(_systemPills, 5);
            Grid.SetRow(_levelPills, 6);
            root.Children.Add(_systemPills);
            root.Children.Add(_levelPills);

            // ─ Preview tabs
            _previewTabs = BuildPreviewTabs();
            Grid.SetRow(_previewTabs, 7);
            root.Children.Add(_previewTabs);

            // Row 8 — clash summary line
            _txtClashSummary = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x9E, 0x4F)),
                TextWrapping = TextWrapping.Wrap,
                Text = "",
            };
            Grid.SetRow(_txtClashSummary, 8);
            root.Children.Add(_txtClashSummary);

            // ─ Title block status row
            var tbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) };
            _txtTitleBlock = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Margin = new Thickness(0, 0, 10, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
            };
            tbRow.Children.Add(_txtTitleBlock);
            tbRow.Children.Add(MiniButton("Configure…", (_, __) => ConfigureTitleBlock()));
            tbRow.Children.Add(MiniButton("Clear", (_, __) =>
            {
                FabricationOptions.ShopDrawing = null;
                RefreshTitleBlockLine();
                StingDockPanel.LastInstance?.UpdateFabShopDrawingStatus(null, null);
            }));
            Grid.SetRow(tbRow, 9);
            root.Children.Add(tbRow);

            // ─ Actions
            var actionRow = BuildActionRow();
            Grid.SetRow(actionRow, 10);
            root.Children.Add(actionRow);

            Content = root;
        }

        // ── Preview tabs ───────────────────────────────────────

        private TabControl BuildPreviewTabs()
        {
            var tabs = new TabControl
            {
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6),
            };
            tabs.Items.Add(BuildPackageTab());
            tabs.Items.Add(BuildCutListTab());
            tabs.Items.Add(BuildWeldMapTab());
            tabs.Items.Add(BuildIsoTab());
            tabs.Items.Add(BuildPcfTab());
            tabs.Items.Add(BuildMajTab());
            return tabs;
        }

        // Phase 5 — PCF preview tab (one row per piping system)
        private TabItem BuildPcfTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _pcfCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_pcfCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_pcfRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_pcfRows, false, r => r.Include = false)));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _pcfGrid = MakeGrid();
            _pcfGrid.ItemsSource = _pcfRows;
            _pcfGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc", Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _pcfGrid.Columns.Add(new DataGridTextColumn { Header = "System",     Binding = new Binding("System"),         Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
            _pcfGrid.Columns.Add(new DataGridTextColumn { Header = "Pipes",      Binding = new Binding("PipeCount"),      Width = 80, IsReadOnly = true });
            _pcfGrid.Columns.Add(new DataGridTextColumn { Header = "Fittings",   Binding = new Binding("FittingCount"),   Width = 90, IsReadOnly = true });
            _pcfGrid.Columns.Add(new DataGridTextColumn { Header = "Accessories",Binding = new Binding("AccessoryCount"), Width = 100, IsReadOnly = true });
            panel.Children.Add(_pcfGrid);
            return MakeTab("PCF", panel);
        }

        // Phase 5 — MAJ preview tab (one row per fab-part element)
        private TabItem BuildMajTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _majCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_majCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_majRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_majRows, false, r => r.Include = false)));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _majGrid = MakeGrid();
            _majGrid.ItemsSource = _majRows;
            _majGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc", Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _majGrid.Columns.Add(new DataGridTextColumn { Header = "Element ID",  Binding = new Binding("ElementId"),   Width = 90,  IsReadOnly = true });
            _majGrid.Columns.Add(new DataGridTextColumn { Header = "Category",    Binding = new Binding("Category"),    Width = 140, IsReadOnly = true });
            _majGrid.Columns.Add(new DataGridTextColumn { Header = "Service",     Binding = new Binding("ServiceName"), Width = 140 });
            _majGrid.Columns.Add(new DataGridTextColumn { Header = "Part name",   Binding = new Binding("PartName"),    Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            panel.Children.Add(_majGrid);
            return MakeTab("MAJ", panel);
        }

        private static TabItem MakeTab(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header,
                Content = content,
                Foreground = Brushes.Black,
            };
        }

        private DataGrid MakeGrid()
        {
            return new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeight = 20,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x21)),
                RowBackground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x36)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
        }

        // ─ PACKAGE tab (grouped assemblies that will be created)
        private TabItem BuildPackageTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _pkgCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_pkgCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_pkgRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_pkgRows, false, r => r.Include = false)));
            bar.Children.Add(MiniButton("Smart group", (_, __) => ReBuildPackageWithGrouper()));
            bar.Children.Add(MiniButton("Clash pre-flight", (_, __) => RunClashPreflight()));
            bar.Children.Add(MiniButton("Undo last run", (_, __) => UndoLastRun()));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _pkgGrid = MakeGrid();
            _pkgGrid.ItemsSource = _pkgRows;
            _pkgGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc",   Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Discipline", Binding = new Binding("Discipline"),          Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "System",     Binding = new Binding("System"),              Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Level",      Binding = new Binding("Level"),               Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Elements",   Binding = new Binding("ElementCount"),        Width = 80,  IsReadOnly = true });
            // Phase 5 #8 — inline-editable spool name
            _pkgGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Assembly name",
                Binding = new Binding("AssemblyNamePreview") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = false,
            });
            panel.Children.Add(_pkgGrid);
            return MakeTab("Package", panel);
        }

        // ─ CUT LIST tab
        private TabItem BuildCutListTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _cutCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_cutCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_cutRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_cutRows, false, r => r.Include = false)));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _cutGrid = MakeGrid();
            _cutGrid.ItemsSource = _cutRows;
            _cutGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc", Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "Element ID", Binding = new Binding("ElementId"), Width = 90,  IsReadOnly = true });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "System",     Binding = new Binding("System"),    Width = 130 });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "Size (mm)",  Binding = new Binding("SizeMm") { StringFormat = "F0" },  Width = 80 });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "Length (mm)",Binding = new Binding("LengthMm") { StringFormat = "F0" },Width = 100 });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "Material",   Binding = new Binding("Material"),  Width = 150 });
            _cutGrid.Columns.Add(new DataGridTextColumn { Header = "Mitre°",     Binding = new Binding("MitreAngleDeg") { StringFormat = "F1" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            panel.Children.Add(_cutGrid);
            return MakeTab("Cut list", panel);
        }

        // ─ WELD MAP tab
        private TabItem BuildWeldMapTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _weldCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_weldCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_weldRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_weldRows, false, r => r.Include = false)));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _weldGrid = MakeGrid();
            _weldGrid.ItemsSource = _weldRows;
            _weldGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc", Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Element ID", Binding = new Binding("ElementId"), Width = 90,  IsReadOnly = true });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Category",   Binding = new Binding("Category"),  Width = 140, IsReadOnly = true });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Name",       Binding = new Binding("Name"),      Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Weld type",  Binding = new Binding("WeldType"),  Width = 90 });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Size (mm)",  Binding = new Binding("SizeMm"),    Width = 80 });
            _weldGrid.Columns.Add(new DataGridTextColumn { Header = "Schedule",   Binding = new Binding("Schedule"),  Width = 90 });
            panel.Children.Add(_weldGrid);
            return MakeTab("Weld map", panel);
        }

        // ─ ISOMETRICS tab (existing SP-... sheets)
        private TabItem BuildIsoTab()
        {
            var panel = new DockPanel { LastChildFill = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            _isoCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)), VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(_isoCount);
            bar.Children.Add(MiniButton("Tick all",   (_, __) => SetAll(_isoRows, true,  r => r.Include = true)));
            bar.Children.Add(MiniButton("Untick all", (_, __) => SetAll(_isoRows, false, r => r.Include = false)));
            bar.Children.Add(MiniButton("Open sheet", (_, __) => OpenSelectedIsoSheet()));
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _isoGrid = MakeGrid();
            _isoGrid.ItemsSource = _isoRows;
            _isoGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc", Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _isoGrid.Columns.Add(new DataGridTextColumn { Header = "Sheet ID", Binding = new Binding("SheetId"),    Width = 90,  IsReadOnly = true });
            _isoGrid.Columns.Add(new DataGridTextColumn { Header = "Number",   Binding = new Binding("SheetNumber"),Width = 160, IsReadOnly = true });
            _isoGrid.Columns.Add(new DataGridTextColumn { Header = "Name",     Binding = new Binding("Name"),       Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            panel.Children.Add(_isoGrid);
            return MakeTab("Isometrics", panel);
        }

        private void OpenSelectedIsoSheet()
        {
            try
            {
                if (!(_isoGrid?.SelectedItem is IsoSheetRow row)) return;
                if (_doc?.GetElement(new ElementId(row.SheetId)) is ViewSheet s)
                    _uidoc.ActiveView = s;
            }
            catch (Exception ex) { StingLog.Warn($"OpenSelectedIsoSheet: {ex.Message}"); }
        }

        private static void SetAll<T>(ObservableCollection<T> rows, bool value, Action<T> setter)
        {
            foreach (var r in rows) setter(r);
            // Force grid refresh by re-assigning the items source? Instead, cycle:
            var temp = rows.ToList();
            rows.Clear();
            foreach (var t in temp) rows.Add(t);
        }

        // ── Action row ─────────────────────────────────────────

        private UIElement BuildActionRow()
        {
            // Two rows of actions: primary 4 + format combos, secondary 3 (PCF/MAJ/BOM/Incremental/Close).
            var outer = new StackPanel();

            var grid = new Grid();
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _cmbCutFormat  = FormatCombo(new[] { "CSV", "XLSX" });
            _cmbWeldFormat = FormatCombo(new[] { "CSV", "XLSX" });
            _cmbIsoFormat  = FormatCombo(new[] { "CSV index", "PDF (combined)" });

            AddActionCellWithFormat(grid, 0, "Generate package", new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                null, () => RunAction(FabAction.GeneratePackage), _initialAction == FabAction.GeneratePackage);
            AddActionCellWithFormat(grid, 1, "Export cut list",  new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                _cmbCutFormat,  () => RunAction(FabAction.CutList),        _initialAction == FabAction.CutList);
            AddActionCellWithFormat(grid, 2, "Export weld map",  new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
                _cmbWeldFormat, () => RunAction(FabAction.WeldMap),        _initialAction == FabAction.WeldMap);
            AddActionCellWithFormat(grid, 3, "Export iso index", new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                _cmbIsoFormat,  () => RunAction(FabAction.Isometrics),     _initialAction == FabAction.Isometrics);
            outer.Children.Add(grid);

            var grid2 = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < 5; i++)
                grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            AddActionCellWithFormat(grid2, 0, "Incremental rebuild", new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                null, () => RunIncremental(),                         false);
            AddActionCellWithFormat(grid2, 1, "BOM roll-up (XLSX)",  new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                null, () => RunAction(FabAction.BomRollup),           false);
            AddActionCellWithFormat(grid2, 2, "Export PCF",          new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                null, () => RunAction(FabAction.Pcf),                 _initialAction == FabAction.Pcf);
            AddActionCellWithFormat(grid2, 3, "Export MAJ",          new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                null, () => RunAction(FabAction.Maj),                 _initialAction == FabAction.Maj);
            AddActionCellWithFormat(grid2, 4, "Link → Doc Register", new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                null, () => LinkToDocumentRegister(),                 false);

            var btnClose = new Button
            {
                Content = "Close",
                Width = 80, Height = 36,
                Margin = new Thickness(12, 6, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            btnClose.Click += (_, __) => { DialogResult = false; Close(); };
            Grid.SetColumn(btnClose, 5);
            grid2.Children.Add(btnClose);

            outer.Children.Add(grid2);
            return outer;
        }

        private static void AddActionCellWithFormat(Grid grid, int col, string label, System.Windows.Media.Brush bg, ComboBox fmt, Action onRun, bool isInitial)
        {
            var stack = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 6, 0, 6, 0) };
            var btn = new Button
            {
                Content = label,
                Height = 30,
                Background = bg,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(isInitial ? 2 : 0),
                BorderBrush = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3),
            };
            btn.Click += (_, __) => onRun();
            stack.Children.Add(btn);
            if (fmt != null) stack.Children.Add(fmt);
            Grid.SetColumn(stack, col);
            grid.Children.Add(stack);
        }

        private ComboBox FormatCombo(string[] items)
        {
            var cb = new ComboBox
            {
                Height = 22,
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
            };
            foreach (var s in items)
                cb.Items.Add(new ComboBoxItem { Content = s, Foreground = Brushes.Black });
            cb.SelectedIndex = 0;
            return cb;
        }

        private void RunIncremental()
        {
            var keep = FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);
            if (keep.Count == 0) { TaskDialog.Show("STING v4 — Incremental", "Nothing in filter."); return; }
            string summary = FabricationActionRunner.RunGeneratePackageIncremental(_uidoc, keep);
            TaskDialog.Show("STING v4 — Incremental rebuild", summary);
            RefreshPreviewRows();
        }

        private void LinkToDocumentRegister()
        {
            try
            {
                var rec = FabricationUndoManager.Peek(_doc);
                if (rec == null || rec.SheetIds.Count == 0)
                {
                    TaskDialog.Show("STING v4 — Doc Register",
                        "No recent fabrication run recorded. Run Generate Package first.");
                    return;
                }
                int n = FabricationDocRegister.PushSheets(_doc, rec.SheetIds.Select(id => new ElementId(id)));
                TaskDialog.Show("STING v4 — Doc Register",
                    n == 0 ? "No new entries added (already in register)."
                           : $"Added {n} SP- sheets to the Document Register.");
            }
            catch (Exception ex) { StingLog.Warn($"LinkToDocumentRegister: {ex.Message}"); }
        }

        // ── Control factories ──────────────────────────────────

        private static TextBlock Hdr(string s) => new TextBlock
        {
            Text = s,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        private static RadioButton Radio(string label, bool isChecked) => new RadioButton
        {
            Content = label,
            IsChecked = isChecked,
            Foreground = Brushes.White,
            GroupName = "FabWorkspaceScope",
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static Button MiniButton(string label, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = label,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            b.Click += onClick;
            return b;
        }

        // ── Scope + preview refresh ────────────────────────────

        private void OnScopeChanged(object sender, RoutedEventArgs e)
        {
            FabricationOptions.ScopeSelection  = _rbSel?.IsChecked  == true;
            FabricationOptions.ScopeActiveView = _rbView?.IsChecked == true;
            FabricationOptions.ScopeProject    = _rbProj?.IsChecked == true;
            RefreshScope();
        }

        private void RefreshScope()
        {
            try
            {
                _scope = FabricationScope.Resolve(_doc, _uidoc);
                _txtScopeSummary.Text = _scope.TotalCount == 0
                    ? $"Scope: {_scope.ScopeLabel} — 0 MEP elements found."
                    : $"Scope: {_scope.ScopeLabel} — {_scope.TotalCount} MEP elements in scope.";
                _txtScopeSummary.Foreground = _scope.Fallback == FabScopeFallback.SelectionToActiveView && _scope.TotalCount > 0
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D))
                    : new SolidColorBrush(Color.FromRgb(200, 200, 200));

                // Category checkboxes
                _catPanel.Children.Clear();
                _catChecks.Clear();
                if (_scope.ByCategory.Count == 0)
                {
                    _catPanel.Children.Add(new TextBlock
                    {
                        Text = "(no MEP elements in scope — pick some in the model, switch to Active view, or Project)",
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(0, 4, 0, 4),
                    });
                }
                else
                {
                    foreach (var kv in _scope.ByCategory.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 2) };
                        var cb = new CheckBox
                        {
                            Content = kv.Key,
                            IsChecked = true,
                            Foreground = Brushes.White,
                            MinWidth = 220,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        cb.Checked   += (_, __) => RefreshPreviewRows();
                        cb.Unchecked += (_, __) => RefreshPreviewRows();
                        var count = new TextBlock
                        {
                            Text = kv.Value.Count.ToString(),
                            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        DockPanel.SetDock(cb, Dock.Left);
                        DockPanel.SetDock(count, Dock.Right);
                        row.Children.Add(cb);
                        row.Children.Add(count);
                        _catPanel.Children.Add(row);
                        _catChecks[kv.Key] = cb;
                    }
                }

                // Discipline rollup
                _txtDiscRollup.Text = _scope.ByDiscipline.Count == 0
                    ? "By discipline: —"
                    : "By discipline: " + string.Join("   •   ",
                        _scope.ByDiscipline.Select(kv => $"{kv.Key} = {kv.Value.Count}"));

                RefreshTitleBlockLine();
                RebuildFilterPills();
                RefreshPreviewRows();
            }
            catch (Exception ex) { StingLog.Warn($"FabricationWorkspaceDialog.RefreshScope: {ex.Message}"); }
        }

        private Dictionary<string, bool> CurrentCategoryMask()
        {
            var m = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _catChecks) m[kv.Key] = kv.Value.IsChecked == true;
            return m;
        }

        private void RefreshPreviewRows()
        {
            try
            {
                var filtered = _scope == null
                    ? new List<ElementId>()
                    : FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);

                // Package
                _pkgRows.Clear();
                foreach (var r in FabricationActionRunner.BuildPackageRows(_doc, filtered)) _pkgRows.Add(r);
                if (_pkgCount != null) _pkgCount.Text = $"{_pkgRows.Count} assemblies will be created  ";

                // Cut list
                _cutRows.Clear();
                foreach (var r in FabricationActionRunner.BuildCutListRows(_doc, filtered)) _cutRows.Add(r);
                if (_cutCount != null) _cutCount.Text = $"{_cutRows.Count} pipes in cut list  ";

                // Weld map
                _weldRows.Clear();
                foreach (var r in FabricationActionRunner.BuildWeldMapRows(_doc, filtered)) _weldRows.Add(r);
                if (_weldCount != null) _weldCount.Text = $"{_weldRows.Count} welds in map  ";

                // Isometrics (reads existing SP-... sheets — independent of scope)
                _isoRows.Clear();
                foreach (var r in FabricationActionRunner.BuildIsoRows(_doc)) _isoRows.Add(r);
                if (_isoCount != null) _isoCount.Text = $"{_isoRows.Count} existing SP- sheets  ";

                // PCF (per-system)
                _pcfRows.Clear();
                foreach (var r in FabricationActionRunner.BuildPcfRows(_doc, filtered)) _pcfRows.Add(r);
                if (_pcfCount != null) _pcfCount.Text = $"{_pcfRows.Count} piping systems for PCF  ";

                // MAJ (per-fabrication-part)
                _majRows.Clear();
                foreach (var r in FabricationActionRunner.BuildMajRows(_doc, filtered)) _majRows.Add(r);
                if (_majCount != null) _majCount.Text = $"{_majRows.Count} fab parts for MAJ  ";
            }
            catch (Exception ex) { StingLog.Warn($"RefreshPreviewRows: {ex.Message}"); }
        }

        private void RefreshTitleBlockLine()
        {
            try
            {
                var opts = FabricationOptions.ShopDrawing;
                if (opts == null)
                {
                    _txtTitleBlock.Text = "Title block: Auto (per-discipline STING_TB_ASSEMBLY_*)   •   View template: None";
                    return;
                }
                string tb = "Auto (per-discipline)";
                if (_doc != null && opts.TitleBlockSymbolId != null && opts.TitleBlockSymbolId != ElementId.InvalidElementId)
                {
                    if (_doc.GetElement(opts.TitleBlockSymbolId) is FamilySymbol fs)
                        tb = $"{fs.FamilyName} : {fs.Name}";
                }
                string vt = "None";
                if (_doc != null && opts.ViewTemplateId != null && opts.ViewTemplateId != ElementId.InvalidElementId)
                {
                    if (_doc.GetElement(opts.ViewTemplateId) is View v) vt = v.Name;
                }
                _txtTitleBlock.Text = $"Title block: {tb}   •   View template: {vt}";
            }
            catch (Exception ex) { StingLog.Warn($"RefreshTitleBlockLine: {ex.Message}"); }
        }

        private void ConfigureTitleBlock()
        {
            try
            {
                var dlg = new ShopDrawingOptionsDialog(_doc) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    FabricationOptions.ShopDrawing = dlg.Result;
                    RefreshTitleBlockLine();
                    StingDockPanel.LastInstance?.UpdateFabShopDrawingStatus(_doc, dlg.Result);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ConfigureTitleBlock: {ex.Message}"); }
        }

        // ── Action dispatch ────────────────────────────────────

        private void RunAction(FabAction action)
        {
            try
            {
                string summary;
                switch (action)
                {
                    case FabAction.GeneratePackage:
                    {
                        var keepIds = _pkgRows.Where(r => r.Include).Any()
                            ? FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow)
                            : new List<ElementId>();
                        if (keepIds.Count == 0)
                        {
                            TaskDialog.Show("STING v4 — Fabrication", "Nothing ticked to package.");
                            return;
                        }
                        summary = FabricationActionRunner.RunGeneratePackage(_uidoc, keepIds);

                        // Phase 5 — auto-create / refresh the spool schedule (#13).
                        try
                        {
                            var sch = FabricationSpoolSchedule.CreateOrRefresh(_doc);
                            if (sch?.Ok == true) summary += "\n" + sch.Message;
                            else if (sch != null && !string.IsNullOrWhiteSpace(sch.Message))
                                summary += "\nSpool schedule skipped: " + sch.Message;
                        }
                        catch (Exception ex) { StingLog.Warn($"SpoolSchedule: {ex.Message}"); }
                        break;
                    }
                    case FabAction.CutList:
                    {
                        if (!_cutRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Cut list", "No rows ticked."); return; }
                        var fmt = SelectedFormat(_cmbCutFormat);
                        if (fmt == FabFormat.Xlsx)
                        {
                            string p = FabricationXlsxExporter.ExportCutListXlsx(_doc, _cutRows);
                            summary = string.IsNullOrEmpty(p) ? "XLSX export failed." : $"Cut list XLSX:\n{p}";
                        }
                        else summary = FabricationActionRunner.RunCutList(_uidoc, _cutRows);
                        break;
                    }
                    case FabAction.WeldMap:
                    {
                        if (!_weldRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Weld map", "No rows ticked."); return; }
                        var fmt = SelectedFormat(_cmbWeldFormat);
                        if (fmt == FabFormat.Xlsx)
                        {
                            string p = FabricationXlsxExporter.ExportWeldMapXlsx(_doc, _weldRows);
                            summary = string.IsNullOrEmpty(p) ? "XLSX export failed." : $"Weld map XLSX:\n{p}";
                        }
                        else summary = FabricationActionRunner.RunWeldMap(_uidoc, _weldRows);
                        break;
                    }
                    case FabAction.Isometrics:
                    {
                        if (!_isoRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Isometrics", "No sheets ticked."); return; }
                        var fmt = SelectedFormat(_cmbIsoFormat);
                        if (fmt == FabFormat.Pdf)
                        {
                            string pdfPath = FabricationPdfExporter.ExportSheetsToPdf(_doc, _isoRows);
                            summary = string.IsNullOrEmpty(pdfPath)
                                ? "PDF export failed (see log)."
                                : $"Exported {_isoRows.Count(r => r.Include)} sheets to:\n{pdfPath}";
                        }
                        else
                        {
                            summary = FabricationActionRunner.RunIsometrics(_uidoc, _isoRows);
                        }
                        break;
                    }
                    case FabAction.Pcf:
                        RunPcfExport();
                        summary = "PCF export complete (see log for per-system detail).";
                        break;
                    case FabAction.Maj:
                        RunMajExport();
                        summary = "MAJ export — see dialog pointer.";
                        break;
                    case FabAction.BomRollup:
                    {
                        var keep = FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);
                        summary = FabricationActionRunner.RunBomRollup(_uidoc, keep);
                        break;
                    }
                    default: return;
                }
                TaskDialog.Show("STING v4 — " + action.ToString(), summary);
                RefreshPreviewRows();
            }
            catch (Exception ex)
            {
                StingLog.Error($"Fabrication action {action} failed", ex);
                TaskDialog.Show("STING v4 — Fabrication", $"Action failed:\n{ex.Message}");
            }
        }

        // ── Preset bar (#1) ────────────────────────────────────

        private UIElement BuildPresetRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(Hdr("PRESET"));
            _cmbPreset = new ComboBox
            {
                Width = 220, Height = 22,
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
            };
            RefreshPresetCombo();
            row.Children.Add(_cmbPreset);
            row.Children.Add(MiniButton("Load",   (_, __) => LoadSelectedPreset()));
            row.Children.Add(MiniButton("Save…",  (_, __) => SaveCurrentAsPreset()));
            row.Children.Add(MiniButton("Delete", (_, __) => DeleteSelectedPreset()));
            return row;
        }

        private void RefreshPresetCombo()
        {
            if (_cmbPreset == null) return;
            _cmbPreset.Items.Clear();
            foreach (var n in FabricationPresetStore.NamedPresetNames(_doc))
                _cmbPreset.Items.Add(new ComboBoxItem { Content = n, Foreground = Brushes.Black });
            if (_cmbPreset.Items.Count > 0) _cmbPreset.SelectedIndex = 0;
        }

        private string SelectedPresetName =>
            (_cmbPreset?.SelectedItem as ComboBoxItem)?.Content?.ToString();

        private void LoadSelectedPreset()
        {
            var name = SelectedPresetName;
            if (string.IsNullOrEmpty(name)) return;
            var preset = FabricationPresetStore.Find(_doc, name);
            if (preset == null) return;
            var mask = FabricationPresetStore.Apply(_doc, preset);
            // Mirror scope radios
            if (_rbSel  != null) _rbSel.IsChecked  = FabricationOptions.ScopeSelection;
            if (_rbView != null) _rbView.IsChecked = FabricationOptions.ScopeActiveView;
            if (_rbProj != null) _rbProj.IsChecked = FabricationOptions.ScopeProject;
            RefreshScope();
            // Re-tick categories per saved mask
            foreach (var kv in mask)
                if (_catChecks.TryGetValue(kv.Key, out var cb)) cb.IsChecked = kv.Value;
            RefreshPreviewRows();
        }

        private void SaveCurrentAsPreset()
        {
            // Minimal prompt — reuse the name from the combo text or suggest a default.
            string defaultName = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " preset";
            var dlg = new PresetNameDialog(defaultName) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var name = dlg.ResultName;
            if (string.IsNullOrWhiteSpace(name)) return;
            var p = FabricationPresetStore.Capture(name, _doc, CurrentCategoryMask(), _initialAction);
            FabricationPresetStore.Save(_doc, p);
            RefreshPresetCombo();
        }

        private void DeleteSelectedPreset()
        {
            var name = SelectedPresetName;
            if (string.IsNullOrEmpty(name)) return;
            FabricationPresetStore.Delete(_doc, name);
            RefreshPresetCombo();
        }

        // ── Filter pills (#2) ──────────────────────────────────

        private void RebuildFilterPills()
        {
            _systemPills.Children.Clear();
            _levelPills.Children.Clear();
            if (_scope == null) return;

            _systemPills.Children.Add(new TextBlock
            {
                Text = "System:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
                Margin = new Thickness(0, 2, 8, 0),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
            });
            foreach (var s in _scope.DistinctSystems) _systemPills.Children.Add(FilterPill(s, _systemAllow));

            _levelPills.Children.Add(new TextBlock
            {
                Text = "Level:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
                Margin = new Thickness(0, 2, 8, 0),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
            });
            foreach (var l in _scope.DistinctLevels) _levelPills.Children.Add(FilterPill(l, _levelAllow));
        }

        private UIElement FilterPill(string label, HashSet<string> bucket)
        {
            var tb = new ToggleButton
            {
                Content = string.IsNullOrEmpty(label) ? "(blank)" : label,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)),
                BorderThickness = new Thickness(1),
                IsChecked = bucket.Contains(label),
            };
            tb.Checked   += (_, __) => { bucket.Add(label); RefreshPreviewRows(); };
            tb.Unchecked += (_, __) => { bucket.Remove(label); RefreshPreviewRows(); };
            return tb;
        }

        // ── Package enhancements ──────────────────────────────

        private void ReBuildPackageWithGrouper()
        {
            if (_scope == null) return;
            var keep = FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);
            var groups = FabricationGrouper.Pack(_doc, keep);
            _pkgRows.Clear();
            foreach (var g in groups)
            {
                _pkgRows.Add(new PackageGroupRow
                {
                    Discipline          = g.Discipline,
                    System              = g.System,
                    Level               = g.Level,
                    ElementCount        = g.ElementIds.Count,
                    AssemblyNamePreview = g.AssemblyNamePreview,
                });
            }
            _pkgCount.Text = $"{_pkgRows.Count} groups (smart-packed by STING_FAB_RULES.json)  ";
        }

        private void RunClashPreflight()
        {
            try
            {
                var keep = FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);
                if (keep.Count == 0) { _txtClashSummary.Text = "No elements in filter."; return; }
                var hits = FabricationClashChecker.ScanAabb(_doc, keep);
                if (hits.Count == 0)
                {
                    _txtClashSummary.Text = $"Clash pre-flight: {keep.Count} elements scanned — 0 clashes.";
                    _txtClashSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }
                else
                {
                    _txtClashSummary.Text = $"Clash pre-flight: {keep.Count} scanned, {hits.Count} clashes (worst overlap {hits.First().OverlapMm:F0} mm).";
                    _txtClashSummary.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x9E, 0x4F));
                }
            }
            catch (Exception ex) { StingLog.Warn($"RunClashPreflight: {ex.Message}"); }
        }

        private void UndoLastRun()
        {
            int n = FabricationUndoManager.UndoLast(_uidoc);
            TaskDialog.Show("STING v4 — Undo fabrication",
                n == 0 ? "Nothing to undo (no recorded last run)."
                       : $"Undid last run — removed {n} elements.");
            RefreshPreviewRows();
        }

        // ── Action-row format combos (wire into BuildActionRow) ─

        private static FabFormat SelectedFormat(ComboBox cb)
        {
            var s = (cb?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (s.IndexOf("PDF",  StringComparison.OrdinalIgnoreCase) >= 0) return FabFormat.Pdf;
            if (s.IndexOf("XLSX", StringComparison.OrdinalIgnoreCase) >= 0) return FabFormat.Xlsx;
            if (s.IndexOf("Revit",StringComparison.OrdinalIgnoreCase) >= 0) return FabFormat.Revit;
            return FabFormat.Csv;
        }

        private void RunPcfExport()
        {
            // Run PcfExporter directly per ticked system — avoids
            // needing ExternalCommandData from inside the modal.
            try
            {
                string outDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_doc.PathName ?? System.IO.Path.GetTempPath()) ?? System.IO.Path.GetTempPath(),
                    "_BIM_COORD", "pcf");
                System.IO.Directory.CreateDirectory(outDir);
                var keep = FabricationScope.FilterFull(_scope, CurrentCategoryMask(), _systemAllow, _levelAllow);
                int written = 0;
                foreach (var row in _pcfRows.Where(r => r.Include))
                {
                    var ids = keep
                        .Where(id =>
                        {
                            try
                            {
                                if (!(_doc.GetElement(id) is Autodesk.Revit.DB.Plumbing.Pipe p)) return false;
                                return string.Equals(p.MEPSystem?.Name ?? "UNKNOWN", row.System, StringComparison.OrdinalIgnoreCase);
                            }
                            catch { return false; }
                        }).ToList();
                    if (ids.Count == 0) continue;
                    try
                    {
                        StingTools.Core.Fabrication.PcfExporter.Export(_doc, ids, outDir, row.System);
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"PCF export {row.System}: {ex.Message}"); }
                }
                TaskDialog.Show("STING v4 — PCF", written == 0
                    ? "No PCF files written."
                    : $"Wrote {written} PCF system file(s) to:\n{outDir}");
            }
            catch (Exception ex) { StingLog.Warn($"RunPcfExport: {ex.Message}"); }
        }

        private void RunMajExport()
        {
            // MAJ API is Revit-version-sensitive (see ExportMajCommand).
            // Point the user at Revit's built-in export rather than
            // re-implement inside the modal.
            TaskDialog.Show("STING v4 — MAJ",
                "MAJ export uses Revit's built-in Fabrication → Export to MAJ menu.\n\n" +
                "The workspace preview shows the " + _majRows.Count + " fab-parts that would\n" +
                "be included. Close this dialog and run Revit's menu command.");
        }

        // Persist the current workspace state as "__last_session__" on close (#5)
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                FabricationPresetStore.SaveLastSession(_doc, CurrentCategoryMask(), _initialAction);
            }
            catch (Exception ex) { StingLog.Warn($"SaveLastSession: {ex.Message}"); }
            base.OnClosed(e);
        }
    }

    // Tiny modal for capturing a preset name (kept in same file for cohesion).
    internal class PresetNameDialog : Window
    {
        public string ResultName { get; private set; } = "";
        public PresetNameDialog(string initial)
        {
            Title = "STING v4 — Save preset";
            Width = 420; Height = 150;
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var g = new Grid { Margin = new Thickness(14) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            g.Children.Add(new TextBlock { Text = "Preset name:", Foreground = Brushes.White });
            var tb = new TextBox
            {
                Text = initial,
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(6),
            };
            Grid.SetRow(tb, 1);
            g.Children.Add(tb);

            var brow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 80, Height = 26, Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            var ca = new Button { Content = "Cancel", Width = 80, Height = 26, Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            ok.Click += (_, __) => { ResultName = tb.Text?.Trim() ?? ""; DialogResult = true; Close(); };
            ca.Click += (_, __) => { DialogResult = false; Close(); };
            brow.Children.Add(ok); brow.Children.Add(ca);
            Grid.SetRow(brow, 2);
            g.Children.Add(brow);

            Content = g;
        }
    }
}
