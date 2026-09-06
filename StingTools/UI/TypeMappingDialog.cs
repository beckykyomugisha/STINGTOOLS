// ══════════════════════════════════════════════════════════════════════════
//  TypeMappingDialog.cs — choose the commodity a model type belongs to, and
//  see what that does before it is written.
//
//  THE RENAME IS OFFERED FIRST, and not as politeness. Renaming the Revit
//  type to something the shipped patterns already match — "IT4 Corrugated
//  Sheet Roof 225" — fixes the cause: the name then means the same thing to
//  the schedule, the bill, the drawings and the next consultant, and no
//  project carries a private patch for it. A mapping fixes one project and
//  leaves the name wrong everywhere else.
//
//  The mapping exists because renaming is often impossible: a linked model, a
//  vendor family, a file somebody else owns. That is common enough that the
//  escape hatch must exist — but a dialog that offered only the escape hatch
//  would have every project accumulate patches instead of fixing anything
//  once.
//
//  Nothing is written until the preview has been seen. A mapping changes the
//  row's UNIT and quantity, and both roof commodities convert equally
//  cleanly, so "it worked" is not evidence it was right.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.Core.MaterialSchedule;

namespace StingTools.UI
{
    public sealed class TypeMappingDialog : Window
    {
        private readonly SupplierUnitTable _units;
        private readonly MaterialScheduleDocument _schedule;
        private readonly ComboBox _commodity;
        private readonly TextBox _pattern;
        private readonly TextBox _why;
        private readonly TextBox _preview;
        private readonly Button _apply;

        public SupplierUnitRule Chosen { get; private set; }
        public string ChosenPattern { get; private set; } = "";
        public string Why { get; private set; } = "";

        public TypeMappingDialog(string rowDescription, List<SupplierUnitRule> candidates,
                                 SupplierUnitTable units, MaterialScheduleDocument schedule)
        {
            _units = units;
            _schedule = schedule;

            Title = "STING — Map a type to a commodity";
            Width = 760; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(14) };

            // ── the rename, first ──
            var advice = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x80)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11.5,
                    Text = "Consider renaming the Revit type first. A type called "
                         + "\"IT4 Corrugated Sheet Roof 225\" matches the shipped patterns with no "
                         + "mapping at all, and the name then means the same thing to the schedule, "
                         + "the bill, the drawings and the next consultant.\n\n"
                         + "Map it here when you cannot rename it — a linked model, a vendor family, "
                         + "or a file somebody else owns. The mapping applies to THIS project only."
                }
            };
            DockPanel.SetDock(advice, Dock.Top);
            root.Children.Add(advice);

            // ── the choice ──
            var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddLabel(form, 0, "Commodity");
            _commodity = new ComboBox { Margin = new Thickness(0, 3, 0, 3), Height = 26 };
            foreach (var r in candidates)
                _commodity.Items.Add(new ComboBoxItem
                {
                    Content = $"{r.CommodityKey}  —  {r.Description}  ({r.SupplierUnit})",
                    Tag = r
                });
            _commodity.SelectionChanged += (a, b) => Refresh();
            Grid.SetRow(_commodity, 0); Grid.SetColumn(_commodity, 1);
            form.Children.Add(_commodity);

            AddLabel(form, 1, "Type pattern");
            _pattern = new TextBox
            {
                Text = rowDescription ?? "", Margin = new Thickness(0, 3, 0, 3), Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Matched as a SUBSTRING of the model type name. Longer is safer."
            };
            _pattern.TextChanged += (a, b) => Refresh();
            Grid.SetRow(_pattern, 1); Grid.SetColumn(_pattern, 1);
            form.Children.Add(_pattern);

            AddLabel(form, 2, "Why");
            _why = new TextBox
            {
                Margin = new Thickness(0, 3, 0, 3), Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Recorded in the file. The next reader needs to know why this project "
                        + "differs from the shipped table."
            };
            Grid.SetRow(_why, 2); Grid.SetColumn(_why, 1);
            form.Children.Add(_why);

            DockPanel.SetDock(form, Dock.Top);
            root.Children.Add(form);

            // ── buttons ──
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var cancel = new Button { Content = "Cancel", Width = 96, Height = 30, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += (a, b) => { DialogResult = false; Close(); };
            _apply = new Button
            {
                Content = "Apply mapping", Width = 140, Height = 30, Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = false
            };
            _apply.Click += (a, b) => Accept();
            buttons.Children.Add(cancel);
            buttons.Children.Add(_apply);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // ── the preview fills what is left ──
            _preview = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 11.5,
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA))
            };
            root.Children.Add(_preview);

            Content = root;
            if (_commodity.Items.Count > 0) _commodity.SelectedIndex = 0;
        }

        private static void AddLabel(Grid g, int row, string text)
        {
            var t = new TextBlock
            {
                Text = text, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 8, 3), FontSize = 11.5
            };
            Grid.SetRow(t, row); Grid.SetColumn(t, 0);
            g.Children.Add(t);
        }

        /// <summary>
        /// Re-plan on every keystroke. The preview is the whole point — Apply
        /// stays disabled until a plan says it can be applied.
        /// </summary>
        private void Refresh()
        {
            var rule = (_commodity.SelectedItem as ComboBoxItem)?.Tag as SupplierUnitRule;
            if (rule == null) { _preview.Text = ""; _apply.IsEnabled = false; return; }

            var plan = TypePatternPlanner.Plan(_units,
                (_schedule?.Stages ?? new List<StageSection>()).SelectMany(st => st.Commodities),
                rule.CommodityKey, _pattern.Text);

            _preview.Text = plan.Summary();
            _apply.IsEnabled = plan.CanApply;
        }

        private void Accept()
        {
            var rule = (_commodity.SelectedItem as ComboBoxItem)?.Tag as SupplierUnitRule;
            if (rule == null) return;

            Chosen = rule;
            ChosenPattern = (_pattern.Text ?? "").Trim();
            Why = (_why.Text ?? "").Trim();
            if (Why.Length == 0)
                Why = "no reason recorded";   // never blank: the file is read by somebody else later
            DialogResult = true;
            Close();
        }
    }
}
