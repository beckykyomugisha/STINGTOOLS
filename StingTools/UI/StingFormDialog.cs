// StingFormDialog — a small declarative WPF input form.
//
// For commands that need a handful of numbers or one choice from the user
// (TaskDialog has no text input). Fields are declared in code; values come
// back typed and validated, invariant-culture, so a comma decimal is not
// silently misread.
//
//   var form = new StingFormDialog("STING — Psychrometric coil check", "Coil conditions")
//       .Number("oaDb", "Outdoor dry bulb (°C)", 28, min: -40, max: 60)
//       .Choice("mode", "Mode", new[] { "A", "B" }, 0);
//   if (form.ShowDialog() != true) return Result.Cancelled;
//   double oaDb = form.Get("oaDb");

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StingTools.UI
{
    public class StingFormDialog : Window
    {
        private sealed class Field
        {
            public string Key;
            public string Label;
            public double Min = double.NegativeInfinity;
            public double Max = double.PositiveInfinity;
            public TextBox Text;
            public ComboBox Combo;
            public CheckBox Check;
        }

        private readonly List<Field> _fields = new List<Field>();
        private readonly Grid _grid;
        private readonly TextBlock _error;
        private readonly Dictionary<string, double> _numbers = new Dictionary<string, double>();
        private readonly Dictionary<string, int> _choices = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _flags = new Dictionary<string, bool>();
        private int _row;

        public StingFormDialog(string title, string heading, string note = null)
        {
            Title = title;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(248, 246, 252));

            var outer = new StackPanel { Margin = new Thickness(16) };
            outer.Children.Add(new TextBlock
            {
                Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                Margin = new Thickness(0, 0, 0, 8)
            });
            if (!string.IsNullOrWhiteSpace(note))
                outer.Children.Add(new TextBlock
                {
                    Text = note, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray,
                    Margin = new Thickness(0, 0, 0, 10)
                });

            _grid = new Grid();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outer.Children.Add(_grid);

            _error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            outer.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Run", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 4, 6, 4) };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true, Padding = new Thickness(6, 4, 6, 4) };
            ok.Click += (s, e) => { if (TryCollect()) DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            outer.Children.Add(buttons);
            Content = outer;
        }

        private void AddRow(string label, UIElement control, string tooltip)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 6), TextWrapping = TextWrapping.Wrap };
            if (!string.IsNullOrEmpty(tooltip)) { tb.ToolTip = tooltip; if (control is FrameworkElement fe) fe.ToolTip = tooltip; }
            Grid.SetRow(tb, _row); Grid.SetColumn(tb, 0); _grid.Children.Add(tb);
            Grid.SetRow(control, _row); Grid.SetColumn(control, 1); _grid.Children.Add(control);
            _row++;
        }

        public StingFormDialog Number(string key, string label, double value,
            double min = double.NegativeInfinity, double max = double.PositiveInfinity, string tooltip = null)
        {
            var f = new Field
            {
                Key = key, Label = label, Min = min, Max = max,
                Text = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture), Margin = new Thickness(0, 2, 0, 6) }
            };
            _fields.Add(f);
            AddRow(label, f.Text, tooltip);
            return this;
        }

        public StingFormDialog Choice(string key, string label, IEnumerable<string> options, int selected = 0, string tooltip = null)
        {
            var cb = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var o in options) cb.Items.Add(o);
            cb.SelectedIndex = cb.Items.Count == 0 ? -1 : Math.Max(0, Math.Min(selected, cb.Items.Count - 1));
            var f = new Field { Key = key, Label = label, Combo = cb };
            _fields.Add(f);
            AddRow(label, cb, tooltip);
            return this;
        }

        public StingFormDialog Flag(string key, string label, bool value, string tooltip = null)
        {
            var chk = new CheckBox { IsChecked = value, Margin = new Thickness(0, 4, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            var f = new Field { Key = key, Label = label, Check = chk };
            _fields.Add(f);
            AddRow(label, chk, tooltip);
            return this;
        }

        private bool TryCollect()
        {
            var errors = new List<string>();
            foreach (var f in _fields)
            {
                if (f.Text != null)
                {
                    string t = (f.Text.Text ?? "").Trim();
                    if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        errors.Add($"{f.Label}: '{t}' is not a number (use a point for decimals).");
                    else if (v < f.Min || v > f.Max)
                        errors.Add($"{f.Label}: {v} is outside {Fmt(f.Min)}…{Fmt(f.Max)}.");
                    else _numbers[f.Key] = v;
                }
                else if (f.Combo != null) _choices[f.Key] = f.Combo.SelectedIndex;
                else if (f.Check != null) _flags[f.Key] = f.Check.IsChecked == true;
            }
            _error.Text = string.Join("\n", errors);
            return errors.Count == 0;
        }

        private static string Fmt(double d) =>
            double.IsInfinity(d) ? (d < 0 ? "−∞" : "∞") : d.ToString("0.###", CultureInfo.InvariantCulture);

        public double Get(string key) => _numbers.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key);
        public int ChoiceIndex(string key) => _choices.TryGetValue(key, out var v) ? v : -1;
        public bool IsSet(string key) => _flags.TryGetValue(key, out var v) && v;
    }
}
