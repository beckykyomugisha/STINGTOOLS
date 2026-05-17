using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StingTools.Commands.Electrical.Coordination;
using StingTools.Core;
using StingTools.Core.SLD;

namespace StingTools.UI
{
    /// <summary>
    /// Modal selective-coordination viewer. Left tree shows the SLD
    /// hierarchy from <see cref="SLDCircuitTraverser"/>; selecting a node
    /// looks up its parent and renders both clearing-time curves on a
    /// log-log canvas. The bottom grid lists every violation found by
    /// <see cref="SelectiveCoordEngine"/>.
    /// </summary>
    public partial class SelectiveCoordDialog : Window
    {
        public ObservableCollection<CoordViolation> Violations { get; }
            = new ObservableCollection<CoordViolation>();

        private readonly SLDNode _root;
        private readonly TccDatabase _tcc;

        public SelectiveCoordDialog(SLDNode root, IEnumerable<CoordViolation> violations,
            TccDatabase tcc)
        {
            InitializeComponent();
            _root = root;
            _tcc = tcc;
            ViolationsGrid.ItemsSource = Violations;
            foreach (var v in violations ?? Enumerable.Empty<CoordViolation>())
                Violations.Add(v);
            BuildTree(root);
            StatusLabel.Text = Violations.Count == 0
                ? "✅ Fully selective"
                : $"⚠ {Violations.Count} violation(s)";
            DrawAxes();
        }

        private void BuildTree(SLDNode root)
        {
            HierarchyTree.Items.Clear();
            if (root == null) return;
            var item = new TreeViewItem
            {
                Header = root.Label ?? "(root)",
                Foreground = Brushes.White,
                IsExpanded = true,
                Tag = root
            };
            HierarchyTree.Items.Add(item);
            foreach (var c in root.Children ?? Enumerable.Empty<SLDNode>())
                AddNode(item, c);
        }

        private void AddNode(TreeViewItem parent, SLDNode node)
        {
            var item = new TreeViewItem
            {
                Header = node.Label ?? "(unnamed)",
                Foreground = Brushes.White,
                IsExpanded = true,
                Tag = node
            };
            parent.Items.Add(item);
            foreach (var c in node.Children ?? Enumerable.Empty<SLDNode>())
                AddNode(item, c);
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is TreeViewItem tvi) || !(tvi.Tag is SLDNode node)) return;
            SLDNode parent = FindParent(_root, node);
            SelectedPairLabel.Text = parent == null
                ? $"{node.Label} (no upstream)"
                : $"{parent.Label} → {node.Label}";
            DrawTccPair(parent, node);
        }

        private static SLDNode FindParent(SLDNode root, SLDNode target)
        {
            if (root == null || target == null) return null;
            foreach (var c in root.Children ?? Enumerable.Empty<SLDNode>())
            {
                if (ReferenceEquals(c, target)) return root;
                var p = FindParent(c, target);
                if (p != null) return p;
            }
            return null;
        }

        // ── log-log chart ───────────────────────────────────────────────
        // X axis: fault kA, 0.01 → 100 (4 decades).
        // Y axis: clearing ms, 1 → 10000 (4 decades).
        private const double X_MIN = 0.01, X_MAX = 100.0;
        private const double Y_MIN = 1.0,  Y_MAX = 10000.0;

        private void DrawAxes()
        {
            try
            {
                TccCanvas.Children.Clear();
                double w = Math.Max(100, TccCanvas.ActualWidth);
                double h = Math.Max(100, TccCanvas.ActualHeight);
                if (w < 80 || h < 80) return;
                var axisBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                var labelBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160));

                // Vertical decade lines
                for (double xv = 0.01; xv <= X_MAX + 1e-9; xv *= 10)
                {
                    double px = MapX(xv, w);
                    TccCanvas.Children.Add(new Line
                    {
                        X1 = px, Y1 = 10, X2 = px, Y2 = h - 20,
                        Stroke = axisBrush, StrokeThickness = 0.5
                    });
                    var tb = new TextBlock { Text = $"{xv:0.##} kA", Foreground = labelBrush, FontSize = 9 };
                    Canvas.SetLeft(tb, px + 2); Canvas.SetTop(tb, h - 18);
                    TccCanvas.Children.Add(tb);
                }
                for (double yv = Y_MIN; yv <= Y_MAX + 1e-9; yv *= 10)
                {
                    double py = MapY(yv, h);
                    TccCanvas.Children.Add(new Line
                    {
                        X1 = 40, Y1 = py, X2 = w - 10, Y2 = py,
                        Stroke = axisBrush, StrokeThickness = 0.5
                    });
                    var tb = new TextBlock { Text = $"{yv:0} ms", Foreground = labelBrush, FontSize = 9 };
                    Canvas.SetLeft(tb, 4); Canvas.SetTop(tb, py - 6);
                    TccCanvas.Children.Add(tb);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrawAxes: {ex.Message}"); }
        }

        private void DrawTccPair(SLDNode parent, SLDNode child)
        {
            DrawAxes();
            if (_tcc == null || child == null) return;
            var down = _tcc.Resolve(child.Rating);
            var up = parent != null ? _tcc.Resolve(parent.Rating) : null;
            double w = Math.Max(100, TccCanvas.ActualWidth);
            double h = Math.Max(100, TccCanvas.ActualHeight);
            if (down != null) DrawCurve(down, w, h, Colors.Tomato);
            if (up != null) DrawCurve(up, w, h, Colors.DodgerBlue);
        }

        private void DrawCurve(TccEntry entry, double w, double h, Color colour)
        {
            try
            {
                var poly = new Polyline
                {
                    Stroke = new SolidColorBrush(colour),
                    StrokeThickness = 2.0
                };
                var pts = new PointCollection();
                for (double f = X_MIN; f <= X_MAX; f *= 1.10)
                {
                    double ms = entry.ClearingTimeMs(f);
                    if (ms <= 0) continue;
                    pts.Add(new Point(MapX(f, w), MapY(ms, h)));
                }
                poly.Points = pts;
                TccCanvas.Children.Add(poly);
            }
            catch (Exception ex) { StingLog.Warn($"DrawCurve: {ex.Message}"); }
        }

        private static double MapX(double valueKa, double widthPx)
        {
            double safe = Math.Max(valueKa, 1e-6);
            double t = (Math.Log10(safe) - Math.Log10(X_MIN)) / (Math.Log10(X_MAX) - Math.Log10(X_MIN));
            return 40 + t * (widthPx - 50);
        }
        private static double MapY(double valueMs, double heightPx)
        {
            double safe = Math.Max(valueMs, 1e-6);
            double t = (Math.Log10(safe) - Math.Log10(Y_MIN)) / (Math.Log10(Y_MAX) - Math.Log10(Y_MIN));
            return (heightPx - 20) - t * (heightPx - 30);
        }

        // ── footer actions ──────────────────────────────────────────────

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(null);
                string outPath = System.IO.Path.Combine(outDir, $"STING_SelectiveCoord_{DateTime.Now:yyyyMMdd-HHmm}.csv");
                using (var sw = new StreamWriter(outPath))
                {
                    sw.WriteLine("Upstream,Downstream,FaultKa,Reason");
                    foreach (var v in Violations)
                        sw.WriteLine($"{Csv(v.UpstreamDevice)},{Csv(v.DownstreamDevice)},{v.FaultKa:0.00},{Csv(v.Reason)}");
                }
                MessageBox.Show($"Exported to:\n{outPath}", "STING");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SelectiveCoord CSV export: {ex.Message}");
                MessageBox.Show($"Export failed: {ex.Message}", "STING");
            }
        }
        private static string Csv(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
