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
    /// Result returned from the ScheduleWizardDialog.
    /// </summary>
    public class ScheduleWizardResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = string.Empty;
        public List<string> SelectedSchedules { get; set; } = new();
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>
    /// Comprehensive Scheduling Dashboard combining ALL scheduling tools:
    /// CREATE & POPULATE, AUDIT & ANALYSIS, MANAGE, EXPORT, FORMAT, CORPORATE/MEP.
    /// Replaces the original 6-operation wizard with a full tabbed dashboard.
    /// </summary>
    internal static class ScheduleWizardDialog
    {
        // ── Theme colours (light corporate) ─────────────────────────────
        private static readonly Color BgColor = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color PanelBg = Colors.White;
        private static readonly Color HeaderBg = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgDim = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly Color BorderClr = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color SelectedBg = Color.FromRgb(0xFD, 0xF0, 0xDD);
        private static readonly Color TabActiveBg = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color TabInactiveBg = Color.FromRgb(0xE8, 0xE8, 0xE8);
        private static readonly Color SectionHeaderBg = Color.FromRgb(0xF0, 0xF0, 0xF0);
        private static readonly Color GreenAccent = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color BlueAccent = Color.FromRgb(0x42, 0x9E, 0xE6);
        private static readonly Color RedAccent = Color.FromRgb(0xE5, 0x39, 0x35);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static readonly SolidColorBrush BrBg = FZ(BgColor);
        private static readonly SolidColorBrush BrPanel = FZ(PanelBg);
        private static readonly SolidColorBrush BrHeader = FZ(HeaderBg);
        private static readonly SolidColorBrush BrAccent = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFg = FZ(FgDark);
        private static readonly SolidColorBrush BrFgDim = FZ(FgDim);
        private static readonly SolidColorBrush BrBorder = FZ(BorderClr);
        private static readonly SolidColorBrush BrSelected = FZ(SelectedBg);
        private static readonly SolidColorBrush BrWhite = FZ(Colors.White);
        private static readonly SolidColorBrush BrTabActive = FZ(TabActiveBg);
        private static readonly SolidColorBrush BrTabInactive = FZ(TabInactiveBg);
        private static readonly SolidColorBrush BrSectionHeader = FZ(SectionHeaderBg);
        private static readonly SolidColorBrush BrGreen = FZ(GreenAccent);
        private static readonly SolidColorBrush BrBlue = FZ(BlueAccent);
        private static readonly SolidColorBrush BrRed = FZ(RedAccent);

        // ── Schedule item model ─────────────────────────────────────────
        private class ScheduleItem
        {
            public string Name { get; set; } = string.Empty;
            public bool ExistsInProject { get; set; }
            public bool ExistsInCsv { get; set; }
            public bool IsSelected { get; set; }
        }

        /// <summary>
        /// Show the comprehensive scheduling dashboard.
        /// </summary>
        public static ScheduleWizardResult Show(
            List<string> csvDefinitions = null,
            List<string> existingSchedules = null)
        {
            csvDefinitions ??= new List<string>();
            existingSchedules ??= new List<string>();

            var result = new ScheduleWizardResult();
            var allItems = BuildScheduleItems(csvDefinitions, existingSchedules);

            var win = new Window
            {
                Title = "STING Scheduling Dashboard",
                Width = 850,
                Height = 620,
                MinWidth = 750,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBg,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = handle;
            }
            catch (Exception ex) { StingLog.Warn($"ScheduleWizardDialog: Could not set owner — {ex.Message}"); }

            // ── Root layout: header + body + footer ─────────────────────
            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Scheduling Dashboard",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrWhite
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"{csvDefinitions.Count} CSV definitions  |  {existingSchedules.Count} project schedules",
                FontSize = 11,
                Foreground = FZ(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer: action buttons ──────────────────────────────────
            var footer = new Border { Padding = new Thickness(12, 8, 12, 10), Background = BrPanel };
            var footerPanel = new DockPanel { LastChildFill = false };

            var btnCancel = new Button
            {
                Content = "Close",
                Width = 80, Height = 30, FontSize = 12,
                Background = BrWhite, BorderBrush = BrBorder, Cursor = Cursors.Hand
            };
            btnCancel.Click += (_, __) => { win.DialogResult = false; };
            DockPanel.SetDock(btnCancel, Dock.Right);
            footerPanel.Children.Add(btnCancel);

            var btnExecute = new Button
            {
                Content = "Execute",
                Width = 110, Height = 30, FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = BrAccent, Foreground = BrWhite, BorderBrush = BrAccent,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(btnExecute, Dock.Right);
            footerPanel.Children.Add(btnExecute);

            var statusText = new TextBlock
            {
                Text = string.Empty, FontSize = 11,
                Foreground = BrFgDim, VerticalAlignment = VerticalAlignment.Center
            };
            footerPanel.Children.Add(statusText);
            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Body: left nav + right content ──────────────────────────
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ── Left navigation panel ───────────────────────────────────
            var navBorder = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 8, 0, 8)
            };
            var navStack = new StackPanel();

            string selectedTab = "create";
            var tabButtons = new Dictionary<string, Border>();
            var contentPanels = new Dictionary<string, UIElement>();

            var navItems = new[]
            {
                ("create",    "CREATE & POPULATE", "Batch create, auto-populate, full auto"),
                ("audit",     "AUDIT & ANALYSIS",  "Audit, compare, stats, report"),
                ("manage",    "MANAGE",            "Duplicate, delete, refresh, field manager"),
                ("export",    "EXPORT",            "CSV/XLSX export, schedule-to-Excel"),
                ("format",    "FORMAT",            "Column widths, alignment, visibility"),
                ("corporate", "CORPORATE / MEP",   "Title block, register, MEP schedules")
            };

            // Content area
            var contentBorder = new Border { Padding = new Thickness(16, 12, 16, 12) };
            var contentHost = new Grid();
            contentBorder.Child = contentHost;
            Grid.SetColumn(contentBorder, 1);
            body.Children.Add(contentBorder);

            // Build all content panels
            var scheduleItems = new List<ScheduleItem>(allItems);
            contentPanels["create"] = BuildCreateTab(scheduleItems, csvDefinitions, statusText);
            contentPanels["audit"] = BuildAuditTab(scheduleItems, statusText);
            contentPanels["manage"] = BuildManageTab(scheduleItems, statusText);
            contentPanels["export"] = BuildExportTab(scheduleItems, statusText);
            contentPanels["format"] = BuildFormatTab(statusText);
            contentPanels["corporate"] = BuildCorporateMepTab(statusText);

            foreach (var kvp in contentPanels)
            {
                kvp.Value.Visibility = Visibility.Collapsed;
                contentHost.Children.Add(kvp.Value);
            }

            void SelectTab(string tab)
            {
                selectedTab = tab;
                foreach (var kvp in tabButtons)
                {
                    bool sel = kvp.Key == tab;
                    kvp.Value.Background = sel ? BrSelected : BrPanel;
                    var sp = kvp.Value.Child as StackPanel;
                    if (sp != null && sp.Children.Count > 0)
                    {
                        var lbl = sp.Children[0] as TextBlock;
                        if (lbl != null) lbl.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                }
                foreach (var kvp in contentPanels)
                    kvp.Value.Visibility = kvp.Key == tab ? Visibility.Visible : Visibility.Collapsed;
            }

            foreach (var (code, label, tooltip) in navItems)
            {
                var navBtn = new Border
                {
                    Background = BrPanel,
                    Padding = new Thickness(14, 8, 14, 8),
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip
                };
                var navBtnStack = new StackPanel();
                navBtnStack.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11.5, Foreground = BrFg
                });
                navBtn.Child = navBtnStack;
                navBtn.MouseEnter += (_, __) => { if (code != selectedTab) navBtn.Background = FZ(Color.FromRgb(0xF0, 0xF0, 0xF0)); };
                navBtn.MouseLeave += (_, __) => { if (code != selectedTab) navBtn.Background = BrPanel; };
                string cap = code;
                navBtn.MouseLeftButtonDown += (_, __) => SelectTab(cap);
                tabButtons[code] = navBtn;
                navStack.Children.Add(navBtn);
            }

            navBorder.Child = navStack;
            Grid.SetColumn(navBorder, 0);
            body.Children.Add(navBorder);

            root.Children.Add(body);
            win.Content = root;

            // ── Execute button wiring ────────────────────────────────────
            btnExecute.Click += (_, __) =>
            {
                result.Confirmed = true;
                result.Operation = CollectOperation(selectedTab, contentPanels);
                result.SelectedSchedules = scheduleItems.Where(s => s.IsSelected).Select(s => s.Name).ToList();
                result.Options = CollectAllOptions(selectedTab, contentPanels);
                win.DialogResult = true;
            };

            // Initialise
            SelectTab("create");

            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) win.DialogResult = false;
            };

            bool? dlgResult = false;
            try { dlgResult = win.ShowDialog(); }
            catch (Exception ex) { StingLog.Warn($"ScheduleWizardDialog: ShowDialog failed — {ex.Message}"); }

            if (dlgResult != true) result.Confirmed = false;
            return result;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 1: CREATE & POPULATE
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildCreateTab(List<ScheduleItem> items, List<string> csvDefs, TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            // ── Operation selector ──────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("OPERATION"));
            var opGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var rbCreateBatch = new RadioButton { Content = "Batch Create — Create schedules from CSV definitions", FontSize = 12, Foreground = BrFg, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), GroupName = "CreateOp", Tag = "CreateBatch" };
            var rbAutoPopulate = new RadioButton { Content = "Auto-Populate — Fill schedule fields with token data", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "CreateOp", Tag = "AutoPopulate" };
            var rbFullAuto = new RadioButton { Content = "Full Auto — Zero-input: populate + create + formulas", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "CreateOp", Tag = "FullAuto" };

            opGroup.Children.Add(rbCreateBatch);
            opGroup.Children.Add(rbAutoPopulate);
            opGroup.Children.Add(rbFullAuto);
            stack.Children.Add(opGroup);

            // ── Discipline filter ───────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("DISCIPLINE FILTER"));
            var discWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var chkM = MakeCheckBox("M - Mechanical", true);
            var chkE = MakeCheckBox("E - Electrical", true);
            var chkP = MakeCheckBox("P - Plumbing", true);
            var chkA = MakeCheckBox("A - Architectural", true);
            var chkS = MakeCheckBox("S - Structural", true);
            var chkFP = MakeCheckBox("FP - Fire Protection", true);
            discWrap.Children.Add(chkM); discWrap.Children.Add(chkE); discWrap.Children.Add(chkP);
            discWrap.Children.Add(chkA); discWrap.Children.Add(chkS); discWrap.Children.Add(chkFP);
            stack.Children.Add(discWrap);

            // ── Auto-populate options ────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("AUTO-POPULATE OPTIONS"));
            var chkOverwrite = MakeCheckBox("Overwrite existing values", false);
            var chkFormulas = MakeCheckBox("Include formula evaluation", true);
            stack.Children.Add(chkOverwrite);
            stack.Children.Add(chkFormulas);

            // ── Schedule list ───────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("SCHEDULES"));
            var listPanel = BuildScheduleListPanel(items);
            stack.Children.Add(listPanel);

            // Store controls for collection
            stack.Tag = new CreateTabState
            {
                RbCreateBatch = rbCreateBatch, RbAutoPopulate = rbAutoPopulate, RbFullAuto = rbFullAuto,
                ChkM = chkM, ChkE = chkE, ChkP = chkP, ChkA = chkA, ChkS = chkS, ChkFP = chkFP,
                ChkOverwrite = chkOverwrite, ChkFormulas = chkFormulas
            };

            scroll.Content = stack;
            return scroll;
        }

        private class CreateTabState
        {
            public RadioButton RbCreateBatch, RbAutoPopulate, RbFullAuto;
            public CheckBox ChkM, ChkE, ChkP, ChkA, ChkS, ChkFP;
            public CheckBox ChkOverwrite, ChkFormulas;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 2: AUDIT & ANALYSIS
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildAuditTab(List<ScheduleItem> items, TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("OPERATION"));
            var opGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var rbAudit = new RadioButton { Content = "Audit — Compare existing schedules against CSV definitions", FontSize = 12, Foreground = BrFg, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), GroupName = "AuditOp", Tag = "ScheduleAudit" };
            var rbCompare = new RadioButton { Content = "Compare — Side-by-side field comparison between two schedules", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "AuditOp", Tag = "ScheduleCompare" };
            var rbStats = new RadioButton { Content = "Statistics — Row/field counts, data coverage, value distribution", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "AuditOp", Tag = "ScheduleStats" };
            var rbReport = new RadioButton { Content = "Report — Comprehensive schedule health report with RAG status", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "AuditOp", Tag = "ScheduleReport" };

            opGroup.Children.Add(rbAudit);
            opGroup.Children.Add(rbCompare);
            opGroup.Children.Add(rbStats);
            opGroup.Children.Add(rbReport);
            stack.Children.Add(opGroup);

            // ── Schedule selection for audit/compare ─────────────────────
            stack.Children.Add(MakeSectionHeader("SELECT SCHEDULES"));
            var listPanel = BuildScheduleListPanel(items);
            stack.Children.Add(listPanel);

            stack.Tag = new AuditTabState { RbAudit = rbAudit, RbCompare = rbCompare, RbStats = rbStats, RbReport = rbReport };

            scroll.Content = stack;
            return scroll;
        }

        private class AuditTabState
        {
            public RadioButton RbAudit, RbCompare, RbStats, RbReport;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 3: MANAGE
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildManageTab(List<ScheduleItem> items, TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("OPERATION"));
            var opGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var rbDuplicate = new RadioButton { Content = "Duplicate — Copy selected schedules with new names", FontSize = 12, Foreground = BrFg, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), GroupName = "ManageOp", Tag = "ScheduleDuplicate" };
            var rbDelete = new RadioButton { Content = "Delete — Remove selected schedules from project", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "ManageOp", Tag = "ScheduleDelete" };
            var rbRefresh = new RadioButton { Content = "Refresh — Rebuild fields from CSV definitions", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "ManageOp", Tag = "ScheduleRefresh" };
            var rbFieldMgr = new RadioButton { Content = "Field Manager — Add, remove, reorder schedule fields", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "ManageOp", Tag = "ScheduleFieldMgr" };

            opGroup.Children.Add(rbDuplicate);
            opGroup.Children.Add(rbDelete);
            opGroup.Children.Add(rbRefresh);
            opGroup.Children.Add(rbFieldMgr);
            stack.Children.Add(opGroup);

            stack.Children.Add(MakeSectionHeader("SELECT SCHEDULES"));
            var listPanel = BuildScheduleListPanel(items);
            stack.Children.Add(listPanel);

            stack.Tag = new ManageTabState { RbDuplicate = rbDuplicate, RbDelete = rbDelete, RbRefresh = rbRefresh, RbFieldMgr = rbFieldMgr };

            scroll.Content = stack;
            return scroll;
        }

        private class ManageTabState
        {
            public RadioButton RbDuplicate, RbDelete, RbRefresh, RbFieldMgr;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 4: EXPORT
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildExportTab(List<ScheduleItem> items, TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("EXPORT OPERATION"));
            var opGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var rbExportCsv = new RadioButton { Content = "Export CSV — Export schedule data to CSV file", FontSize = 12, Foreground = BrFg, IsChecked = true, Margin = new Thickness(0, 2, 0, 2), GroupName = "ExportOp", Tag = "ExportCSV" };
            var rbExportExcel = new RadioButton { Content = "Export to Excel — Export schedules to XLSX workbook", FontSize = 12, Foreground = BrFg, Margin = new Thickness(0, 2, 0, 2), GroupName = "ExportOp", Tag = "ScheduleToExcel" };

            opGroup.Children.Add(rbExportCsv);
            opGroup.Children.Add(rbExportExcel);
            stack.Children.Add(opGroup);

            // ── Output path ─────────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("OUTPUT"));
            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var browseBtn = new Button
            {
                Content = "Browse...", Width = 80, Height = 26, FontSize = 11,
                Background = BrWhite, BorderBrush = BrBorder, Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0)
            };
            DockPanel.SetDock(browseBtn, Dock.Right);
            pathRow.Children.Add(browseBtn);

            var txtPath = new TextBox
            {
                FontSize = 11, Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1), Background = BrWhite,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            pathRow.Children.Add(txtPath);
            stack.Children.Add(pathRow);

            var capturedPath = txtPath;
            browseBtn.Click += (_, __) =>
            {
                try
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Export Schedule Data",
                        InitialDirectory = System.IO.Path.GetDirectoryName(capturedPath.Text) ?? capturedPath.Text,
                        Filter = "CSV Files (*.csv)|*.csv|Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                        FileName = "STING_Schedules"
                    };
                    if (dlg.ShowDialog() == true)
                        capturedPath.Text = dlg.FileName;
                }
                catch (Exception ex) { StingLog.Warn($"ScheduleWizardDialog: Browse failed — {ex.Message}"); }
            };

            // Format
            var fmtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            fmtRow.Children.Add(new TextBlock { Text = "Format:", FontSize = 11, Foreground = BrFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var cmbFormat = new ComboBox { Width = 100, FontSize = 11, SelectedIndex = 0 };
            cmbFormat.Items.Add("CSV");
            cmbFormat.Items.Add("XLSX");
            fmtRow.Children.Add(cmbFormat);
            stack.Children.Add(fmtRow);

            // Schedule selection
            stack.Children.Add(MakeSectionHeader("SELECT SCHEDULES TO EXPORT"));
            var listPanel = BuildScheduleListPanel(items);
            stack.Children.Add(listPanel);

            stack.Tag = new ExportTabState { RbExportCsv = rbExportCsv, RbExportExcel = rbExportExcel, TxtPath = txtPath, CmbFormat = cmbFormat };

            scroll.Content = stack;
            return scroll;
        }

        private class ExportTabState
        {
            public RadioButton RbExportCsv, RbExportExcel;
            public TextBox TxtPath;
            public ComboBox CmbFormat;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 5: FORMAT
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildFormatTab(TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            stack.Children.Add(MakeSectionHeader("COLUMN FORMATTING"));
            stack.Children.Add(new TextBlock
            {
                Text = "Select a formatting operation to apply to the active schedule view.",
                FontSize = 11, Foreground = BrFgDim, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var opGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var formatOps = new[]
            {
                ("SchedAutoFit",      "Auto-Fit Columns",         "Automatically size columns to fit content"),
                ("SchedMatchWidest",  "Match Widest",             "Set all columns to match the widest column"),
                ("SchedEqualise",     "Equalise Widths",          "Make all columns the same width"),
                ("SchedSetWidth",     "Set Column Width",         "Set a specific width for selected columns"),
                ("SchedSyncPos",      "Sync Column Positions",    "Synchronise column positions across schedules"),
                ("SchedSyncRot",      "Sync Column Rotation",     "Synchronise column rotation across schedules"),
                ("SchedToggleHidden", "Toggle Hidden Columns",    "Show or hide columns in the active schedule"),
                ("SchedShowHidden",   "Show All Hidden",          "Reveal all hidden columns in the schedule"),
            };

            RadioButton firstRb = null;
            foreach (var (tag, label, tip) in formatOps)
            {
                var rb = new RadioButton
                {
                    Content = $"{label} — {tip}",
                    FontSize = 12, Foreground = BrFg,
                    Margin = new Thickness(0, 3, 0, 3),
                    GroupName = "FormatOp", Tag = tag
                };
                if (firstRb == null) { firstRb = rb; rb.IsChecked = true; }
                opGroup.Children.Add(rb);
            }
            stack.Children.Add(opGroup);

            // ── Color formatting section ────────────────────────────────
            stack.Children.Add(MakeSectionHeader("COLOR FORMATTING"));
            var rbColor = new RadioButton
            {
                Content = "Schedule Color — Apply color rules to schedule rows/cells",
                FontSize = 12, Foreground = BrFg,
                Margin = new Thickness(0, 3, 0, 3),
                GroupName = "FormatOp", Tag = "ScheduleColor"
            };
            stack.Children.Add(rbColor);

            stack.Tag = "format_tab";
            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 6: CORPORATE / MEP
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildCorporateMepTab(TextBlock status)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            // ── Corporate schedules ─────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("CORPORATE SCHEDULES"));
            var corpGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            var corpOps = new[]
            {
                ("CorporateTitleBlock",     "Corporate Title Block Schedule",  "Create title block schedule with project metadata"),
                ("DrawingRegisterSchedule", "Drawing Register Schedule",       "Create ISO 19650 drawing register"),
                ("MaterialSchedules",       "Material Schedules",              "Create BLE/MEP material quantity schedules"),
            };

            RadioButton firstCorpRb = null;
            foreach (var (tag, label, tip) in corpOps)
            {
                var rb = new RadioButton
                {
                    Content = $"{label} — {tip}",
                    FontSize = 12, Foreground = BrFg,
                    Margin = new Thickness(0, 3, 0, 3),
                    GroupName = "CorpMepOp", Tag = tag
                };
                if (firstCorpRb == null) { firstCorpRb = rb; rb.IsChecked = true; }
                corpGroup.Children.Add(rb);
            }
            stack.Children.Add(corpGroup);

            // ── MEP schedules ───────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("MEP SCHEDULES"));
            var mepGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var mepOps = new[]
            {
                ("PanelSchedule",            "Panel Schedule",             "Electrical panel schedule with circuits"),
                ("LightingFixtureSchedule",  "Lighting Fixture Schedule",  "Lighting fixture quantities and specifications"),
                ("ElectricalDeviceSchedule", "Electrical Device Schedule", "Electrical devices, outlets, switches"),
                ("MechEquipSchedule",        "Mechanical Equipment",       "HVAC equipment with capacity data"),
                ("PlumbingFixtureSchedule",  "Plumbing Fixture Schedule",  "Plumbing fixtures and drainage"),
                ("FireDeviceSchedule",       "Fire Device Schedule",       "Fire alarm and suppression devices"),
            };

            foreach (var (tag, label, tip) in mepOps)
            {
                var rb = new RadioButton
                {
                    Content = $"{label} — {tip}",
                    FontSize = 12, Foreground = BrFg,
                    Margin = new Thickness(0, 3, 0, 3),
                    GroupName = "CorpMepOp", Tag = tag
                };
                mepGroup.Children.Add(rb);
            }
            stack.Children.Add(mepGroup);

            // ── MEP bulk section ────────────────────────────────────────
            stack.Children.Add(MakeSectionHeader("MEP BULK OPERATIONS"));
            var bulkGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var bulkOps = new[]
            {
                ("MEPScheduleHVAC",  "All HVAC Schedules",      "Create all HVAC-related schedules"),
                ("MEPScheduleElec",  "All Electrical Schedules", "Create all electrical schedules"),
                ("MEPSchedulePlumb", "All Plumbing Schedules",   "Create all plumbing schedules"),
                ("MEPScheduleFire",  "All Fire Schedules",       "Create all fire protection schedules"),
            };

            foreach (var (tag, label, tip) in bulkOps)
            {
                var rb = new RadioButton
                {
                    Content = $"{label} — {tip}",
                    FontSize = 12, Foreground = BrFg,
                    Margin = new Thickness(0, 3, 0, 3),
                    GroupName = "CorpMepOp", Tag = tag
                };
                bulkGroup.Children.Add(rb);
            }
            stack.Children.Add(bulkGroup);

            stack.Tag = "corpmep_tab";
            scroll.Content = stack;
            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Build schedule items from CSV + existing project schedules
        // ════════════════════════════════════════════════════════════════
        private static List<ScheduleItem> BuildScheduleItems(List<string> csvDefs, List<string> existing)
        {
            var map = new Dictionary<string, ScheduleItem>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in csvDefs)
            {
                string key = name.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (!map.ContainsKey(key))
                    map[key] = new ScheduleItem { Name = key };
                map[key].ExistsInCsv = true;
            }

            foreach (string name in existing)
            {
                string key = name.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (!map.ContainsKey(key))
                    map[key] = new ScheduleItem { Name = key };
                map[key].ExistsInProject = true;
            }

            return map.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Build reusable schedule list panel with search + checkboxes
        // ════════════════════════════════════════════════════════════════
        private static UIElement BuildScheduleListPanel(List<ScheduleItem> items)
        {
            var container = new StackPanel();

            // Search box
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var searchBox = new TextBox
            {
                FontSize = 11, Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                Background = BrWhite, Tag = "search"
            };

            // Watermark
            var watermark = new TextBlock
            {
                Text = "Search schedules...", FontSize = 11,
                Foreground = BrFgDim, IsHitTestVisible = false,
                Margin = new Thickness(6, 4, 0, 0)
            };
            var searchGrid = new Grid();
            searchGrid.Children.Add(searchBox);
            searchGrid.Children.Add(watermark);
            searchBox.TextChanged += (_, __) =>
            {
                watermark.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
            searchRow.Children.Add(searchGrid);
            container.Children.Add(searchRow);

            // Select All / Clear All
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var lnkSelectAll = CreateLinkButton("Select All");
            var lnkClearAll = CreateLinkButton("Clear All");
            lnkClearAll.Margin = new Thickness(12, 0, 0, 0);
            btnRow.Children.Add(lnkSelectAll);
            btnRow.Children.Add(lnkClearAll);
            container.Children.Add(btnRow);

            // ListBox with checkboxes
            var listBox = new ListBox
            {
                MaxHeight = 180,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Background = BrWhite,
                VirtualizingPanel.IsVirtualizing = true,
                VirtualizingPanel.VirtualizationMode = VirtualizationMode.Recycling
            };

            foreach (var item in items)
            {
                var chk = new CheckBox
                {
                    IsChecked = item.IsSelected,
                    Margin = new Thickness(2),
                    Tag = item,
                    Content = FormatScheduleItem(item)
                };
                chk.Checked += (_, __) => item.IsSelected = true;
                chk.Unchecked += (_, __) => item.IsSelected = false;
                listBox.Items.Add(chk);
            }

            // Search filtering
            searchBox.TextChanged += (_, __) =>
            {
                string filter = searchBox.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var obj in listBox.Items)
                {
                    if (obj is CheckBox cb && cb.Tag is ScheduleItem si)
                    {
                        cb.Visibility = string.IsNullOrEmpty(filter) || si.Name.ToLowerInvariant().Contains(filter)
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }
                }
            };

            // Select All / Clear All wiring
            lnkSelectAll.MouseLeftButtonDown += (_, __) =>
            {
                foreach (var obj in listBox.Items)
                {
                    if (obj is CheckBox cb && cb.Visibility == Visibility.Visible)
                        cb.IsChecked = true;
                }
            };
            lnkClearAll.MouseLeftButtonDown += (_, __) =>
            {
                foreach (var obj in listBox.Items)
                {
                    if (obj is CheckBox cb && cb.Visibility == Visibility.Visible)
                        cb.IsChecked = false;
                }
            };

            container.Children.Add(listBox);

            var countText = new TextBlock
            {
                Text = $"{items.Count} schedules available",
                FontSize = 10, Foreground = BrFgDim,
                Margin = new Thickness(0, 4, 0, 0)
            };
            container.Children.Add(countText);

            return container;
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Format a schedule item with coloured status tag
        // ════════════════════════════════════════════════════════════════
        private static UIElement FormatScheduleItem(ScheduleItem item)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = item.Name, FontSize = 11, Foreground = BrFg,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (item.ExistsInCsv && item.ExistsInProject)
                sp.Children.Add(MakeTag("CSV + Project", BrGreen));
            else if (item.ExistsInProject)
                sp.Children.Add(MakeTag("Project", BrBlue));
            else if (item.ExistsInCsv)
                sp.Children.Add(MakeTag("CSV", BrAccent));

            return sp;
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Coloured tag badge
        // ════════════════════════════════════════════════════════════════
        private static TextBlock MakeTag(string text, SolidColorBrush color)
        {
            return new TextBlock
            {
                Text = text, FontSize = 9,
                Foreground = BrWhite,
                Background = color,
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Section header
        // ════════════════════════════════════════════════════════════════
        private static TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDim,
                Margin = new Thickness(0, 10, 0, 6),
                Padding = new Thickness(4, 3, 4, 3),
                Background = BrSectionHeader
            };
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Styled checkbox
        // ════════════════════════════════════════════════════════════════
        private static CheckBox MakeCheckBox(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                FontSize = 11,
                Foreground = BrFg,
                Margin = new Thickness(0, 2, 12, 2)
            };
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Clickable link-style TextBlock
        // ════════════════════════════════════════════════════════════════
        private static TextBlock CreateLinkButton(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = BrBlue,
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline
            };
            tb.MouseEnter += (_, __) => tb.Foreground = BrAccent;
            tb.MouseLeave += (_, __) => tb.Foreground = BrBlue;
            return tb;
        }

        // ════════════════════════════════════════════════════════════════
        // COLLECTION: Get selected operation from active tab
        // ════════════════════════════════════════════════════════════════
        private static string CollectOperation(string activeTab, Dictionary<string, UIElement> panels)
        {
            if (!panels.TryGetValue(activeTab, out var panel)) return string.Empty;

            // For format/corporate tabs, find the checked RadioButton directly
            var stack = FindStack(panel);
            if (stack == null) return string.Empty;

            // Walk all RadioButtons in the stack and find the checked one
            var checkedRb = FindCheckedRadioButton(stack);
            return checkedRb?.Tag as string ?? string.Empty;
        }

        // ════════════════════════════════════════════════════════════════
        // COLLECTION: Get all options from active tab controls
        // ════════════════════════════════════════════════════════════════
        private static Dictionary<string, string> CollectAllOptions(string activeTab, Dictionary<string, UIElement> panels)
        {
            var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!panels.TryGetValue(activeTab, out var panel)) return opts;
            var stack = FindStack(panel);
            if (stack == null) return opts;

            switch (activeTab)
            {
                case "create":
                    if (stack.Tag is CreateTabState cs)
                    {
                        opts["DiscFilter_M"]  = (cs.ChkM?.IsChecked  == true) ? "1" : "0";
                        opts["DiscFilter_E"]  = (cs.ChkE?.IsChecked  == true) ? "1" : "0";
                        opts["DiscFilter_P"]  = (cs.ChkP?.IsChecked  == true) ? "1" : "0";
                        opts["DiscFilter_A"]  = (cs.ChkA?.IsChecked  == true) ? "1" : "0";
                        opts["DiscFilter_S"]  = (cs.ChkS?.IsChecked  == true) ? "1" : "0";
                        opts["DiscFilter_FP"] = (cs.ChkFP?.IsChecked == true) ? "1" : "0";
                        opts["Overwrite"]     = (cs.ChkOverwrite?.IsChecked == true) ? "1" : "0";
                        opts["Formulas"]      = (cs.ChkFormulas?.IsChecked  == true) ? "1" : "0";
                    }
                    break;

                case "export":
                    if (stack.Tag is ExportTabState es)
                    {
                        opts["OutputPath"] = es.TxtPath?.Text ?? "";
                        opts["Format"]     = es.CmbFormat?.SelectedItem?.ToString() ?? "CSV";
                    }
                    break;

                // audit, manage, format, corporate — operation tag is sufficient, no extra options
            }

            return opts;
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Find the StackPanel inside a ScrollViewer
        // ════════════════════════════════════════════════════════════════
        private static StackPanel FindStack(UIElement element)
        {
            if (element is StackPanel sp) return sp;
            if (element is ScrollViewer sv && sv.Content is StackPanel ssp) return ssp;
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        // HELPER: Recursively find the checked RadioButton in a panel
        // ════════════════════════════════════════════════════════════════
        private static RadioButton FindCheckedRadioButton(Panel parent)
        {
            foreach (var child in parent.Children)
            {
                if (child is RadioButton rb && rb.IsChecked == true) return rb;
                if (child is Panel p)
                {
                    var found = FindCheckedRadioButton(p);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
