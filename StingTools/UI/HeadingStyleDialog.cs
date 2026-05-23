using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned by HeadingStyleDialog containing the user's style choices.
    /// </summary>
    public class HeadingStyleResult
    {
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool ApplyTier2 { get; set; }
        public bool ApplyTier3 { get; set; }
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// Unified WPF dialog for setting TAG7 heading styles. Replaces the 3-step
    /// sequential TaskDialog flow in SetTag7HeadingStyleCommand with a single
    /// window containing style selection, tier checkboxes, and live preview.
    /// </summary>
    public class HeadingStyleDialog : Window
    {
        private RadioButton _rbBoldUnderline;
        private RadioButton _rbUnderlineOnly;
        private RadioButton _rbBoldOnly;
        private RadioButton _rbAllStyles;
        private CheckBox _cbTier2;
        private CheckBox _cbTier3;
        private TextBlock _previewTier2;
        private TextBlock _previewTier3;
        private HeadingStyleResult _result;

        // Light, contrast-safe palette (was dark #2D2D30).
        private static readonly Color BgColor = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardBorder = Color.FromRgb(0xCF, 0xD8, 0xDC);
        private static readonly Color FgColor = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color SubtleColor = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color PreviewBg = Color.FromRgb(0x1A, 0x23, 0x7E);

        private HeadingStyleDialog(string currentTier2Style, string currentTier3Style)
        {
            Title = "TAG7 Heading Style";
            Width = 550;
            Height = 480;
            MinWidth = 550;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(BgColor);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.NoResize;

            // Set Revit as owner window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HeadingStyleDialog: could not set owner: {ex.Message}");
            }

            Content = BuildLayout(currentTier2Style, currentTier3Style);
        }

        private UIElement BuildLayout(string currentTier2Style, string currentTier3Style)
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            // ── Current Settings ──
            var currentHeader = MakeHeader("Current Settings");
            root.Children.Add(currentHeader);

            var currentPanel = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 12)
            };
            var currentStack = new StackPanel();
            currentStack.Children.Add(MakeLabel($"Tier 2 (Technical): {currentTier2Style}", SubtleColor));
            currentStack.Children.Add(MakeLabel($"Tier 3 (Full Specification): {currentTier3Style}", SubtleColor));
            currentPanel.Child = currentStack;
            root.Children.Add(currentPanel);

            // ── Style Selector ──
            root.Children.Add(MakeHeader("Style Selector"));

            var stylesPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };

            _rbBoldUnderline = MakeStyleCard(
                "Bold + Underline", "(Recommended)",
                bold: true, italic: false, underline: true, isDefault: true);
            _rbUnderlineOnly = MakeStyleCard(
                "Underline Only", null,
                bold: false, italic: false, underline: true, isDefault: false);
            _rbBoldOnly = MakeStyleCard(
                "Bold Only", null,
                bold: true, italic: false, underline: false, isDefault: false);
            _rbAllStyles = MakeStyleCard(
                "All Styles (Bold + Italic + Underline)", null,
                bold: true, italic: true, underline: true, isDefault: false);

            stylesPanel.Children.Add(_rbBoldUnderline);
            stylesPanel.Children.Add(_rbUnderlineOnly);
            stylesPanel.Children.Add(_rbBoldOnly);
            stylesPanel.Children.Add(_rbAllStyles);
            root.Children.Add(stylesPanel);

            // ── Tier Application ──
            root.Children.Add(MakeHeader("Apply To"));

            var tierPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
            _cbTier2 = new CheckBox
            {
                Content = "Apply to State 2 (Technical)",
                IsChecked = true,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 13,
                Margin = new Thickness(4, 2, 0, 2)
            };
            _cbTier3 = new CheckBox
            {
                Content = "Apply to State 3 (Full Specification)",
                IsChecked = true,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 13,
                Margin = new Thickness(4, 2, 0, 2)
            };
            _cbTier2.Checked += (s, e) => UpdatePreview();
            _cbTier2.Unchecked += (s, e) => UpdatePreview();
            _cbTier3.Checked += (s, e) => UpdatePreview();
            _cbTier3.Unchecked += (s, e) => UpdatePreview();
            tierPanel.Children.Add(_cbTier2);
            tierPanel.Children.Add(_cbTier3);
            root.Children.Add(tierPanel);

            // ── Live Preview ──
            root.Children.Add(MakeHeader("Live Preview"));

            var previewBorder = new Border
            {
                Background = new SolidColorBrush(PreviewBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 16)
            };
            var previewStack = new StackPanel();
            _previewTier2 = new TextBlock { Foreground = new SolidColorBrush(FgColor), FontSize = 13, Margin = new Thickness(0, 2, 0, 2) };
            _previewTier3 = new TextBlock { Foreground = new SolidColorBrush(FgColor), FontSize = 13, Margin = new Thickness(0, 2, 0, 2) };
            previewStack.Children.Add(_previewTier2);
            previewStack.Children.Add(_previewTier3);
            previewBorder.Child = previewStack;
            root.Children.Add(previewBorder);

            // ── OK / Cancel ──
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new Button
            {
                Content = "OK",
                Width = 90,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(AccentColor),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = true
            };
            btnOk.Click += BtnOk_Click;

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 30,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 13,
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsCancel = true
            };
            btnCancel.Click += (s, e) => { _result = new HeadingStyleResult { Cancelled = true }; Close(); };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            root.Children.Add(btnPanel);

            // Initial preview
            UpdatePreview();

            return root;
        }

        private RadioButton MakeStyleCard(string label, string badge,
            bool bold, bool italic, bool underline, bool isDefault)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel();

            // Label row
            var labelRow = new StackPanel { Orientation = Orientation.Horizontal };
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            labelRow.Children.Add(labelText);

            if (!string.IsNullOrEmpty(badge))
            {
                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(AccentColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badgeBorder.Child = new TextBlock
                {
                    Text = badge,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                labelRow.Children.Add(badgeBorder);
            }
            stack.Children.Add(labelRow);

            // Preview text
            var preview = new TextBlock
            {
                Text = "HVAC-AHU-0001",
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 14,
                Margin = new Thickness(0, 4, 0, 0),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            };
            if (underline)
            {
                preview.TextDecorations = TextDecorations.Underline;
            }
            stack.Children.Add(preview);

            card.Child = stack;

            var rb = new RadioButton
            {
                Content = card,
                GroupName = "HeadingStyle",
                IsChecked = isDefault,
                Margin = new Thickness(0),
                Foreground = new SolidColorBrush(FgColor),
                // Store style info in Tag
                Tag = new bool[] { bold, italic, underline }
            };
            rb.Checked += (s, e) => UpdatePreview();

            return rb;
        }

        private void UpdatePreview()
        {
            var (bold, italic, underline) = GetSelectedStyle();
            string styleName = BuildStyleName(bold, italic, underline);

            bool applyT2 = _cbTier2.IsChecked == true;
            bool applyT3 = _cbTier3.IsChecked == true;

            _previewTier2.Text = applyT2
                ? $"Tier 2 heading will be: {styleName}"
                : "Tier 2 heading: (unchanged)";
            _previewTier2.FontStyle = (applyT2 && italic) ? FontStyles.Italic : FontStyles.Normal;
            _previewTier2.FontWeight = (applyT2 && bold) ? FontWeights.Bold : FontWeights.Normal;
            _previewTier2.TextDecorations = (applyT2 && underline) ? TextDecorations.Underline : null;

            _previewTier3.Text = applyT3
                ? $"Tier 3 heading will be: {styleName}"
                : "Tier 3 heading: (unchanged)";
            _previewTier3.FontStyle = (applyT3 && italic) ? FontStyles.Italic : FontStyles.Normal;
            _previewTier3.FontWeight = (applyT3 && bold) ? FontWeights.Bold : FontWeights.Normal;
            _previewTier3.TextDecorations = (applyT3 && underline) ? TextDecorations.Underline : null;
        }

        private (bool bold, bool italic, bool underline) GetSelectedStyle()
        {
            RadioButton selected = _rbBoldUnderline;
            if (_rbUnderlineOnly.IsChecked == true) selected = _rbUnderlineOnly;
            else if (_rbBoldOnly.IsChecked == true) selected = _rbBoldOnly;
            else if (_rbAllStyles.IsChecked == true) selected = _rbAllStyles;

            var flags = (bool[])selected.Tag;
            return (flags[0], flags[1], flags[2]);
        }

        private static string BuildStyleName(bool bold, bool italic, bool underline)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (bold) parts.Add("bold");
            if (italic) parts.Add("italic");
            if (underline) parts.Add("underline");
            return parts.Count > 0 ? string.Join(" + ", parts) : "none";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var (bold, italic, underline) = GetSelectedStyle();
            _result = new HeadingStyleResult
            {
                Bold = bold,
                Italic = italic,
                Underline = underline,
                ApplyTier2 = _cbTier2.IsChecked == true,
                ApplyTier3 = _cbTier3.IsChecked == true,
                Cancelled = false
            };
            Close();
        }

        private static TextBlock MakeHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            };
        }

        private static TextBlock MakeLabel(string text, Color color)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        /// <summary>
        /// Show the heading style dialog and return the user's choices.
        /// </summary>
        /// <param name="currentTier2Style">Current tier 2 style description (e.g. "underline").</param>
        /// <param name="currentTier3Style">Current tier 3 style description (e.g. "bold + underline").</param>
        /// <returns>HeadingStyleResult with the user's selections, or Cancelled=true if dismissed.</returns>
        public static HeadingStyleResult Show(string currentTier2Style, string currentTier3Style)
        {
            var dlg = new HeadingStyleDialog(currentTier2Style, currentTier3Style);
            StingWindowHelper.ApplyOwner(dlg);
            dlg.ShowDialog();
            return dlg._result ?? new HeadingStyleResult { Cancelled = true };
        }
    }
}
