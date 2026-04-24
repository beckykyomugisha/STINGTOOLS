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
    public enum FabAction { GeneratePackage, CutList, WeldMap, Isometrics }

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

        private DataGrid _cutGrid, _weldGrid, _pkgGrid, _isoGrid;
        private TextBlock _cutCount, _weldCount, _pkgCount, _isoCount;

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
                _                         => 0,
            };
            if (_previewTabs.Items.Count > idx) _previewTabs.SelectedIndex = idx;
        }

        // ── Layout ──────────────────────────────────────────────

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 scope radios
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1 scope summary
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) }); // 2 category list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3 discipline rollup
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4 preview tabs
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 title block
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6 actions

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
            Grid.SetRow(scopeRow, 0);
            root.Children.Add(scopeRow);

            // ─ Scope summary
            _txtScopeSummary = new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 6),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(_txtScopeSummary, 1);
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
            Grid.SetRow(catBox, 2);
            root.Children.Add(catBox);

            // ─ Discipline roll-up
            _txtDiscRollup = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC1, 0x7D)),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(_txtDiscRollup, 3);
            root.Children.Add(_txtDiscRollup);

            // ─ Preview tabs
            _previewTabs = BuildPreviewTabs();
            Grid.SetRow(_previewTabs, 4);
            root.Children.Add(_previewTabs);

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
            Grid.SetRow(tbRow, 5);
            root.Children.Add(tbRow);

            // ─ Actions
            var actionRow = BuildActionRow();
            Grid.SetRow(actionRow, 6);
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
            return tabs;
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
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);

            _pkgGrid = MakeGrid();
            _pkgGrid.ItemsSource = _pkgRows;
            _pkgGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Inc",   Binding = new Binding("Include") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Discipline", Binding = new Binding("Discipline"),          Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "System",     Binding = new Binding("System"),              Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Level",      Binding = new Binding("Level"),               Width = 90,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Elements",   Binding = new Binding("ElementCount"),        Width = 80,  IsReadOnly = true });
            _pkgGrid.Columns.Add(new DataGridTextColumn { Header = "Assembly name (preview)", Binding = new Binding("AssemblyNamePreview"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
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
            var grid = new Grid();
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            AddActionCell(grid, 0, "Generate package", new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), () => RunAction(FabAction.GeneratePackage), _initialAction == FabAction.GeneratePackage);
            AddActionCell(grid, 1, "Export cut list",  new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)), () => RunAction(FabAction.CutList),        _initialAction == FabAction.CutList);
            AddActionCell(grid, 2, "Export weld map",  new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)), () => RunAction(FabAction.WeldMap),        _initialAction == FabAction.WeldMap);
            AddActionCell(grid, 3, "Export iso index", new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x58)), () => RunAction(FabAction.Isometrics),     _initialAction == FabAction.Isometrics);

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
            Grid.SetColumn(btnClose, 4);
            grid.Children.Add(btnClose);

            return grid;
        }

        private static void AddActionCell(Grid grid, int col, string label, System.Windows.Media.Brush bg, Action onRun, bool isInitial)
        {
            var btn = new Button
            {
                Content = label,
                Height = 36,
                Margin = new Thickness(col == 0 ? 0 : 6, 6, 6, 0),
                Background = bg,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(isInitial ? 2 : 0),
                BorderBrush = Brushes.White,
                FontWeight = FontWeights.SemiBold,
            };
            btn.Click += (_, __) => onRun();
            Grid.SetColumn(btn, col);
            grid.Children.Add(btn);
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
                    : FabricationScope.FilterByRulesAndCategoryMask(_scope, CurrentCategoryMask());

                // Package
                _pkgRows.Clear();
                foreach (var r in FabricationActionRunner.BuildPackageRows(_doc, filtered)) _pkgRows.Add(r);
                _pkgCount.Text = $"{_pkgRows.Count} assemblies will be created  ";

                // Cut list
                _cutRows.Clear();
                foreach (var r in FabricationActionRunner.BuildCutListRows(_doc, filtered)) _cutRows.Add(r);
                _cutCount.Text = $"{_cutRows.Count} pipes in cut list  ";

                // Weld map
                _weldRows.Clear();
                foreach (var r in FabricationActionRunner.BuildWeldMapRows(_doc, filtered)) _weldRows.Add(r);
                _weldCount.Text = $"{_weldRows.Count} welds in map  ";

                // Isometrics (reads existing SP-... sheets — independent of scope)
                _isoRows.Clear();
                foreach (var r in FabricationActionRunner.BuildIsoRows(_doc)) _isoRows.Add(r);
                _isoCount.Text = $"{_isoRows.Count} existing SP- sheets  ";
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
                            ? FabricationScope.FilterByRulesAndCategoryMask(_scope, CurrentCategoryMask())
                            : new List<ElementId>();
                        if (keepIds.Count == 0)
                        {
                            TaskDialog.Show("STING v4 — Fabrication", "Nothing ticked to package.");
                            return;
                        }
                        summary = FabricationActionRunner.RunGeneratePackage(_uidoc, keepIds);
                        break;
                    }
                    case FabAction.CutList:
                        if (!_cutRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Cut list", "No rows ticked."); return; }
                        summary = FabricationActionRunner.RunCutList(_uidoc, _cutRows);
                        break;
                    case FabAction.WeldMap:
                        if (!_weldRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Weld map", "No rows ticked."); return; }
                        summary = FabricationActionRunner.RunWeldMap(_uidoc, _weldRows);
                        break;
                    case FabAction.Isometrics:
                        if (!_isoRows.Any(r => r.Include))
                        { TaskDialog.Show("STING v4 — Isometrics", "No sheets ticked."); return; }
                        summary = FabricationActionRunner.RunIsometrics(_uidoc, _isoRows);
                        break;
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
    }
}
