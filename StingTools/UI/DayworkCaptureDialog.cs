// ══════════════════════════════════════════════════════════════════════════
//  DayworkCaptureDialog.cs — capture an instructed daywork sheet. PM-3.
//
//  Mirrors StarRateBuilderDialog (labour / plant / materials resource grids with
//  a live roll-up), differing where dayworks differ: the header carries the CA
//  instruction reference + date the sheet was instructed under, and the roll-up
//  is NRM2 percentage additions per section rather than overhead-then-profit.
//
//  Returns a DayworkRecord or null on cancel.
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
    public class DayworkCaptureDialog : Window
    {
        private readonly ObservableCollection<StarRateLine> _labour = new ObservableCollection<StarRateLine>();
        private readonly ObservableCollection<StarRateLine> _plant = new ObservableCollection<StarRateLine>();
        private readonly ObservableCollection<StarRateLine> _materials = new ObservableCollection<StarRateLine>();
        private readonly TextBox _instrRef, _instrDate, _desc, _labPct, _matPct, _plantPct;
        private readonly TextBlock _totals;

        public DayworkRecord Result { get; private set; }

        /// <summary>Project currency (default UGX). Caller may set before ShowDialog.</summary>
        public string CurrencyCode { get; set; } = "UGX";

        /// <summary>Seed percentages — caller passes the project's tendered
        /// daywork additions so the dialog opens on the contract figures.</summary>
        public DayworkBuildUp Defaults { get; set; } = new DayworkBuildUp();

        public DayworkCaptureDialog()
        {
            Title = "STING — Daywork Sheet";
            Width = 780; Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var root = new DockPanel { Margin = new Thickness(12) };

            // ── Header: instruction ref / date / description ──────────
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock
            {
                Text = "Instructed dayworks — record of resources expended",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 92))
            });
            header.Children.Add(new TextBlock
            {
                Text = "Recorded against the Contract Administrator's instruction (Clause 5.7). "
                     + "Priced at the tendered daywork percentages at final account.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.Children.Add(Label("Instruction ref", 0));
            _instrRef = Text("", 1); grid.Children.Add(_instrRef);
            grid.Children.Add(Label("Date", 2));
            _instrDate = Text(DateTime.Today.ToString("yyyy-MM-dd"), 3); grid.Children.Add(_instrDate);
            header.Children.Add(grid);

            var descRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            descRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            descRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            descRow.Children.Add(Label("Description", 0));
            _desc = Text("", 1); descRow.Children.Add(_desc);
            header.Children.Add(descRow);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer: percentage additions + totals + buttons ───────
            var footer = new StackPanel();
            DockPanel.SetDock(footer, Dock.Bottom);

            var pctRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            pctRow.Children.Add(new TextBlock
            {
                Text = "% additions —", VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0)
            });
            _labPct = PctBox(pctRow, "Labour", Defaults?.LabourAdditionPct ?? 115);
            _matPct = PctBox(pctRow, "Materials", Defaults?.MaterialsAdditionPct ?? 110);
            _plantPct = PctBox(pctRow, "Plant", Defaults?.PlantAdditionPct ?? 112);
            var recalc = new Button
            {
                Content = "↻ Recalculate", Padding = new Thickness(8, 2, 8, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            recalc.Click += (s, e) => UpdateTotals();
            pctRow.Children.Add(recalc);
            footer.Children.Add(pctRow);

            _totals = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
            footer.Children.Add(_totals);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button
            {
                Content = "Save daywork sheet", Width = 150, Height = 28,
                Margin = new Thickness(0, 0, 8, 0), IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(26, 58, 92)), Foreground = Brushes.White
            };
            ok.Click += (s, e) => OnOk();
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
            btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
            footer.Children.Add(btnRow);
            root.Children.Add(footer);

            // ── Resource grids ───────────────────────────────────────
            var center = new StackPanel();
            center.Children.Add(Section("Labour (hours × rate/hr)", _labour, isMaterials: false));
            center.Children.Add(Section("Plant (hours × rate/hr)", _plant, isMaterials: false));
            center.Children.Add(Section("Materials (qty × rate/unit, at net cost)", _materials, isMaterials: true));
            root.Children.Add(new ScrollViewer { Content = center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            _labour.Add(new StarRateLine { Resource = "General labourer (unskilled)", Hours = 0, UnitRate = 0, Unit = "hr" });
            _plant.Add(new StarRateLine { Resource = "", Hours = 0, UnitRate = 0, Unit = "hr" });
            _materials.Add(new StarRateLine { Resource = "", Quantity = 0, UnitRate = 0, Unit = "each" });

            Content = root;
            UpdateTotals();
        }

        private TextBox PctBox(Panel host, string label, double seed)
        {
            host.Children.Add(new TextBlock
            {
                Text = label + ":", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var tb = new TextBox
            {
                Text = seed.ToString("0.##", CultureInfo.InvariantCulture),
                Width = 60, Margin = new Thickness(0, 0, 14, 0)
            };
            host.Children.Add(tb);
            return tb;
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
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = "Resource",
                Binding = new System.Windows.Data.Binding("Resource"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = isMaterials ? "Qty" : "Hours",
                Binding = new System.Windows.Data.Binding(isMaterials ? "Quantity" : "Hours"),
                Width = 70
            });
            dg.Columns.Add(new DataGridTextColumn { Header = "Unit rate", Binding = new System.Windows.Data.Binding("UnitRate"), Width = 80 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Unit", Binding = new System.Windows.Data.Binding("Unit"), Width = 60 });
            dg.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateTotals),
                System.Windows.Threading.DispatcherPriority.Background);
            sp.Children.Add(dg);
            return sp;
        }

        private DayworkRecord Compose()
        {
            DateTime.TryParse((_instrDate.Text ?? "").Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime d);
            if (d == default) d = DateTime.Today;

            return new DayworkRecord
            {
                InstructionRef = (_instrRef.Text ?? "").Trim(),
                InstructionDate = d,
                Description = string.IsNullOrWhiteSpace(_desc.Text) ? "Instructed dayworks" : _desc.Text.Trim(),
                Currency = string.IsNullOrEmpty(CurrencyCode) ? "UGX" : CurrencyCode,
                RecordedBy = Environment.UserName ?? "",
                Status = DayworkStatus.Recorded,
                LabourLines = _labour.Where(NonEmpty).ToList(),
                PlantLines = _plant.Where(NonEmpty).ToList(),
                MaterialsLines = _materials.Where(NonEmpty).ToList(),
                BuildUp = new DayworkBuildUp
                {
                    LabourAdditionPct = ParseD(_labPct.Text, 115),
                    MaterialsAdditionPct = ParseD(_matPct.Text, 110),
                    PlantAdditionPct = ParseD(_plantPct.Text, 112)
                }
            };
        }

        private static bool NonEmpty(StarRateLine l)
            => l != null && (!string.IsNullOrWhiteSpace(l.Resource) || l.UnitRate > 0 || l.Hours > 0 || l.Quantity > 0);

        private void UpdateTotals()
        {
            var d = Compose();
            string cc = d.Currency;
            _totals.Text =
                $"Net prime cost — labour {d.LabourNet:N2} + plant {d.PlantNet:N2} + materials {d.MaterialsNet:N2} "
                + $"= {d.NetTotal:N2}\n"
                + $"+ additions — labour {d.LabourAddition:N2} + plant {d.PlantAddition:N2} "
                + $"+ materials {d.MaterialsAddition:N2} = {d.AdditionTotal:N2}\n"
                + $"=  SHEET VALUE {cc} {d.GrossTotal:N2}";

            var warn = d.BuildUp?.Warnings() ?? new System.Collections.Generic.List<string>();
            if (warn.Count > 0) _totals.Text += "\n⚠ " + string.Join("  ", warn);
        }

        private void OnOk()
        {
            var d = Compose();
            if (!d.HasResources)
            {
                MessageBox.Show("Add at least one labour / plant / material line with a rate.",
                    "Daywork sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(d.InstructionRef))
            {
                // A daywork sheet with no instruction reference is unverifiable at
                // final account — warn, but let the QS proceed (site records often
                // precede the written CI).
                if (MessageBox.Show(
                        "No instruction reference recorded. Dayworks without a CA instruction "
                        + "reference are hard to substantiate at final account.\n\nSave anyway?",
                        "Daywork sheet", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
            Result = d;
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
