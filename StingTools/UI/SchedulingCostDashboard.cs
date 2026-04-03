using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the SchedulingCostDashboard containing the selected operation and options.
    /// </summary>
    public class SchedulingCostResult
    {
        /// <summary>True if the user clicked Run on an operation; false if cancelled.</summary>
        public bool Confirmed { get; set; }
        /// <summary>Dispatch tag for the selected operation (e.g. "AutoSchedule4D", "CashFlow5D").</summary>
        public string Operation { get; set; }
        /// <summary>Additional option values keyed by name.</summary>
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified 4D/5D Scheduling and Cost Dashboard. Consolidates ALL scheduling, costing,
    /// cash flow, and phase management operations into one comprehensive 6-tab WPF dialog.
    /// Covers 4D construction scheduling, 5D cost estimation, cash flow S-curve analysis,
    /// phase/milestone management, configuration, and deliverable readiness assessment.
    ///
    /// Usage:
    ///   var result = SchedulingCostDashboard.Show();
    ///   if (!result.Confirmed) return Result.Cancelled;
    ///   // dispatch result.Operation
    /// </summary>
    internal static class SchedulingCostDashboard
    {
        // ── Theme colours (light corporate theme) ───────────────────────
        private static readonly Color BgLight      = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color BgWhite      = Colors.White;
        private static readonly Color HeaderBg     = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark       = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgSubtle     = Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly Color BorderLight  = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color CardBg       = Colors.White;
        private static readonly Color CardHover    = Color.FromRgb(0xFD, 0xF0, 0xE0);
        private static readonly Color SectionBg    = Color.FromRgb(0xF0, 0xF0, 0xF0);
        private static readonly Color InfoBg       = Color.FromRgb(0xE8, 0xF0, 0xFE);
        private static readonly Color InfoBorder   = Color.FromRgb(0xB0, 0xC8, 0xE8);
        private static readonly Color TabDefault   = Color.FromRgb(0xE0, 0xE0, 0xE0);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static readonly SolidColorBrush BrBgLight     = FZ(BgLight);
        private static readonly SolidColorBrush BrBgWhite     = FZ(BgWhite);
        private static readonly SolidColorBrush BrHeaderBg    = FZ(HeaderBg);
        private static readonly SolidColorBrush BrAccent      = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFgDark      = FZ(FgDark);
        private static readonly SolidColorBrush BrFgSubtle    = FZ(FgSubtle);
        private static readonly SolidColorBrush BrBorder      = FZ(BorderLight);
        private static readonly SolidColorBrush BrCardBg      = FZ(CardBg);
        private static readonly SolidColorBrush BrCardHover   = FZ(CardHover);
        private static readonly SolidColorBrush BrSectionBg   = FZ(SectionBg);
        private static readonly SolidColorBrush BrInfoBg      = FZ(InfoBg);
        private static readonly SolidColorBrush BrInfoBorder  = FZ(InfoBorder);
        private static readonly SolidColorBrush BrWhiteFg     = FZ(Colors.White);
        private static readonly SolidColorBrush BrTabDefault  = FZ(TabDefault);

        // ── State ───────────────────────────────────────────────────────
        private static Window _win;
        private static SchedulingCostResult _result;

        /// <summary>
        /// Show the 4D/5D Scheduling and Cost Dashboard and return the user's selection.
        /// </summary>
        public static SchedulingCostResult Show()
        {
            _result = new SchedulingCostResult();

            _win = new Window
            {
                Title = "STING 4D/5D Scheduling & Cost Dashboard",
                Width = 780,
                Height = 600,
                MinWidth = 720,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            // Set Revit as owner window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_win);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"SchedulingCostDashboard set owner: {ex.Message}"); }

            // ── Root layout ─────────────────────────────────────────────
            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──────────────────────────────────────────────────
            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBgLight,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            tabs.Items.Add(Build4DSchedulingTab());
            tabs.Items.Add(Build5DCostTab());
            tabs.Items.Add(BuildCashFlowTab());
            tabs.Items.Add(BuildPhasesMilestonesTab());
            tabs.Items.Add(BuildConfigurationTab());
            tabs.Items.Add(BuildDeliverablesTab());

            root.Children.Add(tabs);

            _win.Content = root;

            // Keyboard shortcuts
            _win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _result.Confirmed = false;
                    _win.Close();
                }
            };

            _win.ShowDialog();
            return _result.Confirmed ? _result : new SchedulingCostResult { Confirmed = false };
        }

        // ═══════════════════════════════════════════════════════════════
        // HEADER
        // ═══════════════════════════════════════════════════════════════

        private static Border BuildHeader()
        {
            var header = new Border
            {
                Background = BrHeaderBg,
                Padding = new Thickness(16, 10, 16, 10)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "4D/5D Scheduling & Cost Dashboard",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Construction scheduling, cost estimation, cash flow analysis & phase management",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            header.Child = stack;
            return header;
        }

        // ═══════════════════════════════════════════════════════════════
        // FOOTER
        // ═══════════════════════════════════════════════════════════════

        private static Border BuildFooter()
        {
            var footer = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "Click Run on any operation to execute it.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 0);
            grid.Children.Add(hint);

            var closeBtn = MakeButton("Close", false);
            closeBtn.IsCancel = true;
            closeBtn.Click += (s, e) =>
            {
                _result.Confirmed = false;
                _win.Close();
            };
            Grid.SetColumn(closeBtn, 1);
            grid.Children.Add(closeBtn);

            footer.Child = grid;
            return footer;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: 4D SCHEDULING
        // ═══════════════════════════════════════════════════════════════

        private static TabItem Build4DSchedulingTab()
        {
            var tab = MakeTab("4D SCHEDULING");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionHeader("GENERATE & IMPORT"));

            content.Children.Add(MakeOperationCard(
                "Auto-Generate 4D Schedule", "AutoSchedule4D",
                "Generate construction schedule from model elements, phases, and 40 trade sequences"));
            content.Children.Add(MakeOperationCard(
                "Import MS Project", "ImportMSProject",
                "Import MS Project XML with predecessor links (FS/FF/SF/SS)"));
            content.Children.Add(MakeOperationCard(
                "Assign Phase Dates", "AssignPhaseDates",
                "Assign construction dates to Revit phases"));
            content.Children.Add(MakeOperationCard(
                "Link Predecessors", "LinkPredecessors",
                "Auto-link tasks to element categories and levels"));

            content.Children.Add(MakeSectionHeader("VIEW & EXPORT"));

            content.Children.Add(MakeOperationCard(
                "View Timeline (Gantt)", "ViewTimeline4D",
                "Display ASCII Gantt chart timeline"));
            content.Children.Add(MakeOperationCard(
                "Export to MS Project", "ExportSchedule4D",
                "Export schedule as MS Project XML or CSV"));
            content.Children.Add(MakeOperationCard(
                "Export 4D Timeline", "Export4DTimeline",
                "Extended timeline export for external tools"));
            content.Children.Add(MakeOperationCard(
                "Navisworks TimeLiner", "NavisworksTimeLiner",
                "Export for Navisworks 4D simulation"));

            tab.Content = WrapInScrollViewer(content);
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: 5D COST ESTIMATION
        // ═══════════════════════════════════════════════════════════════

        private static TabItem Build5DCostTab()
        {
            var tab = MakeTab("5D COST ESTIMATION");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionHeader("GENERATE"));

            content.Children.Add(MakeOperationCard(
                "Auto-Generate Cost Estimate", "AutoCost5D",
                "Calculate costs from element quantities and rates (265+ categories)"));
            content.Children.Add(MakeOperationCard(
                "Import Cost Rates", "ImportCostRates",
                "Load custom rates from CSV (USD/UGX columns supported)"));
            content.Children.Add(MakeOperationCard(
                "Element Cost Trace", "ElementCostTrace",
                "Write per-element costs to model parameters for traceability"));

            content.Children.Add(MakeSectionHeader("REPORTS"));

            content.Children.Add(MakeOperationCard(
                "Cost Report (CSV)", "CostReport5D",
                "Export cost estimate with discipline breakdown, markups, and grand total"));
            content.Children.Add(MakeOperationCard(
                "Export 5D Cost Data", "Export5DCostData",
                "Extended cost data export"));
            content.Children.Add(MakeOperationCard(
                "Measured Quantities", "MeasuredQuantities",
                "Extract measured quantities (area, linear, volume, weight, count)"));
            content.Children.Add(MakeOperationCard(
                "Element Count Summary", "ElementCountSummary",
                "Element count breakdown by category and discipline"));
            content.Children.Add(MakeOperationCard(
                "BOQ Export", "BOQExport",
                "Bill of Quantities export to Excel (ClosedXML)"));

            tab.Content = WrapInScrollViewer(content);
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: CASH FLOW & S-CURVE
        // ═══════════════════════════════════════════════════════════════

        private static TabItem BuildCashFlowTab()
        {
            var tab = MakeTab("CASH FLOW & S-CURVE");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionHeader("ANALYSIS"));

            content.Children.Add(MakeOperationCard(
                "Generate Cash Flow (S-Curve)", "CashFlow5D",
                "Monthly S-curve projection combining 4D schedule with 5D costs"));
            content.Children.Add(MakeOperationCard(
                "Embodied Carbon", "EmbodiedCarbon",
                "Calculate kgCO2e using ICE Database v3.0 factors (18 material categories)"));
            content.Children.Add(MakeOperationCard(
                "Lifecycle Assessment", "LifecycleAssessment",
                "BS EN 15978 whole-life carbon A1-C4+D with LETI/RIBA benchmarking"));

            tab.Content = WrapInScrollViewer(content);
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: PHASES & MILESTONES
        // ═══════════════════════════════════════════════════════════════

        private static TabItem BuildPhasesMilestonesTab()
        {
            var tab = MakeTab("PHASES & MILESTONES");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionHeader("PHASE MANAGEMENT"));

            content.Children.Add(MakeOperationCard(
                "Phase Filter", "PhaseFilter",
                "Select elements by Revit phase (exclude demolished/temporary)"));
            content.Children.Add(MakeOperationCard(
                "Phase Summary", "PhaseSummary",
                "Generate discipline breakdown per phase"));
            content.Children.Add(MakeOperationCard(
                "Milestone Register", "MilestoneRegister",
                "Export phase milestones as register"));
            content.Children.Add(MakeOperationCard(
                "Working Calendar", "WorkingCalendar",
                "Generate calendar with UK bank holidays and working days"));
            content.Children.Add(MakeOperationCard(
                "Data Drop Readiness", "DataDropReadiness",
                "Assess DD1-DD4 milestone readiness per PAS 1192-2"));

            tab.Content = WrapInScrollViewer(content);
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 5: CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        private static TabItem BuildConfigurationTab()
        {
            var tab = MakeTab("CONFIGURATION");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            // ── Cost settings ───────────────────────────────────────────
            content.Children.Add(MakeSectionHeader("COST SETTINGS"));

            content.Children.Add(MakeInfoBlock(
                "Preliminaries: 12%  |  Contingency: 10%  |  Overhead & Profit: 8%\n" +
                "Configurable via COST_PRELIMINARIES_PCT, COST_CONTINGENCY_PCT, COST_OVERHEAD_PROFIT_PCT in project_config.json"));

            content.Children.Add(MakeOperationCard(
                "Set Output Directory", "SetOutputDirectory",
                "Configure export path with 4-level fallback chain"));

            content.Children.Add(MakeOperationCard(
                "Configure Cost File", "ConfigureCostFile",
                "Select custom cost_rates CSV file",
                "ShowCostConfig", "true"));

            // ── Schedule settings ───────────────────────────────────────
            content.Children.Add(MakeSectionHeader("SCHEDULE SETTINGS"));

            content.Children.Add(MakeInfoBlock(
                "40 construction trade sequences  |  Trade-aware overlap: Structure sequential, MEP/finishes parallel\n" +
                "Sequences sourced from Scheduling4DEngine.TradeSequence with UK construction phasing"));

            content.Children.Add(MakeOperationCard(
                "4D Handover Schedule", "Schedule4DHandover",
                "Handover milestone tracking"));

            tab.Content = WrapInScrollViewer(content);
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 6: DELIVERABLES
        // ═══════════════════════════════════════════════════════════════

        private static TabItem BuildDeliverablesTab()
        {
            var tab = MakeTab("DELIVERABLES");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionHeader("ASSESSMENT & READINESS"));

            content.Children.Add(MakeOperationCard(
                "BREEAM Assessment", "BREEAMAssessment",
                "BREEAM v6.0 credit scoring across 10 weighted categories"));
            content.Children.Add(MakeOperationCard(
                "Floor Efficiency", "FloorEfficiency",
                "Gross-to-net floor area ratio per level with BCO Guide rating"));
            content.Children.Add(MakeOperationCard(
                "Room Area Audit", "RoomAreaAudit",
                "Validate rooms against BCO/BS 6465/BS 5395 minimums"));
            content.Children.Add(MakeOperationCard(
                "Deliverable Readiness", "DeliverableReadiness",
                "0-100 readiness score for COBie, IFC, PDF, FM/Handover"));
            content.Children.Add(MakeOperationCard(
                "Model Complexity", "ModelComplexity",
                "Element count, linked models, worksets, MEP systems scoring"));

            tab.Content = WrapInScrollViewer(content);
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
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 4, 8, 4)
            };

            return new TabItem
            {
                Header = tb,
                Background = BrTabDefault,
                Foreground = BrFgDark
            };
        }

        /// <summary>
        /// Creates a section header with grey background and bold text.
        /// </summary>
        private static Border MakeSectionHeader(string text)
        {
            var border = new Border
            {
                Background = BrSectionBg,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 8, 0, 8),
                CornerRadius = new CornerRadius(3)
            };

            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = BrFgDark
            };

            return border;
        }

        /// <summary>
        /// Creates an operation card with title, description, 4px orange left border, and inline Run button.
        /// Clicking Run sets the result and closes the dialog.
        /// </summary>
        private static Border MakeOperationCard(string title, string operationTag, string description,
            string optionKey = null, string optionValue = null)
        {
            var card = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(4, 1, 1, 1),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(12, 8, 10, 8),
                CornerRadius = new CornerRadius(3),
                SnapsToDevicePixels = true,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ── Left: title + description ────────────────────────────────
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark
            });

            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 12, 0)
            });

            Grid.SetColumn(textStack, 0);
            grid.Children.Add(textStack);

            // ── Right: Run button ────────────────────────────────────────
            var runBtn = new Button
            {
                Content = "\u25B6  Run",
                MinWidth = 60,
                Height = 26,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 2, 10, 2),
                Background = BrAccent,
                Foreground = BrWhiteFg,
                BorderBrush = BrAccent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            string capturedTag = operationTag;
            string capturedOptKey = optionKey;
            string capturedOptVal = optionValue;
            runBtn.Click += (s, e) =>
            {
                _result.Confirmed = true;
                _result.Operation = capturedTag;
                if (!string.IsNullOrEmpty(capturedOptKey))
                    _result.Options[capturedOptKey] = capturedOptVal ?? "";
                if (_win != null) _win.Close();
            };

            Grid.SetColumn(runBtn, 1);
            grid.Children.Add(runBtn);

            card.Child = grid;

            // ── Orange left border via nested border ─────────────────────
            var outerBorder = new Border
            {
                BorderBrush = BrAccent,
                BorderThickness = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 0),
                SnapsToDevicePixels = true
            };
            // Reset card's left border to standard since outer handles the accent
            card.BorderThickness = new Thickness(0, 1, 1, 1);
            card.CornerRadius = new CornerRadius(0, 3, 3, 0);
            outerBorder.Child = card;

            // Hover effects
            outerBorder.MouseEnter += (s, e) => { card.Background = BrCardHover; };
            outerBorder.MouseLeave += (s, e) => { card.Background = BrCardBg; };

            // Entire card clickable
            outerBorder.MouseLeftButtonDown += (s, e) =>
            {
                _result.Confirmed = true;
                _result.Operation = capturedTag;
                if (!string.IsNullOrEmpty(capturedOptKey))
                    _result.Options[capturedOptKey] = capturedOptVal ?? "";
                if (_win != null) _win.Close();
            };

            return outerBorder;
        }

        /// <summary>
        /// Creates an informational block with light blue background for displaying configuration summaries.
        /// </summary>
        private static Border MakeInfoBlock(string text)
        {
            var border = new Border
            {
                Background = BrInfoBg,
                BorderBrush = BrInfoBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };

            return border;
        }

        /// <summary>
        /// Wraps content in a ScrollViewer for vertical scrolling.
        /// </summary>
        private static ScrollViewer WrapInScrollViewer(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
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
                btn.Foreground = BrWhiteFg;
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
