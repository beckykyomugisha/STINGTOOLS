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

        /// <summary>"unpriced" reads as a state; the others say where the number came from.</summary>
        public string Source => string.IsNullOrWhiteSpace(_row.CurrentSource) ? "—" : _row.CurrentSource;

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

        public bool Saved { get; private set; }

        private CommodityRateEditor(Document doc, RateSeedResult seed,
                                    List<CommodityRate> existingProject, string targetPath)
        {
            _doc = doc;
            _existingProject = existingProject ?? new List<CommodityRate>();
            _targetPath = targetPath;

            foreach (var r in seed.Rows) _rows.Add(new RateEditorRowVm(r));

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
                Text = "Writes " + (_targetPath ?? "(project not saved)"),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11,
                Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis
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

            // ── grid ──
            _grid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(12, 10, 12, 0),
                RowBackground = Brushes.Transparent
            };
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                new System.Windows.Data.Binding(nameof(RateEditorRowVm.RowBrush))));
            _grid.RowStyle = rowStyle;

            AddReadOnly("Description", nameof(RateEditorRowVm.Description), 300);
            AddReadOnly("Unit", nameof(RateEditorRowVm.SupplierUnit), 130);
            AddReadOnly("Order qty", nameof(RateEditorRowVm.Quantity), 90);
            AddReadOnly("Rate now", nameof(RateEditorRowVm.CurrentRate), 90);
            AddReadOnly("Source", nameof(RateEditorRowVm.Source), 80);
            AddReadOnly("Amount", nameof(RateEditorRowVm.Amount), 110);
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

        private void AddReadOnly(string header, string path, double width) =>
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path),
                Width = width,
                IsReadOnly = true
            });

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

            var edited = _rows.Select(r => r.Model).ToList();

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
                                      List<CommodityRate> existingProject, string targetPath)
        {
            var seed = RateEditorSeed.Build(schedule);
            if (seed.Rows.Count == 0)
            {
                MessageBox.Show(seed.Summary(), "STING — Price commodities",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var w = new CommodityRateEditor(doc, seed, existingProject, targetPath);
            w.ShowDialog();
            return w.Saved;
        }
    }
}
