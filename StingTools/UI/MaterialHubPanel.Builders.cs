using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    public partial class MaterialHubPanel
    {
        // ── KPI strip ──────────────────────────────────────────────────────
        private void BuildKpiStrip()
        {
            kpiStrip.Items.Clear();
            foreach (var key in new[] { "Materials", "Σ Cost", "Σ Carbon", "EPD", "Unused", "Stale", "Peers", "Coverage" })
                kpiStrip.Items.Add(MakeKpiCard(key, "—", ""));
        }

        private static Border MakeKpiCard(string title, string value, string footer)
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)Application.Current.FindResource("HubLineBr") ?? Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(2),
                MinWidth = 96
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = Brushes.SlateGray, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, Tag = "value" });
            if (!string.IsNullOrEmpty(footer))
                sp.Children.Add(new TextBlock { Text = footer, FontSize = 9, Foreground = Brushes.Gray, Tag = "footer" });
            card.Child = sp;
            card.Tag = title;
            return card;
        }

        private void RefreshKpiValues()
        {
            if (kpiStrip == null || _rows == null) return;
            try
            {
                int total = _rows.Count;
                int costed = _rows.Count(r => r.Cost > 0);
                int carboned = _rows.Count(r => r.CarbonKgCo2e > 0);
                double sumCost = _rows.Sum(r => r.Cost);
                double sumCarbon = _rows.Sum(r => r.CarbonKgCo2e);
                int fresh = _rows.Count(r => r.EpdFreshness == EpdFreshness.Fresh);
                int stale = _rows.Count(r => r.EpdFreshness == EpdFreshness.Stale);
                int expired = _rows.Count(r => r.EpdFreshness == EpdFreshness.Expired);
                int missing = _rows.Count(r => r.EpdFreshness == EpdFreshness.Missing);
                int unused = _rows.Count(r => r.UsageCount == 0);
                double coverage = total == 0 ? 100 : 100.0 * (total - unused) / total;
                var loc = MaterialRow.ActiveLocale;
                string sym = loc?.CurrencySymbol ?? "";

                SetKpi("Materials", total.ToString(), "");
                SetKpi("Σ Cost", $"{sym}{sumCost:N0}", $"{costed}/{total} costed");
                SetKpi("Σ Carbon", $"{(sumCarbon / 1000):F1} tCO₂e", $"{carboned}/{total} carboned");
                SetKpi("EPD", $"✓{fresh} △{stale} ✗{expired} —{missing}", "");
                SetKpi("Unused", unused.ToString(), "");
                SetKpi("Stale", "—", "tag · cost");
                SetKpi("Peers", "—", "edits since last refresh");
                SetKpi("Coverage", $"{coverage:F1}%", "");
            }
            catch (Exception ex) { StingLog.Warn($"RefreshKpiValues: {ex.Message}"); }
        }

        private void SetKpi(string title, string value, string footer)
        {
            foreach (var item in kpiStrip.Items)
            {
                if (item is Border b && (b.Tag as string) == title && b.Child is StackPanel sp)
                {
                    foreach (var child in sp.Children.OfType<TextBlock>())
                    {
                        if ((child.Tag as string) == "value")  child.Text = value;
                        else if ((child.Tag as string) == "footer") child.Text = footer ?? "";
                    }
                    return;
                }
            }
        }

        // ── Action bar (6 grouped sections) ────────────────────────────────
        private void BuildActionBar()
        {
            actionBar.Items.Clear();
            actionBar.Items.Add(MakeActionGroup("FILE",       new[]
            {
                ("Export CSV",       "MAT_ExportCsv"),
                ("Import CSV…",      "MAT_ImportCsv"),
                ("Open Template",    "MAT_TemplateCsv"),
                ("Audit Family…",    "MAT_FamilyAudit"),
                ("Generate RFQ",     "MAT_GenerateRfq"),
            }));
            actionBar.Items.Add(MakeActionGroup("LIBRARY",    new[]
            {
                ("Edit Overrides",   "MAT_EditOverrides"),
                ("Reload",           "MAT_ReloadLib"),
                ("Push Corporate",   "MAT_PushCorp"),
                ("Load Pack…",       "MAT_LoadPack"),
                ("Normalise Classes","MAT_NormaliseClasses"),
            }));
            actionBar.Items.Add(MakeActionGroup("AUTOMATION", new[]
            {
                ("⚙ Auto-Apply",     "MAT_ToggleAutoApply"),
                ("⚙ Auto-Fill",      "MAT_ToggleAutoFill"),
                ("Edit Rules",       "MAT_EditRules"),
                ("Rebuild Caches",   "HUB_RebuildCaches"),
            }));
            actionBar.Items.Add(MakeActionGroup("GATES",      new[]
            {
                ("Coverage",         "MAT_CoverageCheck"),
                ("Sustainability",   "MAT_SustainabilityGate"),
                ("Healthcare",       "MAT_HealthcareGate"),
                ("Fire-Wall",        "MAT_FireWallGate"),
                ("EPD Format",       "MAT_EpdFormatCheck"),
            }));
            actionBar.Items.Add(MakeActionGroup("PIVOT",      new[]
            {
                ("BOQ by Material",  "MAT_BoqByMaterial"),
                ("What-If…",         "MAT_WhatIfSwap"),
                ("Carbon by Phase",  "MAT_CarbonPivot"),
            }));
            actionBar.Items.Add(MakeActionGroup("CONNECT",    new[]
            {
                ("Sync COBie",       "MAT_SyncCobie"),
                ("Linked Materials", "MAT_LinkedScan"),
                ("Enrich Schedules", "MAT_EnrichSchedules"),
                ("Make Legend",      "MaterialLegend"),
            }));
        }

        private Border MakeActionGroup(string header, (string label, string tag)[] items)
        {
            var border = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 6, 0),
                Margin = new Thickness(2)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = header, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SlateGray, Margin = new Thickness(2) });
            var wp = new WrapPanel();
            foreach (var (label, tag) in items)
            {
                var b = new Button
                {
                    Content = label,
                    Tag = tag,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(1),
                    FontSize = 10,
                    MinWidth = 90,
                    Background = Brushes.White,
                };
                b.Click += HubBtn_Click;
                wp.Children.Add(b);
            }
            sp.Children.Add(wp);
            border.Child = sp;
            return border;
        }

        // ── Navigation tree ────────────────────────────────────────────────
        private void BuildNavTreeSkeleton()
        {
            navTree.Items.Clear();
            navTree.Items.Add(MakeNavRoot("ALL MATERIALS", new[]
            {
                MakeNavLeaf("STING-origin", r => r.Origin == "STING"),
                MakeNavLeaf("BLE-origin",   r => r.Origin == "BLE"),
                MakeNavLeaf("MEP-origin",   r => r.Origin == "MEP"),
                MakeNavLeaf("Other",        r => r.Origin == "Other"),
            }));
            navTree.Items.Add(MakeNavRoot("BY CLASS", null));     // populated on refresh
            navTree.Items.Add(MakeNavRoot("ISSUES", new[]
            {
                MakeNavLeaf("Unused",      r => r.UsageCount == 0),
                MakeNavLeaf("Missing EPD", r => r.EpdFreshness == EpdFreshness.Missing),
                MakeNavLeaf("Stale EPD",   r => r.EpdFreshness == EpdFreshness.Stale || r.EpdFreshness == EpdFreshness.Expired),
                MakeNavLeaf("Off-baseline",r => r.Origin == "Other"),
            }));
            navTree.Items.Add(MakeNavRoot("PACKS", null));       // populated on refresh
        }

        private void RefreshNavTreeCounts()
        {
            // Update class facet under "BY CLASS".
            try
            {
                if (_rows == null) return;
                var byClassRoot = navTree.Items.OfType<TreeViewItem>()
                    .FirstOrDefault(t => (t.Header as string)?.StartsWith("BY CLASS") == true);
                if (byClassRoot == null) return;
                byClassRoot.Items.Clear();
                foreach (var g in _rows.GroupBy(r => r.Class ?? "", StringComparer.OrdinalIgnoreCase)
                                       .OrderByDescending(g => g.Count()).Take(20))
                {
                    string cls = g.Key;
                    byClassRoot.Items.Add(MakeNavLeaf($"{cls} ({g.Count()})",
                        r => string.Equals(r.Class, cls, StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception ex) { StingLog.Warn($"RefreshNavTreeCounts: {ex.Message}"); }
        }

        private TreeViewItem MakeNavRoot(string header, IEnumerable<TreeViewItem> children)
        {
            var tvi = new TreeViewItem
            {
                Header = header,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                IsExpanded = true,
            };
            if (children != null)
                foreach (var c in children) tvi.Items.Add(c);
            return tvi;
        }

        private TreeViewItem MakeNavLeaf(string label, Predicate<MaterialRow> predicate)
        {
            return new TreeViewItem
            {
                Header = label,
                FontWeight = FontWeights.Normal,
                FontSize = 11,
                Tag = new FilterChip { Label = label, Predicate = predicate },
            };
        }

        // ── Inspector (card stack) ─────────────────────────────────────────
        private void BuildInspectorPlaceholder()
        {
            inspectorStack.Children.Clear();
            inspectorStack.Children.Add(new TextBlock
            {
                Text = "Pick a material in the grid to inspect.",
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.SlateGray,
                Margin = new Thickness(8)
            });
        }

        private void RebuildInspector(MaterialRow row)
        {
            inspectorStack.Children.Clear();
            if (row == null) { BuildInspectorPlaceholder(); return; }

            inspectorStack.Children.Add(BuildIdentityCard(row));
            inspectorStack.Children.Add(BuildCostCard(row));
            inspectorStack.Children.Add(BuildCarbonCard(row));
            inspectorStack.Children.Add(BuildAppearanceHatchCard(row));
            inspectorStack.Children.Add(BuildAssetsCard(row));
            inspectorStack.Children.Add(BuildLifecycleCard(row));
            inspectorStack.Children.Add(BuildActionsCard(row));
        }

        private Border MakeCard(string title, UIElement body)
        {
            var br = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(body);
            br.Child = sp;
            return br;
        }

        private UIElement BuildIdentityCard(MaterialRow row)
        {
            var sp = new StackPanel();
            // Live render swatch (priority 10 — placeholder until BitmapPreview wiring)
            var swatch = new Border
            {
                Width = 64, Height = 64,
                Background = row.ColorSwatch ?? Brushes.LightGray,
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6),
            };
            sp.Children.Add(swatch);
            sp.Children.Add(KvRow("Name", row.Name ?? ""));
            sp.Children.Add(KvRow("Class", row.Class ?? ""));
            sp.Children.Add(KvRow("Origin", row.Origin ?? ""));
            sp.Children.Add(KvRow("Uniclass", row.UniclassCode ?? ""));
            sp.Children.Add(KvRow("Used", row.UsageCount.ToString()));
            return MakeCard("Identity", sp);
        }

        private UIElement BuildCostCard(MaterialRow row)
        {
            var loc = MaterialRow.ActiveLocale;
            string sym = loc?.CurrencySymbol ?? "";
            var sp = new StackPanel();
            sp.Children.Add(KvRow("Supply",  $"{sym}{row.SupplyCost:N2}"));
            sp.Children.Add(KvRow("Install", $"{sym}{row.InstallCost:N2}"));
            sp.Children.Add(KvRow("VAT %",   $"{row.VatPct:F1}%"));
            sp.Children.Add(KvRow("Total",   $"{sym}{row.Cost:N2}"));
            return MakeCard("Cost", sp);
        }

        private UIElement BuildCarbonCard(MaterialRow row)
        {
            var sp = new StackPanel();
            sp.Children.Add(KvRow("Factor", row.CarbonKgCo2e > 0 ? $"{row.CarbonKgCo2e:F0} kgCO₂e/m³" : "(none)"));
            sp.Children.Add(KvRow("EPD source", row.EpdSource ?? ""));
            sp.Children.Add(KvRow("EPD date",   row.EpdDate ?? ""));
            var fresh = new TextBlock
            {
                Text = row.EpdFreshnessText,
                Foreground = row.EpdFreshnessBrush,
                FontWeight = FontWeights.SemiBold,
            };
            sp.Children.Add(fresh);
            return MakeCard("Carbon (EPD)", sp);
        }

        private UIElement BuildAppearanceHatchCard(MaterialRow row)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Texture, fill patterns, and colours — inline pickers below.", FontSize = 10, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddRow(grid, 0, "Texture",      "Browse…",   "HUB_PickTexture",  row.Id);
            AddRow(grid, 1, "Surface FG",   "Pattern…",  "HUB_PickSurfaceFg",row.Id);
            AddRow(grid, 2, "Surface BG",   "Pattern…",  "HUB_PickSurfaceBg",row.Id);
            AddRow(grid, 3, "Cut FG",       "Pattern…",  "HUB_PickCutFg",    row.Id);
            sp.Children.Add(grid);

            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            actions.Children.Add(MakeAction("Color picker…", "HUB_PickColor"));
            actions.Children.Add(MakeAction("Reset hatch", "HUB_ResetHatch"));
            sp.Children.Add(actions);
            return MakeCard("Appearance / Hatch", sp);
        }

        private void AddRow(Grid g, int row, string label, string action, string tag, Autodesk.Revit.DB.ElementId id)
        {
            var lab = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lab, row); Grid.SetColumn(lab, 0); g.Children.Add(lab);
            var preview = new Border
            {
                Background = Brushes.WhiteSmoke,
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Height = 18, Margin = new Thickness(0, 1, 4, 1),
                Child = new TextBlock { Text = "—", FontSize = 9, Foreground = Brushes.SlateGray, Margin = new Thickness(4, 0, 0, 0) },
            };
            Grid.SetRow(preview, row); Grid.SetColumn(preview, 1); g.Children.Add(preview);
            var btn = new Button { Content = action, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 1, 0, 1), FontSize = 10, Tag = tag };
            btn.Click += HubBtn_Click;
            Grid.SetRow(btn, row); Grid.SetColumn(btn, 2); g.Children.Add(btn);
        }

        private Button MakeAction(string label, string tag)
        {
            var b = new Button { Content = label, Tag = tag, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), FontSize = 10 };
            b.Click += HubBtn_Click;
            return b;
        }

        private UIElement BuildAssetsCard(MaterialRow row)
        {
            var sp = new StackPanel();
            sp.Children.Add(KvRow("Appearance shared by", row.AppearanceSharedBy.ToString()));
            sp.Children.Add(KvRow("Physical shared by",   row.PhysicalSharedBy.ToString()));
            sp.Children.Add(KvRow("Thermal shared by",    row.ThermalSharedBy.ToString()));
            var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            actions.Children.Add(MakeAction("Detach", "MAT_DetachAsset"));
            actions.Children.Add(MakeAction("Repoint…", "MAT_RepointAsset"));
            sp.Children.Add(actions);
            return MakeCard("Assets", sp);
        }

        private UIElement BuildLifecycleCard(MaterialRow row)
        {
            // Lifecycle states (priority 9): Draft → Reviewed → Approved → Frozen.
            // The state is read from STING_MAT_LIFECYCLE_TXT via MaterialLifecycle.
            var current = MaterialLifecycle.Read(row);
            var sp = new StackPanel();
            var chain = new WrapPanel();
            foreach (var state in MaterialLifecycle.States)
            {
                var active = string.Equals(state, current, StringComparison.OrdinalIgnoreCase);
                var pill = new Border
                {
                    Background = active ? Brushes.SteelBlue : Brushes.LightGray,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = new TextBlock { Text = state, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = active ? Brushes.White : Brushes.SlateGray },
                };
                pill.MouseLeftButtonDown += (_, __) => { MaterialLifecycle.Set(row, state); RebuildInspector(row); Toast($"Lifecycle → {state}", "ok"); };
                pill.Cursor = System.Windows.Input.Cursors.Hand;
                chain.Children.Add(pill);
            }
            sp.Children.Add(chain);
            return MakeCard("Lifecycle", sp);
        }

        private UIElement BuildActionsCard(MaterialRow row)
        {
            var wp = new WrapPanel();
            wp.Children.Add(MakeAction("Apply → Sel", "MAT_Apply"));
            wp.Children.Add(MakeAction("Eyedropper", "MAT_Eyedropper"));
            wp.Children.Add(MakeAction("Where Used", "MAT_WhereUsed"));
            wp.Children.Add(MakeAction("Edit Identity", "MAT_EditIdentity"));
            wp.Children.Add(MakeAction("Make Legend", "MaterialLegend"));
            return MakeCard("Actions", wp);
        }

        private static UIElement KvRow(string key, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var k = new TextBlock { Text = key, FontSize = 10, Foreground = Brushes.SlateGray };
            var v = new TextBlock { Text = value, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(k, 0); Grid.SetColumn(v, 1);
            g.Children.Add(k); g.Children.Add(v);
            return g;
        }

        // ── Dispatch (all action tags route here) ──────────────────────────
        private void HubDispatch(string tag)
        {
            try { StingDockPanel.DispatchCommand(tag, GetSelectedIdString()); }
            catch (Exception ex) { Toast($"Dispatch '{tag}' failed: {ex.Message}", "error"); }
        }

        private string GetSelectedIdString()
        {
            if (dgHubMaterials?.SelectedItem is MaterialRow row && row.Id != null && row.Id.Value > 0)
                return row.Id.Value.ToString();
            return "";
        }
    }
}
