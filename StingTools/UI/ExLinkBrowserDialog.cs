// ─────────────────────────────────────────────────────────────
// ExLinkBrowserDialog.cs — WPF dialog for browsing .link files
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// WPF dialog for browsing and previewing ExLink .link definition files.
    /// Shows a searchable list of .link files with details panel.
    /// </summary>
    internal sealed class ExLinkBrowserDialog : Window
    {
        // ── Data model ──
        internal class LinkFileInfo
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string ElementType { get; set; }
            public int PropertyCount { get; set; }
            public int FilterCount { get; set; }
            public string DataVersion { get; set; }
        }

        // ── UI elements ──
        private readonly TextBox _searchBox;
        private readonly ListBox _fileList;
        private readonly TextBlock _detailText;
        private readonly List<LinkFileInfo> _allFiles;

        /// <summary>The selected .link file path, or null if cancelled.</summary>
        public string SelectedFilePath { get; private set; }

        /// <summary>Show the dialog and return the selected file path (null if cancelled).</summary>
        public static string ShowDialog(List<LinkFileInfo> files, string title = "ExLink Browser")
        {
            var dlg = new ExLinkBrowserDialog(files, title);
            var result = dlg.ShowDialog();
            return result == true ? dlg.SelectedFilePath : null;
        }

        private ExLinkBrowserDialog(List<LinkFileInfo> files, string title)
        {
            _allFiles = files ?? new List<LinkFileInfo>();

            Title = $"STING — {title}";
            Width = 700;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this);
                hwnd.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ExLinkBrowser owner set failed: {ex.Message}"); }

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // buttons

            // ── Header ──
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var headerText = new TextBlock
            {
                Text = "ExLink Definition Browser",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.White
            };
            header.Child = headerText;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // ── Search ──
            _searchBox = new TextBox
            {
                Margin = new Thickness(8, 6, 8, 6),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12
            };
            _searchBox.TextChanged += (s, e) => FilterList();
            Grid.SetRow(_searchBox, 1);
            mainGrid.Children.Add(_searchBox);

            // ── Content: split panel ──
            var contentGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Pixel) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: file list
            _fileList = new ListBox
            {
                FontSize = 11,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1)
            };
            _fileList.SelectionChanged += OnSelectionChanged;
            _fileList.MouseDoubleClick += OnDoubleClick;
            Grid.SetColumn(_fileList, 0);
            contentGrid.Children.Add(_fileList);

            // Right: details
            var detailBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Padding = new Thickness(10)
            };
            _detailText = new TextBlock
            {
                Text = "Select a .link file to see details",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
            };
            detailBorder.Child = new ScrollViewer
            {
                Content = _detailText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetColumn(detailBorder, 2);
            contentGrid.Children.Add(detailBorder);

            Grid.SetRow(contentGrid, 2);
            mainGrid.Children.Add(contentGrid);

            // ── Buttons ──
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 8, 8, 8)
            };

            var btnSelect = new Button
            {
                Content = "Select",
                Width = 80,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x14, 0x8C)),
                IsDefault = true
            };
            btnSelect.Click += (s, e) =>
            {
                if (_fileList.SelectedItem is LinkFileInfo info)
                {
                    SelectedFilePath = info.FilePath;
                    DialogResult = true;
                }
            };
            btnPanel.Children.Add(btnSelect);

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                IsCancel = true
            };
            btnPanel.Children.Add(btnCancel);

            Grid.SetRow(btnPanel, 3);
            mainGrid.Children.Add(btnPanel);

            Content = mainGrid;

            // Populate
            FilterList();
        }

        private void FilterList()
        {
            var search = _searchBox.Text?.Trim().ToLowerInvariant() ?? "";
            var filtered = string.IsNullOrEmpty(search)
                ? _allFiles
                : _allFiles.Where(f =>
                    f.FileName.ToLowerInvariant().Contains(search) ||
                    (f.ElementType ?? "").ToLowerInvariant().Contains(search)).ToList();

            _fileList.Items.Clear();
            foreach (var f in filtered)
                _fileList.Items.Add(f);

            _fileList.DisplayMemberPath = "FileName";
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fileList.SelectedItem is LinkFileInfo info)
            {
                _detailText.Text =
                    $"Name: {info.FileName}\n\n" +
                    $"Element Type: {info.ElementType}\n\n" +
                    $"Properties: {info.PropertyCount}\n" +
                    $"Filters: {info.FilterCount}\n" +
                    $"Data Version: {info.DataVersion}\n\n" +
                    $"Path: {info.FilePath}";
            }
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_fileList.SelectedItem is LinkFileInfo info)
            {
                SelectedFilePath = info.FilePath;
                DialogResult = true;
            }
        }
    }
}
