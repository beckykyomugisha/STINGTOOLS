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
    /// Naviate-style sheet creation dialog with editable Excel-like grid.
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
            string suggestedNextNumber = null)
        {
            _titleBlockNames = titleBlockNames ?? new();
            _scopeBoxNames = scopeBoxNames ?? new();
            _dependentViewNames = dependentViewNames ?? new();
            _viewTemplateNames = viewTemplateNames ?? new();

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
            string defaultTitleBlock = null,
            string defaultDiscipline = "A",
            string suggestedNextNumber = null,
            out bool autoPlaceDependentViews)
        {
            var dlg = new NewSheetDialog();
            dlg.Configure(titleBlockNames, scopeBoxNames, dependentViewNames,
                viewTemplateNames, defaultTitleBlock, defaultDiscipline, suggestedNextNumber);
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
                Text = "Add rows for each sheet. Edit cells directly. Use toolbar to add/remove/duplicate rows.",
                FontSize = 11, Foreground = BrFgSubtle, Margin = new Thickness(0, 2, 0, 0)
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
            _rows.Add(new SheetRowData
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
            });
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
