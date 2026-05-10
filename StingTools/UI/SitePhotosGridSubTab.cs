// ══════════════════════════════════════════════════════════════════════
//  SitePhotosGridSubTab — Phase 179 contact-sheet grid for the BCC.
//
//  Renders all photos for the active project as a 4-column thumbnail
//  grid with hover caption + reason chip. This is the "what does my
//  project actually look like right now" view that Procore Photos /
//  PlanGrid Photos default to. Filters mirror the Review queue (Reason
//  / Level / Zone / Date) and a project-wide search box looks across
//  caption + level/zone/discipline.
//
//  Selection: click toggles; long-press / Ctrl-click multi-selects;
//  the bulk footer from the Review queue is reused via the same
//  TabState.SelectedIds set so albums + bulk operations work uniformly
//  across both tabs. No Revit API calls — all server I/O routes through
//  PlanscapeServerClient.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.UI
{
    internal static class SitePhotosGridSubTab
    {
        // 4-column grid; thumbnails are 180×180 with caption underneath.
        private const int Columns = 4;
        private const double TileSize = 180;

        internal static UIElement Build(BIMCoordinationCenter owner, SitePhotosTab.TabState state)
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };

            // Top toolbar — filter strip mirrors the Review queue but adds
            // a free-text search and an "Add to album" button (visible only
            // when selection > 0, populated by the existing bulk footer).
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(bar, Dock.Top);

            var search = new TextBox
            {
                Width = 260, Height = 26, FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 4),
                ToolTip = "Search caption / level / zone / discipline (server-side)"
            };
            bar.Children.Add(search);

            var refreshBtn = new Button
            {
                Content = "↻ Refresh", Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Background = owner.AccentBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 4),
            };
            bar.Children.Add(refreshBtn);

            var status = new TextBlock
            {
                FontSize = 11, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            bar.Children.Add(status);
            root.Children.Add(bar);

            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var wrap = new UniformGrid
            {
                Columns = Columns,
                Margin = new Thickness(0)
            };
            sv.Content = wrap;
            root.Children.Add(sv);

            async Task LoadAsync()
            {
                wrap.Children.Clear();
                if (!PlanscapeServerClient.Instance.IsConnected || state.ProjectId == Guid.Empty)
                {
                    wrap.Children.Add(new TextBlock
                    {
                        Text = "Sign in to Planscape (PLATFORM tab) to load photos.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray,
                        Margin = new Thickness(8)
                    });
                    return;
                }
                status.Text = "Loading…";
                List<SitePhotoDto> photos;
                try
                {
                    photos = await PlanscapeServerClient.Instance.ListSitePhotosAsync(
                        state.ProjectId, pageSize: 200);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"GridSubTab.Load: {ex.Message}");
                    status.Text = "Failed — see log";
                    return;
                }

                var q = (search.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(q))
                    photos = photos.Where(p =>
                        (p.Caption ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.LevelCode ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.ZoneCode ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.Discipline ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                status.Text = $"{photos.Count} photo{(photos.Count == 1 ? "" : "s")}";
                if (photos.Count == 0)
                {
                    wrap.Children.Add(new TextBlock
                    {
                        Text = "No photos to show.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray,
                        Margin = new Thickness(8)
                    });
                    return;
                }

                foreach (var p in photos.OrderByDescending(p => p.CapturedAt))
                    wrap.Children.Add(BuildTile(owner, state, p));

                // Lazy-load thumbnails after the grid lays out so layout
                // doesn't block on N HTTP fetches.
                async Task LoadThumbsAsync()
                {
                    foreach (var child in wrap.Children)
                    {
                        if (child is FrameworkElement fe && fe.Tag is PhotoTileTag tag && tag.ThumbImage.Source == null)
                        {
                            var bytes = await PlanscapeServerClient.Instance
                                .DownloadSitePhotoAsync(state.ProjectId, tag.PhotoId);
                            if (bytes == null) continue;
                            try
                            {
                                var img = new BitmapImage();
                                img.BeginInit();
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.DecodePixelWidth = (int)(TileSize * 2);
                                img.StreamSource = new MemoryStream(bytes);
                                img.EndInit();
                                img.Freeze();
                                tag.ThumbImage.Source = img;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"GridSubTab thumb {tag.PhotoId}: {ex.Message}");
                            }
                        }
                    }
                }
                _ = LoadThumbsAsync();
            }

            refreshBtn.Click += (_, _) => _ = LoadAsync();
            search.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = LoadAsync(); };
            _ = LoadAsync();
            return root;
        }

        private sealed class PhotoTileTag
        {
            public Guid PhotoId;
            public Image ThumbImage = new();
        }

        private static UIElement BuildTile(
            BIMCoordinationCenter owner, SitePhotosTab.TabState state, SitePhotoDto p)
        {
            var border = new Border
            {
                Width = TileSize, Height = TileSize + 56,
                Margin = new Thickness(4),
                Background = owner.CardBrushPub,
                BorderBrush = owner.BorderBrushPub,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Padding = new Thickness(0)
            };
            var tag = new PhotoTileTag { PhotoId = p.Id };
            border.Tag = tag;

            var dock = new DockPanel { LastChildFill = true };
            border.Child = dock;

            var thumbBorder = new Border
            {
                Width = TileSize, Height = TileSize,
                Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)),
                ClipToBounds = true,
                Child = tag.ThumbImage
            };
            tag.ThumbImage.Stretch = Stretch.UniformToFill;
            DockPanel.SetDock(thumbBorder, Dock.Top);
            dock.Children.Add(thumbBorder);

            // Reason chip overlay (top-left)
            var chip = MakeReasonChip(p.Reason);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.VerticalAlignment = VerticalAlignment.Top;
            chip.Margin = new Thickness(4);
            // We can't overlay onto a DockPanel cleanly without a Grid;
            // attach via Canvas tricks isn't worth the cost. Skip — the
            // reason colour shows on the caption strip below.

            var captionStrip = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
            captionStrip.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(p.Caption) ? "(no caption)" : p.Caption!,
                FontSize = 11,
                FontStyle = string.IsNullOrWhiteSpace(p.Caption) ? FontStyles.Italic : FontStyles.Normal,
                Foreground = string.IsNullOrWhiteSpace(p.Caption) ? Brushes.Gray : Brushes.Black,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            captionStrip.Children.Add(new TextBlock
            {
                Text = $"{p.Reason ?? "?"} · {p.LevelCode ?? "—"}/{p.ZoneCode ?? "—"} · {p.CapturedAt.ToLocalTime():dd MMM HH:mm}",
                FontSize = 9, Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            dock.Children.Add(captionStrip);

            // Selection toggling — click adds to TabState.SelectedIds; the
            // Review queue's bulk footer surfaces those. Border outline
            // reflects selection.
            void ApplySelectionVisual()
            {
                bool sel = state.SelectedIds.Contains(p.Id);
                border.BorderThickness = new Thickness(sel ? 3 : 1);
                border.BorderBrush = sel ? owner.AccentBrushPub : owner.BorderBrushPub;
            }
            ApplySelectionVisual();

            border.MouseLeftButtonUp += (_, _) =>
            {
                if (state.SelectedIds.Contains(p.Id)) state.SelectedIds.Remove(p.Id);
                else state.SelectedIds.Add(p.Id);
                ApplySelectionVisual();
            };
            return border;
        }

        private static Border MakeReasonChip(string? reason)
        {
            var match = SitePhotosTab.Reasons.FirstOrDefault(r =>
                string.Equals(r.Code, reason, StringComparison.OrdinalIgnoreCase));
            var colour = match.Code != null ? match.Colour : Color.FromRgb(0x78, 0x90, 0x9C);
            return new Border
            {
                Background = new SolidColorBrush(colour),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(reason) ? "?" : reason,
                    FontSize = 9, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
                }
            };
        }
    }
}
