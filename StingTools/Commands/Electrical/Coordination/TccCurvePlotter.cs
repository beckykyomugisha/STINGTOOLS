using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>
    /// Pure-C# SVG renderer for time-current coordination curves. Plots one
    /// or more <see cref="TccEntry"/>'s clearing characteristic on log-log
    /// axes (current 1 A → 100 kA on X, time 1 ms → 1000 s on Y), shades
    /// the prospective fault current band, and draws labels in standard
    /// IET coordination-study style.
    ///
    /// No dependency on ScottPlot / OxyPlot — the existing project is
    /// already deep on dependencies and this needs to ship in shop drawings,
    /// where SVG embeds cleanly via Revit's Image import path or via the
    /// drawing-set PDF export.
    /// </summary>
    public static class TccCurvePlotter
    {
        // ── Plot constants (log decades) ─────────────────────────────────
        private const double XminA  = 1.0;        // 1 A
        private const double XmaxA  = 100_000.0;  // 100 kA
        private const double YminS  = 0.001;      // 1 ms
        private const double YmaxS  = 1000.0;     // 1000 s

        private const int W = 900, H = 700;
        private const int Pad = 70;

        public static string ToSvg(IList<TccPlotSeries> series, double availableFaultKa = 0,
            string title = "Time-Current Coordination Curves")
        {
            int plotW = W - 2 * Pad;
            int plotH = H - 2 * Pad;
            double xLog0 = Math.Log10(XminA), xLog1 = Math.Log10(XmaxA);
            double yLog0 = Math.Log10(YminS), yLog1 = Math.Log10(YmaxS);
            double Sx(double a) => Pad + plotW * (Math.Log10(a) - xLog0) / (xLog1 - xLog0);
            double Sy(double s) => Pad + plotH - plotH * (Math.Log10(s) - yLog0) / (yLog1 - yLog0);

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {W} {H}' width='{W}' height='{H}'>");
            sb.AppendLine($"  <rect x='0' y='0' width='{W}' height='{H}' fill='white' stroke='none'/>");
            sb.AppendLine($"  <text x='{W / 2}' y='28' text-anchor='middle' font-family='Arial' font-size='16' font-weight='bold'>{Esc(title)}</text>");

            // Plot frame
            sb.AppendLine($"  <rect x='{Pad}' y='{Pad}' width='{plotW}' height='{plotH}' fill='none' stroke='#222' stroke-width='1.2'/>");

            // Log gridlines + tick labels — major decades
            for (int dec = (int)xLog0; dec <= xLog1; dec++)
            {
                double a = Math.Pow(10, dec);
                double x = Sx(a);
                sb.AppendLine($"  <line x1='{x:0.0}' y1='{Pad}' x2='{x:0.0}' y2='{Pad + plotH}' stroke='#ddd' stroke-width='0.6'/>");
                sb.AppendLine($"  <text x='{x:0.0}' y='{Pad + plotH + 18}' text-anchor='middle' font-family='Arial' font-size='10'>{FormatA(a)}</text>");
                // Minor gridlines (2..9)
                for (int mul = 2; mul <= 9; mul++)
                {
                    double xm = Sx(a * mul);
                    if (xm < Pad || xm > Pad + plotW) continue;
                    sb.AppendLine($"  <line x1='{xm:0.0}' y1='{Pad}' x2='{xm:0.0}' y2='{Pad + plotH}' stroke='#f0f0f0' stroke-width='0.4'/>");
                }
            }
            for (int dec = (int)yLog0; dec <= yLog1; dec++)
            {
                double s = Math.Pow(10, dec);
                double y = Sy(s);
                sb.AppendLine($"  <line x1='{Pad}' y1='{y:0.0}' x2='{Pad + plotW}' y2='{y:0.0}' stroke='#ddd' stroke-width='0.6'/>");
                sb.AppendLine($"  <text x='{Pad - 6}' y='{y + 4:0.0}' text-anchor='end' font-family='Arial' font-size='10'>{FormatT(s)}</text>");
                for (int mul = 2; mul <= 9; mul++)
                {
                    double ym = Sy(s * mul);
                    if (ym < Pad || ym > Pad + plotH) continue;
                    sb.AppendLine($"  <line x1='{Pad}' y1='{ym:0.0}' x2='{Pad + plotW}' y2='{ym:0.0}' stroke='#f0f0f0' stroke-width='0.4'/>");
                }
            }

            // Axis labels
            sb.AppendLine($"  <text x='{W / 2}' y='{H - 8}' text-anchor='middle' font-family='Arial' font-size='12'>Current (A) — log scale</text>");
            sb.AppendLine($"  <text x='16' y='{H / 2}' text-anchor='middle' font-family='Arial' font-size='12' transform='rotate(-90 16 {H / 2})'>Time (s) — log scale</text>");

            // Available fault current band (prospective Ipf at the panel)
            if (availableFaultKa > 0)
            {
                double xPsc = Sx(availableFaultKa * 1000.0);
                double xLow = Sx(availableFaultKa * 1000.0 * 0.7);  // 30% tolerance band
                sb.AppendLine($"  <rect x='{xLow:0.0}' y='{Pad}' width='{(xPsc - xLow):0.0}' height='{plotH}' fill='#fde7e7' fill-opacity='0.7' stroke='none'/>");
                sb.AppendLine($"  <line x1='{xPsc:0.0}' y1='{Pad}' x2='{xPsc:0.0}' y2='{Pad + plotH}' stroke='#c00' stroke-width='1' stroke-dasharray='4,3'/>");
                sb.AppendLine($"  <text x='{xPsc:0.0}' y='{Pad + 18}' text-anchor='middle' font-family='Arial' font-size='11' fill='#c00'>I_PSC = {availableFaultKa:0.0} kA</text>");
            }

            // Plot each series
            string[] palette = { "#1f77b4", "#d62728", "#2ca02c", "#9467bd", "#ff7f0e", "#17becf" };
            int cIdx = 0;
            foreach (var ser in series)
            {
                string colour = palette[cIdx++ % palette.Length];
                var pts = ser.Points.OrderBy(p => p.Item1).ToList();
                if (pts.Count < 2) continue;
                var poly = string.Join(" ", pts.Select(p => $"{Sx(p.Item1):0.0},{Sy(p.Item2):0.0}"));
                sb.AppendLine($"  <polyline points='{poly}' fill='none' stroke='{colour}' stroke-width='2'/>");
                // Legend marker
                int legY = Pad + 20 + (cIdx - 1) * 18;
                int legX = Pad + plotW - 200;
                sb.AppendLine($"  <line x1='{legX}' y1='{legY}' x2='{legX + 18}' y2='{legY}' stroke='{colour}' stroke-width='2'/>");
                sb.AppendLine($"  <text x='{legX + 24}' y='{legY + 4}' font-family='Arial' font-size='11' fill='{colour}'>{Esc(ser.Label)}</text>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static void WriteSvgFile(string path, IList<TccPlotSeries> series,
            double availableFaultKa = 0, string title = "Time-Current Coordination Curves")
        {
            File.WriteAllText(path, ToSvg(series, availableFaultKa, title));
        }

        private static string FormatA(double a) =>
            a >= 1000 ? $"{a / 1000.0:0.#}k" : $"{a:0.#}";

        private static string FormatT(double s) =>
            s < 1 ? $"{s * 1000:0}ms" : $"{s:0.#}s";

        private static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    /// <summary>One named curve to plot — list of (current_A, time_s) points.</summary>
    public class TccPlotSeries
    {
        public string Label { get; set; } = "";
        public List<Tuple<double, double>> Points { get; set; } = new();

        /// <summary>
        /// Sample the entry's <see cref="TccEntry.ClearingTimeMs"/> across
        /// the rated fault range (or supplied current decades if missing) to
        /// build a plot-ready series. Honours the log-log point list when
        /// the entry has one (Phase 182).
        /// </summary>
        public static TccPlotSeries FromTccEntry(TccEntry entry, string label,
            double minKa = 0.05, double maxKa = 50)
        {
            var s = new TccPlotSeries { Label = label };
            if (entry == null) return s;
            double i0 = Math.Max(minKa, entry.MinFaultKa);
            double i1 = Math.Min(maxKa, entry.MaxFaultKa > 0 ? entry.MaxFaultKa : maxKa);
            if (i0 <= 0) i0 = 0.05;
            if (i1 <= i0) i1 = i0 * 100;
            // 64 log-spaced samples
            double l0 = Math.Log10(i0), l1 = Math.Log10(i1);
            for (int i = 0; i <= 64; i++)
            {
                double f = i / 64.0;
                double ka = Math.Pow(10, l0 + f * (l1 - l0));
                double t = entry.ClearingTimeMs(ka) / 1000.0;
                if (t > 0 && ka > 0)
                    s.Points.Add(Tuple.Create(ka * 1000.0, t)); // convert kA → A for X
            }
            return s;
        }
    }
}
