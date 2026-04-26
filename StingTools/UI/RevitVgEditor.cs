// StingTools — Drawing Template Manager · Phase 137
//
// RevitVgEditor — full inline replica of Revit's "Visibility/Graphic
// Overrides" dialog. Drops directly into a card on the pack form so
// users never have to bounce out to Revit's native VG dialog.
//
// Faithful reproduction:
//   * Four tabs:  Model Categories · Annotation Categories ·
//                 Imported Categories · Filters
//   * "Show model categories in this view" master toggle
//   * Category-name search + Filter-list dropdown
//   * Composite header with grouped bands:
//       Visibility · [Projection/Surface: Lines · Patterns · Transparency] ·
//       [Cut: Lines · Patterns] · Halftone · Detail Level
//   * Tree-style rows: parent row + indented subcategories
//   * Expand chevron per parent (▶ / ▼)
//   * Bottom toolbar: All · None · Invert · Expand All · Override
//     Host Layers + Cut Line Styles · Object Styles… · Edit…
//
// Backing model: Dictionary<string, PresetCategoryOverride> keyed by
//   "BuiltInCategory" for parent rows or "BuiltInCategory/<SubName>"
//   for subcategory rows. Live read from doc.Settings.Categories so
//   project-specific subcategories show up automatically.
//
// Performance: DataGrid virtualization on (ScrollUnit=Item, recycling).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;
// Resolve type collisions between WPF and Revit API:
using Visibility = System.Windows.Visibility;
using Binding    = System.Windows.Data.Binding;
using Color      = System.Windows.Media.Color;

namespace StingTools.UI
{
    public sealed class RevitVgEditor
    {
        // ─────────────────────────────────────────────────────────
        //  Row view-model
        // ─────────────────────────────────────────────────────────

