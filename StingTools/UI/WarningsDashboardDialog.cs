using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the WarningsDashboardDialog containing the selected operation and options.
    /// </summary>
    public class WarningsDashboardResult
    {
        /// <summary>True if the user clicked Run; false if cancelled or closed.</summary>
        public bool Confirmed { get; set; }
        /// <summary>Selected operation tag (e.g. "WarningsDashboard", "WarningsAutoFix").</summary>
        public string Operation { get; set; }
        /// <summary>Additional options keyed by name.</summary>
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified interactive Warnings Dashboard dialog that consolidates ALL warning management
    /// operations into a single 5-tab WPF dialog. Provides overview, auto-fix, inspection,
    /// baseline/SLA tracking, and export/integration operations.
    ///
    /// Usage:
    ///   var result = WarningsDashboardDialog.Show();
    ///   if (result.Confirmed) handler.SetCommand(result.Operation);
    /// </summary>
    internal static class WarningsDashboardDialog
    {
        // ── Theme colours (light corporate theme) ───────────────────────
        private static readonly Color BgLight       = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color BgWhite       = Colors.White;
        private static readonly Color BgHeader      = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentOrange  = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark        = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgSubtle      = Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly Color FgWhite       = Colors.White;
        private static readonly Color BorderLight   = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color CardBg        = Colors.White;
        private static readonly Color CardHover     = Color.FromRgb(0xFD, 0xF0, 0xE0);
        private static readonly Color CardSelected  = Color.FromRgb(0xFB, 0xE4, 0xC8);
        private static readonly Color TabSelected   = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color TabDefault    = Color.FromRgb(0xE0, 0xE0, 0xE0);
        private static readonly Color InfoBg        = Color.FromRgb(0xEE, 0xF2, 0xF7);
        private static readonly Color InfoBorder    = Color.FromRgb(0xB0, 0xC4, 0xDE);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static readonly SolidColorBrush BrBgLight      = FZ(BgLight);
        private static readonly SolidColorBrush BrBgWhite      = FZ(BgWhite);
        private static readonly SolidColorBrush BrBgHeader     = FZ(BgHeader);
        private static readonly SolidColorBrush BrAccent       = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFgDark       = FZ(FgDark);
        private static readonly SolidColorBrush BrFgSubtle     = FZ(FgSubtle);
        private static readonly SolidColorBrush BrFgWhite      = FZ(FgWhite);
        private static readonly SolidColorBrush BrBorder       = FZ(BorderLight);
        private static readonly SolidColorBrush BrCardBg       = FZ(CardBg);
        private static readonly SolidColorBrush BrCardHover    = FZ(CardHover);
        private static readonly SolidColorBrush BrCardSelected = FZ(CardSelected);
        private static readonly SolidColorBrush BrInfoBg       = FZ(InfoBg);
        private static readonly SolidColorBrush BrInfoBorder   = FZ(InfoBorder);

        // ── State ───────────────────────────────────────────────────────
        private static string _selectedOperation;
        private static Border _activeCard;
        private static TextBlock _statusText;

        /// <summary>
        /// Show the Warnings Dashboard dialog and return the user's selection.
        /// </summary>
        public static WarningsDashboardResult Show()
        {
            _selectedOperation = null;
            _activeCard = null;

            var result = new WarningsDashboardResult();

            var win = new Window
            {
                Title = "STING Warnings Dashboard",
                Width = 850,
                Height = 620,
                MinWidth = 750,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            // Set Revit as owner window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"WarningsDashboardDialog set owner: {ex.Message}"); }

            // ── Root DockPanel ──────────────────────────────────────────
            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(18, 12, 18, 12)
            };
            DockPanel.SetDock(header, Dock.Top);

            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Warnings Dashboard",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Unified warning management: 150+ classification rules, 16 auto-fix strategies, SLA tracking, deliverable impact analysis",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xB0, 0xB8, 0xD0)),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
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

            _statusText = new TextBlock
            {
                Text = "Select an operation to continue.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

            var closeBtn = MakeButton("Close", false);
            closeBtn.IsCancel = true;
            closeBtn.Click += (s, e) =>
            {
                result.Confirmed = false;
                win.Close();
            };
            closeBtn.Margin = new Thickness(0, 0, 8, 0);
            btnStack.Children.Add(closeBtn);

            var runBtn = MakeButton("Run", true);
            runBtn.IsDefault = true;
            runBtn.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(_selectedOperation))
                {
                    _statusText.Text = "Please select an operation first.";
                    _statusText.Foreground = FZ(Color.FromRgb(0xE0, 0x50, 0x50));
                    return;
                }
                result.Confirmed = true;
                result.Operation = _selectedOperation;
                win.Close();
            };
            btnStack.Children.Add(runBtn);

            Grid.SetColumn(btnStack, 1);
            footerGrid.Children.Add(btnStack);
            footer.Child = footerGrid;
            root.Children.Add(footer);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBgLight,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            tabs.Items.Add(BuildOverviewTab());
            tabs.Items.Add(BuildAutoFixTab());
            tabs.Items.Add(BuildSelectInspectTab());
            tabs.Items.Add(BuildBaselineSLATab());
            tabs.Items.Add(BuildExportIntegrationTab());

            root.Children.Add(tabs);

            win.Content = root;

            // Keyboard shortcut
            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    result.Confirmed = false;
                    win.Close();
                }
            };

            win.ShowDialog();
            return result.Confirmed ? result : new WarningsDashboardResult { Confirmed = false };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: OVERVIEW
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildOverviewTab()
        {
            var tab = MakeTab("OVERVIEW");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("WARNING ANALYSIS"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Warnings Dashboard",
                "Comprehensive dashboard: severity, category, discipline, level, and workset breakdowns with trend vs baseline.",
                "WarningsDashboard"));
            cards1.Children.Add(MakeOperationCard(
                "Model Health Score",
                "Weighted 0-100 health score across warnings, compliance, data quality, and performance categories.",
                "ModelHealthScore"));
            cards1.Children.Add(MakeOperationCard(
                "Warning Root Cause",
                "Dependency graph identifying root-cause elements with weighted impact scoring. Top 20 root causes.",
                "WarningRootCause"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("PREDICTION & COMPLIANCE"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Warning Prediction",
                "Linear regression trend analysis on historical warning counts. Predicts 7-day future warnings.",
                "WarningPrediction"));
            cards2.Children.Add(MakeOperationCard(
                "Warnings Compliance",
                "ISO 19650, CIBSE, and BS 7671 compliance mapping. PASS/FAIL per requirement category.",
                "WarningsCompliance"));
            cards2.Children.Add(MakeOperationCard(
                "Deliverable Readiness",
                "0-100 readiness scoring for COBie, IFC, PDF/Drawings, and FM Handover deliverables.",
                "DeliverableReadiness"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: AUTO-FIX
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildAutoFixTab()
        {
            var tab = MakeTab("AUTO-FIX");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("AUTOMATED RESOLUTION"));
            var cards = new WrapPanel { Orientation = Orientation.Horizontal };
            cards.Children.Add(MakeOperationCard(
                "Auto-Fix Warnings",
                "Batch auto-fix with dry-run preview. Strategies: duplicates, room separation, marks, geometry, and more.",
                "WarningsAutoFix"));
            cards.Children.Add(MakeOperationCard(
                "Action Plan",
                "Prioritised BIM coordinator action list based on current model state. Sorted by impact score.",
                "ActionPlan"));
            content.Children.Add(cards);

            // ── Info block: Fix strategies ───────────────────────────────
            content.Children.Add(MakeSectionLabel("AUTO-FIX STRATEGIES REFERENCE"));

            var infoBlock = new Border
            {
                Background = BrInfoBg,
                BorderBrush = BrInfoBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 8)
            };

            var infoStack = new StackPanel();
            string[] strategies =
            {
                "1. Delete duplicate instances at same location",
                "2. Delete shorter room separation line (overlap)",
                "3. Delete redundant room boundary segments",
                "4. Auto-increment duplicate marks with collision-safe suffix",
                "5. Unjoin non-intersecting geometry pairs",
                "6. Auto-join overlapping walls via JoinGeometryUtils",
                "7. Move room tags outside boundary to room center",
                "8. Snap near-axis elements to nearest cardinal direction",
                "9. Delete zero-length elements (walls/pipes/ducts < 3mm)",
                "10. Fix duplicate marks with full-model scan for uniqueness"
            };
            foreach (string strategy in strategies)
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = strategy,
                    FontSize = 11,
                    Foreground = BrFgDark,
                    Margin = new Thickness(0, 1, 0, 1),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            infoBlock.Child = infoStack;
            content.Children.Add(infoBlock);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: SELECT & INSPECT
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildSelectInspectTab()
        {
            var tab = MakeTab("SELECT & INSPECT");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("ELEMENT SELECTION"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Select by Warning",
                "Pick a warning type from grouped list and select all affected elements in the model view.",
                "WarningsSelectElements"));
            cards1.Children.Add(MakeOperationCard(
                "Toggle Warning Visibility",
                "Show or hide warning-affected elements in the active view for focused inspection.",
                "ToggleWarningVisibility"));
            cards1.Children.Add(MakeOperationCard(
                "Warning Monitor",
                "Pre/post-command warning count tracking. Detect warning regression after major operations.",
                "WarningsMonitor"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("VISUAL INSPECTION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Highlight Invalid",
                "Colour-code elements: red for missing tags, orange for incomplete tags in the active view.",
                "HighlightInvalid"));
            cards2.Children.Add(MakeOperationCard(
                "Clear Overrides",
                "Reset all graphic overrides in the active view to restore default appearance.",
                "ClearOverrides"));
            cards2.Children.Add(MakeOperationCard(
                "Anomaly Auto-Fix",
                "Detect and fix tag anomalies: DISC, SYS, FUNC, PROD, TAG7, and stale element issues.",
                "AnomalyAutoFix"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: BASELINE & SLA
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildBaselineSLATab()
        {
            var tab = MakeTab("BASELINE & SLA");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("BASELINE MANAGEMENT"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Save Baseline",
                "Save current warning count as baseline sidecar. Compare against previous baseline with delta report.",
                "WarningsBaseline"));
            cards1.Children.Add(MakeOperationCard(
                "Extended Baseline",
                "Save per-warning-type baseline with first-seen timestamps for type-level regression analysis.",
                "SaveExtendedBaseline"));
            cards1.Children.Add(MakeOperationCard(
                "Compliance Fall Check",
                "Detect > 2% compliance regression between checks. Track stale element count delta.",
                "ComplianceFallCheck"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("SLA & SUPPRESSION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "SLA Violation Report",
                "Per-warning SLA tracking: Critical 4h, High 24h, Medium 1wk, Low 2wk. ISO 19650 aligned.",
                "SLAViolationReport"));
            cards2.Children.Add(MakeOperationCard(
                "Suppress Warnings",
                "Add warning patterns to suppression list. Time-limited and context-aware suppressions.",
                "WarningsSuppress"));
            cards2.Children.Add(MakeOperationCard(
                "Suppression Audit",
                "Review all active suppressions with expiry dates, context, and audit trail.",
                "SuppressionAudit"));
            content.Children.Add(cards2);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 5: EXPORT & INTEGRATION
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildExportIntegrationTab()
        {
            var tab = MakeTab("EXPORT & INTEGRATION");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("EXPORT"));
            var cards1 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards1.Children.Add(MakeOperationCard(
                "Export Warnings CSV",
                "10-column CSV: Description, Category, Severity, FixStrategy, CanAutoFix, ElementIds, Level, and more.",
                "WarningsExport"));
            cards1.Children.Add(MakeOperationCard(
                "Export HTML Dashboard",
                "Self-contained HTML report with KPI cards, discipline table, warning summary. Shareable without Revit.",
                "ExportDashboardHTML"));
            cards1.Children.Add(MakeOperationCard(
                "Weekly Coordinator Report",
                "Corporate HTML report: compliance trend, per-discipline table, warning root-cause summary, issue metrics.",
                "WeeklyCoordinatorReport"));
            content.Children.Add(cards1);

            content.Children.Add(MakeSectionLabel("CROSS-SYSTEM INTEGRATION"));
            var cards2 = new WrapPanel { Orientation = Orientation.Horizontal };
            cards2.Children.Add(MakeOperationCard(
                "Container-Warning Check",
                "Correlate container completeness with data-quality warnings. Recommend actions when gaps detected.",
                "ContainerWarningCheck"));
            cards2.Children.Add(MakeOperationCard(
                "Transmittal Gate Check",
                "Validate tag compliance, containers, stale elements, and critical warnings before transmittal send.",
                "TransmittalGateCheck"));
            content.Children.Add(cards2);

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
        private static TabItem MakeTab(string headerText)
        {
            var tb = new TextBlock
            {
                Text = headerText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4)
            };

            return new TabItem
            {
                Header = tb,
                Background = FZ(TabDefault),
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
        /// Clicking the card or the Run button selects the operation.
        /// </summary>
        private static Border MakeOperationCard(string title, string description, string operationKey)
        {
            // Outer border provides the 4px orange left accent
            var accentBorder = new Border
            {
                Width = 240,
                MinHeight = 110,
                Background = BrAccent,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(4, 0, 0, 0),
                SnapsToDevicePixels = true
            };

            // Inner card with white background
            var card = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 1, 1),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand
            };

            var outerStack = new DockPanel();

            // Run button docked to bottom
            var runBtn = new Button
            {
                Content = "Run",
                Width = 56,
                Height = 24,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = BrAccent,
                Foreground = BrFgWhite,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(runBtn, Dock.Bottom);
            outerStack.Children.Add(runBtn);

            // Title and description
            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            outerStack.Children.Add(textStack);

            card.Child = outerStack;
            accentBorder.Child = card;

            // ── Select handler (shared between card click and Run button) ──
            void SelectCard()
            {
                // Deselect previous
                if (_activeCard != null)
                {
                    _activeCard.Background = BrCardBg;
                }

                // Select this card
                _activeCard = card;
                card.Background = BrCardSelected;
                _selectedOperation = operationKey;

                if (_statusText != null)
                {
                    _statusText.Foreground = BrFgSubtle;
                    _statusText.Text = $"Selected: {title}";
                }
            }

            // Hover effects
            card.MouseEnter += (s, e) =>
            {
                if (_activeCard != card)
                    card.Background = BrCardHover;
            };
            card.MouseLeave += (s, e) =>
            {
                if (_activeCard != card)
                    card.Background = BrCardBg;
            };

            // Click card body to select
            card.MouseLeftButtonDown += (s, e) => SelectCard();

            // Run button also selects the operation
            runBtn.Click += (s, e) => SelectCard();

            return accentBorder;
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
                btn.Foreground = BrFgWhite;
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
