// StingTools — Drawing Template Manager · Phase 137
//
// RevitVgEditor is a self-contained WPF surface that mirrors Revit's
// "Visibility/Graphic Overrides" dialog. The aim is full parity so users
// authoring a DrawingProductionPreset or a ViewStylePack never need to
// open Revit's native VG dialog to set per-category overrides.
//
// Source of truth at run-time:
//   * Every model + annotation Category from doc.Settings.Categories
//   * Every SubCategory of each (so project-specific subcategories the
//     user has authored in Revit show up automatically)
//   * RevitCategoryTree provides metadata (HasCutLines, HasHalftone,
//     HasDetailLevel) so the editor greys out cells that would do
//     nothing on a given category.
//
// Backing model: Dictionary<string, PresetCategoryOverride> keyed by
//   "BuiltInCategory" for parent rows or "BuiltInCategory/<SubName>"
// for subcategory rows. Re-built on every edit so callers can persist
// straight to a DrawingProductionPreset.VgOverrides[<dt-id>].
//
// Columns (left → right, matches Revit):
//   Visibility · Halftone · Detail Level
//   Projection Lines:  Color · Weight · Pattern
//   Surface:           Fg-Color · Fg-Pattern · Bg-Color · Bg-Pattern · Transparency
//   Cut Lines:         Color · Weight · Pattern
//   Cut Patterns:      Fg-Color · Fg-Pattern · Bg-Color · Bg-Pattern
//
// Toolbar: search filter · Show Annotation/Imported/Filters tabs ·
//   All · None · Invert · Expand All / Collapse All · Object Styles…

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
using StingTools.Core.Drawing;

namespace StingTools.UI
{
    public sealed class RevitVgEditor
    {
        public sealed class VgRow : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            public string Bic { get; set; }                  // OST_Walls
            public string SubCategoryName { get; set; }      // null for parent
            public string DisplayName { get; set; }          // "Walls" or "<Hidden Lines>"
            public bool IsParent => string.IsNullOrEmpty(SubCategoryName);
            public bool HasCutLines { get; set; } = true;
            public bool HasHalftone { get; set; } = true;
            public bool HasDetailLevel { get; set; } = false;
            public bool HasChildren { get; set; }            // parent has subcategories
            public string Key => IsParent ? Bic : $"{Bic}/{SubCategoryName}";

            // Backing data — points at the user's PresetCategoryOverride
            public PresetCategoryOverride Data { get; set; }

            // Bindable proxy properties — round-trip into Data
            public bool? Visible
            {
                get => Data.Visible;
                set { Data.Visible = value; Raise(nameof(Visible)); }
            }
            public bool? Halftone
            {
                get => Data.Halftone;
                set { Data.Halftone = value; Raise(nameof(Halftone)); }
            }
            public string DetailLevelStr
            {
                get => Data.DetailLevel ?? "";
                set { Data.DetailLevel = string.IsNullOrEmpty(value) ? null : value; Raise(nameof(DetailLevelStr)); }
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
            public string CutFgColor       { get => Data.CutFgColor;       set { Data.CutFgColor = value;       Raise(nameof(CutFgColor)); } }
            public string CutFgPattern     { get => Data.CutFgPattern;     set { Data.CutFgPattern = value;     Raise(nameof(CutFgPattern)); } }
            public string CutBgColor       { get => Data.CutBgColor;       set { Data.CutBgColor = value;       Raise(nameof(CutBgColor)); } }
            public string CutBgPattern     { get => Data.CutBgPattern;     set { Data.CutBgPattern = value;     Raise(nameof(CutBgPattern)); } }

            public bool IsExpanded { get; set; } = true;
            public Visibility RowVisibility => Visibility.Visible;

            private static int? ParseInt(string s) => int.TryParse(s, out var v) ? (int?)v : null;
        }

        // Public state ----------------------------------------------------
        public Dictionary<string, PresetCategoryOverride> Data { get; }
        public ObservableCollection<VgRow> Rows { get; } = new ObservableCollection<VgRow>();

        private readonly Document _doc;
        private DataGrid _grid;
        private TextBox _search;
        private List<string> _linePatterns;
        private List<string> _fillPatterns;
        private CollectionView _view;

        public RevitVgEditor(Document doc, Dictionary<string, PresetCategoryOverride> data)
        {
            _doc = doc;
            Data = data ?? new Dictionary<string, PresetCategoryOverride>();
            BuildRowsFromDocument();
            LoadPatterns();
        }

