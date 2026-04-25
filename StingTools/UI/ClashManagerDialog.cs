// StingTools v4 MVP — Clash Manager dialog (Phase I.3).
//
// Loads the active project's clashes.json via ClashPersistence and
// presents the results as a filterable DataGrid. Each row is a
// ClashRecord; users can:
//
//   - Filter by severity / state / category pair
//   - Click a row → select both elements in Revit (if in host doc)
//   - Bulk state transition (Resolve / Ignore) with audit trail
//   - Export selection → BCF via ClashBcfExport command
//
// The audit confirmed the full clash engine exists (33 files) but
// the five pipeline commands (ClashRun / SessionRefresh / SessionClear
// / MatrixEdit / BcfExport) had no visible buttons. This dialog is
// the missing entry point.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Clash;

// Autodesk.Revit.DB and System.Windows.* share a batch of type names
// (Grid line, Color, Binding parameter, …). Alias the WPF ones so
// every control / binding / colour ref in this file binds to WPF.
using TextBox      = System.Windows.Controls.TextBox;
using ComboBox     = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Grid         = System.Windows.Controls.Grid;
using Color        = System.Windows.Media.Color;
using Colors       = System.Windows.Media.Colors;
using Binding      = System.Windows.Data.Binding;

namespace StingTools.UI
{
    public class ClashManagerDialog : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private ClashRunRecord _run;
        private DataGrid _grid;
        private ComboBox _cmbSeverity;
        private ComboBox _cmbState;
        private TextBox _txtFilter;
        private TextBlock _header;

        public ClashManagerDialog(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc?.Document;

            Title = "STING v4 — Clash Manager";
            Width = 1000;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(DarkDialogTheme.LightPalette.WindowBg);
            Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg);
            DarkDialogTheme.ApplyComboBoxFix(this,
                DarkDialogTheme.LightPalette.CardBg,
                DarkDialogTheme.LightPalette.BodyFg,
                DarkDialogTheme.LightPalette.AltRowBg);

