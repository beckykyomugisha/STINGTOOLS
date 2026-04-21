using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
// Disambiguate WPF types from identically-named Revit API types when both
// namespaces are imported in this file:
//   Color   — Autodesk.Revit.DB.Color vs System.Windows.Media.Color
//   Grid    — Autodesk.Revit.DB.Grid (grid line) vs System.Windows.Controls.Grid
//   Binding — Autodesk.Revit.DB.Binding (parameter binding) vs System.Windows.Data.Binding
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  TitleBlockCsvEditor — in-Revit WPF editor for TITLE_BLOCK.csv
    //
    //  Replaces the "edit the CSV in Notepad" workflow with a corporate-themed
    //  editable DataGrid inside Revit. Loads the project-adjacent
    //  STING_BIM_MANAGER/TITLE_BLOCK.csv (falling back to the shipped template
    //  under the plugin Data/ folder), lets coordinators add/remove/edit rows
    //  per discipline, and writes the file back atomically on save.
    //
    //  Dialog layout:
    //    - Header strip: path + dirty flag
    //    - DataGrid: 11 columns (ParameterName, DefaultValue, ARCH, STR, MEP,
    //      ELE, PLM, FP, LV, COORD, GEN) all editable, row add/delete allowed
    //    - Footer: Add Row / Duplicate / Delete / Reset / Save / Save & Run /
    //      Cancel buttons
    //
    //  Actions:
    //    - Save           →  write CSV to project-adjacent path
    //    - Save & Populate→  save then fire TitleBlockPopulateCommand
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One editable row in the title-block CSV. All 11 columns bind to this
    /// via INotifyPropertyChanged so the DataGrid + dirty flag stay in sync.
    /// </summary>
    internal class TitleBlockCsvRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _parameterName = "";
        public string ParameterName
        {
            get => _parameterName;
            set { if (_parameterName != value) { _parameterName = value ?? ""; Raise(nameof(ParameterName)); } }
        }
        private string _defaultValue = "";
        public string DefaultValue
        {
            get => _defaultValue;
            set { if (_defaultValue != value) { _defaultValue = value ?? ""; Raise(nameof(DefaultValue)); } }
        }

        // Discipline columns — keep as individual properties so DataGrid binds
        // directly without needing a CollectionView per cell.
        private string _arch  = ""; public string ARCH  { get => _arch;  set { if (_arch  != value) { _arch  = value ?? ""; Raise(nameof(ARCH));  } } }
        private string _str   = ""; public string STR   { get => _str;   set { if (_str   != value) { _str   = value ?? ""; Raise(nameof(STR));   } } }
        private string _mep   = ""; public string MEP   { get => _mep;   set { if (_mep   != value) { _mep   = value ?? ""; Raise(nameof(MEP));   } } }
        private string _ele   = ""; public string ELE   { get => _ele;   set { if (_ele   != value) { _ele   = value ?? ""; Raise(nameof(ELE));   } } }
        private string _plm   = ""; public string PLM   { get => _plm;   set { if (_plm   != value) { _plm   = value ?? ""; Raise(nameof(PLM));   } } }
        private string _fp    = ""; public string FP    { get => _fp;    set { if (_fp    != value) { _fp    = value ?? ""; Raise(nameof(FP));    } } }
        private string _lv    = ""; public string LV    { get => _lv;    set { if (_lv    != value) { _lv    = value ?? ""; Raise(nameof(LV));    } } }
        private string _coord = ""; public string COORD { get => _coord; set { if (_coord != value) { _coord = value ?? ""; Raise(nameof(COORD)); } } }
        private string _gen   = ""; public string GEN   { get => _gen;   set { if (_gen   != value) { _gen   = value ?? ""; Raise(nameof(GEN));   } } }

        /// <summary>Get the per-discipline override by short code (case-insensitive).</summary>
        public string this[string disc]
        {
            get
            {
                switch ((disc ?? "").Trim().ToUpperInvariant())
                {
                    case "ARCH":  return ARCH;
                    case "STR":   return STR;
                    case "MEP":   return MEP;
                    case "ELE":   return ELE;
                    case "PLM":   return PLM;
                    case "FP":    return FP;
                    case "LV":    return LV;
                    case "COORD": return COORD;
                    case "GEN":   return GEN;
                    default: return "";
                }
            }
            set
            {
                switch ((disc ?? "").Trim().ToUpperInvariant())
                {
                    case "ARCH":  ARCH  = value; break;
                    case "STR":   STR   = value; break;
                    case "MEP":   MEP   = value; break;
                    case "ELE":   ELE   = value; break;
                    case "PLM":   PLM   = value; break;
                    case "FP":    FP    = value; break;
                    case "LV":    LV    = value; break;
                    case "COORD": COORD = value; break;
                    case "GEN":   GEN   = value; break;
                }
            }
        }
    }
}

