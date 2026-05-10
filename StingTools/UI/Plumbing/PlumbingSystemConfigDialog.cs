// PlumbingSystemConfigDialog — Phase 179a SYSTEM tab.
//
// Modeless WPF dialog presented by the Plumb_SaveSystemConfig command.
// Presents the foundation choices (building type, K factor, standards,
// pipe materials, velocity limits, slope rules) and persists them via
// PlumbingSystemConfig.Save.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Plumbing;

namespace StingTools.UI.Plumbing
{
    public class PlumbingSystemConfigDialog : Window
    {
        private readonly PlumbingSystemConfig _cfg;
        private readonly Document _doc;
        private TextBox _occupancy;
        private TextBox _beds;
        private TextBox _kFactor;
        private TextBox _supplyPres;
        private ComboBox _bldgType;
        private ComboBox _drainStd;
        private ComboBox _supplyStd;
        private CheckBox _flushValve;
        private Dictionary<string, ComboBox> _materialPickers = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox>  _velocityBoxes  = new Dictionary<string, TextBox>();
        private Dictionary<string, TextBox>  _slopeBoxes     = new Dictionary<string, TextBox>();
        public bool Saved { get; private set; }
        public PlumbingSystemConfig Result => _cfg;

        public PlumbingSystemConfigDialog(Document doc, PlumbingSystemConfig cfg)
        {
            _doc = doc;
            _cfg = cfg ?? PlumbingSystemConfig.Defaults();
            Title = "STING Plumbing — System Configuration";
            Width = 720; Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Content = BuildBody();
        }

        private FrameworkElement BuildBody()
        {
            var root = new DockPanel { LastChildFill = true };

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            DockPanel.SetDock(btnRow, Dock.Bottom);
            var save = new Button { Content = "Save System Config", MinWidth = 160, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4, 0, 4, 0) };
            var cancel = new Button { Content = "Cancel", MinWidth = 90, Padding = new Thickness(10, 6, 10, 6) };
            save.Click   += (s, e) => SaveAndClose();
            cancel.Click += (s, e) => Close();
            btnRow.Children.Add(save); btnRow.Children.Add(cancel);
            root.Children.Add(btnRow);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel { Margin = new Thickness(12) };

            sp.Children.Add(Section("Project & Standards"));
            var grid1 = NewGrid();
            AddRow(grid1, 0, "Building type", _bldgType = NewCombo(new[] { "Dwelling", "Office", "Hospital", "School", "Hotel", "Restaurant", "Factory", "Sports", "PublicWC", "Custom" }, _cfg.BuildingType));
            _bldgType.SelectionChanged += (s, e) =>
            {
                if (_bldgType.SelectedItem is string bt)
                {
                    var def = PlumbingSystemConfig.DefaultsForBuildingType(bt);
                    _kFactor.Text = def.KFactor.ToString("F2");
                }
            };
            AddRow(grid1, 1, "Drainage standard", _drainStd = NewCombo(new[] { "BS-EN-12056", "IPC-2021", "MANUAL" }, _cfg.DrainStandard));
            AddRow(grid1, 2, "Supply standard",   _supplyStd = NewCombo(new[] { "BS-EN-806", "HUNTER-WSFU", "MANUAL" }, _cfg.SupplyStandard));
            AddRow(grid1, 3, "K factor (frequency)", _kFactor = NewTextBox(_cfg.KFactor.ToString("F2")));
            AddRow(grid1, 4, "Flush-valve majority WCs", _flushValve = NewCheck(_cfg.FlushValveMajority));
            AddRow(grid1, 5, "Occupancy",         _occupancy = NewTextBox(_cfg.OccupancyCount.ToString()));
            AddRow(grid1, 6, "Beds / Workstations", _beds = NewTextBox(_cfg.BedsOrWorkstations.ToString()));
            sp.Children.Add(grid1);

            sp.Children.Add(Section("Pipe Materials"));
            var matKeys = new[] { "DCW", "DHW", "Drainage", "Storm", "Vent" };
            var matOptions = PlumbingTables.Materials?.Select(m => m.Key).ToArray() ??
                             new[] { "COPPER_R250", "UPVC_DRAIN", "MDPE_BLUE", "PEX_AL_PEX" };
            var grid2 = NewGrid();
            for (int i = 0; i < matKeys.Length; i++)
            {
                var key = matKeys[i];
                var current = _cfg.Materials != null && _cfg.Materials.TryGetValue(key, out var m) ? m : matOptions.FirstOrDefault();
                var combo = NewCombo(matOptions, current);
                _materialPickers[key] = combo;
                AddRow(grid2, i, key, combo);
            }
            sp.Children.Add(grid2);

