// StingTools — Scope Box Planner (modeless)
//
//   1 Drawing types   tick what the project needs; each row shows the largest box it accepts
//   2 Where           whole model / each STING-LOC building / each level; overlap, padding, fit, grid
//   3 Seeds           the sizes available to copy, and the sizes still to draw
//   4 Plan            every box that will exist, its size, seed and status — recomputed on every tick
//   Colour            pick a mode and the boxes recolour at once
//
// Everything that reads the model (footprints, seeds, levels, grid angle) is read
// once, in the API context the command opened the window from, and cached. Planning
// is Revit-free, so the preview is recomputed on the UI thread the moment anything
// changes. Every WRITE goes through one ExternalEvent, because a modeless window
// may not touch the document itself.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using Grid = System.Windows.Controls.Grid;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    public sealed class ScopeBoxPlannerDialog : Window
    {
        private sealed class PlanRow
        {
            public string Name { get; set; }
            public string Class { get; set; }
            public string Size { get; set; }
            public string Seed { get; set; }
            public string Status { get; set; }
            public Brush Swatch { get; set; }
        }

        private UIApplication _app;
        private Document _doc;
        private ScopeBoxPlannerContext _ctx;
        // Cached footprints, read once in API context.
        private ScopeBoxFootprint _modelFootprint;
        private List<ScopeBoxFootprint> _buildingFootprints = new List<ScopeBoxFootprint>();
        private readonly Dictionary<long, ScopeBoxFootprint> _levelFootprints = new Dictionary<long, ScopeBoxFootprint>();
        private readonly List<string> _footprintWarnings = new List<string>();

        private ScopeBoxPlanRequest _request;
        private ScopeBoxPlanResult _result;

        private readonly Dictionary<string, CheckBox> _typeChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, CheckBox> _levelChecks = new Dictionary<long, CheckBox>();
        private StackPanel _typePanel, _levelPanel;
        private TextBox _filter, _overlap, _padding, _fit;
        private RadioButton _rbModel, _rbBuildings, _rbLevels;
        private CheckBox _alignGrid;
        private TextBlock _seedText, _summary, _status, _problems;
        private DataGrid _grid;
        private ComboBox _colourMode, _colourScope;
        private bool _loading;

        private readonly ExternalEvent _event;
        private readonly Handler _handler;
        private Func<UIApplication, string> _pending;
        private string _pendingTitle;
        private bool _reloadAfter;

        public ScopeBoxPlannerDialog(UIApplication app)
        {
            _app = app; _doc = app.ActiveUIDocument.Document;
            Title = "STING Scope Box Planner";
            Width = 1320; Height = 780; MinWidth = 1000; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _handler = new Handler(this);
            try { _event = ExternalEvent.Create(_handler); }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxPlanner ExternalEvent: {ex.Message}"); }

            LoadModel();           // API context: we are inside the command's Execute.
            Content = Build();
            RestoreFromSavedPlan();
            Replan();
        }

        // ── model reads (API context only) ───────────────────────────────

        private void LoadModel()
        {
            _ctx = ScopeBoxPlannerService.Load(_doc);
            _footprintWarnings.Clear();
            _modelFootprint = ScopeBoxRevit.ModelFootprint(_doc, null);
            _buildingFootprints = ScopeBoxRevit.BuildingFootprints(_doc, _footprintWarnings);
            _levelFootprints.Clear();
            foreach (var l in _ctx.Levels) _levelFootprints[l.Id.Value] = ScopeBoxRevit.ModelFootprint(_doc, l);
        }

        // ── layout ───────────────────────────────────────────────────────

        private UIElement Build()
        {
            var root = new DockPanel { Margin = new Thickness(10) };

            var head = new StackPanel();
            head.Children.Add(new TextBlock
            {
                Text = "Tick the plan drawing types the project needs. Boxes are sized to fit every ticked type's sheet, "
                     + "copied from seed boxes (the Revit API cannot create or resize a scope box), named STING-AREA::…, "
                     + "and shared by every type that fits — not one box per type.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
            });
            _problems = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x5A, 0x00)), TextWrapping = TextWrapping.Wrap };
            head.Children.Add(_problems);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Btn("Create boxes", "Copy a seed for each New box, turn it square to the grid, name it, and save the plan.", OnCreate, true));
            buttons.Children.Add(Btn("Produce views…", "Produce views (and sheets) for every area box from the saved plan.", OnProduce));
            buttons.Children.Add(Btn("Close", null, (s, e) => Close()));
            DockPanel.SetDock(buttons, Dock.Right);
            foot.Children.Add(buttons);
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            foot.Children.Add(_status);
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c0 = TypesColumn(); Grid.SetColumn(c0, 0); cols.Children.Add(c0);
            var c1 = WhereColumn(); Grid.SetColumn(c1, 1); cols.Children.Add(c1);
            var c2 = PlanColumn();  Grid.SetColumn(c2, 2); cols.Children.Add(c2);
            root.Children.Add(cols);
            return root;
        }

        private static Button Btn(string text, string tip, RoutedEventHandler click, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
            if (primary) b.FontWeight = FontWeights.SemiBold;
            b.Click += click;
            return b;
        }

        private static TextBlock Header(string t) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };

        private UIElement TypesColumn()
        {
            var dp = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
            var top = new StackPanel();
            top.Children.Add(Header($"1  Drawing types ({_ctx.Candidates.Count} plan types crop by scope box)"));
            _filter = new TextBox { Margin = new Thickness(0, 0, 0, 4), ToolTip = "Filter by id or discipline" };
            _filter.TextChanged += (s, e) => ApplyFilter();
            top.Children.Add(_filter);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(Btn("All shown", null, (s, e) => SetTypes(true)));
            row.Children.Add(Btn("None", null, (s, e) => SetTypes(false)));
            top.Children.Add(row);
            DockPanel.SetDock(top, Dock.Top);
            dp.Children.Add(top);

            _typePanel = new StackPanel();
            foreach (var grp in _ctx.Candidates.GroupBy(t => string.IsNullOrWhiteSpace(t.Discipline) ? "*" : t.Discipline))
            {
                var exp = new Expander { Header = $"{DisciplineName(grp.Key)} ({grp.Count()})", IsExpanded = true, Tag = grp.Key };
                var list = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                foreach (var t in grp)
                {
                    string size = ScopeBoxSizing.TryMaxExtent(t, _ctx.Drawables, ScopeBoxSizing.DefaultFitFactor, out var w, out var d, out var why)
                        ? $"≤ {ScopeBoxNames.Metres(w)} × {ScopeBoxNames.Metres(d)} m" : why;
                    var cb = new CheckBox
                    {
                        Content = $"{t.Id}   1:{t.Scale}   {size}", Tag = t.Id, Margin = new Thickness(0, 1, 0, 1),
                        ToolTip = $"{t.Name}\n{t.PaperSize} {t.Orientation}, size class {ScopeBoxSizing.SizeClassKey(t)}",
                    };
                    cb.Checked += (s, e) => Replan(); cb.Unchecked += (s, e) => Replan();
                    _typeChecks[t.Id] = cb;
                    list.Children.Add(cb);
                }
                exp.Content = list;
                _typePanel.Children.Add(exp);
            }
            dp.Children.Add(new ScrollViewer { Content = _typePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return dp;
        }

        private static string DisciplineName(string code)
        {
            switch ((code ?? "").ToUpperInvariant())
            {
                case "A": return "A — Architecture"; case "S": return "S — Structure"; case "M": return "M — Mechanical";
                case "E": return "E — Electrical"; case "P": return "P — Public health"; case "FP": return "FP — Fire";
                case "H": return "H — Healthcare"; case "MG": return "MG — Medical gas"; case "RP": return "RP — Radiation";
                case "*": return "Multi-discipline";
                default: return code;
            }
        }

        private UIElement WhereColumn()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sp.Children.Add(Header("2  Where"));
            _rbModel = new RadioButton { Content = "Whole model — one set of boxes", GroupName = "fp", IsChecked = true };
            _rbBuildings = new RadioButton { Content = $"Each building (STING-LOC:: boxes: {_ctx.BuildingBoxCount})", GroupName = "fp", IsEnabled = _ctx.BuildingBoxCount > 0 };
            _rbLevels = new RadioButton { Content = "Each ticked level (level goes in the name)", GroupName = "fp" };
            foreach (var rb in new[] { _rbModel, _rbBuildings, _rbLevels }) { rb.Checked += (s, e) => Replan(); rb.Margin = new Thickness(0, 1, 0, 1); sp.Children.Add(rb); }

            sp.Children.Add(new TextBlock { Text = "Levels (to produce on; to plan, in per-level mode)", Margin = new Thickness(0, 8, 0, 2) });
            _levelPanel = new StackPanel();
            foreach (var l in _ctx.Levels)
            {
                var cb = new CheckBox { Content = $"{l.Name}  ({ParameterHelpers.GetLevelCodeForLevel(l)})", Tag = l.Id.Value, IsChecked = true };
                cb.Checked += (s, e) => Replan(); cb.Unchecked += (s, e) => Replan();
                _levelChecks[l.Id.Value] = cb;
                _levelPanel.Children.Add(cb);
            }
            sp.Children.Add(new ScrollViewer { Content = _levelPanel, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            _overlap = Num(g, 0, "Overlap between boxes (m)", _ctx.Style.Values.OverlapM);
            _padding = Num(g, 1, "Clearance round building (m)", _ctx.Style.Values.PaddingM);
            _fit = Num(g, 2, "Crop share of slot (%)", _ctx.Style.Values.FitFactor * 100);
            sp.Children.Add(g);
            double deg = _ctx.GridAngleRad * 180 / Math.PI;
            _alignGrid = new CheckBox { Content = $"Square to the grids ({deg.ToString("0.#", CultureInfo.InvariantCulture)}°)", IsChecked = Math.Abs(deg) > 0.05, Margin = new Thickness(0, 6, 0, 0) };
            _alignGrid.Checked += (s, e) => Replan(); _alignGrid.Unchecked += (s, e) => Replan();
            sp.Children.Add(_alignGrid);

            sp.Children.Add(Header("3  Seeds"));
            _seedText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(_seedText);
            var sb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            sb.Children.Add(Btn("Register selected", "Rename the scope boxes selected in Revit to STING-SEED::<w>x<d> from their measured size.",
                (s, e) => Raise("Register seeds", a => ScopeBoxPlannerService.RegisterSeeds(a.ActiveUIDocument.Document, a.ActiveUIDocument.Selection.GetElementIds()), reload: true)));
            sb.Children.Add(Btn("Import from file…", "Copy STING-SEED:: boxes from another project or template.", OnImport));
            sp.Children.Add(sb);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private TextBox Num(Grid g, int row, string label, double value)
        {
            g.RowDefinitions.Add(new RowDefinition());
            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lbl, row); g.Children.Add(lbl);
            var tb = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), Margin = new Thickness(0, 1, 0, 1) };
            tb.TextChanged += (s, e) => Replan();
            Grid.SetRow(tb, row); Grid.SetColumn(tb, 1); g.Children.Add(tb);
            return tb;
        }

        private UIElement PlanColumn()
        {
            var dp = new DockPanel();
            var top = new StackPanel();
            top.Children.Add(Header("4  Plan"));
            _summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            top.Children.Add(_summary);
            DockPanel.SetDock(top, Dock.Top);
            dp.Children.Add(top);

            var colour = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            colour.Children.Add(new TextBlock { Text = "Colour boxes by ", VerticalAlignment = VerticalAlignment.Center });
            _colourMode = new ComboBox { Width = 130, ItemsSource = Enum.GetNames(typeof(ScopeBoxColourMode)), SelectedIndex = 0 };
            colour.Children.Add(_colourMode);
            colour.Children.Add(new TextBlock { Text = "  in  ", VerticalAlignment = VerticalAlignment.Center });
            _colourScope = new ComboBox { Width = 130, ItemsSource = new[] { "Active view", "All plan views" }, SelectedIndex = 0 };
            colour.Children.Add(_colourScope);
            colour.Children.Add(new TextBlock
            {
                Text = "  applies at once; Off clears", VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray,
            });
            _colourMode.SelectionChanged += (s, e) => OnColour();
            _colourScope.SelectionChanged += (s, e) => { if (_colourMode.SelectedIndex > 0) OnColour(); };
            DockPanel.SetDock(colour, Dock.Bottom);
            dp.Children.Add(colour);

            _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column };
            var swatch = new FrameworkElementFactory(typeof(Border));
            swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(PlanRow.Swatch)));
            swatch.SetValue(Border.WidthProperty, 14.0); swatch.SetValue(Border.HeightProperty, 14.0);
            _grid.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 24, CellTemplate = new DataTemplate { VisualTree = swatch } });
            foreach (var (h, p, w) in new[] { ("Name", nameof(PlanRow.Name), 280.0), ("Class", nameof(PlanRow.Class), 70.0),
                                             ("Size (m)", nameof(PlanRow.Size), 90.0), ("Seed", nameof(PlanRow.Seed), 140.0), ("Status", nameof(PlanRow.Status), 120.0) })
                _grid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = w });
            dp.Children.Add(_grid);
            return dp;
        }

        // ── state ────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            var f = (_filter.Text ?? "").Trim();
            foreach (Expander exp in _typePanel.Children)
            {
                int shown = 0;
                foreach (CheckBox cb in ((StackPanel)exp.Content).Children)
                {
                    bool show = f.Length == 0 || ((string)cb.Tag).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                || ((string)exp.Tag).Equals(f, StringComparison.OrdinalIgnoreCase);
                    cb.Visibility = show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                    if (show) shown++;
                }
                exp.Visibility = shown > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        private void SetTypes(bool on)
        {
            _loading = true;
            foreach (var cb in _typeChecks.Values.Where(c => c.Visibility == System.Windows.Visibility.Visible)) cb.IsChecked = on;
            _loading = false;
            Replan();
        }

        /// <summary>Open with last time's choices: its types, levels and settings.</summary>
        private void RestoreFromSavedPlan()
        {
            var p = _ctx.SavedPlan;
            if (p == null) return;
            _loading = true;
            foreach (var id in p.Classes.SelectMany(c => c.DrawingTypes))
                if (_typeChecks.TryGetValue(id, out var cb)) cb.IsChecked = true;
            if (p.Levels.Count > 0)
                foreach (var l in _ctx.Levels)
                    _levelChecks[l.Id.Value].IsChecked = p.Levels.Contains(ParameterHelpers.GetLevelCodeForLevel(l), StringComparer.OrdinalIgnoreCase);
            _overlap.Text = p.OverlapM.ToString("0.##", CultureInfo.InvariantCulture);
            _padding.Text = p.PaddingM.ToString("0.##", CultureInfo.InvariantCulture);
            _fit.Text = (p.FitFactor * 100).ToString("0.##", CultureInfo.InvariantCulture);
            if (p.FootprintMode == nameof(ScopeBoxFootprintMode.Buildings) && _rbBuildings.IsEnabled) _rbBuildings.IsChecked = true;
            else if (p.FootprintMode == nameof(ScopeBoxFootprintMode.ModelPerLevel)) _rbLevels.IsChecked = true;
            _loading = false;
        }

        private ScopeBoxPlannerOptions Options(out string error)
        {
            string first = null;
            bool Parse(TextBox tb, string what, double min, double max, out double v)
            {
                bool ok = double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
                tb.BorderBrush = ok ? SystemColors.ControlDarkBrush : Brushes.Red;
                if (!ok && first == null) first = $"{what} must be a number from {min} to {max}.";
                return ok;
            }
            Parse(_overlap, "Overlap", 0, 50, out var overlap);
            Parse(_padding, "Clearance", 0, 50, out var padding);
            Parse(_fit, "Crop share", 10, 100, out var fit);
            error = first;
            return new ScopeBoxPlannerOptions
            {
                DrawingTypeIds = _typeChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
                LevelIds = _levelChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
                FootprintMode = _rbBuildings.IsChecked == true ? ScopeBoxFootprintMode.Buildings
                              : _rbLevels.IsChecked == true ? ScopeBoxFootprintMode.ModelPerLevel : ScopeBoxFootprintMode.Model,
                OverlapM = overlap, PaddingM = padding, FitFactor = fit / 100.0, AlignToGrid = _alignGrid.IsChecked == true,
            };
        }

        /// <summary>Recompute the plan from cached footprints. Revit-free, so safe on the UI thread.</summary>
        private void Replan()
        {
            if (_loading || _grid == null) return;
            var o = Options(out var error);
            var problems = _ctx.Problems.Concat(_footprintWarnings).ToList();
            _problems.Text = problems.Count > 0 ? "⚠ " + string.Join("\n⚠ ", problems) : "";
            if (error != null) { _summary.Text = error; _grid.ItemsSource = null; _result = null; return; }

            _request = new ScopeBoxPlanRequest
            {
                DrawingTypes = _ctx.Candidates.Where(t => o.DrawingTypeIds.Contains(t.Id)).ToList(),
                Drawables = _ctx.Drawables, Seeds = _ctx.Seeds, ExistingNames = _ctx.ExistingNames,
                FitFactor = o.FitFactor, OverlapM = o.OverlapM, PaddingM = o.PaddingM,
                GridAngleRad = o.AlignToGrid ? _ctx.GridAngleRad : 0,
            };
            switch (o.FootprintMode)
            {
                case ScopeBoxFootprintMode.Buildings: _request.Footprints = _buildingFootprints.ToList(); break;
                case ScopeBoxFootprintMode.ModelPerLevel:
                    _request.Footprints = o.LevelIds.Where(_levelFootprints.ContainsKey).Select(id => _levelFootprints[id]).ToList(); break;
                default: _request.Footprints = new List<ScopeBoxFootprint> { _modelFootprint }; break;
            }
            _result = ScopeBoxPlanner.Plan(_request);

            var colours = _ctx.Style.Assign(ScopeBoxColourMode.SizeClass, _result.Classes.Select(c => c.Key));
            _grid.ItemsSource = _result.Boxes.Select(b => new PlanRow
            {
                Name = b.Name, Class = b.ClassKey,
                Size = $"{ScopeBoxNames.Metres(b.WidthM)} × {ScopeBoxNames.Metres(b.DepthM)}",
                Seed = _result.Classes.FirstOrDefault(c => c.Key == b.ClassKey)?.Seed?.Name ?? "— none fits —",
                Status = b.Status == PlannedBoxStatus.New ? "New" : b.Status == PlannedBoxStatus.Exists ? "Exists — kept" : "Needs a seed",
                Swatch = colours.TryGetValue(b.ClassKey, out var hex) && ScopeBoxStyle.TryParseHex(hex, out var r, out var g, out var bl)
                    ? new SolidColorBrush(Color.FromRgb(r, g, bl)) : Brushes.Transparent,
            }).ToList();

            var lines = new List<string>();
            if (o.DrawingTypeIds.Count == 0) lines.Add("Tick at least one drawing type.");
            foreach (var c in _result.Classes)
                lines.Add($"{c.Key}: {c.DrawingTypeIds.Count} type(s), box ≤ {ScopeBoxNames.Metres(c.MaxWidthM)} × {ScopeBoxNames.Metres(c.MaxDepthM)} m, "
                        + (c.Seed != null ? $"seed {c.Seed.Label}{(c.SeedRotated ? " turned 90°" : "")}" : "no seed fits")
                        + $" → {_result.Boxes.Count(b => b.ClassKey == c.Key)} box(es)");
            lines.Add($"{_result.CountToCreate} to create, {_result.Boxes.Count(b => b.Status == PlannedBoxStatus.Exists)} already exist, "
                    + $"{_result.Boxes.Count(b => b.Status == PlannedBoxStatus.NoSeed)} need a seed.");
            lines.AddRange(_result.Warnings.Select(w => "⚠ " + w));
            _summary.Text = string.Join("\n", lines);

            _seedText.Text = (_ctx.Seeds.Count == 0 ? "No STING-SEED:: boxes yet." : "Available: " + string.Join(", ", _ctx.Seeds.Select(s => s.Label)))
                + (_result.MissingSeeds.Count > 0 ? "\n\nStill to draw:\n• " + string.Join("\n• ", _result.MissingSeeds.Select(m => m.Instruction)) : "");
        }

        // ── writes (ExternalEvent) ───────────────────────────────────────

        private void Raise(string title, Func<UIApplication, string> work, bool reload = false)
        {
            if (_event == null) { _status.Text = $"{title} unavailable — close and reopen the planner."; return; }
            _pendingTitle = title; _pending = work; _reloadAfter = reload;
            _status.Text = title + "…";
            try { _event.Raise(); }
            catch (Exception ex) { StingLog.Error($"ScopeBoxPlanner {title}", ex); _status.Text = $"{title} could not start: {ex.Message}"; }
        }

        private void OnCreate(object s, RoutedEventArgs e)
        {
            if (_result == null || _result.CountToCreate == 0) { _status.Text = "Nothing to create — tick types, or add the seeds listed under 'Still to draw'."; return; }
            var o = Options(out var error);
            if (error != null) { _status.Text = error; return; }
            var req = _request; var res = _result;
            Raise("Create boxes", a => ScopeBoxPlannerService.Create(a.ActiveUIDocument.Document, _ctx, o, req, res), reload: true);
        }

        private void OnProduce(object s, RoutedEventArgs e)
            => Raise("Produce views", a =>
                StingTools.Commands.Drawing.ScopeBoxProduceAreasCommand.RunInteractive(a.ActiveUIDocument.Document) ?? "Cancelled.");

        private void OnImport(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a project or template that holds STING-SEED:: scope boxes",
                Filter = "Revit projects and templates (*.rvt;*.rte)|*.rvt;*.rte",
            };
            if (dlg.ShowDialog(this) != true) return;
            var path = dlg.FileName;
            Raise("Import seeds", a => ScopeBoxPlannerService.ImportSeeds(a, a.ActiveUIDocument.Document, path), reload: true);
        }

        private void OnColour()
        {
            if (_colourMode.SelectedItem == null || !Enum.TryParse<ScopeBoxColourMode>((string)_colourMode.SelectedItem, out var mode)) return;
            bool all = _colourScope.SelectedIndex == 1;
            Raise("Colour", a => ScopeBoxPlannerService.Colour(a.ActiveUIDocument.Document, mode, all, a.ActiveUIDocument.ActiveView));
        }

        private void AfterAction(string title, string report)
        {
            _status.Text = $"{title}: {(report ?? "").Split('\n')[0]}";
            if (!string.IsNullOrWhiteSpace(report) && report.Contains("\n"))
                TaskDialog.Show("STING Scope Box Planner — " + title, report);
        }

        private sealed class Handler : IExternalEventHandler
        {
            private readonly ScopeBoxPlannerDialog _o;
            public Handler(ScopeBoxPlannerDialog o) { _o = o; }
            public string GetName() => "STING Scope Box Planner";
            public void Execute(UIApplication app)
            {
                var work = _o._pending; var title = _o._pendingTitle; bool reload = _o._reloadAfter;
                _o._pending = null;
                if (work == null) return;
                string report;
                try
                {
                    // The window may have been opened on another document.
                    if (app.ActiveUIDocument?.Document?.Equals(_o._doc) != true)
                        report = "The active document is not the one this planner was opened on — reopen the planner.";
                    else
                    {
                        report = work(app);
                        if (reload) _o.LoadModel();   // API context: re-read seeds, boxes and the saved plan.
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { report = "Cancelled."; }
                catch (Exception ex) { StingLog.Error($"ScopeBoxPlanner '{title}'", ex); report = "ERROR: " + ex.Message; }
                var r = report;
                try
                {
                    _o.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (reload) _o.Replan();
                        _o.AfterAction(title, r);
                    }));
                }
                catch (Exception ex) { StingLog.Warn($"ScopeBoxPlanner dispatch: {ex.Message}"); }
            }
        }
    }
}
