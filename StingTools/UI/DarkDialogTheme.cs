using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// Palette constants + control-styling helpers for STING dialogs. The
    /// palette was formerly dark (white text on #2D2D30) but suffered from
    /// white-on-white and black-on-dark contrast failures whenever a WPF
    /// control fell back to system chrome. It was flipped to a LIGHT
    /// palette (dark text on near-white) so every default / system
    /// fallback is automatically readable. The orange STING accent is
    /// kept unchanged and still works as a primary-button fill.
    ///
    /// Entry points:
    ///
    /// 1. <see cref="ApplyComboBoxFix"/> — installs a window-level implicit
    ///    style for <see cref="ComboBoxItem"/> so dropdown rows adopt the
    ///    caller's bg/fg/hover instead of system defaults.
    ///
    /// 2. <see cref="StyleInput(TextBox)"/> / <see cref="StyleInput(ComboBox)"/>
    ///    — explicit per-control styling that sets Background / Foreground /
    ///    BorderBrush directly on the instance.
    ///
    /// Callers can consume the <see cref="LightPalette"/> constants below
    /// to stay in sync with the rest of the app.
    /// </summary>
    internal static class DarkDialogTheme
    {
        // ─── Light palette (contrast-safe defaults) ───
        // Previously: DefaultBg=#3E3E42 + DefaultFg=White + DefaultBorder=#555558.
        // That required every input to be hand-styled or text became
        // unreadable against WPF system defaults. The light palette below
        // matches the rest of the STING UI (ThemeManager themes are all
        // light) and degrades gracefully when a control falls back to
        // system chrome.
        private static readonly Color DefaultBg     = Color.FromRgb(0xFF, 0xFF, 0xFF); // white input bg
        private static readonly Color DefaultFg     = Color.FromRgb(0x22, 0x22, 0x22); // near-black text
        private static readonly Color DefaultBorder = Color.FromRgb(0xCF, 0xD8, 0xDC); // light grey border

        /// <summary>
        /// Shared light-theme constants for STING dialogs. Use these
        /// instead of hardcoding dark hex values so every dialog stays
        /// consistent and contrast-safe.
        /// </summary>
        public static class LightPalette
        {
            public static readonly Color WindowBg   = Color.FromRgb(0xFA, 0xFA, 0xFA); // off-white page
            public static readonly Color CardBg     = Color.FromRgb(0xFF, 0xFF, 0xFF); // input / card
            public static readonly Color AltRowBg   = Color.FromRgb(0xF0, 0xF0, 0xF0); // zebra row
            public static readonly Color Border     = Color.FromRgb(0xCF, 0xD8, 0xDC); // subtle border
            public static readonly Color BodyFg     = Color.FromRgb(0x22, 0x22, 0x22); // body text
            public static readonly Color SubtleFg   = Color.FromRgb(0x66, 0x66, 0x66); // muted text
            public static readonly Color Accent     = Color.FromRgb(0xE8, 0x91, 0x2D); // STING orange
            public static readonly Color AccentFg   = Color.FromRgb(0xFF, 0xFF, 0xFF); // on orange
            public static readonly Color SecondaryBtn = Color.FromRgb(0xE8, 0xE8, 0xE8); // neutral btn
        }

        // ═════════════════════════════════════════════════════════════════
        //  Window-level fix (dropdown rows)
        // ═════════════════════════════════════════════════════════════════

        public static void ApplyComboBoxFix(Window window, Color itemBg, Color itemFg, Color hoverBg)
            => ApplyDarkInputTheme(window, itemBg, itemFg, hoverBg);

        public static void ApplyDarkInputTheme(Window window, Color inputBg, Color inputFg, Color hoverBg)
        {
            if (window == null) return;

            var bg     = Freeze(new SolidColorBrush(inputBg));
            var fg     = Freeze(new SolidColorBrush(inputFg));
            var hover  = Freeze(new SolidColorBrush(hoverBg));

            // Install the ComboBoxItem style — this is the one piece of
            // implicit-style wiring that reliably paints (because the
            // ControlTemplate supplied below binds Background via
            // TemplateBinding, not via a system resource key).
            window.Resources[typeof(ComboBoxItem)] = BuildComboBoxItemStyle(bg, fg, hover);
        }

        // ═════════════════════════════════════════════════════════════════
        //  Explicit per-control styling (reliable path)
        // ═════════════════════════════════════════════════════════════════

        public static void StyleInput(TextBox tb)
            => StyleInput(tb, DefaultBg, DefaultFg, DefaultBorder);

        public static void StyleInput(TextBox tb, Color bg, Color fg, Color border)
        {
            if (tb == null) return;
            tb.Background       = Freeze(new SolidColorBrush(bg));
            tb.Foreground       = Freeze(new SolidColorBrush(fg));
            tb.BorderBrush      = Freeze(new SolidColorBrush(border));
            tb.CaretBrush       = tb.Foreground;
            tb.SelectionBrush   = Freeze(new SolidColorBrush(border));
            tb.BorderThickness  = new Thickness(1);
            if (tb.Padding.Left + tb.Padding.Right + tb.Padding.Top + tb.Padding.Bottom == 0)
                tb.Padding = new Thickness(4, 1, 4, 1);
        }

        public static void StyleInput(ComboBox cb)
            => StyleInput(cb, DefaultBg, DefaultFg, DefaultBorder);

        public static void StyleInput(ComboBox cb, Color bg, Color fg, Color border)
        {
            if (cb == null) return;
            var bgBrush     = Freeze(new SolidColorBrush(bg));
            var fgBrush     = Freeze(new SolidColorBrush(fg));
            var borderBrush = Freeze(new SolidColorBrush(border));

            cb.Background      = bgBrush;
            cb.Foreground      = fgBrush;
            cb.BorderBrush     = borderBrush;
            cb.BorderThickness = new Thickness(1);
            // TextElement lives in System.Windows.Documents; fully
            // qualify rather than add a whole using just for this
            // attached-property set.
            System.Windows.Documents.TextElement.SetForeground(cb, fgBrush);

            // WPF's default ComboBox template hosts a PART_EditableTextBox
            // whose Style reference wins over our Foreground. Walk the
            // visual tree once Loaded fires and style that TextBox
            // directly — same pattern as TextBox above.
            cb.Loaded += (s, e) =>
            {
                var part = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
                if (part != null)
                {
                    part.Background     = bgBrush;
                    part.Foreground     = fgBrush;
                    part.BorderBrush    = borderBrush;
                    part.CaretBrush     = fgBrush;
                    part.SelectionBrush = borderBrush;
                }
            };
        }

        // ═════════════════════════════════════════════════════════════════
        //  Internals
        // ═════════════════════════════════════════════════════════════════

        private static Style BuildComboBoxItemStyle(Brush bg, Brush fg, Brush hover)
        {
            // Minimal ControlTemplate where the Border's Background is
            // TemplateBound, so Style/trigger Background actually paints.
            var template = new ControlTemplate(typeof(ComboBoxItem));
            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty,
                new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            border.AppendChild(content);
            template.VisualTree = border;

            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hover));
            style.Triggers.Add(hoverTrigger);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, hover));
            style.Triggers.Add(selected);

            return style;
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