            sp.Children.Add(Section("Velocity Limits (m/s)"));
            var velKeys = new[] { "DCW_Max", "DHW_Max", "Drain_SelfCleansing", "Drain_Max" };
            var grid3 = NewGrid();
            for (int i = 0; i < velKeys.Length; i++)
            {
                var k = velKeys[i];
                var v = (_cfg.VelocityMps != null && _cfg.VelocityMps.TryGetValue(k, out var vv)) ? vv : 2.0;
                var tb = NewTextBox(v.ToString("F2"));
                _velocityBoxes[k] = tb;
                AddRow(grid3, i, k.Replace('_', ' '), tb);
            }
            sp.Children.Add(grid3);

            sp.Children.Add(Section("Slope (%)"));
            var slopeKeys = new[] { "DN32_50", "DN75_100", "DN150", "Target" };
            var grid4 = NewGrid();
            for (int i = 0; i < slopeKeys.Length; i++)
            {
                var k = slopeKeys[i];
                var v = (_cfg.SlopePctMin != null && _cfg.SlopePctMin.TryGetValue(k, out var vv)) ? vv : 1.0;
                var tb = NewTextBox(v.ToString("F2"));
                _slopeBoxes[k] = tb;
                AddRow(grid4, i, k.Replace('_', '–'), tb);
            }
            sp.Children.Add(grid4);

            sp.Children.Add(Section("Supply Pressure"));
            var grid5 = NewGrid();
            AddRow(grid5, 0, "Supply pressure at entry (bar)", _supplyPres = NewTextBox(_cfg.SupplyPressureBarAtEntry.ToString("F2")));
            sp.Children.Add(grid5);

            sv.Content = sp;
            root.Children.Add(sv);
            return root;
        }

        private void SaveAndClose()
        {
            try
            {
                _cfg.BuildingType  = (_bldgType.SelectedItem  as string) ?? _cfg.BuildingType;
                _cfg.DrainStandard = (_drainStd.SelectedItem  as string) ?? _cfg.DrainStandard;
                _cfg.SupplyStandard= (_supplyStd.SelectedItem as string) ?? _cfg.SupplyStandard;
                _cfg.FlushValveMajority = _flushValve.IsChecked == true;
                if (double.TryParse(_kFactor.Text,    out var k))  _cfg.KFactor = k;
                if (int.TryParse(_occupancy.Text,     out var oc)) _cfg.OccupancyCount = oc;
                if (int.TryParse(_beds.Text,          out var bd)) _cfg.BedsOrWorkstations = bd;
                if (double.TryParse(_supplyPres.Text, out var sp)) _cfg.SupplyPressureBarAtEntry = sp;
                _cfg.Materials = _materialPickers.ToDictionary(kv => kv.Key, kv => (kv.Value.SelectedItem as string) ?? "");
                _cfg.VelocityMps = _velocityBoxes.ToDictionary(kv => kv.Key,
                    kv => double.TryParse(kv.Value.Text, out var d) ? d : 0);
                _cfg.SlopePctMin = _slopeBoxes.ToDictionary(kv => kv.Key,
                    kv => double.TryParse(kv.Value.Text, out var d) ? d : 0);

                using (var tx = new Transaction(_doc, "STING Plumbing — Save System Config"))
                {
                    tx.Start();
                    PlumbingSystemConfig.Save(_doc, _cfg);
                    tx.Commit();
                }
                Saved = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save plumbing system config: " + ex.Message, "STING Plumbing");
            }
        }

        // ── helpers ──
        private static TextBlock Section(string label) => new TextBlock
        {
            Text = "── " + label.ToUpperInvariant() + " ──",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 64, 96)),
            Margin = new Thickness(0, 12, 0, 6)
        };

        private static System.Windows.Controls.Grid NewGrid()
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(2, 0, 2, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private static void AddRow(System.Windows.Controls.Grid g, int row, string label, FrameworkElement control)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 6, 8, 4), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetColumn(lbl, 0); System.Windows.Controls.Grid.SetRow(lbl, row);
            g.Children.Add(lbl);
            System.Windows.Controls.Grid.SetColumn(control, 1); System.Windows.Controls.Grid.SetRow(control, row);
            control.Margin = new Thickness(0, 4, 0, 4);
            g.Children.Add(control);
        }

        private static ComboBox NewCombo(IEnumerable<string> items, string selected)
        {
            var c = new ComboBox();
            foreach (var i in items) c.Items.Add(i);
            if (!string.IsNullOrEmpty(selected) && c.Items.Contains(selected)) c.SelectedItem = selected;
            else if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        private static TextBox NewTextBox(string value) => new TextBox
        {
            Text = value ?? "",
            Padding = new Thickness(4, 2, 4, 2)
        };

        private static CheckBox NewCheck(bool isChecked) => new CheckBox
        {
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