            BuildUi();
            LoadClashFile();
        }

        private string ClashesJsonPath
        {
            get
            {
                try
                {
                    var projDir = Path.GetDirectoryName(_doc?.PathName ?? "") ?? Path.GetTempPath();
                    return Path.Combine(projDir, "_BIM_COORD", "clashes.json");
                }
                catch { return Path.Combine(Path.GetTempPath(), "clashes.json"); }
            }
        }

        private void BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _header = new TextBlock
            {
                Margin = new Thickness(16, 12, 16, 6),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Text = "Clash Manager",
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
            };
            Grid.SetRow(_header, 0);
            root.Children.Add(_header);

            // Filter bar
            var filterBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 0, 16, 8),
            };
            filterBar.Children.Add(MakeLabel("Filter:"));
            _txtFilter = new TextBox
            {
                Width = 220, Height = 26,
                Margin = new Thickness(6, 0, 16, 0),
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
                ToolTip = "Search across Id / Category A / Category B / State / Severity",
            };
            _txtFilter.TextChanged += (_, __) => ApplyFilter();
            filterBar.Children.Add(_txtFilter);

            filterBar.Children.Add(MakeLabel("Severity:"));
            _cmbSeverity = MakeCombo(new[] { "(any)", "Hard", "Clearance", "Soft" });
            _cmbSeverity.SelectionChanged += (_, __) => ApplyFilter();
            filterBar.Children.Add(_cmbSeverity);

            filterBar.Children.Add(MakeLabel("State:"));
            _cmbState = MakeCombo(new[] { "(any)", "New", "Active", "Assigned", "InReview", "Resolved", "Reintroduced", "Void" });
            _cmbState.SelectionChanged += (_, __) => ApplyFilter();
            filterBar.Children.Add(_cmbState);

            Grid.SetRow(filterBar, 1);
            root.Children.Add(filterBar);

            // Data grid — light palette with an explicit header style so
            // column titles are always readable (WPF default header
            // foreground is system-dependent and was rendering
            // invisible on the old dark background).
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                RowBackground = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                AlternatingRowBackground = new SolidColorBrush(DarkDialogTheme.LightPalette.AltRowBg),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
                Margin = new Thickness(16, 0, 16, 8),
            };
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty,
                new SolidColorBrush(DarkDialogTheme.LightPalette.AltRowBg)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty,
                new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(6, 4, 6, 4)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.SemiBold));
            _grid.ColumnHeaderStyle = headerStyle;
            _grid.Columns.Add(new DataGridTextColumn { Header = "Id",          Binding = new Binding("Id"),       Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Severity",    Binding = new Binding("Severity"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "State",       Binding = new Binding("State"),    Width = 100 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Category A",  Binding = new Binding("ElementA.Category"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Category B",  Binding = new Binding("ElementB.Category"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Volume mm³",  Binding = new Binding("VolumeMm3"),Width = 100 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Resolution",  Binding = new Binding("ResolutionHint"), Width = 200 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Issue GUID",  Binding = new Binding("LinkedIssueGuid"), Width = 90 });
            _grid.MouseDoubleClick += (_, __) => JumpToSelection();
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            // Action buttons
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 8, 16, 16),
            };
            actions.Children.Add(MakeButton("Refresh",      "Reload clashes.json from disk",     (s, e) => LoadClashFile()));
            actions.Children.Add(MakeButton("Run Clash",    "Dispatch ClashRun command",         (s, e) => Dispatch("ClashRun")));
            actions.Children.Add(MakeButton("Matrix…",      "Edit clash-pair matrix",            (s, e) => Dispatch("ClashMatrixEdit")));
            actions.Children.Add(MakeButton("Mark Resolved","Transition selected to Resolved",   (s, e) => TransitionSelected("Resolved")));
            actions.Children.Add(MakeButton("Ignore",       "Transition selected to Void",       (s, e) => TransitionSelected("Void")));
            actions.Children.Add(MakeButton("Zoom to",      "Zoom Revit view onto both elements",(s, e) => JumpToSelection()));
            actions.Children.Add(MakeButton("Export BCF",   "Emit BCF 2.1 for Navisworks / ACC", (s, e) => Dispatch("ClashBcfExport")));
            actions.Children.Add(MakeButton("Close",        "",                                  (s, e) => Close(), accent: false));
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            Content = root;
        }

        // ---- data binding ------------------------------------------------------

        private void LoadClashFile()
        {
            try
            {
                var path = ClashesJsonPath;
                if (!File.Exists(path))
                {
                    _header.Text = "Clash Manager — no clashes.json on disk (run 'Run Clash' first)";
                    _grid.ItemsSource = null;
                    return;
                }
                _run = ClashPersistence.Load(path);
                if (_run == null)
                {
                    _header.Text = "Clash Manager — clashes.json failed to parse";
                    _grid.ItemsSource = null;
                    return;
                }
                _header.Text = $"Clash Manager — run {_run.RunId}  |  " +
                               $"{_run.Stats?.Raw ?? 0} raw, {_run.Stats?.New ?? 0} new, " +
                               $"{_run.Stats?.Active ?? 0} active, {_run.Stats?.Resolved ?? 0} resolved";
                ApplyFilter();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashManagerDialog.LoadClashFile: {ex.Message}");
                _header.Text = $"Clash Manager — load failed: {ex.Message}";
            }
        }

        private void ApplyFilter()
        {
            if (_run?.Clashes == null) { _grid.ItemsSource = null; return; }
            string q = (_txtFilter.Text ?? "").Trim().ToLowerInvariant();
            string sev = ((_cmbSeverity.SelectedItem as ComboBoxItem)?.Content as string) ?? "(any)";
            string st  = ((_cmbState.SelectedItem    as ComboBoxItem)?.Content as string) ?? "(any)";

            IEnumerable<ClashRecord> rows = _run.Clashes;
            if (sev != "(any)") rows = rows.Where(c => string.Equals(c.Severity, sev, StringComparison.OrdinalIgnoreCase));
            if (st  != "(any)") rows = rows.Where(c => string.Equals(c.State,    st,  StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(q))
                rows = rows.Where(c =>
                    (c.Id      ?? "").ToLowerInvariant().Contains(q) ||
                    (c.State   ?? "").ToLowerInvariant().Contains(q) ||
                    (c.Severity?? "").ToLowerInvariant().Contains(q) ||
                    (c.ElementA?.Category ?? "").ToLowerInvariant().Contains(q) ||
                    (c.ElementB?.Category ?? "").ToLowerInvariant().Contains(q));
            _grid.ItemsSource = rows.ToList();
        }

        // ---- actions -----------------------------------------------------------

        private void JumpToSelection()
        {
            if (_uidoc == null) return;
            var clash = _grid.SelectedItem as ClashRecord;
            if (clash == null) return;
            try
            {
                var ids = new List<ElementId>();
                if (clash.ElementA != null && clash.ElementA.ElementId > 0)
                    ids.Add(new ElementId((long)clash.ElementA.ElementId));
                if (clash.ElementB != null && clash.ElementB.ElementId > 0)
                    ids.Add(new ElementId((long)clash.ElementB.ElementId));
                if (ids.Count > 0)
                {
                    _uidoc.Selection.SetElementIds(ids);
                    try { _uidoc.ShowElements(ids); } catch { }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"ClashManagerDialog.JumpToSelection: {ex.Message}"); }
        }

        private void TransitionSelected(string newState)
        {
            if (_run == null || _grid.SelectedItems == null) return;
            int n = 0;
            foreach (var obj in _grid.SelectedItems)
            {
                if (obj is ClashRecord c)
                {
                    c.State = newState;
                    c.LastSeenUtc = DateTime.UtcNow;
                    c.StateHistory.Add(new StateTransition
                    {
                        AtUtc = DateTime.UtcNow,
                        To    = newState,
                        By    = Environment.UserName,
                    });
                    n++;
                }
            }
            if (n > 0)
            {
                try { ClashPersistence.Save(_run, ClashesJsonPath); }
                catch (Exception ex) { StingLog.Warn($"ClashManagerDialog.TransitionSelected save: {ex.Message}"); }
                ApplyFilter();
            }
        }

        private void Dispatch(string tag)
        {
            try { Commands.Mep.ClashManagerDispatcher.Dispatch(tag); }
            catch (Exception ex)
            { StingLog.Warn($"ClashManagerDialog.Dispatch({tag}): {ex.Message}"); }
        }

        // ---- UI helpers --------------------------------------------------------

        private static TextBlock MakeLabel(string t) => new TextBlock
        {
            Text = t,
            Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static ComboBox MakeCombo(IEnumerable<string> items)
        {
            var cb = new ComboBox
            {
                Width = 110, Height = 26,
                Margin = new Thickness(0, 0, 16, 0),
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
            };
            foreach (var it in items) cb.Items.Add(new ComboBoxItem { Content = it });
            cb.SelectedIndex = 0;
            return cb;
        }

        private static Button MakeButton(string label, string tip, RoutedEventHandler onClick, bool accent = true)
        {
            var b = new Button
            {
                Content = label,
                Width = 110, Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = tip,
                Background = new SolidColorBrush(accent
                    ? DarkDialogTheme.LightPalette.Accent
                    : DarkDialogTheme.LightPalette.SecondaryBtn),
                Foreground = new SolidColorBrush(accent
                    ? DarkDialogTheme.LightPalette.AccentFg
                    : DarkDialogTheme.LightPalette.BodyFg),
                BorderThickness = new Thickness(accent ? 0 : 1),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
                FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            };
            b.Click += onClick;
            return b;
        }
    }
}
