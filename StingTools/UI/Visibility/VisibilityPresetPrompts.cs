// StingTools — Visibility Center · preset prompts
//
// Split out of VisibilityDropdownHost.cs, which had outgrown the 400-line rule. These two
// dialogs are self-contained modal WPF prompts with no shared state; the host owns the
// popup lifecycle, this owns the prompts.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    /// <summary>The Save-preset name prompt and the multi-select preset picker.</summary>
    public static class VisibilityPresetPrompts
    {
        /// <summary>Modal name prompt for Save preset. Returns null when cancelled.</summary>
        public static string PromptForPresetName()
        {
            var win = new Window
            {
                Title = "Save visibility preset",
                Width = 340,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock
            {
                Text = "Preset name",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var box = new System.Windows.Controls.TextBox { Padding = new Thickness(4, 3, 4, 3) };
            stack.Children.Add(box);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var ok = new Button { Content = "Save", Width = 74, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 74, IsCancel = true };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            stack.Children.Add(row);

            win.Content = stack;

            string chosen = null;
            ok.Click += (s, e) => { chosen = box.Text; win.DialogResult = true; };

            box.Focus();
            return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(chosen) ? chosen.Trim() : null;
        }

        /// <summary>
        /// Tick one or more presets and get back a single combined set. Null when cancelled.
        /// <para>Combining is the point: "Level solo" + "Hide all MEP" is a real request, and
        /// forcing one-at-a-time made the user re-pick constantly. Rules concatenate, and the
        /// engine's existing semantics do the rest — values within a rule OR, rules across
        /// kinds AND.</para>
        /// <para>Hide and Show-only cannot be combined: they are opposite instructions and the
        /// engine rejects a mixed set. Rather than let the user build one and fail at Apply,
        /// ticking a preset of one action disables the presets of the other, live.</para>
        /// </summary>
        public static VisibilitySet PromptForPresets(List<VisibilitySet> presets)
        {
            if (presets == null || presets.Count == 0) return null;

            var win = new Window
            {
                Title = "Visibility presets",
                Width = 420,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new TextBlock
            {
                Text = "Tick any number — they combine. Corporate baseline plus this project's saved presets.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Opacity = 0.8
            };
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            var list = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.85
            };
            Grid.SetRow(status, 2);
            root.Children.Add(status);

            var boxes = new List<Tuple<CheckBox, VisibilitySet>>();

            foreach (var p in presets)
            {
                bool isShowOnly = p.Rules != null &&
                                  p.Rules.Any(r => r != null && r.Action == VisibilityAction.ShowOnly);
                string origin = string.Equals(p.Origin, "project", StringComparison.OrdinalIgnoreCase)
                    ? "  (project)" : "";

                var cb = new CheckBox
                {
                    Margin = new Thickness(0, 3, 0, 3),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{p.Name}{origin}   ·   {(isShowOnly ? "Show-only" : "Hide")}"
                                       + (p.Mode == VisibilityMode.ViewFilter ? "   ·   saved to view" : ""),
                                FontWeight = FontWeights.SemiBold
                            },
                            new TextBlock
                            {
                                Text = p.Description ?? "",
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.75,
                                Margin = new Thickness(0, 1, 0, 0)
                            }
                        }
                    }
                };
                list.Children.Add(cb);
                boxes.Add(Tuple.Create(cb, p));
            }

            // Live conflict lock-out + running count.
            Action refresh = () =>
            {
                var ticked = boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList();
                bool anyShowOnly = ticked.Any(p => p.Rules.Any(r => r.Action == VisibilityAction.ShowOnly));
                bool anyHide     = ticked.Any(p => p.Rules.Any(r => r.Action != VisibilityAction.ShowOnly));

                foreach (var b in boxes)
                {
                    if (b.Item1.IsChecked == true) { b.Item1.IsEnabled = true; continue; }
                    bool showOnly = b.Item2.Rules.Any(r => r.Action == VisibilityAction.ShowOnly);
                    b.Item1.IsEnabled = !((anyShowOnly && !showOnly) || (anyHide && showOnly));
                }

                int rules = ticked.Sum(p => p.Rules.Count);
                status.Text = ticked.Count == 0
                    ? "Nothing ticked."
                    : $"{ticked.Count} preset(s), {rules} rule(s) combined: {string.Join(" + ", ticked.Select(p => p.Name))}"
                      + (anyShowOnly
                            ? "\nShow-only is active, so Hide presets are locked out until you untick these."
                            : anyHide
                                ? "\nHide is active, so Show-only presets are locked out until you untick these."
                                : "");
            };
            foreach (var b in boxes)
            {
                b.Item1.Checked   += (s, e) => refresh();
                b.Item1.Unchecked += (s, e) => refresh();
            }
            refresh();

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var clear  = new Button { Content = "Clear", Width = 74, Margin = new Thickness(0, 0, 6, 0) };
            var ok     = new Button { Content = "Load",  Width = 74, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 74, IsCancel = true };
            row.Children.Add(clear);
            row.Children.Add(ok);
            row.Children.Add(cancel);
            Grid.SetRow(row, 3);
            root.Children.Add(row);

            clear.Click += (s, e) => { foreach (var b in boxes) b.Item1.IsChecked = false; refresh(); };

            VisibilitySet combined = null;
            ok.Click += (s, e) =>
            {
                var ticked = boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList();
                if (ticked.Count == 0) { status.Text = "Tick at least one preset, or press Cancel."; return; }
                combined = Combine(ticked);
                win.DialogResult = true;
            };

            win.Content = root;
            return win.ShowDialog() == true ? combined : null;
        }

        /// <summary>
        /// Merge ticked presets into one set. Mode escalates to ViewFilter if any ticked preset
        /// is saved-to-view — the persistent intent is the stronger one, and silently
        /// downgrading it to Temporary would lose work the user asked to keep.
        /// </summary>
        private static VisibilitySet Combine(List<VisibilitySet> ticked)
        {
            if (ticked.Count == 1) return ticked[0];

            return new VisibilitySet
            {
                Name   = string.Join(" + ", ticked.Select(p => p.Name)),
                Mode   = ticked.Any(p => p.Mode == VisibilityMode.ViewFilter)
                            ? VisibilityMode.ViewFilter : VisibilityMode.Temporary,
                Target = ticked[0].Target,
                Origin = "combined",
                Description = "Combined preset: " + string.Join(" + ", ticked.Select(p => p.Name)),
                Rules  = ticked.SelectMany(p => p.Rules).ToList()
            };
        }
    }
}
