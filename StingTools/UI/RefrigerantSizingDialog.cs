// StingTools — Refrigerant sizing input dialog.
//
// Minimal WPF dialog for the inputs that RefrigerantPipeSolver needs:
// refrigerant id, leg type, capacity, equivalent length, vertical lift,
// vertical-riser flag. Keeps the form small (one screen, no scrolling)
// — for a richer experience the LOADS / CALCS tab can host the same
// inputs as a permanent grid in a future phase.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.Core.Refrigerant;

namespace StingTools.UI
{
    public class RefrigerantSizingDialog : Window
    {
        public RefrigerantSizingInput Result { get; private set; }

        private readonly ComboBox _cmbRefrig;
        private readonly ComboBox _cmbLeg;
        private readonly TextBox  _txtCapacity;
        private readonly TextBox  _txtEquivLen;
        private readonly TextBox  _txtLift;
        private readonly CheckBox _chkRiser;
        private readonly TextBox  _txtDpBudget;
        private readonly ComboBox _cmbVendorSeries;
        private readonly TextBox  _txtActualLen;
        private readonly TextBox  _txtTotalLen;

        public RefrigerantSizingDialog(string initialRefrigerant = "R410A")
        {
            Title = "STING HVAC — Refrigerant Pipe Sizing";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(248, 246, 252));

            var grid = new Grid { Margin = new Thickness(16) };
            for (int i = 0; i < 12; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;

            // Title strip
            var title = new TextBlock
            {
                Text = "Refrigerant pipe sizing",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(title, row); Grid.SetColumnSpan(title, 2); grid.Children.Add(title);
            row++;

            AddLabel(grid, "Refrigerant", row);
            _cmbRefrig = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var kv in RefrigerantProperties.All) _cmbRefrig.Items.Add(kv.Key);
            _cmbRefrig.SelectedItem = RefrigerantProperties.All.ContainsKey(initialRefrigerant)
                ? initialRefrigerant : "R410A";
            Grid.SetRow(_cmbRefrig, row); Grid.SetColumn(_cmbRefrig, 1); grid.Children.Add(_cmbRefrig);
            row++;

            AddLabel(grid, "Leg", row);
            _cmbLeg = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            _cmbLeg.Items.Add("Suction");
            _cmbLeg.Items.Add("Discharge");
            _cmbLeg.Items.Add("Liquid");
            _cmbLeg.Items.Add("HotGasReturn");
            _cmbLeg.SelectedIndex = 0;
            Grid.SetRow(_cmbLeg, row); Grid.SetColumn(_cmbLeg, 1); grid.Children.Add(_cmbLeg);
            row++;

            AddLabel(grid, "Capacity (kW)", row);
            _txtCapacity = new TextBox { Text = "20", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtCapacity, row); Grid.SetColumn(_txtCapacity, 1); grid.Children.Add(_txtCapacity);
            row++;

            AddLabel(grid, "Equivalent length (m)", row);
            _txtEquivLen = new TextBox { Text = "60", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtEquivLen, row); Grid.SetColumn(_txtEquivLen, 1); grid.Children.Add(_txtEquivLen);
            row++;

            AddLabel(grid, "Vertical lift (m, + = ODU above IDU)", row);
            _txtLift = new TextBox { Text = "5", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtLift, row); Grid.SetColumn(_txtLift, 1); grid.Children.Add(_txtLift);
            row++;

            AddLabel(grid, "ΔP budget (kPa)", row);
            _txtDpBudget = new TextBox { Text = "30", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtDpBudget, row); Grid.SetColumn(_txtDpBudget, 1); grid.Children.Add(_txtDpBudget);
            row++;

            // Vendor envelope (Phase 187f). Optional — leave at "(none)" to
            // rely on the generic refrigerant envelope only.
            AddLabel(grid, "Vendor series (optional)", row);
            _cmbVendorSeries = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            _cmbVendorSeries.Items.Add("(none — generic envelope)");
            foreach (var kv in StingTools.Core.Refrigerant.RefrigerantVendorRegistry.Get(null).ById)
                _cmbVendorSeries.Items.Add(kv.Key);
            _cmbVendorSeries.SelectedIndex = 0;
            Grid.SetRow(_cmbVendorSeries, row); Grid.SetColumn(_cmbVendorSeries, 1); grid.Children.Add(_cmbVendorSeries);
            row++;

            AddLabel(grid, "Actual one-way length (m)", row);
            _txtActualLen = new TextBox { Text = "0", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtActualLen, row); Grid.SetColumn(_txtActualLen, 1); grid.Children.Add(_txtActualLen);
            row++;

            AddLabel(grid, "Total system length (m)", row);
            _txtTotalLen = new TextBox { Text = "0", Margin = new Thickness(0, 2, 0, 6) };
            Grid.SetRow(_txtTotalLen, row); Grid.SetColumn(_txtTotalLen, 1); grid.Children.Add(_txtTotalLen);
            row++;

            _chkRiser = new CheckBox
            {
                Content = "Includes vertical riser (apply oil-return min velocity)",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(_chkRiser, row); Grid.SetColumnSpan(_chkRiser, 2); grid.Children.Add(_chkRiser);
            row++;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var btnOk = new Button { Content = "Size", Width = 90, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 4, 6, 4) };
            var btnCancel = new Button { Content = "Cancel", Width = 90, Padding = new Thickness(6, 4, 6, 4) };
            btnOk.Click += (s, e) =>
            {
                if (TryBuild(out var ip)) { Result = ip; DialogResult = true; Close(); }
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(btnOk);
            buttons.Children.Add(btnCancel);
            Grid.SetRow(buttons, row); Grid.SetColumnSpan(buttons, 2); grid.Children.Add(buttons);

            Content = grid;
        }

        private bool TryBuild(out RefrigerantSizingInput input)
        {
            input = new RefrigerantSizingInput();
            try
            {
                input.RefrigerantId = _cmbRefrig.SelectedItem?.ToString() ?? "R410A";
                if (!Enum.TryParse(_cmbLeg.SelectedItem?.ToString() ?? "Suction", out RefrigerantLeg leg))
                    leg = RefrigerantLeg.Suction;
                input.Leg = leg;
                input.CapacityKw          = double.Parse(_txtCapacity.Text);
                input.EquivLengthM        = double.Parse(_txtEquivLen.Text);
                input.LiftM               = double.Parse(_txtLift.Text);
                input.MaxPressureDropKpa  = double.Parse(_txtDpBudget.Text);
                input.HasVerticalRiser    = _chkRiser.IsChecked == true;

                // Vendor envelope (optional). Index 0 = "(none)", so anything
                // else is a real series id from the registry.
                if (_cmbVendorSeries?.SelectedIndex > 0)
                    input.VendorSeriesId = _cmbVendorSeries.SelectedItem?.ToString();
                if (double.TryParse(_txtActualLen?.Text, out var aLen) && aLen > 0)
                    input.ActualOnewayLengthM = aLen;
                if (double.TryParse(_txtTotalLen?.Text, out var tLen) && tLen > 0)
                    input.TotalSystemLengthM = tLen;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid input: {ex.Message}", "STING HVAC", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private static void AddLabel(Grid g, string text, int row)
        {
            var t = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 8, 6)
            };
            Grid.SetRow(t, row); Grid.SetColumn(t, 0); g.Children.Add(t);
        }
    }
}
