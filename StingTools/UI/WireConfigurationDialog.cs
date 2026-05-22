// StingTools — Wire Configuration dialog.
//
// Single screen for editing the wire profile assigned to every
// circuit in the project. Mirrors ricaun's WireInConduit Circuit
// Configuration: lists every ElectricalSystem with its panel,
// circuit number, load, current rating, and lets the user pick a
// WireProfile from the catalogue per row. Saving writes both
//   <project>/_BIM_COORD/circuit_wire_map.json
// and (best-effort) the chosen profile's CSA/cores/material onto
// the circuit's conduits via STING's ELC_WIRE_* shared parameters.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Grid = System.Windows.Controls.Grid;
using Ellipse = System.Windows.Shapes.Ellipse;
using Color = System.Windows.Media.Color;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.UI
{
    public class WireConfigurationDialog : Window
    {
        private class Row
        {
            public ElectricalSystem System { get; set; }
            public string Panel { get; set; }
            public string Circuit { get; set; }
            public string Load { get; set; }
            public string Rating { get; set; }
            public string Phase { get; set; }
            public string ProfileId { get; set; }
            public ComboBox Combo { get; set; }
            public Ellipse Chip { get; set; }
        }

        private readonly Document _doc;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<WireProfile> _profiles;
        private readonly TextBlock _statusText;

        public static void ShowFor(Document doc)
        {
            try
            {
                var dlg = new WireConfigurationDialog(doc);
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Error("WireConfigurationDialog.ShowFor", ex);
                MessageBoxStub("Wire Configuration failed to open: " + ex.Message);
            }
        }

        private static void MessageBoxStub(string s)
        {
            try { Autodesk.Revit.UI.TaskDialog.Show("STING", s); }
            catch { }
        }

        private WireConfigurationDialog(Document doc)
        {
            _doc = doc;
            Title  = "STING — Wire Configuration";
            Width  = 980;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 248));

            _profiles = WireProfileRegistry.ListAll(doc);

            var root = new DockPanel { Margin = new Thickness(10) };

            var header = new TextBlock
            {
                Text = "Pick a cable profile per circuit. Saving writes _BIM_COORD/circuit_wire_map.json plus ELC_WIRE_* parameters on the circuit's conduits.",
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(bar, Dock.Top);

            var btnReload = new Button { Content = "Reload catalogue", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
            btnReload.Click += (_, __) =>
            {
                WireProfileRegistry.Reload();
                Autodesk.Revit.UI.TaskDialog.Show("STING", "Wire profile catalogue reloaded from disk.");
            };
            bar.Children.Add(btnReload);

            var btnSave = new Button { Content = "Save & push to model", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)), Foreground = Brushes.White };
            btnSave.Click += (_, __) => OnSave();
            bar.Children.Add(btnSave);

            var btnClose = new Button { Content = "Close", Padding = new Thickness(14, 4, 14, 4) };
            btnClose.Click += (_, __) => Close();
            bar.Children.Add(btnClose);

            root.Children.Add(bar);

            _statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                Margin = new Thickness(0, 0, 0, 6),
            };
            DockPanel.SetDock(_statusText, Dock.Bottom);
            root.Children.Add(_statusText);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                BorderThickness = new Thickness(1),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // status chip
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });  // panel
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // circuit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });  // load
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });   // rating
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });   // phase
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });  // wire profile

            AddHeaderRow(grid);
            BuildRows(grid);
            scroll.Content = grid;
            root.Children.Add(scroll);

            Content = root;
            UpdateStatus();
        }

        private void AddHeaderRow(Grid grid)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] hdr = { "", "Panel", "Circuit", "Load", "Rating", "Phase", "Wire profile" };
            for (int c = 0; c < hdr.Length; c++)
            {
                var tb = new TextBlock
                {
                    Text = hdr[c],
                    Margin = new Thickness(8, 6, 8, 6),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
                };
                Grid.SetRow(tb, 0);
                Grid.SetColumn(tb, c);
                grid.Children.Add(tb);
            }
        }

        private void BuildRows(Grid grid)
        {
            var systems = new FilteredElementCollector(_doc)
                .OfClass(typeof(ElectricalSystem))
                .WhereElementIsNotElementType()
                .Cast<ElectricalSystem>()
                .OrderBy(s => s.PanelName ?? "")
                .ThenBy(s => s.Name ?? "")
                .ToList();

            var map = CircuitWireMap.Load(_doc);

            int rowIndex = 1;
            foreach (var sys in systems)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var row = new Row
                {
                    System  = sys,
                    Panel   = sys.PanelName ?? "",
                    Circuit = sys.Name ?? "",
                    Load    = sys.LoadName ?? "",
                    Rating  = SafeRating(sys),
                    Phase   = SafePhase(sys),
                };
                row.ProfileId = map.GetProfileId(sys.Name ?? "") ?? "";

                var chip = new Ellipse
                {
                    Width = 12, Height = 12,
                    Margin = new Thickness(8, 8, 8, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "",
                };
                row.Chip = chip;
                Grid.SetRow(chip, rowIndex);
                Grid.SetColumn(chip, 0);
                grid.Children.Add(chip);

                AddTextCell(grid, rowIndex, 1, row.Panel);
                AddTextCell(grid, rowIndex, 2, row.Circuit);
                AddTextCell(grid, rowIndex, 3, row.Load);
                AddTextCell(grid, rowIndex, 4, row.Rating);
                AddTextCell(grid, rowIndex, 5, row.Phase);

                var combo = new ComboBox
                {
                    Margin = new Thickness(8, 4, 8, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                combo.Items.Add(new ComboBoxItem { Content = "(auto from circuit)", Tag = "" });
                foreach (var p in _profiles)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = $"{p.Id} — {p.Name}",
                        Tag = p.Id,
                    });
                }
                int sel = 0;
                if (!string.IsNullOrEmpty(row.ProfileId))
                {
                    for (int i = 1; i < combo.Items.Count; i++)
                    {
                        if (combo.Items[i] is ComboBoxItem cbi && (cbi.Tag as string) == row.ProfileId)
                        { sel = i; break; }
                    }
                }
                combo.SelectedIndex = sel;
                var rowRef = row;
                combo.SelectionChanged += (_, __) => { UpdateChip(rowRef); UpdateStatus(); };
                row.Combo = combo;
                UpdateChip(row);

                Grid.SetRow(combo, rowIndex);
                Grid.SetColumn(combo, 6);
                grid.Children.Add(combo);

                _rows.Add(row);
                rowIndex++;
            }

            if (_rows.Count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var empty = new TextBlock
                {
                    Text = "No electrical circuits found in this project.",
                    Margin = new Thickness(12, 12, 12, 12),
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 165)),
                };
                Grid.SetRow(empty, 1);
                Grid.SetColumn(empty, 0);
                Grid.SetColumnSpan(empty, 7);
                grid.Children.Add(empty);
            }
        }

        private void AddTextCell(Grid grid, int row, int col, string text)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(8, 6, 8, 6),
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 50)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private static readonly SolidColorBrush ChipMapped   = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // green
        private static readonly SolidColorBrush ChipAuto     = new SolidColorBrush(Color.FromRgb(189, 189, 199)); // grey

        private void UpdateChip(Row row)
        {
            if (row?.Chip == null) return;
            string id = (row.Combo?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!string.IsNullOrEmpty(id))
            {
                row.Chip.Fill = ChipMapped;
                row.Chip.ToolTip = $"Mapped to profile: {id}";
            }
            else
            {
                row.Chip.Fill = ChipAuto;
                row.Chip.ToolTip = "Auto — derived from circuit's Wire Size / poles at run time.";
            }
        }

        private void UpdateStatus()
        {
            int assigned = _rows.Count(r =>
                r.Combo?.SelectedItem is ComboBoxItem cbi &&
                !string.IsNullOrEmpty(cbi.Tag as string));
            _statusText.Text = $"{assigned} of {_rows.Count} circuits have an explicit wire profile assigned " +
                               $"({_profiles.Count} profiles in catalogue).";
        }

        private void OnSave()
        {
            try
            {
                var map = new CircuitWireMap();
                foreach (var row in _rows)
                {
                    string id = (row.Combo?.SelectedItem as ComboBoxItem)?.Tag as string;
                    if (!string.IsNullOrEmpty(id))
                        map.Set(row.System?.Name ?? row.Circuit, id);
                }
                map.Save(_doc);

                int pushed = PushToModel(map);
                Autodesk.Revit.UI.TaskDialog.Show(
                    "STING — Wire Configuration",
                    $"Saved circuit→profile map with {map.Assignments.Count} entries.\n" +
                    $"Pushed wire spec onto {pushed} conduits via ELC_WIRE_* parameters.\n\n" +
                    "Run 'Refresh All Wire Annotations' to rebuild any existing annotations from the new mapping.");
                Close();
            }
            catch (Exception ex)
            {
                StingLog.Error("WireConfigurationDialog.OnSave", ex);
                Autodesk.Revit.UI.TaskDialog.Show("STING", "Save failed: " + ex.Message);
            }
        }

        private int PushToModel(CircuitWireMap map)
        {
            int touched = 0;
            try
            {
                using (var tg = new TransactionGroup(_doc, "STING Push Wire Profiles"))
                {
                    tg.Start();
                    foreach (var assn in map.Assignments)
                    {
                        var profile = WireProfileRegistry.Get(_doc, assn.ProfileId);
                        if (profile == null) continue;
                        var sys = _rows.FirstOrDefault(r =>
                            string.Equals(r.System?.Name, assn.CircuitId, StringComparison.Ordinal))?.System;
                        if (sys == null) continue;

                        var route = CircuitWalker.Walk(_doc, sys);
                        using (var t = new Transaction(_doc, "STING Stamp Profile"))
                        {
                            t.Start();
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            foreach (var seg in route.AllSegments)
                            {
                                bool any = false;
                                any |= ParameterHelpers.SetString(seg, "ELC_WIRE_CORE_COUNT_INT", profile.Cores.ToString(inv), overwrite: true);
                                any |= ParameterHelpers.SetString(seg, "ELC_WIRE_CSA_MM2_NUM",   profile.CsaMm2.ToString("0.##", inv), overwrite: true);
                                any |= ParameterHelpers.SetString(seg, "ELC_WIRE_COND_MAT_TXT",  profile.ConductorMat ?? "", overwrite: true);
                                any |= ParameterHelpers.SetString(seg, "ELC_WIRE_PROFILE_ID_TXT", profile.Id, overwrite: true);
                                any |= ParameterHelpers.SetString(seg, "ELC_CIRCUIT_NR_TXT",     sys.Name ?? "", overwrite: true);
                                any |= ParameterHelpers.SetString(seg, "ELC_PNL_NAME_TXT",       sys.PanelName ?? "", overwrite: true);
                                if (any) touched++;
                            }
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }
            }
            catch (Exception ex) { StingLog.Warn("PushToModel: " + ex.Message); }
            return touched;
        }

        private static string SafeRating(ElectricalSystem sys)
        {
            try
            {
                var p = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble().ToString("0") + " A";
            }
            catch { }
            return "—";
        }

        private static string SafePhase(ElectricalSystem sys)
        {
            try { return sys.LookupParameter("Phase")?.AsValueString() ?? ""; }
            catch { return ""; }
        }
    }
}
