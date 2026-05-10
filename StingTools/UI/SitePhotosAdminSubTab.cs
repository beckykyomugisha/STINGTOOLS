// ══════════════════════════════════════════════════════════════════════
//  SitePhotosAdminSubTab — Phase 179 BIM-manager surface for the BCC.
//
//  Exposes the author-only operations that coordinators don't see:
//    * Distribution-group editor (named recipient lists)
//    * Bulk re-classify  — Reason rewrite across selected ids
//    * Bulk re-anchor    — Level / Zone rewrite across selected ids
//    * Bulk force-state  — admin-only override of the audience machine
//    * Re-redact         — re-run the blur worker on a single photo
//    * Audit log probe   — last 50 audit events for site photos on this project
//
//  All operations route through PlanscapeServerClient. Server enforces
//  the actual permission gate (PM / Admin / Owner only); the desktop
//  surface just hides the UI from non-curators on a best-effort basis.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.UI
{
    internal static class SitePhotosAdminSubTab
    {
        internal static UIElement Build(BIMCoordinationCenter owner, SitePhotosTab.TabState state)
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(16) };
            sv.Content = root;

            // ── Section: Bulk operations ────────────────────────────
            root.Children.Add(SectionHeader("Bulk operations on selection"));
            var sel = new TextBlock {
                Text = $"{state.SelectedIds.Count} photo{(state.SelectedIds.Count == 1 ? "" : "s")} selected (use the Grid tab to select).",
                FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(sel);

            var bulkBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, code) in SitePhotosTab.Reasons.Select(r => (r.Label, r.Code)))
            {
                var b = new Button {
                    Content = $"→ {label}",
                    Height = 26, Padding = new Thickness(8, 0, 8, 0),
                    Background = Brushes.WhiteSmoke,
                    BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(1),
                    FontSize = 11, Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 4, 4),
                    ToolTip = $"Bulk reclassify selected photos to '{label}'",
                    Tag = code
                };
                b.Click += async (_, _) =>
                {
                    if (state.SelectedIds.Count == 0) return;
                    var n = await PlanscapeServerClient.Instance.BulkReclassifyPhotosAsync(
                        state.ProjectId, state.SelectedIds.ToList(), code);
                    Autodesk.Revit.UI.TaskDialog.Show("Reclassify",
                        n > 0 ? $"Reclassified {n} photo(s) to {code}." :
                        (PlanscapeServerClient.Instance.LastError ?? "(no detail)"));
                };
                bulkBar.Children.Add(b);
            }
            root.Children.Add(bulkBar);

            var reanchorBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var levelBox = new TextBox { Width = 80, Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            var zoneBox  = new TextBox { Width = 80, Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            reanchorBar.Children.Add(new TextBlock { Text = "Re-anchor: Level", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            reanchorBar.Children.Add(levelBox);
            reanchorBar.Children.Add(new TextBlock { Text = "Zone", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            reanchorBar.Children.Add(zoneBox);
            var reanchorBtn = new Button {
                Content = "Apply", Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Background = owner.AccentBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
            };
            reanchorBtn.Click += async (_, _) =>
            {
                if (state.SelectedIds.Count == 0) return;
                var lvl = string.IsNullOrWhiteSpace(levelBox.Text) ? null : levelBox.Text.Trim();
                var zn  = string.IsNullOrWhiteSpace(zoneBox.Text)  ? null : zoneBox.Text.Trim();
                if (lvl == null && zn == null) return;
                var n = await PlanscapeServerClient.Instance.BulkReanchorPhotosAsync(
                    state.ProjectId, state.SelectedIds.ToList(), levelCode: lvl, zoneCode: zn);
                Autodesk.Revit.UI.TaskDialog.Show("Re-anchor",
                    n > 0 ? $"Re-anchored {n} photo(s)." :
                    (PlanscapeServerClient.Instance.LastError ?? "(no detail)"));
            };
            reanchorBar.Children.Add(reanchorBtn);
            root.Children.Add(reanchorBar);

            // ── Section: Distribution groups ────────────────────────
            root.Children.Add(SectionHeader("Distribution groups"));
            var dgPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(dgPanel);
            var dgRefresh = new Button {
                Content = "↻ Reload groups", Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1), FontSize = 11, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 4),
            };
            var dgNew = new Button {
                Content = "＋ New group", Height = 24, Padding = new Thickness(10, 0, 10, 0),
                Background = owner.AccentBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 4),
            };
            var dgBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            dgBar.Children.Add(dgRefresh);
            dgBar.Children.Add(dgNew);
            root.Children.Add(dgBar);

            async Task LoadGroupsAsync()
            {
                dgPanel.Children.Clear();
                if (!PlanscapeServerClient.Instance.IsConnected || state.ProjectId == Guid.Empty)
                {
                    dgPanel.Children.Add(new TextBlock {
                        Text = "Sign in to Planscape to manage distribution groups.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray
                    });
                    return;
                }
                var groups = await PlanscapeServerClient.Instance.ListDistributionGroupsAsync(state.ProjectId);
                if (groups.Count == 0)
                {
                    dgPanel.Children.Add(new TextBlock {
                        Text = "No distribution groups yet.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray
                    });
                    return;
                }
                foreach (var g in groups)
                {
                    var b = new Border {
                        Background = owner.CardBrushPub,
                        BorderBrush = owner.BorderBrushPub, BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 3),
                        CornerRadius = new CornerRadius(4)
                    };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock {
                        Text = g.Name, FontWeight = FontWeights.SemiBold, FontSize = 12
                    });
                    sp.Children.Add(new TextBlock {
                        Text = $"{g.Kind} · {g.MemberCount} member{(g.MemberCount == 1 ? "" : "s")}" +
                               $"{(g.IncludeInDailyDigest ? " · digest" : "")}{(g.ForceRedacted ? " · redacted" : "")}",
                        FontSize = 10, Foreground = Brushes.Gray
                    });
                    b.Child = sp;
                    dgPanel.Children.Add(b);
                }
            }

            dgRefresh.Click += (_, _) => _ = LoadGroupsAsync();
            dgNew.Click += async (_, _) =>
            {
                var name = SitePhotosTabHelpers.PromptForString(owner,
                    "New distribution group", "Group name (required):", "");
                if (string.IsNullOrWhiteSpace(name)) return;
                var grp = await PlanscapeServerClient.Instance.CreateDistributionGroupAsync(
                    state.ProjectId, name.Trim(), kind: "Internal");
                if (grp == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("New group",
                        PlanscapeServerClient.Instance.LastError ?? "(no detail)");
                    return;
                }
                await LoadGroupsAsync();
            };

            _ = LoadGroupsAsync();

            // ── Section: Help ───────────────────────────────────────
            root.Children.Add(SectionHeader("Notes"));
            root.Children.Add(new TextBlock {
                Text = "• Bulk operations require PM, Admin, or Owner role on the server.\n" +
                       "• Force-state and audit-log endpoints are reachable via the web admin only.\n" +
                       "• The watermark / retention / digest hour are edited under the project's\n" +
                       "  Photo Policy (PUT /api/projects/{id}/photo-policy) — a future BCC slice\n" +
                       "  will surface those inline; until then use the web admin.",
                FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            return sv;
        }

        private static UIElement SectionHeader(string text) =>
            new TextBlock {
                Text = text, FontSize = 14, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 6)
            };
    }
}
