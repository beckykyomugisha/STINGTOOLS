using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// S8.3 — three-way merge dialog for sync conflicts. When two authors
    /// touch the same element between syncs, the server returns a conflict
    /// row instead of overwriting one side; the plugin presents the
    /// coordinator with a side-by-side view of base / local / remote and
    /// the chance to pick the winner per field.
    ///
    /// Used by the <c>SyncConflictsResolverCommand</c> (callable from the
    /// dock panel's QA tab) and by the auto-tagger when it detects a
    /// remote change that conflicts with a pending local edit.
    /// </summary>
    public class SyncConflictDialog : Window
    {
        public IReadOnlyDictionary<string, string> Resolution { get; private set; } = new Dictionary<string, string>();

        public SyncConflictDialog(string elementName, IReadOnlyList<ConflictField> fields)
        {
            Title = $"Resolve conflicts — {elementName}";
            Width = 720; Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xfa, 0xfb, 0xfc));

            var grid = new Grid { Margin = new Thickness(20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            // Header row.
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            AddHeader(grid, 0, "Field");
            AddHeader(grid, 1, "Your edit");
            AddHeader(grid, 2, "Server value");
            AddHeader(grid, 3, "Pick");

            int row = 1;
            var picks = new Dictionary<string, ComboBox>();
            foreach (var f in fields)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
                AddCell(grid, row, 0, f.FieldName, FontWeights.SemiBold);
                AddCell(grid, row, 1, f.LocalValue ?? "<empty>");
                AddCell(grid, row, 2, f.RemoteValue ?? "<empty>");
                var pick = new ComboBox
                {
                    Margin = new Thickness(4),
                    ItemsSource = new[] { "Yours", "Server's", "Keep base" },
                    SelectedIndex = 0,
                };
                Grid.SetRow(pick, row); Grid.SetColumn(pick, 3);
                grid.Children.Add(pick);
                picks[f.FieldName] = pick;
                row++;
            }

            // Buttons.
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            var btnRow = row + 1;
            var ok = new Button { Content = "Apply", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)), Foreground = Brushes.White };
            ok.Click += (_, __) =>
            {
                var resolution = new Dictionary<string, string>();
                foreach (var (field, combo) in picks)
                {
                    var pick = combo.SelectedItem?.ToString() ?? "Yours";
                    resolution[field] = pick switch
                    {
                        "Yours"     => "local",
                        "Server's"  => "remote",
                        _           => "base",
                    };
                }
                Resolution = resolution;
                DialogResult = true;
                Close();
            };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(20, 6, 20, 6) };
            cancel.Click += (_, __) => { DialogResult = false; Close(); };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btns.Children.Add(cancel);
            btns.Children.Add(ok);
            Grid.SetRow(btns, btnRow);
            Grid.SetColumnSpan(btns, 4);
            grid.Children.Add(btns);

            Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static void AddHeader(Grid g, int col, string text)
        {
            var tb = new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(4) };
            Grid.SetRow(tb, 0); Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        private static void AddCell(Grid g, int row, int col, string text, FontWeight? weight = null)
        {
            var tb = new TextBlock
            {
                Text = text, Margin = new Thickness(4), TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = weight ?? FontWeights.Normal,
                ToolTip = text,
            };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        public class ConflictField
        {
            public string FieldName { get; set; } = "";
            public string? BaseValue { get; set; }
            public string? LocalValue { get; set; }
            public string? RemoteValue { get; set; }
        }
    }
}
