using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Enhanced parameter lookup dialog with category picker, conditions/filters,
    /// and element selection. Replaces the basic XAML-based lookup with a functional
    /// standalone dialog.
    /// </summary>
    public class ParameterLookupDialog : Window
    {
        // ── Data model ───────────────────────────────────────────────
        public class ParamValueEntry
        {
            public string ParameterName { get; set; }
            public string Value { get; set; }
            public string StorageType { get; set; }
            public int ElementCount { get; set; }
            public List<long> ElementIds { get; set; } = new();
        }

        public class LookupResult
        {
            public string SelectedParameter { get; set; }
            public List<Condition> Conditions { get; set; } = new();
            public List<long> MatchedElementIds { get; set; } = new();
            public string Action { get; set; } // "Select", "Color", "Export"
        }

        public class Condition
        {
            public string Parameter { get; set; }
            public string Operator { get; set; } // contains, equals, starts, ends, >, <, !=, empty, not_empty
            public string Value { get; set; }
        }

        // ── UI controls ──────────────────────────────────────────────
        private readonly List<string> _allParams;
        private readonly List<string> _allCategories;
        private readonly Func<string, string, List<ParamValueEntry>> _queryFunc;
        private readonly Func<List<Condition>, string, List<long>> _filterFunc;

        private readonly ComboBox _categoryPicker;
        private readonly TextBox _paramSearch;
        private readonly ListBox _paramList;
        private readonly ListView _valueList;
        private readonly ListBox _conditionList;
        private readonly ComboBox _condParam;
        private readonly ComboBox _condOp;
        private readonly TextBox _condValue;
        private readonly TextBlock _statusText;
        private readonly TextBlock _matchCount;

        private readonly List<Condition> _conditions = new();
        private LookupResult _result;

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }
        private static readonly SolidColorBrush AccentBrush = FZ(new(Color.FromRgb(88, 44, 131)));

        /// <summary>
        /// Create a parameter lookup dialog.
        /// </summary>
        /// <param name="parameters">All available parameter names</param>
        /// <param name="categories">All available category names</param>
        /// <param name="queryFunc">Function(paramName, category) → list of value entries</param>
        /// <param name="filterFunc">Function(conditions, category) → list of matching element IDs</param>
        public ParameterLookupDialog(
            List<string> parameters,
            List<string> categories,
            Func<string, string, List<ParamValueEntry>> queryFunc,
            Func<List<Condition>, string, List<long>> filterFunc)
        {
            _allParams = parameters;
            _allCategories = categories;
            _queryFunc = queryFunc;
            _filterFunc = filterFunc;

            Title = "STING Parameter Lookup";
            Width = 780;
            Height = 620;
            MinWidth = 600;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = FZ(new SolidColorBrush(Color.FromRgb(250, 250, 252)));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Conditions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // ── Row 0: Header ────────────────────────────────────────
            var header = new Border
            {
                Background = AccentBrush,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Parameter Lookup & Filter",
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"{parameters.Count} parameters | {categories.Count} categories",
                FontSize = 11,
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(206, 147, 216))),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Row 1: Main area (param list + value display) ────────
            var mainGrid = new Grid { Margin = new Thickness(12, 8, 12, 4) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // Param list
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Values

            // Left: Category + Param list
            var leftPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

            leftPanel.Children.Add(new TextBlock { Text = "Category", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            _categoryPicker = new ComboBox { FontSize = 11, Height = 26 };
            _categoryPicker.Items.Add("All Categories");
            foreach (string cat in categories.OrderBy(c => c))
                _categoryPicker.Items.Add(cat);
            _categoryPicker.SelectedIndex = 0;
            _categoryPicker.SelectionChanged += (s, e) => RefreshParamValues();
            leftPanel.Children.Add(_categoryPicker);

            leftPanel.Children.Add(new TextBlock { Text = "Parameter search", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) });
            _paramSearch = new TextBox { FontSize = 11, Height = 26 };
            _paramSearch.TextChanged += (s, e) => FilterParamList();
            leftPanel.Children.Add(_paramSearch);

            _paramList = new ListBox
            {
                FontSize = 11,
                Height = 280,
                Margin = new Thickness(0, 4, 0, 0),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(1)
            };
            _paramList.SelectionChanged += (s, e) => RefreshParamValues();
            leftPanel.Children.Add(_paramList);

            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // Right: Value display
            var rightPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            rightPanel.Children.Add(new TextBlock { Text = "Parameter values (by element count)", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });

            _valueList = new ListView
            {
                FontSize = 11,
                Height = 340,
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };
            var valueGridView = new GridView();
            valueGridView.Columns.Add(new GridViewColumn { Header = "Value", Width = 240 });
            valueGridView.Columns.Add(new GridViewColumn { Header = "Count", Width = 60 });
            valueGridView.Columns.Add(new GridViewColumn { Header = "Type", Width = 70 });
            _valueList.View = valueGridView;
            rightPanel.Children.Add(_valueList);

            Grid.SetColumn(rightPanel, 1);
            mainGrid.Children.Add(rightPanel);

            Grid.SetRow(mainGrid, 1);
            root.Children.Add(mainGrid);

            // ── Row 2: Conditions ────────────────────────────────────
            var condBorder = new Border
            {
                Background = FZ(new SolidColorBrush(Color.FromRgb(245, 245, 248))),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var condStack = new StackPanel();

            condStack.Children.Add(new TextBlock { Text = "Conditions / Filters", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

            // Condition input row
            var condInputRow = new Grid();
            condInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // param
            condInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // operator
            condInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // value
            condInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // buttons

            _condParam = new ComboBox { FontSize = 11, Height = 26, IsEditable = true };
            foreach (string p in parameters.Take(100))
                _condParam.Items.Add(p);
            Grid.SetColumn(_condParam, 0);
            condInputRow.Children.Add(_condParam);

            _condOp = new ComboBox { FontSize = 11, Height = 26, Margin = new Thickness(4, 0, 4, 0) };
            foreach (string op in new[] { "contains", "equals", "not equals", "starts with", "ends with", ">", "<", ">=", "<=", "is empty", "is not empty" })
                _condOp.Items.Add(op);
            _condOp.SelectedIndex = 0;
            _condOp.SelectionChanged += (s, e) =>
            {
                // Hide value box for empty/not-empty operators
                string op = _condOp.SelectedItem?.ToString() ?? "";
                _condValue.IsEnabled = !op.Contains("empty");
            };
            Grid.SetColumn(_condOp, 1);
            condInputRow.Children.Add(_condOp);

            _condValue = new TextBox { FontSize = 11, Height = 26 };
            Grid.SetColumn(_condValue, 2);
            condInputRow.Children.Add(_condValue);

            var condBtnStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
            var addCondBtn = CreateSmallButton("+ Add");
            addCondBtn.Click += (s, e) => AddCondition();
            condBtnStack.Children.Add(addCondBtn);
            var clearCondBtn = CreateSmallButton("Clear");
            clearCondBtn.Click += (s, e) => { _conditions.Clear(); RefreshConditionList(); };
            clearCondBtn.Margin = new Thickness(4, 0, 0, 0);
            condBtnStack.Children.Add(clearCondBtn);
            Grid.SetColumn(condBtnStack, 3);
            condInputRow.Children.Add(condBtnStack);

            condStack.Children.Add(condInputRow);

            // Condition list
            _conditionList = new ListBox
            {
                FontSize = 10,
                MaxHeight = 60,
                Margin = new Thickness(0, 4, 0, 0),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            condStack.Children.Add(_conditionList);

            // Match count
            _matchCount = new TextBlock
            {
                FontSize = 11,
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131))),
                Margin = new Thickness(0, 4, 0, 0)
            };
            condStack.Children.Add(_matchCount);

            condBorder.Child = condStack;
            Grid.SetRow(condBorder, 2);
            root.Children.Add(condBorder);

            // ── Row 3: Buttons ───────────────────────────────────────
            var btnBar = new Border
            {
                Background = FZ(new SolidColorBrush(Color.FromRgb(245, 245, 248))),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
            Grid.SetColumn(_statusText, 0);
            btnRow.Children.Add(_statusText);

            var actionBtns = new StackPanel { Orientation = Orientation.Horizontal };

            var selectBtn = CreateButton("Select Matching", false);
            selectBtn.Click += (s, e) => AcceptResult("Select");
            selectBtn.Margin = new Thickness(0, 0, 6, 0);
            actionBtns.Children.Add(selectBtn);

            var colorBtn = CreateButton("Color By Value", false);
            colorBtn.Click += (s, e) => AcceptResult("Color");
            colorBtn.Margin = new Thickness(0, 0, 6, 0);
            actionBtns.Children.Add(colorBtn);

            var cancelBtn = CreateButton("Cancel", false);
            cancelBtn.Click += (s, e) => { _result = null; Close(); };
            cancelBtn.Margin = new Thickness(0, 0, 6, 0);
            actionBtns.Children.Add(cancelBtn);

            var applyBtn = CreateButton("Apply Filter", true);
            applyBtn.Click += (s, e) => AcceptResult("Apply");
            actionBtns.Children.Add(applyBtn);

            Grid.SetColumn(actionBtns, 1);
            btnRow.Children.Add(actionBtns);

            btnBar.Child = btnRow;
            Grid.SetRow(btnBar, 3);
            root.Children.Add(btnBar);

            Content = root;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = null; Close(); }
            };

            Loaded += (s, e) =>
            {
                FilterParamList();
                _paramSearch.Focus();
            };

            // Set Revit as owner
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ParameterLookupDialog owner: {ex.Message}"); }
        }

        // ── Filter / Refresh ─────────────────────────────────────────

        private void FilterParamList()
        {
            string filter = _paramSearch?.Text?.Trim() ?? "";
            _paramList.Items.Clear();

            // Priority params shown first
            var priority = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_TAG_1_TXT", "Mark", "Comments",
                "Type Name", "Family", "Level"
            };

            var sorted = _allParams
                .Where(p => string.IsNullOrEmpty(filter) ||
                    p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(p => priority.Contains(p))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (string p in sorted)
            {
                bool isPriority = priority.Contains(p);
                var item = new ListBoxItem
                {
                    Content = p,
                    FontWeight = isPriority ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isPriority ? AccentBrush : Brushes.Black,
                    FontSize = 11
                };
                _paramList.Items.Add(item);
            }

            _statusText.Text = $"{_paramList.Items.Count} parameters";
        }

        private void RefreshParamValues()
        {
            _valueList.Items.Clear();
            if (_paramList.SelectedItem is not ListBoxItem selected) return;

            string paramName = selected.Content?.ToString();
            if (string.IsNullOrEmpty(paramName)) return;

            string category = _categoryPicker?.SelectedItem?.ToString() ?? "All Categories";
            if (category == "All Categories") category = null;

            try
            {
                var entries = _queryFunc(paramName, category);
                if (entries == null || entries.Count == 0)
                {
                    _statusText.Text = $"No values found for '{paramName}'";
                    return;
                }

                foreach (var entry in entries.OrderByDescending(e => e.ElementCount))
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                    row.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrEmpty(entry.Value) ? "<empty>" : entry.Value,
                        FontSize = 11,
                        Foreground = string.IsNullOrEmpty(entry.Value) ? Brushes.Red : Brushes.Black,
                        FontStyle = string.IsNullOrEmpty(entry.Value) ? FontStyles.Italic : FontStyles.Normal,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = entry.Value
                    });

                    var countText = new TextBlock
                    {
                        Text = entry.ElementCount.ToString(),
                        FontSize = 11,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(countText, 1);
                    row.Children.Add(countText);

                    var typeText = new TextBlock
                    {
                        Text = entry.StorageType ?? "",
                        FontSize = 10,
                        Foreground = Brushes.Gray
                    };
                    Grid.SetColumn(typeText, 2);
                    row.Children.Add(typeText);

                    _valueList.Items.Add(new ListViewItem
                    {
                        Content = row,
                        Tag = entry,
                        Padding = new Thickness(4, 2, 4, 2)
                    });
                }

                int totalElements = entries.Sum(e => e.ElementCount);
                _statusText.Text = $"'{paramName}': {entries.Count} distinct values across {totalElements} elements";
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
                StingLog.Warn($"ParameterLookup query: {ex.Message}");
            }
        }

        // ── Conditions ───────────────────────────────────────────────

        private void AddCondition()
        {
            string param = _condParam?.Text?.Trim();
            string op = _condOp?.SelectedItem?.ToString() ?? "contains";
            string val = _condValue?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(param))
            {
                MessageBox.Show("Select a parameter first.", "Add Condition", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _conditions.Add(new Condition { Parameter = param, Operator = op, Value = val });
            RefreshConditionList();
        }

        private void RefreshConditionList()
        {
            _conditionList.Items.Clear();
            if (_conditions.Count == 0)
            {
                _conditionList.Visibility = Visibility.Collapsed;
                _matchCount.Text = "";
                return;
            }

            _conditionList.Visibility = Visibility.Visible;

            foreach (var cond in _conditions)
            {
                string display = cond.Operator.Contains("empty")
                    ? $"{cond.Parameter} {cond.Operator}"
                    : $"{cond.Parameter} {cond.Operator} \"{cond.Value}\"";

                var item = new ListBoxItem
                {
                    Content = display,
                    FontSize = 10
                };
                // Double-click to remove
                item.MouseDoubleClick += (s, e) =>
                {
                    int idx = _conditionList.Items.IndexOf(item);
                    if (idx >= 0 && idx < _conditions.Count)
                    {
                        _conditions.RemoveAt(idx);
                        RefreshConditionList();
                    }
                };
                _conditionList.Items.Add(item);
            }

            // Execute filter and show match count
            try
            {
                string category = _categoryPicker?.SelectedItem?.ToString() ?? "All Categories";
                if (category == "All Categories") category = null;
                var matches = _filterFunc(_conditions, category);
                _matchCount.Text = $"{matches?.Count ?? 0} elements match {_conditions.Count} condition(s)  (double-click condition to remove)";
            }
            catch (Exception ex)
            {
                _matchCount.Text = $"Filter error: {ex.Message}";
            }
        }

        private void AcceptResult(string action)
        {
            string selectedParam = null;
            if (_paramList.SelectedItem is ListBoxItem sel)
                selectedParam = sel.Content?.ToString();

            string category = _categoryPicker?.SelectedItem?.ToString() ?? "All Categories";
            if (category == "All Categories") category = null;

            List<long> matchedIds = new();
            if (_conditions.Count > 0)
            {
                try { matchedIds = _filterFunc(_conditions, category) ?? new(); }
                catch (Exception ex) { StingLog.Warn($"Filter execution: {ex.Message}"); }
            }

            _result = new LookupResult
            {
                SelectedParameter = selectedParam,
                Conditions = new List<Condition>(_conditions),
                MatchedElementIds = matchedIds,
                Action = action
            };
            Close();
        }

        // ── Button helpers ───────────────────────────────────────────

        private static Button CreateButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 80, Height = 30,
                FontSize = 11, Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand
            };
            if (isPrimary)
            {
                btn.Background = AccentBrush;
                btn.Foreground = Brushes.White;
                btn.BorderBrush = AccentBrush;
            }
            else
            {
                btn.Background = Brushes.White;
                btn.Foreground = FZ(new SolidColorBrush(Color.FromRgb(60, 60, 70)));
                btn.BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210)));
            }
            return btn;
        }

        private static Button CreateSmallButton(string text)
        {
            return new Button
            {
                Content = text, MinWidth = 50, Height = 26,
                FontSize = 10, Padding = new Thickness(8, 2, 8, 2),
                Cursor = Cursors.Hand,
                Background = Brushes.White,
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210)))
            };
        }

        /// <summary>Show the dialog and return result (null if cancelled).</summary>
        public static LookupResult Show(
            List<string> parameters,
            List<string> categories,
            Func<string, string, List<ParamValueEntry>> queryFunc,
            Func<List<Condition>, string, List<long>> filterFunc)
        {
            var dlg = new ParameterLookupDialog(parameters, categories, queryFunc, filterFunc);
            // Phase 98: BCC-aware owner so the lookup stacks above BCC when dispatched from it.
            StingWindowHelper.ApplyOwner(dlg);
            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