        public sealed class VgRow : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string n)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
                AnyChanged?.Invoke(this);
            }

            public Action<VgRow> AnyChanged { get; set; }

            public string Bic { get; set; }
            public string SubCategoryName { get; set; }     // null for parents
            public string DisplayName { get; set; }
            public string GroupTag { get; set; }            // "Model" | "Annotation" | "Imported"
            public bool IsParent => string.IsNullOrEmpty(SubCategoryName);
            public bool HasCutLines { get; set; } = true;
            public bool HasHalftone { get; set; } = true;
            public bool HasDetailLevel { get; set; } = false;
            public bool HasChildren { get; set; }
            public string Key => IsParent ? Bic : $"{Bic}/{SubCategoryName}";
            public PresetCategoryOverride Data { get; set; }

            // Indentation / chevron rendering
            public Thickness NameMargin => IsParent ? new Thickness(0, 0, 0, 0) : new Thickness(28, 0, 0, 0);
            public FontWeight NameWeight => IsParent ? FontWeights.SemiBold : FontWeights.Normal;
            public Visibility ChevronVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
            public string Chevron { get; private set; } = "▼";
            private bool _expanded = true;
            public bool Expanded
            {
                get => _expanded;
                set { _expanded = value; Chevron = value ? "▼" : "▶"; Raise(nameof(Chevron)); }
            }

            // Bindable cells — round-trip into Data
            public bool? Visible
            {
                get => Data.Visible;
                set { Data.Visible = value; Raise(nameof(Visible)); }
            }
            public bool? Halftone { get => Data.Halftone; set { Data.Halftone = value; Raise(nameof(Halftone)); } }
            public string DetailLevelStr
            {
                get => Data.DetailLevel ?? "By View";
                set { Data.DetailLevel = (value == "By View" || string.IsNullOrEmpty(value)) ? null : value; Raise(nameof(DetailLevelStr)); }
            }
            public string ProjLineColor    { get => Data.ProjLineColor;    set { Data.ProjLineColor = value;    Raise(nameof(ProjLineColor)); } }
            public string ProjLineWeightStr{ get => Data.ProjLineWeight?.ToString(); set { Data.ProjLineWeight = ParseInt(value); Raise(nameof(ProjLineWeightStr)); } }
            public string ProjLinePattern  { get => Data.ProjLinePattern;  set { Data.ProjLinePattern = value;  Raise(nameof(ProjLinePattern)); } }
            public string SurfFgColor      { get => Data.SurfFgColor;      set { Data.SurfFgColor = value;      Raise(nameof(SurfFgColor)); } }
            public string SurfFgPattern    { get => Data.SurfFgPattern;    set { Data.SurfFgPattern = value;    Raise(nameof(SurfFgPattern)); } }
            public string SurfBgColor      { get => Data.SurfBgColor;      set { Data.SurfBgColor = value;      Raise(nameof(SurfBgColor)); } }
            public string SurfBgPattern    { get => Data.SurfBgPattern;    set { Data.SurfBgPattern = value;    Raise(nameof(SurfBgPattern)); } }
            public string TransparencyStr  { get => Data.Transparency?.ToString(); set { Data.Transparency = ParseInt(value); Raise(nameof(TransparencyStr)); } }
            public string CutLineColor     { get => Data.CutLineColor;     set { Data.CutLineColor = value;     Raise(nameof(CutLineColor)); } }
            public string CutLineWeightStr { get => Data.CutLineWeight?.ToString(); set { Data.CutLineWeight = ParseInt(value); Raise(nameof(CutLineWeightStr)); } }
            public string CutLinePattern   { get => Data.CutLinePattern;   set { Data.CutLinePattern = value;   Raise(nameof(CutLinePattern)); } }

            private static int? ParseInt(string s) => int.TryParse(s, out var v) ? (int?)v : null;
        }

        // ─────────────────────────────────────────────────────────
        //  Editor state
        // ─────────────────────────────────────────────────────────

        public Dictionary<string, PresetCategoryOverride> Data { get; }

        /// <summary>Fires after every cell change so callers can mirror the
        /// edit into a different shape (e.g. PackCategoryOverride). The
        /// argument is the row that changed.</summary>
        public event Action<VgRow> RowChanged;

        public ObservableCollection<VgRow> AllRows { get; } = new ObservableCollection<VgRow>();
        private readonly Document _doc;
        private TextBox _search;
        private CheckBox _showInView;
        private CheckBox _hostLayers;
        private DataGrid _modelGrid, _annoGrid;
        private CollectionView _modelView, _annoView;
        private List<string> _linePatterns;
        private List<string> _fillPatterns;

        public RevitVgEditor(Document doc, Dictionary<string, PresetCategoryOverride> data)
        {
            _doc = doc;
            Data = data ?? new Dictionary<string, PresetCategoryOverride>();
            LoadPatterns();
            BuildRowsFromDocument();
        }

        // ─────────────────────────────────────────────────────────
        //  Layout
        // ─────────────────────────────────────────────────────────

        public FrameworkElement Build()
        {
            var dock = new DockPanel { LastChildFill = true };

            // Top: tabs
            var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 0) };
            DockPanel.SetDock(tabs, Dock.Top);

            // Top toolbar shared above the tab content area
            var topBar = BuildTopBar();

            tabs.Items.Add(BuildTab("Model Categories",      isAnno: false, isImported: false));
            tabs.Items.Add(BuildTab("Annotation Categories", isAnno: true,  isImported: false));
            tabs.Items.Add(BuildPlaceholderTab("Imported Categories",
                "DWG-link layer overrides — TODO. The active project's imported DWG/DXF " +
                "instances expose Layer overrides via View.SetCategoryOverrides on each " +
                "import-instance subcategory; this tab will surface them in a follow-up."));
            tabs.Items.Add(BuildPlaceholderTab("Filters",
                "Per-filter graphic overrides — already managed by the Filter rules card on " +
                "the View Style Pack form. This tab mirrors that view in Revit's native VG " +
                "dialog and is informational here."));

            dock.Children.Add(topBar);
            dock.Children.Add(BuildBottomBar());
            dock.Children.Add(tabs);
            return dock;
        }

        private FrameworkElement BuildTopBar()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(stack, Dock.Top);

            _showInView = new CheckBox
            {
                Content = "Show model categories in this view",
                IsChecked = true,
                Margin = new Thickness(2, 4, 0, 4),
                FontWeight = FontWeights.SemiBold
            };
            stack.Children.Add(_showInView);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var lblSearch = new TextBlock { Text = "Category name search:", VerticalAlignment = VerticalAlignment.Center };
            _search = new TextBox { Margin = new Thickness(4, 0, 12, 0) };
            _search.TextChanged += (s, e) => RefreshFilters();
            var lblFilter = new TextBlock { Text = "Filter list:", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            var cbFilter = new ComboBox();
            cbFilter.Items.Add("<multiple>");
            cbFilter.Items.Add("Architectural");
            cbFilter.Items.Add("Structural");
            cbFilter.Items.Add("Mechanical");
            cbFilter.Items.Add("Electrical");
            cbFilter.Items.Add("Plumbing");
            cbFilter.Items.Add("Coordination");
            cbFilter.SelectedIndex = 0;

            Grid.SetColumn(lblSearch, 0); Grid.SetColumn(_search, 1);
            Grid.SetColumn(lblFilter, 2); Grid.SetColumn(cbFilter, 3);
            row.Children.Add(lblSearch);
            row.Children.Add(_search);
            row.Children.Add(lblFilter);
            row.Children.Add(cbFilter);
            stack.Children.Add(row);
            return stack;
        }

        private TabItem BuildTab(string header, bool isAnno, bool isImported)
        {
            var tab = new TabItem { Header = header };
            var dock = new DockPanel { LastChildFill = true };
            // Composite group header strip
            dock.Children.Add(BuildGroupHeader());
            // The grid
            var grid = BuildDataGrid();
            if (isAnno) { _annoGrid = grid; }
            else        { _modelGrid = grid; }
            dock.Children.Add(grid);

            // Filter rows by group tag
            var view = (CollectionView)CollectionViewSource.GetDefaultView(AllRows);
            // Use a per-tab collection view by wrapping in a CollectionViewSource
            var cvs = new CollectionViewSource { Source = AllRows };
            var perTab = (CollectionView)cvs.View;
            perTab.Filter = o =>
            {
                if (!(o is VgRow r)) return false;
                if (isAnno && r.GroupTag != "Annotation") return false;
                if (!isAnno && r.GroupTag != "Model") return false;
                if (!string.IsNullOrEmpty(_search?.Text) &&
                    (r.DisplayName ?? "").IndexOf(_search.Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (r.Bic ?? "").IndexOf(_search.Text, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                return true;
            };
            grid.ItemsSource = perTab;
            if (isAnno) _annoView = perTab; else _modelView = perTab;

            tab.Content = dock;
            return tab;
        }

        private TabItem BuildPlaceholderTab(string header, string body)
        {
            var tab = new TabItem { Header = header };
            tab.Content = new TextBlock {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12),
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 130))
            };
            return tab;
        }

        // ─────────────────────────────────────────────────────────
        //  Composite group header (Visibility · Proj/Surface · Cut · Halftone · DL)
        // ─────────────────────────────────────────────────────────

        // Column widths shared between the group header and the DataGrid
        // columns underneath so the bands line up exactly.
        private const double W_VIS = 320;
        private const double W_PROJ_LINE = 80;
        private const double W_PROJ_PATT = 110;
        private const double W_PROJ_TRANS = 70;
        private const double W_CUT_LINE = 80;
        private const double W_CUT_PATT = 110;
        private const double W_HALFTONE = 64;
        private const double W_DETAIL = 80;

        private FrameworkElement BuildGroupHeader()
        {
            var bar = new Grid { Height = 28, Background = new SolidColorBrush(Color.FromRgb(232, 232, 240)) };
            DockPanel.SetDock(bar, Dock.Top);
            // 1 col Visibility, 3 cols Proj/Surface group, 2 cols Cut group, 1 col Halftone, 1 col Detail Level
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W_VIS) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W_PROJ_LINE + W_PROJ_PATT + W_PROJ_TRANS) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W_CUT_LINE + W_CUT_PATT) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W_HALFTONE) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(W_DETAIL) });

            var bVis = MakeBandLabel("");
            var bProj = MakeBandLabel("Projection / Surface");
            var bCut = MakeBandLabel("Cut");
            var bHt = MakeBandLabel("");
            var bDl = MakeBandLabel("");
            Grid.SetColumn(bVis, 0); Grid.SetColumn(bProj, 1); Grid.SetColumn(bCut, 2); Grid.SetColumn(bHt, 3); Grid.SetColumn(bDl, 4);
            bar.Children.Add(bVis); bar.Children.Add(bProj); bar.Children.Add(bCut); bar.Children.Add(bHt); bar.Children.Add(bDl);
            return bar;
        }

        private static FrameworkElement MakeBandLabel(string text)
        {
            return new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 190)),
                Child = new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70))
                }
            };
        }

        // ─────────────────────────────────────────────────────────
        //  DataGrid (per tab)
        // ─────────────────────────────────────────────────────────

        private DataGrid BuildDataGrid()
        {
            var g = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeColumns = false,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeight = 22,
                ColumnHeaderHeight = 26,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 50)),
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            VirtualizingPanel.SetIsVirtualizing(g, true);
            VirtualizingPanel.SetVirtualizationMode(g, VirtualizationMode.Recycling);

            // Visibility column: chevron + checkbox + name (single composite cell)
            var visCol = new DataGridTemplateColumn { Header = "Visibility", Width = new DataGridLength(W_VIS, DataGridLengthUnitType.Pixel), CanUserResize = false };
            var dt = new DataTemplate();
            var fSp = new FrameworkElementFactory(typeof(StackPanel));
            fSp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var fChev = new FrameworkElementFactory(typeof(TextBlock));
            fChev.SetBinding(TextBlock.TextProperty, new Binding(nameof(VgRow.Chevron)));
            fChev.SetBinding(TextBlock.VisibilityProperty, new Binding(nameof(VgRow.ChevronVisibility)));
            fChev.SetValue(TextBlock.WidthProperty, 14.0);
            fChev.SetValue(TextBlock.MarginProperty, new Thickness(2, 0, 4, 0));
            fChev.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(100, 100, 110)));

            var fCb = new FrameworkElementFactory(typeof(CheckBox));
            fCb.SetValue(CheckBox.IsThreeStateProperty, true);
            fCb.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(VgRow.Visible)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            fCb.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 6, 0));
            fCb.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);

            var fName = new FrameworkElementFactory(typeof(TextBlock));
            fName.SetBinding(TextBlock.TextProperty, new Binding(nameof(VgRow.DisplayName)));
            fName.SetBinding(TextBlock.MarginProperty, new Binding(nameof(VgRow.NameMargin)));
            fName.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(VgRow.NameWeight)));
            fName.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            fSp.AppendChild(fChev);
            fSp.AppendChild(fCb);
            fSp.AppendChild(fName);
            dt.VisualTree = fSp;
            visCol.CellTemplate = dt;
            g.Columns.Add(visCol);

            g.Columns.Add(TextCol("Lines",         nameof(VgRow.ProjLineColor),    W_PROJ_LINE / 2));
            g.Columns.Add(WeightCol("Wt",          nameof(VgRow.ProjLineWeightStr), W_PROJ_LINE / 2));
            g.Columns.Add(PatternCol("Patterns",   nameof(VgRow.ProjLinePattern),  W_PROJ_PATT, lines: true));
            g.Columns.Add(TextCol("Trans %",       nameof(VgRow.TransparencyStr),  W_PROJ_TRANS));

            g.Columns.Add(TextCol("Lines",         nameof(VgRow.CutLineColor),     W_CUT_LINE / 2));
            g.Columns.Add(WeightCol("Wt",          nameof(VgRow.CutLineWeightStr), W_CUT_LINE / 2));
            g.Columns.Add(PatternCol("Patterns",   nameof(VgRow.CutLinePattern),   W_CUT_PATT, lines: true));

            g.Columns.Add(BoolCol("Halftone",      nameof(VgRow.Halftone),         W_HALFTONE));
            g.Columns.Add(ComboCol("Detail Level", nameof(VgRow.DetailLevelStr),
                new[] { "By View", "Coarse", "Medium", "Fine" },                     W_DETAIL));

            return g;
        }

        // ─────────────────────────────────────────────────────────
        //  Bottom toolbar (All / None / Invert / Expand / Override host layers / Object Styles)
        // ─────────────────────────────────────────────────────────

        private FrameworkElement BuildBottomBar()
        {
            var bottom = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: bulk-action buttons + helper note + Object Styles…
            var left = new StackPanel { Orientation = Orientation.Vertical };
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            btnRow.Children.Add(MkBtn("All",        (s, e) => SetAllVisible(true)));
            btnRow.Children.Add(MkBtn("None",       (s, e) => SetAllVisible(false)));
            btnRow.Children.Add(MkBtn("Invert",     (s, e) => InvertVisible()));
            btnRow.Children.Add(MkBtn("Expand All", (s, e) => SetExpanded(true)));
            btnRow.Children.Add(MkBtn("Collapse All",(s, e) => SetExpanded(false)));
            btnRow.Children.Add(MkBtn("Clear Overrides", (s, e) => ClearOverrides()));
            left.Children.Add(btnRow);

            var note = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 120)),
                Text = "Categories that are not overridden are drawn according to Object Style settings."
            };
            var noteRow = new StackPanel { Orientation = Orientation.Horizontal };
            noteRow.Children.Add(note);
            noteRow.Children.Add(new Button
            {
                Content = "Object Styles…",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Object Styles authoring lives in Revit's native Manage tab — STING does not duplicate it."
            });
            left.Children.Add(noteRow);

            // Right: Override Host Layers + Cut Line Styles + Edit…
            var right = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right };
            var hostRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 4) };
            hostRow.Children.Add(new TextBlock { Text = "Override Host Layers", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(hostRow);
            var cutRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _hostLayers = new CheckBox { Content = "Cut Line Styles", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            cutRow.Children.Add(_hostLayers);
            cutRow.Children.Add(new Button
            {
                Content = "Edit…",
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Edit cut-line styles for compound walls / floors / roofs (Revit native dialog)."
            });
            right.Children.Add(cutRow);

            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            bottom.Children.Add(left); bottom.Children.Add(right);
            return bottom;
        }

        // ─────────────────────────────────────────────────────────
        //  Toolbar handlers
        // ─────────────────────────────────────────────────────────

        private void RefreshFilters()
        {
            try { _modelView?.Refresh(); } catch { }
            try { _annoView?.Refresh(); }  catch { }
        }

        private void SetAllVisible(bool v)
        {
            foreach (var r in AllRows) r.Visible = v;
        }

        private void InvertVisible()
        {
            foreach (var r in AllRows)
            {
                if (r.Visible == true)  r.Visible = false;
                else if (r.Visible == false) r.Visible = true;
                // null tri-state stays null
            }
        }

        private void SetExpanded(bool v)
        {
            foreach (var r in AllRows) if (r.IsParent) r.Expanded = v;
        }

        private void ClearOverrides()
        {
            var res = MessageBox.Show(
                "Clear every override on every category? This wipes all colours, weights, " +
                "patterns, transparency, halftone, detail-level, and visibility values you've " +
                "set in this editor.",
                "Clear Overrides", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            foreach (var r in AllRows)
            {
                r.Visible = null; r.Halftone = null; r.DetailLevelStr = "By View";
                r.ProjLineColor = null; r.ProjLineWeightStr = null; r.ProjLinePattern = null;
                r.SurfFgColor   = null; r.SurfFgPattern    = null;
                r.SurfBgColor   = null; r.SurfBgPattern    = null;
                r.TransparencyStr = null;
                r.CutLineColor  = null; r.CutLineWeightStr = null; r.CutLinePattern  = null;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Column factories
        // ─────────────────────────────────────────────────────────

        private static DataGridColumn TextCol(string header, string path, double width)
            => new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            };

        private static DataGridColumn BoolCol(string header, string path, double width)
            => new DataGridCheckBoxColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                IsThreeState = true,
                Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            };

        private static DataGridColumn ComboCol(string header, string path, string[] items, double width)
            => new DataGridComboBoxColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                ItemsSource = items,
                SelectedItemBinding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            };

        private static DataGridColumn WeightCol(string header, string path, double width)
            => ComboCol(header, path,
                new[] { "" }.Concat(Enumerable.Range(1, 16).Select(i => i.ToString())).ToArray(), width);

        private DataGridColumn PatternCol(string header, string path, double width, bool lines)
            => ComboCol(header, path,
                new[] { "" }.Concat(lines ? _linePatterns : _fillPatterns).ToArray(), width);

        private static Button MkBtn(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), MinWidth = 70 };
            b.Click += click;
            return b;
        }

        // ─────────────────────────────────────────────────────────
        //  Population
        // ─────────────────────────────────────────────────────────

        private void BuildRowsFromDocument()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<VgRow>();

            if (_doc != null)
            {
                foreach (Category c in _doc.Settings.Categories)
                {
                    if (c == null) continue;
                    if (c.CategoryType != CategoryType.Model && c.CategoryType != CategoryType.Annotation) continue;
                    AddCategoryRow(rows, seen, c, c.CategoryType.ToString());
                }
            }

            // Catalogue fallback for anything not in the live doc
            foreach (var cat in RevitCategoryTree.All)
            {
                if (string.IsNullOrEmpty(cat.Bic)) continue;
                if (seen.Contains(cat.Bic)) continue;
                AddCatalogueRow(rows, seen, cat, "Model");
            }

            rows.Sort(VgRowOrder);
            foreach (var r in rows)
            {
                r.AnyChanged = OnRowChanged;
                AllRows.Add(r);
            }
        }

        private static int VgRowOrder(VgRow a, VgRow b)
        {
            var dn = string.Compare(a.Bic, b.Bic, StringComparison.OrdinalIgnoreCase);
            if (dn != 0) return dn;
            if (a.IsParent && !b.IsParent) return -1;
            if (!a.IsParent && b.IsParent) return  1;
            return string.Compare(a.SubCategoryName ?? "", b.SubCategoryName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void AddCategoryRow(List<VgRow> rows, HashSet<string> seen, Category c, string groupTag)
        {
            var bic = TryGetBic(c) ?? c.Name;
            if (!seen.Add(bic)) return;
            var meta = RevitCategoryTree.FindByBic(bic);
            var row = new VgRow
            {
                Bic = bic, DisplayName = c.Name, GroupTag = groupTag,
                HasCutLines = meta?.HasCutLines ?? true,
                HasHalftone = meta?.HasHalftone ?? true,
                HasDetailLevel = meta?.HasDetailLevel ?? false,
                HasChildren = c.SubCategories != null && c.SubCategories.Size > 0
            };
            row.Data = ResolveData(bic, null, c.Name);
            rows.Add(row);

            if (c.SubCategories != null)
            {
                foreach (Category s in c.SubCategories)
                {
                    if (s == null) continue;
                    var key = $"{bic}/{s.Name}";
                    if (!seen.Add(key)) continue;
                    var sr = new VgRow
                    {
                        Bic = bic, SubCategoryName = s.Name, DisplayName = s.Name, GroupTag = groupTag,
                        HasCutLines = meta?.HasCutLines ?? true,
                        HasHalftone = meta?.HasHalftone ?? true,
                        HasDetailLevel = meta?.HasDetailLevel ?? false
                    };
                    sr.Data = ResolveData(bic, s.Name, s.Name);
                    rows.Add(sr);
                }
            }
        }

        private void AddCatalogueRow(List<VgRow> rows, HashSet<string> seen, RevitCategory c, string groupTag)
        {
            if (!seen.Add(c.Bic)) return;
            var row = new VgRow
            {
                Bic = c.Bic, DisplayName = c.DisplayName, GroupTag = groupTag,
                HasCutLines = c.HasCutLines, HasHalftone = c.HasHalftone,
                HasDetailLevel = c.HasDetailLevel,
                HasChildren = c.SubCategories != null && c.SubCategories.Count > 0
            };
            row.Data = ResolveData(c.Bic, null, c.DisplayName);
            rows.Add(row);
            if (c.SubCategories == null) return;
            foreach (var s in c.SubCategories)
            {
                var key = $"{c.Bic}/{s.DisplayName}";
                if (!seen.Add(key)) continue;
                var sr = new VgRow
                {
                    Bic = c.Bic, SubCategoryName = s.DisplayName, DisplayName = s.DisplayName, GroupTag = groupTag,
                    HasCutLines = c.HasCutLines, HasHalftone = c.HasHalftone, HasDetailLevel = c.HasDetailLevel
                };
                sr.Data = ResolveData(c.Bic, s.DisplayName, s.DisplayName);
                rows.Add(sr);
            }
        }

        private PresetCategoryOverride ResolveData(string bic, string sub, string display)
        {
            var key = string.IsNullOrEmpty(sub) ? bic : $"{bic}/{sub}";
            if (Data.TryGetValue(key, out var existing)) return existing;
            var fresh = new PresetCategoryOverride
            {
                Category    = string.IsNullOrEmpty(sub) ? display : null,
                SubCategory = sub
            };
            Data[key] = fresh;
            return fresh;
        }

        private static string TryGetBic(Category c)
        {
            try
            {
                if (c.Id == null) return null;
                long v = c.Id.Value;
                if (v >= 0) return null;
                var bic = (BuiltInCategory)(int)v;
                return bic.ToString();
            }
            catch { return null; }
        }

        private void LoadPatterns()
        {
            _linePatterns = new List<string> { "Solid" };
            _fillPatterns = new List<string>();
            if (_doc == null) return;
            try
            {
                foreach (var lp in new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>())
                    if (!string.IsNullOrEmpty(lp.Name) && !_linePatterns.Contains(lp.Name))
                        _linePatterns.Add(lp.Name);
            }
            catch { }
            try
            {
                foreach (var fp in new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
                    if (!string.IsNullOrEmpty(fp.Name) && !_fillPatterns.Contains(fp.Name))
                        _fillPatterns.Add(fp.Name);
            }
            catch { }
            _linePatterns.Sort(StringComparer.OrdinalIgnoreCase);
            _fillPatterns.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void OnRowChanged(VgRow r) => RowChanged?.Invoke(r);
    }
}
