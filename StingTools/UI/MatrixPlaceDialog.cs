// StingTools — Matrix Place dialog (M1..M7).
//
// The Excel-like control surface for Matrix Place. Two grids:
//   - Element Types (top-left): the columns/WHAT — category (allowlist) -> seed, variant,
//     anchor, mounting height (editable, named-standard or free mm), auto-grid, load VA.
//   - Rooms x Counts (centre): frozen Room Type / Rooms / Area, then one editable integer
//     count column per element type, then a live Estimated load VA column (M7). A toggle
//     switches between room-TYPE rows and per-room rows (per-room overrides).
//
// Programmatic WPF (no XAML), modelled on DwgLayerMapDialog: DataGridTemplateColumn +
// always-live controls so edits commit without reverting; SelectionMode=Extended for
// "apply to selected". Shown modally from the Placement Centre's RunInlineAction lambda
// (already on the Revit API thread), so it may open child dialogs and do API work.
//
// The dialog owns the in-memory MatrixDocument; Place/Save persist it to
// <project>/_BIM_COORD/placement_matrix.json. All Revit work runs on the calling thread.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Placement.Matrix;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Binding = System.Windows.Data.Binding;

namespace StingTools.UI
{
    public sealed class MatrixPlaceDialog : Window
    {
        private readonly UIApplication _uiapp;
        private readonly Document _doc;
        private MatrixDocument _matrix;
        private MatrixScanResult _scan;
        private readonly List<string> _categories;
        private readonly string[] _heightOptions;

        private readonly ObservableCollection<ColVm> _cols = new ObservableCollection<ColVm>();
        private readonly ObservableCollection<RowVm> _rows = new ObservableCollection<RowVm>();
        private DataGrid _colGrid;
        private DataGrid _matrixGrid;
        private TextBlock _status;
        private TextBlock _totalLoad;
        private CheckBox _perRoom;
        private CheckBox _replace;
        // The mode the CURRENTLY displayed rows are in — set by RebuildRows. SyncToMatrix must use
        // this (not _perRoom.IsChecked), because on a toggle the checkbox has already flipped to the
        // NEW mode while the rows on screen are still in the OLD one.
        private bool _viewIsPerRoom;

        public MatrixPlaceDialog(UIApplication uiapp)
        {
            _uiapp = uiapp;
            _doc = uiapp?.ActiveUIDocument?.Document;
            _matrix = MatrixStore.Load(_doc);
            _scan = MatrixRoomScanner.Scan(_doc);

            var fixtures = DwgSymbolMapRegistry.GetFixtureCategories(_doc);
            var seedable = CategoryToSeedRegistry.GetMap(_doc);
            _categories = fixtures
                .Where(c => seedable.TryGetValue(c, out var sid) && !string.IsNullOrWhiteSpace(sid))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            if (_categories.Count == 0) _categories = fixtures.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            _heightOptions = BuildHeightOptions();

            Title = "STING — Matrix Place (room x element grid)";
            Width = 1180; Height = 740;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = BuildUi();

            LoadColumnsFromMatrix();
            RebuildRows();
            RefreshStatus($"Scanned {_scan.HostRoomCount} room(s), {_scan.HostSpaceCount} space(s), {_scan.LinkedRoomCount} linked; {_scan.Types.Count} room-type(s).");
        }

        // ── UI construction ─────────────────────────────────────────────────
        private UIElement BuildUi()
        {
            var root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // actions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock
            {
                Text = "Declare WHAT + HOW MANY per room; STING places, then load is calculated.",
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
            });
            header.Children.Add(Btn("Re-scan rooms", OnRescan));
            _perRoom = new CheckBox { Content = "Per-room overrides", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            // Persist edits (in the OLD mode) before rebuilding rows in the NEW mode, else typed
            // counts vanish on toggle.
            _perRoom.Checked += (s, e) => { SyncToMatrix(); RebuildRows(); };
            _perRoom.Unchecked += (s, e) => { SyncToMatrix(); RebuildRows(); };
            header.Children.Add(_perRoom);
            Grid.SetRow(header, 0); root.Children.Add(header);

            // Body: left = element-type editor, right = matrix
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            body.Children.Add(WithLabel("Element types (the columns)", BuildColEditor(), 0));
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.LightGray };
            Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
            body.Children.Add(WithLabel("Rooms x counts", BuildMatrixGrid(), 2));

