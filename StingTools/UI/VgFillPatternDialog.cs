// StingTools — Drawing Template Manager · Phase 137
//
// VgFillPatternDialog — replicates Revit's native "Fill Pattern
// Graphics" popup that opens when a user clicks Override… inside the
// VG Patterns column (under Projection/Surface or Cut). Bundles:
//
//   Pattern Overrides
//     Foreground · Visible CheckBox · Pattern ComboBox + "…" · Color
//     Background · Visible CheckBox · Pattern ComboBox + "…" · Color
//
//   Plus Clear Overrides + OK + Cancel.
//
// Reads project FillPatternElement names live from the document (the
// caller passes the list). Colour cells route through VgColorPicker.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using SwBrush = System.Windows.Media.Brushes;
using StingTools.Core;

namespace StingTools.UI
{
    public sealed class VgFillPattern
    {
        public bool? FgVisible { get; set; }
        public string FgPattern { get; set; }
        public string FgColor   { get; set; }
        public bool? BgVisible { get; set; }
        public string BgPattern { get; set; }
        public string BgColor   { get; set; }
        public bool Cleared { get; set; }
    }

    public static class VgFillPatternDialog
    {
        public static VgFillPattern Show(
            VgFillPattern current,
            IList<string> patternOptions,
            string title = "Fill Pattern Graphics",
            Func<IList<string>> refreshFromDoc = null)
        {
            current = current ?? new VgFillPattern();
            var win = new Window
            {
                Title = title,
                Width = 460, Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };

            // ── Pattern Overrides group ──
            var hdr = new TextBlock { Text = "Pattern Overrides", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(hdr);

            // Per-section comboboxes captured so the Refresh button can
            // repopulate both Fg + Bg without recreating the dialog.
            ComboBox fgCombo = null, bgCombo = null;

            // Foreground
            var fgSection = BuildSection("Foreground", current.FgVisible ?? true,
                current.FgPattern, current.FgColor, patternOptions, refreshFromDoc,
                (vis, pat, col) => { current.FgVisible = vis; current.FgPattern = pat; current.FgColor = col; },
                cb => fgCombo = cb);
            root.Children.Add(fgSection);

            // Background
            var bgSection = BuildSection("Background", current.BgVisible ?? true,
                current.BgPattern, current.BgColor, patternOptions, refreshFromDoc,
                (vis, pat, col) => { current.BgVisible = vis; current.BgPattern = pat; current.BgColor = col; },
                cb => bgCombo = cb);
            root.Children.Add(bgSection);

            // Phase 183 — Refresh button. Re-reads FillPatternElement names
            // from the live document so newly-authored patterns appear
            // without closing the editor. No-op when caller didn't supply
            // a refresh callback (legacy invocations).
            if (refreshFromDoc != null)
            {
                var refreshRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var btnRefresh = new Button
                {
                    Content = "↻ Refresh patterns from project",
                    Padding = new Thickness(8, 3, 8, 3),
                    ToolTip = "Re-scan FillPatternElements after authoring a new pattern via Revit Manage > Additional Settings > Fill Patterns."
                };
                btnRefresh.Click += (s, e) =>
                {
                    var fresh = refreshFromDoc();
                    if (fresh == null) return;
                    RepopulateCombo(fgCombo, fresh, current.FgPattern);
                    RepopulateCombo(bgCombo, fresh, current.BgPattern);
                };
                refreshRow.Children.Add(btnRefresh);
                root.Children.Add(refreshRow);
            }

            // "How do these settings affect view graphics?" hint link
            var link = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
            var hyper = new Hyperlink(new Run("How do these settings affect view graphics?"))
            {
                NavigateUri = new Uri("https://help.autodesk.com/view/RVT/2025/ENU/?guid=GUID-VG-Overrides")
            };
            hyper.RequestNavigate += (s, e) =>
            {
                Autodesk.Revit.UI.TaskDialog.Show("Fill Pattern Graphics",
                    "Foreground = the pattern drawn over the surface (typical hatching).\n" +
                    "Background = the colour fill behind the foreground pattern.\n" +
                    "Visible toggles each layer on/off without losing your settings.\n" +
                    "Click '…' next to a Pattern dropdown to open Revit's Fill Patterns dialog (Manage > Additional Settings).");
                e.Handled = true;
            };
            link.Inlines.Add(hyper);
            root.Children.Add(link);

            // ── Buttons ──
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var btnClear = new Button { Content = "Clear Overrides", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
            var btnOk    = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(20, 4, 20, 4), MinWidth = 80 };
            var btnCxl   = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(20, 4, 20, 4), MinWidth = 80 };
            btnRow.Children.Add(btnClear);
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCxl);
            root.Children.Add(btnRow);

            VgFillPattern result = null;
            btnClear.Click += (s, e) => { result = new VgFillPattern { Cleared = true }; win.DialogResult = true; win.Close(); };
            btnOk.Click    += (s, e) => { result = current; win.DialogResult = true; win.Close(); };

            win.Content = root;
            try { win.Owner = Application.Current?.MainWindow; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            win.ShowDialog();
            return result;
        }

