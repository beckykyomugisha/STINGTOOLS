// StingTools — Drawing Template Manager
//
// DrawingPreflightDialog is the human gate between batch generation
// and the batch actually running. Given a list of (label →
// DrawingType) intents — e.g. "A-01 Level 01 Plan" → arch-plan-A1-1to100
// — the dialog:
//
// 1. Groups them by DrawingType id to avoid showing 40 near-identical
//    rows for the same profile.
// 2. Displays a tile per distinct DrawingType with its scale, paper
//    size, title block status, view template status, and the count of
//    sheets that will use it.
// 3. Runs DrawingTypeValidator on each profile and flags Errors /
//    Warnings inline so the user sees "this will fail / this might
//    look odd" before pressing Generate.
// 4. Requires an explicit "Proceed" click to return true. Cancel /
//    close returns false so the batch command treats it as abort.
//
// No actual thumbnail rendering in Phase III (that requires a Revit
// image export pass that must run on the API thread); the tile
// metadata alone catches 90% of wrong-template / missing-asset
// errors. Thumbnail rendering is a Phase IV polish.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

// WPF + Revit both declare Color / Rectangle / Grid — disambiguate once so
// CS0104 does not fire on every reference. UI code here always wants
// the WPF types.
using Color     = System.Windows.Media.Color;
using Colors    = System.Windows.Media.Colors;
using Rectangle = System.Windows.Shapes.Rectangle;
using Grid      = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    public sealed class DrawingPreflightIntent
    {
        public string Label { get; set; }         // e.g. "A-01 Level 01 Plan"
        public DrawingType DrawingType { get; set; }
    }

    public sealed class DrawingPreflightDialog : Window
    {
        private static readonly Color BgColor     = Color.FromRgb(0x2D, 0x2D, 0x30);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg      = Color.FromRgb(0x3E, 0x3E, 0x42);
        private static readonly Color CardBorder  = Color.FromRgb(0x55, 0x55, 0x58);
        private static readonly Color FgColor     = Colors.White;
        private static readonly Color SubtleColor = Color.FromRgb(0xAA, 0xAA, 0xAA);
        private static readonly Color ErrorColor  = Color.FromRgb(0xE5, 0x5B, 0x3C);
        private static readonly Color WarnColor   = Color.FromRgb(0xE8, 0xC8, 0x4A);
        private static readonly Color OkColor     = Color.FromRgb(0x6F, 0xBE, 0x6F);

        public bool Proceed { get; private set; }

        private readonly Document _doc;
        private readonly List<DrawingPreflightIntent> _intents;

        public DrawingPreflightDialog(Document doc, List<DrawingPreflightIntent> intents)
        {
            _doc = doc;
            _intents = intents ?? new List<DrawingPreflightIntent>();

            Title = "STING — Drawing Preflight";
            Width = 780; Height = 560;
            MinWidth = 640; MinHeight = 400;
            Background = new SolidColorBrush(BgColor);
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }

            Content = BuildLayout();
        }

        // ────────────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(14), LastChildFill = true };

            // Header
            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = "Drawing Preflight",
                FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 0, 0, 4),
            });
            var byType = GroupByDrawingType();
            header.Children.Add(new TextBlock
            {
                Text = $"{_intents.Count} drawing(s) will be produced using {byType.Count} profile(s). " +
                       "Review missing assets below; Proceed when ready.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Buttons footer
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var btnCancel = MakeButton("Cancel", CardBg, false);
            btnCancel.Click += (s, e) => { Proceed = false; DialogResult = false; };
            var btnProceed = MakeButton("Proceed →", AccentColor, true);
            btnProceed.Click += (s, e) => { Proceed = true; DialogResult = true; };
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnProceed);
            DockPanel.SetDock(btnRow, Dock.Bottom);
            root.Children.Add(btnRow);

            // Tile grid
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var grp in byType.OrderBy(g => g.Key?.Discipline).ThenBy(g => g.Key?.Purpose))
                tiles.Children.Add(BuildTile(grp.Key, grp.Value));
            scroll.Content = tiles;
            root.Children.Add(scroll);

            return root;
        }

        private Dictionary<DrawingType, List<DrawingPreflightIntent>> GroupByDrawingType()
        {
            var map = new Dictionary<DrawingType, List<DrawingPreflightIntent>>();
            foreach (var it in _intents)
            {
                if (it?.DrawingType == null) continue;
                if (!map.TryGetValue(it.DrawingType, out var list))
                {
                    list = new List<DrawingPreflightIntent>();
                    map[it.DrawingType] = list;
                }
                list.Add(it);
            }
            return map;
        }

        private UIElement BuildTile(DrawingType dt, List<DrawingPreflightIntent> usingIt)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 410, MinHeight = 190,
                Padding = new Thickness(10, 8, 10, 8),
            };

            // Two-column layout: thumbnail left | info right ------------
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Thumbnail box — vector preview by default, swapped for
            // a real Revit ImageExport when the service callback fires.
            var thumbHost = new Border
            {
                Background = new SolidColorBrush(BgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Width = 150, Height = 170,
                Margin = new Thickness(0, 0, 8, 0),
            };
            thumbHost.Child = BuildVectorLayoutPreview(dt);
            Grid.SetColumn(thumbHost, 0);
            root.Children.Add(thumbHost);

            // Request a real Revit-rendered thumbnail — fires async on
            // the API thread. Callback swaps the vector Canvas for an
            // Image once a PNG is ready. Falls through silently when no
            // matching sheet exists in the project.
            DrawingThumbnailService.Instance.Enqueue(new DrawingThumbnailService.Request
            {
                DrawingType = dt,
                OnComplete = bmp =>
                {
                    if (bmp == null) return; // keep vector preview
                    thumbHost.Child = new Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(2),
                    };
                },
            });

            // Info column -----------------------------------------------
            var info = new StackPanel();
            Grid.SetColumn(info, 1);
            info.Children.Add(new TextBlock
            {
                Text = dt.Name ?? dt.Id, FontWeight = FontWeights.SemiBold, FontSize = 13,
                Foreground = new SolidColorBrush(FgColor),
                TextWrapping = TextWrapping.Wrap,
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{dt.Discipline} · {dt.Purpose} · {dt.PaperSize} · 1:{dt.Scale} · {dt.DetailLevel ?? "Medium"}",
                FontSize = 11, Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 2, 0, 8),
            });
            info.Children.Add(new TextBlock
            {
                Text = $"Sheets using this profile: {usingIt.Count}",
                FontSize = 11, Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 0, 0, 4),
            });

            // Validator output — turn missing assets into visible flags
            var report = DrawingTypeValidator.Validate(_doc, dt);
            foreach (var issue in report.Issues)
            {
                var color = issue.Severity == ValidationSeverity.Error   ? ErrorColor
                          : issue.Severity == ValidationSeverity.Warning ? WarnColor
                                                                         : SubtleColor;
                info.Children.Add(new TextBlock
                {
                    Text = $"● {issue.Severity,-7} {issue.Code}  {issue.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(color),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            if (report.Issues.Count == 0)
            {
                info.Children.Add(new TextBlock
                {
                    Text = "● OK — all referenced assets present.",
                    Foreground = new SolidColorBrush(OkColor),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            root.Children.Add(info);
            card.Child = root;
            return card;
        }

        /// <summary>
        /// Paint a paper-proportioned rectangle with the DrawingType's
        /// slot layout as inner rectangles. Zero Revit calls — always
        /// renders, even when the real sheet export fails or the project
        /// hasn't produced a sheet using this profile yet.
        /// </summary>
        private Canvas BuildVectorLayoutPreview(DrawingType dt)
        {
            var canvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF0)), // paper white-ish
                Width = 148, Height = 168,
                ClipToBounds = true,
            };

            // Paper proportion — A1/A2/A3 landscape default ~ 1.414:1
            double aspect = IsPortrait(dt) ? 1.0 / 1.414 : 1.414;
            double paperH = 155, paperW = paperH * aspect;
            if (paperW > 148) { paperW = 146; paperH = paperW / aspect; }
            double x0 = (148 - paperW) / 2.0, y0 = (168 - paperH) / 2.0;

            // Paper sheet
            var paper = new Rectangle
            {
                Width = paperW, Height = paperH,
                Fill = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF3)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(paper, x0); Canvas.SetTop(paper, y0);
            canvas.Children.Add(paper);

            // Title block strip along bottom (~8% of height)
            double tbH = paperH * 0.08;
            var tb = new Rectangle
            {
                Width = paperW, Height = tbH,
                Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xD8)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                StrokeThickness = 0.5,
            };
            Canvas.SetLeft(tb, x0); Canvas.SetTop(tb, y0 + paperH - tbH);
            canvas.Children.Add(tb);

            // Draw slots. Normalised 0..1 in DrawingSlot map onto the
            // drawable zone, which excludes the title-block strip.
            double zoneX = x0, zoneY = y0, zoneW = paperW, zoneH = paperH - tbH;
            if (dt.Slots != null)
            {
                foreach (var slot in dt.Slots)
                {
                    var rect = new Rectangle
                    {
                        Width  = Math.Max(4, slot.NormW * zoneW),
                        Height = Math.Max(4, slot.NormH * zoneH),
                        Fill   = new SolidColorBrush(Color.FromArgb(0x30, 0xE8, 0x91, 0x2D)),
                        Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
                        StrokeThickness = 0.7,
                    };
                    Canvas.SetLeft(rect, zoneX + slot.NormX * zoneW);
                    // DrawingSlot Y=0 is conventionally bottom; WPF Y=0
                    // is top, so flip before adding.
                    Canvas.SetTop(rect, zoneY + (1 - slot.NormY - slot.NormH) * zoneH);
                    canvas.Children.Add(rect);
                }
            }

            // Scale caption in the title block strip
            var caption = new TextBlock
            {
                Text = $"{dt.PaperSize}  1:{dt.Scale}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            };
            Canvas.SetLeft(caption, x0 + 4);
            Canvas.SetTop(caption, y0 + paperH - tbH + 2);
            canvas.Children.Add(caption);

            return canvas;
        }

        private static bool IsPortrait(DrawingType dt)
            => !string.IsNullOrEmpty(dt?.Orientation)
               && dt.Orientation.Trim().Equals("Portrait", StringComparison.OrdinalIgnoreCase);

        private Button MakeButton(string label, Color bg, bool isDefault)
        {
            var b = new Button
            {
                Content = label,
                Width = 100, Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(FgColor),
                BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault,
                IsCancel = !isDefault,
            };
            return b;
        }

        /// <summary>
        /// Helper that batch commands can call in one line:
        ///   if (!DrawingPreflightDialog.Gate(doc, intents)) return Result.Cancelled;
        /// </summary>
        public static bool Gate(Document doc, List<DrawingPreflightIntent> intents)
        {
            if (intents == null || intents.Count == 0) return true;
            var dlg = new DrawingPreflightDialog(doc, intents);
            var ok = dlg.ShowDialog();
            return ok == true && dlg.Proceed;
        }
    }
}
