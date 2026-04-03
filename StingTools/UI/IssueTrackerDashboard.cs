// IssueTrackerDashboard.cs — Unified WPF Issue Tracker Dashboard
// Consolidates issue creation, management, BCF integration, and SLA reporting
// into a single tabbed dialog replacing the multi-page IssueWizard flow.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>Result returned from the Issue Tracker Dashboard dialog.</summary>
    internal sealed class IssueTrackerDashboardResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = "";
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unified WPF dashboard for BIM issue management — raise issues, manage status,
    /// BCF integration, and SLA/reporting. Pure C# WPF (no XAML).
    /// </summary>
    internal static class IssueTrackerDashboard
    {
        // ── Frozen Brushes ──────────────────────────────────────────────
        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static readonly SolidColorBrush BgBrush       = FZ(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush HeaderBrush    = FZ(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly SolidColorBrush AccentBrush    = FZ(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush PanelBrush     = FZ(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush FgDarkBrush    = FZ(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush FgLightBrush   = FZ(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BorderBrush    = FZ(Color.FromRgb(0xDD, 0xDD, 0xDD));
        private static readonly SolidColorBrush CriticalBrush  = FZ(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly SolidColorBrush HighBrush      = FZ(Color.FromRgb(0xFB, 0x8C, 0x00));
        private static readonly SolidColorBrush MediumBrush    = FZ(Color.FromRgb(0xFD, 0xD8, 0x35));
        private static readonly SolidColorBrush LowBrush       = FZ(Color.FromRgb(0x43, 0xA0, 0x47));
        private static readonly SolidColorBrush SubtleBgBrush  = FZ(Color.FromRgb(0xF0, 0xF0, 0xF0));
        private static readonly SolidColorBrush HoverBrush     = FZ(Color.FromRgb(0xE3, 0xF2, 0xFD));

        // ── Issue Types ─────────────────────────────────────────────────
        private static readonly string[] IssueTypes =
        {
            "RFI", "RFA", "TQ", "CLASH", "DESIGN", "SI", "NCR", "SNAGGING",
            "CHANGE", "VO", "AI", "CVI", "EWN", "CE", "PMI", "RISK",
            "SITE", "ACTION", "COMMENT"
        };

        private static readonly string[] Disciplines =
        {
            "M — Mechanical", "E — Electrical", "P — Plumbing",
            "A — Architectural", "S — Structural", "FP — Fire Protection",
            "LV — Low Voltage", "G — General", "Z — Multi-discipline"
        };

        private static readonly string[] Assignees =
        {
            "Self", "BIM Coordinator", "Design Lead", "Project Manager",
            "Contractor", "Specialist", "Unassigned"
        };

        // ── Public Entry Point ──────────────────────────────────────────
        public static IssueTrackerDashboardResult Show()
        {
            var result = new IssueTrackerDashboardResult();

            var win = new Window
            {
                Title = "STING Issue Tracker Dashboard",
                Width = 740,
                Height = 680,
                MinWidth = 640,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BgBrush,
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            try
            {
                var helper = new WindowInteropHelper(win);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* non-critical */ }

            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = HeaderBrush,
                Padding = new Thickness(20, 14, 20, 14)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "STING Issue Tracker",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = FgLightBrush
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Raise, manage, and track BIM issues with BCF integration and SLA monitoring",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xBB, 0xBB, 0xDD)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──────────────────────────────────────────────────
            var footer = new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var footerPanel = new DockPanel();
            var btnClose = MakeButton("Close", 90);
            btnClose.Click += (s, e) => win.DialogResult = false;
            DockPanel.SetDock(btnClose, Dock.Right);
            footerPanel.Children.Add(btnClose);
            footerPanel.Children.Add(new TextBlock
            {
                Text = "ISO 19650 Issue Management",
                Foreground = FZ(Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Tab Control ─────────────────────────────────────────────
            var tabs = new TabControl
            {
                Margin = new Thickness(12, 10, 12, 6),
                Background = Brushes.Transparent,
                BorderBrush = BorderBrush
            };

            tabs.Items.Add(BuildRaiseIssueTab(result, win));
            tabs.Items.Add(BuildManageIssuesTab(result, win));
            tabs.Items.Add(BuildBcfIntegrationTab(result, win));
            tabs.Items.Add(BuildReportsSlaTab(result, win));

            root.Children.Add(tabs);
            win.Content = root;

            bool? dlg = win.ShowDialog();
            if (dlg != true) return null;
            return result;
        }

        // ================================================================
        //  TAB 1 — RAISE ISSUE
        // ================================================================
        private static TabItem BuildRaiseIssueTab(IssueTrackerDashboardResult result, Window win)
        {
            var tab = new TabItem
            {
                Header = MakeTabHeader("RAISE ISSUE"),
                Padding = new Thickness(0)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var stack = new StackPanel();

            // ── Issue Type ──────────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("Issue Type"));

            var cmbType = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 12),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12
            };
            foreach (var t in IssueTypes)
                cmbType.Items.Add(t);
            cmbType.SelectedIndex = 0;
            stack.Children.Add(cmbType);

            // ── Priority ────────────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("Priority"));

            var priorityPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            var rbCritical = MakePriorityRadio("CRITICAL", CriticalBrush, true);
            var rbHigh     = MakePriorityRadio("HIGH", HighBrush, false);
            var rbMedium   = MakePriorityRadio("MEDIUM", MediumBrush, false);
            var rbLow      = MakePriorityRadio("LOW", LowBrush, false);
            priorityPanel.Children.Add(rbCritical);
            priorityPanel.Children.Add(rbHigh);
            priorityPanel.Children.Add(rbMedium);
            priorityPanel.Children.Add(rbLow);
            stack.Children.Add(priorityPanel);

            // ── Details ─────────────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("Details"));

            var detailGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 5; i++)
                detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            AddDetailRow(detailGrid, 0, "Title:");
            var txtTitle = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12
            };
            Grid.SetRow(txtTitle, 0);
            Grid.SetColumn(txtTitle, 1);
            txtTitle.Margin = new Thickness(0, 0, 0, 6);
            detailGrid.Children.Add(txtTitle);

            // Description
            AddDetailRow(detailGrid, 1, "Description:");
            var txtDesc = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 54,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(txtDesc, 1);
            Grid.SetColumn(txtDesc, 1);
            txtDesc.Margin = new Thickness(0, 0, 0, 6);
            detailGrid.Children.Add(txtDesc);

            // Discipline
            AddDetailRow(detailGrid, 2, "Discipline:");
            var cmbDisc = new ComboBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12
            };
            foreach (var d in Disciplines) cmbDisc.Items.Add(d);
            cmbDisc.SelectedIndex = 0;
            Grid.SetRow(cmbDisc, 2);
            Grid.SetColumn(cmbDisc, 1);
            cmbDisc.Margin = new Thickness(0, 0, 0, 6);
            detailGrid.Children.Add(cmbDisc);

            // Location
            AddDetailRow(detailGrid, 3, "Location:");
            var txtLocation = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12
            };
            Grid.SetRow(txtLocation, 3);
            Grid.SetColumn(txtLocation, 1);
            txtLocation.Margin = new Thickness(0, 0, 0, 6);
            detailGrid.Children.Add(txtLocation);

            // Due Date
            AddDetailRow(detailGrid, 4, "Due Date:");
            var txtDue = new TextBox
            {
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12,
                Text = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd")
            };
            Grid.SetRow(txtDue, 4);
            Grid.SetColumn(txtDue, 1);
            txtDue.Margin = new Thickness(0, 0, 0, 6);
            detailGrid.Children.Add(txtDue);

            stack.Children.Add(detailGrid);

            // ── Assignment ──────────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("Assignment"));

            var cmbAssign = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 16),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12
            };
            foreach (var a in Assignees) cmbAssign.Items.Add(a);
            cmbAssign.SelectedIndex = 6; // Unassigned
            stack.Children.Add(cmbAssign);

            // ── Raise Button ────────────────────────────────────────────
            var btnRaise = new Button
            {
                Content = "Raise Issue",
                Background = AccentBrush,
                Foreground = FgLightBrush,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Padding = new Thickness(24, 8, 24, 8),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(0)
            };
            btnRaise.Click += (s, e) =>
            {
                string title = txtTitle.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(title))
                {
                    MessageBox.Show("Please enter an issue title.", "STING Issue Tracker",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string priority = "CRITICAL";
                if (rbHigh.IsChecked == true) priority = "HIGH";
                else if (rbMedium.IsChecked == true) priority = "MEDIUM";
                else if (rbLow.IsChecked == true) priority = "LOW";

                string discRaw = cmbDisc.SelectedItem?.ToString() ?? "G — General";
                string discCode = discRaw.Split(new[] { " — " }, StringSplitOptions.None)[0].Trim();

                result.Confirmed = true;
                result.Operation = "RaiseIssue";
                result.Options["IssueType"] = cmbType.SelectedItem?.ToString() ?? "RFI";
                result.Options["Priority"] = priority;
                result.Options["Title"] = title;
                result.Options["Description"] = txtDesc.Text?.Trim() ?? "";
                result.Options["Discipline"] = discCode;
                result.Options["Location"] = txtLocation.Text?.Trim() ?? "";
                result.Options["DueDate"] = txtDue.Text?.Trim() ?? "";
                result.Options["AssignedTo"] = cmbAssign.SelectedItem?.ToString() ?? "Unassigned";

                win.DialogResult = true;
            };
            stack.Children.Add(btnRaise);

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ================================================================
        //  TAB 2 — MANAGE ISSUES
        // ================================================================
        private static TabItem BuildManageIssuesTab(IssueTrackerDashboardResult result, Window win)
        {
            var tab = new TabItem
            {
                Header = MakeTabHeader("MANAGE ISSUES"),
                Padding = new Thickness(0)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("Issue Management"));

            stack.Children.Add(MakeOperationCard(
                "Issue Dashboard", "IssueDashboard",
                "View all open, closed, and overdue issues with SLA status and priority breakdown.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Update Issue", "UpdateIssue",
                "Update the status, priority, or assignment of an existing issue.",
                AccentBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Select Issue Elements", "SelectIssueElements",
                "Select all Revit elements linked to a specific issue for review.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Batch Issue Update", "IssueBatchUpdate",
                "Bulk status change across multiple issues — close, escalate, or reassign.",
                AccentBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Assign Issues", "AssignIssues",
                "Reassign issues to team members based on discipline or workload.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Escalate Overdue", "EscalateOverdueActions",
                "Auto-escalate overdue actions to higher priority and create NCR issues.",
                CriticalBrush, result, win));

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ================================================================
        //  TAB 3 — BCF & INTEGRATION
        // ================================================================
        private static TabItem BuildBcfIntegrationTab(IssueTrackerDashboardResult result, Window win)
        {
            var tab = new TabItem
            {
                Header = MakeTabHeader("BCF & INTEGRATION"),
                Padding = new Thickness(0)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("BCF Exchange"));

            stack.Children.Add(MakeOperationCard(
                "BCF Export", "BCFExport",
                "Export issues as BCF 2.1 XML with viewpoints and camera data for external coordination tools.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "BCF Import", "BCFImport",
                "Import BCF files from ACC, Procore, Navisworks, or other BCF-compatible tools.",
                AccentBrush, result, win));

            stack.Children.Add(MakeSectionHeader("Cross-System Links"));

            stack.Children.Add(MakeOperationCard(
                "Issue-Revision Link", "IssueRevisionLink",
                "Link closed issues to revision snapshots for ISO 19650 audit trail.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Auto Meeting Minutes", "AutoMeetingMinutes",
                "Generate meeting minutes from open and recently closed issues.",
                AccentBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Create Transmittal from Issues", "CreateTransmittal",
                "Package issue resolution documents into an ISO 19650 transmittal.",
                HeaderBrush, result, win));

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ================================================================
        //  TAB 4 — REPORTS & SLA
        // ================================================================
        private static TabItem BuildReportsSlaTab(IssueTrackerDashboardResult result, Window win)
        {
            var tab = new TabItem
            {
                Header = MakeTabHeader("REPORTS & SLA"),
                Padding = new Thickness(0)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("SLA Monitoring"));

            stack.Children.Add(MakeOperationCard(
                "SLA Violation Report", "SLAViolationReport",
                "Check SLA breaches: CRITICAL 4h, HIGH 24h, MEDIUM 1 week, LOW 2 weeks per ISO 19650.",
                CriticalBrush, result, win));

            stack.Children.Add(MakeSectionHeader("Reports"));

            stack.Children.Add(MakeOperationCard(
                "Team Report", "TeamReport",
                "Per-assignee workload breakdown showing open issues, tasks, and overdue items.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Export Dashboard HTML", "ExportDashboardHTML",
                "Generate self-contained HTML report with KPI cards and compliance charts.",
                AccentBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Tag Revision Diff", "TagRevisionDiff",
                "Compare tag values between two revision snapshots — see what changed per token.",
                HeaderBrush, result, win));

            stack.Children.Add(MakeOperationCard(
                "Weekly Coordinator Report", "WeeklyCoordinatorReport",
                "Comprehensive HTML weekly report with compliance trend and issue metrics.",
                AccentBrush, result, win));

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ================================================================
        //  UI HELPERS
        // ================================================================

        private static TextBlock MakeTabHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 4, 8, 4)
            };
        }

        private static TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = HeaderBrush,
                Margin = new Thickness(0, 4, 0, 8)
            };
        }

        private static void AddDetailRow(Grid grid, int row, string label)
        {
            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = FgDarkBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 6)
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
        }

        private static RadioButton MakePriorityRadio(string label, SolidColorBrush dotColor, bool isChecked)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = dotColor,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(dot);
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            return new RadioButton
            {
                Content = panel,
                GroupName = "Priority",
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Border MakeOperationCard(
            string title, string dispatchTag, string description,
            SolidColorBrush accentColor,
            IssueTrackerDashboardResult result, Window win)
        {
            var card = new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            var innerBorder = new Border
            {
                BorderBrush = accentColor,
                BorderThickness = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FgDarkBrush
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0x66, 0x66, 0x66)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            innerBorder.Child = stack;
            card.Child = innerBorder;

            // Hover effect
            card.MouseEnter += (s, e) => card.Background = HoverBrush;
            card.MouseLeave += (s, e) => card.Background = PanelBrush;

            // Click handler
            card.MouseLeftButtonUp += (s, e) =>
            {
                result.Confirmed = true;
                result.Operation = dispatchTag;
                win.DialogResult = true;
            };

            return card;
        }

        private static Button MakeButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Cursor = Cursors.Hand
            };
        }
    }
}
