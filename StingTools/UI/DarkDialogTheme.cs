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
    /// (TextBox, ComboBox, ComboBoxItem) do not inherit Window.Background —
    /// they render with default (light) chrome, which makes white text in a
    /// dark dialog invisible.
    ///
    /// Installs implicit Window-level styles for TextBox, ComboBox and
    /// ComboBoxItem (with a minimal ControlTemplate that honours Background
    /// on rows) so standalone text entry fields, combo dropdowns and the
    /// internal PART_EditableTextBox of editable ComboBoxes all paint dark
    /// and keep their text readable.
    ///
    /// Callers that already set Background / Foreground explicitly on a
    /// control are unaffected: a local value beats an implicit style.
    /// </summary>
    internal static class DarkDialogTheme
    {
        /// <summary>
        /// Backwards-compat entry point — delegates to
        /// <see cref="ApplyDarkInputTheme"/> so every existing caller picks
        /// up the extended TextBox / ComboBox coverage without source edits.
        /// </summary>
        public static void ApplyComboBoxFix(Window window, Color itemBg, Color itemFg, Color hoverBg)
            => ApplyDarkInputTheme(window, itemBg, itemFg, hoverBg);

        /// <summary>
        /// Installs dark-dialog implicit styles for TextBox, ComboBox and
        /// ComboBoxItem on the given window.
        /// </summary>
        /// <param name="inputBg">Control body background (e.g. panel card grey).</param>
        /// <param name="inputFg">Control text foreground (typically white).</param>
        /// <param name="hoverBg">Border / hovered / selected-row accent.</param>
        public static void ApplyDarkInputTheme(Window window, Color inputBg, Color inputFg, Color hoverBg)
        {
            if (window == null) return;

            var inputBrush  = Freeze(new SolidColorBrush(inputBg));
            var textBrush   = Freeze(new SolidColorBrush(inputFg));
            var borderBrush = Freeze(new SolidColorBrush(hoverBg));

            window.Resources[typeof(ComboBoxItem)] = BuildComboBoxItemStyle(inputBrush, textBrush, borderBrush);
            window.Resources[typeof(TextBox)]      = BuildTextBoxStyle(inputBrush, textBrush, borderBrush);
            window.Resources[typeof(ComboBox)]     = BuildComboBoxStyle(inputBrush, textBrush, borderBrush);
        }

        // ── TextBox ────────────────────────────────────────────────────────
        // Also inherited by the internal PART_EditableTextBox of an editable
        // ComboBox, because WPF's default ComboBoxTextBox template binds
        // Background to the TextBox's own Background property.
        private static Style BuildTextBoxStyle(Brush bg, Brush fg, Brush border)
        {
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 1, 4, 1)));
            style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, fg));
            style.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, border));
            return style;
        }

        // ── ComboBox ──────────────────────────────────────────────────────
        // Setters only — no ControlTemplate, so keyboard nav / popup
        // behaviour stays standard. The combo's internal editable textbox
        // inherits from the TextBox implicit style above.
        private static Style BuildComboBoxStyle(Brush bg, Brush fg, Brush border)
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            // TextElement.Foreground propagates to the selection box
            // TextBlock that shows the currently selected item when the
            // combo is not editable.
            style.Setters.Add(new Setter(TextElement.ForegroundProperty, fg));
            return style;
        }

        // ── ComboBoxItem (dropdown rows) ──────────────────────────────────
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
