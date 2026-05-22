using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using StingTools.Core;
using Brush = System.Windows.Media.Brush;

namespace StingTools.UI
{
    /// <summary>
    /// Priority 4 + 5 — Inline pickers + confirmation cards.
    /// All popups are WPF Popups anchored to their trigger; no separate
    /// Window means no flicker, no taskbar entry, no modality.
    /// </summary>
    public static class MaterialHubFlyouts
    {
        // ── Texture picker ─────────────────────────────────────────────────
        public static void ShowTexturePicker(UIElement anchor, Document doc, Material mat,
            Action<string> onPicked)
        {
            var pop = NewPopup(anchor, 360, 320);
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock { Text = $"Texture for '{mat?.Name}'", FontWeight = FontWeights.SemiBold, FontSize = 11 });

            var current = mat == null ? "" : MaterialAppearanceActions.ReadCurrentTexturePath(doc, mat);
            sp.Children.Add(new TextBlock { Text = "Current: " + (string.IsNullOrEmpty(current) ? "(none)" : current),
                FontSize = 10, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });

            // Library thumbnails — scan Data/textures/ + project _BIM_COORD/textures/.
            var thumbs = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            try
            {
                foreach (var path in EnumerateTextures(doc).Take(40))
                {
                    var img = new Image { Width = 60, Height = 60, Margin = new Thickness(2), Stretch = Stretch.UniformToFill };
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 60;
                        bmp.EndInit();
                        bmp.Freeze();
                        img.Source = bmp;
                    }
                    catch { }
                    img.ToolTip = path;
                    img.Cursor = System.Windows.Input.Cursors.Hand;
                    string captured = path;
                    img.MouseLeftButtonDown += (_, __) => { onPicked?.Invoke(captured); pop.IsOpen = false; };
                    thumbs.Children.Add(img);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Texture thumbs: {ex.Message}"); }
            var sv = new ScrollViewer { Content = thumbs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 200 };
            sp.Children.Add(sv);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var browse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2) };
            browse.Click += (_, __) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tiff|All files|*.*" };
                if (ofd.ShowDialog() == true) { onPicked?.Invoke(ofd.FileName); pop.IsOpen = false; }
            };
            var clear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
            clear.Click += (_, __) => { onPicked?.Invoke(""); pop.IsOpen = false; };
            row.Children.Add(browse); row.Children.Add(clear);
            sp.Children.Add(row);

            pop.Child = Frame(sp);
            pop.IsOpen = true;
        }

        // ── Hatch pattern picker ───────────────────────────────────────────
        public static void ShowHatchPicker(UIElement anchor, Document doc, Material mat, string slot,
            Action<ElementId, string> onPicked)
        {
            var pop = NewPopup(anchor, 280, 360);
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock { Text = $"Hatch pattern → {slot}", FontWeight = FontWeights.SemiBold, FontSize = 11 });

            var search = new TextBox { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(4, 2, 4, 2) };
            sp.Children.Add(search);
            var list = new ListBox { Height = 240, FontSize = 11 };
            var all = MaterialAppearanceActions.ListSurfacePatterns(doc).ToList();
            void Repop(string filter)
            {
                list.Items.Clear();
                foreach (var fp in all)
                {
                    if (!string.IsNullOrEmpty(filter) && (fp.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    list.Items.Add(fp.Name);
                }
            }
            Repop("");
            search.TextChanged += (_, __) => Repop(search.Text);
            list.MouseDoubleClick += (_, __) =>
            {
                if (list.SelectedItem is string n)
                {
                    var hit = all.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) { onPicked?.Invoke(hit.Id, n); pop.IsOpen = false; }
                }
            };
            sp.Children.Add(list);
            pop.Child = Frame(sp);
            pop.IsOpen = true;
        }

        // ── Color picker (HSV-ish grid + recent strip) ─────────────────────
        public static void ShowColorPicker(UIElement anchor, Material currentMat, Action<byte, byte, byte> onPicked)
        {
            var pop = NewPopup(anchor, 260, 240);
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock { Text = "Pick a colour", FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });

            // Simple 8 × 6 swatch palette + recent strip.
            byte[] vals = new byte[] { 0x10, 0x40, 0x70, 0xA0, 0xD0, 0xF0 };
            var grid = new UniformGrid { Rows = 6, Columns = 8 };
            foreach (var r in vals)
                foreach (var g in vals)
                {
                    if (grid.Children.Count >= 48) break;
                    byte br = r, bg = g, bb = (byte)((r + g) / 2);
                    var sw = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(br, bg, bb)),
                        Width = 24, Height = 24, Margin = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                        BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1)
                    };
                    sw.MouseLeftButtonDown += (_, __) => { onPicked?.Invoke(br, bg, bb); pop.IsOpen = false; };
                    grid.Children.Add(sw);
                }
            sp.Children.Add(grid);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var tbR = new TextBox { Width = 36, Margin = new Thickness(0, 0, 4, 0) };
            var tbG = new TextBox { Width = 36, Margin = new Thickness(0, 0, 4, 0) };
            var tbB = new TextBox { Width = 36, Margin = new Thickness(0, 0, 4, 0) };
            var ok = new Button { Content = "Apply", Padding = new Thickness(8, 2, 8, 2) };
            ok.Click += (_, __) =>
            {
                if (byte.TryParse(tbR.Text, out byte rr) && byte.TryParse(tbG.Text, out byte gg) && byte.TryParse(tbB.Text, out byte bb))
                { onPicked?.Invoke(rr, gg, bb); pop.IsOpen = false; }
            };
            foreach (var c in new System.Windows.Controls.Control[] { tbR, tbG, tbB, ok }) row.Children.Add(c);
            sp.Children.Add(row);
            pop.Child = Frame(sp);
            pop.IsOpen = true;
        }

        // ── Inline gate-result confirmation card (priority 5) ──────────────
        public static void ShowResultCard(UIElement anchor, string title, string body,
            Action onPrimary = null, string primaryLabel = "OK", string severity = "info")
        {
            var pop = NewPopup(anchor, 360, 240);
            var sp = new StackPanel { Margin = new Thickness(8) };
            Color titleBg = severity switch
            {
                "error" => Color.FromRgb(0xC0, 0x30, 0x30),
                "warn"  => Color.FromRgb(0xE0, 0xA0, 0x10),
                "ok"    => Color.FromRgb(0x2C, 0xA0, 0x2C),
                _       => Color.FromRgb(0x3D, 0x6F, 0xBA),
            };
            sp.Children.Add(new Border
            {
                Background = new SolidColorBrush(titleBg),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold }
            });
            sp.Children.Add(new ScrollViewer
            {
                Content = new TextBlock { Text = body ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) },
                Height = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (onPrimary != null)
            {
                var b = new Button { Content = primaryLabel, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += (_, __) => { onPrimary(); pop.IsOpen = false; };
                row.Children.Add(b);
            }
            var dismiss = new Button { Content = "Dismiss", Padding = new Thickness(10, 3, 10, 3) };
            dismiss.Click += (_, __) => pop.IsOpen = false;
            row.Children.Add(dismiss);
            sp.Children.Add(row);
            pop.Child = Frame(sp);
            pop.IsOpen = true;
        }

        // ── helpers ─────────────────────────────────────────────────────────
        private static Popup NewPopup(UIElement anchor, double w, double h)
        {
            return new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Width = w, Height = h,
            };
        }

        private static Border Frame(UIElement child) =>
            new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2),
                SnapsToDevicePixels = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8, Opacity = 0.2, ShadowDepth = 2
                },
                Child = child,
            };

        private static IEnumerable<string> EnumerateTextures(Document doc)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Corporate library
            try
            {
                string dir = Path.Combine(StingToolsApp.DataPath ?? "", "textures");
                if (Directory.Exists(dir))
                    foreach (var p in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                        if (IsImage(p) && seen.Add(p)) yield return p;
            }
            catch { }
            // Project override
            string proj = null;
            try { proj = Path.Combine(Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "", "textures"); }
            catch { }
            if (!string.IsNullOrEmpty(proj))
            {
                IEnumerable<string> files = Array.Empty<string>();
                try { if (Directory.Exists(proj)) files = Directory.EnumerateFiles(proj, "*.*", SearchOption.AllDirectories); }
                catch { }
                foreach (var p in files)
                    if (IsImage(p) && seen.Add(p)) yield return p;
            }
        }

        private static bool IsImage(string p)
        {
            var ext = (Path.GetExtension(p) ?? "").ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tiff";
        }
    }
}
