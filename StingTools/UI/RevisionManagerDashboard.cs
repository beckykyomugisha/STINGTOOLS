using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;
using Autodesk.Revit.DB;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the RevisionManagerDashboard containing the selected operation and options.
    /// </summary>
    public class RevisionManagerResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified interactive Revision Manager Dashboard consolidating ALL revision management
    /// operations into a single 4-tab WPF dialog. Accessible standalone and from the
    /// Document Management dialog. Returns the selected operation dispatch tag so the
    /// caller can route to the appropriate command.
    /// </summary>
    internal static class RevisionManagerDashboard
    {
        // ── Theme-routed palette ─────────────────────────────────────────
        // All colours come from ThemeManager so the dashboard follows the
        // active theme (Corporate by default — navy header, orange accent).
        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static SolidColorBrush BrBgLight   => ThemeManager.GetBrush("AltRowBg");
        private static SolidColorBrush BrBgWhite   => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush BrBgHeader  => ThemeManager.GetBrush("HeaderBg");
        private static SolidColorBrush BrAccent    => ThemeManager.GetBrush("AccentBrush");
        private static SolidColorBrush BrFgDark    => ThemeManager.GetBrush("PanelFg");
        private static SolidColorBrush BrFgSubtle  => ThemeManager.GetBrush("SubtleFg");
        private static SolidColorBrush BrBorder    => ThemeManager.GetBrush("BorderColor");
        private static SolidColorBrush BrCardBg    => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush BrCardHover => ThemeManager.GetBrush("RowHover");
        private static SolidColorBrush BrWhite     => ThemeManager.GetBrush("HeaderFg");
        private static SolidColorBrush BrHeaderFg  => ThemeManager.GetBrush("HeaderFg");

        /// <summary>
        /// Show the Revision Manager Dashboard and return the user's selection.
        /// </summary>
        /// <param name="doc">The active Revit Document (used for context display). May be null.</param>
        /// <returns>RevisionManagerResult with Confirmed=true and the selected operation, or Confirmed=false if cancelled.</returns>
        public static RevisionManagerResult Show(Autodesk.Revit.DB.Document doc)
        {
            string projectName = "Unknown";
            try { if (doc != null) projectName = doc.Title ?? "Untitled"; }
            catch (Exception ex) { StingLog.Warn($"RevisionManagerDashboard get title: {ex.Message}"); }

            var result = new RevisionManagerResult();

            var win = new Window
            {
                Title = "STING Revision Manager",
                Width = 820,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Tag = result
            };

            // Set Revit as owner window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"RevisionManagerDashboard set owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(header, Dock.Top);
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Revision Manager",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"Project: {projectName}  |  ISO 19650 Revision Management",
                FontSize = 11,
                Foreground = BrHeaderFg,
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            root.Children.Add(header);

            // ── Footer ──────────────────────────────────────────────────
            var footer = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusText = new TextBlock
            {
                Text = "Click any operation card or its Run button to execute.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusText, 0);
            footerGrid.Children.Add(statusText);

            var closeBtn = MakeButton("Close", false);
            closeBtn.Click += (s, e) =>
            {
                result.Confirmed = false;
                win.Close();
            };
            closeBtn.IsCancel = true;

            Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);
            footer.Child = footerGrid;
            root.Children.Add(footer);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBgLight,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            tabs.Items.Add(BuildCreateManageTab());
            tabs.Items.Add(BuildTrackingComparisonTab());
            tabs.Items.Add(BuildSheetsExportTab());
            tabs.Items.Add(BuildDashboardAnalyticsTab());

            root.Children.Add(tabs);

            win.Content = root;

            // Keyboard shortcuts
            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    result.Confirmed = false;
                    win.Close();
                }
            };

            win.ShowDialog();
            return result.Confirmed ? result : new RevisionManagerResult { Confirmed = false };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: CREATE & MANAGE
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildCreateManageTab()
        {
            var tab = MakeTab("CREATE & MANAGE");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("CREATE"));

            content.Children.Add(MakeOperationCard(
                "Create Revision",
                "Create new ISO 19650 revision with compliance-gated pre-check",
                "CreateRevision"));
            content.Children.Add(MakeOperationCard(
                "Auto Revision Cloud",
                "Auto-place revision clouds on changed elements with dedup",
                "AutoRevisionCloud"));
            content.Children.Add(MakeOperationCard(
                "Bulk Revision Stamp",
                "Stamp selected elements with current revision",
                "BulkRevisionStamp"));
            content.Children.Add(MakeOperationCard(
                "Auto Revision on Tag Change",
                "Automatically create revision when tags change",
                "AutoRevisionOnTagChange"));

            content.Children.Add(MakeSectionLabel("NAMING & COMPLIANCE"));

            content.Children.Add(MakeOperationCard(
                "Revision Naming Enforce",
                "Enforce ISO 19650 revision naming conventions",
                "RevisionNamingEnforce"));
            content.Children.Add(MakeOperationCard(
                "Revision Tag Integration",
                "Auto-stamp REV parameter on tag changes",
                "RevisionTagIntegration"));
            content.Children.Add(MakeOperationCard(
                "Revision Approval Workflow",
                "ISO 19650-2 \u00A75.6 approval workflow",
                "RevisionApprovalWorkflow"));

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: TRACKING & COMPARISON
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildTrackingComparisonTab()
        {
            var tab = MakeTab("TRACKING & COMPARISON");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("TRACKING"));

            content.Children.Add(MakeOperationCard(
                "Track Element Revisions",
                "Take tag snapshot for change detection",
                "TrackElementRevisions"));
            content.Children.Add(MakeOperationCard(
                "Revision Compare",
                "Compare two snapshots to see tag deltas (ADDED/CHANGED/REMOVED)",
                "RevisionCompare"));
            content.Children.Add(MakeOperationCard(
                "Tag Revision Diff",
                "Token-level diff between revision snapshots as CSV",
                "TagRevisionDiff"));
            content.Children.Add(MakeOperationCard(
                "Revision Comparison Report",
                "Detailed comparison report",
                "RevisionComparisonReport"));

            content.Children.Add(MakeSectionLabel("LINKING"));

            content.Children.Add(MakeOperationCard(
                "Issue-Revision Link",
                "Link closed issues to revision snapshots for ISO 19650 audit",
                "IssueRevisionLink"));
            content.Children.Add(MakeOperationCard(
                "Revision Distribution",
                "Track revision distribution across project",
                "RevisionDistribution"));

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: SHEETS & EXPORT
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildSheetsExportTab()
        {
            var tab = MakeTab("SHEETS & EXPORT");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("SHEETS"));

            content.Children.Add(MakeOperationCard(
                "Issue Sheets for Revision",
                "Issue sheets for the latest revision",
                "IssueSheetsForRevision"));
            content.Children.Add(MakeOperationCard(
                "Revision Schedule",
                "Generate revision schedule view",
                "RevisionSchedule"));

            content.Children.Add(MakeSectionLabel("EXPORT"));

            content.Children.Add(MakeOperationCard(
                "Revision Export",
                "Export revision data to CSV/JSON",
                "RevisionExport"));
            content.Children.Add(MakeOperationCard(
                "Export Tag Map",
                "Export tagged elements to .sting_tagmap.json for cross-project transfer",
                "ExportTagMap"));
            content.Children.Add(MakeOperationCard(
                "Import Tag Map",
                "Import tags from another project via tagmap JSON",
                "ImportTagMap"));

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: DASHBOARD & ANALYTICS
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildDashboardAnalyticsTab()
        {
            var tab = MakeTab("DASHBOARD & ANALYTICS");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("OVERVIEW"));

            content.Children.Add(MakeOperationCard(
                "Revision Dashboard",
                "Comprehensive revision status overview",
                "RevisionDashboard"));
            content.Children.Add(MakeOperationCard(
                "Weekly Coordinator Report",
                "Generate HTML report with compliance trend and KPIs",
                "WeeklyCoordinatorReport"));

            content.Children.Add(MakeSectionLabel("AUTOMATION"));

            content.Children.Add(MakeOperationCard(
                "Save Extended Baseline",
                "Save per-warning-type baseline with first-seen timestamps",
                "SaveExtendedBaseline"));
            content.Children.Add(MakeOperationCard(
                "Take Model Snapshot",
                "Capture model compliance state for meeting record",
                "TakeModelSnapshot"));

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // SHARED UI BUILDERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a styled TabItem with header text.
        /// </summary>
        private static TabItem MakeTab(string header)
        {
            var tb = new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 4, 8, 4)
            };

            return new TabItem
            {
                Header = tb,
                Background = FZ(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                Foreground = BrFgDark
            };
        }

        /// <summary>
        /// Creates a section label with orange accent text.
        /// </summary>
        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 8, 0, 6)
            };
        }

        /// <summary>
        /// Creates an operation card with a 4px orange left border, title, description, and Run button.
        /// Layout: [4px accent border | Title + Description | Run button]
        /// </summary>
        private static Border MakeOperationCard(string title, string description, string operationKey)
        {
            var card = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Orange left accent bar
            var accentBar = new Border
            {
                Background = BrAccent,
                CornerRadius = new CornerRadius(3, 0, 0, 3)
            };
            Grid.SetColumn(accentBar, 0);
            outerGrid.Children.Add(accentBar);

            // Title + description
            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 8, 8, 8)
            };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            outerGrid.Children.Add(textStack);

            // Run button
            var runBtn = new Button
            {
                Content = "\u25B6 Run",
                MinWidth = 60,
                Height = 26,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 2, 10, 2),
                Cursor = Cursors.Hand,
                Background = BrAccent,
                Foreground = BrWhite,
                BorderBrush = BrAccent,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            runBtn.Click += (s, e) =>
            {
                try
                {
                    var parentWindow = Window.GetWindow(card);
                    if (parentWindow?.Tag is RevisionManagerResult res)
                    {
                        res.Confirmed = true;
                        res.Operation = operationKey;
                        parentWindow.Close();
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RevisionManagerDashboard run click: {ex.Message}");
                }
                e.Handled = true;
            };
            Grid.SetColumn(runBtn, 2);
            outerGrid.Children.Add(runBtn);

            card.Child = outerGrid;

            // Hover effects on the card
            card.MouseEnter += (s, e) => { card.Background = BrCardHover; };
            card.MouseLeave += (s, e) => { card.Background = BrCardBg; };

            // Click anywhere on the card to dispatch immediately
            card.MouseLeftButtonDown += (s, e) =>
            {
                // Avoid double-fire if the click came from the Run button
                if (e.OriginalSource is Button) return;
                try
                {
                    var parentWindow = Window.GetWindow(card);
                    if (parentWindow?.Tag is RevisionManagerResult res)
                    {
                        res.Confirmed = true;
                        res.Operation = operationKey;
                        parentWindow.Close();
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RevisionManagerDashboard card click: {ex.Message}");
                }
            };

            return card;
        }

        /// <summary>
        /// Creates a styled button matching the STING dialog theme.
        /// </summary>
        private static Button MakeButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 80,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(14, 4, 14, 4),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };

            if (isPrimary)
            {
                btn.Background = BrAccent;
                btn.Foreground = BrWhite;
                btn.BorderBrush = BrAccent;
                btn.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                btn.Background = BrBgWhite;
                btn.Foreground = BrFgDark;
                btn.BorderBrush = BrBorder;
            }

            return btn;
        }
    }
}
