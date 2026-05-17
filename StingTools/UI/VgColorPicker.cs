// StingTools — Drawing Template Manager · Phase 137
//
// VgColorPicker — thin WPF wrapper over the Windows ColorDialog so cells
// in the inline RevitVgEditor open the native Windows color picker
// (which is what Revit's own VG dialog uses) when the user clicks a
// color swatch. Returns null when the user cancels OR clicks the
// "No Override" affordance, which the editor interprets as "clear
// this override".
//
// Lives in StingTools.UI alongside the other editor-only helpers.

using SwfColorDialog = System.Windows.Forms.ColorDialog;
using SwfColorDialog = System.Windows.Forms.ColorDialog;
using StingTools.Core;
namespace StingTools.UI
{
    public static class VgColorPicker
    {
        /// <summary>
        /// Returns "" to clear the override (No Override picked), null to
        /// cancel and leave the value alone, or a "#RRGGBB" hex string
        /// when the user picks a colour.
        /// </summary>
        public static string Pick(string currentHex)
        {
            // Custom layered dialog: a small WPF window hosting our own
            // "No Override / Pick…" surface, so we get parity with Revit's
            // "<No Override>" affordance the standard ColorDialog lacks.
            var win = new Window
            {
                Title = "Colour",
                Width = 320, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            var stack = new StackPanel { Margin = new Thickness(12) };

            var swatch = new Border
            {
                Width = 60, Height = 24, Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
            };
            swatch.Background = HexToBrush(currentHex);

            var lblCurrent = new TextBlock { Text = "Current: " + (string.IsNullOrEmpty(currentHex) ? "<No Override>" : currentHex), Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(lblCurrent);
            stack.Children.Add(swatch);

            string result = null;
            bool resolved = false;

            var btnPick = new Button { Content = "Pick Colour…", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(8, 4, 8, 4) };
            btnPick.Click += (s, e) =>
            {
                using (var dlg = new SwfColorDialog())
                {
                    dlg.AnyColor = true;
                    dlg.FullOpen = true;
                    if (TryParseHex(currentHex, out byte r0, out byte g0, out byte b0))
                        dlg.Color = System.Drawing.Color.FromArgb(r0, g0, b0);
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        result   = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                        resolved = true;
                        win.Close();
                    }
                }
            };
            stack.Children.Add(btnPick);

            var btnNoOv = new Button { Content = "<No Override>", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(8, 4, 8, 4) };
            btnNoOv.Click += (s, e) => { result = ""; resolved = true; win.Close(); };
            stack.Children.Add(btnNoOv);

            var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(8, 4, 8, 4) };
            btnCancel.Click += (s, e) => { result = null; resolved = false; win.Close(); };
            stack.Children.Add(btnCancel);

            win.Content = stack;
            try { win.Owner = Application.Current?.MainWindow; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            win.ShowDialog();
            return resolved ? result : null;
        }

        public static Brush HexToBrush(string hex)
        {
            if (TryParseHex(hex, out byte r, out byte g, out byte b))
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            // Hatched grey for "no override"
            var dg = new DrawingGroup();
            using (var dc = dg.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 245, 245)), null, new Rect(0, 0, 8, 8));
                var stripe = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                dc.DrawLine(new Pen(stripe, 1), new Point(0, 8), new Point(8, 0));
            }
            return new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        }

        public static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var s = hex.TrimStart('#');
            if (s.Length != 6) return false;
            try
            {
                r = byte.Parse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                g = byte.Parse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                b = byte.Parse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
