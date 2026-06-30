// ============================================================================
// DwgLayerMapDialog.cs — Map DWG layers to STING categories (Placement Centre).
//
// Lists the layers actually present in the resolved DWG import (from
// CADExtractionResult.LayerCounts) and lets the user map each to a STING
// placement category + optional variant hint + host anchor. Pre-filled from the
// existing resolution chain (LayerMapper / DWG symbol map). Save writes ByLayer
// rules to <project>/_BIM_COORD/dwg_symbol_map.json (the EXISTING override the
// bridge reads) — the caller does the write via DwgSymbolMapRegistry.
//
// Programmatic WPF (no XAML), modelled on MepCadWizard. ASCII-only.
//
// Editing: the STING-category and Anchor columns use DataGridTemplateColumn with
// an always-live ComboBox CellTemplate so a selection commits immediately on
// change (the previous DataGridComboBoxColumn + SelectedItemBinding snapped the
// value back to the prefill). Multi-select (Extended / FullRow) + a bulk
// "Apply to selected" toolbar and "Skip all" / "Reset to detected" buttons let
// the user map many layers fast.
// ============================================================================
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    public class DwgLayerMapDialog : Window
    {
        private readonly ObservableCollection<DwgLayerMapRow> _rows = new ObservableCollection<DwgLayerMapRow>();
        private DataGrid _grid;

        public bool Confirmed { get; private set; }
        /// <summary>The (possibly edited) rows — read after a confirmed close.</summary>
        public IReadOnlyList<DwgLayerMapRow> Rows => _rows;

        public const string SkipOption = "(skip)";
        private const string DefaultAnchor = "WALL_MIDPOINT";
        private static readonly string[] Anchors =
            { "WALL_MIDPOINT", "WALL_FACE_OFFSET", "CEILING_CENTRE", "ROOM_CENTRE" };

        /// <param name="categories">STING categories to offer (seed-mappable).</param>
        /// <param name="seedRows">layer, entity count, pre-filled category/variant/anchor.</param>
        public DwgLayerMapDialog(IEnumerable<string> categories,
            IEnumerable<(string Layer, int Count, string Category, string Variant, string Anchor)> seedRows)
        {
            var cats = new List<string> { SkipOption };
            cats.AddRange((categories ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)));

            foreach (var r in (seedRows ?? Enumerable.Empty<(string, int, string, string, string)>())
                         .OrderByDescending(r => r.Count))
            {
                string cat = string.IsNullOrWhiteSpace(r.Category) ? SkipOption : r.Category;
                string anchor = string.IsNullOrWhiteSpace(r.Anchor) ? DefaultAnchor : r.Anchor;
                string variant = r.Variant ?? "";
                _rows.Add(new DwgLayerMapRow
                {
                    Layer = r.Layer ?? "",
                    Count = r.Count,
                    Category = cat,
                    Variant = variant,
                    Anchor = anchor,
                    // Snapshot the auto-detected values so "Reset to detected" can restore them.
                    DetectedCategory = cat,
                    DetectedVariant = variant,
                    DetectedAnchor = anchor
                });
            }

            Title = "STING — Map DWG Layers to STING categories";
            Width = 800; Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildLayout(cats);
        }

        private UIElement BuildLayout(List<string> categories)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // intro
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // bulk toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // buttons

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = "Map each DWG layer to the STING category it represents. Category " + SkipOption +
                       " leaves the layer unmapped. Variant is optional (e.g. SOCKET_2G, DOWNLIGHT); " +
                       "Anchor picks the host search (wall vs ceiling). Pre-filled from the auto-detector — " +
                       "correct as needed. Ctrl/Shift-click to select several layers, then 'Apply to selected'. " +
                       "Saved to this project's _BIM_COORD override; the corporate map is untouched."
            };
            Grid.SetRow(intro, 0); root.Children.Add(intro);

            // ── Bulk toolbar (B4): category + anchor combo + Apply to selected. ──
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(new TextBlock { Text = "Set selected to:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var bulkCat = new ComboBox { ItemsSource = categories, Width = 200, IsEditable = false, VerticalAlignment = VerticalAlignment.Center };
            bulkCat.SelectedIndex = categories.Count > 1 ? 1 : 0;   // first real category, else (skip)
            toolbar.Children.Add(bulkCat);
            toolbar.Children.Add(new TextBlock { Text = "Anchor:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
            var bulkAnchor = new ComboBox { ItemsSource = Anchors, Width = 150, IsEditable = false, VerticalAlignment = VerticalAlignment.Center };
            bulkAnchor.SelectedItem = DefaultAnchor;
            toolbar.Children.Add(bulkAnchor);
            var applySel = new Button { Content = "Apply to selected", Width = 130, Margin = new Thickness(10, 0, 0, 0) };
            applySel.Click += (s, e) =>
            {
                var cat = bulkCat.SelectedItem as string ?? SkipOption;
                var anchor = bulkAnchor.SelectedItem as string ?? DefaultAnchor;
                var selected = _grid?.SelectedItems?.OfType<DwgLayerMapRow>().ToList() ?? new List<DwgLayerMapRow>();
                if (selected.Count == 0) { ToastInline("Select one or more layers first (Ctrl/Shift-click)."); return; }
                foreach (var row in selected) { row.Category = cat; row.Anchor = anchor; }
            };
            toolbar.Children.Add(applySel);
            Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);

            _grid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = "DWG layer", Binding = new Binding("Layer") { Mode = BindingMode.OneWay }, Width = 260, IsReadOnly = true });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Entities", Binding = new Binding("Count") { Mode = BindingMode.OneWay }, Width = 70, IsReadOnly = true });
            _grid.Columns.Add(MakeComboColumn("STING category", "Category", categories, 200));
            _grid.Columns.Add(new DataGridTextColumn { Header = "Variant hint", Binding = new Binding("Variant") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 130 });
            _grid.Columns.Add(MakeComboColumn("Anchor", "Anchor", Anchors, 150));
            Grid.SetRow(_grid, 2); root.Children.Add(_grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };

            var skipAll = new Button { Content = "Skip all", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            skipAll.Click += (s, e) => { foreach (var r in _rows) r.Category = SkipOption; };
            buttons.Children.Add(skipAll);

            var reset = new Button { Content = "Reset to detected", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += (s, e) =>
            {
                foreach (var r in _rows) { r.Category = r.DetectedCategory; r.Variant = r.DetectedVariant; r.Anchor = r.DetectedAnchor; }
            };
            buttons.Children.Add(reset);

            var save = new Button { Content = "Save mappings", Width = 130, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            save.Click += (s, e) => { Confirmed = true; DialogResult = true; Close(); };
            buttons.Children.Add(save);

            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 3); root.Children.Add(buttons);

            return root;
        }

        /// <summary>An always-live ComboBox column. CellTemplate (not CellEditingTemplate)
        /// means the combo is rendered in every cell, so a selection commits on change with
        /// no edit-mode dance — TwoWay/PropertyChanged binding writes straight to the row.</summary>
        private static DataGridTemplateColumn MakeComboColumn(string header, string boundProp, IEnumerable<string> items, double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.ItemsSourceProperty, items);
            factory.SetValue(ComboBox.IsEditableProperty, false);
            factory.SetValue(ComboBox.MarginProperty, new Thickness(1));
            factory.SetBinding(ComboBox.SelectedItemProperty,
                new Binding(boundProp) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory }
            };
        }

        private void ToastInline(string msg)
        {
            try { Title = "STING — Map DWG Layers — " + msg; } catch { }
        }
    }

    public class DwgLayerMapRow : INotifyPropertyChanged
    {
        public string Layer { get; set; } = "";
        public int Count { get; set; }

        private string _category = DwgLayerMapDialog.SkipOption;
        public string Category { get => _category; set { _category = value; OnChanged(nameof(Category)); } }

        private string _variant = "";
        public string Variant { get => _variant; set { _variant = value; OnChanged(nameof(Variant)); } }

        private string _anchor = "WALL_MIDPOINT";
        public string Anchor { get => _anchor; set { _anchor = value; OnChanged(nameof(Anchor)); } }

        // Auto-detected snapshot (set at construction) for "Reset to detected".
        public string DetectedCategory { get; set; } = DwgLayerMapDialog.SkipOption;
        public string DetectedVariant { get; set; } = "";
        public string DetectedAnchor { get; set; } = "WALL_MIDPOINT";

        /// <summary>True when the user actually mapped this layer (not "(skip)").</summary>
        public bool IsMapped => !string.IsNullOrWhiteSpace(Category) && Category != DwgLayerMapDialog.SkipOption;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
