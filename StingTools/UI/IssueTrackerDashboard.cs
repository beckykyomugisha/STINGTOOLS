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
using StingTools.Core;

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

        // Phase 78 Section 2.3: Issue types now sourced from BIMCoordinationCenter.IsoIssueTypes
        // Kept as property for backward compatibility with any remaining direct IssueTypes[] references.
        private static IEnumerable<string> IssueTypes =>
            BIMCoordinationCenter.IsoIssueTypes.Select(t => t.Code);

        private static readonly string[] Disciplines =
        {
            "M — Mechanical", "E — Electrical", "P — Plumbing",
            "A — Architectural", "S — Structural", "FP — Fire Protection",
            "LV — Low Voltage", "G — General", "Z — Multi-discipline"
        };

        // ── Public Entry Point ──────────────────────────────────────────
        public static IssueTrackerDashboardResult Show(List<string> memberNames = null)
        {
            var resolvedAssignees = (memberNames != null && memberNames.Count > 0)
                ? memberNames
                : new List<string> { "Self", "BIM Coordinator", "Design Lead", "Project Manager",
                                      "Architect", "Structural Engineer", "MEP Engineer",
                                      "Mechanical Engineer", "Electrical Engineer", "Contractor",
                                      "Site Manager", "Facilities Manager", "Unassigned" };
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
            catch (Exception ex) { StingLog.Warn($"Issue tracker window owner: {ex.Message}"); }

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

            tabs.Items.Add(BuildRaiseIssueTab(result, win, resolvedAssignees));
            tabs.Items.Add(BuildManageIssuesTab(result, win, resolvedAssignees));
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
        private static TabItem BuildRaiseIssueTab(IssueTrackerDashboardResult result, Window win, List<string> assignees = null)
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
            // Phase 78 Section 2.3: Populate from IsoIssueTypes with label + tooltip
            foreach (var it in BIMCoordinationCenter.IsoIssueTypes)
                cmbType.Items.Add(new ComboBoxItem
                {
                    Content = $"{it.Code} — {it.Label}",
                    Tag = it.Code,
                    ToolTip = it.Description
                });
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
                IsEditable = true,
                IsTextSearchEnabled = true,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12
            };
            var effectiveAssignees = assignees ?? new List<string> { "Self", "BIM Coordinator", "Design Lead", "Project Manager",
                                      "Architect", "Structural Engineer", "MEP Engineer",
                                      "Mechanical Engineer", "Electrical Engineer", "Contractor",
                                      "Site Manager", "Facilities Manager", "Unassigned" };
            foreach (var a in effectiveAssignees) cmbAssign.Items.Add(a);
            cmbAssign.Items.Add("── Custom ──");
            int unassignedIdx = effectiveAssignees.IndexOf("Unassigned");
            cmbAssign.SelectedIndex = unassignedIdx >= 0 ? unassignedIdx : effectiveAssignees.Count - 1;
            stack.Children.Add(cmbAssign);

            var hintText = new TextBlock
            {
                Text = "Type a name directly in the box above, or pick a role from the dropdown",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(hintText);

            // Custom assignee panel — shown when "── Custom ──" is picked
            var customPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            var lblCustom = new TextBlock
            {
                Text = "Assignee Name:",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FgDarkBrush,
                Margin = new Thickness(0, 0, 0, 3)
            };
            customPanel.Children.Add(lblCustom);
            var txtCustomAssignee = new TextBox
            {
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1)
            };
            customPanel.Children.Add(txtCustomAssignee);
            var lblCustomHint = new TextBlock
            {
                Text = "Enter the person's full name or email",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            customPanel.Children.Add(lblCustomHint);
            stack.Children.Add(customPanel);

            cmbAssign.SelectionChanged += (s, e) =>
            {
                string sel = cmbAssign.SelectedItem?.ToString() ?? "";
                if (sel == "── Custom ──")
                {
                    customPanel.Visibility = Visibility.Visible;
                    txtCustomAssignee.Focus();
                }
                else
                {
                    customPanel.Visibility = Visibility.Collapsed;
                }
            };

            // spacer before button
            stack.Children.Add(new Border { Height = 10 });

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
                // Resolve assignee: custom name field > editable combo text > selected item
                string assignee = "Unassigned";
                string customName = txtCustomAssignee.Text?.Trim() ?? "";
                string comboText = cmbAssign.Text?.Trim() ?? "";
                string comboItem = cmbAssign.SelectedItem?.ToString() ?? "";

                if (!string.IsNullOrEmpty(customName))
                    assignee = customName;
                else if (comboItem == "── Custom ──")
                    assignee = "Unassigned";
                else if (!string.IsNullOrEmpty(comboText) && comboText != "── Custom ──")
                    assignee = comboText;
                else if (!string.IsNullOrEmpty(comboItem))
                    assignee = comboItem;

                result.Options["AssignedTo"] = assignee;

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
        // ── IssueRow for live DataGrid ──────────────────────────────────
        private class IssueRowVm
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Type { get; set; }
            public string Priority { get; set; }
            public string Status { get; set; }
            public string Assignee { get; set; }
            public string Location { get; set; }
            public string Disc { get; set; }
            public int Elems { get; set; }
            public string Created { get; set; }
            public string Age { get; set; }
            public bool IsOverdue { get; set; }
            public bool IsCritical { get; set; }
            public bool IsClosed { get; set; }
        }

        private static TabItem BuildManageIssuesTab(IssueTrackerDashboardResult result, Window win, List<string> assignees = null)
        {
            var tab = new TabItem { Header = MakeTabHeader("MANAGE ISSUES"), Padding = new Thickness(0) };
            var root = new DockPanel { LastChildFill = true };

            // ── Action toolbar ───────────────────────────────────────────
            var toolbar = new WrapPanel { Margin = new Thickness(12, 10, 12, 4) };
            var toolActions = new (string Label, string Op, SolidColorBrush Clr)[]
            {
                ("Update Status",    "UpdateIssue",            AccentBrush),
                ("Reassign",         "AssignIssues",           HeaderBrush),
                ("Select Elements",  "SelectIssueElements",    HeaderBrush),
                ("BCF Export",       "BCFExport",              HeaderBrush),
                ("Close Issue",      "CloseIssue",             LowBrush),
                ("Escalate",         "EscalateOverdueActions", CriticalBrush),
                ("Export Excel",     "IssueExport",            HeaderBrush),
            };
            foreach (var (lbl, op, clr) in toolActions)
            {
                var btn = MakeButton(lbl, double.NaN);
                btn.Background = clr; btn.Foreground = FgLightBrush; btn.Margin = new Thickness(0, 0, 6, 4);
                btn.BorderThickness = new Thickness(0);
                string capturedOp = op;
                btn.Click += (s, e) => { result.Confirmed = true; result.Operation = capturedOp; win.DialogResult = true; };
                toolbar.Children.Add(btn);
            }
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            // ── Filter bar ──────────────────────────────────────────────
            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 6) };
            var cmbFilter = new ComboBox { Width = 130, Height = 26, FontSize = 11, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var f in new[] { "All", "Open", "Closed", "Critical", "Overdue" }) cmbFilter.Items.Add(f);
            cmbFilter.SelectedIndex = 0;
            var txtSearch = new TextBox { Width = 180, Height = 26, FontSize = 11, Padding = new Thickness(4, 2, 4, 2), ToolTip = "Search title, assignee, ID..." };
            filterRow.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = 11 });
            filterRow.Children.Add(cmbFilter);
            filterRow.Children.Add(txtSearch);
            DockPanel.SetDock(filterRow, Dock.Top);
            root.Children.Add(filterRow);

            // ── DataGrid ────────────────────────────────────────────────
            var dg = new DataGrid
            {
                IsReadOnly = true,
                AutoGenerateColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                FontSize = 11,
                RowHeight = 24,
                Margin = new Thickness(12, 0, 12, 10),
                CanUserSortColumns = true
            };
            dg.Columns.Add(new DataGridTextColumn { Header = "ID",       Binding = new System.Windows.Data.Binding("Id"),       Width = 80 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Title",    Binding = new System.Windows.Data.Binding("Title"),    Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Type",     Binding = new System.Windows.Data.Binding("Type"),     Width = 55 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new System.Windows.Data.Binding("Priority"), Width = 65 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Status",   Binding = new System.Windows.Data.Binding("Status"),   Width = 65 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Assignee", Binding = new System.Windows.Data.Binding("Assignee"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Location", Binding = new System.Windows.Data.Binding("Location"), Width = 80 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Disc",     Binding = new System.Windows.Data.Binding("Disc"),     Width = 40 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Elems",    Binding = new System.Windows.Data.Binding("Elems"),    Width = 45 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Created",  Binding = new System.Windows.Data.Binding("Created"),  Width = 80 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Age",      Binding = new System.Windows.Data.Binding("Age"),      Width = 50 });
            root.Children.Add(dg);

            // ── Load data from LastIssuesPath ────────────────────────────
            var allRows = new List<IssueRowVm>();
            try
            {
                string issPath = StingCommandHandler.GetExtraParam("LastIssuesPath") ?? "";
                if (!string.IsNullOrEmpty(issPath) && System.IO.File.Exists(issPath))
                {
                    var arr = Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(issPath));
                    int idx = 0;
                    foreach (var item in arr)
                    {
                        string status = (item["status"]?.ToString() ?? "OPEN").ToUpperInvariant();
                        string priority = (item["priority"]?.ToString() ?? "MEDIUM").ToUpperInvariant();
                        string created = item["date_raised"]?.ToString() ?? item["created"]?.ToString() ?? item["created_date"]?.ToString() ?? "";
                        bool overdue = false; string age = "";
                        if (DateTime.TryParse(created, out DateTime cDt))
                        {
                            int d = (int)(DateTime.Now - cDt).TotalDays;
                            age = d < 1 ? "<1d" : d < 7 ? $"{d}d" : d < 30 ? $"{d/7}w" : $"{d/30}mo";
                            double slaH = priority == "CRITICAL" ? 4 : priority == "HIGH" ? 24 : priority == "MEDIUM" ? 168 : 336;
                            if (status == "OPEN" && (DateTime.Now - cDt).TotalHours > slaH) overdue = true;
                        }
                        int elemCount = 0;
                        var ea = item["element_ids"] as Newtonsoft.Json.Linq.JArray;
                        if (ea != null) elemCount = ea.Count;
                        allRows.Add(new IssueRowVm
                        {
                            Id = item["issue_id"]?.ToString() ?? item["id"]?.ToString() ?? $"ISS-{++idx:D3}",
                            Title = item["title"]?.ToString() ?? "",
                            Type = item["type"]?.ToString() ?? item["category"]?.ToString() ?? "RFI",
                            Priority = priority, Status = status,
                            Assignee = item["assignee"]?.ToString() ?? item["assigned_to"]?.ToString() ?? "Unassigned",
                            Location = item["location"]?.ToString() ?? "",
                            Disc = item["discipline"]?.ToString() ?? "",
                            Elems = elemCount,
                            Created = created.Length > 10 ? created.Substring(0, 10) : created,
                            Age = age, IsOverdue = overdue,
                            IsCritical = priority == "CRITICAL",
                            IsClosed = status == "CLOSED"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ITD ManageIssues load: {ex.Message}"); }

            // Row styles
            dg.LoadingRow += (s, e) =>
            {
                if (e.Row.Item is IssueRowVm row)
                {
                    if (row.IsOverdue)
                    {
                        e.Row.Background = FZ(Color.FromRgb(0xFF, 0xEB, 0xEE));
                        e.Row.Foreground = CriticalBrush;
                    }
                    else if (row.IsCritical) { e.Row.FontWeight = FontWeights.Bold; }
                    else if (row.IsClosed) { e.Row.Foreground = FZ(Color.FromRgb(0xAA, 0xAA, 0xAA)); }
                    else { e.Row.Background = Brushes.White; e.Row.Foreground = FgDarkBrush; }
                }
            };

            // Apply filter
            void ApplyFilter(string filter, string search)
            {
                var filtered = allRows.AsEnumerable();
                if (filter == "Open")     filtered = filtered.Where(r => r.Status == "OPEN");
                else if (filter == "Closed")   filtered = filtered.Where(r => r.Status == "CLOSED");
                else if (filter == "Critical") filtered = filtered.Where(r => r.IsCritical);
                else if (filter == "Overdue")  filtered = filtered.Where(r => r.IsOverdue);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    string q = search.ToLowerInvariant();
                    filtered = filtered.Where(r =>
                        (r.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                        (r.Id?.ToLowerInvariant().Contains(q) ?? false) ||
                        (r.Assignee?.ToLowerInvariant().Contains(q) ?? false));
                }
                dg.ItemsSource = filtered.ToList();
            }

            cmbFilter.SelectionChanged += (s, e) => ApplyFilter(cmbFilter.SelectedItem?.ToString() ?? "All", txtSearch.Text);
            txtSearch.TextChanged += (s, e) => ApplyFilter(cmbFilter.SelectedItem?.ToString() ?? "All", txtSearch.Text);
            ApplyFilter("All", "");

            tab.Content = root;
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