            Grid.SetRow(body, 1); root.Children.Add(body);

            // Actions
            var actions = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
            var row1 = new StackPanel { Orientation = Orientation.Horizontal };
            _replace = new CheckBox { Content = "Replace existing", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Delete previously matrix-placed instances for a (room, column) before re-placing. Off = skip already-populated cells (idempotent)." };
            row1.Children.Add(_replace);
            row1.Children.Add(Btn("Auto-suggest counts", OnAutoSuggest));
            row1.Children.Add(Btn("Preview (dry run)", OnDryRun));
            row1.Children.Add(BtnPrimary("Place", OnPlace));
            actions.Children.Add(row1);

            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row2.Children.Add(Btn("Export .xlsx", OnExport));
            row2.Children.Add(Btn("Import .xlsx", OnImport));
            row2.Children.Add(Btn("Circuit...", OnCircuit));
            row2.Children.Add(Btn("Typical floors...", OnTypicalFloors));
            row2.Children.Add(Btn("DIALux...", OnDialux));
            row2.Children.Add(Btn("Run load calc", OnLoadCalc));
            _totalLoad = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), FontWeight = FontWeights.SemiBold };
            row2.Children.Add(_totalLoad);
            actions.Children.Add(row2);

            var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            row3.Children.Add(Btn("Save matrix", OnSave));
            row3.Children.Add(Btn("Close", (s, e) => Close()));
            actions.Children.Add(row3);

