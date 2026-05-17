// StingTools — Drawing Template Manager · Phase 137
//
// VgLineGraphicsDialog — replicates Revit's native "Line Graphics"
// popup that opens when a user clicks Override… inside the VG
// Lines column. Bundles three controls:
//
//   * Pattern  — ComboBox of LinePatternElement names (+ "Solid")
//   * Colour   — Button + sample swatch; opens VgColorPicker
//   * Weight   — ComboBox 1..16
//
// Plus Clear Overrides button + OK / Cancel.
//
// Static Show() factory takes the current values, returns the user's
// committed values (or null if Cancelled). Used by the inline VG
// editor's Lines / Cut Lines columns.

using System.Windows.Media;
using StingTools.Core;
namespace StingTools.UI
{
    public sealed class VgLineGraphics
    {
        public string Pattern { get; set; }      // null/empty = no override
        public string ColorHex { get; set; }     // null/empty = no override
        public int? Weight { get; set; }         // null = no override
        public bool Cleared { get; set; }        // true = Clear Overrides clicked
    }

    public static class VgLineGraphicsDialog
    {
        public static VgLineGraphics Show(
            VgLineGraphics current,
            IList<string> patternOptions,
            string title = "Line Graphics")
        {
            current = current ?? new VgLineGraphics();
            var win = new Window
            {
                Title = title,
                Width = 380, Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            for (int r = 0; r < 5; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new TextBlock { Text = "Lines", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(header, 0); Grid.SetColumn(header, 0); Grid.SetColumnSpan(header, 3);
            grid.Children.Add(header);

            // Pattern row
            var lblP = new TextBlock { Text = "Pattern:", VerticalAlignment = VerticalAlignment.Center };
            var cbP  = new ComboBox { Margin = new Thickness(4, 4, 4, 4), MinWidth = 200, IsEditable = false };
            cbP.Items.Add("<No Override>");
            if (patternOptions != null) foreach (var p in patternOptions)
                if (!string.IsNullOrEmpty(p) && !cbP.Items.Contains(p) && p != "<no override>") cbP.Items.Add(p);
            cbP.SelectedItem = string.IsNullOrEmpty(current.Pattern) ? "<No Override>" : current.Pattern;
            var btnPatternMgr = new Button { Content = "…", ToolTip = "Manage line patterns (Revit native)", Width = 24, Height = 24, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(lblP, 1); Grid.SetColumn(lblP, 0);
            Grid.SetRow(cbP, 1);  Grid.SetColumn(cbP, 1);
            Grid.SetRow(btnPatternMgr, 1); Grid.SetColumn(btnPatternMgr, 2);
            grid.Children.Add(lblP); grid.Children.Add(cbP); grid.Children.Add(btnPatternMgr);

            // Colour row
            var lblC = new TextBlock { Text = "Color:", VerticalAlignment = VerticalAlignment.Center };
            var btnC = new Button { Margin = new Thickness(4, 4, 4, 4), Height = 26, MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Left };
            UpdateColorButtonContent(btnC, current.ColorHex);
            btnC.Click += (s, e) =>
            {
                var picked = VgColorPicker.Pick(current.ColorHex);
                if (picked == null) return; // cancelled
                current.ColorHex = picked;
                UpdateColorButtonContent(btnC, current.ColorHex);
            };
            Grid.SetRow(lblC, 2); Grid.SetColumn(lblC, 0);
            Grid.SetRow(btnC, 2); Grid.SetColumn(btnC, 1); Grid.SetColumnSpan(btnC, 2);
            grid.Children.Add(lblC); grid.Children.Add(btnC);

            // Weight row
            var lblW = new TextBlock { Text = "Weight:", VerticalAlignment = VerticalAlignment.Center };
            var cbW  = new ComboBox { Margin = new Thickness(4, 4, 4, 4), MinWidth = 80, IsEditable = false };
            cbW.Items.Add("<No Override>");
            for (int i = 1; i <= 16; i++) cbW.Items.Add(i.ToString());
            cbW.SelectedItem = current.Weight.HasValue ? current.Weight.Value.ToString() : "<No Override>";
            Grid.SetRow(lblW, 3); Grid.SetColumn(lblW, 0);
            Grid.SetRow(cbW, 3);  Grid.SetColumn(cbW, 1); Grid.SetColumnSpan(cbW, 2);
            grid.Children.Add(lblW); grid.Children.Add(cbW);

            // Buttons row
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var btnClear = new Button { Content = "Clear Overrides", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
            var btnOk    = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(20, 4, 20, 4), MinWidth = 80 };
            var btnCxl   = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(20, 4, 20, 4), MinWidth = 80 };
            btnRow.Children.Add(btnClear);
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCxl);
            Grid.SetRow(btnRow, 4); Grid.SetColumn(btnRow, 0); Grid.SetColumnSpan(btnRow, 3);
            grid.Children.Add(btnRow);

            VgLineGraphics result = null;

            btnClear.Click += (s, e) =>
            {
                result = new VgLineGraphics { Cleared = true };
                win.DialogResult = true; win.Close();
            };
            btnOk.Click += (s, e) =>
            {
                result = new VgLineGraphics
                {
                    Pattern  = (cbP.SelectedItem as string) == "<No Override>" ? null : cbP.SelectedItem as string,
                    ColorHex = current.ColorHex,
                    Weight   = (cbW.SelectedItem as string) == "<No Override>" ? (int?)null : int.Parse(cbW.SelectedItem as string)
                };
                win.DialogResult = true; win.Close();
            };
            btnPatternMgr.Click += (s, e) =>
            {
                Autodesk.Revit.UI.TaskDialog.Show("Line Patterns",
                    "Use Revit Manage tab → Additional Settings → Line Patterns to author and edit line patterns. " +
                    "Newly added patterns appear in this dropdown after you re-open the editor.");
            };

            win.Content = grid;
            try { win.Owner = Application.Current?.MainWindow; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            win.ShowDialog();
            return result;
        }

        private static void UpdateColorButtonContent(Button btn, string hex)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 24, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                Background = VgColorPicker.HexToBrush(hex),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
            });
            sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(hex) ? "<No Override>" : hex,
                VerticalAlignment = VerticalAlignment.Center
            });
            btn.Content = sp;
        }
    }
}