namespace StingTools.Docs
{
    /// <summary>
    /// Editable WPF DataGrid for TITLE_BLOCK.csv. Invoked by
    /// TitleBlockEditCsvCommand and by TitleBlockPopulateCommand when it
    /// can't find a usable CSV.
    /// </summary>
    internal partial class TitleBlockCsvEditor : Window
    {
        private static readonly Color HeaderBg = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentBg = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color AltRowBg = Color.FromRgb(0xF7, 0xF7, 0xF9);
        private static readonly Color FooterBg = Color.FromRgb(0xF0, 0xF0, 0xF5);
        private static readonly Color BorderGrey = Color.FromRgb(0xCC, 0xCC, 0xD0);

        private readonly Document _doc;
        private readonly ObservableCollection<TitleBlockCsvRow> _rows
            = new ObservableCollection<TitleBlockCsvRow>();
        private readonly DataGrid _grid;
        private readonly TextBlock _pathLabel;
        private readonly TextBlock _dirtyLabel;
        private readonly TextBlock _statusText;
        private string _currentPath;
        private bool _dirty;
        internal bool SavedAndRun { get; private set; }

        internal TitleBlockCsvEditor(Document doc)
        {
            _doc = doc;
            Title = "STING — Title Block CSV Editor";
            Width = 1180;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFC));

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header ──
            var header = new Border
            {
                Background = new SolidColorBrush(HeaderBg),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var hStack = new StackPanel();
            hStack.Children.Add(new TextBlock
            {
                Text = "Title Block CSV Editor",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            hStack.Children.Add(new TextBlock
            {
                Text = "Edit PRJ_TB_* values per discipline — columns ARCH through GEN override DefaultValue",
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF))
            });
            header.Child = hStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Path / dirty strip ──
            var pathBar = new Border
            {
                Background = new SolidColorBrush(AltRowBg),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 6, 16, 6)
            };
            var pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pathLabel = new TextBlock
            {
                Text = "", FontSize = 11, FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x68)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_pathLabel, 0);
            pathGrid.Children.Add(_pathLabel);

            _dirtyLabel = new TextBlock
            {
                Text = "", FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBg),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_dirtyLabel, 1);
            pathGrid.Children.Add(_dirtyLabel);

            pathBar.Child = pathGrid;
            Grid.SetRow(pathBar, 1);
            root.Children.Add(pathBar);

            // ── DataGrid ──
            _grid = new DataGrid
            {
                Margin = new Thickness(12, 10, 12, 8),
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserResizeRows = false,
                CanUserSortColumns = true,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.Cell,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush(AltRowBg),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                ItemsSource = _rows
            };
            BuildColumns(_grid);
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            // ── Footer button bar ──
            var footer = new Border
            {
                Background = new SolidColorBrush(FooterBg),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var fGrid = new Grid();
            fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x64, 0x78))
            };
            Grid.SetColumn(_statusText, 0);
            fGrid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            btnPanel.Children.Add(MakeBtn("Add Row", "Add a blank parameter row", AddRow));
            btnPanel.Children.Add(MakeBtn("Duplicate", "Copy selected row", DuplicateRow));
            btnPanel.Children.Add(MakeBtn("Delete", "Remove selected rows", DeleteSelected));
            btnPanel.Children.Add(MakeBtn("Reset to Shipped", "Reload the template shipped with the plugin", ResetFromShipped));
            btnPanel.Children.Add(MakeBtn("Reload", "Reload current CSV from disk (discards changes)", ReloadFromDisk));
            btnPanel.Children.Add(new Border { Width = 14 });
            btnPanel.Children.Add(MakeBtn("Save", "Write CSV to project STING_BIM_MANAGER/TITLE_BLOCK.csv", Save, accent: true));
            btnPanel.Children.Add(MakeBtn("Save & Populate", "Save then run TitleBlockPopulate", SaveAndRun, accent: true));
            btnPanel.Children.Add(MakeBtn("Cancel", "Close without saving", Cancel));
            Grid.SetColumn(btnPanel, 1);
            fGrid.Children.Add(btnPanel);

            footer.Child = fGrid;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;

            // Set Revit as owner (matches StingDataGridDialog pattern)
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"TB Editor: owner set failed: {ex.Message}"); }

            _rows.CollectionChanged += (s, e) => MarkDirty();
            KeyDown += OnKey;
        }
    }
}

