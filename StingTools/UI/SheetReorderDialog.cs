// StingTools — reorder a drawing set, then renumber it
//
// WHY THIS FILE EXISTS
// --------------------
// "The first sheet was supposed to be the third."
//
// Auto-Number Sheets numbers in the order the sheets are already in, which is the
// right default and no help at all when the order itself is the mistake. The only
// route was to renumber a sheet by hand so it sorted where you wanted, then run
// Auto-Number and hope — on a set of two hundred, that is a day.
//
// Two observations shape this dialog:
//
//   1. On a large disorganised set, nobody wants to drag two hundred rows. A SORT
//      gets you ninety percent of the way in one click — by level, by name, by
//      what the sheet contains — and then a handful of nudges finish it. So the
//      sort presets are the primary control and moving rows is the touch-up.
//
//   2. "Make this one third" is a position, not a direction. Move Up pressed
//      eleven times is how an ordering gets quietly wrong, so there is a "Move
//      to #" that takes the number directly.
//
// The new number for every row is shown live, because the number is what gets
// printed, exported and referenced — and a preview that shows only the order asks
// the operator to do the numbering in their head.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StingTools.UI
{
    public class SheetReorderDialog : Window
    {
        public class Item
        {
            public object Tag { get; set; }
            public string Number { get; set; }
            public string Name { get; set; }
            public string Level { get; set; }
            public string Discipline { get; set; }
            /// <summary>Set by the caller's formatter each time the order changes.</summary>
            public string NewNumber { get; set; }
            public bool Locked { get; set; }
        }

        private readonly List<Item> _items;
        private readonly Func<Item, int, string> _format;
        private readonly ListBox _list;
        private readonly TextBlock _hint;
        private bool _ok;

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }
        private static readonly SolidColorBrush Brand = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131)));
        private static readonly SolidColorBrush Muted = FZ(new SolidColorBrush(Color.FromRgb(120, 120, 140)));
        private static readonly SolidColorBrush Changed = FZ(new SolidColorBrush(Color.FromRgb(21, 101, 192)));
        private static readonly SolidColorBrush LockedBg = FZ(new SolidColorBrush(Color.FromRgb(245, 245, 248)));

        private SheetReorderDialog(string title, string subtitle, List<Item> items,
                                   Func<Item, int, string> format)
        {
            _items = items;
            _format = format;

            Title = title;
            Width = 760;
            Height = 620;
            MinWidth = 560;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = FZ(new SolidColorBrush(Color.FromRgb(250, 250, 252)));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── header ───────────────────────────────────────────────────
            var header = new Border { Background = Brand, Padding = new Thickness(16, 12, 16, 12) };
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            hs.Children.Add(new TextBlock
            {
                Text = subtitle, FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                Foreground = FZ(new SolidColorBrush(Color.FromRgb(220, 210, 235))),
                TextWrapping = TextWrapping.Wrap
            });
            header.Child = hs;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── sort presets: the fast path ──────────────────────────────
            var sortBar = new WrapPanel { Margin = new Thickness(14, 10, 14, 4) };
            sortBar.Children.Add(new TextBlock
            {
                Text = "Sort by:", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0), Foreground = Muted
            });
            AddSort(sortBar, "Level, then name", it => it
                .OrderBy(x => x.Level ?? "", NaturalOrder.Instance)
                .ThenBy(x => x.Name ?? "", NaturalOrder.Instance));
            AddSort(sortBar, "Sheet name", it => it
                .OrderBy(x => x.Name ?? "", NaturalOrder.Instance));
            AddSort(sortBar, "Current number", it => it
                .OrderBy(x => x.Number ?? "", NaturalOrder.Instance));
            AddSort(sortBar, "Reverse", it => it.Reverse());
            Grid.SetRow(sortBar, 1);
            root.Children.Add(sortBar);

            // ── the list ─────────────────────────────────────────────────
            _list = new ListBox
            {
                Margin = new Thickness(14, 4, 14, 4),
                SelectionMode = SelectionMode.Extended,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(200, 200, 210))),
            };
            _list.PreviewKeyDown += OnListKey;
            Grid.SetRow(_list, 2);
            root.Children.Add(_list);

            // ── move controls ────────────────────────────────────────────
            var moveBar = new WrapPanel { Margin = new Thickness(14, 4, 14, 4) };
            AddButton(moveBar, "Top", () => MoveSelected(int.MinValue));
            AddButton(moveBar, "Up", () => Nudge(-1));
            AddButton(moveBar, "Down", () => Nudge(+1));
            AddButton(moveBar, "Bottom", () => MoveSelected(int.MaxValue));

            var posBox = new TextBox
            {
                Width = 54, Margin = new Thickness(16, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Target position, 1-based"
            };
            moveBar.Children.Add(new TextBlock
            {
                Text = "Move to #", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 6, 0), Foreground = Muted
            });
            moveBar.Children.Add(posBox);
            AddButton(moveBar, "Go", () =>
            {
                if (int.TryParse(posBox.Text.Trim(), out int p) && p >= 1)
                    MoveSelected(p - 1);
            });
            Grid.SetRow(moveBar, 3);
            root.Children.Add(moveBar);

            // ── footer ───────────────────────────────────────────────────
            var footer = new Grid { Margin = new Thickness(14, 4, 14, 12) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _hint = new TextBlock
            {
                Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(_hint, 0);
            footer.Children.Add(_hint);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var cancel = new Button { Content = "Cancel", Width = 88, Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += (s, e) => { _ok = false; Close(); };
            var ok = new Button
            {
                Content = "Renumber", Width = 110, Height = 28, Margin = new Thickness(6, 0, 0, 0),
                Background = Brand, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
            };
            ok.Click += (s, e) => { _ok = true; Close(); };
            actions.Children.Add(cancel);
            actions.Children.Add(ok);
            Grid.SetColumn(actions, 1);
            footer.Children.Add(actions);

            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            Content = root;
            Refresh();
        }

        private void AddSort(Panel host, string label, Func<IEnumerable<Item>, IEnumerable<Item>> sort)
        {
            var b = new Button
            {
                Content = label, Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0)
            };
            b.Click += (s, e) =>
            {
                // Locked rows keep their place. They are not being renumbered, so
                // moving them would only mislead about where they sit.
                var sorted = sort(_items.Where(x => !x.Locked)).ToList();
                var rebuilt = new List<Item>();
                int si = 0;
                foreach (var original in _items)
                    rebuilt.Add(original.Locked ? original : sorted[si++]);
                _items.Clear();
                _items.AddRange(rebuilt);
                Refresh();
            };
            host.Children.Add(b);
        }

        private static void AddButton(Panel host, string label, Action act)
        {
            var b = new Button
            {
                Content = label, Height = 26, Width = 72, Margin = new Thickness(0, 0, 6, 0)
            };
            b.Click += (s, e) => act();
            host.Children.Add(b);
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
            if (e.Key == Key.Up) { Nudge(-1); e.Handled = true; }
            else if (e.Key == Key.Down) { Nudge(+1); e.Handled = true; }
        }

        private List<int> SelectedIndices()
            => _list.SelectedItems.Cast<object>()
                    .Select(o => _list.Items.IndexOf(o))
                    .Where(i => i >= 0)
                    .OrderBy(i => i)
                    .ToList();

        private void Nudge(int delta)
        {
            var sel = SelectedIndices();
            if (sel.Count == 0) return;
            // Moving DOWN must start from the bottom of the selection, or the rows
            // leapfrog each other and the block comes apart.
            if (delta > 0) sel.Reverse();

            foreach (int i in sel)
            {
                int j = i + delta;
                if (j < 0 || j >= _items.Count) return;       // block is at the edge
                if (_items[i].Locked || _items[j].Locked) return;
                var t = _items[i]; _items[i] = _items[j]; _items[j] = t;
            }
            Refresh(sel.Select(i => i + delta).ToList());
        }

        private void MoveSelected(int target)
        {
            var sel = SelectedIndices();
            if (sel.Count == 0) return;

            var moving = sel.Select(i => _items[i]).Where(x => !x.Locked).ToList();
            if (moving.Count == 0) return;

            var rest = _items.Where(x => !moving.Contains(x)).ToList();
            int at = target == int.MinValue ? 0
                   : target == int.MaxValue ? rest.Count
                   : Math.Max(0, Math.Min(target, rest.Count));

            rest.InsertRange(at, moving);
            _items.Clear();
            _items.AddRange(rest);
            Refresh(Enumerable.Range(at, moving.Count).ToList());
        }

        private void Refresh(List<int> select = null)
        {
            // The new number is recomputed for every row on every change, because a
            // move changes the number of every row after it. Showing the order alone
            // would leave the operator to do that arithmetic.
            int seq = 1;
            foreach (var it in _items)
                it.NewNumber = it.Locked ? it.Number : _format(it, seq++);

            _list.Items.Clear();
            int pos = 1;
            foreach (var it in _items)
                _list.Items.Add(Render(it, pos++));

            if (select != null)
            {
                _list.SelectedItems.Clear();
                foreach (int i in select)
                    if (i >= 0 && i < _list.Items.Count)
                        _list.SelectedItems.Add(_list.Items[i]);
                if (_list.SelectedItems.Count > 0)
                    _list.ScrollIntoView(_list.SelectedItems[_list.SelectedItems.Count - 1]);
            }

            int changing = _items.Count(x => !x.Locked
                && !string.Equals(x.Number, x.NewNumber, StringComparison.Ordinal));
            int locked = _items.Count(x => x.Locked);
            _hint.Text = $"{changing} of {_items.Count} will change"
                + (locked > 0 ? $"  ·  {locked} locked, left as they are" : "")
                + "   ·   select rows, then Alt+↑ / Alt+↓ to move";
        }

        private UIElement Render(Item it, int pos)
        {
            var g = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            if (it.Locked) g.Background = LockedBg;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            void Cell(string text, int col, Brush fg, FontWeight w)
            {
                var tb = new TextBlock
                {
                    Text = text ?? "", Foreground = fg, FontWeight = w,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tb, col);
                g.Children.Add(tb);
            }

            bool moved = !it.Locked && !string.Equals(it.Number, it.NewNumber, StringComparison.Ordinal);

            Cell(pos.ToString(), 0, Muted, FontWeights.Normal);
            Cell(it.Number, 1, Muted, FontWeights.Normal);
            Cell(it.Name + (it.Locked ? "   (locked)" : ""), 2,
                 it.Locked ? Muted : Brushes.Black, FontWeights.Normal);
            Cell(moved ? "→  " + it.NewNumber : it.NewNumber, 3,
                 moved ? Changed : Muted, moved ? FontWeights.SemiBold : FontWeights.Normal);
            return g;
        }

        /// <summary>Show the dialog. Returns the items in their chosen order, or null
        /// when cancelled. `format` builds the number a row at position n would get.</summary>
        public static List<Item> Show(string title, string subtitle, List<Item> items,
                                      Func<Item, int, string> format)
        {
            var dlg = new SheetReorderDialog(title, subtitle, items, format);
            try { dlg.Owner = System.Windows.Application.Current?.MainWindow; }
            catch (Exception ex) { Core.StingLog.Warn($"SheetReorderDialog owner: {ex.Message}"); }
            dlg.ShowDialog();
            return dlg._ok ? dlg._items.ToList() : null;
        }

        /// <summary>Orders "A-2" before "A-10". Plain string ordering puts 10 first,
        /// which is exactly the reshuffle this dialog exists to undo.</summary>
        internal sealed class NaturalOrder : IComparer<string>
        {
            public static readonly NaturalOrder Instance = new NaturalOrder();

            public int Compare(string a, string b)
            {
                a ??= ""; b ??= "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        string da = a.Substring(si, i - si).TrimStart('0');
                        string db = b.Substring(sj, j - sj).TrimStart('0');
                        if (da.Length != db.Length) return da.Length - db.Length;
                        int c = string.CompareOrdinal(da, db);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (a.Length - i) - (b.Length - j);
            }
        }
    }
}
