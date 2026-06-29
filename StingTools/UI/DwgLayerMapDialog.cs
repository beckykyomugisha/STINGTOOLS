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

        public bool Confirmed { get; private set; }
        /// <summary>The (possibly edited) rows — read after a confirmed close.</summary>
        public IReadOnlyList<DwgLayerMapRow> Rows => _rows;

        public const string SkipOption = "(skip)";
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
                _rows.Add(new DwgLayerMapRow
                {
                    Layer = r.Layer ?? "",
                    Count = r.Count,
                    Category = string.IsNullOrWhiteSpace(r.Category) ? SkipOption : r.Category,
                    Variant = r.Variant ?? "",
                    Anchor = string.IsNullOrWhiteSpace(r.Anchor) ? "WALL_MIDPOINT" : r.Anchor
                });

            Title = "STING — Map DWG Layers to STING categories";
            Width = 760; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildLayout(cats);
        }

        private UIElement BuildLayout(List<string> categories)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = "Map each DWG layer to the STING category it represents. Category " + SkipOption +
                       " leaves the layer unmapped. Variant is optional (e.g. SOCKET_2G, DOWNLIGHT); " +
                       "Anchor picks the host search (wall vs ceiling). Pre-filled from the auto-detector — " +
                       "correct as needed. Saved to this project's _BIM_COORD override; the corporate map is untouched."
            };
            Grid.SetRow(intro, 0); root.Children.Add(intro);

            var grid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "DWG layer", Binding = new Binding("Layer") { Mode = BindingMode.OneWay }, Width = 260, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "Entities", Binding = new Binding("Count") { Mode = BindingMode.OneWay }, Width = 70, IsReadOnly = true });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "STING category", SelectedItemBinding = new Binding("Category") { Mode = BindingMode.TwoWay }, Width = 200, ItemsSource = categories });
            grid.Columns.Add(new DataGridTextColumn { Header = "Variant hint", Binding = new Binding("Variant") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 130 });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "Anchor", SelectedItemBinding = new Binding("Anchor") { Mode = BindingMode.TwoWay }, Width = 150, ItemsSource = Anchors });
            Grid.SetRow(grid, 1); root.Children.Add(grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var save = new Button { Content = "Save mappings", Width = 130, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            save.Click += (s, e) => { Confirmed = true; DialogResult = true; Close(); };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(save); buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2); root.Children.Add(buttons);

            return root;
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

        /// <summary>True when the user actually mapped this layer (not "(skip)").</summary>
        public bool IsMapped => !string.IsNullOrWhiteSpace(Category) && Category != DwgLayerMapDialog.SkipOption;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
