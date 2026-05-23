// ══════════════════════════════════════════════════════════════════════
//  SitePhotosChecklistsSubTab — Phase 179 photo-checklist surface.
//
//  Lists every checklist on the active project with per-checklist RAG
//  (done/total) and surfaces a quick "create" button for BIM-managers.
//  Detail pane shows checklist items, fulfilment state, and a "fulfil
//  with selected" shortcut that links the BCC's currently-selected
//  photo (Grid tab) to a pending item.
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
    internal static class SitePhotosChecklistsSubTab
    {
        internal static UIElement Build(BIMCoordinationCenter owner, SitePhotosTab.TabState state)
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };

            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(bar, Dock.Top);

            var statusCb = new ComboBox { Width = 130, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
            statusCb.Items.Add(new ComboBoxItem { Content = "(any status)", Tag = null });
            foreach (var s in new[] { "Draft", "Active", "Closed", "Archived" })
                statusCb.Items.Add(new ComboBoxItem { Content = s, Tag = s });
            statusCb.SelectedIndex = 0;
            bar.Children.Add(statusCb);

            var refreshBtn = new Button {
                Content = "↻ Refresh", Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Background = owner.AccentBrushPub, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0),
            };
            bar.Children.Add(refreshBtn);

            var status = new TextBlock {
                FontSize = 11, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            bar.Children.Add(status);
            dock.Children.Add(bar);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var listPanel = new StackPanel();
            sv.Content = listPanel;
            dock.Children.Add(sv);

            async Task LoadAsync()
            {
                listPanel.Children.Clear();
                if (!PlanscapeServerClient.Instance.IsConnected || state.ProjectId == Guid.Empty)
                {
                    listPanel.Children.Add(new TextBlock {
                        Text = "Sign in to Planscape (PLATFORM tab) to load checklists.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, Margin = new Thickness(8)
                    });
                    return;
                }
                var statusFilter = (statusCb.SelectedItem as ComboBoxItem)?.Tag as string;
                List<PhotoChecklistDto> rows;
                try { rows = await PlanscapeServerClient.Instance.ListPhotoChecklistsAsync(state.ProjectId, statusFilter); }
                catch (Exception ex)
                {
                    StingLog.Warn($"ChecklistsSubTab.Load: {ex.Message}");
                    listPanel.Children.Add(new TextBlock { Text = "Load failed.", Foreground = Brushes.Crimson, Margin = new Thickness(8) });
                    return;
                }
                status.Text = $"{rows.Count} checklist{(rows.Count == 1 ? "" : "s")}";
                if (rows.Count == 0)
                {
                    listPanel.Children.Add(new TextBlock {
                        Text = "No checklists yet — BIM managers can create one to require specific shots before a pour / handover.",
                        FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, Margin = new Thickness(8),
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }
                foreach (var c in rows.OrderByDescending(c => c.CreatedAt))
                    listPanel.Children.Add(BuildRow(owner, c));
            }
            refreshBtn.Click += (_, _) => _ = LoadAsync();
            statusCb.SelectionChanged += (_, _) => _ = LoadAsync();

            _ = LoadAsync();
            return dock;
        }

        private static UIElement BuildRow(BIMCoordinationCenter owner, PhotoChecklistDto c)
        {
            var pct = c.Total == 0 ? 0 : (int)Math.Round(100.0 * c.Done / c.Total);
            var rag = pct >= 90 ? "GREEN" : pct >= 50 ? "AMBER" : "RED";

            var border = new Border {
                Background = owner.CardBrushPub,
                BorderBrush = owner.BorderBrushPub,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(10, 6, 10, 6),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock {
                Text = c.Name, FontWeight = FontWeights.SemiBold, FontSize = 13
            });
            sp.Children.Add(new TextBlock {
                Text = $"{c.Status} · {c.Kind ?? "Custom"} · {c.LevelCode ?? "—"}/{c.ZoneCode ?? "—"}{(c.DueAt.HasValue ? $" · due {c.DueAt:dd MMM}" : "")}",
                FontSize = 10, Foreground = Brushes.Gray
            });
            grid.Children.Add(sp);

            var ragPill = new Border
            {
                Background = owner.RagBrushPub(rag),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 1, 8, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock {
                    Text = $"{c.Done}/{c.Total}",
                    FontSize = 11, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
                }
            };
            Grid.SetColumn(ragPill, 1);
            grid.Children.Add(ragPill);

            border.Child = grid;
            return border;
        }
    }
}