            Grid.SetRow(actions, 2); root.Children.Add(actions);

            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(_status, 3); root.Children.Add(_status);
            return root;
        }

        private FrameworkElement WithLabel(string label, FrameworkElement content, int col)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock { Text = label, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(tb, 0); g.Children.Add(tb);
            Grid.SetRow(content, 1); g.Children.Add(content);
            Grid.SetColumn(g, col);
            return g;
        }

        private FrameworkElement BuildColEditor()
        {
            var panel = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Children.Add(Btn("Add element type", OnAddColumn));
            buttons.Children.Add(Btn("Remove selected", OnRemoveColumn));
            panel.Children.Add(buttons);

            _colGrid = new DataGrid
            {
                ItemsSource = _cols, AutoGenerateColumns = false, CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow
            };
            _colGrid.Columns.Add(ComboColumn("Category", nameof(ColVm.Category), _categories, 150));
            _colGrid.Columns.Add(VariantColumn(110));
            _colGrid.Columns.Add(ComboColumn("Anchor", nameof(ColVm.Anchor), MatrixDefaults.Anchors, 120));
            _colGrid.Columns.Add(HeightColumn("Height", 130));
            _colGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Grid", Binding = Tw(nameof(ColVm.AutoGrid)), Width = 40 });
            _colGrid.Columns.Add(new DataGridTextColumn { Header = "VA", Binding = Tw(nameof(ColVm.LoadVaDisplay)), Width = 55 });
            panel.Children.Add(_colGrid);
            return panel;
        }

        private FrameworkElement BuildMatrixGrid()
        {
            _matrixGrid = new DataGrid
            {
                ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
                FrozenColumnCount = 3
            };
            RebuildMatrixColumns();
            return _matrixGrid;
        }

        // Rebuild the matrix count columns to match the element-type list.
        private void RebuildMatrixColumns()
        {
            if (_matrixGrid == null) return;
            _matrixGrid.Columns.Clear();
            _matrixGrid.Columns.Add(new DataGridTextColumn { Header = "Room Type", Binding = Ow(nameof(RowVm.Label)), Width = 150, IsReadOnly = true });
            _matrixGrid.Columns.Add(new DataGridTextColumn { Header = "Rooms", Binding = Ow(nameof(RowVm.RoomsDisplay)), Width = 55, IsReadOnly = true });
            _matrixGrid.Columns.Add(new DataGridTextColumn { Header = "Area m2", Binding = Ow(nameof(RowVm.AreaDisplay)), Width = 65, IsReadOnly = true });
            for (int i = 0; i < _cols.Count; i++)
                _matrixGrid.Columns.Add(CountColumn(_cols[i].DisplayLabel, i, 70));
            _matrixGrid.Columns.Add(new DataGridTextColumn { Header = "Est VA", Binding = Ow(nameof(RowVm.EstLoadDisplay)), Width = 70, IsReadOnly = true });
        }

        // A non-reverting integer count cell bound to Cells[index].Count.
        private DataGridTemplateColumn CountColumn(string header, int index, double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(TextBox));
            factory.SetValue(TextBox.MarginProperty, new Thickness(1));
            factory.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Right);
            factory.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding($"Cells[{index}].Count")
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataGridTemplateColumn
            {
                Header = header, Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory }
            };
        }

        private DataGridTemplateColumn ComboColumn(string header, string prop, IEnumerable<string> items, double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.ItemsSourceProperty, items);
            factory.SetValue(ComboBox.IsEditableProperty, false);
            factory.SetValue(ComboBox.MarginProperty, new Thickness(1));
            factory.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding(prop)
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataGridTemplateColumn { Header = header, Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory } };
        }

        // Per-row editable variant combo: ItemsSource is the ROW's own AvailableVariants (populated
        // from the category's seed variants), so each element-type row offers its category's variants;
        // editable so a bespoke type can still be typed. Non-reverting (always-live in the cell).
        private DataGridTemplateColumn VariantColumn(double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ComboBox.MarginProperty, new Thickness(1));
            factory.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(ColVm.AvailableVariants)));
            factory.SetBinding(ComboBox.TextProperty, new System.Windows.Data.Binding(nameof(ColVm.Variant))
            { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataGridTemplateColumn { Header = "Variant", Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory } };
        }

        // Fill a column's AvailableVariants from its category's seed variants. resetSelection=true
        // (a user category change) defaults the variant to the first; on load it keeps a saved value.
        private void PopulateVariants(ColVm vm, bool resetSelection)
        {
            if (vm == null) return;
            var vs = MatrixVariants.ForCategory(_doc, vm.Category);
            vm.AvailableVariants.Clear();
            foreach (var v in vs) vm.AvailableVariants.Add(v);
            if (resetSelection || string.IsNullOrWhiteSpace(vm.Variant))
                vm.Variant = vs.FirstOrDefault() ?? (vm.Variant ?? "");
        }

        private void OnColumnCategoryChanged(ColVm vm) => PopulateVariants(vm, resetSelection: true);

        private DataGridTemplateColumn HeightColumn(string header, double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.ItemsSourceProperty, _heightOptions);
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            factory.SetValue(ComboBox.MarginProperty, new Thickness(1));
            factory.SetBinding(ComboBox.TextProperty, new System.Windows.Data.Binding(nameof(ColVm.HeightText)) { Mode = BindingMode.OneWay });
            factory.AddHandler(ComboBox.LostFocusEvent, new RoutedEventHandler(HeightCommit));
            factory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => HeightCommit(s, null)));
            return new DataGridTemplateColumn { Header = header, Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory } };
        }

        private void HeightCommit(object sender, RoutedEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.DataContext is ColVm vm)) return;
            if (TryParseHeight(cb.Text, out double mm, out string std))
            { vm.MountingHeightMm = mm; vm.HeightStandard = std; }
            cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
        }

        // ── data <-> VM sync ────────────────────────────────────────────────
        private void LoadColumnsFromMatrix()
        {
            _cols.Clear();
            foreach (var c in _matrix.Columns ?? new List<MatrixColumnDef>())
            {
                var vm = new ColVm(c, OnCellOrColChanged, OnColumnCategoryChanged);
                PopulateVariants(vm, resetSelection: false);   // keep the saved variant
                _cols.Add(vm);
            }
            if (_matrixGrid != null) RebuildMatrixColumns();
        }

        private void RebuildRows()
        {
            _rows.Clear();
            bool perRoom = _perRoom?.IsChecked == true;
            _viewIsPerRoom = perRoom;
            if (_scan?.Types == null) return;

            if (!perRoom)
            {
                foreach (var t in _scan.Types)
                {
                    var tc = EnsureType(t.Key);
                    var row = new RowVm(this, t.Key, t.PlaceableCount, t.TypicalAreaM2, false, null);
                    FillCells(row, i => tc.Cells != null && tc.Cells.TryGetValue(_cols[i].Id, out var v) ? v : 0);
                    _rows.Add(row);
                }
            }
            else
            {
                foreach (var t in _scan.Types)
                {
                    var tc = EnsureType(t.Key);
                    foreach (var r in t.Rooms.Where(x => !x.IsLinked))
                    {
                        var row = new RowVm(this, $"{r.Name}", 1, r.AreaM2, true, r.UniqueId) { TypeKey = t.Key };
                        FillCells(row, i => tc.CountFor(r.UniqueId, _cols[i].Id));
                        _rows.Add(row);
                    }
                }
            }
            RebuildMatrixColumns();
            RecomputeTotals();
        }

        private void FillCells(RowVm row, Func<int, int> get)
        {
            row.Cells.Clear();
            for (int i = 0; i < _cols.Count; i++)
                row.Cells.Add(new CellVm(get(i), row.OnCellChanged));
        }

        // Write the live VMs back into _matrix (columns + counts). Called before every action.
        private void SyncToMatrix()
        {
            // Columns
            _matrix.Columns = _cols.Select(c => c.ToDef()).ToList();

            // Counts — group-by-type view writes type.Cells; per-room view writes overrides.
            // Use the mode the DISPLAYED rows are in (not the checkbox, which may have just flipped).
            bool perRoom = _viewIsPerRoom;
            if (!perRoom)
            {
                foreach (var row in _rows)
                {
                    var tc = EnsureType(row.Label);
                    for (int i = 0; i < _cols.Count && i < row.Cells.Count; i++)
                        tc.Cells[_cols[i].Id] = Math.Max(0, row.Cells[i].Count);
                }
            }
            else
            {
                foreach (var row in _rows)
                {
                    if (string.IsNullOrEmpty(row.RoomUniqueId)) continue;
                    var tc = EnsureType(row.TypeKey ?? row.Label);
                    var ov = tc.Overrides.FirstOrDefault(o => o.RoomUniqueId == row.RoomUniqueId);
                    if (ov == null) { ov = new MatrixRoomOverride { RoomUniqueId = row.RoomUniqueId }; tc.Overrides.Add(ov); }
                    for (int i = 0; i < _cols.Count && i < row.Cells.Count; i++)
                        ov.Cells[_cols[i].Id] = Math.Max(0, row.Cells[i].Count);
                }
            }
        }

        private MatrixTypeCounts EnsureType(string key)
        {
            var tc = _matrix.Type(key);
            if (tc == null) { tc = new MatrixTypeCounts { Key = key }; _matrix.RoomTypes.Add(tc); }
            return tc;
        }

        private void OnCellOrColChanged() { RebuildMatrixColumns(); RecomputeTotals(); }

        internal void RecomputeTotals()
        {
            double grand = 0;
            foreach (var row in _rows)
            {
                double perRoom = 0;
                for (int i = 0; i < _cols.Count && i < row.Cells.Count; i++)
                    perRoom += row.Cells[i].Count * _cols[i].EffectiveVa(_doc);
                row.EstLoadVa = perRoom;
                grand += perRoom * (row.PerRoom ? 1 : row.RoomsCount);
            }
            if (_totalLoad != null)
                _totalLoad.Text = $"Estimated connected load: {grand / 1000.0:F2} kVA";
        }

        // ── actions ─────────────────────────────────────────────────────────
        private void OnRescan(object s, RoutedEventArgs e)
        {
            _scan = MatrixRoomScanner.Scan(_doc);
            RebuildRows();
            RefreshStatus($"Re-scanned: {_scan.HostRoomCount} room(s), {_scan.HostSpaceCount} space(s), {_scan.Types.Count} type(s).");
        }

        private void OnAddColumn(object s, RoutedEventArgs e)
        {
            var cat = _categories.FirstOrDefault() ?? "Lighting Fixtures";
            var def = new MatrixColumnDef { Id = _matrix.NextColumnId(), Category = cat, Anchor = MatrixDefaults.DefaultAnchor(cat, true), AutoGrid = true };
            _matrix.Columns.Add(def);
            var vm = new ColVm(def, OnCellOrColChanged, OnColumnCategoryChanged);
            PopulateVariants(vm, resetSelection: true);   // default a new column to its first seed variant
            _cols.Add(vm);
            foreach (var row in _rows) row.Cells.Add(new CellVm(0, row.OnCellChanged));
            RebuildMatrixColumns();
            RefreshStatus("Added element type. Pick its category, then set counts in the grid.");
        }

        private void OnRemoveColumn(object s, RoutedEventArgs e)
        {
            var sel = _colGrid?.SelectedItems?.OfType<ColVm>().ToList() ?? new List<ColVm>();
            if (sel.Count == 0) { RefreshStatus("Select one or more element types to remove."); return; }
            foreach (var vm in sel)
            {
                int idx = _cols.IndexOf(vm);
                if (idx < 0) continue;
                _cols.RemoveAt(idx);
                foreach (var row in _rows) if (idx < row.Cells.Count) row.Cells.RemoveAt(idx);
            }
            RebuildMatrixColumns();
            RecomputeTotals();
            RefreshStatus($"Removed {sel.Count} element type(s).");
        }

        private void OnAutoSuggest(object s, RoutedEventArgs e)
        {
            if (_cols.Count == 0) { RefreshStatus("Add an element type first."); return; }
            int set = 0;
            foreach (var row in _rows)
            {
                double area = row.AreaM2;
                for (int i = 0; i < _cols.Count && i < row.Cells.Count; i++)
                {
                    if (row.Cells[i].Count > 0) continue;  // only fill blanks — never overwrite the user
                    int sug = MatrixDefaults.SuggestCount(_doc, _cols[i].Category, area);
                    if (sug > 0) { row.Cells[i].Count = sug; set++; }
                }
            }
            RecomputeTotals();
            RefreshStatus($"Auto-suggested {set} cell(s) from density rules — tweak as needed, then Place.");
        }

        private void OnDryRun(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            var res = MatrixPlacementEngine.Place(_doc, _matrix, _scan, _replace?.IsChecked == true, dryRun: true);
            ShowResult("Matrix Place — dry run", res);
        }

        private void OnPlace(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            if (_matrix.Columns.Count == 0 || _rows.All(r => r.Cells.All(c => c.Count == 0)))
            { RefreshStatus("Nothing to place — add element types and counts first."); return; }
            var res = MatrixPlacementEngine.Place(_doc, _matrix, _scan, _replace?.IsChecked == true, dryRun: false);
            string saved = MatrixStore.Save(_doc, _matrix);
            ShowResult("Matrix Place", res);
            RefreshStatus($"Placed {res.TotalPlaced}/{res.TotalRequested}. Matrix saved{(saved == null ? " FAILED (unsaved doc?)" : "")}.");
        }

        private void OnExport(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "placement_matrix.xlsx" };
            if (dlg.ShowDialog() != true) return;
            try { MatrixExcel.Export(_doc, _matrix, _scan, dlg.FileName); RefreshStatus($"Exported to {dlg.FileName}."); }
            catch (Exception ex) { RefreshStatus($"Export failed: {ex.Message}"); }
        }

        private void OnImport(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            var res = MatrixExcel.Import(_doc, dlg.FileName, _matrix, _scan, _categories);
            LoadColumnsFromMatrix();
            RebuildRows();
            TaskDialog.Show("Matrix Import", string.Join("\n", res.Messages));
            RefreshStatus(res.Ok ? "Imported — review the grid, then Preview / Place." : "Import had issues — see dialog.");
        }

        private void OnCircuit(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            var panels = MatrixCircuiting.Panels(_doc);
            if (panels.Count == 0) { TaskDialog.Show("Circuit", "No electrical panels (OST_ElectricalEquipment) in the model."); return; }
            var pick = PickFromList("Circuit to panel", "Panel:", panels.Select(p => $"{p.Name}  (id {p.Id.Value})").ToList());
            if (pick < 0) return;
            var grouping = PickGrouping();
            if (grouping == null) return;
            var res = MatrixCircuiting.Circuit(_doc, _matrix, _scan, panels[pick].Id, grouping.Value, 5800.0);
            TaskDialog.Show("Matrix Circuit", string.Join("\n", res.Messages.Concat(res.Warnings.Take(8))));
            RefreshStatus($"Circuiting: {res.Circuits} circuit(s), {res.DevicesCircuited} device(s).");
        }

        private void OnTypicalFloors(object s, RoutedEventArgs e)
        {
            var levels = MatrixTypicalFloors.Levels(_doc);
            if (levels.Count < 2) { TaskDialog.Show("Typical floors", "Need at least two levels."); return; }
            int src = PickFromList("Source floor", "Copy placement FROM level:", levels.Select(l => l.Name).ToList());
            if (src < 0) return;
            var targets = PickMany("Target floors", "Replicate ONTO levels (Ctrl/Shift-click):", levels.Select(l => l.Name).ToList(), src);
            if (targets.Count == 0) { RefreshStatus("No target floors chosen."); return; }
            var res = MatrixTypicalFloors.Replicate(_doc, _matrix, _scan, levels[src].Id,
                targets.Select(i => levels[i].Id), circuitPerFloor: false, circuitPanelId: ElementId.InvalidElementId);
            TaskDialog.Show("Typical floors", string.Join("\n", res.Messages.Concat(res.Warnings.Take(8))));
            RefreshStatus($"Replicated to {res.TargetFloors} floor(s); {res.Copied} instance(s) copied.");
        }

        private void OnDialux(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "DIALux IFC (*.ifc)|*.ifc|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            var read = MatrixDialux.Read(_doc, dlg.FileName, _matrix, _scan);
            var withCount = read.Diffs.Where(d => d.HasCount && d.Delta != 0).ToList();
            if (!read.Ok || withCount.Count == 0)
            {
                TaskDialog.Show("DIALux", string.Join("\n", read.Messages) +
                    "\n\nNo per-room luminaire-count changes to apply.");
                return;
            }
            var sb = new StringBuilder("Apply DIALux luminaire counts?\n\n");
            foreach (var d in withCount.Take(25))
                sb.AppendLine($"  {d.RoomTypeKey}: grid {d.CurrentCount} -> DIALux {d.DialuxCount}" +
                              (d.IlluminanceLux > 0 ? $"  ({d.IlluminanceLux:F0} lux)" : ""));
            var td = new TaskDialog("DIALux diff")
            {
                MainInstruction = "Update lighting counts to the DIALux design?",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;
            int n = MatrixDialux.Apply(_doc, _matrix, withCount);
            RebuildRows();
            RefreshStatus($"Applied DIALux counts to {n} room-type(s). Review, then Place.");
        }

        private void OnLoadCalc(object s, RoutedEventArgs e)
        {
            try
            {
                StingCommandHandler.SetCurrentApp(_uiapp);
                RunCmd(new StingTools.Commands.Electrical.ElecLoadSummaryCommand());
                RunCmd(new StingTools.Commands.Electrical.Lighting.LightingPowerDensityCommand());
                RunCmd(new StingTools.Commands.Electrical.Reports.DemandFactorReportCommand());
                RefreshStatus("Ran load summary + LPD + demand-factor on the placed set. See their reports.");
            }
            catch (Exception ex) { RefreshStatus($"Load calc chain: {ex.Message}"); }
        }

        private void RunCmd(Autodesk.Revit.UI.IExternalCommand cmd)
        {
            try { string m = ""; cmd.Execute(null, ref m, new Autodesk.Revit.DB.ElementSet()); }
            catch (Exception ex) { StingLog.Warn($"MatrixPlace load-cmd {cmd.GetType().Name}: {ex.Message}"); }
        }

        private void OnSave(object s, RoutedEventArgs e)
        {
            SyncToMatrix();
            string p = MatrixStore.Save(_doc, _matrix);
            RefreshStatus(p == null ? "Save failed (document unsaved?)." : $"Matrix saved to {p}.");
        }

        // ── small helpers / pickers ─────────────────────────────────────────
        private void ShowResult(string title, MatrixPlaceResult res)
        {
            var sb = new StringBuilder();
            foreach (var m in res.Messages) sb.AppendLine(m);
            sb.AppendLine();
            foreach (var c in res.Cells.Where(c => c.Placed > 0 || c.Skipped > 0 || !string.IsNullOrEmpty(c.Note)).Take(30))
            {
                string col = _matrix.Column(c.ColumnId)?.DisplayLabel() ?? c.ColumnId;
                sb.AppendLine($"  {c.RoomName} / {col}: placed {c.Placed}/{c.Requested}" +
                              (c.Skipped > 0 ? $", skipped {c.Skipped}" : "") +
                              (string.IsNullOrEmpty(c.Note) ? "" : $"  [{c.Note}]"));
            }
            TaskDialog.Show(title, sb.ToString());
            RecomputeTotals();
        }

        private int PickFromList(string title, string prompt, List<string> items)
        {
            var dlg = new Window { Title = title, Width = 420, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this };
            var g = new Grid { Margin = new Thickness(10) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) }; Grid.SetRow(tb, 0); g.Children.Add(tb);
            var lb = new ListBox { ItemsSource = items }; Grid.SetRow(lb, 1); g.Children.Add(lb);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            int result = -1;
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
            ok.Click += (s, e) => { result = lb.SelectedIndex; dlg.DialogResult = true; };
            var cancel = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            bp.Children.Add(ok); bp.Children.Add(cancel); Grid.SetRow(bp, 2); g.Children.Add(bp);
            dlg.Content = g;
            return dlg.ShowDialog() == true ? result : -1;
        }

        private List<int> PickMany(string title, string prompt, List<string> items, int excludeIndex)
        {
            var dlg = new Window { Title = title, Width = 420, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this };
            var g = new Grid { Margin = new Thickness(10) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) }; Grid.SetRow(tb, 0); g.Children.Add(tb);
            var lb = new ListBox { SelectionMode = SelectionMode.Extended };
            for (int i = 0; i < items.Count; i++) if (i != excludeIndex) lb.Items.Add(new ListBoxItem { Content = items[i], Tag = i });
            Grid.SetRow(lb, 1); g.Children.Add(lb);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            var picked = new List<int>();
            var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
            ok.Click += (s, e) => { foreach (ListBoxItem it in lb.SelectedItems) picked.Add((int)it.Tag); dlg.DialogResult = true; };
            var cancel = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            bp.Children.Add(ok); bp.Children.Add(cancel); Grid.SetRow(bp, 2); g.Children.Add(bp);
            dlg.Content = g;
            return dlg.ShowDialog() == true ? picked : new List<int>();
        }

        private MatrixCircuitGrouping? PickGrouping()
        {
            int i = PickFromList("Circuit grouping", "Group devices into circuits by:",
                new List<string> { "Per room", "Per element type", "Fill to breaker (~32A)" });
            switch (i)
            {
                case 0: return MatrixCircuitGrouping.PerRoom;
                case 1: return MatrixCircuitGrouping.PerColumn;
                case 2: return MatrixCircuitGrouping.FillToBreaker;
                default: return null;
            }
        }

        private string[] BuildHeightOptions()
        {
            var list = new List<string>();
            try
            {
                foreach (var kv in HeightStandardsTable.All ?? new Dictionary<string, HeightStandardEntry>())
                    list.Add($"{kv.Key} - {kv.Value.PreferredMm:F0}mm ({kv.Value.Standard})");
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }

        private bool TryParseHeight(string text, out double mm, out string standard)
        {
            mm = 0; standard = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            // "KEY - NNNmm (Standard)"
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            string key = dash > 0 ? t.Substring(0, dash).Trim() : t;
            var entry = HeightStandardsTable.Get(key);
            if (entry != null) { mm = entry.PreferredMm; standard = key; return true; }
            // bare number, tolerating "mm"
            string num = new string(t.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0)
            { mm = v; standard = ""; return true; }
            return false;
        }

        private void RefreshStatus(string msg) { if (_status != null) _status.Text = msg; }

        private Button Btn(string text, RoutedEventHandler click)
        { var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) }; b.Click += click; return b; }
        private Button BtnPrimary(string text, RoutedEventHandler click)
        { var b = Btn(text, click); b.FontWeight = FontWeights.Bold; return b; }

        private static System.Windows.Data.Binding Tw(string p) => new System.Windows.Data.Binding(p) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        private static System.Windows.Data.Binding Ow(string p) => new System.Windows.Data.Binding(p) { Mode = BindingMode.OneWay };

        // ── view-models ─────────────────────────────────────────────────────
        internal sealed class CellVm : INotifyPropertyChanged
        {
            private int _count;
            private readonly Action _onChanged;
            public CellVm(int count, Action onChanged) { _count = count; _onChanged = onChanged; }
            public int Count
            {
                get => _count;
                set { if (_count != value) { _count = value < 0 ? 0 : value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count))); _onChanged?.Invoke(); } }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        internal sealed class RowVm : INotifyPropertyChanged
        {
            private readonly MatrixPlaceDialog _owner;
            public RowVm(MatrixPlaceDialog owner, string label, int roomsCount, double areaM2, bool perRoom, string roomUid)
            { _owner = owner; Label = label; RoomsCount = roomsCount; AreaM2 = areaM2; PerRoom = perRoom; RoomUniqueId = roomUid; }
            public string Label { get; }
            public string TypeKey { get; set; }
            public int RoomsCount { get; }
            public double AreaM2 { get; }
            public bool PerRoom { get; }
            public string RoomUniqueId { get; }
            public ObservableCollection<CellVm> Cells { get; } = new ObservableCollection<CellVm>();
            public string RoomsDisplay => PerRoom ? "1" : RoomsCount.ToString();
            public string AreaDisplay => AreaM2.ToString("F1");
            private double _estLoad;
            public double EstLoadVa { get => _estLoad; set { _estLoad = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstLoadDisplay))); } }
            public string EstLoadDisplay => _estLoad <= 0 ? "-" : _estLoad.ToString("F0");
            public void OnCellChanged() { _owner?.RecomputeTotals(); }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        internal sealed class ColVm : INotifyPropertyChanged
        {
            private readonly Action _onChanged;
            private readonly Action<ColVm> _onCategoryChanged;
            public ColVm(MatrixColumnDef def, Action onChanged, Action<ColVm> onCategoryChanged)
            {
                _onChanged = onChanged;
                _onCategoryChanged = onCategoryChanged;
                Id = def.Id; _category = def.Category; _variant = def.Variant; _anchor = def.Anchor;
                MountingHeightMm = def.MountingHeightMm; HeightStandard = def.HeightStandard;
                AutoGrid = def.AutoGrid; LoadVaOverride = def.LoadVaOverride; Label = def.Label;
            }
            public string Id { get; }
            public string Label { get; set; }
            private string _category;
            public string Category { get => _category; set { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel))); _onCategoryChanged?.Invoke(this); _onChanged?.Invoke(); } }
            public ObservableCollection<string> AvailableVariants { get; } = new ObservableCollection<string>();
            private string _variant = "";
            public string Variant { get => _variant; set { if (_variant != value) { _variant = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Variant))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel))); } } }
            private string _anchor;
            public string Anchor { get => _anchor; set { _anchor = value; } }
            public double MountingHeightMm { get; set; }
            public string HeightStandard { get; set; } = "";
            public bool AutoGrid { get; set; } = true;
            public double LoadVaOverride { get; set; }
            public string HeightText => MountingHeightMm > 0
                ? (string.IsNullOrEmpty(HeightStandard) ? $"{MountingHeightMm:F0} mm" : $"{HeightStandard} - {MountingHeightMm:F0}mm")
                : "(default)";
            public string LoadVaDisplay { get => LoadVaOverride > 0 ? LoadVaOverride.ToString("F0") : ""; set { if (double.TryParse(value, out var v)) { LoadVaOverride = v; _onChanged?.Invoke(); } } }
            public string DisplayLabel => string.IsNullOrWhiteSpace(Variant) ? (Category ?? "") : $"{Category} ({Variant})";
            public double EffectiveVa(Document doc) => LoadVaOverride > 0 ? LoadVaOverride : MatrixDefaults.LoadVa(doc, Category);
            public MatrixColumnDef ToDef() => new MatrixColumnDef
            {
                Id = Id, Category = Category, Variant = Variant ?? "",
                Anchor = string.IsNullOrWhiteSpace(Anchor) ? MatrixDefaults.DefaultAnchor(Category, AutoGrid) : Anchor,
                MountingHeightMm = MountingHeightMm, HeightStandard = HeightStandard ?? "",
                AutoGrid = AutoGrid, LoadVaOverride = LoadVaOverride, Label = Label ?? ""
            };
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