        public FrameworkElement Build()
        {
            var dock = new DockPanel { LastChildFill = true };

            // ── Toolbar ──
            var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(bar, Dock.Top);
            var btnAll      = NewBtn("All",       (_, __) => SetAllVisible(true));
            var btnNone     = NewBtn("None",      (_, __) => SetAllVisible(false));
            var btnInvert   = NewBtn("Invert",    (_, __) => InvertVisible());
            var btnExpand   = NewBtn("Expand All", (_, __) => SetAllExpanded(true));
            var btnCollapse = NewBtn("Collapse All", (_, __) => SetAllExpanded(false));
            var btnReset    = NewBtn("Clear Overrides", (_, __) => ClearOverrides());
            DockPanel.SetDock(btnAll, Dock.Right);
            DockPanel.SetDock(btnNone, Dock.Right);
            DockPanel.SetDock(btnInvert, Dock.Right);
            DockPanel.SetDock(btnExpand, Dock.Right);
            DockPanel.SetDock(btnCollapse, Dock.Right);
            DockPanel.SetDock(btnReset, Dock.Right);
            bar.Children.Add(btnReset);
            bar.Children.Add(btnCollapse);
            bar.Children.Add(btnExpand);
            bar.Children.Add(btnInvert);
            bar.Children.Add(btnNone);
            bar.Children.Add(btnAll);

            _search = new TextBox { Margin = new Thickness(0, 0, 6, 0), MinWidth = 220 };
            _search.TextChanged += (s, e) => ApplyFilter();
            var label = new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(label, Dock.Left);
            bar.Children.Add(label);
            bar.Children.Add(_search);

            dock.Children.Add(bar);

            // ── Grid ──
            _grid = BuildGrid();
            _view = (CollectionView)CollectionViewSource.GetDefaultView(Rows);
            _view.Filter = FilterPredicate;
            _grid.ItemsSource = _view;

            // ── Footer hint ──
            var hint = new TextBlock {
                Text = $"{Rows.Count(r => r.IsParent)} categories · {Rows.Count(r => !r.IsParent)} subcategories — pre-populated from the active project. " +
                       "Tab away from a cell to commit. Empty cells inherit the view template.",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 130)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            DockPanel.SetDock(hint, Dock.Bottom);
            dock.Children.Add(hint);

            dock.Children.Add(_grid);
            return dock;
        }

