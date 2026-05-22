// ══════════════════════════════════════════════════════════════════════
//  SitePhotosTab — BCC desktop review surface for the Planscape site-
//  photo workflow (Slice 4a).
//
//  The desktop coordinator triages incoming site photos by Reason
//  taxonomy (Progress | Issue | Defect | Safety | AsBuilt | Reference),
//  approves / rejects individually, and bulk-approves with a shared
//  caption. Captured photos arrive from the mobile app; the BCC user is
//  the *primary* approval surface — server enforces the 5-state Audience
//  state machine.
//
//  This file is a helper for BIMCoordinationCenter.cs. The 14th tab in
//  the BCC delegates to BuildTab(owner) here so the existing 13-tab file
//  doesn't grow unbounded. All chrome (cards, KPIs, brushes) is sourced
//  from BIMCoordinationCenter via internal accessors so the tab matches
//  every other tab visually.
//
//  No Revit API calls compile-tested — the Linux sandbox can't reach
//  RevitAPI.dll. Verify in Revit before merge.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StingTools.BIMManager;
using StingTools.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using Grid       = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;
using TextBox    = System.Windows.Controls.TextBox;
using ComboBox   = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace StingTools.UI
{
    /// <summary>
    /// Builds the SITE PHOTOS tab embedded in the BIM Coordination Center.
    /// Pure UI layer — all server I/O routes through PlanscapeServerClient.
    /// </summary>
    internal static class SitePhotosTab
    {
        // ── Reason taxonomy (locked design) ────────────────────────────
        // Order is the display order in the Pending Review grouped view —
        // Safety first because hazards must be triaged immediately.
        internal static readonly (string Code, string Label, Color Colour)[] Reasons =
        {
            ("Safety",    "Safety hazard",   Color.FromRgb(0xC6, 0x28, 0x28)),
            ("Defect",    "Defect / snag",   Color.FromRgb(0xE6, 0x5C, 0x00)),
            ("Issue",     "Issue / RFI",     Color.FromRgb(0xE8, 0x91, 0x2D)),
            ("Progress",  "Progress",        Color.FromRgb(0x15, 0x65, 0xC0)),
            ("AsBuilt",   "As-built",        Color.FromRgb(0x2E, 0x7D, 0x32)),
            ("Reference", "Reference",       Color.FromRgb(0x45, 0x50, 0x6E)),
        };

        // BCC sub-view selector — drives the filter audience= query.
        private const string ViewPending  = "Pending Review";
        private const string ViewAll      = "All Photos";
        private const string ViewClient   = "Client Portal";

        // Auto-refresh cadence — match the Issues tab.
        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

        // ── Per-tab state ──────────────────────────────────────────────
        // The tab is rebuilt on every NavigateTo (it lives in _liveDataTabs)
        // so we keep state on the instance returned to the BCC.
        internal sealed class TabState
        {
            public Guid ProjectId;
            public string CurrentView = ViewPending;

            // Active filter values (null = no filter)
            public string? FilterReason;
            public string? FilterLevel;
            public string? FilterZone;
            public string? FilterDiscipline;
            public DateTime? FilterFrom;
            public DateTime? FilterTo;

            // Photo collection bound to UI
            public ObservableCollection<PhotoRow> Rows = new();

            // Selection set for bulk operations — keyed by photo Id
            public HashSet<Guid> SelectedIds = new();

            // Caption box content for bulk approve
            public string BulkCaption = "";

            // Auto-refresh timer (null if not started)
            public DispatcherTimer? RefreshTimer;
        }

        /// <summary>UI-bindable photo row. Wraps the DTO so we can add a
        /// thumbnail BitmapImage + IsSelected without mutating the DTO.</summary>
        internal sealed class PhotoRow : System.ComponentModel.INotifyPropertyChanged
        {
            public SitePhotoDto Dto { get; set; } = new();

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected != value) { _isSelected = value; OnChanged(nameof(IsSelected)); } }
            }

            private BitmapImage? _thumb;
            public BitmapImage? Thumbnail
            {
                get => _thumb;
                set { _thumb = value; OnChanged(nameof(Thumbnail)); }
            }

            public string DisplayCaption =>
                string.IsNullOrWhiteSpace(Dto.Caption) ? "(no caption)" : Dto.Caption!;

            public string DisplayLevelZone
            {
                get
                {
                    var l = string.IsNullOrEmpty(Dto.LevelCode) ? "—" : Dto.LevelCode!;
                    var z = string.IsNullOrEmpty(Dto.ZoneCode) ? "—" : Dto.ZoneCode!;
                    return $"{l} / {z}";
                }
            }

            public string DisplayCapturedBy =>
                string.IsNullOrEmpty(Dto.CapturedByName) ? "(unknown)" : Dto.CapturedByName!;

            public string DisplayCapturedAt =>
                Dto.CapturedAt == DateTime.MinValue ? "—"
                    : Dto.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string p) =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }

        // ──────────────────────────────────────────────────────────────
        //  PUBLIC ENTRY POINT
        //  Called by BIMCoordinationCenter.BuildSitePhotosTab().
        // ──────────────────────────────────────────────────────────────

        /// <summary>Build the Site Photos tab UI. The BCC keeps the returned
        /// element in its tab cache (live-data tab — rebuilt on every nav).
        ///
        /// Phase 179 — wraps the existing review queue inside a 5-pane
        /// TabControl: Review (the original list), Grid (contact-sheet
        /// thumbnail grid), Albums (curate + share), Checklists (required
        /// shots), Admin (BIM-manager only — policy + bulk operations).
        /// </summary>
        internal static UIElement BuildTab(BIMCoordinationCenter owner)
        {
            var state = new TabState { ProjectId = ResolveProjectId() };

            var tabs = new TabControl
            {
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(8),
            };

            tabs.Items.Add(new TabItem
            {
                Header  = "Review queue",
                Content = BuildReviewSubTab(owner, state)
            });
            tabs.Items.Add(new TabItem
            {
                Header  = "Grid",
                Content = SitePhotosGridSubTab.Build(owner, state)
            });
            tabs.Items.Add(new TabItem
            {
                Header  = "Albums",
                Content = SitePhotosAlbumsSubTab.Build(owner, state)
            });
            tabs.Items.Add(new TabItem
            {
                Header  = "Checklists",
                Content = SitePhotosChecklistsSubTab.Build(owner, state)
            });
            tabs.Items.Add(new TabItem
            {
                Header  = "Admin",
                Content = SitePhotosAdminSubTab.Build(owner, state)
            });

            owner.Closed += (_, _) => state.RefreshTimer?.Stop();
            return tabs;
        }

        /// <summary>The Phase 178 review queue, factored out as a sub-tab
        /// content so it composes with the new Phase 179 sibling tabs.</summary>
        private static UIElement BuildReviewSubTab(BIMCoordinationCenter owner, TabState state)
        {
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var root = new StackPanel { Margin = new Thickness(16) };
            sv.Content = root;

            root.Children.Add(BuildHeader(owner, state));
            root.Children.Add(BuildActionBar(owner, state, root));
            root.Children.Add(BuildFilterRow(owner, state, root));

            var listPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            listPanel.Tag = "PhotoList";
            root.Children.Add(listPanel);

            var footer = BuildBulkFooter(owner, state, listPanel);
            footer.Visibility = Visibility.Collapsed;
            footer.Tag = "BulkFooter";
            root.Children.Add(footer);

            async Task BootAsync()
            {
                await ReloadAsync(owner, state, listPanel, footer);
                StartAutoRefresh(owner, state, listPanel, footer);
            }
            _ = BootAsync();
            return sv;
        }

        // ──────────────────────────────────────────────────────────────
        //  HEADER + KPI STRIP
        // ──────────────────────────────────────────────────────────────

        private static UIElement BuildHeader(BIMCoordinationCenter owner, TabState state)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Site Photos — review queue",
                FontSize = 16, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(title);

            // Live pill — mirrors the Issues tab style
            var livePill = BuildLivePill(owner, state);
            Grid.SetColumn(livePill, 1);
            grid.Children.Add(livePill);

            return grid;
        }

        private static Border BuildLivePill(BIMCoordinationCenter owner, TabState state)
        {
            bool connected = PlanscapeServerClient.Instance.IsConnected;
            var pill = new Border
            {
                Background      = connected ? owner.RagBrushPub("GREEN") : owner.RagBrushPub("RED"),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            pill.Child = new TextBlock
            {
                Text = connected ? "● Live" : "○ Offline",
                Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold
            };
            pill.ToolTip = connected
                ? "Connected to Planscape server. Auto-refresh every 30 s."
                : "Not signed into Planscape — sign in via PLATFORM tab to load photos.";
            return pill;
        }

        // ──────────────────────────────────────────────────────────────
        //  ACTION BAR
        // ──────────────────────────────────────────────────────────────

        private static UIElement BuildActionBar(
            BIMCoordinationCenter owner, TabState state, StackPanel root)
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Sub-view selector (radio-style buttons)
            foreach (var view in new[] { ViewPending, ViewAll, ViewClient })
            {
                var btn = new RadioButton
                {
                    Content = view, GroupName = "SitePhotosView",
                    IsChecked = view == state.CurrentView,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = view
                };
                btn.Checked += (_, _) =>
                {
                    state.CurrentView = view;
                    state.SelectedIds.Clear();
                    var listPanel = FindByTag(root, "PhotoList") as StackPanel;
                    var footer    = FindByTag(root, "BulkFooter") as Border;
                    _ = ReloadAsync(owner, state, listPanel, footer);
                };
                bar.Children.Add(btn);
            }

            bar.Children.Add(MakeSeparator());

            // Refresh
            var refreshBtn = MakeBarButton("↻ Refresh", owner.AccentBrushPub, "Reload photos from server");
            refreshBtn.Click += (_, _) =>
            {
                var listPanel = FindByTag(root, "PhotoList") as StackPanel;
                var footer    = FindByTag(root, "BulkFooter") as Border;
                _ = ReloadAsync(owner, state, listPanel, footer);
            };
            bar.Children.Add(refreshBtn);

            // Digest preview
            var digestBtn = MakeBarButton("📨 Digest preview",
                owner.HeaderBrushPub, "Preview today's client digest content before it ships");
            digestBtn.Click += async (_, _) => await ShowDigestPreviewAsync(owner, state);
            bar.Children.Add(digestBtn);

            return bar;
        }

        private static UIElement MakeSeparator() =>
            new Border
            {
                Width = 1, Height = 22,
                Background = Brushes.Gainsboro,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

        private static Button MakeBarButton(string label, SolidColorBrush bg, string tip)
        {
            return new Button
            {
                Content = label, Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Background = bg, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0),
                ToolTip = tip
            };
        }

        // ──────────────────────────────────────────────────────────────
        //  FILTER ROW
        // ──────────────────────────────────────────────────────────────

        private static UIElement BuildFilterRow(
            BIMCoordinationCenter owner, TabState state, StackPanel root)
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Reason
            var reasonCb = new ComboBox { Width = 130, FontSize = 11, Margin = new Thickness(0, 0, 8, 4) };
            reasonCb.Items.Add(new ComboBoxItem { Content = "(all reasons)", Tag = null });
            foreach (var r in Reasons)
                reasonCb.Items.Add(new ComboBoxItem { Content = r.Label, Tag = r.Code });
            reasonCb.SelectedIndex = 0;
            reasonCb.SelectionChanged += (_, _) =>
            {
                state.FilterReason = (reasonCb.SelectedItem as ComboBoxItem)?.Tag as string;
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            AddFilterLabel(bar, "Reason");
            bar.Children.Add(reasonCb);

            // Level — free-text combo (project-specific values vary)
            var levelTb = new TextBox { Width = 80, FontSize = 11, Margin = new Thickness(0, 0, 8, 4),
                ToolTip = "Filter by level code, e.g. L01, GF, B1" };
            levelTb.LostFocus += (_, _) =>
            {
                state.FilterLevel = string.IsNullOrWhiteSpace(levelTb.Text) ? null : levelTb.Text.Trim();
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            AddFilterLabel(bar, "Level");
            bar.Children.Add(levelTb);

            var zoneTb = new TextBox { Width = 80, FontSize = 11, Margin = new Thickness(0, 0, 8, 4),
                ToolTip = "Filter by zone code, e.g. Z01" };
            zoneTb.LostFocus += (_, _) =>
            {
                state.FilterZone = string.IsNullOrWhiteSpace(zoneTb.Text) ? null : zoneTb.Text.Trim();
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            AddFilterLabel(bar, "Zone");
            bar.Children.Add(zoneTb);

            var discTb = new TextBox { Width = 80, FontSize = 11, Margin = new Thickness(0, 0, 8, 4),
                ToolTip = "Filter by discipline code, e.g. M, E, P, A, S" };
            discTb.LostFocus += (_, _) =>
            {
                state.FilterDiscipline = string.IsNullOrWhiteSpace(discTb.Text) ? null : discTb.Text.Trim();
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            AddFilterLabel(bar, "Disc");
            bar.Children.Add(discTb);

            // Date range — preset combo (server filter is from/to)
            var dateCb = new ComboBox { Width = 110, FontSize = 11, Margin = new Thickness(0, 0, 8, 4) };
            dateCb.Items.Add(new ComboBoxItem { Content = "(any date)", Tag = "ANY" });
            dateCb.Items.Add(new ComboBoxItem { Content = "Today",      Tag = "TODAY" });
            dateCb.Items.Add(new ComboBoxItem { Content = "Last 7 days",Tag = "WEEK" });
            dateCb.Items.Add(new ComboBoxItem { Content = "Last 30 days",Tag= "MONTH" });
            dateCb.SelectedIndex = 0;
            dateCb.SelectionChanged += (_, _) =>
            {
                var tag = (dateCb.SelectedItem as ComboBoxItem)?.Tag as string ?? "ANY";
                (state.FilterFrom, state.FilterTo) = tag switch
                {
                    "TODAY" => (DateTime.UtcNow.Date, (DateTime?)null),
                    "WEEK"  => (DateTime.UtcNow.AddDays(-7), (DateTime?)null),
                    "MONTH" => (DateTime.UtcNow.AddDays(-30), (DateTime?)null),
                    _       => ((DateTime?)null, (DateTime?)null),
                };
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            AddFilterLabel(bar, "Date");
            bar.Children.Add(dateCb);

            // Reset
            var resetBtn = new Button
            {
                Content = "Reset", Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1), FontSize = 11,
                Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 4)
            };
            resetBtn.Click += (_, _) =>
            {
                state.FilterReason = null;
                state.FilterLevel = null;
                state.FilterZone = null;
                state.FilterDiscipline = null;
                state.FilterFrom = null;
                state.FilterTo = null;
                reasonCb.SelectedIndex = 0;
                levelTb.Text = "";
                zoneTb.Text = "";
                discTb.Text = "";
                dateCb.SelectedIndex = 0;
                _ = ReloadAsync(owner, state, FindByTag(root, "PhotoList") as StackPanel,
                    FindByTag(root, "BulkFooter") as Border);
            };
            bar.Children.Add(resetBtn);

            return bar;
        }

        private static void AddFilterLabel(WrapPanel bar, string text)
        {
            bar.Children.Add(new TextBlock
            {
                Text = text + ":", FontSize = 11,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // ──────────────────────────────────────────────────────────────
        //  PHOTO LIST RENDER (grouped by Reason in PendingReview)
        // ──────────────────────────────────────────────────────────────

        private static async Task ReloadAsync(
            BIMCoordinationCenter owner, TabState state,
            StackPanel? listPanel, Border? footer)
        {
            if (listPanel == null) return;

            // Render placeholder while we fetch
            listPanel.Children.Clear();
            listPanel.Children.Add(new TextBlock
            {
                Text = PlanscapeServerClient.Instance.IsConnected
                    ? "Loading photos..." : "Sign in to Planscape (PLATFORM tab) to load photos.",
                FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray, Margin = new Thickness(8)
            });

            if (state.ProjectId == Guid.Empty)
            {
                listPanel.Children.Clear();
                listPanel.Children.Add(new TextBlock {
                    Text = "Open a Planscape-linked project to load photos.",
                    FontSize = 12, FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray, Margin = new Thickness(8)
                });
                return;
            }
            if (!PlanscapeServerClient.Instance.IsConnected)
            {
                // Phase 179 — fall back to the on-disk cache so the panel
                // doesn't render empty when the desktop is offline.
                var cache = SitePhotoOfflineCache.Load(state.ProjectId);
                listPanel.Children.Clear();
                if (cache == null || cache.Photos.Count == 0)
                {
                    listPanel.Children.Add(new TextBlock {
                        Text = "Offline — sign in to Planscape (PLATFORM tab) to load photos.",
                        FontSize = 12, FontStyle = FontStyles.Italic,
                        Foreground = Brushes.Gray, Margin = new Thickness(8)
                    });
                    return;
                }
                listPanel.Children.Add(new TextBlock {
                    Text = $"Offline — showing {cache.Photos.Count} photo(s) cached at {cache.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.",
                    FontSize = 11, FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray, Margin = new Thickness(8, 4, 8, 8)
                });
                foreach (var dto in cache.Photos.OrderByDescending(d => d.CapturedAt))
                {
                    var row = new PhotoRow { Dto = dto };
                    var bytes = SitePhotoOfflineCache.LoadThumbBytes(state.ProjectId, dto.Id);
                    if (bytes != null)
                    {
                        try
                        {
                            var img = new BitmapImage();
                            img.BeginInit();
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.DecodePixelWidth = 160;
                            img.StreamSource = new MemoryStream(bytes);
                            img.EndInit();
                            img.Freeze();
                            row.Thumbnail = img;
                        }
                        catch { /* ignore */ }
                    }
                    listPanel.Children.Add(BuildPhotoRow(owner, state, row, listPanel, footer));
                }
                return;
            }

            string? audienceFilter = state.CurrentView switch
            {
                ViewPending => "PendingReview",
                ViewClient  => "ClientReady",
                _           => null,
            };

            List<SitePhotoDto> dtos;
            try
            {
                dtos = await PlanscapeServerClient.Instance.ListSitePhotosAsync(
                    state.ProjectId,
                    reason:    state.FilterReason,
                    audience:  audienceFilter,
                    levelCode: state.FilterLevel,
                    zoneCode:  state.FilterZone,
                    from:      state.FilterFrom,
                    to:        state.FilterTo);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SitePhotosTab.ReloadAsync: {ex.Message}");
                listPanel.Children.Clear();
                listPanel.Children.Add(new TextBlock { Text = "Failed to load photos — see log.",
                    FontSize = 12, Foreground = Brushes.Crimson, Margin = new Thickness(8) });
                return;
            }

            // Phase 179 — write the just-fetched DTOs to the offline cache
            // so a later session-without-server still renders the page.
            try { SitePhotoOfflineCache.Save(state.ProjectId, dtos); }
            catch { /* best-effort */ }

            // Apply discipline filter client-side (server doesn't expose discipline directly)
            if (!string.IsNullOrEmpty(state.FilterDiscipline))
                dtos = dtos.Where(d =>
                    string.Equals(d.Discipline, state.FilterDiscipline, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            // Drop selection IDs that no longer exist in the dataset
            var liveIds = new HashSet<Guid>(dtos.Select(d => d.Id));
            state.SelectedIds.RemoveWhere(id => !liveIds.Contains(id));

            // Build rows + thumbnails
            var rows = dtos.Select(d => new PhotoRow
            {
                Dto = d,
                IsSelected = state.SelectedIds.Contains(d.Id),
            }).ToList();

            // Render
            listPanel.Children.Clear();

            if (rows.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = state.CurrentView switch
                    {
                        ViewPending => "✓ No photos awaiting review.",
                        ViewClient  => "No photos currently published to client portal.",
                        _           => "No photos match the current filters.",
                    },
                    FontSize = 12, Margin = new Thickness(8),
                    Foreground = Brushes.Gray, FontStyle = FontStyles.Italic
                });
                UpdateFooterVisibility(footer, state);
                return;
            }

            if (state.CurrentView == ViewPending)
            {
                // Group by Reason — Safety surfaces first (Reasons array order).
                foreach (var (code, label, colour) in Reasons)
                {
                    var group = rows.Where(r =>
                        string.Equals(r.Dto.Reason, code, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (group.Count == 0) continue;
                    listPanel.Children.Add(BuildReasonGroupHeader(owner, state, code, label, colour, group, listPanel, footer));
                    foreach (var r in group)
                        listPanel.Children.Add(BuildPhotoRow(owner, state, r, listPanel, footer));
                }
            }
            else
            {
                // Flat list — All Photos / Client Portal
                foreach (var r in rows.OrderByDescending(r => r.Dto.CapturedAt))
                    listPanel.Children.Add(BuildPhotoRow(owner, state, r, listPanel, footer));
            }

            UpdateFooterVisibility(footer, state);

            // Lazy-load thumbnails after the layout pass — keeps the initial
            // render snappy even with 50 photos (default page size).
            // Fire-and-forget via a local async method so the `_ =`
            // discard is unambiguous (vs Dispatcher.BeginInvoke wrapping
            // an async-void lambda, which Roslyn analyzers occasionally
            // mis-classify and raise CS4014 on). Per-thumb failures are
            // swallowed by the try/catch inside the loop body.
            // Phase 180 — parallelise thumb fetches with a small
            // concurrency cap so the BCC review queue paints quickly
            // even on a 50-photo page (was strictly sequential — N
            // round-trips of latency).
            async Task LoadThumbsAsync()
            {
                using var sem = new System.Threading.SemaphoreSlim(8);
                var todo = rows.Where(r => r.Thumbnail == null).ToList();
                var tasks = todo.Select(async r =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        var bytes = await PlanscapeServerClient.Instance
                            .DownloadSitePhotoAsync(state.ProjectId, r.Dto.Id);
                        if (bytes == null) return;
                        try
                        {
                            // Dispose the MemoryStream after EndInit — with CacheOption.OnLoad
                            // the BitmapImage copies the pixel data into its own memory store,
                            // so the stream is no longer needed. Per-photo leak otherwise.
                            using (var ms = new MemoryStream(bytes))
                            {
                                var img = new BitmapImage();
                                img.BeginInit();
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.DecodePixelWidth = 160; // 2x for retina; rendered at 80
                                img.StreamSource = ms;
                                img.EndInit();
                                img.Freeze();
                                r.Thumbnail = img;
                            }
                            try { SitePhotoOfflineCache.SaveThumbBytes(state.ProjectId, r.Dto.Id, bytes); }
                            catch { /* best-effort */ }
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"SitePhotosTab thumbnail decode {r.Dto.Id}: {ex2.Message}");
                        }
                    }
                    finally { sem.Release(); }
                }).ToList();
                await Task.WhenAll(tasks);
            }
            _ = LoadThumbsAsync();
        }

        private static UIElement BuildReasonGroupHeader(
            BIMCoordinationCenter owner, TabState state,
            string code, string label, Color colour,
            List<PhotoRow> group, StackPanel listPanel, Border? footer)
        {
            var bar = new Border
            {
                Background = new SolidColorBrush(colour) { Opacity = 0.12 },
                BorderBrush = new SolidColorBrush(colour),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Margin = new Thickness(0, 8, 0, 4),
                Padding = new Thickness(10, 6, 10, 6)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hdr = new TextBlock
            {
                Text = $"{label}  —  {group.Count} photo{(group.Count == 1 ? "" : "s")}",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(colour),
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(hdr);

            // Group-level "Approve all" — uses a single shared caption prompt.
            // Safety hazards never bulk-approve silently — surface a confirmation.
            var approveAllBtn = new Button
            {
                Content = "Approve all in this group",
                Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Background = new SolidColorBrush(colour),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                ToolTip = $"Approve every {label.ToLowerInvariant()} photo in this group with the bulk caption."
            };
            Grid.SetColumn(approveAllBtn, 1);
            approveAllBtn.Click += async (_, _) =>
            {
                var ids = group.Select(r => r.Dto.Id).ToList();
                await BulkApproveAsync(owner, state, ids, listPanel, footer, ConfirmDestructive: code == "Safety");
            };
            grid.Children.Add(approveAllBtn);

            bar.Child = grid;
            return bar;
        }

        private static UIElement BuildPhotoRow(
            BIMCoordinationCenter owner, TabState state,
            PhotoRow row, StackPanel listPanel, Border? footer)
        {
            var card = new Border
            {
                Background = owner.CardBrushPub,
                BorderBrush = owner.BorderBrushPub,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });   // thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // actions

            // Checkbox (selection)
            var cb = new CheckBox
            {
                IsChecked = row.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0)
            };
            cb.Checked += (_, _) =>
            {
                row.IsSelected = true;
                state.SelectedIds.Add(row.Dto.Id);
                UpdateFooterVisibility(footer, state);
            };
            cb.Unchecked += (_, _) =>
            {
                row.IsSelected = false;
                state.SelectedIds.Remove(row.Dto.Id);
                UpdateFooterVisibility(footer, state);
            };
            grid.Children.Add(cb);

            // Thumbnail
            var thumb = new Image
            {
                Width = 80, Height = 80, Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 8, 0),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Bind to PhotoRow.Thumbnail so the lazy loader updates the UI
            // without needing a second pass to find the Image.
            thumb.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(PhotoRow.Thumbnail))
            {
                Source = row, Mode = System.Windows.Data.BindingMode.OneWay
            });
            // Placeholder background until the binding fires
            var thumbBorder = new Border
            {
                Width = 80, Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)),
                BorderBrush = owner.BorderBrushPub, BorderThickness = new Thickness(1),
                Child = thumb
            };
            Grid.SetColumn(thumbBorder, 1);
            grid.Children.Add(thumbBorder);

            // Body
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // Reason chip + audience
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            chipRow.Children.Add(MakeReasonChip(row.Dto.Reason));
            if (state.CurrentView == ViewAll)
                chipRow.Children.Add(MakeAudiencePill(row.Dto.Audience));
            if (row.Dto.WatermarkApplied)
                chipRow.Children.Add(MakeMiniPill("watermark", Color.FromRgb(0x45, 0x50, 0x6E)));
            if (!string.IsNullOrEmpty(row.Dto.BlurStatus) && row.Dto.BlurStatus != "None")
                chipRow.Children.Add(MakeMiniPill($"blur: {row.Dto.BlurStatus}", Color.FromRgb(0x6A, 0x1B, 0x9A)));
            body.Children.Add(chipRow);

            // Caption (or placeholder)
            var captionTb = new TextBlock
            {
                Text = row.DisplayCaption,
                FontSize = 12,
                FontStyle = string.IsNullOrWhiteSpace(row.Dto.Caption) ? FontStyles.Italic : FontStyles.Normal,
                Foreground = string.IsNullOrWhiteSpace(row.Dto.Caption)
                    ? Brushes.Gray : Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600
            };
            body.Children.Add(captionTb);

            // Meta row: level/zone, captured by, captured at
            var meta = new TextBlock
            {
                Text = $"{row.DisplayLevelZone}   •   {row.DisplayCapturedBy}   •   {row.DisplayCapturedAt}",
                FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            };
            body.Children.Add(meta);

            Grid.SetColumn(body, 2);
            grid.Children.Add(body);

            // Per-row action buttons (right column)
            var actions = new StackPanel { Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center };

            if (state.CurrentView == ViewClient)
            {
                // Withdraw button — Client Portal only
                var wBtn = MakeRowButton("↶ Withdraw", Color.FromRgb(0xC6, 0x28, 0x28),
                    "Withdraw this photo from the client portal");
                wBtn.Click += async (_, _) => await WithdrawAsync(owner, state, row, listPanel, footer);
                actions.Children.Add(wBtn);
            }
            else
            {
                // Approve / Reject — Pending Review + All Photos
                var aBtn = MakeRowButton("✓ Approve", owner.GreenColourPub,
                    "Approve with caption (defaults to current bulk caption)");
                aBtn.Click += async (_, _) => await ApproveSingleAsync(owner, state, row, listPanel, footer);
                actions.Children.Add(aBtn);

                var rBtn = MakeRowButton("✗ Reject", Color.FromRgb(0xC6, 0x28, 0x28),
                    "Reject with reason — sender will see the reason in mobile");
                rBtn.Click += async (_, _) => await RejectAsync(owner, state, row, listPanel, footer);
                actions.Children.Add(rBtn);
            }

            // Cross-link: Open in Issues tab (for photos linked to an Issue)
            if (row.Dto.AnchorIssueId.HasValue && row.Dto.AnchorIssueId.Value != Guid.Empty)
            {
                var linkBtn = MakeRowButton("→ Issue", Color.FromRgb(0x15, 0x65, 0xC0),
                    "Open the linked Issue in the ISSUES tab");
                linkBtn.Click += (_, _) => owner.NavigateToIssuePub(row.Dto.AnchorIssueId.Value);
                actions.Children.Add(linkBtn);
            }

            // Cross-link: Select element in Revit view (for photos with an anchor)
            if (!string.IsNullOrEmpty(row.Dto.AnchorElementGuid))
            {
                var elBtn = MakeRowButton("📂 Element", Color.FromRgb(0x6A, 0x1B, 0x9A),
                    "Select the linked element in the active Revit view (zooms to it)");
                elBtn.Click += (_, _) => owner.SelectElementInRevitPub(row.Dto.AnchorElementGuid);
                actions.Children.Add(elBtn);
            }

            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        private static Button MakeRowButton(string label, Color colour, string tip)
        {
            return new Button
            {
                Content = label, Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(colour),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = tip
            };
        }

        private static Border MakeReasonChip(string reason)
        {
            var match = Reasons.FirstOrDefault(r =>
                string.Equals(r.Code, reason, StringComparison.OrdinalIgnoreCase));
            var colour = match.Code != null ? match.Colour : Color.FromRgb(0x78, 0x90, 0x9C);
            return new Border
            {
                Background = new SolidColorBrush(colour),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(reason) ? "(no reason)" : reason,
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                }
            };
        }

        private static Border MakeAudiencePill(string audience)
        {
            var colour = audience switch
            {
                "PendingReview" => Color.FromRgb(0xE8, 0x91, 0x2D),
                "ClientReady"   => Color.FromRgb(0x2E, 0x7D, 0x32),
                "InternalOnly"  => Color.FromRgb(0x45, 0x50, 0x6E),
                "Rejected"      => Color.FromRgb(0xC6, 0x28, 0x28),
                "Withdrawn"     => Color.FromRgb(0x78, 0x90, 0x9C),
                _               => Color.FromRgb(0x78, 0x90, 0x9C),
            };
            return MakeMiniPill(audience ?? "(unknown)", colour);
        }

        private static Border MakeMiniPill(string text, Color colour)
        {
            return new Border
            {
                Background = new SolidColorBrush(colour) { Opacity = 0.15 },
                BorderBrush = new SolidColorBrush(colour),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = text, FontSize = 9,
                    Foreground = new SolidColorBrush(colour),
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        // ──────────────────────────────────────────────────────────────
        //  ACTIONS — approve / reject / withdraw / bulk approve
        // ──────────────────────────────────────────────────────────────

        private static async Task ApproveSingleAsync(
            BIMCoordinationCenter owner, TabState state, PhotoRow row,
            StackPanel? listPanel, Border? footer)
        {
            // Default caption: current bulk caption, else photo's existing caption.
            string defaultCaption =
                !string.IsNullOrWhiteSpace(state.BulkCaption) ? state.BulkCaption :
                !string.IsNullOrWhiteSpace(row.Dto.Caption)   ? row.Dto.Caption!  :
                "";

            string? caption = PromptForString(owner,
                "Approve photo",
                $"Caption to ship to client (min 3 chars):\n\nReason: {row.Dto.Reason}\nLevel/Zone: {row.DisplayLevelZone}",
                defaultCaption);
            if (caption == null) return; // cancelled

            if (caption.Trim().Length < 3)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Approve photo",
                    "Caption must be at least 3 characters.");
                return;
            }

            var dto = await PlanscapeServerClient.Instance
                .ApproveSitePhotoAsync(state.ProjectId, row.Dto.Id, caption.Trim());
            if (dto == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Approve photo",
                    $"Server rejected the approval.\n\n{PlanscapeServerClient.Instance.LastError ?? "(no detail)"}");
                return;
            }
            await ReloadAsync(owner, state, listPanel, footer);
        }

        private static async Task RejectAsync(
            BIMCoordinationCenter owner, TabState state, PhotoRow row,
            StackPanel? listPanel, Border? footer)
        {
            string? reason = PromptForString(owner,
                "Reject photo",
                "Reason for rejection (visible to the photo's author on mobile):",
                "");
            if (reason == null) return;
            if (reason.Trim().Length < 3)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Reject photo",
                    "Rejection reason must be at least 3 characters.");
                return;
            }

            var dto = await PlanscapeServerClient.Instance
                .RejectSitePhotoAsync(state.ProjectId, row.Dto.Id, reason.Trim());
            if (dto == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Reject photo",
                    $"Server rejected the call.\n\n{PlanscapeServerClient.Instance.LastError ?? "(no detail)"}");
                return;
            }
            await ReloadAsync(owner, state, listPanel, footer);
        }

        private static async Task WithdrawAsync(
            BIMCoordinationCenter owner, TabState state, PhotoRow row,
            StackPanel? listPanel, Border? footer)
        {
            var confirm = Autodesk.Revit.UI.TaskDialog.Show("Withdraw photo",
                "Withdraw this photo from the client portal?\n\nThe photo will return to PendingReview and will not appear in future digests.",
                Autodesk.Revit.UI.TaskDialogCommonButtons.Yes |
                Autodesk.Revit.UI.TaskDialogCommonButtons.No);
            if (confirm != Autodesk.Revit.UI.TaskDialogResult.Yes) return;

            bool ok = await PlanscapeServerClient.Instance
                .WithdrawSitePhotoAsync(state.ProjectId, row.Dto.Id);
            if (!ok)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Withdraw photo",
                    $"Server rejected the withdrawal.\n\n{PlanscapeServerClient.Instance.LastError ?? "(no detail)"}");
                return;
            }
            await ReloadAsync(owner, state, listPanel, footer);
        }

        private static async Task BulkApproveAsync(
            BIMCoordinationCenter owner, TabState state, IList<Guid> ids,
            StackPanel? listPanel, Border? footer, bool ConfirmDestructive = false)
        {
            if (ids.Count == 0) return;
            string caption = state.BulkCaption?.Trim() ?? "";
            if (caption.Length < 3)
            {
                caption = PromptForString(owner,
                    "Bulk approve",
                    $"Shared caption for {ids.Count} photo{(ids.Count == 1 ? "" : "s")} (min 3 chars):",
                    "")?.Trim() ?? "";
                if (caption.Length < 3)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Bulk approve",
                        "Caption must be at least 3 characters — bulk approve cancelled.");
                    return;
                }
            }

            if (ConfirmDestructive)
            {
                var ok = Autodesk.Revit.UI.TaskDialog.Show("Bulk approve safety photos",
                    $"You're about to approve {ids.Count} SAFETY photo{(ids.Count == 1 ? "" : "s")} in one action.\n\n" +
                    "Safety hazards normally require individual review. Continue?",
                    Autodesk.Revit.UI.TaskDialogCommonButtons.Yes |
                    Autodesk.Revit.UI.TaskDialogCommonButtons.No);
                if (ok != Autodesk.Revit.UI.TaskDialogResult.Yes) return;
            }

            var (approved, skipped) = await PlanscapeServerClient.Instance
                .BulkApproveSitePhotosAsync(state.ProjectId, ids, caption);
            Autodesk.Revit.UI.TaskDialog.Show("Bulk approve",
                $"Approved: {approved}\nSkipped: {skipped}\n\n" +
                (skipped > 0
                    ? "Skipped photos may already be approved, rejected, or in a non-pending audience.\n\n"
                    : "") +
                (PlanscapeServerClient.Instance.LastError != null
                    ? $"Last error: {PlanscapeServerClient.Instance.LastError}"
                    : ""));
            state.SelectedIds.Clear();
            state.BulkCaption = "";
            await ReloadAsync(owner, state, listPanel, footer);
        }

        // ──────────────────────────────────────────────────────────────
        //  BULK FOOTER
        // ──────────────────────────────────────────────────────────────

        private static Border BuildBulkFooter(
            BIMCoordinationCenter owner, TabState state, StackPanel listPanel)
        {
            var border = new Border
            {
                Background = owner.HeaderBrushPub,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 8, 0, 0),
                CornerRadius = new CornerRadius(4)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var countTb = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "BulkCountLabel"
            };
            grid.Children.Add(countTb);

            var captionGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            captionGrid.Children.Add(new TextBlock
            {
                Text = "Caption:", Foreground = Brushes.White, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            var capBox = new TextBox
            {
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Tag = "BulkCaptionBox", Padding = new Thickness(4, 2, 4, 2)
            };
            capBox.TextChanged += (_, _) => state.BulkCaption = capBox.Text ?? "";
            Grid.SetColumn(capBox, 1);
            captionGrid.Children.Add(capBox);
            Grid.SetColumn(captionGrid, 1);
            grid.Children.Add(captionGrid);

            var approveBtn = new Button
            {
                Content = "✓ Approve N", Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Background = owner.GreenBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0),
                Tag = "BulkApproveBtn"
            };
            approveBtn.Click += async (_, _) =>
            {
                var ids = state.SelectedIds.ToList();
                await BulkApproveAsync(owner, state, ids, listPanel,
                    border /* footer is this border */);
            };
            Grid.SetColumn(approveBtn, 2);
            grid.Children.Add(approveBtn);

            var clearBtn = new Button
            {
                Content = "Clear selection", Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                FontSize = 11, Cursor = Cursors.Hand
            };
            clearBtn.Click += (_, _) =>
            {
                state.SelectedIds.Clear();
                _ = ReloadAsync(owner, state, listPanel, border);
            };
            Grid.SetColumn(clearBtn, 3);
            grid.Children.Add(clearBtn);

            border.Child = grid;
            return border;
        }

        private static void UpdateFooterVisibility(Border? footer, TabState state)
        {
            if (footer == null) return;
            int n = state.SelectedIds.Count;
            footer.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (n == 0) return;
            if (footer.Child is Grid g)
            {
                foreach (var ch in g.Children)
                {
                    if (ch is TextBlock tb && (tb.Tag as string) == "BulkCountLabel")
                        tb.Text = $"{n} selected";
                    if (ch is Button bb && (bb.Tag as string) == "BulkApproveBtn")
                        bb.Content = $"✓ Approve {n}";
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  DIGEST PREVIEW MODAL
        // ──────────────────────────────────────────────────────────────

        private static async Task ShowDigestPreviewAsync(BIMCoordinationCenter owner, TabState state)
        {
            if (!PlanscapeServerClient.Instance.IsConnected || state.ProjectId == Guid.Empty)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Digest preview",
                    "Sign in to Planscape (PLATFORM tab) and ensure a project is linked before previewing the digest.");
                return;
            }

            var preview = await PlanscapeServerClient.Instance
                .GetDigestPreviewAsync(state.ProjectId);
            if (preview == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Digest preview",
                    $"Could not load digest preview.\n\n{PlanscapeServerClient.Instance.LastError ?? "(no detail)"}");
                return;
            }

            var dlg = new Window
            {
                Title = "Today's client digest — preview",
                Width = 720, Height = 520,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.PageBrushPub
            };
            var sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock
            {
                Text = $"Digest for {preview.Date:yyyy-MM-dd}",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"Total photos: {preview.TotalPhotos}\n" +
                       $"Recipients: {(preview.Recipients.Count == 0 ? "(none configured)" : string.Join(", ", preview.Recipients))}\n" +
                       $"Subject: {preview.Subject ?? "(default)"}",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
            });

            // Per-Reason breakdown
            if (preview.ByReason != null && preview.ByReason.Count > 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "By reason", FontWeight = FontWeights.SemiBold,
                    FontSize = 12, Margin = new Thickness(0, 4, 0, 4)
                });
                foreach (var (k, v) in preview.ByReason)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 1, 0, 1) };
                    row.Children.Add(MakeReasonChip(k));
                    row.Children.Add(new TextBlock { Text = v.ToString(), FontSize = 12 });
                    sp.Children.Add(row);
                }
            }

            // Body preview
            if (!string.IsNullOrEmpty(preview.Preview))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "Body preview", FontWeight = FontWeights.SemiBold,
                    FontSize = 12, Margin = new Thickness(0, 8, 0, 4)
                });
                sp.Children.Add(new Border
                {
                    Background = Brushes.White,
                    BorderBrush = owner.BorderBrushPub,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Child = new TextBox
                    {
                        Text = preview.Preview,
                        IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
                        FontSize = 11, Background = Brushes.White,
                        BorderThickness = new Thickness(0),
                        TextWrapping = TextWrapping.Wrap, Height = 220,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                });
            }

            var closeBtn = new Button
            {
                Content = "Close", Height = 28,
                Width = 90, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Background = owner.HeaderBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11
            };
            closeBtn.Click += (_, _) => dlg.Close();
            sp.Children.Add(closeBtn);

            dlg.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = sp
            };
            dlg.ShowDialog();
        }

        // ──────────────────────────────────────────────────────────────
        //  AUTO-REFRESH
        // ──────────────────────────────────────────────────────────────

        private static void StartAutoRefresh(
            BIMCoordinationCenter owner, TabState state,
            StackPanel listPanel, Border footer)
        {
            state.RefreshTimer?.Stop();
            var timer = new DispatcherTimer { Interval = AutoRefreshInterval };
            timer.Tick += async (_, _) =>
            {
                if (!owner.IsLoaded || !PlanscapeServerClient.Instance.IsConnected) return;
                // Don't yank the UI from under the user mid-edit:
                if (state.SelectedIds.Count > 0) return;
                await ReloadAsync(owner, state, listPanel, footer);
            };
            timer.Start();
            state.RefreshTimer = timer;
        }

        // ──────────────────────────────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────────────────────────────

        private static UIElement? FindByTag(StackPanel root, string tag)
        {
            foreach (var c in root.Children)
                if (c is FrameworkElement fe && (fe.Tag as string) == tag)
                    return c as UIElement;
            return null;
        }

        /// <summary>Modal text-input dialog. Returns null if user cancels.</summary>
        private static string? PromptForString(BIMCoordinationCenter owner,
            string title, string prompt, string initialValue)
        {
            var dlg = new Window
            {
                Title = title, Width = 480, Height = 220,
                Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.PageBrushPub, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock
            {
                Text = prompt, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var box = new TextBox { Text = initialValue, FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4), Height = 28 };
            sp.Children.Add(box);
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            string? result = null;
            var ok = new Button { Content = "OK", Width = 80, Height = 28,
                Margin = new Thickness(0, 0, 6, 0), IsDefault = true,
                Background = owner.HeaderBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0) };
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

        /// <summary>Read the current Planscape project id from the document config —
        /// same source the Issues / Documents tabs use. Returns Guid.Empty if not linked.</summary>
        private static Guid ResolveProjectId()
        {
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null) return Guid.Empty;
                string bimDir = ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER");
                if (string.IsNullOrEmpty(bimDir)) return Guid.Empty;
                string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
                return PlatformSyncCommand.LoadPlanscapeProjectId(cfgPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SitePhotosTab.ResolveProjectId: {ex.Message}");
                return Guid.Empty;
            }
        }
    }
}
