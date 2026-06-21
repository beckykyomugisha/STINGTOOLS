// ============================================================================
// MepCadWizard.cs — Phase: MEP-from-DWG V2.
//
// Compact per-layer mapping dialog for MEP-from-DWG, modelled on the structural
// CAD wizard but focused: it lists the DWG layers that carry detected fixtures
// or runs and lets the user (a) choose the target level, (b) toggle host-snap,
// (c) include/exclude each layer, (d) override a run layer's kind, and (e) set a
// per-run-layer elevation offset. Family/run TYPE stays "first available in the
// project" in V2 — see docs/ROADMAP.md for the per-layer type matrix.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using StingTools.Core.Cad.Mep;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;

namespace StingTools.UI
{
    public class MepCadWizard : Window
    {
        private readonly MepDetectionResult _detection;
        private readonly ObservableCollection<MepLayerRow> _rows = new ObservableCollection<MepLayerRow>();
        private readonly List<Level> _levels;
        private ComboBox _levelCombo;
        private CheckBox _hostChk;

        public bool Confirmed { get; private set; }
        public Level SelectedLevel => (_levelCombo?.SelectedItem as LevelItem)?.Level;
        public bool HostSnap => _hostChk?.IsChecked == true;

        private static readonly string[] FixtureRoles = { "Auto", "Skip" };
        private static readonly string[] RunRoles = { "Auto", "Skip", "Duct", "Pipe", "Conduit", "CableTray" };

        public MepCadWizard(Document doc, MepDetectionResult detection)
        {
            _detection = detection ?? new MepDetectionResult();
            _levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();

            Title = "STING — MEP CAD → Model (per-layer mapping)";
            Width = 720; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            BuildRows();
            Content = BuildLayout(doc);
        }

        private void BuildRows()
        {
            foreach (var g in _detection.Fixtures.GroupBy(f => f.Block?.LayerName ?? "(unnamed)").OrderBy(g => g.Key))
            {
                string topCat = g.GroupBy(f => f.Category).OrderByDescending(x => x.Count()).First().Key;
                _rows.Add(new MepLayerRow { Layer = g.Key, Items = g.Count(), IsRun = false,
                    Auto = $"Fixture · {topCat}", Role = "Auto", Include = true });
            }
            foreach (var g in _detection.Runs.GroupBy(r => r.Line?.LayerName ?? "(unnamed)").OrderBy(g => g.Key))
            {
                var kind = g.GroupBy(r => r.Kind).OrderByDescending(x => x.Count()).First().Key;
                _rows.Add(new MepLayerRow { Layer = g.Key, Items = g.Count(), IsRun = true,
                    Auto = $"Run · {kind}", Role = "Auto", Include = true,
                    OffsetMm = MepRunClassifier.DefaultOffsetMm(kind).ToString("F0", CultureInfo.InvariantCulture) });
            }
        }

        private UIElement BuildLayout(Document doc)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header controls
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock { Text = "Level:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _levelCombo = new ComboBox { Width = 200, VerticalAlignment = VerticalAlignment.Center };
            foreach (var l in _levels) _levelCombo.Items.Add(new LevelItem { Level = l });
            if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null)
            {
                var match = _levelCombo.Items.Cast<LevelItem>().FirstOrDefault(li => li.Level.Id == vp.GenLevel.Id);
                _levelCombo.SelectedItem = match ?? _levelCombo.Items.Cast<object>().FirstOrDefault();
            }
            else if (_levelCombo.Items.Count > 0) _levelCombo.SelectedIndex = 0;
            header.Children.Add(_levelCombo);

            _hostChk = new CheckBox { Content = "Host-snap fixtures (wall/ceiling)", IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            header.Children.Add(_hostChk);
            Grid.SetRow(header, 0); root.Children.Add(header);

            // Grid of layers
            var grid = new DataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Layer", Binding = new Binding("Layer") { Mode = BindingMode.OneWay }, Width = 240, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "Items", Binding = new Binding("Items") { Mode = BindingMode.OneWay }, Width = 60, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "Auto-detected", Binding = new Binding("Auto") { Mode = BindingMode.OneWay }, Width = 170, IsReadOnly = true });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Include", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 60 });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "Role", SelectedItemBinding = new Binding("Role") { Mode = BindingMode.TwoWay }, Width = 100, ItemsSource = RunRoles });
            grid.Columns.Add(new DataGridTextColumn { Header = "Offset mm", Binding = new Binding("OffsetMm") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 80 });
            Grid.SetRow(grid, 1); root.Children.Add(grid);

            // Footer buttons
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Place", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Click += (s, e) => { Confirmed = true; DialogResult = true; Close(); };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            footer.Children.Add(ok); footer.Children.Add(cancel);
            Grid.SetRow(footer, 2); root.Children.Add(footer);

            return root;
        }

        /// <summary>Apply the per-layer choices to the detection, returning a filtered copy
        /// (excluded/Skip layers dropped; run-kind + offset overrides applied).</summary>
        public MepDetectionResult ApplyTo(MepDetectionResult detection)
        {
            var fixtureRows = _rows.Where(r => !r.IsRun).ToDictionary(r => r.Layer, r => r, StringComparer.OrdinalIgnoreCase);
            var runRows = _rows.Where(r => r.IsRun).ToDictionary(r => r.Layer, r => r, StringComparer.OrdinalIgnoreCase);

            var outR = new MepDetectionResult
            {
                LayerCounts = detection.LayerCounts,
                TotalEntities = detection.TotalEntities,
                TotalBlocks = detection.TotalBlocks,
                TotalLines = detection.TotalLines,
            };

            foreach (var fx in detection.Fixtures)
            {
                string layer = fx.Block?.LayerName ?? "(unnamed)";
                if (fixtureRows.TryGetValue(layer, out var row) && (!row.Include || row.Role == "Skip")) continue;
                outR.Fixtures.Add(fx);
            }

            foreach (var run in detection.Runs)
            {
                string layer = run.Line?.LayerName ?? "(unnamed)";
                if (runRows.TryGetValue(layer, out var row))
                {
                    if (!row.Include || row.Role == "Skip") continue;
                    if (row.Role != "Auto" && Enum.TryParse<MepRunKind>(row.Role, out var k)) run.Kind = k;
                    if (double.TryParse(row.OffsetMm, NumberStyles.Any, CultureInfo.InvariantCulture, out double off) && off >= 0)
                        run.OffsetMm = off;
                }
                outR.Runs.Add(run);
            }

            // Risers aren't in the per-layer grid — carry them through unchanged.
            foreach (var r in detection.Risers) outR.Risers.Add(r);
            return outR;
        }

        private class LevelItem { public Level Level; public override string ToString() => Level?.Name ?? "(level)"; }
    }

    public class MepLayerRow : INotifyPropertyChanged
    {
        public string Layer { get; set; } = "";
        public int Items { get; set; }
        public string Auto { get; set; } = "";
        public bool IsRun { get; set; }

        private bool _include = true;
        public bool Include { get => _include; set { _include = value; OnChanged(nameof(Include)); } }

        private string _role = "Auto";
        public string Role { get => _role; set { _role = value; OnChanged(nameof(Role)); } }

        private string _offsetMm = "";
        public string OffsetMm { get => _offsetMm; set { _offsetMm = value; OnChanged(nameof(OffsetMm)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