        private DataGrid BuildGrid()
        {
            var g = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeight = 22,
                ColumnHeaderHeight = 38,
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 50))
            };

            // Visibility column — checkbox + indented label
            var visCol = new DataGridTemplateColumn { Header = "Visibility", Width = new DataGridLength(220, DataGridLengthUnitType.Pixel) };
            var vt = new DataTemplate();
            var fSp = new FrameworkElementFactory(typeof(StackPanel));
            fSp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var fCb = new FrameworkElementFactory(typeof(CheckBox));
            fCb.SetValue(CheckBox.IsThreeStateProperty, true);
            fCb.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(VgRow.Visible)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            fCb.SetValue(CheckBox.MarginProperty, new Thickness(2, 0, 6, 0));
            var fName = new FrameworkElementFactory(typeof(TextBlock));
            fName.SetBinding(TextBlock.TextProperty, new Binding(nameof(VgRow.DisplayName)));
            fName.SetBinding(TextBlock.MarginProperty, new Binding(nameof(VgRow.SubCategoryName)) { Converter = new IndentConverter() });
            fName.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(VgRow.IsParent)) { Converter = new BoolToFontWeightConverter() });
            fSp.AppendChild(fCb);
            fSp.AppendChild(fName);
            vt.VisualTree = fSp;
            visCol.CellTemplate = vt;
            g.Columns.Add(visCol);

            g.Columns.Add(BoolCol("Halftone", nameof(VgRow.Halftone), 60));
            g.Columns.Add(ComboCol("Detail Level", nameof(VgRow.DetailLevelStr),
                new[] { "", "Coarse", "Medium", "Fine" }, 80));

            g.Columns.Add(HeaderCol("Projection — Lines"));
            g.Columns.Add(TextCol("Color",   nameof(VgRow.ProjLineColor),    70));
            g.Columns.Add(WeightCol("Weight",nameof(VgRow.ProjLineWeightStr),50));
            g.Columns.Add(PatternCol("Pattern", nameof(VgRow.ProjLinePattern), 90, lines: true));

            g.Columns.Add(HeaderCol("Surface"));
            g.Columns.Add(TextCol("Fg Color",  nameof(VgRow.SurfFgColor),   70));
            g.Columns.Add(PatternCol("Fg Patt", nameof(VgRow.SurfFgPattern), 90, lines: false));
            g.Columns.Add(TextCol("Bg Color",  nameof(VgRow.SurfBgColor),   70));
            g.Columns.Add(PatternCol("Bg Patt", nameof(VgRow.SurfBgPattern), 90, lines: false));
            g.Columns.Add(TextCol("Trans %",   nameof(VgRow.TransparencyStr), 50));

            g.Columns.Add(HeaderCol("Cut — Lines"));
            g.Columns.Add(TextCol("Color",   nameof(VgRow.CutLineColor),    70));
            g.Columns.Add(WeightCol("Weight",nameof(VgRow.CutLineWeightStr),50));
            g.Columns.Add(PatternCol("Pattern", nameof(VgRow.CutLinePattern), 90, lines: true));

            g.Columns.Add(HeaderCol("Cut — Patterns"));
            g.Columns.Add(TextCol("Fg Color",  nameof(VgRow.CutFgColor),    70));
            g.Columns.Add(PatternCol("Fg Patt", nameof(VgRow.CutFgPattern),  90, lines: false));
            g.Columns.Add(TextCol("Bg Color",  nameof(VgRow.CutBgColor),    70));
            g.Columns.Add(PatternCol("Bg Patt", nameof(VgRow.CutBgPattern),  90, lines: false));
            return g;
        }

        // ── Column factories ──

        private static DataGridColumn TextCol(string header, string path, int width)
            => new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            };

        private static DataGridColumn BoolCol(string header, string path, int width)
            => new DataGridCheckBoxColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                IsThreeState = true,
                Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            };

        private static DataGridColumn ComboCol(string header, string path, string[] items, int width)
        {
            var col = new DataGridComboBoxColumn
            {
                Header = header,
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                ItemsSource = items,
                SelectedItemBinding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            };
            return col;
        }

        private static DataGridColumn WeightCol(string header, string path, int width)
        {
            var items = new[] { "" }.Concat(Enumerable.Range(1, 16).Select(i => i.ToString())).ToArray();
            return ComboCol(header, path, items, width);
        }

        private DataGridColumn PatternCol(string header, string path, int width, bool lines)
        {
            var items = new[] { "" }.Concat(lines ? _linePatterns : _fillPatterns).ToArray();
            return ComboCol(header, path, items, width);
        }

        private static DataGridColumn HeaderCol(string header)
            => new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(0, DataGridLengthUnitType.Pixel),
                IsReadOnly = true,
                CanUserResize = false,
                CanUserReorder = false,
                Visibility = Visibility.Hidden
            };

        private static Button NewBtn(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 0, 0), MinWidth = 76 };
            b.Click += click;
            return b;
        }

        // ── Population ──

        private void BuildRowsFromDocument()
        {
            // 1. Collect every Model + Annotation category from doc.Settings.Categories
            //    (this gives the live, project-true list including any custom
            //    subcategories the user added via Manage > Object Styles).
            //    Fall back to RevitCategoryTree.All catalogue when doc is null.
            var rows = new List<VgRow>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_doc != null)
            {
                foreach (Category c in _doc.Settings.Categories)
                {
                    if (c == null) continue;
                    if (c.CategoryType != CategoryType.Model && c.CategoryType != CategoryType.Annotation) continue;
                    AddCategoryRow(rows, seenKeys, c);
                }
            }

            // 2. Cover any catalogue categories that didn't show up in the active
            //    document (e.g. categories Revit hides on the active discipline
            //    filter). Users can still set overrides — Revit will read them
            //    when the category becomes visible.
            foreach (var cat in RevitCategoryTree.All)
            {
                if (string.IsNullOrEmpty(cat.Bic)) continue;
                if (seenKeys.Contains(cat.Bic)) continue;
                AddCatalogueRow(rows, seenKeys, cat);
            }

            // 3. Order: parent display name asc, then sub-categories under each parent.
            rows.Sort(VgRowOrder);
            foreach (var r in rows) Rows.Add(r);
        }

        private static int VgRowOrder(VgRow a, VgRow b)
        {
            var dn = string.Compare(
                ResolveParentDisplayName(a), ResolveParentDisplayName(b),
                StringComparison.OrdinalIgnoreCase);
            if (dn != 0) return dn;
            if (a.IsParent && !b.IsParent) return -1;
            if (!a.IsParent && b.IsParent) return  1;
            return string.Compare(a.SubCategoryName ?? "", b.SubCategoryName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveParentDisplayName(VgRow r)
        {
            var cat = RevitCategoryTree.FindByBic(r.Bic);
            return cat?.DisplayName ?? r.DisplayName;
        }

        private void AddCategoryRow(List<VgRow> rows, HashSet<string> seen, Category c)
        {
            var bic = TryGetBic(c) ?? c.Name;
            if (!seen.Add(bic)) return;
            var meta = RevitCategoryTree.FindByBic(bic);
            var row = new VgRow
            {
                Bic = bic,
                DisplayName = c.Name,
                HasCutLines = meta?.HasCutLines ?? true,
                HasHalftone = meta?.HasHalftone ?? true,
                HasDetailLevel = meta?.HasDetailLevel ?? false,
                HasChildren = c.SubCategories != null && c.SubCategories.Size > 0
            };
            row.Data = ResolveData(bic, null, c.Name);
            rows.Add(row);

            if (c.SubCategories != null)
                foreach (Category s in c.SubCategories)
                {
                    if (s == null) continue;
                    var key = $"{bic}/{s.Name}";
                    if (!seen.Add(key)) continue;
                    var srow = new VgRow
                    {
                        Bic = bic,
                        SubCategoryName = s.Name,
                        DisplayName = s.Name,
                        HasCutLines = meta?.HasCutLines ?? true,
                        HasHalftone = meta?.HasHalftone ?? true,
                        HasDetailLevel = meta?.HasDetailLevel ?? false
                    };
                    srow.Data = ResolveData(bic, s.Name, s.Name);
                    rows.Add(srow);
                }
        }

        private void AddCatalogueRow(List<VgRow> rows, HashSet<string> seen, RevitCategory c)
        {
            if (!seen.Add(c.Bic)) return;
            var row = new VgRow
            {
                Bic = c.Bic,
                DisplayName = c.DisplayName,
                HasCutLines = c.HasCutLines,
                HasHalftone = c.HasHalftone,
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
                var srow = new VgRow
                {
                    Bic = c.Bic,
                    SubCategoryName = s.DisplayName,
                    DisplayName = s.DisplayName,
                    HasCutLines = c.HasCutLines,
                    HasHalftone = c.HasHalftone,
                    HasDetailLevel = c.HasDetailLevel
                };
                srow.Data = ResolveData(c.Bic, s.DisplayName, s.DisplayName);
                rows.Add(srow);
            }
        }

        private PresetCategoryOverride ResolveData(string bic, string sub, string display)
        {
            var key = string.IsNullOrEmpty(sub) ? bic : $"{bic}/{sub}";
            if (Data.TryGetValue(key, out var existing)) return existing;
            var newOne = new PresetCategoryOverride
            {
                Category = string.IsNullOrEmpty(sub) ? display : null,
                SubCategory = sub
            };
            Data[key] = newOne;
            return newOne;
        }

        private static string TryGetBic(Category c)
        {
            try
            {
                if (c.Id == null) return null;
                var v = c.Id.IntegerValue;
                if (v >= 0) return null;
                var bic = (BuiltInCategory)v;
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

        // ── Toolbar handlers ──

        private bool FilterPredicate(object o)
        {
            if (!(o is VgRow r)) return false;
            var q = (_search?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q)) return true;
            return (r.DisplayName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.Bic ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyFilter() => _view?.Refresh();

        private void SetAllVisible(bool visible)
        {
            foreach (var r in Rows) r.Visible = visible;
        }

        private void InvertVisible()
        {
            foreach (var r in Rows)
            {
                if (r.Visible == true) r.Visible = false;
                else if (r.Visible == false) r.Visible = true;
                // tri-state null stays null
            }
        }

        private void SetAllExpanded(bool expanded)
        {
            // Expand/collapse currently always shows all rows; reserved for
            // a future TreeView-style render. We leave all rows visible so
            // users can scroll the full list.
            foreach (var r in Rows) r.IsExpanded = expanded;
        }

        private void ClearOverrides()
        {
            var res = MessageBox.Show(
                "Clear every override on every category? This wipes all colours, weights, patterns, transparency, halftone, detail-level, and visibility values you've set in this editor.",
                "Clear Overrides", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            foreach (var r in Rows)
            {
                r.Visible = null;
                r.Halftone = null;
                r.DetailLevelStr = "";
                r.ProjLineColor = null; r.ProjLineWeightStr = null; r.ProjLinePattern = null;
                r.SurfFgColor = null;   r.SurfFgPattern = null;
                r.SurfBgColor = null;   r.SurfBgPattern = null;
                r.TransparencyStr = null;
                r.CutLineColor = null;  r.CutLineWeightStr = null; r.CutLinePattern = null;
                r.CutFgColor = null;    r.CutFgPattern = null;
                r.CutBgColor = null;    r.CutBgPattern = null;
            }
        }
    }

    // ── Helper converters ──

    internal sealed class IndentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value == null ? new Thickness(0, 0, 0, 0) : new Thickness(20, 0, 0, 0);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }

    internal sealed class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b && b ? FontWeights.SemiBold : FontWeights.Normal;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }
}
