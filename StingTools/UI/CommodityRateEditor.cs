// ══════════════════════════════════════════════════════════════════════════
//  CommodityRateEditor.cs — price the material schedule's commodities in a
//  grid, seeded from the schedule itself.
//
//  WHAT IT REPLACES. The reconciler's advice: "Add a row keyed
//  'RD_Breeze Block 01_Panel — Concrete' to commodity_rates.csv to price it."
//  That instruction cannot be followed reliably. The key IS the row's display
//  name, so 21 of the 25 keys the first real export asked for carry a
//  non-ASCII em dash; CommodityRateResolver.Resolve is an exact
//  OrdinalIgnoreCase dictionary hit, so a hyphen typed in its place misses,
//  the key goes back into _unpriced, and the same flag comes back with no
//  error anywhere. Until now nothing in the codebase wrote a rate file at all.
//
//  So the keys come from the schedule and the user types only a NUMBER.
//
//  Shape borrowed from TitleBlockCsvEditor, which solved the same problem for
//  title-block parameters: ObservableCollection grid, validate on save, ATOMIC
//  write (tmp → Replace → .bak), refuse on an unsaved project, and never write
//  the shipped corporate baseline — only the project override.
//
//  Everything about WHAT may be priced and what a blank cell means lives in
//  the Revit-free RateEditorSeed, where it is tested.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.UI
{
    /// <summary>Grid row. Only NewRate is typed; everything else is read-only.</summary>
    public sealed class RateEditorRowVm : INotifyPropertyChanged
    {
        private readonly RateEditRow _row;
        public RateEditorRowVm(RateEditRow row) { _row = row; }

        public RateEditRow Model => _row;

        public string CommodityKey => _row.CommodityKey;
        public string Description  => _row.Description;
        public string SupplierUnit => _row.SupplierUnit;
        public string Quantity     => _row.OrderQuantity.ToString("N2", CultureInfo.CurrentCulture);

        public string CurrentRate => _row.IsUnpriced
            ? "—"
            : _row.CurrentRateUGX.ToString("N0", CultureInfo.CurrentCulture);

        /// <summary>
        /// The SAME wording the export uses.
        ///
        /// This read "baseline" while the schedule the user reads next said
        /// "Indicative" for the identical row. Two names for one fact, in two
        /// places a QS compares, is how a shipped guess comes to be trusted as
        /// a corporate rate.
        /// </summary>
        public string Source => RateProvenanceLabel.For(new MaterialCommodity
        {
            RateUGX = _row.CurrentRateUGX, RateSource = _row.CurrentSource
        });

        /// <summary>Where the quantity was measured from, e.g. "Roofs".</summary>
        public string Origin => string.IsNullOrWhiteSpace(_row.Origin) ? "—" : _row.Origin;

        /// <summary>The exact key written to commodity_rates.csv.</summary>
        public string Key => _row.CommodityKey;

        public string Kind => string.IsNullOrWhiteSpace(_row.SourceKind) ? "—" : _row.SourceKind;

        /// <summary>
        /// How many measured rows fed this commodity, or the single element
        /// reference when there is exactly one.
        ///
        /// A commodity is an aggregate. Naming "the first of 40" elements would
        /// be a confident label for something untrue of the row, so a count is
        /// shown instead and a reference only when it identifies the whole row.
        /// </summary>
        public string Backing => _row.ContributingRows == 1 && _row.SingleTraceRef.Length > 0
            ? _row.SingleTraceRef
            : _row.ContributingRows.ToString(CultureInfo.CurrentCulture);

        public string Note => string.IsNullOrWhiteSpace(_row.ConversionNote) ? "" : _row.ConversionNote;

        /// <summary>The model types behind the row, for the row tooltip.</summary>
        public string TypeDetail => string.IsNullOrWhiteSpace(_row.TypeDetail)
            ? "No model type recorded for this row."
            : "Measured from:\n" + _row.TypeDetail;

        public string Amount => _row.IsUnpriced
            ? "—"
            : _row.CurrentAmountUGX.ToString("N0", CultureInfo.CurrentCulture);

        /// <summary>
        /// The only editable cell. EMPTY is not zero — it means "leave this
        /// alone", and the merge honours that so saving after editing one row
        /// cannot wipe every other price.
        /// </summary>
        public string NewRate
        {
            get => _row.NewRateUGX.HasValue
                ? _row.NewRateUGX.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "";
            set
            {
                string v = (value ?? "").Replace(",", "").Replace("UGX", "").Trim();
                if (v.Length == 0) _row.NewRateUGX = null;
                else if (double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out double d)
                      || double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    _row.NewRateUGX = d;
                // An unparseable entry leaves the previous value in place rather
                // than silently becoming 0 — a 0 here would read as "free".
                OnChanged(nameof(NewRate));
                OnChanged(nameof(RowBrush));
            }
        }

        public Brush RowBrush =>
            _row.NewRateUGX.HasValue ? new SolidColorBrush(MediaColor.FromRgb(232, 245, 233))   // edited
            : _row.IsUnpriced        ? new SolidColorBrush(MediaColor.FromRgb(255, 243, 224))   // unpriced
                                     : Brushes.Transparent;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class CommodityRateEditor : Window
    {
        private static readonly MediaColor BrandPurple = MediaColor.FromRgb(88, 44, 131);

        private readonly Document _doc;
        private readonly ObservableCollection<RateEditorRowVm> _rows = new ObservableCollection<RateEditorRowVm>();
        private readonly List<CommodityRate> _existingProject;
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly string _targetPath;
        private readonly System.Collections.Generic.List<RateEditorRowVm> _all;
        private readonly TextBox _filter;
        private readonly CheckBox _unpricedOnly;
        private readonly System.Collections.Generic.List<DataGridColumn> _detailColumns =
            new System.Collections.Generic.List<DataGridColumn>();
        private readonly string _seedStakes;
        private Button _detailsButton;
        private bool _detailsShown;
        private readonly SupplierUnitTable _units;
        private readonly string _patchPath;
        private readonly MaterialScheduleDocument _schedule;

        public bool Saved { get; private set; }

        private CommodityRateEditor(Document doc, RateSeedResult seed,
                                    List<CommodityRate> existingProject, string targetPath,
                                    SupplierUnitTable units, string patchPath,
                                    MaterialScheduleDocument schedule)
        {
            _units = units;
            _patchPath = patchPath;
            _schedule = schedule;
            _doc = doc;
            _existingProject = existingProject ?? new List<CommodityRate>();
            _targetPath = targetPath;
            _seedStakes = seed.Stakes();

            _all = seed.Rows.Select(r => new RateEditorRowVm(r)).ToList();
            foreach (var r in _all) _rows.Add(r);

            Title = "STING — Price commodities";
            Width = 1080; Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;

            var root = new DockPanel();

            // ── header ──
            var header = new StackPanel
            {
                Background = new SolidColorBrush(BrandPurple),
                Margin = new Thickness(0)
            };
            header.Children.Add(new TextBlock
            {
                Text = "Price commodities",
                Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold,
                Margin = new Thickness(14, 10, 14, 2)
            });
            header.Children.Add(new TextBlock
            {
                Text = seed.Summary()
                     + "  Type a rate in the last column only — the keys come from the schedule, "
                     + "so nothing has to be retyped. A blank cell leaves that price alone.",
                Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 11.5,
                Margin = new Thickness(14, 0, 14, 10)
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── footer ──
            var footer = new DockPanel { Margin = new Thickness(12, 8, 12, 12) };
            _status = new TextBlock
            {
                Text = seed.Stakes(),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11,
                Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = "Writes " + (targetPath ?? "(project not saved)")
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);

            var cancel = new Button { Content = "Cancel", Width = 96, Height = 30, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };

            var save = new Button
            {
                Content = "Save rates", Width = 130, Height = 30, Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(BrandPurple), Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            save.Click += (s, e) => { if (Save()) { DialogResult = true; Close(); } };

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            footer.Children.Add(buttons);
            footer.Children.Add(_status);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── inline actions ──
            //
            // Every action that operates on the grid lives ON the grid, with
            // the keyboard shortcut printed on it. The alternative - a dialog
            // per action - is what made the editor's own button hard to find
            // in the first place.
            var bar = new WrapPanel { Margin = new Thickness(12, 10, 12, 0) };

            bar.Children.Add(ToolButton("Paste column  (Ctrl+V)",
                "Paste a column of rates copied from Excel, starting at the selected row. "
              + "Blank lines keep their place so the rates below them stay on the right "
              + "commodities; a line with no number is reported, never treated as zero.",
                (a, b) => PasteColumn()));

            bar.Children.Add(ToolButton("Fill down  (Ctrl+D)",
                "Copy the rate in the row above into every selected row.",
                (a, b) => FillDown()));

            bar.Children.Add(ToolButton("Clear  (Del)",
                "Clear the typed rate in the selected rows. The row keeps whatever rate it "
              + "already had - clearing is not the same as pricing at zero.",
                (a, b) => ClearSelected()));

            bar.Children.Add(ToolButton("Map to commodity…",
                "Say which commodity the selected row's model type belongs to — a roof named "
              + "'Generic - 225mm' can be sheeting or tiles, and the schedule cannot tell. Shows "
              + "every row the mapping would claim and what each becomes BEFORE writing anything. "
              + "Writes only the mapping; conversion factors and wastage stay in the shipped table.",
                (a, b) => MapToCommodity()));

            _detailsButton = ToolButton("Show detail",
                "Show the columns that identify a row rather than price it: the exact key written to "
              + "commodity_rates.csv, the constituent kind behind it, how many measured rows back it, "
              + "and why a row stayed in measured units.",
                (a, b) => ToggleDetailColumns());
            bar.Children.Add(_detailsButton);

            bar.Children.Add(ToolButton("Copy keys",
                "Copy Description and Unit for the visible rows to the clipboard, so a rate "
              + "column can be built beside them in Excel and pasted straight back.",
                (a, b) => CopyKeys()));

            bar.Children.Add(new TextBlock
            {
                Text = "Find:", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 4, 0), FontSize = 11.5
            });
            _filter = new TextBox { Width = 170, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
            _filter.TextChanged += (a, b) => ApplyFilter();
            bar.Children.Add(_filter);

            _unpricedOnly = new CheckBox
            {
                Content = "Unpriced only", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), FontSize = 11.5
            };
            _unpricedOnly.Checked   += (a, b) => ApplyFilter();
            _unpricedOnly.Unchecked += (a, b) => ApplyFilter();
            bar.Children.Add(_unpricedOnly);

            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // ── grid ──
            _grid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(12, 6, 12, 0),
                RowBackground = Brushes.Transparent,

                // Excel behaviour, deliberately:
                //  * cell selection, not whole rows, so the rate column can be
                //    navigated and pasted into like a spreadsheet column;
                //  * Tab and Enter both commit and move on, Enter downwards,
                //    which is what a hand pricing a column expects;
                //  * one click into the cell rather than click-then-click.
                SelectionUnit = DataGridSelectionUnit.Cell,
                SelectionMode = DataGridSelectionMode.Extended,
                CanUserSortColumns = true,
                ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader
            };
            // Single click begins the edit. Without this the first click only
            // selects, which reads as the cell being read-only.
            _grid.PreparingCellForEdit += (s2, e2) =>
            {
                if (e2.EditingElement is TextBox tb)
                { tb.SelectAll(); tb.Focus(); }
            };
            _grid.CurrentCellChanged += (s2, e2) =>
            {
                if (_grid.CurrentCell.Column != null && !_grid.CurrentCell.Column.IsReadOnly)
                    _grid.BeginEdit();
            };
            _grid.PreviewKeyDown += Grid_PreviewKeyDown;
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                new System.Windows.Data.Binding(nameof(RateEditorRowVm.RowBrush))));
            rowStyle.Setters.Add(new Setter(DataGridRow.ToolTipProperty,
                new System.Windows.Data.Binding(nameof(RateEditorRowVm.TypeDetail))));
            _grid.RowStyle = rowStyle;

            AddReadOnly("Description", nameof(RateEditorRowVm.Description), 290);
            // "Generic - 225mm, 610 m2, unpriced" told a QS nothing about what
            // it was. Naming the category is what makes the row priceable.
            AddReadOnly("From", nameof(RateEditorRowVm.Origin), 110);
            AddReadOnly("Unit", nameof(RateEditorRowVm.SupplierUnit), 120);
            AddReadOnly("Order qty", nameof(RateEditorRowVm.Quantity), 90);
            AddReadOnly("Rate now", nameof(RateEditorRowVm.CurrentRate), 90);
            AddReadOnly("Source", nameof(RateEditorRowVm.Source), 80);
            AddReadOnly("Amount", nameof(RateEditorRowVm.Amount), 110);
            // Detail columns, COLLAPSED by default.
            //
            // Every one of these identifies a row rather than pricing it, and a
            // grid wide enough to show them all at once is a grid nobody reads.
            // They are kept together so one toggle reveals the whole set.
            _detailColumns.Add(AddReadOnly("Key", nameof(RateEditorRowVm.Key), 220));
            _detailColumns.Add(AddReadOnly("Kind", nameof(RateEditorRowVm.Kind), 120));
            _detailColumns.Add(AddReadOnly("Backing", nameof(RateEditorRowVm.Backing), 80));
            _detailColumns.Add(AddReadOnly("Why measured units", nameof(RateEditorRowVm.Note), 280));
            foreach (var c in _detailColumns) c.Visibility = System.Windows.Visibility.Collapsed;

            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "New rate UGX",
                Binding = new System.Windows.Data.Binding(nameof(RateEditorRowVm.NewRate))
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus
                },
                Width = 120,
                IsReadOnly = false
            });

            root.Children.Add(_grid);
            Content = root;
        }

        private static Button ToolButton(string label, string tip, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = label, Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, FontSize = 11.5
            };
            b.Click += onClick;
            return b;
        }

        // ══ Excel keys ════════════════════════════════════════
        private void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers
                         & System.Windows.Input.ModifierKeys.Control) != 0;

            if (ctrl && e.Key == System.Windows.Input.Key.V) { PasteColumn(); e.Handled = true; }
            else if (ctrl && e.Key == System.Windows.Input.Key.D) { FillDown(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Delete && !IsEditing()) { ClearSelected(); e.Handled = true; }
        }

        private bool IsEditing() =>
            _grid.CurrentCell.Column != null
            && _grid.CurrentColumn != null
            && System.Windows.Input.Keyboard.FocusedElement is TextBox;

        private int SelectedIndex()
        {
            var vm = _grid.CurrentCell.Item as RateEditorRowVm;
            int i = vm != null ? _rows.IndexOf(vm) : -1;
            return i < 0 ? 0 : i;
        }

        /// <summary>
        /// Paste a column from Excel over consecutive rows from the selection.
        ///
        /// The parsing, and every decision about blanks and unparseable lines,
        /// lives in the Revit-free RatePasteParser where it is tested. This is
        /// the part that moves values into rows and says what happened.
        /// </summary>
        private void PasteColumn()
        {
            string text;
            try { text = Clipboard.GetText(); }
            catch (Exception ex)
            {
                StingLog.Warn("RateEditor paste: " + ex.Message);
                MessageBox.Show(this, "The clipboard could not be read.", Caption,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var parsed = RatePasteParser.Parse(text);
            if (parsed.Cells.Count == 0)
            {
                MessageBox.Show(this,
                    "There is nothing on the clipboard to paste.\n\nCopy a column of rates in "
                  + "Excel first — one per line, in the same order as the rows here. "
                  + "Copy keys puts the descriptions on the clipboard so the column can be built "
                  + "beside them.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int start = SelectedIndex();
            int applied = 0, ranOut = 0;

            for (int i = 0; i < parsed.Cells.Count; i++)
            {
                int target = start + i;
                if (target >= _rows.Count)
                {
                    if (parsed.Cells[i].Value.HasValue) ranOut++;
                    continue;
                }
                var cell = parsed.Cells[i];
                if (!cell.Value.HasValue) continue;      // blank or rejected: row untouched
                _rows[target].NewRate = cell.Value.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                applied++;
            }

            CommitAndRefresh();
            string summary = parsed.Summary(applied, ranOut);
            _status.Text = summary ?? _status.Text;
            if (parsed.Rejected.Count > 0 || ranOut > 0)
                MessageBox.Show(this, summary, Caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Copy the rate above into every selected row.</summary>
        private void FillDown()
        {
            var targets = _grid.SelectedCells
                .Select(c => c.Item as RateEditorRowVm)
                .Where(v => v != null).Distinct().ToList();
            if (targets.Count == 0) { var v = _grid.CurrentCell.Item as RateEditorRowVm; if (v != null) targets.Add(v); }
            if (targets.Count == 0) return;

            int first = _rows.IndexOf(targets[0]);
            if (first <= 0)
            {
                _status.Text = "Fill down needs a row above the selection to copy from.";
                return;
            }

            string source = _rows[first - 1].NewRate;
            if (string.IsNullOrWhiteSpace(source))
            {
                _status.Text = "The row above has no typed rate to fill down.";
                return;
            }

            foreach (var t in targets) t.NewRate = source;
            CommitAndRefresh();
            _status.Text = $"Filled {targets.Count} row(s) with {source}.";
        }

        /// <summary>
        /// Clear the TYPED rate, not the rate in force.
        ///
        /// Clearing is "leave this one alone", which is exactly what an empty
        /// cell means to the merge. It is not pricing at zero, and the tooltip
        /// says so, because those two would look identical in the grid.
        /// </summary>
        private void ClearSelected()
        {
            var targets = _grid.SelectedCells
                .Select(c => c.Item as RateEditorRowVm)
                .Where(v => v != null).Distinct().ToList();
            if (targets.Count == 0) return;

            foreach (var t in targets) t.NewRate = "";
            CommitAndRefresh();
            _status.Text = $"Cleared the typed rate on {targets.Count} row(s) — each keeps the rate it already had.";
        }

        /// <summary>Description + unit for the VISIBLE rows, tab-separated.</summary>
        private void CopyKeys()
        {
            var sb = new StringBuilder();
            foreach (var r in _rows) sb.AppendLine(r.Description + "\t" + r.SupplierUnit);
            try
            {
                Clipboard.SetText(sb.ToString());
                _status.Text = $"Copied {_rows.Count} description(s) — paste into Excel, put rates "
                             + "beside them, then copy that column back and press Ctrl+V here.";
            }
            catch (Exception ex)
            {
                StingLog.Warn("RateEditor copy: " + ex.Message);
                _status.Text = "The clipboard could not be written.";
            }
        }

        /// <summary>
        /// Filter the visible rows. Edits live on the underlying model, so a
        /// row filtered out of sight keeps whatever was typed into it — hiding
        /// a row must never discard the work done on it.
        /// </summary>
        private void ApplyFilter()
        {
            string q = (_filter?.Text ?? "").Trim();
            bool unpricedOnly = _unpricedOnly?.IsChecked == true;

            _rows.Clear();
            foreach (var r in _all)
            {
                if (unpricedOnly && !r.Model.IsUnpriced) continue;
                if (q.Length > 0 &&
                    (r.Description ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (r.SupplierUnit ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _rows.Add(r);
            }
            _status.Text = _rows.Count == _all.Count
                ? "Writes " + (_targetPath ?? "(project not saved)")
                : $"Showing {_rows.Count} of {_all.Count} row(s). Rates typed into hidden rows are kept.";
        }

        /// <summary>
        /// Commit any open cell edit before refreshing.
        ///
        /// Items.Refresh() throws if the grid is mid-edit, and every one of
        /// these actions can be triggered by a keyboard shortcut while a cell
        /// is open — which is the normal way to use them.
        /// </summary>
        private void CommitAndRefresh()
        {
            try
            {
                _grid.CommitEdit(DataGridEditingUnit.Cell, true);
                _grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (Exception ex) { StingLog.Warn("RateEditor commit: " + ex.Message); }
            _grid.Items.Refresh();
        }

        /// <summary>
        /// Map the selected row's model type to a commodity.
        ///
        /// Two things happen before anything is written: the RENAME is offered
        /// first, and the mapping is previewed.
        ///
        /// The rename comes first because it is the better fix and the mapping
        /// is the escape hatch. Calling a roof "IT4 Corrugated Sheet Roof 225"
        /// matches the shipped pattern with no override at all, and the name
        /// then means something to the schedule, the bill, the drawings and the
        /// next consultant. A mapping fixes one project and leaves the name
        /// wrong everywhere else — which is right when the type is in a linked
        /// model, a vendor family or somebody else's file, and those are common
        /// enough that the escape hatch has to exist.
        /// </summary>
        private void MapToCommodity()
        {
            var vm = _grid.CurrentCell.Item as RateEditorRowVm;
            if (vm == null) { _status.Text = "Select a row first."; return; }
            if (_units == null || _schedule == null)
            {
                MessageBox.Show(this, "The supplier-unit table was not loaded, so nothing can be mapped.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var candidates = TypePatternPlanner.Candidates(_units);
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "The supplier-unit table declares no commodities to map to.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new TypeMappingDialog(vm.Description, candidates, _units, _schedule) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Chosen == null) return;

            try
            {
                var file = LoadPatches(_patchPath);
                file.TypePatterns.Add(new SupplierUnitPatch
                {
                    CommodityKey = dlg.Chosen.CommodityKey,
                    Pattern = dlg.ChosenPattern,
                    Why = dlg.Why,
                    AddedBy = Environment.UserName ?? "",
                    AddedUtc = DateTime.UtcNow.ToString("u")
                });

                string dir = Path.GetDirectoryName(_patchPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = _patchPath + ".tmp";
                File.WriteAllText(tmp,
                    Newtonsoft.Json.JsonConvert.SerializeObject(file, Newtonsoft.Json.Formatting.Indented),
                    new UTF8Encoding(false));
                if (File.Exists(_patchPath))
                {
                    string bak = _patchPath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Replace(tmp, _patchPath, bak);
                }
                else File.Move(tmp, _patchPath);

                StingLog.Info($"CommodityRateEditor: mapped '{dlg.ChosenPattern}' -> {dlg.Chosen.CommodityKey}");
                MessageBox.Show(this,
                    $"'{dlg.ChosenPattern}' is now mapped to {dlg.Chosen.CommodityKey}.\n\n"
                  + _patchPath
                  + "\n\nRe-run the material schedule to see the converted quantities. The rows in "
                  + "this grid still show the OLD unit — the mapping is applied when the schedule is "
                  + "rebuilt, not here.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StingLog.Error("CommodityRateEditor.MapToCommodity", ex);
                MessageBox.Show(this, "The mapping could not be written:\n\n" + ex.Message,
                    Caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static SupplierUnitPatchFile LoadPatches(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return Newtonsoft.Json.JsonConvert
                               .DeserializeObject<SupplierUnitPatchFile>(File.ReadAllText(path))
                           ?? new SupplierUnitPatchFile();
            }
            catch (Exception ex) { StingLog.Warn("LoadPatches: " + ex.Message); }
            return new SupplierUnitPatchFile();
        }

        private const string Caption = "STING — Price commodities";

        private DataGridColumn AddReadOnly(string header, string path, double width)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path),
                Width = width,
                IsReadOnly = true
            };
            _grid.Columns.Add(col);
            return col;
        }

        /// <summary>Show or hide the identification columns as one set.</summary>
        private void ToggleDetailColumns()
        {
            _detailsShown = !_detailsShown;
            foreach (var c in _detailColumns)
                c.Visibility = _detailsShown ? System.Windows.Visibility.Visible
                                             : System.Windows.Visibility.Collapsed;
            if (_detailsButton != null)
                _detailsButton.Content = _detailsShown ? "Hide detail" : "Show detail";
            _status.Text = _detailsShown
                ? "Detail columns: the exact CSV key, the constituent kind that produced the row, how "
                + "many measured rows back it (or the element reference when only one does), and why a "
                + "row stayed in measured units."
                : _seedStakes;
        }

        // ══════════════════════════════════════════════════════════════════
        private bool Save()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            if (string.IsNullOrEmpty(_targetPath))
            {
                MessageBox.Show(this,
                    "This project has not been saved, so there is no project folder to write "
                  + "commodity_rates.csv into.\n\nSave the Revit project first, then re-open the editor.",
                    "STING — Price commodities", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // _all, not _rows: a filtered-out row that was priced before the
            // filter was applied must still be saved. Saving only what is on
            // screen would silently discard work the user can no longer see.
            var edited = _all.Select(r => r.Model).ToList();

            var problems = RateEditorSeed.Validate(edited);
            if (problems.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (string p in problems.Take(10)) sb.AppendLine("- " + p);
                sb.AppendLine();
                sb.AppendLine("Save anyway?");
                if (MessageBox.Show(this, sb.ToString(), "STING — Price commodities",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return false;
            }

            var merged = RateEditorSeed.Merge(_existingProject, edited,
                                              out int added, out int changed, out int untouched);

            if (added == 0 && changed == 0)
            {
                MessageBox.Show(this,
                    "Nothing was changed, so nothing was written.\n\n"
                  + "Type a number in the 'New rate UGX' column to price a row. A rate of 0 is not "
                  + "written — the resolver ignores a zero project rate, so the file would look like "
                  + "a decision and behave like an absence.",
                    "STING — Price commodities", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(_targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string note = $"Last edited {DateTime.Now:yyyy-MM-dd HH:mm} by "
                            + (Environment.UserName ?? "?")
                            + $" — {added} added, {changed} changed.";
                string text = string.Join(Environment.NewLine,
                                  CommodityRateResolver.WriteCsv(merged, note))
                            + Environment.NewLine;

                // Atomic: a crash mid-write must not leave a half-parsed rate
                // file, because the next export would price from it silently.
                string tmp = _targetPath + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(_targetPath))
                {
                    string bak = _targetPath + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Replace(tmp, _targetPath, bak);
                }
                else File.Move(tmp, _targetPath);

                StingLog.Info($"CommodityRateEditor: {added} added, {changed} changed, "
                            + $"{untouched} untouched -> {_targetPath}");
                Saved = true;

                MessageBox.Show(this,
                    $"{added} rate(s) added, {changed} changed, {untouched} left alone.\n\n"
                  + _targetPath
                  + "\n\nRe-run the material schedule export to price the schedule with these rates.",
                    "STING — Price commodities", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("CommodityRateEditor save", ex);
                MessageBox.Show(this, "Save failed:\n\n" + ex.Message,
                    "STING — Price commodities", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>Open the editor over a built schedule. Returns true if rates were written.</summary>
        public static bool ShowDialog(Document doc, MaterialScheduleDocument schedule,
                                      List<CommodityRate> existingProject, string targetPath,
                                      SupplierUnitTable units, string patchPath)
        {
            var seed = RateEditorSeed.Build(schedule);
            if (seed.Rows.Count == 0)
            {
                MessageBox.Show(seed.Summary(), "STING — Price commodities",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var w = new CommodityRateEditor(doc, seed, existingProject, targetPath,
                                            units, patchPath, schedule);
            w.ShowDialog();
            return w.Saved;
        }
    }
}
