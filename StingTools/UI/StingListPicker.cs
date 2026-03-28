using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.Select
{
    /// <summary>
    /// Reusable WPF list picker dialog with search filtering, multi-select support,
    /// and a professional corporate appearance. Replaces paginated TaskDialog patterns.
    /// </summary>
    public class StingListPicker : Window
    {
        public class ListItem
        {
            public string Label { get; set; }
            public string Detail { get; set; }
            public object Tag { get; set; }
            public bool IsSelected { get; set; }
            /// <summary>If true, item is flagged as non-compliant (red highlight).</summary>
            public bool IsInvalid { get; set; }
        }

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }

        // Cached frozen brushes for validation and list item rendering
        private static readonly SolidColorBrush BrushNeutralBorder = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210)));
        private static readonly SolidColorBrush BrushGreenBorder = FZ(new SolidColorBrush(Color.FromRgb(76, 175, 80)));
        private static readonly SolidColorBrush BrushRedText = FZ(new SolidColorBrush(Color.FromRgb(211, 47, 47)));
        private static readonly SolidColorBrush BrushDarkText = FZ(new SolidColorBrush(Color.FromRgb(40, 40, 50)));
        private static readonly SolidColorBrush BrushDetailText = FZ(new SolidColorBrush(Color.FromRgb(120, 120, 140)));
        private static readonly SolidColorBrush BrushLightRedBg = FZ(new SolidColorBrush(Color.FromRgb(255, 235, 238)));

        private readonly List<ListItem> _allItems;
        private readonly bool _allowMultiSelect;
        private readonly ListBox _listBox;
        private readonly TextBox _searchBox;
        private readonly TextBlock _countText;
        private readonly Border _searchBorder;
        private readonly TextBlock _validationHint;
        private List<ListItem> _result;
        private HashSet<string> _validCodes;

        private StingListPicker(string title, string subtitle, List<ListItem> items, bool allowMultiSelect)
        {
            _allItems = items;
            _allowMultiSelect = allowMultiSelect;

            Title = title;
            Width = 420;
            Height = 520;
            MinWidth = 320;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = FZ(new SolidColorBrush(Color.FromRgb(250, 250, 252)));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Header bar
            var header = new Border
            {
                Background = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131))), // Purple brand
                Padding = new Thickness(16, 12, 16, 12)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            if (!string.IsNullOrEmpty(subtitle))
            {
                headerStack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    Foreground = FZ(new SolidColorBrush(Color.FromRgb(206, 147, 216))),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Search box
            _searchBorder = new Border
            {
                Margin = new Thickness(12, 8, 12, 4),
                Background = Brushes.White,
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var searchBorder = _searchBorder;
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchIcon = new TextBlock
            {
                Text = "\U0001F50D",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(140, 140, 160)))
            };
            Grid.SetColumn(searchIcon, 0);
            searchGrid.Children.Add(searchIcon);

            _searchBox = new TextBox
            {
                BorderThickness = new Thickness(0),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };
            _searchBox.TextChanged += (s, e) => FilterList();
            Grid.SetColumn(_searchBox, 1);
            searchGrid.Children.Add(_searchBox);

            _countText = new TextBlock
            {
                FontSize = 10,
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(140, 140, 160))),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(_countText, 2);
            searchGrid.Children.Add(_countText);

            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 1);
            root.Children.Add(searchBorder);

            // Validation hint (hidden by default, shown when search text is non-compliant)
            _validationHint = new TextBlock
            {
                FontSize = 10,
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(211, 47, 47))),
                Margin = new Thickness(16, 0, 12, 4),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };
            // Insert as a new auto row after search
            root.RowDefinitions.Insert(2, new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_validationHint, 2);
            root.Children.Add(_validationHint);

            // Shift list and buttons down by one row
            // List box row becomes 3, buttons row becomes 4

            // List box
            _listBox = new ListBox
            {
                Margin = new Thickness(12, 4, 12, 8),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                SelectionMode = allowMultiSelect ? SelectionMode.Multiple : SelectionMode.Single,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _listBox.MouseDoubleClick += (s, e) =>
            {
                if (!allowMultiSelect && _listBox.SelectedItem != null)
                    AcceptSelection();
            };
            Grid.SetRow(_listBox, 3);
            root.Children.Add(_listBox);

            // Button bar
            var buttonBar = new Border
            {
                Background = FZ(new SolidColorBrush(Color.FromRgb(245, 245, 248))),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(220, 220, 230))),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (allowMultiSelect)
            {
                var selectAllBtn = CreateButton("Select All", false);
                selectAllBtn.Click += (s, e) => { _listBox.SelectAll(); };
                selectAllBtn.Margin = new Thickness(0, 0, 8, 0);
                buttonStack.Children.Add(selectAllBtn);

                var clearBtn = CreateButton("Clear", false);
                clearBtn.Click += (s, e) => { _listBox.UnselectAll(); };
                clearBtn.Margin = new Thickness(0, 0, 16, 0);
                buttonStack.Children.Add(clearBtn);
            }

            var cancelBtn = CreateButton("Cancel", false);
            cancelBtn.Click += (s, e) => { _result = null; Close(); };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            buttonStack.Children.Add(cancelBtn);

            var okBtn = CreateButton("OK", true);
            okBtn.Click += (s, e) => AcceptSelection();
            buttonStack.Children.Add(okBtn);

            buttonBar.Child = buttonStack;
            Grid.SetRow(buttonBar, 4);
            root.Children.Add(buttonBar);

            Content = root;

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = null; Close(); }
                else if (e.Key == Key.Enter) AcceptSelection();
            };

            PopulateList(_allItems);
            Loaded += (s, e) => _searchBox.Focus();
        }

        private void FilterList()
        {
            string filter = _searchBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(filter))
            {
                PopulateList(_allItems);
                UpdateSearchValidation("");
                return;
            }

            var filtered = _allItems.Where(i =>
                i.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (i.Detail != null && i.Detail.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();

            PopulateList(filtered);
            UpdateSearchValidation(filter);
        }

        /// <summary>
        /// Turns the search box border red if the typed text doesn't match any
        /// valid code in the validation set. Provides immediate visual feedback.
        /// </summary>
        private void UpdateSearchValidation(string text)
        {
            if (_validCodes == null || string.IsNullOrEmpty(text))
            {
                // No validation set or empty text — neutral border
                _searchBorder.BorderBrush = BrushNeutralBorder;
                _searchBorder.BorderThickness = new Thickness(1);
                if (_validationHint != null)
                    _validationHint.Visibility = Visibility.Collapsed;
                return;
            }

            bool exactMatch = _validCodes.Contains(text);
            // Only scan for partial match if no exact match (avoids O(n) on every keystroke)
            bool partialMatch = exactMatch || _validCodes.Any(c =>
                c.StartsWith(text, StringComparison.OrdinalIgnoreCase));

            if (exactMatch)
            {
                // Valid code — green border
                _searchBorder.BorderBrush = BrushGreenBorder;
                _searchBorder.BorderThickness = new Thickness(2);
                if (_validationHint != null)
                    _validationHint.Visibility = Visibility.Collapsed;
            }
            else if (partialMatch)
            {
                // Partial match — neutral (still typing)
                _searchBorder.BorderBrush = BrushNeutralBorder;
                _searchBorder.BorderThickness = new Thickness(1);
                if (_validationHint != null)
                    _validationHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                // No match — red border (non-compliant)
                _searchBorder.BorderBrush = BrushRedText;
                _searchBorder.BorderThickness = new Thickness(2);
                if (_validationHint != null)
                {
                    _validationHint.Text = $"\u26A0 \"{text}\" is not an ISO 19650 compliant code";
                    _validationHint.Visibility = Visibility.Visible;
                }
            }
        }

        private void PopulateList(List<ListItem> items)
        {
            _listBox.Items.Clear();
            foreach (var item in items)
            {
                bool invalid = item.IsInvalid ||
                    (_validCodes != null && !_validCodes.Contains(item.Label));

                var panel = new DockPanel { Margin = new Thickness(4, 4, 4, 4) };

                var label = new TextBlock
                {
                    Text = item.Label,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = invalid ? BrushRedText : BrushDarkText
                };
                DockPanel.SetDock(label, Dock.Left);
                panel.Children.Add(label);

                if (!string.IsNullOrEmpty(item.Detail))
                {
                    var detail = new TextBlock
                    {
                        Text = item.Detail,
                        FontSize = 11,
                        Foreground = invalid ? BrushRedText : BrushDetailText,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    DockPanel.SetDock(detail, Dock.Right);
                    panel.Children.Add(detail);
                }

                // Non-compliant warning badge
                if (invalid)
                {
                    var badge = new TextBlock
                    {
                        Text = " \u26A0",
                        FontSize = 11,
                        Foreground = BrushRedText,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "Non-compliant: value not in ISO 19650 valid code list"
                    };
                    DockPanel.SetDock(badge, Dock.Left);
                    panel.Children.Add(badge);
                }

                var lbi = new ListBoxItem
                {
                    Content = panel,
                    Tag = item,
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = invalid ? BrushLightRedBg : Brushes.Transparent
                };
                _listBox.Items.Add(lbi);
            }
            _countText.Text = $"{items.Count}/{_allItems.Count}";
        }

        private void AcceptSelection()
        {
            _result = new List<ListItem>();
            foreach (ListBoxItem lbi in _listBox.SelectedItems)
            {
                if (lbi.Tag is ListItem item)
                    _result.Add(item);
            }
            if (_result.Count == 0)
            {
                // If nothing explicitly selected, take first item for single-select
                if (!_allowMultiSelect && _listBox.Items.Count > 0)
                {
                    var first = _listBox.Items[0] as ListBoxItem;
                    if (first?.Tag is ListItem fi) _result.Add(fi);
                }
            }
            Close();
        }

        private static Button CreateButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 72,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand
            };

            if (isPrimary)
            {
                btn.Background = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131)));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131)));
            }
            else
            {
                btn.Background = Brushes.White;
                btn.Foreground = FZ(new SolidColorBrush(Color.FromRgb(60, 60, 70)));
                btn.BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210)));
            }

            return btn;
        }

        /// <summary>
        /// Show the list picker dialog and return selected items.
        /// Returns null if cancelled, empty list if nothing selected.
        /// </summary>
        /// <summary>
        /// Convenience overload that accepts a list of strings and returns the selected string label.
        /// Returns null if cancelled.
        /// </summary>
        public static string Show(string title, string subtitle, List<string> items)
        {
            var listItems = items.Select(s => new ListItem { Label = s }).ToList();
            var result = Show(title, subtitle, listItems, false);
            return result?.FirstOrDefault()?.Label;
        }

        /// <summary>
        /// Show the list picker with ISO 19650 validation. Items not in validCodes
        /// are highlighted with red text and a warning badge. The search box border
        /// turns red when typed text doesn't match any valid code.
        /// </summary>
        public static List<ListItem> Show(string title, string subtitle,
            List<ListItem> items, bool allowMultiSelect, HashSet<string> validCodes)
        {
            var picker = new StingListPicker(title, subtitle, items, allowMultiSelect);
            picker._validCodes = validCodes;
            // Re-populate to apply validation coloring
            picker.PopulateList(items);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(picker);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"StingListPicker owner: {ex.Message}"); }

            picker.ShowDialog();
            return picker._result;
        }

        public static List<ListItem> Show(string title, string subtitle,
            List<ListItem> items, bool allowMultiSelect = false)
        {
            var dlg = new StingListPicker(title, subtitle, items, allowMultiSelect);

            // FIX-B13: Set Revit main window as owner so the dialog stays on top
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(dlg);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Set window owner failed: {ex.Message}"); }

            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
