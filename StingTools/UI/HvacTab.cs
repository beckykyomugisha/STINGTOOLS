// ══════════════════════════════════════════════════════════════════════
//  HvacTab — BCC desktop surface for HVAC project status (Phase 183).
//
//  16th tab in the BIM Coordination Center, sibling to TabHealthcare and
//  TabSitePhotos. Read-only mirror of the live STING HVAC panel: HVAC
//  health KPIs, drift list, recent workflow runs. Click-throughs open
//  the HVAC dock panel for editable work.
//
//  Wrapper pattern (per SitePhotosTab) so the existing 10,690-line
//  BIMCoordinationCenter.cs doesn't grow another tab body.
//
//  No Revit API calls compile-tested — Linux sandbox.
// ══════════════════════════════════════════════════════════════════════

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Mep;

namespace StingTools.UI
{
    internal static class HvacTab
    {
        public static UIElement BuildTab(BIMCoordinationCenter owner)
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20)
            };
            var stack = new StackPanel();

            // ── Header chips ─────────────────────────────────────────────
            var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

            string regionLabel = ReadCurrentRegionLabel();
            header.Children.Add(MakeChip($"Region: {regionLabel}", BrushFromHex("#FF1565C0")));

            string pclassLabel = ReadCurrentPressureClassLabel();
            header.Children.Add(MakeChip($"Pressure class: {pclassLabel}", BrushFromHex("#FF00897B")));

            string strategy = SafeStatic(() => StingHvacCommandHandler.CurrentSizingStrategyId, "velocity");
            header.Children.Add(MakeChip($"Strategy: {strategy}", BrushFromHex("#FF5E35B1")));

            string scope = SafeStatic(() => StingHvacCommandHandler.CurrentScope, "Project");
            header.Children.Add(MakeChip($"Scope: {scope}", BrushFromHex("#FF6D4C41")));

            stack.Children.Add(header);

            // ── KPI strip ────────────────────────────────────────────────
            var kpis = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            int ductCount = 0, pipeCount = 0, equipCount = 0;
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    ductCount = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType().GetElementCount();
                    pipeCount = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType().GetElementCount();
                    equipCount = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                        .WhereElementIsNotElementType().GetElementCount();
                }
            }
            catch (Exception ex) { StingLog.Warn($"HvacTab KPI collect: {ex.Message}"); }

            kpis.Children.Add(MakeKpi("Ducts", ductCount.ToString()));
            kpis.Children.Add(MakeKpi("Pipes", pipeCount.ToString()));
            kpis.Children.Add(MakeKpi("Equipment", equipCount.ToString()));

            int stale = CountStaleDucts();
            kpis.Children.Add(MakeKpi("Stale-sized", stale.ToString(),
                stale > 0 ? BrushFromHex("#FFEF6C00") : BrushFromHex("#FF2E7D32")));

            stack.Children.Add(kpis);

            // ── Drift / workflow mirror ──────────────────────────────────
            var panel = StingHvacPanel.Instance;
            if (panel != null)
            {
                stack.Children.Add(MakeSectionTitle("Recent HVAC runs"));
                if (panel.WorkflowRows.Count == 0)
                {
                    stack.Children.Add(MakeMutedNote(
                        "No runs yet this session. Open the HVAC dock panel and run an action " +
                        "(❄ HVAC ribbon → STING HVAC)."));
                }
                else
                {
                    foreach (var row in panel.WorkflowRows.Take(10))
                    {
                        var line = new TextBlock
                        {
                            Text = $"{row.StatusDot}  {row.Number}  {row.Name}  ·  {row.Timestamp}",
                            FontSize = 12,
                            Margin = new Thickness(0, 2, 0, 2)
                        };
                        stack.Children.Add(line);
                    }
                }

                if (panel.IssueRows.Count > 0)
                {
                    stack.Children.Add(MakeSectionTitle("Active HVAC issues"));
                    foreach (var iss in panel.IssueRows.Take(10))
                    {
                        var line = new TextBlock
                        {
                            Text = $"{iss.Severity}  [{iss.Element}]  {iss.Issue}  →  {iss.Suggestion}",
                            FontSize = 11,
                            Foreground = BrushFromHex("#FFD84315"),
                            Margin = new Thickness(0, 2, 0, 2),
                            TextWrapping = TextWrapping.Wrap
                        };
                        stack.Children.Add(line);
                    }
                }
            }
            else
            {
                stack.Children.Add(MakeMutedNote(
                    "STING HVAC panel is not open. Toggle it via Ribbon → ❄ HVAC → STING HVAC " +
                    "to populate this tab with live KPIs and issues."));
            }

            // ── Quick actions ────────────────────────────────────────────
            stack.Children.Add(MakeSectionTitle("Quick actions"));
            var actions = new WrapPanel();
            actions.Children.Add(MakeButton("Open HVAC panel",       "ToggleHvacPanel"));
            actions.Children.Add(MakeButton("Auto-size ducts",       "Hvac_AutoSizeDuct"));
            actions.Children.Add(MakeButton("Auto-size pipes",       "Mep_AutoSizePipe"));
            actions.Children.Add(MakeButton("Detect stale sizes",    "Hvac_DetectStaleSizes"));
            actions.Children.Add(MakeButton("Pressure-class audit",  "Hvac_PressureClassAudit"));
            actions.Children.Add(MakeButton("Plant carbon report",   "Hvac_CarbonReport"));
            actions.Children.Add(MakeButton("Run HVAC design preset","WorkflowPreset:HVACDesign"));
            stack.Children.Add(actions);

            scroll.Content = stack;
            return scroll;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string ReadCurrentRegionLabel()
        {
            try
            {
                string id = StingHvacCommandHandler.CurrentRegion ?? "UK_SI";
                var rules = MepSizingRegistry.Get(null);
                if (rules.Regions.TryGetValue(id, out var r) && !string.IsNullOrEmpty(r.Label))
                    return r.Label;
                return id;
            }
            catch { return "UK_SI"; }
        }

        private static string ReadCurrentPressureClassLabel()
        {
            try
            {
                string id = StingHvacCommandHandler.CurrentPressureClassId ?? "low";
                var rules = MepSizingRegistry.Get(null);
                var pc = rules.DuctPressureClasses
                    .FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                return pc?.Label ?? id;
            }
            catch { return "low"; }
        }

        private static T SafeStatic<T>(Func<T> read, T fallback)
        {
            try { return read(); } catch { return fallback; }
        }

        private static int CountStaleDucts()
        {
            try
            {
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null) return 0;
                int n = 0;
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType();
                foreach (Element d in ducts)
                {
                    var p = d.LookupParameter(ParamRegistry.HVC_SIZE_STALE_BOOL);
                    if (p != null && p.HasValue
                        && p.StorageType == StorageType.Integer && p.AsInteger() == 1)
                        n++;
                }
                return n;
            }
            catch { return 0; }
        }

        private static Border MakeChip(string text, Brush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 4),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        private static Border MakeKpi(string label, string value, Brush valueBrush = null)
        {
            valueBrush = valueBrush ?? BrushFromHex("#FF263238");
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = valueBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = BrushFromHex("#FF607D8B"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return new Border
            {
                Width = 110,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 8),
                BorderBrush = BrushFromHex("#FFCFD8DC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = stack
            };
        }

        private static TextBlock MakeSectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#FF37474F"),
                Margin = new Thickness(0, 16, 0, 6)
            };
        }

        private static TextBlock MakeMutedNote(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = BrushFromHex("#FF78909C"),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            };
        }

        private static Button MakeButton(string label, string tag)
        {
            var btn = new Button
            {
                Content = label,
                Tag = tag,
                MinWidth = 140,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11
            };
            btn.Click += (_, __) =>
            {
                try
                {
                    if (tag.StartsWith("WorkflowPreset:"))
                    {
                        // The main dock panel knows how to dispatch a preset run.
                        StingDockPanel.DispatchCommand("RunWorkflowPreset", tag.Substring("WorkflowPreset:".Length));
                        return;
                    }
                    StingDockPanel.DispatchCommand(tag);
                }
                catch (Exception ex) { StingLog.Warn($"HvacTab action {tag}: {ex.Message}"); }
            };
            return btn;
        }

        private static Brush BrushFromHex(string hex)
        {
            try { return (Brush)new BrushConverter().ConvertFromString(hex); }
            catch { return Brushes.Gray; }
        }
    }
}
