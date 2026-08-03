// ══════════════════════════════════════════════════════════════════════
//  SitePhotosAlbumsSubTab — Phase 179 album curation surface for the BCC.
//
//  Two-pane layout:
//    Left  — list of albums on the current project (name, kind chip,
//            visibility pill, photo count, lock state)
//    Right — selected album detail: header strip with rename / lock /
//            export, photo grid of members, "add selected from grid"
//            shortcut, share-link issuance.
//
//  Mutation routes through PlanscapeServerClient; a single failure
//  surfaces in a TaskDialog and the list re-loads to drop the stale
//  optimistic state.
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
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.UI
{
    internal static class SitePhotosAlbumsSubTab
    {
        internal static UIElement Build(BIMCoordinationCenter owner, SitePhotosTab.TabState state)
        {
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftDock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(leftDock, 0);

            var topBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(topBar, Dock.Top);
            var newBtn = new Button
            {
                Content = "＋ New album", Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Background = owner.AccentBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
            };
            topBar.Children.Add(newBtn);
            var refreshBtn = new Button
            {
                Content = "↻", Height = 26, Width = 28, Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1), FontSize = 11, Cursor = Cursors.Hand,
            };
            topBar.Children.Add(refreshBtn);
            leftDock.Children.Add(topBar);

            var listSv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var listPanel = new StackPanel();
            listSv.Content = listPanel;
            leftDock.Children.Add(listSv);
            grid.Children.Add(leftDock);

            var rightSv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var rightPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            rightSv.Content = rightPanel;
            Grid.SetColumn(rightSv, 1);
            grid.Children.Add(rightSv);

            PhotoAlbumDto? selected = null;

            async Task LoadListAsync()
            {
                listPanel.Children.Clear();
                // Not-signed-in and no-project-linked are separate blockers with
                // separate fixes; rendering one message for both sends a signed-in
                // user to sign in again while the real blocker goes unnamed.
                var blocked = SitePhotosTabHelpers.DescribeBlocker(state.ProjectId);
                if (blocked != null)
                {
                    listPanel.Children.Add(SitePhotosTabHelpers.BuildBlockerNotice(
                        blocked.Value.Headline, blocked.Value.WhatToDo));
                    return;
                }
                // null = the load FAILED. An empty list = the project genuinely has
                // no albums. These must never render the same: "No albums yet" over
                // a failed request is invented data.
                List<PhotoAlbumDto> albums;
                try { albums = await PlanscapeServerClient.Instance.ListPhotoAlbumsAsync(state.ProjectId); }
                catch (Exception ex)
                {
                    StingLog.Warn($"AlbumsSubTab.Load: {ex.Message}");
                    listPanel.Children.Add(SitePhotosTabHelpers.BuildLoadFailure("Could not load albums.", ex.Message));
                    return;
                }
                if (albums == null)
                {
                    listPanel.Children.Add(SitePhotosTabHelpers.BuildLoadFailure(
                        "Could not load albums.",
                        PlanscapeServerClient.Instance.LastError));
                    return;
                }
                if (albums.Count == 0)
                {
                    listPanel.Children.Add(new TextBlock {
                        Text = "No albums yet — create one to curate photos for the client / handover.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, Margin = new Thickness(6),
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }
                foreach (var a in albums)
                {
                    var row = BuildAlbumRow(owner, a, () =>
                    {
                        selected = a;
                        _ = RenderRightAsync();
                    });
                    listPanel.Children.Add(row);
                }
            }

            async Task RenderRightAsync()
            {
                rightPanel.Children.Clear();
                if (selected == null)
                {
                    rightPanel.Children.Add(new TextBlock
                    {
                        Text = "Select an album to view its photos.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, Margin = new Thickness(8)
                    });
                    return;
                }

                rightPanel.Children.Add(BuildAlbumHeader(owner, selected, async () => await LoadListAsync()));

                rightPanel.Children.Add(new TextBlock
                {
                    Text = $"{selected.PhotoCount} photo{(selected.PhotoCount == 1 ? "" : "s")}" +
                           (selected.IsLocked ? " · 🔒 locked" : ""),
                    FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 8)
                });

                var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

                var addSelected = new Button
                {
                    Content = $"＋ Add selected ({state.SelectedIds.Count})",
                    Height = 26, Padding = new Thickness(10, 0, 10, 0),
                    Background = owner.GreenBrushPub, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                    IsEnabled = !selected.IsLocked && state.SelectedIds.Count > 0,
                };
                addSelected.Click += async (_, _) =>
                {
                    var ids = state.SelectedIds.ToList();
                    var ok = await PlanscapeServerClient.Instance
                        .AddPhotosToAlbumAsync(state.ProjectId, selected!.Id, ids);
                    if (!ok)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Add to album",
                            $"Failed.\n\n{PlanscapeServerClient.Instance.LastError}");
                    }
                    else
                    {
                        state.SelectedIds.Clear();
                        await LoadListAsync();
                        await RenderRightAsync();
                    }
                };
                actions.Children.Add(addSelected);

                var lockBtn = new Button
                {
                    Content = selected.IsLocked ? "Unlock" : "Lock",
                    Height = 26, Padding = new Thickness(10, 0, 10, 0),
                    Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gainsboro,
                    BorderThickness = new Thickness(1), FontSize = 11, Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                lockBtn.Click += async (_, _) =>
                {
                    var ok = await PlanscapeServerClient.Instance
                        .LockPhotoAlbumAsync(state.ProjectId, selected!.Id, !selected.IsLocked);
                    if (!ok) Autodesk.Revit.UI.TaskDialog.Show("Lock",
                        SitePhotosTabHelpers.Reason(selected.IsLocked ? "Unlocking the album" : "Locking the album"));
                    else { await LoadListAsync(); selected.IsLocked = !selected.IsLocked; await RenderRightAsync(); }
                };
                actions.Children.Add(lockBtn);

                var shareBtn = new Button
                {
                    Content = "🔗 Share link",
                    Height = 26, Padding = new Thickness(10, 0, 10, 0),
                    Background = owner.HeaderBrushPub, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                shareBtn.Click += async (_, _) =>
                {
                    var link = await PlanscapeServerClient.Instance.CreatePhotoShareLinkAsync(
                        state.ProjectId, albumId: selected!.Id, label: selected.Name + " (BCC)");
                    if (link == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Share link",
                            SitePhotosTabHelpers.Reason("Creating the share link"));
                        return;
                    }
                    var full = $"/api/share/{link.Token}";
                    Autodesk.Revit.UI.TaskDialog.Show("Share link",
                        $"Token: {link.Token}\nExpires: {link.ExpiresAt:yyyy-MM-dd HH:mm}\nForce-redacted: {link.ForceRedacted}\n\n" +
                        $"Append to your Planscape server URL:\n{full}");
                };
                actions.Children.Add(shareBtn);

                var exportBtn = new Button
                {
                    Content = "⤓ Export ZIP",
                    Height = 26, Padding = new Thickness(10, 0, 10, 0),
                    Background = owner.HeaderBrushPub, Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                };
                exportBtn.Click += async (_, _) =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "ZIP archive (*.zip)|*.zip|PDF document (*.pdf)|*.pdf",
                        FileName = $"album-{selected!.Name}-{DateTime.Now:yyyyMMddHHmmss}.zip"
                    };
                    if (dlg.ShowDialog() != true) return;
                    // Filter index 1 = ZIP, 2 = PDF (1-based in WPF SaveFileDialog).
                    var format = dlg.FilterIndex == 2 ? "pdf" : "zip";
                    var path = await PlanscapeServerClient.Instance.ExportPhotosAsync(
                        state.ProjectId, dlg.FileName, albumId: selected.Id, format: format);
                    Autodesk.Revit.UI.TaskDialog.Show("Export",
                        path != null ? $"Wrote {path}" : SitePhotosTabHelpers.Reason("Exporting the album"));
                };
                actions.Children.Add(exportBtn);

                rightPanel.Children.Add(actions);

                // Photo strip — load detail + render thumbnails.
                var detail = await PlanscapeServerClient.Instance.GetPhotoAlbumAsync(state.ProjectId, selected.Id);
                if (detail == null)
                {
                    // Failed to load — NOT an empty album. Saying "Album is empty"
                    // here would invite someone to re-add photos that are already in it.
                    rightPanel.Children.Add(SitePhotosTabHelpers.BuildLoadFailure(
                        "Could not load this album's photos.",
                        PlanscapeServerClient.Instance.LastError));
                    return;
                }
                if (detail.Photos == null || detail.Photos.Count == 0)
                {
                    rightPanel.Children.Add(new TextBlock
                    {
                        Text = "Album is empty. Select photos in the Grid tab and click '＋ Add selected'.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0)
                    });
                    return;
                }
                var wrap = new UniformGrid { Columns = 4 };
                rightPanel.Children.Add(wrap);
                foreach (var entry in detail.Photos)
                {
                    var thumb = new Border
                    {
                        Width = 160, Height = 160, Margin = new Thickness(4),
                        Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)),
                        BorderBrush = owner.BorderBrushPub, BorderThickness = new Thickness(1),
                    };
                    var img = new Image { Stretch = Stretch.UniformToFill };
                    thumb.Child = img;
                    wrap.Children.Add(thumb);

                    async Task LoadAsync()
                    {
                        var bytes = await PlanscapeServerClient.Instance
                            .DownloadSitePhotoAsync(state.ProjectId, entry.PhotoId);
                        if (bytes == null) return;
                        try
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = 320;
                            bmp.StreamSource = new MemoryStream(bytes);
                            bmp.EndInit();
                            bmp.Freeze();
                            img.Source = bmp;
                        }
                        catch (Exception ex) { StingLog.Warn($"Album thumb: {ex.Message}"); }
                    }
                    _ = LoadAsync();
                }
            }

            newBtn.Click += async (_, _) =>
            {
                // Was a bare `if (!IsConnected) return;` — a button that does
                // nothing, says nothing, and logs nothing. It also let an unlinked
                // model through to POST /api/projects/{Guid.Empty}/photo-albums,
                // so the *reported* failure was a server 404 about a project id the
                // user never chose. Refuse before the round trip, and say which.
                var blocked = SitePhotosTabHelpers.DescribeBlocker(state.ProjectId);
                if (blocked != null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("New album",
                        $"{blocked.Value.Headline}\n\n{blocked.Value.WhatToDo}");
                    return;
                }
                var name = SitePhotosTabHelpers.PromptForString(owner,
                    "New album", "Album name (required):", "");
                if (string.IsNullOrWhiteSpace(name)) return;
                var album = await PlanscapeServerClient.Instance.CreatePhotoAlbumAsync(
                    state.ProjectId, name.Trim(), visibility: "Members");
                if (album == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("New album",
                        SitePhotosTabHelpers.Reason("Creating the album"));
                    return;
                }
                await LoadListAsync();
                selected = album;
                await RenderRightAsync();
            };
            refreshBtn.Click += (_, _) => _ = LoadListAsync();

            _ = LoadListAsync();
            return grid;
        }

        private static UIElement BuildAlbumRow(
            BIMCoordinationCenter owner, PhotoAlbumDto a, Action onClick)
        {
            var b = new Border
            {
                Background = owner.CardBrushPub,
                BorderBrush = owner.BorderBrushPub,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock {
                Text = a.Name, FontWeight = FontWeights.SemiBold, FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            sp.Children.Add(new TextBlock {
                Text = $"{a.Visibility}{(a.Kind != null ? " · " + a.Kind : "")} · {a.PhotoCount} photo{(a.PhotoCount == 1 ? "" : "s")}{(a.IsLocked ? " · 🔒" : "")}",
                FontSize = 10, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis
            });
            b.Child = sp;
            b.MouseLeftButtonUp += (_, _) => onClick();
            return b;
        }

        private static UIElement BuildAlbumHeader(
            BIMCoordinationCenter owner, PhotoAlbumDto a, Func<Task> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(new TextBlock {
                Text = a.Name, FontSize = 18, FontWeight = FontWeights.Bold,
            });
            if (!string.IsNullOrEmpty(a.Description))
            {
                sp.Children.Add(new TextBlock {
                    Text = a.Description, FontSize = 12, Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
            }
            return sp;
        }
    }

    /// <summary>Tiny helpers so multiple sub-tabs can reuse the
    /// PromptForString modal without re-implementing it.</summary>
    internal static class SitePhotosTabHelpers
    {
        /// <summary>
        /// Why a site-photo call cannot even be attempted — or <c>null</c> when it can.
        ///
        /// "Not signed in" and "no project linked" are DIFFERENT problems with
        /// different fixes, and the panes used to collapse both into
        /// "Sign in to Planscape (PLATFORM tab)". A signed-in user reading that
        /// message has been told to do the one thing they have already done, and
        /// the actual blocker — an unlinked model — goes unmentioned.
        ///
        /// Returns a (headline, whatToDo) pair so callers can render it inline
        /// (list panes) or in a TaskDialog (mutation buttons) from one source.
        /// </summary>
        public static (string Headline, string WhatToDo)? DescribeBlocker(Guid projectId)
        {
            if (!PlanscapeServerClient.Instance.IsConnected)
                return ("Not signed in to Planscape.",
                        "Open the PLATFORM tab and connect, then return here.");

            if (projectId == Guid.Empty)
                return ("This model is not linked to a Planscape project.",
                        "Open the PLATFORM tab → Planscape → \"Link project\" and pick the "
                      + "server project this model belongs to. Signing in is not enough — "
                      + "photos, albums and checklists all live under a project.");

            return null;
        }

        /// <summary>
        /// Render a blocked precondition. Deliberately NOT styled as a load failure
        /// (<see cref="BuildLoadFailure"/>) — nothing failed; the request was never
        /// made. It still names the blocker and the fix instead of a bare prompt.
        /// </summary>
        public static UIElement BuildBlockerNotice(string headline, string whatToDo)
        {
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock
            {
                Text = headline,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
            sp.Children.Add(new TextBlock
            {
                Text = whatToDo,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            return sp;
        }

        /// <summary>
        /// The reason a mutation failed, guaranteed non-empty.
        ///
        /// Call sites used to write <c>LastError ?? "(no detail)"</c>. When that
        /// fallback is reached the dialog says nothing at all — the exact failure
        /// this suite exists to eliminate, and the one shape of it that survived
        /// because it looked like it had already been handled. Every client method
        /// sets <see cref="PlanscapeServerClient.LastError"/> on every failure path,
        /// so an empty reason is a BUG, not a state — say so, and name the operation
        /// so the report is actionable.
        /// </summary>
        public static string Reason(string operation)
        {
            var err = PlanscapeServerClient.Instance.LastError;
            if (!string.IsNullOrWhiteSpace(err)) return err!;
            return $"{operation} failed, but the client recorded no reason.\n\n"
                 + "That is a bug in STING Tools, not a server error — please report it "
                 + "with the timestamp so the failing path can be found in StingTools.log.";
        }

        /// <summary>
        /// Render a load FAILURE — visibly different from an empty result.
        ///
        /// The site-photo client returns <c>null</c> from a list call when the
        /// request failed and an empty list only when the project genuinely has
        /// nothing. Before this existed the sub-tabs collapsed both into the
        /// friendly "No albums yet" empty-state, so an unreachable server rendered
        /// as a confident, wrong, empty answer. Anything that could not be loaded
        /// says so, in red, with the server's own reason.
        /// </summary>
        public static UIElement BuildLoadFailure(string headline, string? detail)
        {
            var sp = new StackPanel { Margin = new Thickness(6) };
            sp.Children.Add(new TextBlock
            {
                Text = "⚠ " + headline,
                Foreground = Brushes.Crimson,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(detail))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = Brushes.Crimson,
                    Opacity = 0.85,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            sp.Children.Add(new TextBlock
            {
                // Say plainly that the pane is not authoritative right now.
                Text = "This is not an empty result — the list could not be retrieved.",
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return sp;
        }

        public static string? PromptForString(Window owner, string title, string prompt, string initialValue)
        {
            var dlg = new Window
            {
                Title = title, Width = 460, Height = 200,
                Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock {
                Text = prompt, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var box = new TextBox {
                Text = initialValue, FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4), Height = 28
            };
            sp.Children.Add(box);
            string? result = null;
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button {
                Content = "OK", Width = 80, Height = 28, IsDefault = true,
                Margin = new Thickness(0, 0, 6, 0)
            };
            ok.Click += (_, _) => { result = box.Text ?? ""; dlg.Close(); };
            var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
            cancel.Click += (_, _) => { result = null; dlg.Close(); };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            sp.Children.Add(btnRow);
            dlg.Content = sp;
            box.Focus(); box.SelectAll();
            dlg.ShowDialog();
            return result;
        }
    }
}