        // Builds one Foreground/Background row group with Visible checkbox,
        // Pattern combo + "…" picker hint button, and Colour swatch button.
        private static FrameworkElement BuildSection(
            string label, bool visible, string pattern, string colorHex,
            IList<string> patternOptions,
            Func<IList<string>> refreshFromDoc,
            Action<bool, string, string> onChange,
            Action<ComboBox> exposeCombo = null)
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            // Header row: label + Visible
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            headerRow.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
            var cbVis = new CheckBox { Content = "Visible", IsChecked = visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            headerRow.Children.Add(cbVis);
            section.Children.Add(headerRow);

            // Pattern row
            var patternRow = new Grid();
            patternRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            patternRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            patternRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var lblP = new TextBlock { Text = "Pattern:", VerticalAlignment = VerticalAlignment.Center };
            var cbP  = new ComboBox { Margin = new Thickness(0, 2, 0, 2), IsEditable = false };
            cbP.Items.Add("<No Override>");
            if (patternOptions != null) foreach (var p in patternOptions)
                if (!string.IsNullOrEmpty(p) && !cbP.Items.Contains(p) && p != "<no override>" && p != "<Solid fill>") cbP.Items.Add(p);
            cbP.Items.Add("<Solid fill>");
            cbP.SelectedItem = string.IsNullOrEmpty(pattern) ? "<No Override>" : (cbP.Items.Contains(pattern) ? pattern : "<No Override>");
            var btnPM = new Button
            {
                Content = "…",
                Width = 24, Height = 24,
                ToolTip = refreshFromDoc != null
                    ? "Author a new pattern via Revit Manage > Additional Settings > Fill Patterns, then click Refresh below."
                    : "Open Revit's Fill Patterns dialog (Manage > Additional Settings)"
            };
            btnPM.Click += (s, e) =>
            {
                var msg = "Use Revit Manage tab → Additional Settings → Fill Patterns to author and edit fill patterns.";
                if (refreshFromDoc != null)
                    msg += "\n\nAfter saving the new pattern, click ↻ Refresh patterns from project below to repopulate this dropdown without closing the editor.";
                else
                    msg += "\n\nNewly added patterns appear in this dropdown after you re-open the editor.";
                Autodesk.Revit.UI.TaskDialog.Show("Fill Patterns", msg);
            };
            Grid.SetColumn(lblP, 0); Grid.SetColumn(cbP, 1); Grid.SetColumn(btnPM, 2);
            patternRow.Children.Add(lblP); patternRow.Children.Add(cbP); patternRow.Children.Add(btnPM);
            section.Children.Add(patternRow);
            exposeCombo?.Invoke(cbP);

            // Colour row
            var colorRow = new Grid();
            colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lblC = new TextBlock { Text = "Color:", VerticalAlignment = VerticalAlignment.Center };
            var btnC = new Button { Margin = new Thickness(0, 2, 0, 2), Height = 26, HorizontalContentAlignment = HorizontalAlignment.Left };
            string currentColor = colorHex;
            UpdateColorButtonContent(btnC, currentColor);
            btnC.Click += (s, e) =>
            {
                var picked = VgColorPicker.Pick(currentColor);
                if (picked == null) return;
                currentColor = picked;
                UpdateColorButtonContent(btnC, currentColor);
                onChange?.Invoke(cbVis.IsChecked == true,
                                 (cbP.SelectedItem as string) == "<No Override>" ? null : cbP.SelectedItem as string,
                                 currentColor);
            };
            Grid.SetColumn(lblC, 0); Grid.SetColumn(btnC, 1);
            colorRow.Children.Add(lblC); colorRow.Children.Add(btnC);
            section.Children.Add(colorRow);

            // Wire change callbacks
            cbVis.Checked   += (s, e) => onChange?.Invoke(true,  StripSentinel(cbP.SelectedItem as string), currentColor);
            cbVis.Unchecked += (s, e) => onChange?.Invoke(false, StripSentinel(cbP.SelectedItem as string), currentColor);
            cbP.SelectionChanged += (s, e) => onChange?.Invoke(cbVis.IsChecked == true,
                                                               StripSentinel(cbP.SelectedItem as string), currentColor);

            return section;
        }

        private static string StripSentinel(string s)
            => (s == "<No Override>" || string.IsNullOrEmpty(s)) ? null : s;

        private static void RepopulateCombo(ComboBox cb, IList<string> patterns, string preserveSelection)
        {
            if (cb == null) return;
            var keep = (cb.SelectedItem as string) ?? preserveSelection;
            cb.Items.Clear();
            cb.Items.Add("<No Override>");
            if (patterns != null) foreach (var p in patterns)
                if (!string.IsNullOrEmpty(p) && !cb.Items.Contains(p)
                    && p != "<no override>" && p != "<Solid fill>") cb.Items.Add(p);
            cb.Items.Add("<Solid fill>");
            cb.SelectedItem = string.IsNullOrEmpty(keep) ? "<No Override>"
                : (cb.Items.Contains(keep) ? keep : "<No Override>");
        }

        private static void UpdateColorButtonContent(Button btn, string hex)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 24, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                Background = VgColorPicker.HexToBrush(hex),
                BorderBrush = SwBrush.Gray, BorderThickness = new Thickness(1)
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
