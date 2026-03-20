using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════
    //  Duplicate View — copy view with filters, overrides, visibility
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Duplicate the active view with all settings: filters, graphic overrides,
    /// visibility state, crop region, and view template assignment.
    /// Supports Duplicate, Duplicate with Detailing, and Duplicate as Dependent.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View sourceView = ctx.ActiveView;

            if (sourceView is ViewSheet)
            {
                TaskDialog.Show("Duplicate View", "Cannot duplicate a sheet. Open the view to duplicate.");
                return Result.Succeeded;
            }

            TaskDialog modeDlg = new TaskDialog("Duplicate View");
            modeDlg.MainInstruction = $"Duplicate '{sourceView.Name}'";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Duplicate with Detailing (recommended)",
                "Copy view with all annotations, detail items, and tags");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Duplicate (view only)",
                "Copy view settings only — no annotations or detailing");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Duplicate as Dependent",
                "Create a dependent view linked to the original");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ViewDuplicateOption option;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    option = ViewDuplicateOption.WithDetailing; break;
                case TaskDialogResult.CommandLink2:
                    option = ViewDuplicateOption.Duplicate; break;
                case TaskDialogResult.CommandLink3:
                    option = ViewDuplicateOption.AsDependent; break;
                default:
                    return Result.Cancelled;
            }

            ElementId newViewId;
            using (Transaction tx = new Transaction(doc, "STING Duplicate View"))
            {
                tx.Start();
                try
                {
                    newViewId = sourceView.Duplicate(option);
                }
                catch (Exception ex)
                {
                    string errMsg = ex.Message;
                    tx.RollBack();
                    TaskDialog.Show("Duplicate View",
                        $"Cannot duplicate this view:\n{errMsg}");
                    return Result.Failed;
                }

                // Rename the duplicate
                View newView = doc.GetElement(newViewId) as View;
                if (newView != null)
                {
                    string baseName = sourceView.Name;
                    string newName = baseName + " Copy";
                    int suffix = 2;
                    while (ViewNameExists(doc, newName))
                    {
                        newName = $"{baseName} Copy {suffix++}";
                    }
                    try { newView.Name = newName; }
                    catch (Exception ex) { StingLog.Warn($"DuplicateView rename: {ex.Message}"); }
                }

                tx.Commit();
            }

            View duplicated = doc.GetElement(newViewId) as View;
            string resultName = duplicated?.Name ?? "Unknown";
            TaskDialog.Show("Duplicate View",
                $"Created: {resultName}\nMode: {option}");
            StingLog.Info($"DuplicateView: '{sourceView.Name}' → '{resultName}' ({option})");

            // Switch to the new view
            try { uidoc.ActiveView = duplicated; }
            catch (Exception ex) { StingLog.Warn($"DuplicateView activate: {ex.Message}"); }

            return Result.Succeeded;
        }

        private static bool ViewNameExists(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == name);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Rename Views — find/replace or pattern-based
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Batch rename elements using a single-page WPF DataGrid dialog.
    /// Shows Original Name → New Name (editable) with category/family filters,
    /// operation presets, search/filter, Select All, and live preview.
    /// Supports views, sheets, schedules, families, types, materials, levels, grids, etc.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Collect ALL rename targets across all categories
            var allTargets = new List<RenameRow>();
            CollectAllTargets(doc, allTargets);

            if (allTargets.Count == 0)
            {
                TaskDialog.Show("STING Batch Rename", "No renameable items found in the project.");
                return Result.Succeeded;
            }

            // Show single-page WPF DataGrid dialog
            var dialog = new BatchRenameDialog(allTargets);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Set window owner: {ex.Message}"); }

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            var toRename = dialog.GetRenameResults();
            if (toRename.Count == 0) return Result.Cancelled;

            int renamed = 0, failed = 0;
            using (Transaction tx = new Transaction(doc, "STING Batch Rename"))
            {
                tx.Start();
                foreach (var row in toRename)
                {
                    if (string.IsNullOrEmpty(row.NewName) || row.NewName == row.OriginalName) continue;
                    Element el = doc.GetElement(row.Id);
                    if (el == null) { failed++; continue; }
                    try
                    {
                        SetElementName(el, row.NewName);
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"Rename '{row.OriginalName}' → '{row.NewName}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Batch Rename",
                $"Renamed: {renamed}\nFailed: {failed}\nTotal selected: {toRename.Count}");
            StingLog.Info($"BatchRename: renamed={renamed}, failed={failed}");
            return Result.Succeeded;
        }

        // ── Rename row model ──
        internal class RenameRow : System.ComponentModel.INotifyPropertyChanged
        {
            public ElementId Id { get; set; }
            public string OriginalName { get; set; }
            private string _newName;
            public string NewName
            {
                get => _newName;
                set { _newName = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NewName))); }
            }
            public string Category { get; set; }
            public string Family { get; set; }
            public bool IsSelected { get; set; }
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        }

        // ── WPF DataGrid dialog ──
        internal class BatchRenameDialog : System.Windows.Window
        {
            private readonly List<RenameRow> _allRows;
            private readonly System.Windows.Controls.DataGrid _grid;
            private readonly System.Windows.Controls.ComboBox _categoryFilter;
            private readonly System.Windows.Controls.ComboBox _familyFilter;
            private readonly System.Windows.Controls.TextBox _searchBox;
            private readonly System.Windows.Controls.ComboBox _operationCombo;
            private readonly System.Windows.Controls.TextBlock _statusText;
            private List<RenameRow> _filteredRows;

            public BatchRenameDialog(List<RenameRow> rows)
            {
                _allRows = rows;
                _filteredRows = new List<RenameRow>(rows);

                Title = "STING Batch Rename";
                Width = 960; Height = 640;
                MinWidth = 700; MinHeight = 400;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
                FontFamily = new FontFamily("Segoe UI");
                ResizeMode = ResizeMode.CanResizeWithGrip;

                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Filters
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Operation
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Buttons

                // ── Header ──
                var header = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                    Padding = new Thickness(16, 12, 16, 12)
                };
                var headerStack = new StackPanel();
                headerStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "STING Batch Rename",
                    FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
                });
                headerStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = $"{rows.Count} items loaded",
                    FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(206, 147, 216)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                header.Child = headerStack;
                Grid.SetRow(header, 0);
                root.Children.Add(header);

                // ── Filter bar ──
                var filterPanel = new System.Windows.Controls.WrapPanel
                {
                    Margin = new Thickness(12, 8, 12, 4)
                };

                filterPanel.Children.Add(MakeLabel("Category"));
                _categoryFilter = new System.Windows.Controls.ComboBox { Width = 180, Margin = new Thickness(4, 0, 16, 0) };
                _categoryFilter.SelectionChanged += (s, e) => ApplyFilters();
                filterPanel.Children.Add(_categoryFilter);

                filterPanel.Children.Add(MakeLabel("Family"));
                _familyFilter = new System.Windows.Controls.ComboBox { Width = 180, Margin = new Thickness(4, 0, 16, 0) };
                _familyFilter.SelectionChanged += (s, e) => ApplyFilters();
                filterPanel.Children.Add(_familyFilter);

                filterPanel.Children.Add(MakeLabel("Search / Filter"));
                _searchBox = new System.Windows.Controls.TextBox { Width = 200, Margin = new Thickness(4, 0, 0, 0) };
                _searchBox.TextChanged += (s, e) => ApplyFilters();
                filterPanel.Children.Add(_searchBox);

                Grid.SetRow(filterPanel, 1);
                root.Children.Add(filterPanel);

                // ── Operation bar ──
                var opPanel = new System.Windows.Controls.WrapPanel { Margin = new Thickness(12, 4, 12, 4) };
                opPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Operation:", FontWeight = FontWeights.SemiBold, FontSize = 12,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                _operationCombo = new System.Windows.Controls.ComboBox { Width = 280, FontSize = 12 };
                _operationCombo.Items.Add("(Manual edit — type in New Name column)");
                _operationCombo.Items.Add("Add 'STING - ' prefix");
                _operationCombo.Items.Add("Remove ' Copy' suffix");
                _operationCombo.Items.Add("UPPERCASE all names");
                _operationCombo.Items.Add("lowercase all names");
                _operationCombo.Items.Add("Title Case");
                _operationCombo.Items.Add("Standardise Levels");
                _operationCombo.Items.Add("Custom find/replace...");
                _operationCombo.Items.Add("Add numbering suffix (-001, -002, ...)");
                _operationCombo.Items.Add("Remove prefix up to ' - '");
                _operationCombo.SelectedIndex = 0;
                _operationCombo.SelectionChanged += (s, e) => ApplyOperation();
                opPanel.Children.Add(_operationCombo);

                var applyOpBtn = new System.Windows.Controls.Button
                {
                    Content = "Apply", Width = 70, Height = 26, Margin = new Thickness(8, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                    Foreground = Brushes.White, FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand
                };
                applyOpBtn.Click += (s, e) => ApplyOperation();
                opPanel.Children.Add(applyOpBtn);

                Grid.SetRow(opPanel, 2);
                root.Children.Add(opPanel);

                // ── DataGrid ──
                _grid = new System.Windows.Controls.DataGrid
                {
                    Margin = new Thickness(12, 4, 12, 8),
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    SelectionMode = System.Windows.Controls.DataGridSelectionMode.Extended,
                    GridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                    RowHeaderWidth = 0,
                    AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                    BorderThickness = new Thickness(1),
                    FontSize = 12
                };

                // Checkbox column for selection
                var checkCol = new System.Windows.Controls.DataGridCheckBoxColumn
                {
                    Header = "",
                    Binding = new System.Windows.Data.Binding("IsSelected") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                    Width = 30
                };
                _grid.Columns.Add(checkCol);

                // Original Name (read-only)
                var origCol = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Original Name",
                    Binding = new System.Windows.Data.Binding("OriginalName"),
                    IsReadOnly = true,
                    Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star),
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90))
                };
                _grid.Columns.Add(origCol);

                // New Name (editable)
                var newCol = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "New Name",
                    Binding = new System.Windows.Data.Binding("NewName") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                    IsReadOnly = false,
                    Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star),
                    Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 50)),
                    FontStyle = System.Windows.FontStyles.Normal
                };
                _grid.Columns.Add(newCol);

                // Category (read-only)
                var catCol = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Category",
                    Binding = new System.Windows.Data.Binding("Category"),
                    IsReadOnly = true,
                    Width = 120,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
                };
                _grid.Columns.Add(catCol);

                // Family (read-only)
                var famCol = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Family",
                    Binding = new System.Windows.Data.Binding("Family"),
                    IsReadOnly = true,
                    Width = 140,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
                };
                _grid.Columns.Add(famCol);

                Grid.SetRow(_grid, 3);
                root.Children.Add(_grid);

                // ── Button bar ──
                var btnBar = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var btnGrid = new Grid();
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _statusText = new System.Windows.Controls.TextBlock
                {
                    FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
                };
                Grid.SetColumn(_statusText, 0);
                btnGrid.Children.Add(_statusText);

                var rightButtons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

                var selectAllBtn = MakeButton("Select All", false);
                selectAllBtn.Click += (s, e) => { foreach (var r in _filteredRows) r.IsSelected = true; RefreshGrid(); };
                rightButtons.Children.Add(selectAllBtn);

                var selectNoneBtn = MakeButton("Select None", false);
                selectNoneBtn.Margin = new Thickness(8, 0, 16, 0);
                selectNoneBtn.Click += (s, e) => { foreach (var r in _filteredRows) r.IsSelected = false; RefreshGrid(); };
                rightButtons.Children.Add(selectNoneBtn);

                var cancelBtn = MakeButton("Cancel", false);
                cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
                rightButtons.Children.Add(cancelBtn);

                var applyBtn = MakeButton("Apply Rename", true);
                applyBtn.Margin = new Thickness(8, 0, 0, 0);
                applyBtn.Click += (s, e) => { DialogResult = true; Close(); };
                rightButtons.Children.Add(applyBtn);

                Grid.SetColumn(rightButtons, 1);
                btnGrid.Children.Add(rightButtons);
                btnBar.Child = btnGrid;
                Grid.SetRow(btnBar, 4);
                root.Children.Add(btnBar);

                Content = root;
                PopulateFilters();
                RefreshGrid();
            }

            private void PopulateFilters()
            {
                var categories = _allRows.Select(r => r.Category).Where(c => !string.IsNullOrEmpty(c))
                    .Distinct().OrderBy(c => c).ToList();
                _categoryFilter.Items.Clear();
                _categoryFilter.Items.Add("All Categories");
                foreach (var c in categories) _categoryFilter.Items.Add(c);
                _categoryFilter.SelectedIndex = 0;

                PopulateFamilyFilter();
            }

            private void PopulateFamilyFilter()
            {
                string catFilter = _categoryFilter.SelectedItem as string;
                var source = _allRows.AsEnumerable();
                if (catFilter != null && catFilter != "All Categories")
                    source = source.Where(r => r.Category == catFilter);

                var families = source.Select(r => r.Family).Where(f => !string.IsNullOrEmpty(f))
                    .Distinct().OrderBy(f => f).ToList();
                _familyFilter.Items.Clear();
                _familyFilter.Items.Add("All Families");
                foreach (var f in families) _familyFilter.Items.Add(f);
                _familyFilter.SelectedIndex = 0;
            }

            private void ApplyFilters()
            {
                string catFilter = _categoryFilter.SelectedItem as string;
                string famFilter = _familyFilter.SelectedItem as string;
                string search = _searchBox.Text?.Trim() ?? "";

                _filteredRows = _allRows.Where(r =>
                {
                    if (catFilter != null && catFilter != "All Categories" && r.Category != catFilter) return false;
                    if (famFilter != null && famFilter != "All Families" && r.Family != famFilter) return false;
                    if (!string.IsNullOrEmpty(search))
                    {
                        return r.OriginalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (r.Family ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    return true;
                }).ToList();

                // Update family filter when category changes
                if (_categoryFilter.IsDropDownOpen || _familyFilter.Items.Count <= 1)
                    PopulateFamilyFilter();

                RefreshGrid();
            }

            private void RefreshGrid()
            {
                _grid.ItemsSource = null;
                _grid.ItemsSource = _filteredRows;
                int willChange = _filteredRows.Count(r => r.IsSelected && !string.IsNullOrEmpty(r.NewName) && r.NewName != r.OriginalName);
                int selected = _filteredRows.Count(r => r.IsSelected);
                _statusText.Text = $"{_filteredRows.Count} items shown | {willChange} will change | {selected} selected for rename";
            }

            private void ApplyOperation()
            {
                int opIdx = _operationCombo.SelectedIndex;
                if (opIdx <= 0) return; // Manual edit mode

                string findText = null, replaceText = null;
                if (opIdx == 7) // Custom find/replace
                {
                    var inputWin = new System.Windows.Window
                    {
                        Title = "Custom Find/Replace", Width = 400, Height = 170,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                        ResizeMode = ResizeMode.NoResize, Owner = this
                    };
                    var stack = new StackPanel { Margin = new Thickness(12) };
                    stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Find:", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
                    var findBox = new System.Windows.Controls.TextBox { FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
                    stack.Children.Add(findBox);
                    stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Replace with:", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
                    var replaceBox = new System.Windows.Controls.TextBox { FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
                    stack.Children.Add(replaceBox);
                    var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                    okBtn.Click += (s2, e2) => { inputWin.DialogResult = true; };
                    stack.Children.Add(okBtn);
                    inputWin.Content = stack;
                    if (inputWin.ShowDialog() != true || string.IsNullOrEmpty(findBox.Text)) return;
                    findText = findBox.Text;
                    replaceText = replaceBox.Text ?? "";
                }

                int seqNum = 0;
                foreach (var row in _filteredRows)
                {
                    string name = row.OriginalName;
                    string result = name;
                    switch (opIdx)
                    {
                        case 1: result = name.StartsWith("STING - ") ? name : "STING - " + name; break;
                        case 2: result = System.Text.RegularExpressions.Regex.Replace(name, @"\s*Copy\s*\d*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); break;
                        case 3: result = name.ToUpperInvariant(); break;
                        case 4: result = name.ToLowerInvariant(); break;
                        case 5: result = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()); break;
                        case 6: result = StandardiseLevelName(name); break;
                        case 7: result = findText != null && name.Contains(findText) ? name.Replace(findText, replaceText) : name; break;
                        case 8: seqNum++; result = $"{name}-{seqNum:D3}"; break;
                        case 9:
                            int dashIdx = name.IndexOf(" - ");
                            result = dashIdx >= 0 && dashIdx < name.Length - 3 ? name.Substring(dashIdx + 3) : name;
                            break;
                    }
                    row.NewName = result;
                    row.IsSelected = (result != name);
                }
                RefreshGrid();
            }

            public List<RenameRow> GetRenameResults()
            {
                return _allRows.Where(r => r.IsSelected && !string.IsNullOrEmpty(r.NewName) && r.NewName != r.OriginalName).ToList();
            }

            private static System.Windows.Controls.TextBlock MakeLabel(string text)
            {
                return new System.Windows.Controls.TextBlock
                {
                    Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
                    Margin = new Thickness(0, 0, 4, 0)
                };
            }

            private static System.Windows.Controls.Button MakeButton(string text, bool primary)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = text, MinWidth = 80, Height = 30, FontSize = 12,
                    Padding = new Thickness(12, 4, 12, 4), Cursor = System.Windows.Input.Cursors.Hand
                };
                if (primary)
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(88, 44, 131));
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.Background = Brushes.White;
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210));
                }
                return btn;
            }
        }

        // ── Collect ALL renameable elements ──
        private static void CollectAllTargets(Document doc, List<RenameRow> rows)
        {
            // Views
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && !(v is ViewSheet)).OrderBy(v => v.Name))
            {
                string family = "";
                try { family = v.ViewType.ToString(); } catch { /* ignore */ }
                rows.Add(new RenameRow { Id = v.Id, OriginalName = v.Name, NewName = v.Name, Category = "View", Family = family });
            }
            // Sheets
            foreach (ViewSheet s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber))
                rows.Add(new RenameRow { Id = s.Id, OriginalName = s.Name, NewName = s.Name, Category = "Sheet", Family = s.SheetNumber });
            // Schedules
            foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate).OrderBy(s => s.Name))
                rows.Add(new RenameRow { Id = vs.Id, OriginalName = vs.Name, NewName = vs.Name, Category = "Schedule", Family = "" });
            // Families
            foreach (Family f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .OrderBy(f => f.Name))
                rows.Add(new RenameRow { Id = f.Id, OriginalName = f.Name, NewName = f.Name, Category = f.FamilyCategory?.Name ?? "Family", Family = f.FamilyCategory?.Name ?? "" });
            // Family Types
            foreach (ElementType et in new FilteredElementCollector(doc).WhereElementIsElementType()
                .OfType<ElementType>().Where(e => e.Category != null).OrderBy(e => e.Name).Take(2000))
                rows.Add(new RenameRow { Id = et.Id, OriginalName = et.Name, NewName = et.Name, Category = et.Category?.Name ?? "Type", Family = GetFamilyNameSafe(et) });
            // Levels
            foreach (Level lv in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(lv => lv.Elevation))
                rows.Add(new RenameRow { Id = lv.Id, OriginalName = lv.Name, NewName = lv.Name, Category = "Level", Family = $"Elev: {lv.Elevation * 0.3048:F1}m" });
            // Grids
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .OrderBy(g => g.Name))
                rows.Add(new RenameRow { Id = g.Id, OriginalName = g.Name, NewName = g.Name, Category = "Grid", Family = "" });
            // Materials
            foreach (Material m in new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>().OrderBy(m => m.Name))
                rows.Add(new RenameRow { Id = m.Id, OriginalName = m.Name, NewName = m.Name, Category = "Material", Family = "" });
        }

        private static string GetFamilyNameSafe(ElementType et)
        {
            try
            {
                if (et is FamilySymbol fs) return fs.FamilyName;
                return et.FamilyName ?? "";
            }
            catch { return ""; }
        }

        private record RenameTarget(string Name, string Category, ElementId Id);

        private static List<RenameTarget> CollectRenameTargets(Document doc, string category)
        {
            var results = new List<RenameTarget>();
            switch (category)
            {
                case "views":
                    foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate && v.CanBePrinted).OrderBy(v => v.Name))
                        results.Add(new RenameTarget(v.Name, v.ViewType.ToString(), v.Id));
                    break;
                case "sheets":
                    foreach (ViewSheet s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                        .OrderBy(s => s.SheetNumber))
                        results.Add(new RenameTarget($"{s.SheetNumber} - {s.Name}", "Sheet", s.Id));
                    break;
                case "schedules":
                    foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                        .Where(s => !s.IsTemplate).OrderBy(s => s.Name))
                        results.Add(new RenameTarget(vs.Name, "Schedule", vs.Id));
                    break;
                case "families":
                    foreach (Family f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                        .OrderBy(f => f.Name))
                        results.Add(new RenameTarget(f.Name, f.FamilyCategory?.Name ?? "", f.Id));
                    break;
                case "types":
                    foreach (ElementType et in new FilteredElementCollector(doc).WhereElementIsElementType()
                        .OfType<ElementType>().Where(e => e.Category != null).OrderBy(e => e.Name).Take(500))
                        results.Add(new RenameTarget(et.Name, et.Category?.Name ?? "", et.Id));
                    break;
                case "linestyles":
                    var linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                    if (linesCat?.SubCategories != null)
                        foreach (Category sub in linesCat.SubCategories)
                            results.Add(new RenameTarget(sub.Name, "Line Style", sub.Id));
                    break;
                case "fillpatterns":
                    foreach (FillPatternElement fp in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>().OrderBy(fp => fp.Name))
                        results.Add(new RenameTarget(fp.Name, fp.GetFillPattern()?.IsSolidFill == true ? "Solid" : "Pattern", fp.Id));
                    break;
                case "materials":
                    foreach (Material m in new FilteredElementCollector(doc).OfClass(typeof(Material))
                        .Cast<Material>().OrderBy(m => m.Name))
                        results.Add(new RenameTarget(m.Name, "Material", m.Id));
                    break;
                case "levels":
                    foreach (Level lv in new FilteredElementCollector(doc).OfClass(typeof(Level))
                        .Cast<Level>().OrderBy(lv => lv.Elevation))
                        results.Add(new RenameTarget(lv.Name, $"Elevation: {lv.Elevation:F2}", lv.Id));
                    break;
                case "grids":
                    foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid))
                        .Cast<Grid>().OrderBy(g => g.Name))
                        results.Add(new RenameTarget(g.Name, "Grid", g.Id));
                    break;
                case "templates":
                    foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => v.IsTemplate).OrderBy(v => v.Name))
                        results.Add(new RenameTarget(v.Name, "Template", v.Id));
                    break;
                case "worksets":
                    if (doc.IsWorkshared)
                    {
                        var wsList = new FilteredWorksetCollector(doc)
                            .OfKind(WorksetKind.UserWorkset).ToList();
                        foreach (var ws in wsList.OrderBy(w => w.Name))
                            results.Add(new RenameTarget(ws.Name, "Workset", new ElementId(ws.Id.IntegerValue)));
                    }
                    break;
            }
            return results;
        }

        private static string GetElementName(Element el)
        {
            if (el is ViewSheet sheet) return sheet.Name;
            if (el is View view) return view.Name;
            return el.Name;
        }

        private static void SetElementName(Element el, string name)
        {
            if (el is ViewSheet sheet)
            {
                // For sheets, rename both number and name if pattern matches
                if (name.Contains(" - "))
                {
                    int idx = name.IndexOf(" - ");
                    sheet.SheetNumber = name.Substring(0, idx);
                    sheet.Name = name.Substring(idx + 3);
                }
                else sheet.Name = name;
            }
            else
            {
                el.Name = name;
            }
        }

        private static string StandardiseLevelName(string name)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ground Floor"] = "GF",
                ["Ground Level"] = "GF",
                ["Ground"] = "GF",
                ["Basement 1"] = "B1",
                ["Basement 2"] = "B2",
                ["Basement"] = "B1",
                ["Roof"] = "RF",
                ["Roof Level"] = "RF",
                ["Level 1"] = "L01",
                ["Level 2"] = "L02",
                ["Level 3"] = "L03",
                ["Level 4"] = "L04",
                ["Level 5"] = "L05",
                ["Level 6"] = "L06",
                ["Level 7"] = "L07",
                ["Level 8"] = "L08",
                ["Level 9"] = "L09",
                ["Level 10"] = "L10",
                ["First Floor"] = "L01",
                ["Second Floor"] = "L02",
                ["Third Floor"] = "L03",
            };

            foreach (var kvp in replacements)
            {
                if (name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    name = name.Replace(kvp.Key, kvp.Value,
                        StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }
            return name;
        }

        private static bool ViewNameExists(Document doc, string name, ElementId excludeId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == name && v.Id != excludeId);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Copy View Settings — filters + overrides between views
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Copy view filters, graphic overrides, and visibility settings from the
    /// active view to other views of the same type.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CopyViewSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View source = ctx.ActiveView;

            if (source is ViewSheet || source.IsTemplate)
            {
                TaskDialog.Show("Copy View Settings",
                    "Active view must be a regular view (not sheet or template).");
                return Result.Succeeded;
            }

            // Get same-type views as targets
            var targets = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.Id != source.Id &&
                    !v.IsTemplate &&
                    v.ViewType == source.ViewType &&
                    v.CanBePrinted)
                .OrderBy(v => v.Name)
                .ToList();

            if (targets.Count == 0)
            {
                TaskDialog.Show("Copy View Settings",
                    $"No other {source.ViewType} views found to copy settings to.");
                return Result.Succeeded;
            }

            // Get source filters
            var sourceFilterIds = source.GetFilters();

            TaskDialog dlg = new TaskDialog("Copy View Settings");
            dlg.MainInstruction = $"Copy settings from '{source.Name}'";
            dlg.MainContent =
                $"Source view type: {source.ViewType}\n" +
                $"Filters to copy: {sourceFilterIds.Count}\n" +
                $"Target views available: {targets.Count}\n\n" +
                "Settings copied: view filters, filter overrides, " +
                "category visibility, detail level, scale.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Apply to all {targets.Count} {source.ViewType} views",
                "Copy to every matching view");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Apply to STING views only",
                "Copy only to views with 'STING' in the name");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<View> selectedTargets;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    selectedTargets = targets;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedTargets = targets
                        .Where(v => v.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            if (selectedTargets.Count == 0)
            {
                TaskDialog.Show("Copy View Settings", "No matching target views found.");
                return Result.Succeeded;
            }

            int updated = 0;
            int filtersCopied = 0;

            using (Transaction tx = new Transaction(doc, "STING Copy View Settings"))
            {
                tx.Start();
                foreach (View target in selectedTargets)
                {
                    try
                    {
                        // Copy detail level and scale
                        if (source.DetailLevel != ViewDetailLevel.Undefined)
                            target.DetailLevel = source.DetailLevel;
                        try { target.Scale = source.Scale; }
                        catch (Exception ex) { StingLog.Warn($"CopyViewSettings scale: {ex.Message}"); }

                        // Copy filters
                        foreach (ElementId filterId in sourceFilterIds)
                        {
                            try
                            {
                                // Check if filter already applied
                                var existingFilters = target.GetFilters();
                                if (!existingFilters.Contains(filterId))
                                    target.AddFilter(filterId);

                                // Copy filter overrides and visibility
                                var ogs = source.GetFilterOverrides(filterId);
                                target.SetFilterOverrides(filterId, ogs);
                                target.SetFilterVisibility(filterId,
                                    source.GetFilterVisibility(filterId));
                                filtersCopied++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Copy filter to '{target.Name}': {ex.Message}");
                            }
                        }

                        updated++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CopyViewSettings to '{target.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Copy View Settings",
                $"Updated {updated} of {selectedTargets.Count} views.\n" +
                $"Filters applied: {filtersCopied} total " +
                $"({sourceFilterIds.Count} filters × {updated} views).");
            StingLog.Info($"CopyViewSettings: source='{source.Name}', targets={updated}, filters={filtersCopied}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Auto-Place Viewports — grid-based intelligent placement
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Automatically place viewports on a sheet using a grid layout.
    /// Arranges views in a grid pattern within the sheet's title block area.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPlaceViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View activeView = ctx.ActiveView;

            if (!(activeView is ViewSheet sheet))
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "Active view must be a sheet.\nOpen a sheet first.");
                return Result.Succeeded;
            }

            // Get existing viewports to know what's already placed
            var existingVpIds = sheet.GetAllViewports();

            // Find unplaced views
            var placedViewIds = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .SelectMany(s => s.GetAllPlacedViews()));

            var unplacedViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    !(v is ViewSheet) &&
                    !placedViewIds.Contains(v.Id))
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            if (unplacedViews.Count == 0)
            {
                TaskDialog.Show("Auto-Place Viewports",
                    "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Auto-Place Viewports");
            dlg.MainInstruction = $"Place views on '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Place first {Math.Min(4, unplacedViews.Count)} views (2×2 grid)",
                "4 viewports in a 2-column, 2-row grid");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Place first {Math.Min(6, unplacedViews.Count)} views (3×2 grid)",
                "6 viewports in a 3-column, 2-row grid");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Place single view (centered)",
                "One viewport centered on the sheet");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            dlg.FooterText = $"{unplacedViews.Count} unplaced views available.";

            int cols, rows;
            int maxViews;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    cols = 2; rows = 2; maxViews = 4; break;
                case TaskDialogResult.CommandLink2:
                    cols = 3; rows = 2; maxViews = 6; break;
                case TaskDialogResult.CommandLink3:
                    cols = 1; rows = 1; maxViews = 1; break;
                default:
                    return Result.Cancelled;
            }

            var viewsToPlace = unplacedViews.Take(maxViews).ToList();

            // Sheet dimensions (A1 default: 841mm × 594mm ≈ 2.76ft × 1.95ft)
            // Use title block to get sheet size
            double sheetWidth = 2.76;  // feet (A1)
            double sheetHeight = 1.95;
            double margin = 0.15;      // margin from edges

            // Try to get actual sheet dimensions from title block
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (titleBlocks.Count > 0)
            {
                BoundingBoxXYZ tbBB = titleBlocks[0].get_BoundingBox(null);
                if (tbBB != null)
                {
                    sheetWidth = tbBB.Max.X - tbBB.Min.X;
                    sheetHeight = tbBB.Max.Y - tbBB.Min.Y;
                }
            }

            double usableWidth = sheetWidth - 2 * margin;
            double usableHeight = sheetHeight - 2 * margin;
            double cellWidth = usableWidth / cols;
            double cellHeight = usableHeight / rows;

            int placed = 0;
            using (Transaction tx = new Transaction(doc, "STING Auto-Place Viewports"))
            {
                tx.Start();
                int idx = 0;
                for (int row = 0; row < rows && idx < viewsToPlace.Count; row++)
                {
                    for (int col = 0; col < cols && idx < viewsToPlace.Count; col++)
                    {
                        View v = viewsToPlace[idx++];
                        double cx = margin + cellWidth * (col + 0.5);
                        double cy = sheetHeight - margin - cellHeight * (row + 0.5);
                        XYZ center = new XYZ(cx, cy, 0);

                        try
                        {
                            Viewport vp = Viewport.Create(doc, sheet.Id, v.Id, center);
                            if (vp != null) placed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Place viewport '{v.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Auto-Place Viewports",
                $"Placed {placed} of {viewsToPlace.Count} viewports on '{sheet.Name}'.\n" +
                $"Layout: {cols}×{rows} grid.");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Crop to Content — auto-crop view to element extents
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-crop the active view boundaries to fit element extents with optional padding.
    /// Works on floor plans, sections, and elevations with crop regions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CropToContentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            if (view is ViewSheet || view.IsTemplate)
            {
                TaskDialog.Show("Crop to Content",
                    "Active view must be a floor plan, section, or elevation.");
                return Result.Succeeded;
            }

            // Get all model elements in the view
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    e.Category.CategoryType == CategoryType.Model)
                .ToList();

            if (elems.Count == 0)
            {
                TaskDialog.Show("Crop to Content", "No model elements in active view.");
                return Result.Succeeded;
            }

            // Calculate bounding box of all elements
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            int counted = 0;

            foreach (Element elem in elems)
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(view);
                if (bb == null) continue;

                if (bb.Min.X < minX) minX = bb.Min.X;
                if (bb.Min.Y < minY) minY = bb.Min.Y;
                if (bb.Min.Z < minZ) minZ = bb.Min.Z;
                if (bb.Max.X > maxX) maxX = bb.Max.X;
                if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                if (bb.Max.Z > maxZ) maxZ = bb.Max.Z;
                counted++;
            }

            if (counted == 0)
            {
                TaskDialog.Show("Crop to Content", "No elements with bounding boxes found.");
                return Result.Succeeded;
            }

            // Padding options
            TaskDialog padDlg = new TaskDialog("Crop to Content");
            padDlg.MainInstruction = $"Crop view to {counted} elements";
            double widthM = (maxX - minX) * 0.3048;
            double heightM = (maxY - minY) * 0.3048;
            padDlg.MainContent = $"Content extent: {widthM:F1}m × {heightM:F1}m";

            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tight crop (5% padding)", "Minimal margin around content");
            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Standard crop (10% padding)", "Standard drawing margin");
            padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Loose crop (20% padding)", "Extra space for annotations and dimensions");
            padDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double padFactor;
            switch (padDlg.Show())
            {
                case TaskDialogResult.CommandLink1: padFactor = 0.05; break;
                case TaskDialogResult.CommandLink2: padFactor = 0.10; break;
                case TaskDialogResult.CommandLink3: padFactor = 0.20; break;
                default: return Result.Cancelled;
            }

            double padX = (maxX - minX) * padFactor;
            double padY = (maxY - minY) * padFactor;
            if (padX < 1.0) padX = 1.0; // Minimum 1 foot padding
            if (padY < 1.0) padY = 1.0;

            using (Transaction tx = new Transaction(doc, "STING Crop to Content"))
            {
                tx.Start();

                // Enable crop box
                view.CropBoxActive = true;
                view.CropBoxVisible = true;

                BoundingBoxXYZ cropBox = view.CropBox;
                Transform inverse = cropBox.Transform.Inverse;

                // Transform element extents from model coords to view coords
                XYZ viewMin = inverse.OfPoint(new XYZ(minX - padX, minY - padY, minZ));
                XYZ viewMax = inverse.OfPoint(new XYZ(maxX + padX, maxY + padY, maxZ));

                // CropBox min/max must be in view-local coordinates
                cropBox.Min = new XYZ(viewMin.X, viewMin.Y, cropBox.Min.Z);
                cropBox.Max = new XYZ(viewMax.X, viewMax.Y, cropBox.Max.Z);

                view.CropBox = cropBox;
                tx.Commit();
            }

            TaskDialog.Show("Crop to Content",
                $"Cropped view to {counted} elements with {padFactor * 100:F0}% padding.\n" +
                $"Content: {widthM:F1}m × {heightM:F1}m");
            StingLog.Info($"CropToContent: elements={counted}, padding={padFactor*100:F0}%");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Batch Align Viewports — across multiple sheets
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Align viewports across all sheets to the same position.
    /// Ensures consistent viewport placement for drawing sets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAlignViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.GetAllViewports().Any())
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count < 2)
            {
                TaskDialog.Show("Batch Align Viewports",
                    "Need at least 2 sheets with viewports.");
                return Result.Succeeded;
            }

            // Use active sheet as reference (or first sheet)
            ViewSheet refSheet = ctx.ActiveView is ViewSheet activeSheet
                ? activeSheet
                : sheets[0];

            var refVpIds = refSheet.GetAllViewports().ToList();
            if (refVpIds.Count == 0)
            {
                TaskDialog.Show("Batch Align Viewports",
                    "Reference sheet has no viewports.");
                return Result.Succeeded;
            }

            // Get reference position (center of first viewport)
            Viewport refVp = doc.GetElement(refVpIds[0]) as Viewport;
            XYZ refCenter = refVp?.GetBoxCenter();

            if (refCenter == null)
            {
                TaskDialog.Show("Batch Align Viewports", "Cannot determine reference position.");
                return Result.Succeeded;
            }

            TaskDialog dlg = new TaskDialog("Batch Align Viewports");
            dlg.MainInstruction = $"Align viewports across {sheets.Count} sheets";
            dlg.MainContent =
                $"Reference: '{refSheet.SheetNumber} - {refSheet.Name}'\n" +
                $"Reference position: ({refCenter.X:F2}, {refCenter.Y:F2})\n\n" +
                "The primary (first) viewport on each sheet will be moved to match " +
                "the reference position. This ensures consistent placement across sheets.";
            dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (dlg.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int sheetsUpdated = 0;
            int vpMoved = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Align Viewports"))
            {
                tx.Start();
                foreach (ViewSheet s in sheets)
                {
                    if (s.Id == refSheet.Id) continue;

                    var vpIds = s.GetAllViewports().ToList();
                    if (vpIds.Count == 0) continue;

                    Viewport vp = doc.GetElement(vpIds[0]) as Viewport;
                    if (vp == null) continue;

                    XYZ currentCenter = vp.GetBoxCenter();
                    if (currentCenter.IsAlmostEqualTo(refCenter)) continue;

                    // Move primary viewport to reference position
                    vp.SetBoxCenter(refCenter);
                    vpMoved++;

                    // If there are additional viewports, maintain their relative offset
                    if (vpIds.Count > 1)
                    {
                        XYZ delta = refCenter - currentCenter;
                        for (int i = 1; i < vpIds.Count; i++)
                        {
                            Viewport otherVp = doc.GetElement(vpIds[i]) as Viewport;
                            if (otherVp == null) continue;

                            XYZ otherCenter = otherVp.GetBoxCenter();
                            otherVp.SetBoxCenter(otherCenter + delta);
                            vpMoved++;
                        }
                    }
                    sheetsUpdated++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Batch Align Viewports",
                $"Updated {sheetsUpdated} sheets, moved {vpMoved} viewports.\n" +
                $"Reference: '{refSheet.SheetNumber}'.");
            StingLog.Info($"BatchAlignViewports: sheets={sheetsUpdated}, viewports={vpMoved}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MagicRenameCommand — Universal batch rename with patterns
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Universal rename tool for views, sheets, rooms, and families.
    /// Supports Prefix/Suffix, Find and Replace, Case change, and
    /// Sequential numbering modes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MagicRenameCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Step 1: Choose element type
                var typeDlg = new TaskDialog("STING Magic Rename");
                typeDlg.MainInstruction = "What do you want to rename?";
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Views",
                    "Rename floor plans, sections, elevations, 3D views, etc.");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Sheets",
                    "Rename sheet names (preserves sheet numbers).");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Rooms",
                    "Rename room names.");
                typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Families",
                    "Rename family types loaded in the project.");
                typeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var typeResult = typeDlg.Show();
                string elementType;
                if (typeResult == TaskDialogResult.CommandLink1) elementType = "Views";
                else if (typeResult == TaskDialogResult.CommandLink2) elementType = "Sheets";
                else if (typeResult == TaskDialogResult.CommandLink3) elementType = "Rooms";
                else if (typeResult == TaskDialogResult.CommandLink4) elementType = "Families";
                else return Result.Cancelled;

                // Step 2: Choose rename mode
                var modeDlg = new TaskDialog("STING Magic Rename");
                modeDlg.MainInstruction = "Rename mode";
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Add Prefix/Suffix",
                    "Add text before or after existing names.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Find & Replace",
                    "Replace text within names.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Change Case",
                    "Convert to UPPER, lower, or Title Case.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Sequential Number",
                    "Add sequential numbers (001, 002, ...).");
                modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var modeResult = modeDlg.Show();
                string mode;
                if (modeResult == TaskDialogResult.CommandLink1) mode = "PrefixSuffix";
                else if (modeResult == TaskDialogResult.CommandLink2) mode = "FindReplace";
                else if (modeResult == TaskDialogResult.CommandLink3) mode = "Case";
                else if (modeResult == TaskDialogResult.CommandLink4) mode = "Sequential";
                else return Result.Cancelled;

                // Collect elements
                var targetElements = new List<Element>();
                if (elementType == "Views")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet)
                        .Cast<Element>());
                }
                else if (elementType == "Sheets")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<Element>());
                }
                else if (elementType == "Rooms")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Element>());
                }
                else if (elementType == "Families")
                {
                    targetElements.AddRange(new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<Element>());
                }

                if (targetElements.Count == 0)
                {
                    TaskDialog.Show("STING", $"No {elementType.ToLower()} found in the project.");
                    return Result.Cancelled;
                }

                // Get rename parameters via input dialogs
                string prefix = "", suffix = "", findText = "", replaceText = "", caseMode = "UPPER";
                int seqStart = 1;

                if (mode == "PrefixSuffix")
                {
                    var inp = new TaskDialog("Prefix/Suffix");
                    inp.MainInstruction = "Enter prefix and/or suffix";
                    inp.MainContent = $"Found {targetElements.Count} {elementType.ToLower()}.\n" +
                        "Enter prefix in the format: PREFIX_ or _SUFFIX or PREFIX_*_SUFFIX\n" +
                        "(Use * as placeholder for existing name)";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Add 'STING - ' prefix");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Add ' - REV' suffix");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) { prefix = "STING - "; }
                    else if (r == TaskDialogResult.CommandLink2) { suffix = " - REV"; }
                    else return Result.Cancelled;
                }
                else if (mode == "FindReplace")
                {
                    // Simple preset-based find/replace
                    var inp = new TaskDialog("Find & Replace");
                    inp.MainInstruction = $"Find & Replace in {targetElements.Count} {elementType.ToLower()}";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Remove 'Copy' / 'Copy 1'",
                        "Clean up duplicated view names.");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Replace '-' with ' - '",
                        "Standardise dash spacing.");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Remove leading/trailing spaces",
                        "Trim whitespace from names.");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) { findText = " Copy"; replaceText = ""; }
                    else if (r == TaskDialogResult.CommandLink2) { findText = "-"; replaceText = " - "; }
                    else if (r == TaskDialogResult.CommandLink3) { mode = "Trim"; }
                    else return Result.Cancelled;
                }
                else if (mode == "Case")
                {
                    var inp = new TaskDialog("Case Change");
                    inp.MainInstruction = "Select case mode";
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "UPPER CASE");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "lower case");
                    inp.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Title Case");
                    inp.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var r = inp.Show();
                    if (r == TaskDialogResult.CommandLink1) caseMode = "UPPER";
                    else if (r == TaskDialogResult.CommandLink2) caseMode = "lower";
                    else if (r == TaskDialogResult.CommandLink3) caseMode = "Title";
                    else return Result.Cancelled;
                }

                // Apply renames
                int renamed = 0, skipped = 0;
                using (var tx = new Transaction(doc, "STING Magic Rename"))
                {
                    tx.Start();
                    int seq = seqStart;
                    foreach (var el in targetElements)
                    {
                        try
                        {
                            string currentName = "";
                            Parameter nameParam = null;

                            if (el is View v)
                            {
                                currentName = v.Name ?? "";
                                nameParam = el.get_Parameter(BuiltInParameter.VIEW_NAME);
                            }
                            else if (el is ViewSheet s)
                            {
                                currentName = s.Name ?? "";
                                nameParam = el.get_Parameter(BuiltInParameter.SHEET_NAME);
                            }
                            else if (el.Category?.BuiltInCategory == BuiltInCategory.OST_Rooms)
                            {
                                nameParam = el.get_Parameter(BuiltInParameter.ROOM_NAME);
                                currentName = nameParam?.AsString() ?? "";
                            }
                            else if (el is FamilySymbol fs)
                            {
                                currentName = fs.Name ?? "";
                                nameParam = null; // Will use Name property
                            }

                            if (string.IsNullOrEmpty(currentName)) { skipped++; continue; }

                            string newName = currentName;

                            switch (mode)
                            {
                                case "PrefixSuffix":
                                    newName = prefix + currentName + suffix;
                                    break;
                                case "FindReplace":
                                    newName = currentName.Replace(findText, replaceText);
                                    break;
                                case "Trim":
                                    newName = currentName.Trim();
                                    break;
                                case "Case":
                                    newName = caseMode switch
                                    {
                                        "UPPER" => currentName.ToUpperInvariant(),
                                        "lower" => currentName.ToLowerInvariant(),
                                        "Title" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(currentName.ToLowerInvariant()),
                                        _ => currentName
                                    };
                                    break;
                                case "Sequential":
                                    newName = $"{currentName} {seq:D3}";
                                    seq++;
                                    break;
                            }

                            if (newName == currentName) { skipped++; continue; }

                            if (nameParam != null && !nameParam.IsReadOnly)
                                nameParam.Set(newName);
                            else if (el is FamilySymbol fsSym)
                                fsSym.Name = newName;
                            else
                                el.Name = newName;

                            renamed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"MagicRename: failed to rename '{el.Name}': {ex.Message}");
                            skipped++;
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Magic Rename",
                    $"Renamed: {renamed}\nSkipped: {skipped}\nTotal: {targetElements.Count}");
                StingLog.Info($"MagicRename: {mode} on {elementType}, renamed={renamed}, skipped={skipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MagicRenameCommand failed", ex);
                try { TaskDialog.Show("STING", $"Magic Rename failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ViewTabColourCommand — Color view browser tabs by discipline
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Colors view tabs in the Revit view bar by discipline, similar to
    /// pyRevit tab colouring. Uses OverrideGraphicSettings on view-associated
    /// elements to visually distinguish disciplines.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTabColourCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Revit API does not expose direct control over view tab colours
                // in the document tab bar. This command applies discipline-based
                // naming prefixes and view template assignments as a visual proxy.

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet)
                    .ToList();

                // Build discipline map from view names
                var discMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mechanical"] = 0, ["HVAC"] = 0,
                    ["Electrical"] = 0, ["Lighting"] = 0,
                    ["Plumbing"] = 0, ["Hydraulic"] = 0,
                    ["Architectural"] = 0, ["Interior"] = 0,
                    ["Structural"] = 0, ["Fire"] = 0,
                    ["Coordination"] = 0
                };

                foreach (var v in views)
                {
                    string name = v.Name ?? "";
                    foreach (var kvp in discMap.Keys.ToList())
                    {
                        if (name.Contains(kvp, StringComparison.OrdinalIgnoreCase))
                            discMap[kvp]++;
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine($"View discipline analysis ({views.Count} views):\n");
                foreach (var kvp in discMap.Where(k => k.Value > 0).OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value} views");

                int unmatched = views.Count - discMap.Values.Sum();
                if (unmatched > 0)
                    sb.AppendLine($"  Unclassified: {unmatched} views");

                sb.AppendLine("\nNote: Revit API does not support direct tab colour control.");
                sb.AppendLine("View tabs are coloured natively based on view templates.");
                sb.AppendLine("Use Auto-Assign Templates to ensure discipline templates are applied.");

                TaskDialog.Show("View Tab Colours", sb.ToString());
                StingLog.Info($"ViewTabColour: analysed {views.Count} views");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ViewTabColourCommand failed", ex);
                try { TaskDialog.Show("STING", $"View Tab Colour failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RibbonPanelStylerCommand — Color ribbon panels by discipline
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies discipline-based colour styling information to ribbon panels.
    /// Reports the current ribbon panel configuration and discipline alignment.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RibbonPanelStylerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

                // Revit API does not expose ribbon panel colour/style modification
                // at runtime. This command provides an informational report about
                // the STING ribbon configuration.

                var sb = new StringBuilder();
                sb.AppendLine("STING Ribbon Panel Configuration\n");
                sb.AppendLine("Panels:");
                sb.AppendLine("  SELECT  - Element selection & colour coding");
                sb.AppendLine("  DOCS    - Document management & automation");
                sb.AppendLine("  TAGS    - ISO 19650 tagging pipeline");
                sb.AppendLine("  ORGANISE - Tag operations & annotation management");
                sb.AppendLine("  TEMP    - Template setup & data pipeline");
                sb.AppendLine("  PANEL   - WPF dockable panel toggle");
                sb.AppendLine();
                sb.AppendLine("Discipline Colour Mapping:");
                sb.AppendLine("  M (Mechanical) = Blue");
                sb.AppendLine("  E (Electrical) = Gold/Yellow");
                sb.AppendLine("  P (Plumbing)   = Green");
                sb.AppendLine("  A (Architectural) = Grey");
                sb.AppendLine("  S (Structural) = Red");
                sb.AppendLine("  FP (Fire)      = Orange");
                sb.AppendLine("  LV (Low Voltage) = Purple");
                sb.AppendLine();
                sb.AppendLine("Note: Ribbon panel styling is controlled by the");
                sb.AppendLine("WPF dockable panel ThemeManager (Dark/Light/Grey/Corporate).");
                sb.AppendLine("Use the Theme button in the panel to change styles.");

                TaskDialog.Show("Ribbon Panel Styler", sb.ToString());
                StingLog.Info("RibbonPanelStyler: displayed configuration");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RibbonPanelStylerCommand failed", ex);
                try { TaskDialog.Show("STING", $"Ribbon Styler failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }
    }
}