namespace StingTools.Docs
{
    internal partial class TitleBlockCsvEditor
    {
        // ── Column factory ──────────────────────────────────────────────────
        private static void BuildColumns(DataGrid grid)
        {
            // Parameter name column (wider)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Parameter Name",
                Binding = new Binding(nameof(TitleBlockCsvRow.ParameterName))
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Mode = BindingMode.TwoWay
                },
                Width = 280,
                IsReadOnly = false
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Default Value",
                Binding = new Binding(nameof(TitleBlockCsvRow.DefaultValue))
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Mode = BindingMode.TwoWay
                },
                Width = 180,
                IsReadOnly = false
            });
            foreach (string disc in new[] { "ARCH","STR","MEP","ELE","PLM","FP","LV","COORD","GEN" })
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = disc,
                    Binding = new Binding(disc)
                    {
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                        Mode = BindingMode.TwoWay
                    },
                    MinWidth = 90,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    IsReadOnly = false
                });
            }
        }

        // ── Button factory (matches corporate theme) ─────────────────────────
        private Button MakeBtn(string label, string tip, Action click, bool accent = false)
        {
            var b = new Button
            {
                Content = label,
                ToolTip = tip,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(4, 0, 0, 0),
                MinWidth = 86,
                FontSize = 12,
                Cursor = Cursors.Hand,
                Background = accent
                    ? new SolidColorBrush(AccentBg)
                    : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFC)),
                Foreground = accent ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44)),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(1),
                FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal
            };
            b.Click += (s, e) =>
            {
                try { click?.Invoke(); }
                catch (Exception ex)
                {
                    StingLog.Error($"TB Editor: {label} action failed", ex);
                    MessageBox.Show(this, $"Action failed:\n\n{ex.Message}",
                        "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            return b;
        }

        // ── Dirty-flag plumbing ──────────────────────────────────────────────
        private void MarkDirty()
        {
            _dirty = true;
            if (_dirtyLabel != null) _dirtyLabel.Text = "●  unsaved changes";
            if (_statusText != null)
                _statusText.Text = $"{_rows.Count} row(s) — edited";
        }
        private void MarkClean()
        {
            _dirty = false;
            if (_dirtyLabel != null) _dirtyLabel.Text = "";
            if (_statusText != null)
                _statusText.Text = $"{_rows.Count} row(s)";
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            { Save(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; return; }
            if (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.None)
            { AddRow(); e.Handled = true; return; }
            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Shift)
            { DeleteSelected(); e.Handled = true; return; }
        }

        // ── CSV load / save ─────────────────────────────────────────────────
        internal void LoadFrom(string path)
        {
            _currentPath = path;
            _rows.Clear();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _pathLabel.Text = $"(no CSV — will create on save) — proposed: {GetProjectAdjacentPath() ?? "(unsaved project)"}";
                MarkClean();
                return;
            }
            try
            {
                var lines = File.ReadAllLines(path);
                // Header row tells us which discipline columns exist; however we
                // always expose the canonical nine columns on the grid. Values
                // from the file are mapped positionally by header name.
                if (lines.Length < 1) { _pathLabel.Text = path; MarkClean(); return; }
                string[] header = SplitCsv(lines[0]);
                var discColMap = new Dictionary<int, string>();
                for (int i = 2; i < header.Length; i++)
                    discColMap[i] = header[i].Trim().ToUpperInvariant();

                for (int r = 1; r < lines.Length; r++)
                {
                    string ln = lines[r];
                    if (string.IsNullOrWhiteSpace(ln) || ln.TrimStart().StartsWith("#")) continue;
                    string[] cols = SplitCsv(ln);
                    if (cols.Length == 0) continue;
                    var row = new TitleBlockCsvRow
                    {
                        ParameterName = cols.Length > 0 ? cols[0] : "",
                        DefaultValue  = cols.Length > 1 ? cols[1] : ""
                    };
                    foreach (var kvp in discColMap)
                    {
                        if (kvp.Key < cols.Length)
                            row[kvp.Value] = cols[kvp.Key];
                    }
                    _rows.Add(row);
                }
                _pathLabel.Text = path;
                MarkClean();
                StingLog.Info($"TB Editor: loaded {_rows.Count} row(s) from {path}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"TB Editor: load failed: {path}", ex);
                MessageBox.Show(this, $"Could not read CSV:\n\n{path}\n\n{ex.Message}",
                    "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string BuildCsvText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ParameterName,DefaultValue,ARCH,STR,MEP,ELE,PLM,FP,LV,COORD,GEN");
            foreach (var r in _rows)
            {
                if (string.IsNullOrWhiteSpace(r.ParameterName)) continue; // drop empty
                sb.Append(Esc(r.ParameterName)).Append(',');
                sb.Append(Esc(r.DefaultValue)).Append(',');
                sb.Append(Esc(r.ARCH)).Append(',');
                sb.Append(Esc(r.STR)).Append(',');
                sb.Append(Esc(r.MEP)).Append(',');
                sb.Append(Esc(r.ELE)).Append(',');
                sb.Append(Esc(r.PLM)).Append(',');
                sb.Append(Esc(r.FP)).Append(',');
                sb.Append(Esc(r.LV)).Append(',');
                sb.Append(Esc(r.COORD)).Append(',');
                sb.Append(Esc(r.GEN));
                sb.Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        private static string Esc(string v)
        {
            if (v == null) return "";
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        private static string[] SplitCsv(string line)
        {
            // Mirror TitleBlockCsv.ParseCsvLine — quoted fields + escaped quotes
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result.ToArray();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else q = !q;
                }
                else if (c == ',' && !q) { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}

namespace StingTools.Docs
{
    internal partial class TitleBlockCsvEditor
    {
        // ── Action handlers (button clicks + keyboard shortcuts) ─────────────
        private void AddRow()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var row = new TitleBlockCsvRow { ParameterName = "PRJ_TB_" };
            _rows.Add(row);
            _grid.ScrollIntoView(row);
            _grid.SelectedItem = row;
        }

        private void DuplicateRow()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!(_grid.SelectedItem is TitleBlockCsvRow src))
            {
                if (_rows.Count == 0) { AddRow(); return; }
                src = _rows[_rows.Count - 1];
            }
            var dup = new TitleBlockCsvRow
            {
                ParameterName = src.ParameterName,
                DefaultValue = src.DefaultValue,
                ARCH = src.ARCH, STR = src.STR, MEP = src.MEP, ELE = src.ELE,
                PLM = src.PLM, FP = src.FP, LV = src.LV, COORD = src.COORD, GEN = src.GEN
            };
            int idx = _rows.IndexOf(src);
            if (idx < 0) _rows.Add(dup); else _rows.Insert(idx + 1, dup);
            _grid.SelectedItem = dup;
            _grid.ScrollIntoView(dup);
        }

        private void DeleteSelected()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = _grid.SelectedCells
                .Select(c => c.Item as TitleBlockCsvRow)
                .Where(r => r != null)
                .Distinct()
                .ToList();
            if (rows.Count == 0 && _grid.SelectedItem is TitleBlockCsvRow r0)
                rows.Add(r0);
            if (rows.Count == 0) return;
            if (MessageBox.Show(this,
                $"Delete {rows.Count} row(s)?",
                "STING Title Block Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            foreach (var r in rows) _rows.Remove(r);
        }

        private void ResetFromShipped()
        {
            if (_dirty && MessageBox.Show(this,
                "Discard unsaved changes and load the shipped template?",
                "STING Title Block Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            string shipped = StingToolsApp.FindDataFile("TITLE_BLOCK.csv");
            if (string.IsNullOrEmpty(shipped) || !File.Exists(shipped))
            {
                MessageBox.Show(this,
                    "Could not locate shipped TITLE_BLOCK.csv template in the plugin Data/ folder.",
                    "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            LoadFrom(shipped);
            // Mark as dirty since the in-memory state diverges from the project-local CSV
            MarkDirty();
        }

        private void ReloadFromDisk()
        {
            if (_dirty && MessageBox.Show(this,
                "Discard unsaved changes and reload from disk?",
                "STING Title Block Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            LoadFrom(_currentPath ?? GetProjectAdjacentPath() ?? StingToolsApp.FindDataFile("TITLE_BLOCK.csv"));
        }

        private void Save()
        {
            if (!SaveCore(runPopulate: false)) return;
            MessageBox.Show(this,
                $"Saved:\n{_currentPath}\n\n{_rows.Count} row(s) written.",
                "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveAndRun()
        {
            if (!SaveCore(runPopulate: true)) return;
            SavedAndRun = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Common save path. Always writes to the project-adjacent
        /// STING_BIM_MANAGER/TITLE_BLOCK.csv — never back to the shipped
        /// template. Returns false on cancel/failure.
        /// </summary>
        private bool SaveCore(bool runPopulate)
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            // Validate — warn on blank parameter names and non-PRJ_TB_ rows
            var blanks = _rows.Count(r => string.IsNullOrWhiteSpace(r.ParameterName));
            var dups = _rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName))
                .GroupBy(r => r.ParameterName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (blanks > 0 || dups.Count > 0)
            {
                var sb = new StringBuilder();
                if (blanks > 0) sb.AppendLine($"- {blanks} row(s) have empty parameter names and will be skipped.");
                if (dups.Count > 0) sb.AppendLine($"- Duplicate parameter names: {string.Join(", ", dups)}");
                sb.AppendLine();
                sb.AppendLine("Save anyway?");
                if (MessageBox.Show(this, sb.ToString(),
                    "STING Title Block Editor — Validation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return false;
            }

            string target = GetProjectAdjacentPath();
            if (string.IsNullOrEmpty(target))
            {
                MessageBox.Show(this,
                    "This project has not been saved yet — STING_BIM_MANAGER/TITLE_BLOCK.csv cannot be created.\n\n" +
                    "Save the Revit project first, then re-open the editor.",
                    "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Atomic write: temp file + Replace so a crash mid-write can't
                // corrupt the CSV coordinators rely on.
                string tmp = target + ".tmp";
                File.WriteAllText(tmp, BuildCsvText(), new UTF8Encoding(false));
                if (File.Exists(target))
                {
                    string bak = target + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Replace(tmp, target, bak);
                }
                else
                {
                    File.Move(tmp, target);
                }
                _currentPath = target;
                _pathLabel.Text = target;
                MarkClean();
                StingLog.Info($"TB Editor: saved {_rows.Count} row(s) to {target}");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("TB Editor: save failed", ex);
                MessageBox.Show(this,
                    $"Save failed:\n\n{ex.Message}",
                    "STING Title Block Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Cancel()
        {
            if (_dirty)
            {
                var r = MessageBox.Show(this,
                    "Discard unsaved changes?",
                    "STING Title Block Editor",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Resolve STING_BIM_MANAGER/TITLE_BLOCK.csv next to the open .rvt.
        /// Returns null if the project has never been saved.
        /// </summary>
        internal string GetProjectAdjacentPath()
        {
            try
            {
                string projPath = _doc?.PathName;
                if (string.IsNullOrEmpty(projPath)) return null;
                string dir = Path.GetDirectoryName(projPath);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "STING_BIM_MANAGER", "TITLE_BLOCK.csv");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB Editor: GetProjectAdjacentPath failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Entry point used by the IExternalCommand wrapper.</summary>
        internal static (bool Saved, bool RunPopulate, string Path) ShowDialog(Document doc)
        {
            var dlg = new TitleBlockCsvEditor(doc);
            // Prefer project-adjacent CSV; fall back to shipped template so the
            // user starts with the 32 seeded rows instead of a blank grid.
            string projLocal = dlg.GetProjectAdjacentPath();
            string openFrom = (!string.IsNullOrEmpty(projLocal) && File.Exists(projLocal))
                ? projLocal
                : StingToolsApp.FindDataFile("TITLE_BLOCK.csv");
            dlg.LoadFrom(openFrom);
            bool? res = dlg.ShowDialog();
            return (res == true, dlg.SavedAndRun, dlg._currentPath);
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  IExternalCommand entry — "Edit CSV" button on the DOCS tab.
    //
    //  If the user clicks "Save & Populate" in the editor, we dispatch the
    //  TitleBlockPopulateCommand immediately so the edit → populate round trip
    //  is one click instead of two.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockEditCsvCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var outcome = TitleBlockCsvEditor.ShowDialog(doc);
            if (!outcome.Saved) return Result.Cancelled;
            if (outcome.RunPopulate)
            {
                // Dispatch TitleBlockPopulate on the same UIApplication — the
                // user asked for "save & populate" so we run it inline.
                try
                {
                    var pop = new TitleBlockPopulateCommand();
                    string msg = null;
                    pop.Execute(commandData, ref msg, elements);
                }
                catch (Exception ex)
                {
                    StingLog.Error("TB Editor: post-save populate failed", ex);
                }
            }
            return Result.Succeeded;
        }
    }
}
