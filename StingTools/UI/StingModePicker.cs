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
    /// WPF mode picker dialog matching StingListPicker's corporate style.
    /// Replaces Revit TaskDialog CommandLink mode-selection dialogs with a
    /// polished, branded experience featuring large clickable option cards.
    ///
    /// Usage:
    ///   var options = new List&lt;StingModePicker.ModeOption&gt;
    ///   {
    ///       new("Skip already-tagged", "Only tag elements without complete tags", "skip", true),
    ///       new("Overwrite all tags", "Re-derive and overwrite ALL tokens", "overwrite"),
    ///       new("Auto-increment on collision", "Increment SEQ if tag already exists", "increment"),
    ///   };
    ///   string result = StingModePicker.Show("Auto Tag", "Choose collision mode", options);
    ///   if (result == null) return Result.Cancelled;  // user cancelled
    /// </summary>
    public class StingModePicker : Window
    {
        /// <summary>Represents a single mode option (replaces TaskDialog CommandLink).</summary>
        public class ModeOption
        {
            /// <summary>Primary label text (bold).</summary>
            public string Label { get; set; }
            /// <summary>Secondary description text (grey, smaller).</summary>
            public string Description { get; set; }
            /// <summary>Return value when this option is selected.</summary>
            public string Tag { get; set; }
            /// <summary>If true, this option is visually highlighted as recommended.</summary>
            public bool IsRecommended { get; set; }

            public ModeOption() { }
            public ModeOption(string label, string description, string tag, bool isRecommended = false)
            {
                Label = label;
                Description = description;
                Tag = tag;
                IsRecommended = isRecommended;
            }
        }

        private string _result;
        private readonly List<Border> _optionCards = new List<Border>();
        private int _focusIndex = -1;

        // Brand colours matching StingListPicker
        private static readonly Color BrandPurple = Color.FromRgb(88, 44, 131);
        private static readonly Color BrandPurpleLight = Color.FromRgb(206, 147, 216);
        private static readonly Color CardHover = Color.FromRgb(245, 240, 250);
        private static readonly Color CardSelected = Color.FromRgb(235, 225, 245);
        private static readonly Color RecommendedBorder = Color.FromRgb(88, 44, 131);
        private static readonly Color NormalBorder = Color.FromRgb(220, 220, 230);
        private static readonly Color TextPrimary = Color.FromRgb(40, 40, 50);
        private static readonly Color TextSecondary = Color.FromRgb(120, 120, 140);

        // H-01: Pre-frozen brushes for hover/focus events (avoid per-event allocation)
        private static readonly Brush CardHoverBrush = FrzBrush(CardHover);
        private static readonly Brush CardSelectedBrush = FrzBrush(CardSelected);
        private static Brush FrzBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private StingModePicker(string title, string subtitle,
            List<ModeOption> options, string extraInfo)
        {
            Title = title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 620;
            MinWidth = 340;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Extra info
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Options
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // ── Header bar ──
            var header = new Border
            {
                Background = new SolidColorBrush(BrandPurple),
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
                    Foreground = new SolidColorBrush(BrandPurpleLight),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Extra info bar (optional) ──
            if (!string.IsNullOrEmpty(extraInfo))
            {
                var infoBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 252, 240)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(230, 220, 180)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(14, 8, 14, 8)
                };
                infoBorder.Child = new TextBlock
                {
                    Text = extraInfo,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 90, 50)),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(infoBorder, 1);
                root.Children.Add(infoBorder);
            }

            // ── Option cards ──
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12, 8, 12, 4)
            };
            var cardStack = new StackPanel();

            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                var card = BuildOptionCard(opt, i);
                cardStack.Children.Add(card);
                _optionCards.Add(card);
            }

            scroll.Content = cardStack;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                BorderBrush = new SolidColorBrush(NormalBorder),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                MinWidth = 72,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { _result = null; Close(); };
            footerStack.Children.Add(cancelBtn);
            footer.Child = footerStack;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;

            // Keyboard
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = null; Close(); }
                else if (e.Key == Key.Down) MoveFocus(1, options);
                else if (e.Key == Key.Up) MoveFocus(-1, options);
                else if (e.Key == Key.Enter && _focusIndex >= 0 && _focusIndex < options.Count)
                {
                    _result = options[_focusIndex].Tag;
                    Close();
                }
                else if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    int idx = e.Key - Key.D1;
                    if (idx < options.Count) { _result = options[idx].Tag; Close(); }
                }
            };

            // Auto-focus recommended option
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].IsRecommended) { SetFocusIndex(i); break; }
            }
        }

        private Border BuildOptionCard(ModeOption opt, int index)
        {
            bool recommended = opt.IsRecommended;
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(recommended ? RecommendedBorder : NormalBorder),
                BorderThickness = new Thickness(recommended ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand,
                Tag = opt.Tag
            };

            var stack = new StackPanel();

            // Number + Label row
            var labelRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Number badge
            var badge = new Border
            {
                Background = new SolidColorBrush(recommended ? BrandPurple : Color.FromRgb(200, 200, 210)),
                CornerRadius = new CornerRadius(10),
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 1, 8, 0)
            };
            badge.Child = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            labelRow.Children.Add(badge);

            var label = new TextBlock
            {
                Text = opt.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };
            labelRow.Children.Add(label);

            if (recommended)
            {
                var recLabel = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                recLabel.Child = new TextBlock
                {
                    Text = "Recommended",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50))
                };
                labelRow.Children.Add(recLabel);
            }

            stack.Children.Add(labelRow);

            if (!string.IsNullOrEmpty(opt.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = opt.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextSecondary),
                    Margin = new Thickness(28, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Child = stack;

            // Click handler
            card.MouseLeftButtonDown += (s, e) =>
            {
                _result = opt.Tag;
                Close();
            };

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                card.Background = CardHoverBrush;
                SetFocusIndex(index);
            };
            card.MouseLeave += (s, e) =>
            {
                if (_focusIndex != index)
                    card.Background = Brushes.White;
            };

            return card;
        }

        private void SetFocusIndex(int index)
        {
            if (_focusIndex >= 0 && _focusIndex < _optionCards.Count)
                _optionCards[_focusIndex].Background = Brushes.White;
            _focusIndex = index;
            if (_focusIndex >= 0 && _focusIndex < _optionCards.Count)
                _optionCards[_focusIndex].Background = CardSelectedBrush;
        }

        private void MoveFocus(int delta, List<ModeOption> options)
        {
            int newIndex = _focusIndex + delta;
            if (newIndex < 0) newIndex = options.Count - 1;
            else if (newIndex >= options.Count) newIndex = 0;
            SetFocusIndex(newIndex);
        }

        /// <summary>
        /// Show mode picker and return selected tag string, or null if cancelled.
        /// </summary>
        /// <param name="title">Dialog title (shown in header bar)</param>
        /// <param name="subtitle">Secondary text under title</param>
        /// <param name="options">List of mode options to display as cards</param>
        /// <param name="extraInfo">Optional info banner text (shown below header)</param>
        /// <returns>The Tag of the selected option, or null if cancelled</returns>
        public static string Show(string title, string subtitle,
            List<ModeOption> options, string extraInfo = null)
        {
            if (options == null || options.Count == 0) return null;

            var dlg = new StingModePicker(title, subtitle, options, extraInfo);

            // Phase 98: prefer BCC as owner when open so the mode picker sits
            // above BCC instead of getting buried behind it.
            StingWindowHelper.ApplyOwner(dlg);

            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
