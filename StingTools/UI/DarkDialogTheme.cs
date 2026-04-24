using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// Fixes white-on-white text in WPF dialogs that paint a custom dark
    /// Background and rely on an inherited white Foreground. Input controls
    /// (TextBox, ComboBox) do not inherit Window.Background — they render
    /// with default (light) chrome, which makes white text invisible.
    ///
    /// Two complementary entry points:
    ///
    /// 1. <see cref="ApplyComboBoxFix"/> — installs a window-level implicit
    ///    style for <see cref="ComboBoxItem"/>. This alone fixes the
    ///    dropdown rows but not the combo edit field or standalone
    ///    TextBoxes (WPF default templates bind those to
    ///    SystemColors.WindowBrushKey, which wins over implicit styles).
    ///
    /// 2. <see cref="StyleInput(TextBox)"/> / <see cref="StyleInput(ComboBox)"/>
    ///    — explicit per-control styling that sets Background / Foreground /
    ///    BorderBrush directly on the instance. This is the proven pattern
    ///    (see ShopDrawingOptionsDialog) and is the one to reach for when
    ///    the implicit-style approach does not land.
    ///
    /// Call ApplyComboBoxFix(...) once per Window and StyleInput(...) on
    /// every input control created procedurally; the two cover both the
    /// dropdown rows and the input body/edit field.
    /// </summary>
    internal static class DarkDialogTheme
    {
        // ─── Default palette (matches the dark dialogs already in the codebase) ───
        private static readonly Color DefaultBg     = Color.FromRgb(0x3E, 0x3E, 0x42);
        private static readonly Color DefaultFg     = Colors.White;
        private static readonly Color DefaultBorder = Color.FromRgb(0x55, 0x55, 0x58);

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
            TextElement.SetForeground(cb, fgBrush);

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
