using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// Fixes white-on-white text in ComboBox / TextBox dropdowns on dialogs
    /// that use a custom dark Background + inherited white Foreground. WPF
    /// popups render with default (light) chrome and do not inherit
    /// Window.Background, so items in the dropdown become unreadable.
    ///
    /// Installs implicit Window-level styles for <see cref="ComboBoxItem"/>
    /// (with a minimal ControlTemplate that actually honours Background) so
    /// dropdown rows paint dark and keep white text readable.
    /// </summary>
    internal static class DarkDialogTheme
    {
        public static void ApplyComboBoxFix(Window window, Color itemBg, Color itemFg, Color hoverBg)
        {
            if (window == null) return;
            var style = BuildComboBoxItemStyle(itemBg, itemFg, hoverBg);
            window.Resources[typeof(ComboBoxItem)] = style;
        }

        private static Style BuildComboBoxItemStyle(Color itemBg, Color itemFg, Color hoverBg)
        {
            var itemBrush  = Freeze(new SolidColorBrush(itemBg));
            var textBrush  = Freeze(new SolidColorBrush(itemFg));
            var hoverBrush = Freeze(new SolidColorBrush(hoverBg));

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
            style.Setters.Add(new Setter(Control.BackgroundProperty, itemBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBrush));
            style.Triggers.Add(hover);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, hoverBrush));
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
