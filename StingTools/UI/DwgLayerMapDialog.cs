// ============================================================================
// DwgLayerMapDialog.cs — Map DWG layers to STING categories (Placement Centre).
//
// Lists the layers actually present in the resolved DWG import (from
// CADExtractionResult.LayerCounts) and lets the user map each to a STING
// placement category + optional variant hint + host anchor + mounting height.
// Pre-filled from the existing resolution chain (LayerMapper / DWG symbol map)
// and the per-category height defaults. Save writes ByLayer rules to
// <project>/_BIM_COORD/dwg_symbol_map.json (the EXISTING override the bridge
// reads) — the caller does the write via DwgSymbolMapRegistry.
//
// F1 — an "Import:" dropdown at the top lists EVERY detected import
// (<name> . id <ElementId> . [Import|Link] . view <owner view or "model">),
// newest first. Switching it re-runs the caller-supplied repopulate callback
// against the chosen import, so the grid AND the eventual place run target the
// SAME import (the chosen ImportInstance is exposed via SelectedImport). If the
// expected DWG is a Revit link / nested xref / import-in-another-view, the
// dropdown makes that VISIBLE instead of silently substituting.
//
// F3 — a "Mounting height" column (DataGridTemplateColumn + always-live EDITABLE
// combo) lists the named HeightStandards entries in its dropdown and lets the user
// type ANY custom height in mm directly (no raw quick-list). Pre-filled from the
// category default. Typed text commits on focus-loss / Enter / row-change: a match
// against a named standard reuses it (key + PreferredMm); otherwise the number is
// parsed (tolerating a trailing "mm") into a raw custom height with empty standard.
// Non-numeric / negative input is rejected (reverts) and absurd values (>5000 mm)
// warn but apply. A bulk "Set selected to:" editable height combo sets many rows at
// once. The resolved mm + standard key are stored on the row and written into the
// project override.
//
// Programmatic WPF (no XAML), modelled on MepCadWizard. ASCII-only.
//
// Editing: the STING-category, Anchor and Mounting-height columns use
// DataGridTemplateColumn with an always-live ComboBox CellTemplate so a
// selection commits immediately on change (a DataGridComboBoxColumn +
// SelectedItemBinding snapped the value back to the prefill). Multi-select
// (Extended / FullRow) + a bulk "Apply to selected" toolbar and "Skip all" /
// "Reset to detected" buttons let the user map many layers fast.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StingTools.Core;
using Binding = System.Windows.Data.Binding;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    /// <summary>F1 — one detected DWG/DXF import the user can choose to map.
    /// Holds the opaque ImportInstance (object, so the UI layer stays Revit-API-free
    /// at compile-shape) plus a human label and metadata for the dropdown.</summary>
    public sealed class DwgImportChoice
    {
        public object Import { get; set; }          // Autodesk.Revit.DB.ImportInstance
        public long Id { get; set; }
        public string Name { get; set; } = "DWG import";
        public bool IsLink { get; set; }
        public string ViewName { get; set; } = "model";

        /// <summary>Dropdown label: "<name> . id <id> . [Import|Link] . view <view>".</summary>
        public string Label =>
            $"{Name}  .  id {Id}  .  [{(IsLink ? "Link" : "Import")}]  .  view {(string.IsNullOrWhiteSpace(ViewName) ? "model" : ViewName)}";

        public override string ToString() => Label;
    }

    /// <summary>F3 — one mounting-height value carried by a row / offered in the combos.
    /// Either a named HeightStandards entry (dropdown) or a raw typed height in mm.
    /// Resolves to a millimetre value + (optional) standard key.</summary>
    public sealed class DwgHeightOption
    {
        public string Display { get; set; } = "";   // shown in the combo
        public double Mm { get; set; }               // resolved mounting height
        public string Standard { get; set; } = "";   // HeightStandards key, "" for raw

        public override string ToString() => Display;
    }

    public class DwgLayerMapDialog : Window
    {
        private readonly ObservableCollection<DwgLayerMapRow> _rows = new ObservableCollection<DwgLayerMapRow>();
        private DataGrid _grid;

        public bool Confirmed { get; private set; }
        /// <summary>The (possibly edited) rows — read after a confirmed close.</summary>
        public IReadOnlyList<DwgLayerMapRow> Rows => _rows;

        /// <summary>F1 — the import the user is mapping (the chosen DwgImportChoice.Import).
        /// The caller targets the place run at this same import.</summary>
        public object SelectedImport { get; private set; }

        public const string SkipOption = "(skip)";
        private const string DefaultAnchor = "WALL_MIDPOINT";
        private static readonly string[] Anchors =
            { "WALL_MIDPOINT", "WALL_FACE_OFFSET", "CEILING_CENTRE", "ROOM_CENTRE" };

        private readonly List<string> _categories;
        private readonly List<DwgHeightOption> _heightOptions;
        // category -> default height option Display (so each row pre-fills from its category).
        private readonly Dictionary<string, DwgHeightOption> _categoryHeightDefault;
        private readonly List<DwgImportChoice> _imports;
        // Caller callback: given the chosen import, return fresh seed rows for the grid (F1 switch).
        private readonly Func<object, IEnumerable<(string Layer, int Count, string Category, string Variant, string Anchor)>> _repopulate;

        private ComboBox _importCombo;
        private bool _suppressImportChange;

        /// <param name="categories">STING categories to offer (seed-mappable).</param>
        /// <param name="seedRows">layer, entity count, pre-filled category/variant/anchor.</param>
        /// <param name="heightOptions">F3 — named standards + raw quick heights for the height combo.</param>
        /// <param name="categoryHeightDefault">F2 — category -> default DwgHeightOption (pre-fills the height column).</param>
        /// <param name="imports">F1 — all detected imports for the dropdown.</param>
        /// <param name="selectedImportIndex">F1 — index of the import to start on.</param>
        /// <param name="repopulate">F1 — callback that, given a chosen import, yields its seed rows.</param>
        public DwgLayerMapDialog(
            IEnumerable<string> categories,
            IEnumerable<(string Layer, int Count, string Category, string Variant, string Anchor)> seedRows,
            IEnumerable<DwgHeightOption> heightOptions = null,
            IDictionary<string, DwgHeightOption> categoryHeightDefault = null,
            IReadOnlyList<DwgImportChoice> imports = null,
            int selectedImportIndex = 0,
            Func<object, IEnumerable<(string Layer, int Count, string Category, string Variant, string Anchor)>> repopulate = null)
        {
            _categories = new List<string> { SkipOption };
            _categories.AddRange((categories ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)));

            _heightOptions = (heightOptions ?? Enumerable.Empty<DwgHeightOption>()).ToList();
            _categoryHeightDefault = new Dictionary<string, DwgHeightOption>(
                categoryHeightDefault ?? new Dictionary<string, DwgHeightOption>(), StringComparer.OrdinalIgnoreCase);

            _imports = (imports ?? new List<DwgImportChoice>()).ToList();
            _repopulate = repopulate;
            if (_imports.Count > 0)
            {
                int idx = (selectedImportIndex >= 0 && selectedImportIndex < _imports.Count) ? selectedImportIndex : 0;
                SelectedImport = _imports[idx].Import;
            }

            LoadRows(seedRows);

            Title = "STING - Map DWG Layers to STING categories";
            Width = 940; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildLayout(selectedImportIndex);
        }

        private void LoadRows(IEnumerable<(string Layer, int Count, string Category, string Variant, string Anchor)> seedRows)
        {
            _rows.Clear();
            foreach (var r in (seedRows ?? Enumerable.Empty<(string, int, string, string, string)>())
                         .OrderByDescending(r => r.Count))
            {
                string cat = string.IsNullOrWhiteSpace(r.Category) ? SkipOption : r.Category;
                string anchor = string.IsNullOrWhiteSpace(r.Anchor) ? DefaultAnchor : r.Anchor;
                string variant = r.Variant ?? "";
                var height = DefaultHeightForCategory(cat);
                _rows.Add(new DwgLayerMapRow
                {
                    Layer = r.Layer ?? "",
                    Count = r.Count,
                    Category = cat,
                    Variant = variant,
                    Anchor = anchor,
                    Height = height,
                    // Snapshot the auto-detected values so "Reset to detected" can restore them.
                    DetectedCategory = cat,
                    DetectedVariant = variant,
                    DetectedAnchor = anchor,
                    DetectedHeight = height
                });
            }
        }

        /// <summary>F2 — the height option a category pre-fills with (the resolved category
        /// default), or null when the category has no default (height column shows blank).</summary>
        private DwgHeightOption DefaultHeightForCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == SkipOption) return null;
            if (_categoryHeightDefault.TryGetValue(category, out var def) && def != null)
            {
                // Prefer the matching option instance from the shared list (same reference
                // so the combo shows it selected); fall back to the default itself.
                var match = _heightOptions.FirstOrDefault(h =>
                    Math.Abs(h.Mm - def.Mm) < 0.5 &&
                    string.Equals(h.Standard ?? "", def.Standard ?? "", StringComparison.OrdinalIgnoreCase));
                return match ?? def;
            }
            return null;
        }

        private UIElement BuildLayout(int selectedImportIndex)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // import picker
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // intro
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // bulk toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // buttons

            // -- F1 import picker --------------------------------------------------
            var importBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            string hdr = _imports.Count == 0
                ? "0 DWG imports found"
                : (_imports.Count == 1 ? "1 DWG import found" : $"{_imports.Count} DWG imports found");
            importBar.Children.Add(new TextBlock
            {
                Text = "Import:", FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            _importCombo = new ComboBox
            {
                Width = 560, IsEditable = false, VerticalAlignment = VerticalAlignment.Center,
                ItemsSource = _imports, DisplayMemberPath = nameof(DwgImportChoice.Label),
                IsEnabled = _imports.Count > 1
            };
            if (_imports.Count > 0)
                _importCombo.SelectedIndex = (selectedImportIndex >= 0 && selectedImportIndex < _imports.Count) ? selectedImportIndex : 0;
            _importCombo.SelectionChanged += ImportCombo_SelectionChanged;
            importBar.Children.Add(_importCombo);
            importBar.Children.Add(new TextBlock
            {
                Text = "   " + hdr, Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            });
            Grid.SetRow(importBar, 0); root.Children.Add(importBar);

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = "Map each DWG layer to the STING category it represents. Category " + SkipOption +
                       " leaves the layer unmapped. Variant is optional (e.g. SOCKET_2G, DOWNLIGHT); " +
                       "Anchor picks the host search (wall vs ceiling); Mounting height pre-fills from the " +
                       "category's standard default (BS 7671 / BS 8300 / BS 5839 etc.) - override per layer by " +
                       "picking a named standard OR typing any height in mm. Pre-filled from the auto-detector - correct as needed. " +
                       "Ctrl/Shift-click to select several layers, then 'Apply to selected'. " +
                       "Saved to this project's _BIM_COORD override; the corporate map is untouched."
            };
            Grid.SetRow(intro, 1); root.Children.Add(intro);

            // -- Bulk toolbar (B4): category + anchor + height + Apply to selected. --
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(new TextBlock { Text = "Set selected to:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var bulkCat = new ComboBox { ItemsSource = _categories, Width = 180, IsEditable = false, VerticalAlignment = VerticalAlignment.Center };
            bulkCat.SelectedIndex = _categories.Count > 1 ? 1 : 0;   // first real category, else (skip)
            toolbar.Children.Add(bulkCat);
            toolbar.Children.Add(new TextBlock { Text = "Anchor:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
            var bulkAnchor = new ComboBox { ItemsSource = Anchors, Width = 140, IsEditable = false, VerticalAlignment = VerticalAlignment.Center };
            bulkAnchor.SelectedItem = DefaultAnchor;
            toolbar.Children.Add(bulkAnchor);
            // F3 — bulk height combo. EDITABLE: pick a named standard or type a height in mm.
            // ToString() on DwgHeightOption renders the Display text (no DisplayMemberPath so
            // the editable text box can show typed custom values too).
            toolbar.Children.Add(new TextBlock { Text = "Height:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
            var bulkHeight = new ComboBox { ItemsSource = _heightOptions, Width = 230, IsEditable = true, VerticalAlignment = VerticalAlignment.Center };
            if (_heightOptions.Count > 0) bulkHeight.SelectedIndex = 0;
            toolbar.Children.Add(bulkHeight);
            var applySel = new Button { Content = "Apply to selected", Width = 130, Margin = new Thickness(10, 0, 0, 0) };
            applySel.Click += (s, e) =>
            {
                var cat = bulkCat.SelectedItem as string ?? SkipOption;
                var anchor = bulkAnchor.SelectedItem as string ?? DefaultAnchor;
                var selected = _grid?.SelectedItems?.OfType<DwgLayerMapRow>().ToList() ?? new List<DwgLayerMapRow>();
                if (selected.Count == 0) { ToastInline("Select one or more layers first (Ctrl/Shift-click)."); return; }
                // Resolve the bulk height: a picked item OR typed text. Invalid text warns and
                // leaves heights untouched (category + anchor still apply).
                DwgHeightOption height = bulkHeight.SelectedItem as DwgHeightOption;
                bool applyHeight = height != null;
                if (height == null)
                {
                    if (TryResolveHeightText(bulkHeight.Text, out var opt, out var note))
                    {
                        if (note != null) ToastInline(note);
                        height = opt; applyHeight = opt != null;
                    }
                    else { ToastInline(note); applyHeight = false; }
                }
                foreach (var row in selected)
                {
                    row.Category = cat;
                    row.Anchor = anchor;
                    if (applyHeight) row.Height = height;
                }
            };
            toolbar.Children.Add(applySel);
            Grid.SetRow(toolbar, 2); root.Children.Add(toolbar);

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
            _grid.Columns.Add(new DataGridTextColumn { Header = "DWG layer", Binding = new Binding("Layer") { Mode = BindingMode.OneWay }, Width = 240, IsReadOnly = true });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Entities", Binding = new Binding("Count") { Mode = BindingMode.OneWay }, Width = 65, IsReadOnly = true });
            _grid.Columns.Add(MakeComboColumn("STING category", "Category", _categories, 180));
            _grid.Columns.Add(new DataGridTextColumn { Header = "Variant hint", Binding = new Binding("Variant") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 120 });
            _grid.Columns.Add(MakeComboColumn("Anchor", "Anchor", Anchors, 140));
            _grid.Columns.Add(MakeHeightColumn("Mounting height", _heightOptions, 230));   // F3
            Grid.SetRow(_grid, 3); root.Children.Add(_grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };

            var skipAll = new Button { Content = "Skip all", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            skipAll.Click += (s, e) => { foreach (var r in _rows) r.Category = SkipOption; };
            buttons.Children.Add(skipAll);

            var reset = new Button { Content = "Reset to detected", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += (s, e) =>
            {
                foreach (var r in _rows)
                {
                    r.Category = r.DetectedCategory; r.Variant = r.DetectedVariant;
                    r.Anchor = r.DetectedAnchor; r.Height = r.DetectedHeight;
                }
            };
            buttons.Children.Add(reset);

            var save = new Button { Content = "Save mappings", Width = 130, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            save.Click += (s, e) => { Confirmed = true; DialogResult = true; Close(); };
            buttons.Children.Add(save);

            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 4); root.Children.Add(buttons);

            return root;
        }

        // F1 — switch the active import: re-run the repopulate callback against the chosen
        // import and rebuild the grid. SelectedImport is updated so the place run follows.
        private void ImportCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressImportChange) return;
            if (!(_importCombo?.SelectedItem is DwgImportChoice choice)) return;
            SelectedImport = choice.Import;
            if (_repopulate == null) return;
            try
            {
                var fresh = _repopulate(choice.Import);
                LoadRows(fresh);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgLayerMapDialog.ImportSwitch: {ex.Message}");
                ToastInline("Could not read the chosen import's layers - see the log.");
            }
        }

        /// <summary>An always-live ComboBox column (string-bound). CellTemplate (not
        /// CellEditingTemplate) means the combo is rendered in every cell, so a selection
        /// commits on change with no edit-mode dance — TwoWay/PropertyChanged binding writes
        /// straight to the row.</summary>
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

        /// <summary>F3 — always-live EDITABLE height combo column. The dropdown lists the named
        /// HeightStandards options (DwgHeightOption.ToString() renders Display — no
        /// DisplayMemberPath so the editable text box can also show a typed custom value). The
        /// Text is bound OneWay to the row's HeightText so programmatic changes (bulk apply /
        /// reset) refresh the box; user input is parsed + committed by HeightCell_Commit on
        /// dropdown selection, focus loss, or Enter.</summary>
        private DataGridTemplateColumn MakeHeightColumn(string header, IEnumerable<DwgHeightOption> items, double width)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.ItemsSourceProperty, items);
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            factory.SetValue(ComboBox.MarginProperty, new Thickness(1));
            // OneWay: the row drives the displayed text; typed input is read back by the handlers.
            factory.SetBinding(ComboBox.TextProperty,
                new Binding(nameof(DwgLayerMapRow.HeightText)) { Mode = BindingMode.OneWay });
            factory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(HeightCell_SelectionChanged));
            factory.AddHandler(ComboBox.LostFocusEvent, new RoutedEventHandler(HeightCell_Commit));
            factory.AddHandler(ComboBox.KeyDownEvent, new System.Windows.Input.KeyEventHandler(HeightCell_KeyDown));
            return new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = factory }
            };
        }

        // F3 — a dropdown pick commits the named option straight onto the row.
        private void HeightCell_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.DataContext is DwgLayerMapRow row)) return;
            if (e.AddedItems != null && e.AddedItems.Count > 0 && e.AddedItems[0] is DwgHeightOption opt)
                row.Height = opt;   // OneWay Text binding refreshes the box to the canonical display
        }

        // F3 — Enter commits the typed text (same path as focus-loss). Handled so Enter-in-combo
        // doesn't also fire the default Save button and close the dialog mid-edit.
        private void HeightCell_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { HeightCell_Commit(sender, null); e.Handled = true; }
        }

        // F3 — parse the editable combo's text and commit it to the row. A named-standard match
        // reuses the standard; a bare number becomes a raw custom height; junk/negative reverts.
        private void HeightCell_Commit(object sender, RoutedEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.DataContext is DwgLayerMapRow row)) return;
            string text = cb.Text;
            // Already canonical for the committed value? Nothing to do (avoids redundant churn).
            if (string.Equals((text ?? "").Trim(), (row.Height?.Display ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return;
            if (TryResolveHeightText(text, out var option, out var note))
            {
                if (note != null) ToastInline(note);   // out-of-range warning (non-blocking)
                row.Height = option;                    // commit (null = defer to category default)
            }
            else
            {
                ToastInline(note);                      // reject — leave Height; revert the box below
            }
            // Re-pull the committed value into the box ("450" -> "450 mm", junk -> prior committed).
            // UpdateTarget refreshes from HeightText WITHOUT clearing the OneWay binding (a direct
            // cb.Text assignment would remove it and break later bulk-apply refreshes).
            cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
        }

        /// <summary>F3 — resolve a typed/selected height string. Returns true with <paramref name="option"/>
        /// (null = clear, defer to the category default) when valid; false + <paramref name="note"/> on
        /// reject (non-numeric / negative). A valid-but-absurd value (&gt;5000 mm) returns true with a
        /// warning note. Matching order: named-standard Display, then standard key, then a bare number
        /// (tolerating a trailing "mm", spaces, thousands separators).</summary>
        private bool TryResolveHeightText(string text, out DwgHeightOption option, out string note)
        {
            option = null; note = null;
            text = (text ?? "").Trim();
            if (text.Length == 0) return true;   // empty -> clear (category default at place time)

            // 1) Named standard, matched by its dropdown Display ...
            var named = _heightOptions.FirstOrDefault(o =>
                o != null && string.Equals(o.Display, text, StringComparison.OrdinalIgnoreCase));
            // ... or by its HeightStandard key (e.g. user types "BS7671_SOCKET_STD").
            if (named == null)
                named = _heightOptions.FirstOrDefault(o =>
                    o != null && !string.IsNullOrEmpty(o.Standard) &&
                    string.Equals(o.Standard, text, StringComparison.OrdinalIgnoreCase));
            if (named != null) { option = named; return true; }

            // 2) Bare number in mm. Tolerate a trailing "mm", spaces, and thousands separators.
            string num = text;
            if (num.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                num = num.Substring(0, num.Length - 2);
            num = num.Trim().Replace(",", "");
            if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double mm) ||
                double.TryParse(num, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out mm))
            {
                if (mm < 0) { note = $"Mounting height must be 0 or positive - '{text}' rejected."; return false; }
                option = new DwgHeightOption { Display = $"{mm:F0} mm", Mm = mm, Standard = "" };
                if (mm > 5000) note = $"Mounting height {mm:F0} mm is unusually high (>5000 mm) - applied anyway.";
                return true;
            }

            note = $"Enter a height in mm (e.g. 450) or pick a named standard - '{text}' is not a number.";
            return false;
        }

        private void ToastInline(string msg)
        {
            try { Title = "STING - Map DWG Layers - " + msg; } catch { }
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

        // F3 — chosen mounting-height option (null = leave to the category default at place time).
        private DwgHeightOption _height;
        public DwgHeightOption Height { get => _height; set { _height = value; OnChanged(nameof(Height)); OnChanged(nameof(HeightText)); } }

        /// <summary>F3 — the editable height combo's displayed text (OneWay source). Reflects the
        /// committed Height; bulk apply / reset that set Height refresh the box through this.</summary>
        public string HeightText => _height?.Display ?? "";

        // Auto-detected snapshot (set at construction) for "Reset to detected".
        public string DetectedCategory { get; set; } = DwgLayerMapDialog.SkipOption;
        public string DetectedVariant { get; set; } = "";
        public string DetectedAnchor { get; set; } = "WALL_MIDPOINT";
        public DwgHeightOption DetectedHeight { get; set; }

        /// <summary>True when the user actually mapped this layer (not "(skip)").</summary>
        public bool IsMapped => !string.IsNullOrWhiteSpace(Category) && Category != DwgLayerMapDialog.SkipOption;

        /// <summary>F3 — resolved mounting height in mm (0 when no explicit height chosen, so
        /// the bridge falls back to the category default).</summary>
        public double MountingHeightMm => Height?.Mm ?? 0.0;

        /// <summary>F3 — HeightStandards key carried with the chosen height ("" for raw).</summary>
        public string HeightStandard => Height?.Standard ?? "";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
