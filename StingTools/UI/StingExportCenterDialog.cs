using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Docs;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Panel = System.Windows.Controls.Panel;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SystemColors = System.Windows.SystemColors;
using Newtonsoft.Json;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    // ════════════════════════════════════════════════════════════════════════════
    //  StingExportCenterDialog — the unified STING Export Centre.
    //
    //  A resizable WPF dialog that consolidates BatchPrintSheets, PrintManager,
    //  AutomationEngine PDF/DWG, IFC export, ExLink batch export, and the
    //  Drawing Production Config dialog into one publishing surface.
    //
    //  Layout:
    //    HEADER : mode toggle (Simple / BIM), profile picker, share/import
    //    BODY   : 3 columns — SELECT │ FORMAT │ OUTPUT
    //    FOOTER : status summary, progress bar, EXPORT button
    //
    //  See CLAUDE.md "STING Export Centre — Complete Implementation Prompt"
    //  for the full UX brief that this dialog implements.
    // ════════════════════════════════════════════════════════════════════════════

    public class StingExportCenterDialog : Window
    {
        private readonly Document _doc;
        private readonly Autodesk.Revit.UI.UIDocument _uiDoc;

        private ExportCenterState _state;
        private ExportProfile _profile;

        private ComboBox _profileCombo;
        private DataGrid _selectGrid;
        private TextBox _searchBox;
        private ComboBox _setCombo;
        private RadioButton _kindSheets, _kindViews;

        private CheckBox _fmtPdf, _fmtDwg, _fmtIfc, _fmtNwc, _fmtDgn, _fmtDwf, _fmtImage, _fmtXml;
        private StackPanel _formatOptionsHost;

        private TextBox _folderBox;
        private TextBox _namingBox;
        private TextBlock _namingPreview;
        private TextBlock _statusLine;
        private TextBlock _progressLabel;
        private ProgressBar _progressBar;
        private Button _exportButton;

        private readonly ObservableCollection<SheetRow> _rows = new();
        private bool _exporting;
        private bool _cancelRequested;

        // ── SELECT-panel grouping / bulk-selection (Part 3) ──────────────────────
        private enum GroupMode { None, Discipline, Level, Series }
        private GroupMode _groupMode = GroupMode.None;
        private ComboBox _groupCombo;
        // Realized group-header tri-state checkboxes, tracked so they can be
        // refreshed when row selection changes. Each carries its CollectionViewGroup
        // as DataContext.
        private readonly List<CheckBox> _groupHeaderChecks = new();
        private bool _suppressHeaderRefresh;   // re-entrancy guard while we set header state
        private bool _headerRefreshQueued;      // debounce dispatcher refreshes

        // ── Entry point ─────────────────────────────────────────────────────────

        public static void Show(Autodesk.Revit.UI.UIDocument uiDoc)
        {
            try
            {
                var dlg = new StingExportCenterDialog(uiDoc);
                var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(dlg).Owner = owner;
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Error("StingExportCenterDialog.Show failed", ex);
                Autodesk.Revit.UI.TaskDialog.Show("Export Centre", "Failed to open: " + ex.Message);
            }
        }

        public StingExportCenterDialog(Autodesk.Revit.UI.UIDocument uiDoc)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = uiDoc.Document;

            _state = ExportCenterEngine.LoadState();
            _profile = ResolveStartingProfile();

            InitWindowChrome();
            BuildLayout();
            ApplyProfileToUi();
            RefreshSelectionGrid();
            UpdateStatusLine();
        }

        private ExportProfile ResolveStartingProfile()
        {
            var p = _state.Profiles.FirstOrDefault(x => x.Name == _state.LastProfile);
            return p ?? _state.Profiles.FirstOrDefault() ?? new ExportProfile { Name = "New profile" };
        }

        private void InitWindowChrome()
        {
            Title = "STING Export Centre";
            Width = 1280; Height = 820;
            MinWidth = 1024; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)new BrushConverter().ConvertFromString("#F5F6F8");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            ResizeMode = ResizeMode.CanResize;
        }

        // ── Layout ──────────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // footer

            root.Children.Add(BuildHeader());
            var body = BuildBody();
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            var footer = BuildFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // ── Header ──────────────────────────────────────────────────────────────

        private FrameworkElement BuildHeader()
        {
            var bar = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#1F2A3A"),
                Padding = new Thickness(16, 10, 16, 10),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "STING Export Centre",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(title);

            // Profile picker
            var profilePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            profilePanel.Children.Add(new TextBlock
            {
                Text = "Profile:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            _profileCombo = new ComboBox { Width = 280, Padding = new Thickness(6, 3, 6, 3) };
            foreach (var p in _state.Profiles)
                _profileCombo.Items.Add(p.Name + (p.BuiltIn ? "  (built-in)" : ""));
            _profileCombo.SelectedIndex = Math.Max(0, _state.Profiles.IndexOf(_profile));
            _profileCombo.SelectionChanged += (_, __) =>
            {
                int i = _profileCombo.SelectedIndex;
                if (i >= 0 && i < _state.Profiles.Count)
                {
                    _profile = _state.Profiles[i];
                    ApplyProfileToUi();
                    UpdateStatusLine();
                }
            };
            profilePanel.Children.Add(_profileCombo);

            profilePanel.Children.Add(SmallButton("★ Save",  OnSaveProfileClick));
            profilePanel.Children.Add(SmallButton("Save As…", OnSaveAsProfileClick));
            profilePanel.Children.Add(SmallButton("Import…", OnImportProfileClick));
            profilePanel.Children.Add(SmallButton("Share",   OnShareProfileClick));

            Grid.SetColumn(profilePanel, 1);
            grid.Children.Add(profilePanel);

            // Mode toggle
            var modeBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var simpleBtn = ModePill("Simple",  ExportCenterMode.Simple);
            var bimBtn    = ModePill("BIM 19650", ExportCenterMode.BIM);
            modeBox.Children.Add(simpleBtn);
            modeBox.Children.Add(bimBtn);
            Grid.SetColumn(modeBox, 2);
            grid.Children.Add(modeBox);

            // Close
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 32, Height = 28,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 14,
                Cursor = Cursors.Hand,
            };
            closeBtn.Click += (_, __) => Close();
            Grid.SetColumn(closeBtn, 3);
            grid.Children.Add(closeBtn);

            bar.Child = grid;
            return bar;
        }

        private Button ModePill(string label, ExportCenterMode mode)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(2, 0, 2, 0),
                Background = (_profile?.Mode == mode)
                    ? (Brush)new BrushConverter().ConvertFromString("#3577F0")
                    : Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
            };
            btn.Click += (_, __) =>
            {
                _profile.Mode = mode;
                _state.Mode = mode;
                ApplyProfileToUi();
                UpdateStatusLine();
            };
            return btn;
        }

        private Button SmallButton(string label, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = label,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(4, 0, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFromString("#33FFFFFF"),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            b.Click += onClick;
            return b;
        }

        // ── Body (three columns + splitters) ────────────────────────────────────

        private FrameworkElement BuildBody()
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 320 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star), MinWidth = 280 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 280 });

            var sel = WrapPanel("1.  SELECT", BuildSelectPanel());
            Grid.SetColumn(sel, 0);
            grid.Children.Add(sel);

            var s1 = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)new BrushConverter().ConvertFromString("#E2E5EA"),
            };
            Grid.SetColumn(s1, 1);
            grid.Children.Add(s1);

            var fmt = WrapPanel("2.  FORMAT", BuildFormatPanel());
            Grid.SetColumn(fmt, 2);
            grid.Children.Add(fmt);

            var s2 = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)new BrushConverter().ConvertFromString("#E2E5EA"),
            };
            Grid.SetColumn(s2, 3);
            grid.Children.Add(s2);

            var outp = WrapPanel("3.  OUTPUT", BuildOutputPanel());
            Grid.SetColumn(outp, 4);
            grid.Children.Add(outp);

            return grid;
        }

        private FrameworkElement WrapPanel(string title, FrameworkElement content)
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D9DDE3"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2),
            };
            var dock = new DockPanel();
            var header = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F0F2F5"),
                Padding = new Thickness(10, 6, 10, 6),
            };
            header.Child = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#1F2A3A"),
            };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            content.Margin = new Thickness(8);
            dock.Children.Add(content);
            card.Child = dock;
            return card;
        }

        // ── SELECT panel ────────────────────────────────────────────────────────

        private FrameworkElement BuildSelectPanel()
        {
            var dock = new DockPanel { LastChildFill = true };

            // Top: kind toggle + saved-set picker + search
            var topBar = new Grid();
            topBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            topBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            topBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _kindSheets = new RadioButton { Content = "Sheets", IsChecked = true, GroupName = "Kind", Margin = new Thickness(0, 0, 12, 0) };
            _kindViews  = new RadioButton { Content = "Views",  GroupName = "Kind" };
            _kindSheets.Checked += (_, __) => RefreshSelectionGrid();
            _kindViews.Checked  += (_, __) => RefreshSelectionGrid();
            kindRow.Children.Add(_kindSheets);
            kindRow.Children.Add(_kindViews);
            Grid.SetRow(kindRow, 0);
            topBar.Children.Add(kindRow);

            var setRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            setRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            setRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            setRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            setRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            setRow.Children.Add(new TextBlock { Text = "Set:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            _setCombo = new ComboBox();
            foreach (var s in _state.SavedSets) _setCombo.Items.Add(s.Name + (s.BuiltIn ? "  (built-in)" : ""));
            _setCombo.SelectedIndex = 0;
            _setCombo.SelectionChanged += (_, __) => ApplySavedSetSelection();
            Grid.SetColumn(_setCombo, 1);
            setRow.Children.Add(_setCombo);

            var saveSetBtn = new Button { Content = "+ Save", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            saveSetBtn.Click += OnSaveSetClick;
            Grid.SetColumn(saveSetBtn, 2);
            setRow.Children.Add(saveSetBtn);

            var manageBtn = new Button { Content = "✎", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            manageBtn.Click += OnManageSetsClick;
            Grid.SetColumn(manageBtn, 3);
            setRow.Children.Add(manageBtn);

            Grid.SetRow(setRow, 1);
            topBar.Children.Add(setRow);

            _searchBox = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = "Search sheets, titles, disciplines…",
            };
            _searchBox.TextChanged += (_, __) => ApplySearchFilter();
            Grid.SetRow(_searchBox, 2);
            topBar.Children.Add(_searchBox);

            DockPanel.SetDock(topBar, Dock.Top);
            dock.Children.Add(topBar);

            // Filter chips
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            string[] chipLabels = { "All", "Issued", "Unissued", "Current Revision", "Opened Only" };
            foreach (var c in chipLabels) chips.Children.Add(MakeChip(c));
            DockPanel.SetDock(chips, Dock.Top);
            dock.Children.Add(chips);

            // Bulk-selection + grouping controls (Part 3)
            var selControls = BuildSelectionControls();
            DockPanel.SetDock(selControls, Dock.Top);
            dock.Children.Add(selControls);

            // Grid
            _selectGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = (Brush)new BrushConverter().ConvertFromString("#F8F9FB"),
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,
                ItemsSource = _rows,
            };

            _selectGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "",
                Binding = new Binding(nameof(SheetRow.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 32,
            });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "Sheet No.", Binding = new Binding(nameof(SheetRow.Number)),     Width = 110, IsReadOnly = true });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "Title",     Binding = new Binding(nameof(SheetRow.Title)),      Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "Rev",       Binding = new Binding(nameof(SheetRow.Revision)),   Width = 50,  IsReadOnly = true });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "Disc",      Binding = new Binding(nameof(SheetRow.Discipline)), Width = 50,  IsReadOnly = true });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "Size",      Binding = new Binding(nameof(SheetRow.PaperSize)),  Width = 60,  IsReadOnly = true });
            _selectGrid.Columns.Add(new DataGridTextColumn { Header = "CDE",       Binding = new Binding(nameof(SheetRow.CdeStatus)),  Width = 60,  IsReadOnly = true });

            // Collapsible grouping with a tri-state group-header checkbox (Part 3).
            _selectGrid.GroupStyle.Add(BuildGroupStyle());

            dock.Children.Add(_selectGrid);
            return dock;
        }

        private Border MakeChip(string label)
        {
            var b = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#EEF1F5"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
            };
            b.Child = new TextBlock { Text = label, FontSize = 11 };
            b.MouseLeftButtonUp += (_, __) => { _searchBox.Text = label == "All" ? "" : label; ApplySearchFilter(); };
            return b;
        }

        // ── SELECT bulk-selection + grouping (Part 3) ────────────────────────────

        private FrameworkElement BuildSelectionControls()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

            var selectAll = new Button { Content = "Select All", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            selectAll.Click += (_, __) => SetVisibleChecked(true);
            row.Children.Add(selectAll);

            var deselectAll = new Button { Content = "Deselect All", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            deselectAll.Click += (_, __) => SetVisibleChecked(false);
            row.Children.Add(deselectAll);

            var byDisc = new Button { Content = "By discipline ▾", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 12, 0) };
            byDisc.Click += OnByDisciplineClick;
            row.Children.Add(byDisc);

            row.Children.Add(new TextBlock { Text = "Group by:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _groupCombo = new ComboBox { Width = 130 };
            _groupCombo.Items.Add("None");
            _groupCombo.Items.Add("Discipline");
            _groupCombo.Items.Add("Level");
            _groupCombo.Items.Add("Sheet series");
            _groupCombo.SelectedIndex = 0;
            _groupCombo.SelectionChanged += (_, __) => ApplyGrouping();
            row.Children.Add(_groupCombo);

            return row;
        }

        /// <summary>Rows currently visible under the active search/chip filter,
        /// in the collection-view's order. A null filter ⇒ every row.</summary>
        private IEnumerable<SheetRow> VisibleRows()
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            if (view == null) return _rows.ToList();
            return view.Cast<SheetRow>().ToList();
        }

        /// <summary>Check/uncheck every CURRENTLY-VISIBLE row; filtered-out rows
        /// keep their state (selection survives filter changes).</summary>
        private void SetVisibleChecked(bool value)
        {
            foreach (var r in VisibleRows()) r.IsChecked = value;
            RefreshGroupHeaders();
            UpdateStatusLine();
        }

        private void OnByDisciplineClick(object sender, RoutedEventArgs e)
        {
            // Build a fresh checkable menu of the DISTINCT disciplines among the
            // currently-visible rows. Ticking a discipline checks all its visible
            // rows; unticking clears them; disciplines union independently.
            var visible = VisibleRows().ToList();
            if (visible.Count == 0) return;

            var menu = new ContextMenu();
            var discGroups = visible
                .GroupBy(DisciplineBucket)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var g in discGroups)
            {
                string disc = g.Key;
                int total = g.Count();
                int checkedCount = g.Count(r => r.IsChecked);
                var mi = new MenuItem
                {
                    Header = $"{disc}  ({checkedCount}/{total})",
                    IsCheckable = true,
                    IsChecked = checkedCount == total,   // fully selected ⇒ ticked
                    StaysOpenOnClick = true,
                };
                mi.Click += (_, __) =>
                {
                    bool want = mi.IsChecked;
                    foreach (var r in visible.Where(x => DisciplineBucket(x) == disc))
                        r.IsChecked = want;
                    RefreshGroupHeaders();
                    UpdateStatusLine();
                };
                menu.Items.Add(mi);
            }

            if (sender is UIElement ue)
            {
                menu.PlacementTarget = ue;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void ApplyGrouping()
        {
            _groupMode = _groupCombo?.SelectedIndex switch
            {
                1 => GroupMode.Discipline,
                2 => GroupMode.Level,
                3 => GroupMode.Series,
                _ => GroupMode.None,
            };

            var view = CollectionViewSource.GetDefaultView(_rows);
            if (view == null) return;
            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();
                _groupHeaderChecks.Clear();
                if (_groupMode != GroupMode.None)
                    view.GroupDescriptions.Add(new PropertyGroupDescription(null, new GroupKeyConverter(_groupMode)));
            }
        }

        // Group key for a single row under the active grouping mode.
        private static string GroupKeyFor(SheetRow r, GroupMode mode) => mode switch
        {
            GroupMode.Discipline => DisciplineBucket(r),
            GroupMode.Level      => DeriveLevel(r),
            GroupMode.Series     => DeriveSeries(r),
            _                    => "",
        };

        private static string DisciplineBucket(SheetRow r)
            => string.IsNullOrWhiteSpace(r?.Discipline) ? "(Other)" : r.Discipline.Trim().ToUpperInvariant();

        // Level bucket derived from the sheet number/title (the export engine's
        // GetLevelFromSheet is private + the engine is frozen this task, so we
        // derive a display-only token here using the same ISO level vocabulary).
        private static string DeriveLevel(SheetRow r)
        {
            string s = ((r?.Number ?? "") + " " + (r?.Title ?? "")).ToUpperInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"\bROOF\b|\bRF\b")) return "RF";
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"\bGROUND\b|\bGF\b")) return "GF";
            var mB = System.Text.RegularExpressions.Regex.Match(s, @"\b(?:BASEMENT|B)\s*0*(\d)\b");
            if (mB.Success) return "B" + mB.Groups[1].Value;
            var mL = System.Text.RegularExpressions.Regex.Match(s, @"\b(?:LEVEL|FLOOR|L)\s*0*(\d{1,2})\b");
            if (mL.Success) return $"L{int.Parse(mL.Groups[1].Value):D2}";
            return "(No level)";
        }

        // Series bucket: discipline prefix + leading hundreds digit, e.g. A-101 → "A-1xx".
        private static string DeriveSeries(SheetRow r)
        {
            string disc = DisciplineBucket(r);
            string num = r?.Number ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(num, @"(\d)\d*\b");
            if (m.Success) return $"{disc}-{m.Groups[1].Value}xx";
            return disc;
        }

        // ── Group-style (Expander + tri-state header checkbox) ───────────────────

        private GroupStyle BuildGroupStyle()
        {
            // Header: [tri-state checkbox] Name (count)
            var headerTpl = new DataTemplate();
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var cb = new FrameworkElementFactory(typeof(CheckBox));
            cb.SetValue(CheckBox.IsThreeStateProperty, true);
            cb.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            cb.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 6, 0));
            cb.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnGroupHeaderLoaded));
            cb.AddHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnGroupHeaderUnloaded));
            cb.AddHandler(System.Windows.Controls.Primitives.ToggleButton.ClickEvent, new RoutedEventHandler(OnGroupHeaderClick));
            sp.AppendChild(cb);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            sp.AppendChild(name);

            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount") { StringFormat = "  ({0})" });
            count.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            count.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            sp.AppendChild(count);

            headerTpl.VisualTree = sp;

            // Container: collapsible Expander whose Header is the group itself.
            var containerStyle = new Style(typeof(GroupItem));
            var tpl = new ControlTemplate(typeof(GroupItem));
            var exp = new FrameworkElementFactory(typeof(Expander));
            exp.SetValue(Expander.IsExpandedProperty, true);
            exp.SetValue(Expander.MarginProperty, new Thickness(0, 2, 0, 2));
            exp.SetValue(HeaderedContentControl.HeaderTemplateProperty, headerTpl);
            exp.SetBinding(HeaderedContentControl.HeaderProperty, new Binding());   // DataContext = CollectionViewGroup
            var ip = new FrameworkElementFactory(typeof(ItemsPresenter));
            ip.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));
            exp.AppendChild(ip);
            tpl.VisualTree = exp;
            containerStyle.Setters.Add(new Setter(Control.TemplateProperty, tpl));

            return new GroupStyle { ContainerStyle = containerStyle };
        }

        private void OnGroupHeaderLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && !_groupHeaderChecks.Contains(cb))
            {
                _groupHeaderChecks.Add(cb);
                SetHeaderState(cb);
            }
        }

        private void OnGroupHeaderUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb) _groupHeaderChecks.Remove(cb);
        }

        private void OnGroupHeaderClick(object sender, RoutedEventArgs e)
        {
            if (_suppressHeaderRefresh) return;
            if (sender is not CheckBox cb || cb.DataContext is not CollectionViewGroup grp) return;
            // Tri-state click cycles; collapse to a clean toggle: if every leaf is
            // already checked, clear the group, otherwise select the whole group.
            bool anyUnchecked = LeafRows(grp).Any(r => !r.IsChecked);
            bool target = anyUnchecked;
            foreach (var r in LeafRows(grp)) r.IsChecked = target;
            RefreshGroupHeaders();
            UpdateStatusLine();
        }

        private static IEnumerable<SheetRow> LeafRows(CollectionViewGroup grp)
        {
            foreach (var item in grp.Items)
            {
                if (item is SheetRow sr) yield return sr;
                else if (item is CollectionViewGroup sub)
                    foreach (var s in LeafRows(sub)) yield return s;
            }
        }

        // Debounced refresh of all realized group-header checkboxes.
        private void ScheduleHeaderRefresh()
        {
            if (_headerRefreshQueued || _groupHeaderChecks.Count == 0) return;
            _headerRefreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _headerRefreshQueued = false;
                RefreshGroupHeaders();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RefreshGroupHeaders()
        {
            _suppressHeaderRefresh = true;
            try { foreach (var cb in _groupHeaderChecks) SetHeaderState(cb); }
            finally { _suppressHeaderRefresh = false; }
        }

        private static void SetHeaderState(CheckBox cb)
        {
            if (cb?.DataContext is not CollectionViewGroup grp) return;
            int total = 0, on = 0;
            foreach (var r in LeafRows(grp)) { total++; if (r.IsChecked) on++; }
            cb.IsChecked = total == 0 ? false : on == total ? true : on == 0 ? (bool?)false : null;
        }

        // PropertyGroupDescription helper: keys a row to its group under the
        // active grouping mode (whole row is passed because the path is null).
        private sealed class GroupKeyConverter : IValueConverter
        {
            private readonly GroupMode _mode;
            public GroupKeyConverter(GroupMode mode) { _mode = mode; }
            public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
                => value is SheetRow r ? GroupKeyFor(r, _mode) : "";
            public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c)
                => throw new NotSupportedException();
        }

        // ── FORMAT panel ────────────────────────────────────────────────────────

        private FrameworkElement BuildFormatPanel()
        {
            var dock = new DockPanel { LastChildFill = true };

            // Format chips
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _fmtPdf   = AddFormatChip(chips, "📄 PDF",  ExportFormats.PDF);
            _fmtDwg   = AddFormatChip(chips, "⬛ DWG",  ExportFormats.DWG);
            _fmtIfc   = AddFormatChip(chips, "🏛 IFC",  ExportFormats.IFC);
            _fmtNwc   = AddFormatChip(chips, "🔷 NWC",  ExportFormats.NWC);
            _fmtDgn   = AddFormatChip(chips, "◇ DGN",  ExportFormats.DGN);
            _fmtDwf   = AddFormatChip(chips, "≋ DWF",  ExportFormats.DWF);
            _fmtImage = AddFormatChip(chips, "🖼 Image",ExportFormats.Image);
            _fmtXml   = AddFormatChip(chips, "⊞ XML",  ExportFormats.XML);
            DockPanel.SetDock(chips, Dock.Top);
            dock.Children.Add(chips);

            // Per-format options
            _formatOptionsHost = new StackPanel();
            var scroller = new ScrollViewer { Content = _formatOptionsHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            dock.Children.Add(scroller);

            return dock;
        }

        private CheckBox AddFormatChip(System.Windows.Controls.Panel parent, string label, ExportFormats fmt)
        {
            var cb = new CheckBox
            {
                Content = label,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 6),
                IsChecked = (_profile.Formats & fmt) != 0,
            };
            cb.Checked   += (_, __) => { _profile.Formats |= fmt;  RebuildFormatOptions(); UpdateStatusLine(); };
            cb.Unchecked += (_, __) => { _profile.Formats &= ~fmt; RebuildFormatOptions(); UpdateStatusLine(); };
            parent.Children.Add(cb);
            return cb;
        }

        private void RebuildFormatOptions()
        {
            _formatOptionsHost.Children.Clear();
            if ((_profile.Formats & ExportFormats.PDF)   != 0) _formatOptionsHost.Children.Add(BuildPdfOptions());
            if ((_profile.Formats & ExportFormats.DWG)   != 0) _formatOptionsHost.Children.Add(BuildDwgOptions());
            if ((_profile.Formats & ExportFormats.IFC)   != 0) _formatOptionsHost.Children.Add(BuildIfcOptions());
            if ((_profile.Formats & ExportFormats.NWC)   != 0) _formatOptionsHost.Children.Add(BuildNwcOptions());
            if ((_profile.Formats & ExportFormats.DGN)   != 0) _formatOptionsHost.Children.Add(BuildSimpleNote("DGN", "Default DGN export options applied."));
            if ((_profile.Formats & ExportFormats.DWF)   != 0) _formatOptionsHost.Children.Add(BuildSimpleNote("DWF", "DWFx default options applied."));
            if ((_profile.Formats & ExportFormats.Image) != 0) _formatOptionsHost.Children.Add(BuildImageOptions());
            if ((_profile.Formats & ExportFormats.XML)   != 0) _formatOptionsHost.Children.Add(BuildXmlOptions());
        }

        private Border BuildPdfOptions()
        {
            var card = NewSection("PDF — Combine, Bookmarks, Watermark");
            var sp = (StackPanel)card.Child;

            // Combine mode — friendly labels so the "merge everything into a single
            // PDF" option is actually discoverable (raw enum names like "OnePerSet"
            // told the user nothing).
            var combineLabels = new (PdfCombineMode Mode, string Label)[]
            {
                (PdfCombineMode.OnePerSheet,      "Separate PDF per sheet"),
                (PdfCombineMode.OnePerSet,        "Single combined PDF (all selected sheets)"),
                (PdfCombineMode.OnePerDiscipline, "Combined PDF per discipline"),
                (PdfCombineMode.CustomGroups,     "Combined PDF per custom group"),
            };
            var combineBox = new ComboBox { Margin = new Thickness(0, 4, 0, 6) };
            foreach (var pair in combineLabels)
                combineBox.Items.Add(new ComboBoxItem { Content = pair.Label, Tag = pair.Mode });
            combineBox.SelectedIndex = Array.FindIndex(combineLabels, p => p.Mode == _profile.Pdf.CombineMode);
            if (combineBox.SelectedIndex < 0) combineBox.SelectedIndex = 0;
            combineBox.SelectionChanged += (_, __) =>
            {
                if ((combineBox.SelectedItem as ComboBoxItem)?.Tag is PdfCombineMode m)
                {
                    _profile.Pdf.CombineMode = m;
                    UpdateStatusLine();
                }
            };
            sp.Children.Add(LabelFor("Output mode", combineBox));

            // Bookmarks
            sp.Children.Add(BindCheck("Add bookmarks", () => _profile.Pdf.AddBookmarks, v => _profile.Pdf.AddBookmarks = v));
            sp.Children.Add(BindCheck("Nest by Discipline", () => _profile.Pdf.NestBookmarksByDiscipline, v => _profile.Pdf.NestBookmarksByDiscipline = v));
            sp.Children.Add(BindCheck("Nest by Level",       () => _profile.Pdf.NestBookmarksByLevel,       v => _profile.Pdf.NestBookmarksByLevel = v));

            // Render
            var hidden = new ComboBox();
            hidden.Items.Add("Auto"); hidden.Items.Add("Vector"); hidden.Items.Add("Raster");
            hidden.SelectedItem = _profile.Pdf.HiddenLineMode;
            hidden.SelectionChanged += (_, __) => _profile.Pdf.HiddenLineMode = hidden.SelectedItem?.ToString() ?? "Auto";
            sp.Children.Add(LabelFor("Hidden lines", hidden));

            var colour = new ComboBox();
            colour.Items.Add("Colour"); colour.Items.Add("Greyscale"); colour.Items.Add("BlackAndWhite");
            colour.SelectedItem = _profile.Pdf.ColourScheme;
            colour.SelectionChanged += (_, __) => _profile.Pdf.ColourScheme = colour.SelectedItem?.ToString() ?? "Colour";
            sp.Children.Add(LabelFor("Colour scheme", colour));

            var dpi = new ComboBox();
            foreach (int d in new[] { 72, 150, 300, 600 }) dpi.Items.Add(d);
            dpi.SelectedItem = _profile.Pdf.RasterDpi;
            dpi.SelectionChanged += (_, __) => { if (int.TryParse(dpi.SelectedItem?.ToString(), out int n)) _profile.Pdf.RasterDpi = n; };
            sp.Children.Add(LabelFor("Raster DPI", dpi));

            // Watermark
            sp.Children.Add(BindCheck("Apply watermark", () => _profile.Pdf.ApplyWatermark, v => _profile.Pdf.ApplyWatermark = v));
            var wmText = new TextBox { Text = _profile.Pdf.WatermarkText };
            wmText.TextChanged += (_, __) => _profile.Pdf.WatermarkText = wmText.Text;
            sp.Children.Add(LabelFor("Watermark text", wmText));

            return card;
        }

        private Border BuildDwgOptions()
        {
            var card = NewSection("DWG — Output mode & Layouts");
            var sp = (StackPanel)card.Child;

            var modeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 6) };
            foreach (var v in Enum.GetNames(typeof(DwgOutputMode))) modeBox.Items.Add(v);
            modeBox.SelectedItem = _profile.Dwg.OutputMode.ToString();
            modeBox.SelectionChanged += (_, __) =>
            {
                Enum.TryParse<DwgOutputMode>(modeBox.SelectedItem?.ToString(), out var m);
                _profile.Dwg.OutputMode = m;
                UpdateStatusLine();
            };
            sp.Children.Add(LabelFor("Output mode", modeBox));

            sp.Children.Add(new TextBlock
            {
                Text =
                    "• OnePerSheet — standard one-DWG-per-sheet (Revit native)\n" +
                    "• AllInOneMultiLayout — all sheets become layout tabs in a single DWG\n" +
                    "  (requires AutoCAD COM or ODA libraries; falls back if missing)\n" +
                    "• ModelSpaceOnly — geometry merged, no paper space\n" +
                    "• CustomGroups — user-defined groups, one DWG per group",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#5B6470"),
                Margin = new Thickness(0, 4, 0, 6),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            });

            var version = new ComboBox();
            foreach (var v in new[] { "AC2018", "AC2013", "AC2010", "AC2007", "AC2004" }) version.Items.Add(v);
            version.SelectedItem = _profile.Dwg.DwgVersion;
            version.SelectionChanged += (_, __) => _profile.Dwg.DwgVersion = version.SelectedItem?.ToString() ?? "AC2018";
            sp.Children.Add(LabelFor("DWG version", version));

            var layoutTpl = new TextBox { Text = _profile.Dwg.LayoutNameTemplate };
            layoutTpl.TextChanged += (_, __) => _profile.Dwg.LayoutNameTemplate = layoutTpl.Text;
            sp.Children.Add(LabelFor("Layout tab name template", layoutTpl));

            sp.Children.Add(BindCheck("Fall back to individual files if merge fails",
                () => _profile.Dwg.FallbackOnMergeFailure, v => _profile.Dwg.FallbackOnMergeFailure = v));

            // Availability hint — distinguishes Method A (true merge) from
            // Method B (ODA: version-normalise + manifest, no real merge).
            string hint;
            Brush hintColour;
            if (ExportCenterDwgMerger.IsAvailable())
            {
                hint = "✅ Method A active — AutoCAD COM detected, true layout merge available.";
                hintColour = Brushes.DarkGreen;
            }
            else if (ExportCenterOdaConverter.IsAvailable())
            {
                hint = "⚙ Method B active — ODA File Converter detected. " +
                       "Per-sheet DWGs will be version-normalised and a merge_manifest.json " +
                       "emitted; true layout merging requires AutoCAD or the Teigha SDK.";
                hintColour = (Brush)new BrushConverter().ConvertFromString("#B07000");
            }
            else
            {
                hint = "⚠ No merger detected. Install AutoCAD (Method A) or the free " +
                       "ODA File Converter (Method B) to enable multi-layout DWG output.";
                hintColour = Brushes.DarkOrange;
            }
            sp.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = hintColour,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });

            return card;
        }

        private Border BuildIfcOptions()
        {
            var card = NewSection("IFC — Schema & Phase");
            var sp = (StackPanel)card.Child;

            var schema = new ComboBox();
            foreach (var v in new[] { "IFC2x3", "IFC4", "IFC4x3" }) schema.Items.Add(v);
            schema.SelectedItem = _profile.Ifc.Schema;
            schema.SelectionChanged += (_, __) => _profile.Ifc.Schema = schema.SelectedItem?.ToString() ?? "IFC4";
            sp.Children.Add(LabelFor("Schema", schema));

            var phase = new ComboBox();
            phase.Items.Add("(current)");
            try
            {
                foreach (Phase p in _doc.Phases) phase.Items.Add(p.Name);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            phase.SelectedIndex = 0;
            phase.SelectionChanged += (_, __) =>
                _profile.Ifc.PhaseName = phase.SelectedIndex == 0 ? null : phase.SelectedItem?.ToString();
            sp.Children.Add(LabelFor("Phase", phase));

            sp.Children.Add(BindCheck("Export linked models", () => _profile.Ifc.ExportLinkedModels, v => _profile.Ifc.ExportLinkedModels = v));

            return card;
        }

        private Border BuildImageOptions()
        {
            var card = NewSection("Image — Format & DPI");
            var sp = (StackPanel)card.Child;

            var fmt = new ComboBox();
            foreach (var v in new[] { "PNG", "JPEG", "TIFF" }) fmt.Items.Add(v);
            fmt.SelectedItem = _profile.Image.Format;
            fmt.SelectionChanged += (_, __) => _profile.Image.Format = fmt.SelectedItem?.ToString() ?? "PNG";
            sp.Children.Add(LabelFor("Format", fmt));

            var dpi = new ComboBox();
            foreach (int d in new[] { 72, 150, 300, 600 }) dpi.Items.Add(d);
            dpi.SelectedItem = _profile.Image.Dpi;
            dpi.SelectionChanged += (_, __) => { if (int.TryParse(dpi.SelectedItem?.ToString(), out int n)) _profile.Image.Dpi = n; };
            sp.Children.Add(LabelFor("DPI", dpi));

            return card;
        }

        private Border BuildSimpleNote(string title, string text)
        {
            var card = NewSection(title);
            ((StackPanel)card.Child).Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)new BrushConverter().ConvertFromString("#5B6470"), FontSize = 11 });
            return card;
        }

        private Border BuildNwcOptions()
        {
            var card = NewSection("NWC — Navisworks Cache");
            var sp = (StackPanel)card.Child;

            string hint = ExportCenterNwcExporter.IsAvailable()
                ? "✅ Navisworks NWC Export Utility detected."
                : "⚠ Navisworks NWC Export Utility NOT detected. Install the free utility from Autodesk to enable.";
            sp.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = ExportCenterNwcExporter.IsAvailable() ? Brushes.DarkGreen : Brushes.DarkOrange,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });

            var scope = new ComboBox();
            foreach (var v in new[] { "Selected", "CurrentView", "Entire" }) scope.Items.Add(v);
            scope.SelectedItem = _profile.Nwc.Scope;
            scope.SelectionChanged += (_, __) => _profile.Nwc.Scope = scope.SelectedItem?.ToString() ?? "Selected";
            sp.Children.Add(LabelFor("Scope", scope));

            var coords = new ComboBox();
            foreach (var v in new[] { "Project", "Shared" }) coords.Items.Add(v);
            coords.SelectedItem = _profile.Nwc.CoordinateSystem;
            coords.SelectionChanged += (_, __) => _profile.Nwc.CoordinateSystem = coords.SelectedItem?.ToString() ?? "Project";
            sp.Children.Add(LabelFor("Coordinates", coords));

            sp.Children.Add(BindCheck("Export element IDs (clash detective)",
                () => _profile.Nwc.ExportElementIdsForClash, v => _profile.Nwc.ExportElementIdsForClash = v));
            return card;
        }

        private Border BuildXmlOptions()
        {
            var card = NewSection("XML — Sheet metadata + parameters");
            var sp = (StackPanel)card.Child;

            var scope = new ComboBox();
            foreach (var v in new[] { "Selected", "ProjectInfoOnly" }) scope.Items.Add(v);
            scope.SelectedItem = _profile.Xml.Scope;
            scope.SelectionChanged += (_, __) => _profile.Xml.Scope = scope.SelectedItem?.ToString() ?? "Selected";
            sp.Children.Add(LabelFor("Scope", scope));

            sp.Children.Add(BindCheck("Include project information",
                () => _profile.Xml.IncludeProjectInfo, v => _profile.Xml.IncludeProjectInfo = v));

            sp.Children.Add(new TextBlock
            {
                Text = "Parameter groups",
                FontSize = 11,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#5B6470"),
                Margin = new Thickness(0, 6, 0, 2),
            });
            foreach (var group in new[] { "Identity", "Location", "Dimensions", "Revisions", "Custom" })
            {
                var g = group;
                var cb = new CheckBox
                {
                    Content = g,
                    IsChecked = _profile.Xml.ParameterGroups.Contains(g),
                    Margin = new Thickness(0, 1, 0, 1),
                };
                cb.Checked   += (_, __) => { if (!_profile.Xml.ParameterGroups.Contains(g)) _profile.Xml.ParameterGroups.Add(g); };
                cb.Unchecked += (_, __) => _profile.Xml.ParameterGroups.RemoveAll(x => x == g);
                sp.Children.Add(cb);
            }

            return card;
        }

        private Border NewSection(string title)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            return new Border
            {
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E5EA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = sp,
            };
        }

        private FrameworkElement LabelFor(string label, FrameworkElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)new BrushConverter().ConvertFromString("#5B6470") });
            sp.Children.Add(control);
            return sp;
        }

        private CheckBox BindCheck(string label, Func<bool> read, Action<bool> write)
        {
            var cb = new CheckBox { Content = label, IsChecked = read(), Margin = new Thickness(0, 2, 0, 2) };
            cb.Checked   += (_, __) => write(true);
            cb.Unchecked += (_, __) => write(false);
            return cb;
        }

        // ── OUTPUT panel ────────────────────────────────────────────────────────

        private FrameworkElement BuildOutputPanel()
        {
            var sp = new StackPanel();

            // Destination
            var dest = NewSection("Destination");
            var destSp = (StackPanel)dest.Child;
            var local = new RadioButton { Content = "Local / Network folder", IsChecked = _profile.Output.Destination == ExportDestination.LocalFolder, GroupName = "Dest" };
            var cde   = new RadioButton { Content = "Planscape CDE",          IsChecked = _profile.Output.Destination == ExportDestination.PlanscapeCde, GroupName = "Dest" };
            var both  = new RadioButton { Content = "Both",                   IsChecked = _profile.Output.Destination == ExportDestination.Both,         GroupName = "Dest" };
            local.Checked += (_, __) => _profile.Output.Destination = ExportDestination.LocalFolder;
            cde.Checked   += (_, __) => _profile.Output.Destination = ExportDestination.PlanscapeCde;
            both.Checked  += (_, __) => _profile.Output.Destination = ExportDestination.Both;
            destSp.Children.Add(local); destSp.Children.Add(cde); destSp.Children.Add(both);

            var folderRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _folderBox = new TextBox { Text = _profile.Output.LocalFolder ?? "" };
            _folderBox.TextChanged += (_, __) => { _profile.Output.LocalFolder = _folderBox.Text; UpdateStatusLine(); };
            Grid.SetColumn(_folderBox, 0);
            folderRow.Children.Add(_folderBox);
            var browse = new Button { Content = "Browse…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            browse.Click += OnBrowseFolderClick;
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(browse);
            destSp.Children.Add(folderRow);

            destSp.Children.Add(BindCheck("Open export(s) when done", () => _profile.Output.OpenFolderWhenDone, v => _profile.Output.OpenFolderWhenDone = v));
            destSp.Children.Add(BindCheck("Create folder if it doesn't exist", () => _profile.Output.CreateFolderIfMissing, v => _profile.Output.CreateFolderIfMissing = v));
            destSp.Children.Add(BindCheck("Split into format sub-folders",     () => _profile.Output.SplitByFormatSubFolder, v => _profile.Output.SplitByFormatSubFolder = v));
            destSp.Children.Add(BindCheck("Split by discipline sub-folders",   () => _profile.Output.SplitByDisciplineSubFolder, v => _profile.Output.SplitByDisciplineSubFolder = v));

            sp.Children.Add(dest);

            // Naming
            var naming = NewSection("File naming");
            var nSp = (StackPanel)naming.Child;

            var preset = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            var presetMap = _profile.Mode == ExportCenterMode.BIM ? ExportNamingPresets.BimMode : ExportNamingPresets.SimpleMode;
            foreach (var k in presetMap.Keys) preset.Items.Add(k);
            preset.Items.Add("Custom…");
            // Select the preset whose template matches the currently-loaded
            // NamingTemplate, otherwise fall through to "Custom…" so the user
            // sees their saved template instead of being silently overridden.
            int matchIdx = -1;
            int i = 0;
            foreach (var kv in presetMap)
            {
                if (string.Equals(kv.Value, _profile.Output.NamingTemplate, StringComparison.Ordinal)) { matchIdx = i; break; }
                i++;
            }
            preset.SelectedIndex = matchIdx >= 0 ? matchIdx : preset.Items.Count - 1; // "Custom…" is last
            preset.SelectionChanged += (_, __) =>
            {
                string key = preset.SelectedItem?.ToString();
                if (key != null && presetMap.TryGetValue(key, out string tpl))
                {
                    _namingBox.Text = tpl;
                }
            };
            nSp.Children.Add(LabelFor("Preset", preset));

            _namingBox = new TextBox { Text = _profile.Output.NamingTemplate };
            _namingBox.TextChanged += (_, __) =>
            {
                _profile.Output.NamingTemplate = _namingBox.Text;
                // Re-sync the preset combo when the user edits the template
                // by hand — keeps the dropdown honest about what's active.
                int newIdx = -1;
                int j = 0;
                foreach (var kv in presetMap)
                {
                    if (string.Equals(kv.Value, _namingBox.Text, StringComparison.Ordinal)) { newIdx = j; break; }
                    j++;
                }
                preset.SelectedIndex = newIdx >= 0 ? newIdx : preset.Items.Count - 1;
                RefreshNamingPreview();
            };
            nSp.Children.Add(LabelFor("Template", _namingBox));

            // Token palette. ISO 19650 tokens are surfaced in both modes —
            // ExportCenterEngine.BuildTokenContext auto-populates them per sheet
            // (sheet STING_* params → stamped DrawingType.IsoNaming → defaults).
            var tokens = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
            string[] common = { "{SheetNumber}", "{SheetTitle}", "{Revision}", "{Discipline}", "{Date:yyyyMMdd}",
                                "{ProjectCode}", "{Originator}", "{Volume}", "{Level}", "{Type}", "{Role}", "{Suitability}" };
            foreach (var t in common) tokens.Children.Add(MakeTokenPill(t));
            nSp.Children.Add(tokens);

            _namingPreview = new TextBlock
            {
                FontSize = 11,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#1F2A3A"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            nSp.Children.Add(LabelFor("Preview (first sheet)", _namingPreview));

            // Conflict mode
            var conflict = new ComboBox();
            foreach (var v in Enum.GetNames(typeof(FilenameConflictMode))) conflict.Items.Add(v);
            conflict.SelectedItem = _profile.Output.ConflictMode.ToString();
            conflict.SelectionChanged += (_, __) =>
            {
                Enum.TryParse<FilenameConflictMode>(conflict.SelectedItem?.ToString(), out var m);
                _profile.Output.ConflictMode = m;
            };
            nSp.Children.Add(LabelFor("If file already exists", conflict));

            sp.Children.Add(naming);

            // Report
            var report = NewSection("Export report");
            var rSp = (StackPanel)report.Child;
            rSp.Children.Add(BindCheck("Generate report", () => _profile.Output.GenerateReport, v => _profile.Output.GenerateReport = v));
            rSp.Children.Add(BindCheck("Open report when done", () => _profile.Output.OpenReportWhenDone, v => _profile.Output.OpenReportWhenDone = v));
            sp.Children.Add(report);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private Border MakeTokenPill(string token)
        {
            var b = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#E5ECFB"),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3577F0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = Cursors.Hand,
            };
            b.Child = new TextBlock { Text = token, FontSize = 11 };
            b.MouseLeftButtonUp += (_, __) =>
            {
                _namingBox.Text = (_namingBox.Text ?? "").TrimEnd() + token;
                RefreshNamingPreview();
            };
            return b;
        }

        // ── Footer ──────────────────────────────────────────────────────────────

        private FrameworkElement BuildFooter()
        {
            var bar = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#F0F2F5"),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D9DDE3"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusLine = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(_statusLine, 0);
            grid.Children.Add(_statusLine);

            var actionRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var progPanel = new StackPanel { Orientation = Orientation.Vertical };
            _progressBar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Value = 0 };
            _progressLabel = new TextBlock { FontSize = 11, Foreground = (Brush)new BrushConverter().ConvertFromString("#5B6470") };
            progPanel.Children.Add(_progressBar);
            progPanel.Children.Add(_progressLabel);
            Grid.SetColumn(progPanel, 0);
            actionRow.Children.Add(progPanel);

            _exportButton = new Button
            {
                Content = "▶  EXPORT",
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(12, 0, 0, 0),
                FontWeight = FontWeights.Bold,
                Background = (Brush)new BrushConverter().ConvertFromString("#3577F0"),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            _exportButton.Click += OnExportClick;
            Grid.SetColumn(_exportButton, 1);
            actionRow.Children.Add(_exportButton);

            Grid.SetRow(actionRow, 1);
            grid.Children.Add(actionRow);

            bar.Child = grid;
            return bar;
        }

        // ── Behaviour: applying/saving profile ──────────────────────────────────

        private void ApplyProfileToUi()
        {
            // Mode-driven UI tweaks: column visibility on the grid, naming presets, etc.
            if (_selectGrid != null)
            {
                var cdeCol = _selectGrid.Columns.LastOrDefault(c => string.Equals(c.Header?.ToString(), "CDE"));
                if (cdeCol != null) cdeCol.Visibility = _profile.Mode == ExportCenterMode.BIM
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            // Sync format chips
            if (_fmtPdf   != null) _fmtPdf.IsChecked   = (_profile.Formats & ExportFormats.PDF)   != 0;
            if (_fmtDwg   != null) _fmtDwg.IsChecked   = (_profile.Formats & ExportFormats.DWG)   != 0;
            if (_fmtIfc   != null) _fmtIfc.IsChecked   = (_profile.Formats & ExportFormats.IFC)   != 0;
            if (_fmtNwc   != null) _fmtNwc.IsChecked   = (_profile.Formats & ExportFormats.NWC)   != 0;
            if (_fmtDgn   != null) _fmtDgn.IsChecked   = (_profile.Formats & ExportFormats.DGN)   != 0;
            if (_fmtDwf   != null) _fmtDwf.IsChecked   = (_profile.Formats & ExportFormats.DWF)   != 0;
            if (_fmtImage != null) _fmtImage.IsChecked = (_profile.Formats & ExportFormats.Image) != 0;
            if (_fmtXml   != null) _fmtXml.IsChecked   = (_profile.Formats & ExportFormats.XML)   != 0;

            if (_namingBox != null) _namingBox.Text = _profile.Output.NamingTemplate;
            if (_folderBox != null) _folderBox.Text = _profile.Output.LocalFolder;

            RebuildFormatOptions();
            RefreshNamingPreview();
        }

        // ── Selection grid + filter ─────────────────────────────────────────────

        private void RefreshSelectionGrid()
        {
            try
            {
                _rows.Clear();
                bool wantSheets = _kindSheets?.IsChecked == true;
                if (wantSheets)
                {
                    var sheets = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>().Where(s => !s.IsTemplate)
                        .OrderBy(s => s.SheetNumber).ToList();
                    foreach (var s in sheets)
                        AddRow(SheetRow.From(_doc, s));
                }
                else
                {
                    var views = new FilteredElementCollector(_doc).OfClass(typeof(View))
                        .Cast<View>().Where(v => !v.IsTemplate && !(v is ViewSheet))
                        .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name).ToList();
                    foreach (var v in views)
                        AddRow(SheetRow.FromView(v));
                }
                ApplySearchFilter();
                RefreshNamingPreview();
                UpdateStatusLine();
            }
            catch (Exception ex)
            {
                StingLog.Warn("ExportCentre RefreshSelectionGrid: " + ex.Message);
            }
        }

        // Add a row and keep group-header tri-state in sync when its checkbox toggles.
        private void AddRow(SheetRow row)
        {
            row.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(SheetRow.IsChecked)) ScheduleHeaderRefresh();
            };
            _rows.Add(row);
        }

        private void ApplySearchFilter()
        {
            try
            {
                var view = CollectionViewSource.GetDefaultView(_rows);
                if (view == null) return;
                string q = _searchBox?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(q)) { view.Filter = null; return; }
                view.Filter = obj =>
                {
                    var r = (SheetRow)obj;
                    return (r.Number ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (r.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (r.Discipline ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (r.PaperSize ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                };
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private void ApplySavedSetSelection()
        {
            try
            {
                int idx = _setCombo.SelectedIndex;
                if (idx < 0 || idx >= _state.SavedSets.Count) return;
                var set = _state.SavedSets[idx];
                var ids = ExportCenterEngine.ResolveSet(_doc, set, out int missing);
                var idSet = new HashSet<long>(ids.Select(e => e.Value));
                foreach (var r in _rows) r.IsChecked = idSet.Contains(r.Id);
                if (missing > 0) UpdateStatusLine($"{missing} sheets in set were not found in this model.");
                else UpdateStatusLine();
            }
            catch (Exception ex) { StingLog.Warn("ApplySavedSetSelection: " + ex.Message); }
        }

        // ── Status line + naming preview ────────────────────────────────────────

        private void RefreshNamingPreview()
        {
            try
            {
                if (_namingPreview == null) return;
                var first = _rows.FirstOrDefault(r => r.IsChecked) ?? _rows.FirstOrDefault();
                if (first == null) { _namingPreview.Text = "(no sheets)"; return; }
                var view = _doc.GetElement(new ElementId(first.Id)) as View;
                string stem = ExportCenterEngine.ResolveNaming(_doc, view, _namingBox.Text, _profile.Output);
                stem = ExportCenterEngine.Sanitise(stem, _profile.Output.IllegalCharReplacement);
                _namingPreview.Text = stem + ".pdf";
            }
            catch (Exception ex) { _namingPreview.Text = "(error: " + ex.Message + ")"; }
        }

        private void UpdateStatusLine(string warning = null)
        {
            try
            {
                int picked = _rows.Count(r => r.IsChecked);
                var fmts = new List<string>();
                foreach (ExportFormats f in Enum.GetValues(typeof(ExportFormats)))
                    if (f != ExportFormats.None && (_profile.Formats & f) != 0) fmts.Add(f.ToString());
                string fmtsLabel = fmts.Count == 0 ? "(no format)" : string.Join(", ", fmts);

                string line = $"{picked} {(_kindSheets?.IsChecked == true ? "sheets" : "views")} selected · " +
                              $"{fmtsLabel} · Naming: {_profile.Output.NamingTemplate} · " +
                              $"Output: {_profile.Output.LocalFolder ?? "(not set)"}";
                if (!string.IsNullOrEmpty(warning))
                    line += "\n⚠ " + warning;
                _statusLine.Text = line;
                _statusLine.Foreground = string.IsNullOrEmpty(warning)
                    ? (Brush)new BrushConverter().ConvertFromString("#1F2A3A")
                    : Brushes.DarkOrange;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // ── Header button handlers ──────────────────────────────────────────────

        private void OnSaveProfileClick(object _, RoutedEventArgs __)
        {
            if (_profile.BuiltIn)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Export Centre", "Built-in profiles can't be edited. Use 'Save As…' to fork.");
                return;
            }
            CommitProfile();
            Autodesk.Revit.UI.TaskDialog.Show("Export Centre", $"Profile '{_profile.Name}' saved.");
        }

        private void OnSaveAsProfileClick(object _, RoutedEventArgs __)
        {
            string name = PromptForText("Save profile as", "Profile name:", _profile.Name + " (copy)");
            if (string.IsNullOrEmpty(name)) return;
            var copy = JsonClone(_profile);
            copy.Name = name; copy.BuiltIn = false; copy.CreatedAt = DateTime.UtcNow;
            _state.Profiles.Add(copy);
            _profile = copy;
            _profileCombo.Items.Add(name);
            _profileCombo.SelectedIndex = _state.Profiles.Count - 1;
            CommitProfile();
        }

        private void OnImportProfileClick(object _, RoutedEventArgs __)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "STING Export Profile (*.stingexport)|*.stingexport|JSON files (*.json)|*.json|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true) return;
                var json = File.ReadAllText(dlg.FileName);
                var p = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportProfile>(json);
                if (p == null) return;
                p.BuiltIn = false;
                _state.Profiles.Add(p);
                _profileCombo.Items.Add(p.Name);
                _profile = p;
                _profileCombo.SelectedIndex = _state.Profiles.Count - 1;
                CommitProfile();
            }
            catch (Exception ex) { Autodesk.Revit.UI.TaskDialog.Show("Import", "Failed: " + ex.Message); }
        }

        private void OnShareProfileClick(object _, RoutedEventArgs __)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "STING Export Profile (*.stingexport)|*.stingexport",
                    FileName = ExportCenterEngine.Sanitise(_profile.Name ?? "profile", "_") + ".stingexport",
                };
                if (dlg.ShowDialog() != true) return;
                File.WriteAllText(dlg.FileName,
                    Newtonsoft.Json.JsonConvert.SerializeObject(_profile, Newtonsoft.Json.Formatting.Indented));
                Autodesk.Revit.UI.TaskDialog.Show("Share", "Profile exported to:\n" + dlg.FileName);
            }
            catch (Exception ex) { Autodesk.Revit.UI.TaskDialog.Show("Share", "Failed: " + ex.Message); }
        }

        // ── OUTPUT button handlers ──────────────────────────────────────────────

        private void OnBrowseFolderClick(object _, RoutedEventArgs __)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Pick output folder",
                    FileName = "ExportCentre",
                    Filter = "Folder|*.this",
                };
                if (dlg.ShowDialog() == true)
                {
                    string folder = Path.GetDirectoryName(dlg.FileName);
                    _folderBox.Text = folder;
                    _profile.Output.LocalFolder = folder;
                    if (!_state.RecentFolders.Contains(folder)) _state.RecentFolders.Insert(0, folder);
                    if (_state.RecentFolders.Count > 5) _state.RecentFolders.RemoveAt(5);
                    UpdateStatusLine();
                }
            }
            catch (Exception ex) { Autodesk.Revit.UI.TaskDialog.Show("Browse", ex.Message); }
        }

        // ── Saved-set handlers ──────────────────────────────────────────────────

        private void OnSaveSetClick(object _, RoutedEventArgs __)
        {
            string name = PromptForText("Save selection set", "Set name:", "My set");
            if (string.IsNullOrEmpty(name)) return;
            var set = new ExportSavedSet
            {
                Name = name,
                Kind = _kindSheets?.IsChecked == true ? ExportSelectionKind.Sheets : ExportSelectionKind.Views,
                ElementIds = _rows.Where(r => r.IsChecked).Select(r => r.Id.ToString()).ToList(),
                FilterText = _searchBox?.Text,
            };
            _state.SavedSets.Add(set);
            _setCombo.Items.Add(name);
            _setCombo.SelectedIndex = _state.SavedSets.Count - 1;
            ExportCenterEngine.SaveState(_state);
        }

        private void OnManageSetsClick(object _, RoutedEventArgs __)
        {
            // Minimal manager: list sets and offer Delete on user-defined ones.
            var dlg = new Window
            {
                Title = "Manage selection sets",
                Width = 420, Height = 360,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var lb = new ListBox();
            foreach (var s in _state.SavedSets) lb.Items.Add(s.Name + (s.BuiltIn ? "  (built-in)" : ""));
            var del = new Button { Content = "Delete selected", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            del.Click += (_, __) =>
            {
                int i = lb.SelectedIndex;
                if (i < 0) return;
                var s = _state.SavedSets[i];
                if (s.BuiltIn) { Autodesk.Revit.UI.TaskDialog.Show("Sets", "Built-in sets cannot be deleted."); return; }
                _state.SavedSets.RemoveAt(i);
                lb.Items.RemoveAt(i);
                _setCombo.Items.RemoveAt(i);
                ExportCenterEngine.SaveState(_state);
            };
            var sp = new StackPanel { Margin = new Thickness(10) };
            sp.Children.Add(lb); sp.Children.Add(del);
            dlg.Content = sp;
            dlg.ShowDialog();
        }

        // ── EXPORT click — pre-flight + run ─────────────────────────────────────

        private void OnExportClick(object _, RoutedEventArgs __)
        {
            if (_exporting) { _cancelRequested = true; return; }

            CommitProfile();
            var ids = _rows.Where(r => r.IsChecked).Select(r => new ElementId(r.Id)).ToList();

            // Pre-flight
            var issues = ExportCenterEngine.PreflightCheck(_doc, _profile, ids);
            var blocking = issues.Where(i => i.Level == ExportPreflightIssue.Severity.Error).ToList();
            if (blocking.Count > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Pre-flight",
                    "Cannot start export:\n\n" + string.Join("\n", blocking.Select(i => "• " + i.Message)));
                return;
            }
            var warns = issues.Where(i => i.Level == ExportPreflightIssue.Severity.Warning).ToList();
            if (warns.Count > 0)
            {
                var td = new Autodesk.Revit.UI.TaskDialog("Pre-flight warnings")
                {
                    MainInstruction = $"{warns.Count} warning(s) found. Continue?",
                    MainContent = string.Join("\n", warns.Select(i => "• " + i.Message)),
                    CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                    DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Yes,
                };
                if (td.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes) return;
            }

            // Run inline. Revit API calls happen on the UI thread already (we're
            // inside the dialog launched from an external command). For long runs
            // a future patch can route through ExternalEvent.
            _exporting = true; _cancelRequested = false;
            _exportButton.Content = "■ CANCEL";
            try
            {
                int total = ids.Count * Math.Max(1, CountActiveFormats());
                var result = ExportCenterEngine.Run(_doc, _profile, ids,
                    (cur, tot, label) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _progressBar.Value = (double)cur * 100.0 / Math.Max(1, total);
                            _progressLabel.Text = $"{cur}/{total}  {label}";
                        });
                    },
                    () => _cancelRequested);

                ShowResultSummary(result);
                OpenExportOutputs(result);
            }
            finally
            {
                _exporting = false;
                _exportButton.Content = "▶  EXPORT";
                _progressBar.Value = 0;
                _progressLabel.Text = "";
            }
        }

        private int CountActiveFormats()
        {
            int n = 0;
            foreach (ExportFormats f in Enum.GetValues(typeof(ExportFormats)))
                if (f != ExportFormats.None && (_profile.Formats & f) != 0) n++;
            return n;
        }

        /// <summary>
        /// "Open … when done" handler. Opens the actual produced document(s) in the
        /// OS default app (e.g. the PDF viewer) for a small batch; for a large batch
        /// opens the containing folder instead so we don't spawn dozens of viewer
        /// windows. Falls back to the destination folder when no per-file path
        /// resolved. Replaces the old explorer-only open, which only ever opened the
        /// folder and silently no-op'd when the produced files lived in a subfolder.
        /// </summary>
        private void OpenExportOutputs(ExportRunResult result)
        {
            try
            {
                if (_profile?.Output == null || !_profile.Output.OpenFolderWhenDone) return;

                var files = result?.Rows?
                    .Where(r => r.Success && !string.IsNullOrEmpty(r.OutputPath) && File.Exists(r.OutputPath))
                    .Select(r => r.OutputPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                if (files.Count > 0 && files.Count <= 8)
                {
                    foreach (var f in files) ShellOpen(f);   // open each document
                }
                else
                {
                    string folder = files.Count > 0
                        ? Path.GetDirectoryName(files[0])
                        : _profile.Output.LocalFolder;
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        ShellOpen(folder);                   // open the containing folder
                }
            }
            catch (Exception ex) { StingLog.Warn($"OpenExportOutputs: {ex.Message}"); }
        }

        /// <summary>
        /// Open a file or folder with the OS shell so a file launches in its default
        /// app and a folder opens in Explorer. UseShellExecute=true is required in
        /// .NET 8 to open a document by path (the old Process.Start("explorer.exe",
        /// path) defaulted to UseShellExecute=false and could silently fail).
        /// </summary>
        private static void ShellOpen(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { StingLog.Warn($"ShellOpen '{path}': {ex.Message}"); }
        }

        private void ShowResultSummary(ExportRunResult r)
        {
            string msg = $"Export complete.\n\n" +
                         $"Successful: {r.Success}\n" +
                         $"Failed:     {r.Failed}\n" +
                         $"Cancelled:  {(r.Cancelled ? "yes" : "no")}\n" +
                         $"Duration:   {(r.FinishedUtc - r.StartedUtc).TotalSeconds:F1} sec\n";
            if (r.Warnings.Count > 0)
                msg += "\nWarnings:\n" + string.Join("\n", r.Warnings.Take(8).Select(w => "• " + w));
            if (r.Failed > 0)
                msg += "\n\nFirst failures:\n" + string.Join("\n",
                    r.Rows.Where(x => !x.Success).Take(5).Select(x => $"• {x.SheetNumber}: {x.Error}"));
            Autodesk.Revit.UI.TaskDialog.Show("STING Export Centre", msg);
        }

        private void CommitProfile()
        {
            _state.LastProfile = _profile.Name;
            if (!string.IsNullOrEmpty(_profile.Output.LocalFolder))
                _state.LastOutputFolder = _profile.Output.LocalFolder;
            _state.LastNamingTemplate = _profile.Output.NamingTemplate;
            ExportCenterEngine.SaveState(_state);
        }

        // ── Tiny helpers ────────────────────────────────────────────────────────

        private static T JsonClone<T>(T src) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
                Newtonsoft.Json.JsonConvert.SerializeObject(src));

        private string PromptForText(string title, string label, string initial)
        {
            var dlg = new Window
            {
                Title = title, Width = 380, Height = 150,
                Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var tb = new TextBox { Text = initial };
            sp.Children.Add(tb);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
            btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
            sp.Children.Add(btnRow);
            string result = null;
            ok.Click += (_, __) => { result = tb.Text; dlg.DialogResult = true; };
            dlg.Content = sp;
            return dlg.ShowDialog() == true ? result : null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  SheetRow — view-model for the SELECT grid.
    // ════════════════════════════════════════════════════════════════════════════

    public class SheetRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string Discipline { get; set; }
        public string PaperSize { get; set; }
        public string CdeStatus { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static SheetRow From(Document doc, ViewSheet s)
        {
            var row = new SheetRow
            {
                Id = s.Id.Value,
                Number = s.SheetNumber,
                Title = s.Name,
                Discipline = ExportCenterEngine.GetDisciplinePrefix(s.SheetNumber),
            };

            // Revision (current)
            try
            {
                var revIds = s.GetAllRevisionIds();
                if (revIds != null && revIds.Count > 0)
                {
                    if (doc.GetElement(revIds[revIds.Count - 1]) is Revision r)
                        row.Revision = r.SequenceNumber.ToString();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // Paper size — heuristic: read sheet's title block instance width.
            try
            {
                var tb = new FilteredElementCollector(doc, s.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType().FirstElement();
                if (tb != null)
                {
                    double w = tb.LookupParameter("Sheet Width")?.AsDouble() ?? 0;
                    double h = tb.LookupParameter("Sheet Height")?.AsDouble() ?? 0;
                    row.PaperSize = ClassifyPaperSize(w, h);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // CDE — read ASS_CDE_STATUS_TXT if present, else "-"
            try
            {
                var p = s.LookupParameter("ASS_CDE_STATUS_TXT");
                row.CdeStatus = p != null && p.HasValue ? p.AsString() : "-";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); row.CdeStatus = "-"; }

            return row;
        }

        public static SheetRow FromView(View v) => new()
        {
            Id = v.Id.Value,
            Number = v.ViewType.ToString(),
            Title = v.Name,
            Discipline = "",
            PaperSize = "",
            CdeStatus = "",
        };

        private static string ClassifyPaperSize(double widthFt, double heightFt)
        {
            // Convert to mm; classify by closest ISO size.
            double wmm = Math.Round(widthFt  * 304.8);
            double hmm = Math.Round(heightFt * 304.8);
            (string name, double w, double h)[] sizes =
            {
                ("A0", 1189, 841), ("A1", 841, 594), ("A2", 594, 420),
                ("A3", 420, 297),  ("A4", 297, 210),
            };
            foreach (var sz in sizes)
                if ((Approx(wmm, sz.w) && Approx(hmm, sz.h)) ||
                    (Approx(wmm, sz.h) && Approx(hmm, sz.w))) return sz.name;
            return $"{wmm:F0}×{hmm:F0}";
        }

        private static bool Approx(double a, double b) => Math.Abs(a - b) < 8.0;
    }
}
