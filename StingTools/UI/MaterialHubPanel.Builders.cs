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

        // The "HubLineBr" brush lives in the Page's local resources, so the
        // application-scope FindResource used previously threw "resource not
        // found" (logged on every InitialPopulate). Resolve it safely with a
        // frozen fallback matching the XAML colour (#D0D7E0).
        private static Brush _hubLineBrush;
        private static Brush HubLineBrush()
        {
            if (_hubLineBrush != null) return _hubLineBrush;
            try { _hubLineBrush = Application.Current?.TryFindResource("HubLineBr") as Brush; }
            catch { /* never throw building a card */ }
            _hubLineBrush ??= new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD7, 0xE0));
            if (_hubLineBrush.CanFreeze && !_hubLineBrush.IsFrozen) _hubLineBrush.Freeze();
            return _hubLineBrush;
        }

        private static Border MakeKpiCard(string title, string value, string footer)
        {
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = HubLineBrush(),
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
            actionBar.Items.Add(MakeActionGroup("TEXTURES",   new[]
            {
                ("Browse Library…",  "Pbr_BrowseLibrary"),
                ("Bulk Apply",       "Pbr_BulkApply"),
                ("Apply Pack…",      "HUB_PBR_ApplyFolder"),
                ("Reload Providers", "Pbr_ReloadProviders"),
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
        // Skeleton + counts are the SAME full rebuild — the tree always
        // reflects the live _rows, grouped by Origin / Class / Lifecycle with
        // live counts. Selecting any leaf filters the grid via its FilterChip.
        private void BuildNavTreeSkeleton() => BuildNavTree();
        private void RefreshNavTreeCounts() => BuildNavTree();

        private string ClassKey(MaterialRow r)
            => string.IsNullOrWhiteSpace(r.Class) ? "(unclassified)" : r.Class.Trim();

        private TreeViewItem MakeNavLeafCounted(string label, Predicate<MaterialRow> predicate)
        {
            int n = _rows?.Count(r => predicate(r)) ?? 0;
            return MakeNavLeaf($"{label} ({n})", predicate);
        }

        private void BuildNavTree()
        {
            if (navTree == null) return;
            navTree.Items.Clear();

            int total = _rows?.Count ?? 0;

            // ALL MATERIALS — header is itself a "show everything" chip.
            var allRoot = MakeNavRoot($"ALL MATERIALS ({total})", new[]
            {
                MakeNavLeafCounted("STING-origin", r => r.Origin == "STING"),
                MakeNavLeafCounted("BLE-origin",   r => r.Origin == "BLE"),
                MakeNavLeafCounted("MEP-origin",   r => r.Origin == "MEP"),
                MakeNavLeafCounted("Other-origin", r => r.Origin == "Other"),
            });
            allRoot.Tag = new FilterChip { Label = "All materials", Predicate = r => true };
            navTree.Items.Add(allRoot);

            // BY CLASS — one leaf per distinct material class, busiest first.
            var byClass = MakeNavRoot("BY CLASS", null);
            if (_rows != null)
            {
                foreach (var g in _rows.GroupBy(ClassKey, StringComparer.OrdinalIgnoreCase)
                                       .OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
                {
                    string cls = g.Key;
                    byClass.Items.Add(MakeNavLeaf($"{cls} ({g.Count()})",
                        r => string.Equals(ClassKey(r), cls, StringComparison.OrdinalIgnoreCase)));
                }
            }
            navTree.Items.Add(byClass);

            // BY LIFECYCLE — usage + EPD state.
            navTree.Items.Add(MakeNavRoot("BY LIFECYCLE", new[]
            {
                MakeNavLeafCounted("In use",      r => r.UsageCount > 0),
                MakeNavLeafCounted("Unused",      r => r.UsageCount == 0),
                MakeNavLeafCounted("EPD fresh",   r => r.EpdFreshness == EpdFreshness.Fresh),
                MakeNavLeafCounted("EPD stale",   r => r.EpdFreshness == EpdFreshness.Stale || r.EpdFreshness == EpdFreshness.Expired),
                MakeNavLeafCounted("EPD missing", r => r.EpdFreshness == EpdFreshness.Missing),
            }));

            // ISSUES — actionable problems.
            navTree.Items.Add(MakeNavRoot("ISSUES", new[]
            {
                MakeNavLeafCounted("Unused",       r => r.UsageCount == 0),
                MakeNavLeafCounted("Missing EPD",  r => r.EpdFreshness == EpdFreshness.Missing),
                MakeNavLeafCounted("Stale EPD",    r => r.EpdFreshness == EpdFreshness.Stale || r.EpdFreshness == EpdFreshness.Expired),
                MakeNavLeafCounted("Off-baseline", r => r.Origin == "Other"),
            }));
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
            inspectorStack.Children.Add(BuildPbrTexturesCard(row));
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
            try
            {
                if (tag != null && tag.StartsWith("HUB_", StringComparison.Ordinal))
                {
                    HandleHubLocal(tag);
                    return;
                }
                StingDockPanel.DispatchCommand(tag, GetSelectedIdString());
            }
            catch (Exception ex) { Toast($"Dispatch '{tag}' failed: {ex.Message}", "error"); }
        }

        private string GetSelectedIdString()
        {
            if (dgHubMaterials?.SelectedItem is MaterialRow row && row.Id != null && row.Id.Value > 0)
                return row.Id.Value.ToString();
            return "";
        }

        /// <summary>HUB_* tags handled in-process (no external event).
        /// Appearance / hatch / colour pickers + bookmark / refresh / etc.</summary>
        private void HandleHubLocal(string tag)
        {
            var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
            var row = dgHubMaterials?.SelectedItem as MaterialRow;
            var mat = (row != null && doc != null) ? doc.GetElement(row.Id) as Autodesk.Revit.DB.Material : null;

            // PBR tags handled in MaterialHubPanel.Textures.cs partial.
            if (HandlePbrTag(tag, doc, row, mat)) return;

            switch (tag)
            {
                case "HUB_Refresh":      Refresh(); return;
                case "HUB_Settings":     Toast("Settings — coming next commit.", "info"); return;
                case "HUB_Help":         Toast("F5 refresh · Ctrl+F search · right-click row for actions.", "info"); return;
                case "HUB_RebuildCaches":MaterialNameCache.InvalidateAll(); MaterialUsageIndex.InvalidateAll(); Refresh(); Toast("Caches rebuilt.", "ok"); return;
                case "HUB_Bookmark":
                    if (row != null) { row.IsBookmarked = !row.IsBookmarked; Toast(row.IsBookmarked ? "★ Bookmarked" : "Bookmark cleared", "ok"); }
                    return;
                case "HUB_AddToPack":    Toast("Add-to-pack picker — coming next commit.", "info"); return;
                case "HUB_Compare":      Toast("Compare panel — coming next commit.", "info"); return;
                case "HUB_PickTexture":
                    if (mat == null) { Toast("Pick a material in the grid first.", "warn"); return; }
                    MaterialHubFlyouts.ShowTexturePicker(GetButtonAnchor("HUB_PickTexture"), doc, mat,
                        path =>
                        {
                            bool ok = MaterialAppearanceActions.SetTexturePath(doc, mat, path);
                            Toast(ok ? $"Texture set: {System.IO.Path.GetFileName(path)}" : "Texture write failed.",
                                  ok ? "ok" : "error");
                        });
                    return;
                case "HUB_PickSurfaceFg":
                case "HUB_PickSurfaceBg":
                case "HUB_PickCutFg":
                case "HUB_PickCutBg":
                {
                    if (mat == null) { Toast("Pick a material first.", "warn"); return; }
                    string slot = tag.Substring("HUB_Pick".Length);
                    MaterialHubFlyouts.ShowHatchPicker(GetButtonAnchor(tag), doc, mat, slot,
                        (id, name) =>
                        {
                            bool ok = MaterialAppearanceActions.SetHatchPattern(doc, mat, slot, id);
                            Toast(ok ? $"{slot} → {name}" : "Pattern write failed.", ok ? "ok" : "error");
                            if (ok) ReloadSingleRow(row.Id);
                        });
                    return;
                }
                case "HUB_PickColor":
                {
                    if (mat == null) { Toast("Pick a material first.", "warn"); return; }
                    MaterialHubFlyouts.ShowColorPicker(GetButtonAnchor("HUB_PickColor"), mat,
                        (r, g, b) =>
                        {
                            bool ok = MaterialAppearanceActions.SetSurfaceColor(doc, mat, r, g, b);
                            Toast(ok ? $"Colour set ({r},{g},{b}).":"Colour write failed.", ok ? "ok" : "error");
                            if (ok) ReloadSingleRow(row.Id);
                        });
                    return;
                }
                case "HUB_ResetHatch":
                    if (mat == null) { Toast("Pick a material first.", "warn"); return; }
                    MaterialAppearanceActions.SetHatchPattern(doc, mat, "SurfaceFg",
                        Autodesk.Revit.DB.ElementId.InvalidElementId);
                    Toast("Hatch cleared.", "ok");
                    ReloadSingleRow(row.Id);
                    return;
            }
        }

        private UIElement GetButtonAnchor(string tag)
        {
            UIElement Walk(DependencyObject root)
            {
                if (root is Button b && (b.Tag as string) == tag) return b;
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var hit = Walk(VisualTreeHelper.GetChild(root, i));
                    if (hit != null) return hit;
                }
                return null;
            }
            return Walk(inspectorStack) ?? (UIElement)inspectorStack;
        }
    }
}
