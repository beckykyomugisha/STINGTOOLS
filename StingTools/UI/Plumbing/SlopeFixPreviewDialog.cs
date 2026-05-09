// SlopeFixPreviewDialog — SyncStyles-style preview grid for the
// SlopeAutoCorrector. Shows planned fix list with current/proposed
// slope, Δ-elevation, and connector impact. User picks Apply All /
// Apply Selected / Cancel. Phase 178c.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Calc;

namespace StingTools.UI.Plumbing
{
    public enum SlopeFixDecision { Cancel, ApplyAll, ApplySelected }

    public class SlopeFixDecisionResult
    {
        public SlopeFixDecision Decision { get; set; } = SlopeFixDecision.Cancel;
        public HashSet<long> SelectedPipeIds { get; } = new HashSet<long>();
    }

    public static class SlopeFixPreviewDialog
    {
        public static SlopeFixDecisionResult Show(SlopeAutoCorrectionResult preview)
        {
            var result = new SlopeFixDecisionResult();
            if (preview == null || preview.Fixes == null || preview.Fixes.Count == 0) return result;

            var win = new Window
            {
                Title = "STING Plumbing — Slope Fix Preview",
                Width = 1100, Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            var root = new DockPanel { LastChildFill = true };

            var summary = new TextBlock
            {
                Text = $"{preview.Fixes.Count} pipes analysed · " +
                       $"flip {preview.PipesFlipped} · depress {preview.PipesDepressed} · " +
                       $"unchanged {preview.PipesUnchanged} · skipped (both ends connected) {preview.PipesSkippedConnectedBothEnds} · " +
                       $"failed {preview.PipesFailed}",
                Margin = new Thickness(12, 10, 12, 6),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            };
            DockPanel.SetDock(summary, Dock.Top);
            root.Children.Add(summary);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                Margin = new Thickness(8)
            };
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Apply", Binding = new System.Windows.Data.Binding("Selected") { Mode = System.Windows.Data.BindingMode.TwoWay } });
            grid.Columns.Add(new DataGridTextColumn { Header = "Pipe ID",      Binding = new System.Windows.Data.Binding("PipeId"),       IsReadOnly = true, Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Action",       Binding = new System.Windows.Data.Binding("Action"),       IsReadOnly = true, Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Current %",    Binding = new System.Windows.Data.Binding("OriginalPct"),  IsReadOnly = true, Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Target %",     Binding = new System.Windows.Data.Binding("TargetPct"),    IsReadOnly = true, Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Δ-elevation",  Binding = new System.Windows.Data.Binding("DeltaText"),    IsReadOnly = true, Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Connector impact", Binding = new System.Windows.Data.Binding("ImpactText"), IsReadOnly = true, Width = 230 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Notes",        Binding = new System.Windows.Data.Binding("Notes"),        IsReadOnly = true, Width = 280 });

            var rows = preview.Fixes.Select(f => new RowVm(f)).ToList();
            grid.ItemsSource = rows;
            root.Children.Add(grid);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            DockPanel.SetDock(bar, Dock.Bottom);
            var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            var apply  = new Button { Content = "Apply Selected", Width = 130, Margin = new Thickness(6, 0, 0, 0) };
            var all    = new Button { Content = "Apply All", Width = 100, Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(34, 139, 34)), Foreground = Brushes.White };
            cancel.Click += (s, e) => { result.Decision = SlopeFixDecision.Cancel; win.Close(); };
            apply.Click  += (s, e) =>
            {
                result.Decision = SlopeFixDecision.ApplySelected;
                foreach (var r in rows) if (r.Selected && r.RawId != 0) result.SelectedPipeIds.Add(r.RawId);
                win.Close();
            };
            all.Click    += (s, e) => { result.Decision = SlopeFixDecision.ApplyAll; win.Close(); };
            bar.Children.Add(cancel);
            bar.Children.Add(apply);
            bar.Children.Add(all);
            root.Children.Add(bar);

            win.Content = root;
            win.ShowDialog();
            return result;
        }

        private class RowVm
        {
            public RowVm(SlopeFix f)
            {
                RawId       = f.PipeId?.Value ?? 0;
                PipeId      = f.PipeId?.Value.ToString() ?? "";
                Action      = f.Action;
                OriginalPct = f.OriginalPct.ToString("F2");
                TargetPct   = f.TargetPct.ToString("F2");
                DeltaText   = (f.DeltaZFt * 304.8).ToString("F0") + " mm";
                ImpactText  = f.ConnectorImpact switch
                {
                    ConnectorImpact.NoConnections        => "No connections — safe",
                    ConnectorImpact.FlipDirection        => "Flip — connector roles re-evaluated",
                    ConnectorImpact.MovedAttachedFitting => $"Moves fitting {f.MovedFittingId.Value} (preserves topology)",
                    ConnectorImpact.SkippedConnected     => "Both ends connected — skipped",
                    _ => ""
                };
                Notes       = f.Success ? "" : f.FailureReason;
                Selected    = f.Success && f.ConnectorImpact != ConnectorImpact.SkippedConnected;
            }
            public long RawId       { get; }
            public string PipeId    { get; }
            public string Action    { get; }
            public string OriginalPct { get; }
            public string TargetPct { get; }
            public string DeltaText { get; }
            public string ImpactText{ get; }
            public string Notes     { get; }
            public bool Selected    { get; set; }
        }
    }
}
