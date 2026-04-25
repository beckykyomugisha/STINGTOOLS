// StingTools v4 MVP — Phase L tray fill cross-section widget.
//
// WPF window that renders a scaled cross-section of a cable tray or
// conduit with one circle per cable (OD to scale, colour by
// segregation class). Non-modal so users can select multiple trays
// in sequence without closing the window. Designed for live feedback
// during cable-pulling / prefab planning.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StingTools.Core.Electrical;

namespace StingTools.UI
{
    public partial class TrayFillWindow : Window
    {
        private readonly Canvas _canvas;
        private readonly TextBlock _header;

        public TrayFillWindow()
        {
            Title  = "STING v4 — Tray Fill";
            Width  = 560;
            Height = 540;
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _header = new TextBlock
            {
                Margin = new Thickness(12, 10, 12, 10),
                FontSize = 14,
                Text = "(no tray selected)"
            };
            Grid.SetRow(_header, 0);
            grid.Children.Add(_header);

            _canvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 44)),
                Margin = new Thickness(12, 0, 12, 12)
            };
            Grid.SetRow(_canvas, 1);
            grid.Children.Add(_canvas);
            Content = grid;
        }

        public void Display(TrayFillReport r)
        {
            if (r == null) return;
            _canvas.Children.Clear();

            _header.Text =
                $"Tray #{r.TrayId}  {r.TrayKind}  " +
                $"{r.InnerWidthMm:F0}×{r.InnerHeightMm:F0} mm    " +
                $"Cables: {r.CableCount}    " +
                $"Fill: {(r.FillRatio * 100.0):F1}% / {(r.FillLimit * 100.0):F1}%  " +
                $"{(r.PassesLimit ? "PASS" : "OVERFILL")}";
            _header.Foreground = r.PassesLimit
                ? new SolidColorBrush(Color.FromRgb(80, 200, 80))
                : new SolidColorBrush(Color.FromRgb(240, 90, 90));

            if (r.InnerWidthMm <= 0 || r.InnerHeightMm <= 0) return;

            double availableW = _canvas.ActualWidth  <= 0 ? Width  - 32 : _canvas.ActualWidth  - 32;
            double availableH = _canvas.ActualHeight <= 0 ? Height - 96 : _canvas.ActualHeight - 32;
            double scaleX = availableW / r.InnerWidthMm;
            double scaleY = availableH / r.InnerHeightMm;
            double scale  = Math.Min(scaleX, scaleY);

            double drawnW = r.InnerWidthMm  * scale;
            double drawnH = r.InnerHeightMm * scale;
            double ox = (availableW - drawnW) * 0.5 + 16;
            double oy = (availableH - drawnH) * 0.5 + 16;

            // Tray outline
            Shape outline = r.TrayKind == "CONDUIT"
                ? (Shape)new Ellipse { Width = drawnW, Height = drawnH }
                : new Rectangle       { Width = drawnW, Height = drawnH };
            outline.Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 210));
            outline.StrokeThickness = 2;
            Canvas.SetLeft(outline, ox);
            Canvas.SetTop(outline,  oy);
            _canvas.Children.Add(outline);

            // Cables — simple row-packing left-to-right, bottom-to-top.
            double cursorX = ox + 4;
            double cursorY = oy + drawnH - 4;
            double rowMaxH = 0;
            foreach (var e in r.Cables)
            {
                double dMm  = e.Cable.OuterDiameterMm > 0 ? e.Cable.OuterDiameterMm : EstimateOd(e.Cable);
                double d    = dMm * scale;
                if (cursorX + d > ox + drawnW - 4)
                {
                    cursorX = ox + 4;
                    cursorY -= rowMaxH + 1;
                    rowMaxH = 0;
                }
                var col = ColorForClass(e.Cable.SegregationClass);
                var c = new Ellipse
                {
                    Width = d, Height = d,
                    Fill = new SolidColorBrush(col),
                    Stroke = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                    StrokeThickness = 0.5,
                    ToolTip = $"#{e.Cable.SequenceNumber} {e.Cable.CsaMm2:F1}×{e.Cable.CoreCount} " +
                              $"{e.Cable.ConductorMaterial}/{e.Cable.InsulationType} {e.Cable.SegregationClass}"
                };
                Canvas.SetLeft(c, cursorX);
                Canvas.SetTop(c,  cursorY - d);
                _canvas.Children.Add(c);
                cursorX += d + 1;
                if (d > rowMaxH) rowMaxH = d;
            }
        }

        private static Color ColorForClass(string cls)
        {
            return cls?.ToUpperInvariant() switch
            {
                "POWER"   => Color.FromRgb(220, 100, 100),
                "UTP"     => Color.FromRgb(100, 160, 240),
                "FTP"     => Color.FromRgb(100, 200, 200),
                "SFTP"    => Color.FromRgb(120, 220, 180),
                "SWA"     => Color.FromRgb(160, 160, 180),
                "FIRE"    => Color.FromRgb(240, 170, 80),
                _         => Color.FromRgb(200, 200, 200)
            };
        }

        private static double EstimateOd(StingCable c)
        {
            if (c == null || c.CsaMm2 <= 0) return 5.0;
            double conductor = Math.Sqrt(4 * c.CsaMm2 / Math.PI);
            double exp = c.InsulationType == "XLPE" ? 1.7 : 1.9;
            return conductor * exp + (c.CoreCount > 1 ? 2.0 : 0);
        }
    }
}
