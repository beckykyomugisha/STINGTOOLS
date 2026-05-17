using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

// Phase 165 follow-up — WPF/Revit type-name aliases. Autodesk.Revit.DB
// ships its own Color, Ellipse, and Visibility types that collide with
// the WPF equivalents used throughout this panel. Alias the WPF types
// so unqualified Color / Ellipse / Visibility references compile.
using Color      = System.Windows.Media.Color;
using Ellipse    = System.Windows.Shapes.Ellipse;
using Visibility = System.Windows.Visibility;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>
    /// Compact folder-health view (also usable as a small dialog window).
    /// Shows per-folder status with file counts and last-modified dates.
    /// </summary>
    public class FolderHealthPanel : UserControl
    {
        // Corporate palette — matches ThemeManager "Corporate" + StingTools brand
        private static readonly SolidColorBrush BgDark   = new(Color.FromRgb(0xFA, 0xFA, 0xFA));  // page bg
        private static readonly SolidColorBrush BgPanel  = new(Color.FromRgb(0xEC, 0xEF, 0xF1));  // header / read-only panel
        private static readonly SolidColorBrush BgRow    = new(Color.FromRgb(0xFF, 0xFF, 0xFF));  // row bg
        private static readonly SolidColorBrush FgWhite  = new(Color.FromRgb(0x37, 0x47, 0x4F));  // body text (slate)
        private static readonly SolidColorBrush FgSubtle = new(Color.FromRgb(0x60, 0x7D, 0x8B));  // muted slate
        private static readonly SolidColorBrush Accent   = new(Color.FromRgb(0x1A, 0x23, 0x7E));  // STING navy
        private static readonly SolidColorBrush BrBorder = new(Color.FromRgb(0xCF, 0xD8, 0xDC));  // light slate
        private static readonly SolidColorBrush Green    = new(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush Amber    = new(Color.FromRgb(0xE6, 0x51, 0x00));
        private static readonly SolidColorBrush Red      = new(Color.FromRgb(0xB7, 0x1C, 0x1C));

        private readonly UIApplication _uiapp;
        private TextBlock _header;
        private StackPanel _list;
        private TextBlock _statusBar;

        public FolderHealthPanel(UIApplication uiapp)
        {
            _uiapp = uiapp;
            Background = BgDark;
            Foreground = FgWhite;
            Build();
            Refresh(uiapp?.ActiveUIDocument?.Document);
        }

        private void Build()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(8) };

            // Header
            var headerDock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            _header = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = FgWhite,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var openBtn = new Button
            {
                Content = "▷ Open in Explorer",
                Width = 130,
                Height = 24,
                Background = BgRow,
                Foreground = Accent,
                BorderBrush = BrBorder,
                FontSize = 11,
            };
            openBtn.Click += (s, e) => OpenRoot();
            DockPanel.SetDock(openBtn, Dock.Right);
            headerDock.Children.Add(openBtn);
            headerDock.Children.Add(_header);
            DockPanel.SetDock(headerDock, Dock.Top);
            root.Children.Add(headerDock);

            // Status bar (bottom)
            _statusBar = new TextBlock
            {
                Foreground = FgSubtle,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            DockPanel.SetDock(_statusBar, Dock.Bottom);
            root.Children.Add(_statusBar);

            // Refresh button
            var refresh = new Button
            {
                Content = "↻ Refresh",
                Width = 90,
                Height = 24,
                Background = BgRow,
                Foreground = FgWhite,
                BorderBrush = BrBorder,
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            refresh.Click += (s, e) => Refresh(_uiapp?.ActiveUIDocument?.Document);
            DockPanel.SetDock(refresh, Dock.Bottom);
            root.Children.Add(refresh);

            // List
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = BgPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
            };
            _list = new StackPanel { Margin = new Thickness(2) };
            scroller.Content = _list;
            root.Children.Add(scroller);

            Content = root;
        }

        private void OpenRoot()
        {
            try
            {
                var doc = _uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                string root = ProjectFolderEngine.GetRootPath(doc);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
            }
            catch (Exception ex) { StingLog.Warn($"FolderHealthPanel.OpenRoot: {ex.Message}"); }
        }

        public void Refresh(Document doc)
        {
            try
            {
                _list.Children.Clear();
                if (doc == null)
                {
                    _header.Text = "(no document open)";
                    _statusBar.Text = "";
                    return;
                }

                string code = ProjectFolderEngine.DetectProjectCode(doc);
                string root = ProjectFolderEngine.GetRootPath(doc);
                _header.Text = $"Project Folder: {code}\\  →  {root}";

                var entries = ProjectFolderEngine.GetFolderHealth(doc);
                int totalFiles = 0, emptyCount = 0, activeCount = 0;
                foreach (var entry in entries)
                {
                    _list.Children.Add(BuildRow(entry));
                    if (entry.Exists) activeCount++;
                    if (entry.IsEmpty) emptyCount++;
                    totalFiles += entry.FileCount;
                }

                int dataFiles = 0;
                try
                {
                    string dataDir = ProjectFolderEngine.GetDataPath(doc);
                    if (!string.IsNullOrEmpty(dataDir) && Directory.Exists(dataDir))
                        dataFiles = Directory.GetFiles(dataDir, "*.json", SearchOption.AllDirectories).Length;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                _statusBar.Text = $"{totalFiles} files across {activeCount} folders   |   " +
                                  $"{emptyCount} empty   |   _data: {dataFiles} JSON files";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FolderHealthPanel.Refresh: {ex.Message}");
                _statusBar.Text = $"Error: {ex.Message}";
            }
        }

        private UIElement BuildRow(ProjectFolderEngine.FolderHealthEntry entry)
        {
            var dock = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 1, 0, 1),
                Background = BgRow,
            };

            // Status pill
            SolidColorBrush pillColor = !entry.Exists ? Red : (entry.IsEmpty ? Amber : Green);
            var pill = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = pillColor,
                Margin = new Thickness(6, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dock.Children.Add(pill);

            // Open button
            var openBtn = new Button
            {
                Content = "▷",
                Width = 26,
                Height = 22,
                Background = BgRow,
                Foreground = Accent,
                BorderBrush = BrBorder,
                FontSize = 11,
                ToolTip = "Open in Explorer",
            };
            openBtn.Click += (s, e) =>
            {
                try
                {
                    if (Directory.Exists(entry.FullPath))
                        Process.Start(new ProcessStartInfo { FileName = entry.FullPath, UseShellExecute = true });
                }
                catch (Exception ex) { StingLog.Warn($"FolderHealthPanel row open: {ex.Message}"); }
            };
            DockPanel.SetDock(openBtn, Dock.Right);
            dock.Children.Add(openBtn);

            // Last modified
            string lm = entry.LastModified.HasValue
                ? entry.LastModified.Value.ToString("yyyy-MM-dd")
                : "—";
            var dateBlock = new TextBlock
            {
                Text = lm,
                Foreground = FgSubtle,
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
            DockPanel.SetDock(dateBlock, Dock.Right);
            dock.Children.Add(dateBlock);

            // Count
            var countBlock = new TextBlock
            {
                Text = entry.Exists ? $"{entry.FileCount} files" : "(missing)",
                Foreground = entry.Exists ? FgWhite : Red,
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
            DockPanel.SetDock(countBlock, Dock.Right);
            dock.Children.Add(countBlock);

            // Name
            var nameBlock = new TextBlock
            {
                Text = entry.DisplayName,
                Foreground = FgWhite,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4),
            };
            dock.Children.Add(nameBlock);

            return dock;
        }

        // ── Dialog wrapper ─────────────────────────────────────────────────

        /// <summary>Show this panel in a small modal dialog window.</summary>
        public static void ShowDialog(UIApplication uiapp)
        {
            try
            {
                var panel = new FolderHealthPanel(uiapp);
                var w = new Window
                {
                    Title = "Folder Health",
                    Width = 520,
                    Height = 460,
                    Background = BgDark,
                    Foreground = FgWhite,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = panel,
                };
                w.ShowDialog();
            }
            catch (Exception ex) { StingLog.Warn($"FolderHealthPanel.ShowDialog: {ex.Message}"); }
        }
    }

}
