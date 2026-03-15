using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        }

        private readonly List<ListItem> _allItems;
        private readonly bool _allowMultiSelect;
        private readonly ListBox _listBox;
        private readonly TextBox _searchBox;
        private readonly TextBlock _countText;
        private List<ListItem> _result;

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
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
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
                Background = new SolidColorBrush(Color.FromRgb(88, 44, 131)), // Purple brand
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
                    Foreground = new SolidColorBrush(Color.FromRgb(206, 147, 216)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Search box
            var searchBorder = new Border
            {
                Margin = new Thickness(12, 8, 12, 4),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6)
            };
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
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 160))
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
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 160)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(_countText, 2);
            searchGrid.Children.Add(_countText);

            searchBorder.Child = searchGrid;
            Grid.SetRow(searchBorder, 1);
            root.Children.Add(searchBorder);

            // List box
            _listBox = new ListBox
            {
                Margin = new Thickness(12, 4, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
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
            Grid.SetRow(_listBox, 2);
            root.Children.Add(_listBox);

            // Button bar
            var buttonBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
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
            Grid.SetRow(buttonBar, 3);
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
                return;
            }

            var filtered = _allItems.Where(i =>
                i.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (i.Detail != null && i.Detail.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();

            PopulateList(filtered);
        }

        private void PopulateList(List<ListItem> items)
        {
            _listBox.Items.Clear();
            foreach (var item in items)
            {
                var panel = new DockPanel { Margin = new Thickness(4, 4, 4, 4) };

                var label = new TextBlock
                {
                    Text = item.Label,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 50))
                };
                DockPanel.SetDock(label, Dock.Left);
                panel.Children.Add(label);

                if (!string.IsNullOrEmpty(item.Detail))
                {
                    var detail = new TextBlock
                    {
                        Text = item.Detail,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    DockPanel.SetDock(detail, Dock.Right);
                    panel.Children.Add(detail);
                }

                var lbi = new ListBoxItem
                {
                    Content = panel,
                    Tag = item,
                    Padding = new Thickness(8, 6, 8, 6)
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
                btn.Background = new SolidColorBrush(Color.FromRgb(88, 44, 131));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(88, 44, 131));
            }
            else
            {
                btn.Background = Brushes.White;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210));
            }

            return btn;
        }

        /// <summary>
        /// Show the list picker dialog and return selected items.
        /// Returns null if cancelled, empty list if nothing selected.
        /// </summary>
        public static List<ListItem> Show(string title, string subtitle,
            List<ListItem> items, bool allowMultiSelect = false)
        {
            var dlg = new StingListPicker(title, subtitle, items, allowMultiSelect);
            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
