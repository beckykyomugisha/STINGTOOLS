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
//  All operations route through PlanscapeServerClient. The SERVER remains
//  the gate. What the desktop surface adds (#558) is affordance: it asks
//  the server what this user can do (GET members/capabilities) and
//  disables only what the server has explicitly said no to, naming the
//  capability. A capability we could not determine — unreachable server,
//  timeout, unparseable body — leaves the control enabled and lets the
//  attempt report. Unknown is not denied.
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
            var bulkButtons = new List<Button>();
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
                    if (n > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Reclassify",
                            $"Reclassified {n} photo(s) to {code}.");
                        return;
                    }
                    PlanscapeForbidden.ShowFailureOrForbidden(
                        "Reclassify", PlanscapeCapability.ApproveSitePhotos);
                };
                bulkBar.Children.Add(b);
                bulkButtons.Add(b);
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
                if (n > 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Re-anchor", $"Re-anchored {n} photo(s).");
                    return;
                }
                PlanscapeForbidden.ShowFailureOrForbidden(
                    "Re-anchor", PlanscapeCapability.ApproveSitePhotos);
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
                // null = the load FAILED; an empty list = there are genuinely no
                // groups. "No distribution groups yet." over an unreachable server
                // is invented data — and here it is actively dangerous, because an
                // operator could conclude nobody is on distribution and re-add
                // recipients who are already there.
                var groups = await PlanscapeServerClient.Instance.ListDistributionGroupsAsync(state.ProjectId);
                if (groups == null)
                {
                    dgPanel.Children.Add(PlanscapeForbidden.BuildFailureOrForbidden(
                        "Could not load distribution groups.",
                        "Distribution groups are not available to you on this project.",
                        PlanscapeCapability.CurateProject));
                    return;
                }
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

                    // Members were only ever counted, never listed or editable —
                    // so a group could be created but never populated from here.
                    var memberLine = new TextBlock {
                        FontSize = 10, Foreground = Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                    };
                    sp.Children.Add(memberLine);

                    var grp = g;
                    async Task LoadMembersAsync()
                    {
                        var mem = await PlanscapeServerClient.Instance
                            .ListDistributionGroupMembersAsync(state.ProjectId, grp.Id);
                        // null is a FAILED load, not an empty group. Saying "No members
                        // yet" here is the same fabrication #550 removed from the album
                        // pane: an operator would conclude nobody is on distribution and
                        // re-add recipients who are already there.
                        memberLine.Text = mem == null
                            ? (PlanscapeServerClient.Instance.LastStatus == 403
                                ? "🔒 " + PlanscapeForbidden.Describe(PlanscapeCapability.CurateProject)
                                : "Could not load members — "
                                  + (PlanscapeServerClient.Instance.LastError ?? "(no detail)"))
                            : mem.Count == 0
                                ? "No members yet."
                                : string.Join(", ", mem.Select(m =>
                                    m.IsProjectMember ? m.Label : m.Label + " (external)"));
                    }

                    var addMemberBtn = new Button {
                        Content = "＋ Add member", Height = 22, Padding = new Thickness(8, 0, 8, 0),
                        Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gainsboro,
                        BorderThickness = new Thickness(1), FontSize = 10, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
                    };
                    addMemberBtn.Click += async (_, _) =>
                    {
                        // Members come from the canonical project roster; the
                        // server's ExternalEmail column is the escape hatch for
                        // people outside the project (a client contact on a
                        // distribution list who has no Planscape account).
                        var roster = StingTools.Core.ProjectRoster.LoadForProject(state.ProjectId);
                        const string externalOpt = "[External — not a project member]";
                        var picks = roster
                            .Select(m => string.IsNullOrWhiteSpace(m.Email) ? m.Display : $"{m.Display} — {m.Email}")
                            .ToList();
                        picks.Add(externalOpt);

                        string pick = StingTools.Select.StingListPicker.Show($"Add to {grp.Name}",
                            roster.Count > 0
                                ? "Select a project member:"
                                : "No project members found — add an external address instead.",
                            picks);
                        if (string.IsNullOrEmpty(pick)) return;

                        bool ok;
                        if (pick == externalOpt)
                        {
                            var email = SitePhotosTabHelpers.PromptForString(owner,
                                "External recipient", "Email address:", "");
                            if (string.IsNullOrWhiteSpace(email)) return;
                            ok = await PlanscapeServerClient.Instance.AddDistributionGroupMemberAsync(
                                state.ProjectId, grp.Id, externalEmail: email.Trim());
                        }
                        else
                        {
                            int dash = pick.IndexOf(" — ", StringComparison.Ordinal);
                            string nm = dash > 0 ? pick.Substring(0, dash) : pick;
                            var member = roster.FirstOrDefault(m =>
                                string.Equals(m.Display, nm, StringComparison.OrdinalIgnoreCase));
                            if (member?.ServerUserId == null)
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Add member",
                                    $"\"{nm}\" has no server account, so they cannot be added as a " +
                                    "project member. Add them as an external recipient instead.");
                                return;
                            }
                            ok = await PlanscapeServerClient.Instance.AddDistributionGroupMemberAsync(
                                state.ProjectId, grp.Id, userId: member.ServerUserId,
                                displayName: member.Display);
                        }

                        if (!ok)
                        {
                            PlanscapeForbidden.ShowFailureOrForbidden(
                                "Add member", PlanscapeCapability.CurateProject);
                            return;
                        }
                        await LoadMembersAsync();
                    };
                    sp.Children.Add(addMemberBtn);

                    b.Child = sp;
                    dgPanel.Children.Add(b);
                    _ = LoadMembersAsync();
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
                    PlanscapeForbidden.ShowFailureOrForbidden(
                        "New group", PlanscapeCapability.CurateProject);
                    return;
                }
                // Non-null with LastError set is partial success: the group exists but
                // some recipients did not land. No recipients are passed here today, so
                // this cannot fire yet — it is present so that adding them later cannot
                // make the partial failure silent.
                if (PlanscapeServerClient.Instance.LastError is { } partial)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("New group", partial);
                }
                await LoadGroupsAsync();
            };

            _ = LoadGroupsAsync();

            // ── Affordance from capabilities (#547 / #558) ──────────
            // Two different capabilities on one pane, and they are NOT the
            // same set of people: bulk reclassify / re-anchor rewrite the
            // audience machine and need ApproveSitePhotos, while distribution
            // groups are curation. Gating both on one flag would tell a
            // coordinator they cannot do something they can.
            bool bannerShown = false;
            void ApplyCaps()
            {
                foreach (var b in bulkButtons)
                    PlanscapeForbidden.ApplyIfDenied(b, state.Caps.ApproveSitePhotos,
                        PlanscapeCapability.ApproveSitePhotos);
                PlanscapeForbidden.ApplyIfDenied(reanchorBtn, state.Caps.ApproveSitePhotos,
                    PlanscapeCapability.ApproveSitePhotos);
                PlanscapeForbidden.ApplyIfDenied(dgNew, state.Caps.CurateProject,
                    PlanscapeCapability.CurateProject);

                if (bannerShown) return;
                var banner = PlanscapeForbidden.BuildBannerIfDenied(
                    state.Caps.ApproveSitePhotos, PlanscapeCapability.ApproveSitePhotos)
                    ?? PlanscapeForbidden.BuildBannerIfDenied(
                        state.Caps.CurateProject, PlanscapeCapability.CurateProject);
                if (banner == null) return;
                bannerShown = true;
                root.Children.Insert(0, banner);
            }
            state.CapabilitiesResolved += ApplyCaps;
            ApplyCaps();

            // ── Section: Help ───────────────────────────────────────
            root.Children.Add(SectionHeader("Notes"));
            root.Children.Add(new TextBlock {
                // Sourced from the shared helper rather than retyped — this line
                // and the forbidden panels must never drift apart.
                Text = "• " + PlanscapeForbidden.Describe(PlanscapeCapability.ApproveSitePhotos) + "\n" +
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
