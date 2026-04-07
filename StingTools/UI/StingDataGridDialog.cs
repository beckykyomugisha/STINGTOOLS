using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Reusable corporate-style WPF DataGrid dialog for ExLink-style data views.
    /// Single-page layout with filters, DataGrid, action buttons, and status bar.
    /// Used for Excel Link, CDE Browser, Revision Management, Issue Dashboard, etc.
    /// </summary>
    public class StingDataGridDialog : Window
    {
        // ── Brand colours ──
        private static readonly Color BrandPurple = Color.FromRgb(88, 44, 131);
        private static readonly Color BrandPurpleLight = Color.FromRgb(206, 147, 216);
        private static readonly Color HeaderBg = Color.FromRgb(63, 81, 181);
        private static readonly Color AltRowBg = Color.FromRgb(248, 248, 252);
        private static readonly Color BorderGrey = Color.FromRgb(220, 220, 230);
        private static readonly Color FooterBg = Color.FromRgb(245, 245, 248);

        private readonly DataGrid _grid;
        private readonly TextBlock _statusText;
        private readonly StackPanel _actionPanel;
        private readonly WrapPanel _filterPanel;
        private readonly TextBox _searchBox;

        /// <summary>Items displayed in the DataGrid.</summary>
        public IList<object> Items { get; private set; } = new List<object>();

        /// <summary>Raised when the user clicks an action button.</summary>
        public event Action<string> ActionClicked;

        /// <summary>Raised when search/filter changes.</summary>
        public event Action<string> SearchChanged;

        /// <summary>Selected item(s) in the grid.</summary>
        public IList<object> SelectedItems => _grid.SelectedItems.Cast<object>().ToList();

        /// <summary>Result tag set by action buttons.</summary>
        public string ResultAction { get; set; }

        public StingDataGridDialog(string title, string subtitle, int width = 960, int height = 640)
        {
            Title = title;
            Width = width; Height = height;
            MinWidth = 700; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Filters
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Buttons

            // ── Header ──
            var header = new Border
            {
                Background = new SolidColorBrush(BrandPurple),
                Padding = new Thickness(16, 12, 16, 12)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            if (!string.IsNullOrEmpty(subtitle))
            {
                headerStack.Children.Add(new TextBlock
                {
                    Text = subtitle, FontSize = 11,
                    Foreground = new SolidColorBrush(BrandPurpleLight),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Filter panel ──
            _filterPanel = new WrapPanel { Margin = new Thickness(12, 8, 12, 4) };
            _filterPanel.Children.Add(MakeLabel("Search / Filter"));
            _searchBox = new TextBox { Width = 250, Margin = new Thickness(4, 0, 16, 0), FontSize = 12 };
            _searchBox.TextChanged += (s, e) => SearchChanged?.Invoke(_searchBox.Text);
            _filterPanel.Children.Add(_searchBox);
            Grid.SetRow(_filterPanel, 1);
            root.Children.Add(_filterPanel);

            // ── DataGrid ──
            _grid = new DataGrid
            {
                Margin = new Thickness(12, 4, 12, 8),
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                AlternatingRowBackground = new SolidColorBrush(AltRowBg),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                IsReadOnly = false
            };
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            // ── Button bar ──
            var btnBar = new Border
            {
                Background = new SolidColorBrush(FooterBg),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
            };
            Grid.SetColumn(_statusText, 0);
            btnGrid.Children.Add(_statusText);

            _actionPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(_actionPanel, 1);
            btnGrid.Children.Add(_actionPanel);

            btnBar.Child = btnGrid;
            Grid.SetRow(btnBar, 3);
            root.Children.Add(btnBar);

            Content = root;

            // Set Revit as owner
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Set window owner: {ex.Message}"); }

            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        }

        // ── Column builders ──

        /// <summary>Add a text column (read-only by default).</summary>
        public void AddTextColumn(string header, string bindingPath, double width = 0,
            bool isReadOnly = true, Color? foreground = null)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(bindingPath) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                IsReadOnly = isReadOnly,
                Foreground = new SolidColorBrush(foreground ?? Color.FromRgb(40, 40, 50))
            };
            if (width > 0)
                col.Width = width;
            else
                col.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            _grid.Columns.Add(col);
        }

        /// <summary>Add a checkbox column.</summary>
        public void AddCheckColumn(string header, string bindingPath, double width = 40)
        {
            var col = new DataGridCheckBoxColumn
            {
                Header = header,
                Binding = new Binding(bindingPath) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = width
            };
            _grid.Columns.Add(col);
        }

        // ── Filter controls ──

        /// <summary>Add a dropdown filter to the filter bar.</summary>
        public ComboBox AddFilter(string label, IEnumerable<string> items, Action<string> onChanged)
        {
            _filterPanel.Children.Add(MakeLabel(label));
            var combo = new ComboBox { Width = 180, Margin = new Thickness(4, 0, 16, 0), FontSize = 12 };
            foreach (var item in items) combo.Items.Add(item);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            combo.SelectionChanged += (s, e) => onChanged?.Invoke(combo.SelectedItem as string);
            _filterPanel.Children.Add(combo);
            return combo;
        }

        // ── Action buttons ──

        /// <summary>Add an action button to the footer bar.</summary>
        public Button AddActionButton(string text, string tag, bool isPrimary = false)
        {
            var btn = MakeButton(text, isPrimary);
            btn.Margin = new Thickness(8, 0, 0, 0);
            btn.Click += (s, e) =>
            {
                ResultAction = tag;
                ActionClicked?.Invoke(tag);
                if (tag == "Cancel") { DialogResult = false; Close(); }
                else if (tag == "OK" || tag == "Apply" || tag == "Export" || tag == "Import")
                { DialogResult = true; Close(); }
            };
            _actionPanel.Children.Add(btn);
            return btn;
        }

        // ── Data binding ──

        /// <summary>Set the data source for the grid.</summary>
        public void SetItems<T>(IList<T> items) where T : class
        {
            Items = items.Cast<object>().ToList();
            _grid.ItemsSource = items;
            UpdateStatus();
        }

        /// <summary>Refresh the grid display.</summary>
        public void RefreshItems<T>(IList<T> items) where T : class
        {
            _grid.ItemsSource = null;
            _grid.ItemsSource = items;
            Items = items.Cast<object>().ToList();
            UpdateStatus();
        }

        /// <summary>Update the status bar text.</summary>
        public void SetStatus(string text)
        {
            _statusText.Text = text;
        }

        /// <summary>Update status with item count.</summary>
        public void UpdateStatus()
        {
            _statusText.Text = $"{Items.Count} items";
        }

        /// <summary>Access the underlying DataGrid for advanced configuration.</summary>
        public DataGrid DataGrid => _grid;

        /// <summary>Access the search text.</summary>
        public string SearchText => _searchBox.Text?.Trim() ?? "";

        // ── Helpers ──

        private static TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
            Margin = new Thickness(0, 0, 4, 0)
        };

        private static Button MakeButton(string text, bool primary)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 80, Height = 30, FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand
            };
            if (primary)
            {
                btn.Background = new SolidColorBrush(BrandPurple);
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(BrandPurple);
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
}
