using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Single-step WPF batch rename dialog with live preview.
    /// Combines category/family/type picker, rename operations,
    /// and before/after preview into one unified dialog.
    /// </summary>
    public class BatchRenameDialog : Window
    {
        // ── Data model ───────────────────────────────────────────────
        public class RenameItem
        {
            public string OriginalName { get; set; }
            public string NewName { get; set; }
            public string Category { get; set; }
            public string Family { get; set; }
            public string TypeName { get; set; }
            public object ElementRef { get; set; } // ElementId
            public bool IsSelected { get; set; } = true;
            public bool IsChanged => OriginalName != NewName;
        }

        public class RenameResult
        {
            public List<RenameItem> Items { get; set; } = new();
            public string Operation { get; set; }
            public bool Confirmed { get; set; }
        }

        // ── UI controls ──────────────────────────────────────────────
        private readonly List<RenameItem> _allItems;
        private List<RenameItem> _filteredItems;
        private readonly ListView _listView;
        private readonly TextBox _searchBox;
        private readonly ComboBox _categoryFilter;
        private readonly ComboBox _familyFilter;
        private readonly ComboBox _operationPicker;
        private readonly TextBox _findBox;
        private readonly TextBox _replaceBox;
        private readonly TextBox _prefixBox;
        private readonly TextBox _suffixBox;
        private readonly ComboBox _caseMode;
        private readonly TextBox _seqStart;
        private readonly TextBox _seqPad;
        private readonly TextBlock _statusText;
        private readonly StackPanel _findReplacePanel;
        private readonly StackPanel _prefixSuffixPanel;
        private readonly StackPanel _casePanel;
        private readonly StackPanel _seqPanel;
        private readonly CheckBox _regexCheck;

        private RenameResult _result;

        private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(88, 44, 131));
        private static readonly SolidColorBrush ChangedBrush = new(Color.FromRgb(46, 125, 50));
        private static readonly SolidColorBrush UnchangedBrush = new(Color.FromRgb(158, 158, 158));

        public BatchRenameDialog(string title, List<RenameItem> items)
        {
            _allItems = items;
            _filteredItems = new List<RenameItem>(items);

            Title = title;
            Width = 820;
            Height = 640;
            MinWidth = 650;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filters
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Operations
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status + Buttons

            // ── Row 0: Header ────────────────────────────────────────
            var header = new Border
            {
                Background = AccentBrush,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"{items.Count} items loaded",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(206, 147, 216)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Row 1: Filters (Category + Family + Search) ──────────
            var filterPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var filterGrid = new Grid();
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

            // Category filter
            var catStack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            catStack.Children.Add(new TextBlock { Text = "Category", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            _categoryFilter = new ComboBox { FontSize = 11, Height = 26 };
            _categoryFilter.Items.Add("All Categories");
            foreach (string cat in items.Select(i => i.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c))
                _categoryFilter.Items.Add(cat);
            _categoryFilter.SelectedIndex = 0;
            _categoryFilter.SelectionChanged += (s, e) => { UpdateFamilyFilter(); ApplyFiltersAndPreview(); };
            catStack.Children.Add(_categoryFilter);
            Grid.SetColumn(catStack, 0);
            filterGrid.Children.Add(catStack);

            // Family filter
            var famStack = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            famStack.Children.Add(new TextBlock { Text = "Family", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            _familyFilter = new ComboBox { FontSize = 11, Height = 26 };
            _familyFilter.Items.Add("All Families");
            _familyFilter.SelectedIndex = 0;
            _familyFilter.SelectionChanged += (s, e) => ApplyFiltersAndPreview();
            famStack.Children.Add(_familyFilter);
            Grid.SetColumn(famStack, 1);
            filterGrid.Children.Add(famStack);

            // Search filter
            var searchStack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            searchStack.Children.Add(new TextBlock { Text = "Search / Filter", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            _searchBox = new TextBox { FontSize = 11, Height = 26 };
            _searchBox.TextChanged += (s, e) => ApplyFiltersAndPreview();
            searchStack.Children.Add(_searchBox);
            Grid.SetColumn(searchStack, 2);
            filterGrid.Children.Add(searchStack);

            filterPanel.Child = filterGrid;
            Grid.SetRow(filterPanel, 1);
            root.Children.Add(filterPanel);

            // ── Row 2: Operations ────────────────────────────────────
            var opBorder = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var opStack = new StackPanel();

            // Operation picker row
            var opRow = new Grid();
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            opRow.Children.Add(new TextBlock
            {
                Text = "Operation:",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            _operationPicker = new ComboBox { FontSize = 11, Height = 26 };
            _operationPicker.Items.Add("Find & Replace");
            _operationPicker.Items.Add("Add Prefix / Suffix");
            _operationPicker.Items.Add("Change Case");
            _operationPicker.Items.Add("Sequential Number");
            _operationPicker.Items.Add("Standardise Levels");
            _operationPicker.Items.Add("Remove ' Copy' suffix");
            _operationPicker.Items.Add("Remove prefix up to ' - '");
            _operationPicker.Items.Add("Trim Whitespace");
            _operationPicker.Items.Add("Replace Spaces with Underscores");
            _operationPicker.SelectedIndex = 0;
            _operationPicker.SelectionChanged += (s, e) => { ShowActiveOpPanel(); ApplyFiltersAndPreview(); };
            Grid.SetColumn(_operationPicker, 1);
            opRow.Children.Add(_operationPicker);
            opStack.Children.Add(opRow);

            // Find & Replace panel
            _findReplacePanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var frGrid = new Grid();
            frGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            frGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            frGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            frGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            frGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            frGrid.Children.Add(new TextBlock { Text = "Find:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _findBox = new TextBox { FontSize = 11, Height = 26 };
            _findBox.TextChanged += (s, e) => ApplyFiltersAndPreview();
            Grid.SetColumn(_findBox, 1);
            frGrid.Children.Add(_findBox);

            var replLabel = new TextBlock { Text = "Replace:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
            Grid.SetColumn(replLabel, 2);
            frGrid.Children.Add(replLabel);
            _replaceBox = new TextBox { FontSize = 11, Height = 26 };
            _replaceBox.TextChanged += (s, e) => ApplyFiltersAndPreview();
            Grid.SetColumn(_replaceBox, 3);
            frGrid.Children.Add(_replaceBox);

            _regexCheck = new CheckBox { Content = "Regex", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _regexCheck.Checked += (s, e) => ApplyFiltersAndPreview();
            _regexCheck.Unchecked += (s, e) => ApplyFiltersAndPreview();
            Grid.SetColumn(_regexCheck, 4);
            frGrid.Children.Add(_regexCheck);

            _findReplacePanel.Children.Add(frGrid);
            opStack.Children.Add(_findReplacePanel);

            // Prefix/Suffix panel
            _prefixSuffixPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            var psGrid = new Grid();
            psGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            psGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            psGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            psGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            psGrid.Children.Add(new TextBlock { Text = "Prefix:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _prefixBox = new TextBox { FontSize = 11, Height = 26, Text = "STING - " };
            _prefixBox.TextChanged += (s, e) => ApplyFiltersAndPreview();
            Grid.SetColumn(_prefixBox, 1);
            psGrid.Children.Add(_prefixBox);

            var sufLabel = new TextBlock { Text = "Suffix:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
            Grid.SetColumn(sufLabel, 2);
            psGrid.Children.Add(sufLabel);
            _suffixBox = new TextBox { FontSize = 11, Height = 26 };
            _suffixBox.TextChanged += (s, e) => ApplyFiltersAndPreview();
            Grid.SetColumn(_suffixBox, 3);
            psGrid.Children.Add(_suffixBox);

            _prefixSuffixPanel.Children.Add(psGrid);
            opStack.Children.Add(_prefixSuffixPanel);

            // Case panel
            _casePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            _casePanel.Children.Add(new TextBlock { Text = "Mode:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _caseMode = new ComboBox { FontSize = 11, Height = 26, Width = 160 };
            _caseMode.Items.Add("UPPERCASE");
            _caseMode.Items.Add("lowercase");
            _caseMode.Items.Add("Title Case");
            _caseMode.SelectedIndex = 0;
            _caseMode.SelectionChanged += (s, e) => ApplyFiltersAndPreview();
            _casePanel.Children.Add(_caseMode);
            opStack.Children.Add(_casePanel);

            // Sequential panel
            _seqPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            _seqPanel.Children.Add(new TextBlock { Text = "Start:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _seqStart = new TextBox { FontSize = 11, Height = 26, Width = 60, Text = "1" };
            _seqStart.TextChanged += (s, e) => ApplyFiltersAndPreview();
            _seqPanel.Children.Add(_seqStart);
            _seqPanel.Children.Add(new TextBlock { Text = "Pad:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
            _seqPad = new TextBox { FontSize = 11, Height = 26, Width = 40, Text = "3" };
            _seqPad.TextChanged += (s, e) => ApplyFiltersAndPreview();
            _seqPanel.Children.Add(_seqPad);
            opStack.Children.Add(_seqPanel);

            opBorder.Child = opStack;
            Grid.SetRow(opBorder, 2);
            root.Children.Add(opBorder);

            // ── Row 3: Preview ListView ──────────────────────────────
            _listView = new ListView
            {
                Margin = new Thickness(12, 8, 12, 4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                FontSize = 11
            };

            // Set up GridView columns
            var gridView = new GridView();
            gridView.Columns.Add(new GridViewColumn { Header = "Original Name", Width = 260 });
            gridView.Columns.Add(new GridViewColumn { Header = "New Name", Width = 260 });
            gridView.Columns.Add(new GridViewColumn { Header = "Category", Width = 100 });
            gridView.Columns.Add(new GridViewColumn { Header = "Family", Width = 120 });
            _listView.View = gridView;

            Grid.SetRow(_listView, 3);
            root.Children.Add(_listView);

            // ── Row 4: Status + Buttons ──────────────────────────────
            var bottomBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 110))
            };
            Grid.SetColumn(_statusText, 0);
            bottomGrid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };
            var selectAllBtn = CreateButton("Select All", false);
            selectAllBtn.Click += (s, e) =>
            {
                foreach (var item in _filteredItems) item.IsSelected = true;
                RefreshListView();
            };
            selectAllBtn.Margin = new Thickness(0, 0, 6, 0);
            btnStack.Children.Add(selectAllBtn);

            var selectNoneBtn = CreateButton("Select None", false);
            selectNoneBtn.Click += (s, e) =>
            {
                foreach (var item in _filteredItems) item.IsSelected = false;
                RefreshListView();
            };
            selectNoneBtn.Margin = new Thickness(0, 0, 16, 0);
            btnStack.Children.Add(selectNoneBtn);

            var cancelBtn = CreateButton("Cancel", false);
            cancelBtn.Click += (s, e) => { _result = null; Close(); };
            cancelBtn.Margin = new Thickness(0, 0, 6, 0);
            btnStack.Children.Add(cancelBtn);

            var applyBtn = CreateButton("Apply Rename", true);
            applyBtn.Click += (s, e) => AcceptAndClose();
            btnStack.Children.Add(applyBtn);

            Grid.SetColumn(btnStack, 1);
            bottomGrid.Children.Add(btnStack);

            bottomBar.Child = bottomGrid;
            Grid.SetRow(bottomBar, 4);
            root.Children.Add(bottomBar);

            Content = root;

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = null; Close(); }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) AcceptAndClose();
            };

            Loaded += (s, e) =>
            {
                UpdateFamilyFilter();
                ApplyFiltersAndPreview();
                _searchBox.Focus();
            };

            // Set Revit as owner
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"BatchRenameDialog owner: {ex.Message}"); }
        }

        // ── Filtering ────────────────────────────────────────────────

        private void UpdateFamilyFilter()
        {
            string selectedCat = _categoryFilter.SelectedItem?.ToString() ?? "All Categories";
            string currentFam = _familyFilter.SelectedItem?.ToString();

            _familyFilter.Items.Clear();
            _familyFilter.Items.Add("All Families");

            var families = _allItems
                .Where(i => selectedCat == "All Categories" || i.Category == selectedCat)
                .Select(i => i.Family)
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .OrderBy(f => f);

            foreach (string fam in families)
                _familyFilter.Items.Add(fam);

            // Restore previous selection if still available
            if (currentFam != null && _familyFilter.Items.Contains(currentFam))
                _familyFilter.SelectedItem = currentFam;
            else
                _familyFilter.SelectedIndex = 0;
        }

        private void ApplyFiltersAndPreview()
        {
            string catFilter = _categoryFilter?.SelectedItem?.ToString() ?? "All Categories";
            string famFilter = _familyFilter?.SelectedItem?.ToString() ?? "All Families";
            string search = _searchBox?.Text?.Trim() ?? "";

            _filteredItems = _allItems.Where(item =>
            {
                if (catFilter != "All Categories" && item.Category != catFilter) return false;
                if (famFilter != "All Families" && item.Family != famFilter) return false;
                if (!string.IsNullOrEmpty(search) &&
                    item.OriginalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            }).ToList();

            // Apply rename operation to generate preview
            ApplyRenamePreview();
            RefreshListView();
            UpdateStatusText();
        }

        // ── Rename operations ────────────────────────────────────────

        private void ApplyRenamePreview()
        {
            int opIdx = _operationPicker?.SelectedIndex ?? 0;
            int seq = 1;
            int pad = 3;
            int.TryParse(_seqStart?.Text, out seq);
            int.TryParse(_seqPad?.Text, out pad);
            if (pad < 1) pad = 1;
            if (pad > 6) pad = 6;

            foreach (var item in _filteredItems)
            {
                string name = item.OriginalName;
                item.NewName = opIdx switch
                {
                    0 => ApplyFindReplace(name),
                    1 => ApplyPrefixSuffix(name),
                    2 => ApplyCase(name),
                    3 => ApplySequential(name, ref seq, pad),
                    4 => StandardiseLevelName(name),
                    5 => Regex.Replace(name, @"\s*Copy\s*\d*$", "", RegexOptions.IgnoreCase),
                    6 => RemovePrefixUpToDash(name),
                    7 => name.Trim(),
                    8 => name.Replace(' ', '_'),
                    _ => name
                };
            }
        }

        private string ApplyFindReplace(string name)
        {
            string find = _findBox?.Text ?? "";
            string replace = _replaceBox?.Text ?? "";
            if (string.IsNullOrEmpty(find)) return name;

            try
            {
                if (_regexCheck?.IsChecked == true)
                    return Regex.Replace(name, find, replace, RegexOptions.IgnoreCase);
                else
                    return name.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return name; // Invalid regex — no change
            }
        }

        private string ApplyPrefixSuffix(string name)
        {
            string prefix = _prefixBox?.Text ?? "";
            string suffix = _suffixBox?.Text ?? "";
            return prefix + name + suffix;
        }

        private string ApplyCase(string name)
        {
            int mode = _caseMode?.SelectedIndex ?? 0;
            return mode switch
            {
                0 => name.ToUpperInvariant(),
                1 => name.ToLowerInvariant(),
                2 => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()),
                _ => name
            };
        }

        private static string ApplySequential(string name, ref int seq, int pad)
        {
            string result = $"{name}-{seq.ToString().PadLeft(pad, '0')}";
            seq++;
            return result;
        }

        private static string StandardiseLevelName(string name)
        {
            var replacements = new (string find, string replace)[]
            {
                ("Ground Floor", "GF"), ("Ground Level", "GF"), ("Ground", "GF"),
                ("Basement 2", "B2"), ("Basement 1", "B1"), ("Basement", "B1"),
                ("Roof Level", "RF"), ("Roof", "RF"),
                ("Level 10", "L10"), ("Level 9", "L09"), ("Level 8", "L08"),
                ("Level 7", "L07"), ("Level 6", "L06"), ("Level 5", "L05"),
                ("Level 4", "L04"), ("Level 3", "L03"), ("Level 2", "L02"),
                ("Level 1", "L01"),
                ("Third Floor", "L03"), ("Second Floor", "L02"), ("First Floor", "L01"),
                ("Mezzanine", "MZ"), ("Plant Room", "PL"),
            };

            foreach (var (find, replace) in replacements)
            {
                if (name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    name = Regex.Replace(name, Regex.Escape(find), replace, RegexOptions.IgnoreCase);
                    break;
                }
            }
            return name;
        }

        private static string RemovePrefixUpToDash(string name)
        {
            int idx = name.IndexOf(" - ", StringComparison.Ordinal);
            if (idx >= 0 && idx < name.Length - 3)
                return name.Substring(idx + 3);
            return name;
        }

        // ── UI update ────────────────────────────────────────────────

        private void ShowActiveOpPanel()
        {
            int opIdx = _operationPicker?.SelectedIndex ?? 0;
            _findReplacePanel.Visibility = opIdx == 0 ? Visibility.Visible : Visibility.Collapsed;
            _prefixSuffixPanel.Visibility = opIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
            _casePanel.Visibility = opIdx == 2 ? Visibility.Visible : Visibility.Collapsed;
            _seqPanel.Visibility = opIdx == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshListView()
        {
            _listView.Items.Clear();
            var gv = _listView.View as GridView;

            foreach (var item in _filteredItems)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

                var origText = new TextBlock
                {
                    Text = item.OriginalName,
                    FontSize = 11,
                    Foreground = item.IsChanged ? Brushes.Gray : UnchangedBrush,
                    TextDecorations = item.IsChanged ? TextDecorations.Strikethrough : null,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.OriginalName
                };
                Grid.SetColumn(origText, 0);
                row.Children.Add(origText);

                var newText = new TextBlock
                {
                    Text = item.NewName,
                    FontSize = 11,
                    FontWeight = item.IsChanged ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = item.IsChanged ? ChangedBrush : UnchangedBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.NewName
                };
                Grid.SetColumn(newText, 1);
                row.Children.Add(newText);

                var catText = new TextBlock
                {
                    Text = item.Category ?? "",
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(catText, 2);
                row.Children.Add(catText);

                var famText = new TextBlock
                {
                    Text = item.Family ?? "",
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(famText, 3);
                row.Children.Add(famText);

                var lvi = new ListViewItem
                {
                    Content = row,
                    Tag = item,
                    Padding = new Thickness(4, 3, 4, 3),
                    Background = item.IsChanged
                        ? new SolidColorBrush(Color.FromRgb(232, 245, 233))
                        : Brushes.Transparent
                };
                lvi.MouseDoubleClick += (s, e) =>
                {
                    if (lvi.Tag is RenameItem ri)
                    {
                        ri.IsSelected = !ri.IsSelected;
                        RefreshListView();
                    }
                };
                _listView.Items.Add(lvi);
            }
        }

        private void UpdateStatusText()
        {
            int changed = _filteredItems.Count(i => i.IsChanged);
            int selected = _filteredItems.Count(i => i.IsSelected && i.IsChanged);
            _statusText.Text = $"{_filteredItems.Count} items shown | {changed} will change | {selected} selected for rename";
        }

        private void AcceptAndClose()
        {
            _result = new RenameResult
            {
                Items = _filteredItems.Where(i => i.IsSelected && i.IsChanged).ToList(),
                Operation = _operationPicker?.SelectedItem?.ToString() ?? "",
                Confirmed = true
            };
            Close();
        }

        private static Button CreateButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 80,
                Height = 30,
                FontSize = 11,
                Padding = new Thickness(12, 4, 12, 4),
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
                btn.Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210));
            }
            return btn;
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Show the batch rename dialog and return the result.
        /// Returns null if cancelled.
        /// </summary>
        public static RenameResult Show(string title, List<RenameItem> items)
        {
            var dlg = new BatchRenameDialog(title, items);
            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
