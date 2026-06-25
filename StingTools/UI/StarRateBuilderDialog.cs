// ══════════════════════════════════════════════════════════════════════════
//  StarRateBuilderDialog.cs — P4.2 — interactive first-principles star rate.
//
//  Replaces the canned demo build-up in VariationBuildStarRateCommand. The QS
//  enters labour / plant / material lines + overhead% + profit%; the dialog
//  shows the live build-up (Labour + Plant + Materials → Subtotal → +OH → +Profit
//  → Final rate). Returns a StarRate or null on cancel.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.Core.Variation;

namespace StingTools.UI
{
    public class StarRateBuilderDialog : Window
    {
        private readonly ObservableCollection<StarRateLine> _labour = new ObservableCollection<StarRateLine>();
        private readonly ObservableCollection<StarRateLine> _plant = new ObservableCollection<StarRateLine>();
        private readonly ObservableCollection<StarRateLine> _materials = new ObservableCollection<StarRateLine>();
        private readonly TextBox _desc, _unit, _oh, _profit;
        private readonly TextBlock _totals;

        public StarRate Result { get; private set; }

        /// <summary>Project currency for the build-up (default UGX). Caller may
        /// set it before ShowDialog to match the active project.</summary>
        public string CurrencyCode { get; set; } = "UGX";

        public StarRateBuilderDialog()
        {
            Title = "STING — Star Rate Build-Up";
            Width = 760; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var root = new DockPanel { Margin = new Thickness(12) };

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock { Text = "First-principles rate build-up",
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 92)) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.Children.Add(Label("Description", 0));
            _desc = Text("New rate", 1); grid.Children.Add(_desc);
            grid.Children.Add(Label("Unit", 2));
            _unit = Text("each", 3); grid.Children.Add(_unit);
            header.Children.Add(grid);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer (totals + buttons) docks bottom.
            var footer = new StackPanel { };
            DockPanel.SetDock(footer, Dock.Bottom);
            var ohRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            ohRow.Children.Add(new TextBlock { Text = "Overhead %:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _oh = new TextBox { Text = "8", Width = 60, Margin = new Thickness(0, 0, 16, 0) };
            ohRow.Children.Add(_oh);
            ohRow.Children.Add(new TextBlock { Text = "Profit %:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _profit = new TextBox { Text = "5", Width = 60, Margin = new Thickness(0, 0, 16, 0) };
            ohRow.Children.Add(_profit);
            var recalc = new Button { Content = "↻ Recalculate", Padding = new Thickness(8, 2, 8, 2), Cursor = System.Windows.Input.Cursors.Hand };
            recalc.Click += (s, e) => UpdateTotals();
            ohRow.Children.Add(recalc);
            footer.Children.Add(ohRow);

            _totals = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
            footer.Children.Add(_totals);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Save star rate", Width = 130, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(26, 58, 92)), Foreground = Brushes.White };
            ok.Click += (s, e) => OnOk();
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
            btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
            footer.Children.Add(btnRow);
            root.Children.Add(footer);

            // Three grids stack in the centre.
            var center = new StackPanel();
            center.Children.Add(Section("Labour (hours × £/hr)", _labour, isMaterials: false));
            center.Children.Add(Section("Plant (hours × £/hr)", _plant, isMaterials: false));
            center.Children.Add(Section("Materials (qty × £/unit)", _materials, isMaterials: true));
            var scroll = new ScrollViewer { Content = center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);

            // Seed one empty line each so the grids are immediately editable.
            _labour.Add(new StarRateLine { Resource = "Skilled labourer", Hours = 0, UnitRate = 0, Unit = "hr" });
            _plant.Add(new StarRateLine { Resource = "", Hours = 0, UnitRate = 0, Unit = "hr" });
            _materials.Add(new StarRateLine { Resource = "", Quantity = 0, UnitRate = 0, Unit = "each" });

            Content = root;
            UpdateTotals();
        }

        private UIElement Section(string title, ObservableCollection<StarRateLine> src, bool isMaterials)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            var dg = new DataGrid
            {
                ItemsSource = src, AutoGenerateColumns = false, CanUserAddRows = true,
                HeadersVisibility = DataGridHeadersVisibility.Column, FontSize = 12, MinHeight = 70,
                RowHeaderWidth = 0
            };
            dg.Columns.Add(new DataGridTextColumn { Header = "Resource", Binding = new System.Windows.Data.Binding("Resource"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = isMaterials ? "Qty" : "Hours",
                Binding = new System.Windows.Data.Binding(isMaterials ? "Quantity" : "Hours"), Width = 70 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Unit rate", Binding = new System.Windows.Data.Binding("UnitRate"), Width = 80 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Unit", Binding = new System.Windows.Data.Binding("Unit"), Width = 60 });
            dg.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateTotals),
                System.Windows.Threading.DispatcherPriority.Background);
            sp.Children.Add(dg);
            return sp;
        }

        private StarRate Compose()
        {
            double oh = ParseD(_oh.Text, 8), pr = ParseD(_profit.Text, 5);
            return new StarRate
            {
                Description = string.IsNullOrWhiteSpace(_desc.Text) ? "Star rate" : _desc.Text.Trim(),
                Unit = string.IsNullOrWhiteSpace(_unit.Text) ? "each" : _unit.Text.Trim(),
                Currency = string.IsNullOrEmpty(CurrencyCode) ? "UGX" : CurrencyCode,
                OverheadPercent = oh, ProfitPercent = pr,
                Author = Environment.UserName ?? "",
                LabourLines = _labour.Where(NonEmpty).ToList(),
                PlantLines = _plant.Where(NonEmpty).ToList(),
                MaterialsLines = _materials.Where(NonEmpty).ToList()
            };
        }

        private static bool NonEmpty(StarRateLine l)
            => l != null && (!string.IsNullOrWhiteSpace(l.Resource) || l.UnitRate > 0 || l.Hours > 0 || l.Quantity > 0);

        private void UpdateTotals()
        {
            var sr = Compose();
            _totals.Text =
                $"Labour {sr.LabourTotal:N2}  +  Plant {sr.PlantTotal:N2}  +  Materials {sr.MaterialsTotal:N2}  " +
                $"=  Subtotal {sr.Subtotal:N2}\n" +
                $"+ Overhead ({sr.OverheadPercent:0.##}%) {sr.OverheadAmount:N2}  " +
                $"+ Profit ({sr.ProfitPercent:0.##}%) {sr.ProfitAmount:N2}  " +
                $"=  FINAL RATE {sr.Currency} {sr.FinalRate:N2} / {sr.Unit}";
        }

        private void OnOk()
        {
            var sr = Compose();
            if (sr.Subtotal <= 0)
            {
                MessageBox.Show("Add at least one labour / plant / material line with a rate.", "Star rate",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = sr;
            DialogResult = true;
            Close();
        }

        private static double ParseD(string s, double dflt)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : dflt;

        private static TextBlock Label(string t, int col)
        {
            var tb = new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(tb, col); return tb;
        }
        private static TextBox Text(string t, int col)
        {
            var tb = new TextBox { Text = t, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, col); return tb;
        }
    }
}
