using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Sheet creation dialog with editable Excel-like grid.
    /// Shows all parameters bound to sheets, dependent views, scope boxes.
    /// Users can create one or many sheets in a single batch with full control
    /// over sheet number, name, discipline, title block, dependent views, and scope boxes.
    /// </summary>
    public class NewSheetDialog : Window
    {
        // ── Theme colours (matching Sheet Manager) ───────────────────
        private static readonly SolidColorBrush BrBgLight = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush BrBgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrBgHeader = new(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly SolidColorBrush BrAccent = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrFgDark = new(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush BrFgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrFgSubtle = new(Color.FromRgb(0x77, 0x77, 0x77));
        private static readonly SolidColorBrush BrBorder = new(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidColorBrush BrGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrRed = new(Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush BrBlue = new(Color.FromRgb(0x21, 0x96, 0xF3));

        // ── Data ─────────────────────────────────────────────────────
        private readonly ObservableCollection<SheetRowData> _rows = new();
        private DataGrid _grid;
        private TextBlock _statusText;

        // ── Available options (populated by caller) ──────────────────
        private List<string> _titleBlockNames = new();
        private List<string> _scopeBoxNames = new();
        private List<string> _dependentViewNames = new();
        private List<string> _viewTemplateNames = new();
        private List<string> _disciplineCodes = new() { "A", "S", "M", "E", "P", "FP", "C", "G" };
        private List<string> _sheetParamNames = new();

        // ── Drag-fill state ──
        private bool _isDragFilling;

        /// <summary>Result: list of sheet rows to create (null if cancelled).</summary>
        public List<SheetRowData> ResultRows { get; private set; }

        /// <summary>Whether to auto-place dependent views on created sheets.</summary>
        public bool AutoPlaceDependentViews { get; private set; }

        public NewSheetDialog()
        {
            Title = "STING — Create New Sheets";
            Width = 1100;
            Height = 620;
            MinWidth = 800;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrBgLight;
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            BuildUI();
        }

        // ────────────────────────────────────────────────────────────
        //  PUBLIC API: Set available options before showing
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Configure the dialog with project data before showing.
        /// </summary>
        public void Configure(
            List<string> titleBlockNames,
            List<string> scopeBoxNames,
            List<string> dependentViewNames,
            List<string> viewTemplateNames,
            string defaultTitleBlock = null,
            string defaultDiscipline = "A",
            string suggestedNextNumber = null,
            List<string> sheetParamNames = null)
        {
            _titleBlockNames = titleBlockNames ?? new();
            _scopeBoxNames = scopeBoxNames ?? new();
            _dependentViewNames = dependentViewNames ?? new();
            _viewTemplateNames = viewTemplateNames ?? new();
            _sheetParamNames = sheetParamNames ?? new();

            // Add one default row
            _rows.Add(new SheetRowData
            {
                SheetNumber = suggestedNextNumber ?? "A-001",
                SheetName = "New Sheet",
                Discipline = defaultDiscipline,
                TitleBlock = defaultTitleBlock ?? (_titleBlockNames.Count > 0 ? _titleBlockNames[0] : ""),
                ScopeBox = "",
                DependentViews = "",
                ViewTemplate = "",
                Scale = "1:100",
                Revision = "P01"
            });

            // Rebuild combo columns with actual options
            RebuildGrid();
            UpdateStatus();
        }

        // ────────────────────────────────────────────────────────────
        //  STATIC SHOW HELPER
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows the dialog modally and returns the configured sheet rows,
        /// or null if cancelled.
        /// </summary>
        public static List<SheetRowData> Show(
            List<string> titleBlockNames,
            List<string> scopeBoxNames,
            List<string> dependentViewNames,
            List<string> viewTemplateNames,
            out bool autoPlaceDependentViews,
            string defaultTitleBlock = null,
            string defaultDiscipline = "A",
            string suggestedNextNumber = null,
            List<string> sheetParamNames = null)
        {
            var dlg = new NewSheetDialog();
            dlg.Configure(titleBlockNames, scopeBoxNames, dependentViewNames,
                viewTemplateNames, defaultTitleBlock, defaultDiscipline, suggestedNextNumber, sheetParamNames);
            bool? ok = dlg.ShowDialog();
            autoPlaceDependentViews = dlg.AutoPlaceDependentViews;
            return ok == true ? dlg.ResultRows : null;
        }

        // ────────────────────────────────────────────────────────────
        //  UI CONSTRUCTION
        // ────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 0: Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 1: Toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 3: Options
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // 4: Buttons
            Content = root;

            // ── 0: Header ──
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Create New Sheets",
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = BrFgWhite
            });
            headerStack.Children.Add(new TextBlock
            {
                // BrFgSubtle (#777) is unreadable on the dark banner;
                // use a near-white muted grey for the subtitle so it
                // clears WCAG contrast against BrBgHeader.
                Text = "Add rows for each sheet. Edit cells directly. Use toolbar to add/remove/duplicate rows.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD8, 0xDC)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── 1: Toolbar ──
            var toolbar = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var tbWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            tbWrap.Children.Add(MakeToolBtn("\u2795 Add Row", "AddRow", BrGreen));
            tbWrap.Children.Add(MakeToolBtn("\U0001F4CB Duplicate Row", "DuplicateRow", BrBlue));
            tbWrap.Children.Add(MakeToolBtn("\u2796 Remove Row", "RemoveRow", BrRed));
            tbWrap.Children.Add(MakeSeparator());
            tbWrap.Children.Add(MakeToolBtn("\U0001F4C4 Add 5 Rows", "Add5Rows", BrAccent));
            tbWrap.Children.Add(MakeToolBtn("\U0001F522 Auto-Number", "AutoNumber", BrAccent));
            tbWrap.Children.Add(MakeSeparator());
            tbWrap.Children.Add(MakeToolBtn("\u274C Clear All", "ClearAll", BrFgSubtle));

            toolbar.Child = tbWrap;
            Grid.SetRow(toolbar, 1);
            root.Children.Add(toolbar);

            // ── 2: DataGrid (placeholder — rebuilt in RebuildGrid) ──
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = BrBorder,
                VerticalGridLinesBrush = BrBorder,
                RowHeaderWidth = 28,
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 4, 8, 4),
                FontSize = 12,
                ItemsSource = _rows,
                ColumnHeaderHeight = 30,
                RowHeight = 26
            };

            // Style header
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3F, 0x51, 0xB5))));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, BrFgWhite));
            headerStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 4, 6, 4)));
            headerStyle.Setters.Add(new Setter(FontSizeProperty, 11.5));
            headerStyle.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x30, 0x40, 0x90))));
            headerStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            _grid.ColumnHeaderStyle = headerStyle;

            // Alternating row colours
            _grid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xFC));
            _grid.RowBackground = BrBgWhite;

            // ── Wire up Excel-like editing event handlers ──
            _grid.CellEditEnding += OnCellEditEnding;
            _grid.PreviewKeyDown += OnGridKeyDown;
            _grid.PreviewMouseLeftButtonDown += OnGridPreviewMouseLeftButtonDown;
            _grid.PreviewMouseMove += OnGridPreviewMouseMove;
            _grid.PreviewMouseLeftButtonUp += OnGridPreviewMouseLeftButtonUp;

            // ── Drag-fill cursor: show crosshair near cell bottom-right ──
            _grid.MouseMove += (s, ev) =>
            {
                var hit = VisualTreeHelper.HitTest(_grid, ev.GetPosition(_grid));
                if (hit?.VisualHit == null) return;
                var cell = FindParent<DataGridCell>(hit.VisualHit as DependencyObject);
                if (cell != null && !cell.Column.IsReadOnly)
                {
                    var pos = ev.GetPosition(cell);
                    if (pos.X >= cell.ActualWidth - 8 && pos.Y >= cell.ActualHeight - 8)
                        _grid.Cursor = Cursors.Cross;
                    else
                        _grid.Cursor = Cursors.Arrow;
                }
            };

            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            // ── 3: Options row ──
            var optPanel = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var optWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var chkAutoPlace = new CheckBox
            {
                Content = "Auto-place dependent views on sheets",
                IsChecked = true,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0),
                FontSize = 12
            };
            chkAutoPlace.Checked += (s, e) => AutoPlaceDependentViews = true;
            chkAutoPlace.Unchecked += (s, e) => AutoPlaceDependentViews = false;
            AutoPlaceDependentViews = true;
            optWrap.Children.Add(chkAutoPlace);

            _statusText = new TextBlock
            {
                Text = "0 sheets to create",
                FontSize = 11, Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            optWrap.Children.Add(_statusText);

            optPanel.Child = optWrap;
            Grid.SetRow(optPanel, 3);
            root.Children.Add(optPanel);

            // ── 4: Action buttons ──
            var btnPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnCancel = new Button
            {
                Content = "Cancel", Width = 90, Height = 30,
                Margin = new Thickness(0, 0, 8, 0), FontSize = 12,
                Background = BrBgWhite, Foreground = BrFgDark,
                BorderBrush = BrBorder
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };

            var btnCreate = new Button
            {
                Content = "Create Sheets", Width = 120, Height = 30,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Background = BrGreen, Foreground = BrFgWhite,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C))
            };
            btnCreate.Click += OnCreateClick;

            btnStack.Children.Add(btnCancel);
            btnStack.Children.Add(btnCreate);
            btnPanel.Child = btnStack;
            Grid.SetRow(btnPanel, 4);
            root.Children.Add(btnPanel);
        }

        // ────────────────────────────────────────────────────────────
        //  GRID COLUMN SETUP
        // ────────────────────────────────────────────────────────────

        private void RebuildGrid()
        {
            _grid.Columns.Clear();

            // 1. Row number (read-only)
            var rowNumCol = new DataGridTextColumn
            {
                Header = "#",
                Binding = new Binding { Mode = BindingMode.OneWay, Converter = new RowIndexConverter(_rows) },
                Width = new DataGridLength(35),
                IsReadOnly = true
            };
            _grid.Columns.Add(rowNumCol);

            // 2. Sheet Number (editable text)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Sheet Number",
                Binding = new Binding("SheetNumber") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(110)
            });

            // 3. Sheet Name (editable text)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Sheet Name",
                Binding = new Binding("SheetName") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(200)
            });

            // 4. Discipline (combo)
            _grid.Columns.Add(MakeComboColumn("Discipline", "Discipline", _disciplineCodes, 80));

            // 5. Title Block (combo)
            _grid.Columns.Add(MakeComboColumn("Title Block", "TitleBlock", _titleBlockNames, 160));

            // 6. Scope Box (combo with empty option)
            var scopeOptions = new List<string> { "(None)" };
            scopeOptions.AddRange(_scopeBoxNames);
            _grid.Columns.Add(MakeComboColumn("Scope Box", "ScopeBox", scopeOptions, 130));

            // 7. Dependent Views (editable text — semicolon-separated)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Dependent Views",
                Binding = new Binding("DependentViews") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(160)
            });

            // 8. View Template (combo with empty option)
            var templateOptions = new List<string> { "(None)" };
            templateOptions.AddRange(_viewTemplateNames);
            _grid.Columns.Add(MakeComboColumn("View Template", "ViewTemplate", templateOptions, 140));

            // 9. Scale (editable text)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Scale",
                Binding = new Binding("Scale") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(70)
            });

            // 10. Revision (editable text)
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Revision",
                Binding = new Binding("Revision") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(70)
            });

            // 11+. Custom shared parameters bound to sheets
            foreach (string paramName in _sheetParamNames)
            {
                // Use a converter to read/write from the CustomParams dictionary
                _grid.Columns.Add(new DataGridTextColumn
                {
                    Header = paramName,
                    Binding = new Binding
                    {
                        Path = new PropertyPath("."),
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                        Converter = new CustomParamConverter(paramName)
                    },
                    Width = new DataGridLength(120)
                });
            }
        }

        private DataGridComboBoxColumn MakeComboColumn(string header, string binding, List<string> items, int width)
        {
            return new DataGridComboBoxColumn
            {
                Header = header,
                SelectedItemBinding = new Binding(binding) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                ItemsSource = items,
                Width = new DataGridLength(width)
            };
        }

        // ────────────────────────────────────────────────────────────
        //  TOOLBAR HANDLERS
        // ────────────────────────────────────────────────────────────

        private Button MakeToolBtn(string text, string tag, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = text,
                Tag = tag,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(2),
                Background = BrBgWhite,
                Foreground = fg,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand
            };
            btn.Click += OnToolbarClick;
            return btn;
        }

        private static Separator MakeSeparator()
        {
            return new Separator
            {
                Width = 1, Margin = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0))
            };
        }

        private void OnToolbarClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string action = btn.Tag as string ?? "";

            switch (action)
            {
                case "AddRow":
                    AddNewRow();
                    break;

                case "DuplicateRow":
                    DuplicateSelectedRow();
                    break;

                case "RemoveRow":
                    RemoveSelectedRows();
                    break;

                case "Add5Rows":
                    for (int i = 0; i < 5; i++) AddNewRow();
                    break;

                case "AutoNumber":
                    AutoNumberRows();
                    break;

                case "ClearAll":
                    _rows.Clear();
                    UpdateStatus();
                    break;
            }
        }

        private void AddNewRow()
        {
            string lastNum = _rows.Count > 0 ? _rows.Last().SheetNumber : "A-000";
            string lastDisc = _rows.Count > 0 ? _rows.Last().Discipline : "A";
            string lastTb = _rows.Count > 0 ? _rows.Last().TitleBlock : (_titleBlockNames.Count > 0 ? _titleBlockNames[0] : "");
            string lastScale = _rows.Count > 0 ? _rows.Last().Scale : "1:100";
            string lastRev = _rows.Count > 0 ? _rows.Last().Revision : "P01";

            string nextNum = IncrementSheetNumber(lastNum);

            _rows.Add(new SheetRowData
            {
                SheetNumber = nextNum,
                SheetName = "New Sheet",
                Discipline = lastDisc,
                TitleBlock = lastTb,
                ScopeBox = "",
                DependentViews = "",
                ViewTemplate = "",
                Scale = lastScale,
                Revision = lastRev
            });
            UpdateStatus();
        }

        private void DuplicateSelectedRow()
        {
            if (_grid.SelectedItem is not SheetRowData selected) return;
            string nextNum = IncrementSheetNumber(selected.SheetNumber);
            var dupRow = new SheetRowData
            {
                SheetNumber = nextNum,
                SheetName = selected.SheetName,
                Discipline = selected.Discipline,
                TitleBlock = selected.TitleBlock,
                ScopeBox = selected.ScopeBox,
                DependentViews = selected.DependentViews,
                ViewTemplate = selected.ViewTemplate,
                Scale = selected.Scale,
                Revision = selected.Revision
            };
            // Copy custom parameter values
            foreach (var kvp in selected.CustomParams)
                dupRow.CustomParams[kvp.Key] = kvp.Value;
            _rows.Add(dupRow);
            UpdateStatus();
        }

        private void RemoveSelectedRows()
        {
            var toRemove = _grid.SelectedItems.Cast<SheetRowData>().ToList();
            foreach (var row in toRemove)
                _rows.Remove(row);
            UpdateStatus();
        }

        private void AutoNumberRows()
        {
            if (_rows.Count == 0) return;

            // Group by discipline, number sequentially within each group
            var groups = _rows.GroupBy(r => r.Discipline).ToList();
            foreach (var grp in groups)
            {
                int seq = 1;
                foreach (var row in grp)
                {
                    row.SheetNumber = $"{row.Discipline}-{seq:D3}";
                    seq++;
                }
            }

            _grid.Items.Refresh();
        }

        // ────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────

        private static string IncrementSheetNumber(string number)
        {
            if (string.IsNullOrEmpty(number)) return "A-001";

            // Try to find trailing digits and increment
            int lastDash = number.LastIndexOf('-');
            if (lastDash >= 0 && lastDash < number.Length - 1)
            {
                string prefix = number.Substring(0, lastDash + 1);
                string numPart = number.Substring(lastDash + 1);
                if (int.TryParse(numPart, out int val))
                {
                    return prefix + (val + 1).ToString("D" + numPart.Length);
                }
            }

            // Fallback: just append "-001"
            return number + "-001";
        }

        private void UpdateStatus()
        {
            if (_statusText == null) return;
            int count = _rows.Count;
            int discCount = _rows.Select(r => r.Discipline).Distinct().Count();
            _statusText.Text = $"{count} sheet{(count == 1 ? "" : "s")} to create across {discCount} discipline{(discCount == 1 ? "" : "s")}";
        }

        // ────────────────────────────────────────────────────────────
        //  CREATE BUTTON
        // ────────────────────────────────────────────────────────────

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0)
            {
                MessageBox.Show("Add at least one sheet row.", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate: check for empty numbers/names
            var emptyNums = _rows.Where(r => string.IsNullOrWhiteSpace(r.SheetNumber)).ToList();
            if (emptyNums.Count > 0)
            {
                MessageBox.Show($"{emptyNums.Count} row(s) have empty sheet numbers.", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for duplicate sheet numbers within the batch
            var dupes = _rows.GroupBy(r => r.SheetNumber).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
            {
                MessageBox.Show($"Duplicate sheet numbers: {string.Join(", ", dupes)}", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultRows = _rows.ToList();
            DialogResult = true;
            Close();
        }

        // ────────────────────────────────────────────────────────────
        //  CELL EDIT ENDING — write custom param values back
        // ────────────────────────────────────────────────────────────

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel) return;

            // Check if this is a custom parameter column
            string header = e.Column.Header?.ToString() ?? "";
            if (_sheetParamNames.Contains(header) && e.Row.Item is SheetRowData row)
            {
                if (e.EditingElement is TextBox tb)
                {
                    row.SetCustomParam(header, tb.Text);
                }
            }
            UpdateStatus();
        }

        // ────────────────────────────────────────────────────────────
        //  KEYBOARD SHORTCUTS (Ctrl+C/V/X, Delete)
        // ────────────────────────────────────────────────────────────

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                // Clear selected cells
                ClearSelectedCells();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.C:
                        CopySelectedCells();
                        e.Handled = true;
                        break;
                    case Key.V:
                        PasteFromClipboard();
                        e.Handled = true;
                        break;
                    case Key.X:
                        CopySelectedCells();
                        ClearSelectedCells();
                        e.Handled = true;
                        break;
                    case Key.D:
                        // Ctrl+D: fill down from first selected row
                        FillDown();
                        e.Handled = true;
                        break;
                }
            }
        }

        private void CopySelectedCells()
        {
            try
            {
                var cells = _grid.SelectedCells;
                if (cells == null || cells.Count == 0) return;

                // Group cells by row for proper multi-row copy
                var rowGroups = new SortedDictionary<int, SortedDictionary<int, string>>();
                foreach (var cell in cells)
                {
                    if (cell.Item is not SheetRowData row) continue;
                    int rowIdx = _rows.IndexOf(row);
                    int colIdx = cell.Column.DisplayIndex;
                    string val = GetCellValue(row, cell.Column);

                    if (!rowGroups.ContainsKey(rowIdx))
                        rowGroups[rowIdx] = new SortedDictionary<int, string>();
                    rowGroups[rowIdx][colIdx] = val;
                }

                // Build tab-separated text (Excel compatible)
                var lines = new List<string>();
                foreach (var rg in rowGroups.Values)
                    lines.Add(string.Join("\t", rg.Values));

                if (lines.Count > 0)
                    Clipboard.SetText(string.Join("\r\n", lines));
            }
            catch (Exception ex) { StingLog.Warn($"Copy failed: {ex.Message}"); }
        }

        private void PasteFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string clipText = Clipboard.GetText();
                if (string.IsNullOrEmpty(clipText)) return;

                // Parse tab-separated rows
                var lines = clipText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                // Find starting cell
                var currentCell = _grid.CurrentCell;
                if (currentCell.Item is not SheetRowData startRow) return;
                int startRowIdx = _rows.IndexOf(startRow);
                int startColIdx = currentCell.Column?.DisplayIndex ?? 1;

                // Get editable columns (skip # column at index 0)
                var editableCols = _grid.Columns.OrderBy(c => c.DisplayIndex).Where(c => !c.IsReadOnly).ToList();

                for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                {
                    int targetRowIdx = startRowIdx + lineIdx;
                    // Auto-add rows if pasting beyond current row count
                    while (targetRowIdx >= _rows.Count)
                        AddNewRow();

                    var targetRow = _rows[targetRowIdx];
                    var values = lines[lineIdx].Split('\t');

                    int colOffset = 0;
                    foreach (string val in values)
                    {
                        // Find the column at the target display index
                        int targetColDisplayIdx = startColIdx + colOffset;
                        var targetCol = _grid.Columns.FirstOrDefault(c => c.DisplayIndex == targetColDisplayIdx);
                        if (targetCol != null && !targetCol.IsReadOnly)
                        {
                            SetCellValue(targetRow, targetCol, val.Trim());
                        }
                        colOffset++;
                    }
                }

                _grid.Items.Refresh();
                UpdateStatus();
            }
            catch (Exception ex) { StingLog.Warn($"Paste failed: {ex.Message}"); }
        }

        private void ClearSelectedCells()
        {
            try
            {
                var cells = _grid.SelectedCells;
                if (cells == null || cells.Count == 0) return;

                foreach (var cell in cells)
                {
                    if (cell.Item is SheetRowData row && !cell.Column.IsReadOnly)
                        SetCellValue(row, cell.Column, "");
                }
                _grid.Items.Refresh();
                UpdateStatus();
            }
            catch (Exception ex) { StingLog.Warn($"Clear failed: {ex.Message}"); }
        }

        private void FillDown()
        {
            try
            {
                var cells = _grid.SelectedCells;
                if (cells == null || cells.Count < 2) return;

                // Group by column, fill from first row value
                var colGroups = new Dictionary<int, List<(SheetRowData row, DataGridColumn col)>>();
                foreach (var cell in cells)
                {
                    if (cell.Item is not SheetRowData row || cell.Column.IsReadOnly) continue;
                    int colIdx = cell.Column.DisplayIndex;
                    if (!colGroups.ContainsKey(colIdx))
                        colGroups[colIdx] = new List<(SheetRowData, DataGridColumn)>();
                    colGroups[colIdx].Add((row, cell.Column));
                }

                foreach (var kvp in colGroups)
                {
                    var items = kvp.Value.OrderBy(x => _rows.IndexOf(x.row)).ToList();
                    if (items.Count < 2) continue;
                    string sourceVal = GetCellValue(items[0].row, items[0].col);
                    for (int i = 1; i < items.Count; i++)
                        SetCellValue(items[i].row, items[i].col, sourceVal);
                }

                _grid.Items.Refresh();
            }
            catch (Exception ex) { StingLog.Warn($"Fill down failed: {ex.Message}"); }
        }

        // ────────────────────────────────────────────────────────────
        //  DRAG-FILL (Excel-like cell corner drag to increment)
        // ────────────────────────────────────────────────────────────

        private int _dragFillStartRowIdx = -1;
        private int _dragFillColIdx = -1;
        private string _dragFillSourceValue = "";

        private void OnGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Detect if click is near the bottom-right corner of the current cell
            // (the drag-fill handle area)
            var hitResult = VisualTreeHelper.HitTest(_grid, e.GetPosition(_grid));
            if (hitResult?.VisualHit == null) return;

            var cell = FindParent<DataGridCell>(hitResult.VisualHit as DependencyObject);
            if (cell == null || cell.Column == null || cell.Column.IsReadOnly) return;

            // Check if mouse is in the bottom-right 8x8 corner of the cell
            var cellPos = e.GetPosition(cell);
            if (cellPos.X >= cell.ActualWidth - 8 && cellPos.Y >= cell.ActualHeight - 8)
            {
                // Start drag-fill
                var row = cell.DataContext as SheetRowData;
                if (row == null) return;

                _isDragFilling = true;
                _dragFillStartRowIdx = _rows.IndexOf(row);
                _dragFillColIdx = cell.Column.DisplayIndex;
                _dragFillSourceValue = GetCellValue(row, cell.Column);
                cell.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnGridPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragFilling || e.LeftButton != MouseButtonState.Pressed) return;

            // Visual feedback: highlight rows as mouse moves
            // (In a full implementation, you'd add selection highlighting)
        }

        private void OnGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragFilling) return;
            _isDragFilling = false;

            try
            {
                // Find the row under the mouse
                var hitResult = VisualTreeHelper.HitTest(_grid, e.GetPosition(_grid));
                if (hitResult?.VisualHit == null) return;

                var cell = FindParent<DataGridCell>(hitResult.VisualHit as DependencyObject);
                if (cell == null) return;

                var endRow = cell.DataContext as SheetRowData;
                if (endRow == null) return;

                int endRowIdx = _rows.IndexOf(endRow);
                if (endRowIdx == _dragFillStartRowIdx) return; // No drag movement

                // Determine fill direction and range
                int minRow = Math.Min(_dragFillStartRowIdx, endRowIdx);
                int maxRow = Math.Max(_dragFillStartRowIdx, endRowIdx);

                var col = _grid.Columns.FirstOrDefault(c => c.DisplayIndex == _dragFillColIdx);
                if (col == null || col.IsReadOnly) return;

                // Fill with incremented values
                for (int i = minRow; i <= maxRow; i++)
                {
                    if (i == _dragFillStartRowIdx) continue;
                    int offset = i - _dragFillStartRowIdx;
                    string fillValue = IncrementValue(_dragFillSourceValue, offset);
                    SetCellValue(_rows[i], col, fillValue);
                }

                _grid.Items.Refresh();
                UpdateStatus();
            }
            catch (Exception ex) { StingLog.Warn($"Drag-fill failed: {ex.Message}"); }
            finally
            {
                Mouse.Capture(null);
                _dragFillStartRowIdx = -1;
            }
        }

        /// <summary>
        /// Increments a value for drag-fill. Handles numeric suffixes, pure numbers,
        /// and falls back to copying the value unchanged.
        /// </summary>
        private static string IncrementValue(string source, int offset)
        {
            if (string.IsNullOrEmpty(source)) return source;

            // Pure integer
            if (int.TryParse(source, out int intVal))
                return (intVal + offset).ToString();

            // Pure decimal
            if (double.TryParse(source, out double dblVal))
                return (dblVal + offset).ToString();

            // String with trailing number (e.g., "A-001" → "A-002", "Sheet 1" → "Sheet 2")
            int trailingStart = source.Length;
            while (trailingStart > 0 && char.IsDigit(source[trailingStart - 1]))
                trailingStart--;

            if (trailingStart < source.Length)
            {
                string prefix = source.Substring(0, trailingStart);
                string numStr = source.Substring(trailingStart);
                if (int.TryParse(numStr, out int numVal))
                    return prefix + (numVal + offset).ToString("D" + numStr.Length);
            }

            // No numeric part — just copy the value
            return source;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T target) return target;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        // ────────────────────────────────────────────────────────────
        //  CELL VALUE GET/SET HELPERS
        // ────────────────────────────────────────────────────────────

        private string GetCellValue(SheetRowData row, DataGridColumn col)
        {
            string header = col.Header?.ToString() ?? "";
            return header switch
            {
                "Sheet Number" => row.SheetNumber,
                "Sheet Name" => row.SheetName,
                "Discipline" => row.Discipline,
                "Title Block" => row.TitleBlock,
                "Scope Box" => row.ScopeBox,
                "Dependent Views" => row.DependentViews,
                "View Template" => row.ViewTemplate,
                "Scale" => row.Scale,
                "Revision" => row.Revision,
                _ => _sheetParamNames.Contains(header) ? row.GetCustomParam(header) : ""
            };
        }

        private void SetCellValue(SheetRowData row, DataGridColumn col, string value)
        {
            string header = col.Header?.ToString() ?? "";
            switch (header)
            {
                case "Sheet Number": row.SheetNumber = value; break;
                case "Sheet Name": row.SheetName = value; break;
                case "Discipline": row.Discipline = value; break;
                case "Title Block": row.TitleBlock = value; break;
                case "Scope Box": row.ScopeBox = value; break;
                case "Dependent Views": row.DependentViews = value; break;
                case "View Template": row.ViewTemplate = value; break;
                case "Scale": row.Scale = value; break;
                case "Revision": row.Revision = value; break;
                default:
                    if (_sheetParamNames.Contains(header))
                        row.SetCustomParam(header, value);
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────
        //  ROW INDEX CONVERTER (for # column)
        // ────────────────────────────────────────────────────────────

        private class RowIndexConverter : IValueConverter
        {
            private readonly ObservableCollection<SheetRowData> _collection;
            public RowIndexConverter(ObservableCollection<SheetRowData> collection) => _collection = collection;

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is SheetRowData row)
                    return (_collection.IndexOf(row) + 1).ToString();
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotImplementedException();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CUSTOM PARAMETER VALUE CONVERTER
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// IValueConverter that reads/writes custom shared parameter values
    /// from the SheetRowData.CustomParams dictionary.
    /// Bound with Path="." (the whole SheetRowData object) so it can
    /// call GetCustomParam/SetCustomParam by the paramName captured at construction.
    /// </summary>
    internal class CustomParamConverter : IValueConverter
    {
        private readonly string _paramName;
        public CustomParamConverter(string paramName) => _paramName = paramName;

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is SheetRowData row)
                return row.GetCustomParam(_paramName);
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // ConvertBack is called when the user edits the cell.
            // We need to write the value back to the dictionary.
            // However, with Path="." the binding target is the whole row object,
            // so ConvertBack cannot directly set — we handle this via CellEditEnding instead.
            // Return the string value so the binding doesn't throw.
            return value;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DATA MODEL
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a single sheet row in the New Sheet creation dialog.
    /// Each row becomes one ViewSheet in Revit.
    /// </summary>
    public class SheetRowData : INotifyPropertyChanged
    {
        private string _sheetNumber = "";
        private string _sheetName = "";
        private string _discipline = "A";
        private string _titleBlock = "";
        private string _scopeBox = "";
        private string _dependentViews = "";
        private string _viewTemplate = "";
        private string _scale = "1:100";
        private string _revision = "P01";

        /// <summary>Custom shared parameter values keyed by parameter name.</summary>
        public Dictionary<string, string> CustomParams { get; set; } = new();

        public string SheetNumber
        {
            get => _sheetNumber;
            set { _sheetNumber = value; OnPropertyChanged(nameof(SheetNumber)); }
        }

        public string SheetName
        {
            get => _sheetName;
            set { _sheetName = value; OnPropertyChanged(nameof(SheetName)); }
        }

        public string Discipline
        {
            get => _discipline;
            set { _discipline = value; OnPropertyChanged(nameof(Discipline)); }
        }

        public string TitleBlock
        {
            get => _titleBlock;
            set { _titleBlock = value; OnPropertyChanged(nameof(TitleBlock)); }
        }

        public string ScopeBox
        {
            get => _scopeBox;
            set { _scopeBox = value; OnPropertyChanged(nameof(ScopeBox)); }
        }

        /// <summary>Semicolon-separated list of dependent view names to place on this sheet.</summary>
        public string DependentViews
        {
            get => _dependentViews;
            set { _dependentViews = value; OnPropertyChanged(nameof(DependentViews)); }
        }

        public string ViewTemplate
        {
            get => _viewTemplate;
            set { _viewTemplate = value; OnPropertyChanged(nameof(ViewTemplate)); }
        }

        public string Scale
        {
            get => _scale;
            set { _scale = value; OnPropertyChanged(nameof(Scale)); }
        }

        public string Revision
        {
            get => _revision;
            set { _revision = value; OnPropertyChanged(nameof(Revision)); }
        }

        /// <summary>Get/set a custom parameter value by name.</summary>
        public string GetCustomParam(string name) =>
            CustomParams.TryGetValue(name, out string v) ? v : "";

        public void SetCustomParam(string name, string value)
        {
            CustomParams[name] = value;
            OnPropertyChanged("CP_" + name);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
