using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Select;
using Color = System.Windows.Media.Color;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using Visibility = System.Windows.Visibility;

namespace StingTools.UI
{
    // ══════════════════════════════════════════════════════════════════════
    //  RESULT MODEL
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Result from DocumentManagementDialog.</summary>
    public class DocumentManagementResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public string Tab { get; set; }
        public Dictionary<string, object> Options { get; set; } = new();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DOCUMENT ITEM VIEW MODEL (Enhanced with aging, element count, links)
    // ══════════════════════════════════════════════════════════════════════

    public class DocItemVM : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string TypeDesc { get; set; }
        public string Status { get; set; }
        public string StatusDesc { get; set; }
        public string CDE { get; set; }
        public string Revision { get; set; }
        public string Date { get; set; }
        public string Discipline { get; set; }
        public string Folder { get; set; }
        public string FolderId { get; set; }
        public string FilePath { get; set; }
        public string FileFormat { get; set; }
        public string Size { get; set; }
        public string Direction { get; set; }
        public string Priority { get; set; }
        public string AssignedTo { get; set; }
        public string Category { get; set; }
        public string Icon { get; set; }

        // ── Enhanced fields (GAP fixes) ──
        public int DaysOpen { get; set; }              // GAP GRID-02: issue aging
        public string Aging { get; set; }              // "3d", "2w", "1m" display text
        public int ElementCount { get; set; }          // GAP GRID-01: affected elements
        public string LinkedRevision { get; set; }     // GAP CROSS-01: linked revision ID
        public string LinkedIssues { get; set; }       // GAP CROSS-01: linked issue IDs
        public string SLADeadline { get; set; }        // GAP GRID-02: SLA target date
        public bool IsOverdue { get; set; }            // GAP GRID-02: past SLA
        public string StatusHistory { get; set; }      // GAP PERSIST-02: status change log
        public string Suitability { get; set; }        // S0-S7 code
        public string CreatedBy { get; set; }          // GAP PERSIST-01: audit trail

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class NavNode
    {
        public string Label { get; set; }
        public string Tag { get; set; }
        public string Icon { get; set; }
        public int Count { get; set; }
        public List<NavNode> Children { get; set; } = new();
        public override string ToString() => Count > 0 ? $"{Icon} {Label} ({Count})" : $"{Icon} {Label}";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DOCUMENT MANAGEMENT DIALOG — V2 (47 gap fixes)
    // ══════════════════════════════════════════════════════════════════════

    internal static class DocumentManagementDialog
    {
        // ── Theme ─────────────────────────────────────────────────────
        private static readonly SolidColorBrush BrHeader  = new(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly SolidColorBrush BrAccent  = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrBg      = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush BrWhite   = Brushes.White;
        private static readonly SolidColorBrush BrFgDark  = new(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush BrFgSub   = new(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush BrBorder  = new(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidColorBrush BrGreen   = new(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush BrOrange  = new(Color.FromRgb(0xE6, 0x51, 0x00));
        private static readonly SolidColorBrush BrRed     = new(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly SolidColorBrush BrPurple  = new(Color.FromRgb(0x6A, 0x1B, 0x9A));
        private static readonly SolidColorBrush BrTeal    = new(Color.FromRgb(0x00, 0x69, 0x5C));
        private static readonly SolidColorBrush BrAmber   = new(Color.FromRgb(0xFF, 0x8F, 0x00));

        // ── State ─────────────────────────────────────────────────────
        private static ObservableCollection<DocItemVM> _allItems;
        private static ListCollectionView _view;
        private static string _currentFilter = "ALL";
        private static string _searchText = "";
        private static TextBlock _statusText;
        private static TextBlock _countText;
        private static ListView _listView;
        private static TreeView _treeView;
        private static StackPanel _dashPanel;
        private static Document _doc;
        private static string _selectedOperation;
        private static ComplianceScan.ComplianceResult _complianceResult;
        private static System.Windows.Controls.TextBox _searchBox;

        // ══════════════════════════════════════════════════════════════════
        //  SHOW
        // ══════════════════════════════════════════════════════════════════

        public static DocumentManagementResult Show(Document doc)
        {
            _doc = doc;
            _selectedOperation = null;
            _allItems = new ObservableCollection<DocItemVM>();
            var result = new DocumentManagementResult();

            // Pre-load compliance scan
            try { _complianceResult = ComplianceScan.Scan(doc); }
            catch (Exception ex) { StingLog.Warn($"DocMgr compliance scan: {ex.Message}"); }

            LoadAllData(doc);
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_allItems);
            _view.Filter = FilterItem;

            var win = new Window
            {
                Title = "STING Document Management Center",
                Width = 1280, Height = 850,
                MinWidth = 960, MinHeight = 650,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Background = BrBg
            };
            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // Header
            var header = BuildHeader(doc);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Dashboard strip (GAP DASH-01: embedded health summary)
            var dash = BuildDashboardStrip(doc);
            DockPanel.SetDock(dash, Dock.Top);
            root.Children.Add(dash);

            // Footer
            var footer = BuildFooter(win, result);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Action bar
            var actionBar = BuildActionBar(doc, win);
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            // Main: Tree left + List right
            var splitter = new System.Windows.Controls.Grid();
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = BuildTreePanel(doc);
            System.Windows.Controls.Grid.SetColumn(leftPanel, 0);
            splitter.Children.Add(leftPanel);

            var gridSplitter = new GridSplitter
            {
                Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder
            };
            System.Windows.Controls.Grid.SetColumn(gridSplitter, 1);
            splitter.Children.Add(gridSplitter);

            var rightPanel = BuildDocumentPanel();
            System.Windows.Controls.Grid.SetColumn(rightPanel, 2);
            splitter.Children.Add(rightPanel);

            root.Children.Add(splitter);

            // Wire context menu to ListView (needs doc and win references)
            if (_listView != null)
                _listView.ContextMenu = BuildContextMenu(doc, win);

            win.Content = root;

            // ── Keyboard shortcuts ──
            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.F5) { RefreshData(); e.Handled = true; }
                else if (e.Key == Key.Delete && _listView?.SelectedItem is DocItemVM) { DeleteSelected(); e.Handled = true; }
                else if (e.Key == Key.F2 && _listView?.SelectedItem is DocItemVM) { RenameSelected(); e.Handled = true; }
                else if (e.Key == Key.Escape) { win.DialogResult = false; win.Close(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (e.Key == Key.E) { ExportVisibleToCSV(); e.Handled = true; }
                    else if (e.Key == Key.L) { ShowCodeLegend(); e.Handled = true; }
                    else if (e.Key == Key.F) { _searchBox?.Focus(); _searchBox?.SelectAll(); e.Handled = true; }
                }
            };

            bool? dialogResult = win.ShowDialog();
            if (dialogResult == true && _selectedOperation != null)
            {
                result.Confirmed = true;
                result.Operation = _selectedOperation;
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DASHBOARD STRIP (GAP DASH-01: Project health at a glance)
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildDashboardStrip(Document doc)
        {
            var strip = new Border
            {
                Background = BrWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6, 12, 6)
            };

            _dashPanel = new StackPanel { Orientation = Orientation.Horizontal };
            RefreshDashboard(doc);
            strip.Child = _dashPanel;
            return strip;
        }

        private static void RefreshDashboard(Document doc)
        {
            if (_dashPanel == null) return;
            _dashPanel.Children.Clear();

            // RAG status
            var cr = _complianceResult;
            if (cr != null)
            {
                SolidColorBrush ragBrush = cr.RAGStatus == "GREEN" ? BrGreen :
                    cr.RAGStatus == "AMBER" ? BrAmber : BrRed;
                _dashPanel.Children.Add(MakeDashCard($"{cr.CompliancePercent:F0}%",
                    "Tag Compliance", ragBrush));
                _dashPanel.Children.Add(MakeDashCard($"{cr.StrictPercent:F0}%",
                    "Strict (all tokens)", cr.StrictPercent >= 80 ? BrGreen : cr.StrictPercent >= 50 ? BrAmber : BrRed));

                // Per-token empty counts (GAP DM-01)
                if (cr.EmptyTokenCounts != null && cr.EmptyTokenCounts.Count > 0)
                {
                    int totalEmpty = cr.EmptyTokenCounts.Values.Sum();
                    string worstToken = cr.EmptyTokenCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "?";
                    int worstCount = cr.EmptyTokenCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Value;
                    _dashPanel.Children.Add(MakeDashCard($"{totalEmpty}",
                        $"Empty tokens (worst: {worstToken}={worstCount})", totalEmpty == 0 ? BrGreen : BrOrange));
                }

                if (cr.StaleCount > 0)
                    _dashPanel.Children.Add(MakeDashCard($"{cr.StaleCount}", "Stale elements", BrRed));

                // Data completeness (tags + STATUS + containers weighted)
                _dashPanel.Children.Add(MakeDashCard($"{cr.DataCompletenessPercent:F0}%",
                    "Data completeness", cr.DataCompletenessPercent >= 80 ? BrGreen : cr.DataCompletenessPercent >= 50 ? BrAmber : BrRed));

                // Status distribution summary
                if (cr.StatusDistribution != null && cr.StatusDistribution.Count > 0)
                {
                    int missing = cr.StatusDistribution.GetValueOrDefault("", 0) + cr.StatusDistribution.GetValueOrDefault("NONE", 0);
                    if (missing > 0)
                        _dashPanel.Children.Add(MakeDashCard($"{missing}", "Missing STATUS", BrOrange));
                }

                // Empty containers
                if (cr.EmptyContainerCounts != null && cr.EmptyContainerCounts.Count > 0)
                {
                    int emptyContainers = cr.EmptyContainerCounts.Values.Sum();
                    if (emptyContainers > 0)
                    {
                        string worstContainer = cr.EmptyContainerCounts.OrderByDescending(kv => kv.Value).First().Key;
                        _dashPanel.Children.Add(MakeDashCard($"{emptyContainers}",
                            $"Empty containers (worst: {worstContainer})", BrOrange));
                    }
                }
            }

            // Issue counts
            int openIssues = _allItems.Count(i => i.Category == "ISSUE" && i.Status == "OPEN");
            int criticalIssues = _allItems.Count(i => i.Category == "ISSUE" && i.Priority == "CRITICAL");
            int overdueIssues = _allItems.Count(i => i.Category == "ISSUE" && i.IsOverdue);
            _dashPanel.Children.Add(MakeDashCard($"{openIssues}",
                "Open issues", openIssues == 0 ? BrGreen : BrOrange, "CAT:ISSUE"));
            if (criticalIssues > 0)
                _dashPanel.Children.Add(MakeDashCard($"{criticalIssues}", "CRITICAL", BrRed, "PRIORITY:CRITICAL"));
            if (overdueIssues > 0)
                _dashPanel.Children.Add(MakeDashCard($"{overdueIssues}", "Overdue", BrRed, "OVERDUE"));

            // Revision count
            int revCount = _allItems.Count(i => i.Category == "REVISION");
            int issuedRevs = _allItems.Count(i => i.Category == "REVISION" && i.Status == "ISSUED");
            _dashPanel.Children.Add(MakeDashCard($"{issuedRevs}/{revCount}",
                "Revisions issued", BrPurple, "CAT:REVISION"));

            // Clash count
            int clashCount = _allItems.Count(i => i.Category == "CLASH");
            if (clashCount > 0)
                _dashPanel.Children.Add(MakeDashCard($"{clashCount}", "Clashes", BrRed, "CAT:CLASH"));

            // Document totals
            int totalDocs = _allItems.Count(i => i.Category == "DOCUMENT");
            _dashPanel.Children.Add(MakeDashCard($"{totalDocs}", "Documents", BrTeal));

            // Data drop readiness (milestone tracking)
            try
            {
                var drops = ProjectFolderEngine.CheckAllDataDrops(doc);
                foreach (var dd in drops)
                {
                    SolidColorBrush ddBrush = dd.ReadyPercent >= 100 ? BrGreen :
                        dd.ReadyPercent >= 50 ? BrAmber : BrRed;
                    _dashPanel.Children.Add(MakeDashCard($"{dd.ReadyPercent:F0}%",
                        $"{dd.DataDropId}: {dd.ReadyCount}/{dd.TotalCount}", ddBrush, $"DD:{dd.DataDropId}"));
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr dashboard drops: {ex.Message}"); }
        }

        private static Border MakeDashCard(string value, string label, SolidColorBrush color)
        {
            var card = new Border
            {
                BorderBrush = color,
                BorderThickness = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Background = BrWhite
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = color, HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 9, Foreground = BrFgSub,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            card.Child = stack;
            return card;
        }

        /// <summary>Clickable dashboard card — clicking sets filter and refreshes grid.</summary>
        private static Border MakeDashCard(string value, string label, SolidColorBrush color, string filterOnClick)
        {
            var card = MakeDashCard(value, label, color);
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonDown += (s, e) =>
            {
                _currentFilter = filterOnClick;
                _view?.Refresh();
                UpdateCounts();
            };
            card.ToolTip = $"Click to filter: {filterOnClick}";
            return card;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HEADER
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildHeader(Document doc)
        {
            var header = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 8, 16, 8)
            };
            var g = new System.Windows.Controls.Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = "DOCUMENT MANAGEMENT CENTER",
                FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White
            });
            string projName = "";
            try { projName = doc?.ProjectInformation?.Name ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            left.Children.Add(new TextBlock
            {
                Text = $"Project: {projName}  |  ISO 19650",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            System.Windows.Controls.Grid.SetColumn(left, 0);
            g.Children.Add(left);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
            rightBtns.Children.Add(MakeHeaderBtn("Create Folders", "CreateFolders"));
            rightBtns.Children.Add(MakeHeaderBtn("Import File", "ImportFile"));
            rightBtns.Children.Add(MakeHeaderBtn("Set Output Dir", "SetOutputDirectory"));
            rightBtns.Children.Add(MakeHeaderBtn("Refresh", "Refresh"));
            rightBtns.Children.Add(MakeHeaderBtn("Watch Folder", "StartWatch"));
            System.Windows.Controls.Grid.SetColumn(rightBtns, 1);
            g.Children.Add(rightBtns);

            header.Child = g;
            return header;
        }

        private static Button MakeHeaderBtn(string label, string tag)
        {
            var btn = new Button
            {
                Content = label, Tag = tag,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrAccent, Foreground = Brushes.White,
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            btn.Click += HeaderBtn_Click;
            return btn;
        }

        private static void HeaderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString();
            switch (tag)
            {
                case "CreateFolders":
                    int created = ProjectFolderEngine.CreateFolderStructure(_doc);
                    MessageBox.Show($"Created {created} folders at:\n{ProjectFolderEngine.GetRootPath(_doc)}",
                        "STING Folder Structure", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshData();
                    break;
                case "ImportFile":
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Import file into STING project",
                        Filter = "All files|*.*|PDF|*.pdf|Excel|*.xlsx;*.csv|Images|*.png;*.jpg|BCF|*.bcfzip;*.bcf",
                        Multiselect = true
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        foreach (string file in dlg.FileNames)
                        {
                            string ext = Path.GetExtension(file).ToUpperInvariant().TrimStart('.');
                            string targetFolder = "BRIEFCASE";
                            if (ProjectFolderEngine.ExportTypeToFolder.TryGetValue(ext, out string fid))
                                targetFolder = fid;
                            ProjectFolderEngine.ImportFile(_doc, file, targetFolder);
                        }
                        RefreshData();
                    }
                    break;
                case "SetOutputDirectory":
                    OutputLocationHelper.PromptSetPreferredDirectory();
                    break;
                case "Refresh":
                    RefreshData();
                    break;
                case "StartWatch":
                    ProjectFolderEngine.StartWatching(_doc, changedFile =>
                    {
                        // Auto-refresh on external file changes (dispatched to UI thread)
                        try
                        {
                            _listView?.Dispatcher?.BeginInvoke(new Action(() => RefreshData()));
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    });
                    MessageBox.Show($"Now monitoring: {ProjectFolderEngine.GetRootPath(_doc)}\n\n" +
                        "External file changes will auto-refresh the document list.",
                        "STING File Watcher", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  TREE NAVIGATOR (Enhanced: GAP NAV-01/02/03/04)
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildTreePanel(Document doc)
        {
            var border = new Border
            {
                Background = BrWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };

            var stack = new DockPanel { LastChildFill = true };

            // Workflow buttons at top of tree (GAP WF-01)
            var wfPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var wfStack = new StackPanel();
            wfStack.Children.Add(new TextBlock
            {
                Text = "QUICK WORKFLOWS", FontSize = 9,
                FontWeight = FontWeights.Bold, Foreground = BrGreen,
                Margin = new Thickness(0, 0, 0, 3)
            });
            var wfWrap = new WrapPanel();
            wfWrap.Children.Add(MakeWfBtn("Daily QA", "WorkflowPreset:DailyQA", BrGreen));
            wfWrap.Children.Add(MakeWfBtn("Doc Package", "WorkflowPreset:DocumentPackage", BrTeal));
            wfWrap.Children.Add(MakeWfBtn("Fix Compliance", "ResolveAllIssues", BrOrange));
            wfWrap.Children.Add(MakeWfBtn("Full Setup", "WorkflowPreset:ProjectKickoff", BrPurple));
            wfStack.Children.Add(wfWrap);
            wfPanel.Child = wfStack;
            DockPanel.SetDock(wfPanel, Dock.Top);
            stack.Children.Add(wfPanel);

            // Tree header
            var treeHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                Padding = new Thickness(10, 5, 10, 5)
            };
            treeHeader.Child = new TextBlock
            {
                Text = "NAVIGATOR", FontSize = 11,
                FontWeight = FontWeights.Bold, Foreground = BrFgDark
            };
            DockPanel.SetDock(treeHeader, Dock.Top);
            stack.Children.Add(treeHeader);

            // UI-03: Tree search box
            var treeSearchBox = new System.Windows.Controls.TextBox
            {
                FontSize = 10, Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(6, 3, 6, 3),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1)
            };
            // Placeholder text via GotFocus/LostFocus
            treeSearchBox.Text = "Search tree...";
            treeSearchBox.Foreground = BrFgSub;
            treeSearchBox.GotFocus += (s, e) =>
            {
                if (treeSearchBox.Text == "Search tree...")
                { treeSearchBox.Text = ""; treeSearchBox.Foreground = BrFgDark; }
            };
            treeSearchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(treeSearchBox.Text))
                { treeSearchBox.Text = "Search tree..."; treeSearchBox.Foreground = BrFgSub; }
            };
            treeSearchBox.TextChanged += (s, e) =>
            {
                string search = treeSearchBox.Text;
                if (search == "Search tree..." || string.IsNullOrEmpty(search))
                {
                    SetAllTreeItemsVisible(_treeView, true);
                    return;
                }
                FilterTreeItems(_treeView, search);
            };
            DockPanel.SetDock(treeSearchBox, Dock.Top);
            stack.Children.Add(treeSearchBox);

            _treeView = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = BrWhite, Padding = new Thickness(4)
            };
            PopulateTree();
            _treeView.SelectedItemChanged += (s, e) =>
            {
                if (_treeView.SelectedItem is TreeViewItem item && item.Tag is string filter)
                {
                    _currentFilter = filter;
                    _view?.Refresh();
                    UpdateCounts();
                }
            };
            stack.Children.Add(_treeView);
            border.Child = stack;
            return border;
        }

        private static Button MakeWfBtn(string label, string tag, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = label, Tag = tag,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2), FontSize = 9,
                Background = BrWhite, Foreground = fg,
                BorderBrush = fg, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.Click += (s, e) =>
            {
                string t = (s as Button)?.Tag?.ToString() ?? "";
                if (t.StartsWith("WorkflowPreset:"))
                {
                    // Close dialog and dispatch workflow
                    _selectedOperation = t.Replace("WorkflowPreset:", "WorkflowPreset_");
                    var w = Window.GetWindow(s as DependencyObject);
                    if (w != null) { w.DialogResult = true; w.Close(); }
                }
                else
                {
                    _selectedOperation = t;
                    var w = Window.GetWindow(s as DependencyObject);
                    if (w != null) { w.DialogResult = true; w.Close(); }
                }
            };
            return btn;
        }

        private static void PopulateTree()
        {
            _treeView.Items.Clear();

            // ── ALL ──
            var allNode = MakeTreeItem("ALL DOCUMENTS", "ALL", true);
            _treeView.Items.Add(allNode);

            // ── BY TIME (GAP NAV-02) ──
            var timeNode = MakeTreeItem("BY DATE", "TIME_ROOT", false);
            int today = _allItems.Count(i => IsToday(i.Date));
            int thisWeek = _allItems.Count(i => IsThisWeek(i.Date));
            int thisMonth = _allItems.Count(i => IsThisMonth(i.Date));
            timeNode.Items.Add(MakeTreeItem($"Today ({today})", "TIME:TODAY", false));
            timeNode.Items.Add(MakeTreeItem($"This Week ({thisWeek})", "TIME:WEEK", false));
            timeNode.Items.Add(MakeTreeItem($"This Month ({thisMonth})", "TIME:MONTH", false));
            timeNode.Items.Add(MakeTreeItem("Older", "TIME:OLDER", false));
            _treeView.Items.Add(timeNode);

            // ── BY DISCIPLINE (GAP NAV-01) ──
            var discNode = MakeTreeItem("BY DISCIPLINE", "DISC_ROOT", false);
            foreach (string disc in new[] { "M", "E", "P", "A", "S", "FP", "LV", "G", "Z" })
            {
                int count = _allItems.Count(i => (i.Discipline ?? "").Equals(disc, StringComparison.OrdinalIgnoreCase));
                if (count > 0)
                    discNode.Items.Add(MakeTreeItem($"{disc} ({count})", $"DISC:{disc}", false));
            }
            if (discNode.Items.Count > 0) _treeView.Items.Add(discNode);

            // ── FOLDERS ──
            var foldersNode = MakeTreeItem("FOLDERS", "FOLDER_ROOT", false);
            var stats = ProjectFolderEngine.GetFolderStats(_doc);
            foreach (var fs in stats.Where(s => s.FileCount > 0 || s.Exists))
            {
                string label = $"{fs.FolderName.Substring(3)} ({fs.FileCount}) [{fs.TotalSizeDisplay}]";
                foldersNode.Items.Add(MakeTreeItem(label, $"FOLDER:{fs.FolderId}", false));
            }
            if (foldersNode.Items.Count == 0)
                foldersNode.Items.Add(MakeTreeItem("(click Create Folders)", "", false));
            _treeView.Items.Add(foldersNode);

            // ── CDE STATUS ──
            var cdeNode = MakeTreeItem("CDE STATUS", "CDE_ROOT", false);
            foreach (var (code, label) in new[] { ("WIP", "Work In Progress"), ("SHARED", "Shared"),
                ("PUBLISHED", "Published"), ("ARCHIVE", "Archive") })
            {
                int count = _allItems.Count(i => i.CDE == code);
                cdeNode.Items.Add(MakeTreeItem($"{code} ({count})", $"CDE:{code}", false));
            }
            _treeView.Items.Add(cdeNode);

            // ── DOCUMENT STATUS (IFI, AFD, IFR, IFC, IFD etc.) ──
            var statusNode = MakeTreeItem("DOC STATUS CODES", "STATUS_ROOT", false);
            var usedStatuses = _allItems.Where(i => !string.IsNullOrEmpty(i.Status) && i.Category == "DOCUMENT")
                .GroupBy(i => i.Status).OrderByDescending(g => g.Count());
            foreach (var g in usedStatuses)
            {
                string desc = BIMManager.DocStatusCodes.All.TryGetValue(g.Key, out string d) ? d : g.Key;
                statusNode.Items.Add(MakeTreeItem($"{g.Key} — {desc} ({g.Count()})", $"STATUS:{g.Key}", false));
            }
            _treeView.Items.Add(statusNode);

            // ── ISSUES (GAP NAV-03: with priority breakdown) ──
            var issuesNode = MakeTreeItem("ISSUES & RFIs", "CAT:ISSUE", false);
            // By priority first
            var priNode = MakeTreeItem("By Priority", "ISSUE_PRI", false);
            foreach (string pri in new[] { "CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO" })
            {
                int count = _allItems.Count(i => i.Category == "ISSUE" && i.Priority == pri);
                if (count > 0)
                    priNode.Items.Add(MakeTreeItem($"{pri} ({count})", $"PRIORITY:{pri}", false));
            }
            issuesNode.Items.Add(priNode);
            // By type
            var typeNode = MakeTreeItem("By Type", "ISSUE_TYPE", false);
            foreach (var kv in BIMManager.BIMManagerEngine.IssueTypes)
            {
                int count = _allItems.Count(i => i.Category == "ISSUE" && i.Type == kv.Key);
                if (count > 0)
                    typeNode.Items.Add(MakeTreeItem($"{kv.Key} ({count})", $"ISSUE:{kv.Key}", false));
            }
            issuesNode.Items.Add(typeNode);
            // By status
            var issStatNode = MakeTreeItem("By Status", "ISSUE_STAT", false);
            foreach (string st in new[] { "OPEN", "IN_PROGRESS", "RESPONDED", "ACCEPTED", "REJECTED", "CLOSED", "VOID" })
            {
                int count = _allItems.Count(i => i.Category == "ISSUE" && i.Status == st);
                if (count > 0)
                    issStatNode.Items.Add(MakeTreeItem($"{st} ({count})", $"ISSUESTATUS:{st}", false));
            }
            issuesNode.Items.Add(issStatNode);
            // Overdue (GAP NAV-02/03)
            int overdue = _allItems.Count(i => i.Category == "ISSUE" && i.IsOverdue);
            if (overdue > 0)
                issuesNode.Items.Add(MakeTreeItem($"OVERDUE ({overdue})", "OVERDUE", false));
            _treeView.Items.Add(issuesNode);

            // ── REVISIONS ──
            var revNode = MakeTreeItem("REVISIONS", "CAT:REVISION", false);
            var revGroups = _allItems.Where(i => i.Category == "REVISION").GroupBy(i => i.Revision ?? "?");
            foreach (var g in revGroups.OrderBy(x => x.Key))
                revNode.Items.Add(MakeTreeItem($"Rev {g.Key} ({g.Count()})", $"REV:{g.Key}", false));
            _treeView.Items.Add(revNode);

            // ── CLASHES ──
            int clashCount = _allItems.Count(i => i.Category == "CLASH");
            var clashNode = MakeTreeItem($"CLASHES ({clashCount})", "CAT:CLASH", false);
            clashNode.Items.Add(MakeTreeItem("BCF Files", "FOLDER:CLASHES", false));
            _treeView.Items.Add(clashNode);

            // ── COBie & HANDOVER ──
            var handoverNode = MakeTreeItem("COBie & HANDOVER", "CAT:HANDOVER", false);
            handoverNode.Items.Add(MakeTreeItem("COBie Exports", "FOLDER:COBIE", false));
            handoverNode.Items.Add(MakeTreeItem("FM Handover", "FOLDER:HANDOVER", false));
            handoverNode.Items.Add(MakeTreeItem("Registers", "FOLDER:REGISTERS", false));
            _treeView.Items.Add(handoverNode);

            // ── TRANSMITTALS ──
            var transNode = MakeTreeItem("TRANSMITTALS", "CAT:TRANSMITTAL", false);
            _treeView.Items.Add(transNode);

            // ── COMPLIANCE (GAP NAV-04) ──
            var compNode = MakeTreeItem("COMPLIANCE", "CAT:COMPLIANCE", false);
            if (_complianceResult != null)
            {
                compNode.Items.Add(MakeTreeItem(
                    $"Overall: {_complianceResult.RAGStatus} {_complianceResult.CompliancePercent:F0}%",
                    "CAT:COMPLIANCE", false));
                if (_complianceResult.ByDisc != null)
                {
                    foreach (var kv in _complianceResult.ByDisc.OrderBy(x => x.Key))
                    {
                        string rag = kv.Value.CompliancePct >= 80 ? "G" : kv.Value.CompliancePct >= 50 ? "A" : "R";
                        compNode.Items.Add(MakeTreeItem(
                            $"{kv.Key}: {kv.Value.CompliancePct:F0}% [{rag}]",
                            $"DISC:{kv.Key}", false));
                    }
                }
            }
            _treeView.Items.Add(compNode);

            // ── BEP ──
            _treeView.Items.Add(MakeTreeItem("BEP", "FOLDER:BEP", false));

            // ── STICKY NOTES (DM-02: with category breakdown) ──
            int stickyCount = _allItems.Count(i => i.Category == "STICKY");
            if (stickyCount > 0)
            {
                var stickyNode = MakeTreeItem($"STICKY NOTES ({stickyCount})", "CAT:STICKY", false);
                var stickyCats = _allItems.Where(i => i.Category == "STICKY")
                    .GroupBy(i => i.Status ?? "GENERAL");
                foreach (var sg in stickyCats.OrderByDescending(g => g.Count()))
                    stickyNode.Items.Add(MakeTreeItem($"{sg.Key} ({sg.Count()})", $"STICKYCAT:{sg.Key}", false));
                _treeView.Items.Add(stickyNode);
            }

            // ── DATA DROPS (milestone tracking) ──
            var ddNode = MakeTreeItem("DATA DROPS", "DD_ROOT", false);
            try
            {
                var drops = ProjectFolderEngine.CheckAllDataDrops(_doc);
                foreach (var dd in drops)
                {
                    string ddStatus = dd.ReadyPercent >= 100 ? "READY" : dd.ReadyPercent >= 50 ? "PARTIAL" : "NOT READY";
                    ddNode.Items.Add(MakeTreeItem(
                        $"{dd.DataDropId}: {dd.Stage} [{ddStatus} {dd.ReadyPercent:F0}%]",
                        $"DD:{dd.DataDropId}", false));
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr tree drops: {ex.Message}"); }
            _treeView.Items.Add(ddNode);

            // ── ACTIVITY LOG ──
            int activityCount = _allItems.Count(i => i.Category == "ACTIVITY");
            _treeView.Items.Add(MakeTreeItem($"ACTIVITY LOG ({activityCount})", "CAT:ACTIVITY", false));

            // ── CLASH GROUPS ──
            try
            {
                var clashGroups = ProjectFolderEngine.GroupClashes(_doc);
                if (clashGroups.Count > 0)
                {
                    var cgNode = MakeTreeItem("CLASH GROUPS", "CLASHGROUP_ROOT", false);
                    foreach (var cg in clashGroups)
                    {
                        cgNode.Items.Add(MakeTreeItem(
                            $"{cg.Discipline}: {cg.OpenClashes} open / {cg.TotalClashes} total ({cg.CriticalClashes} critical)",
                            $"CLASHDISC:{cg.Discipline}", false));
                    }
                    _treeView.Items.Add(cgNode);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr tree clash groups: {ex.Message}"); }

            allNode.IsExpanded = true;
            allNode.IsSelected = true;
        }

        private static TreeViewItem MakeTreeItem(string label, string filter, bool expanded)
        {
            return new TreeViewItem
            {
                Header = label, Tag = filter,
                IsExpanded = expanded,
                Padding = new Thickness(2, 2, 2, 2),
                FontSize = 11
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  DOCUMENT LIST (Enhanced: GAP GRID-01/02/03)
        // ══════════════════════════════════════════════════════════════════

        private static DockPanel BuildDocumentPanel()
        {
            var panel = new DockPanel { LastChildFill = true, Background = BrWhite };

            // Search bar
            var searchBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8)),
                Padding = new Thickness(8, 5, 8, 5),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var searchGrid = new System.Windows.Controls.Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            searchGrid.Children.Add(new TextBlock
            {
                Text = "Search: ", VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11, Foreground = BrFgDark, Margin = new Thickness(0, 0, 4, 0)
            });

            _searchBox = new System.Windows.Controls.TextBox
            {
                FontSize = 11, Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                ToolTip = "Search by name, ID, type, status, assignee, priority... (Ctrl+F to focus)"
            };
            _searchBox.TextChanged += (s, e) =>
            {
                _searchText = _searchBox.Text ?? "";
                _view?.Refresh();
                UpdateCounts();
            };
            System.Windows.Controls.Grid.SetColumn(_searchBox, 1);
            searchGrid.Children.Add(_searchBox);

            _countText = new TextBlock
            {
                FontSize = 10, Foreground = BrFgSub,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            System.Windows.Controls.Grid.SetColumn(_countText, 2);
            searchGrid.Children.Add(_countText);
            searchBar.Child = searchGrid;
            DockPanel.SetDock(searchBar, Dock.Top);
            panel.Children.Add(searchBar);

            // Quick filter buttons
            var filterBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                Padding = new Thickness(8, 3, 8, 3),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var filterWrap = new WrapPanel();
            foreach (var (label, filter, brush) in new (string, string, SolidColorBrush)[]
            {
                ("All",       "ALL",           BrFgDark),
                ("Docs",      "CAT:DOCUMENT",  BrAccent),
                ("Issues",    "CAT:ISSUE",     BrOrange),
                ("Revisions", "CAT:REVISION",  BrPurple),
                ("Clashes",   "CAT:CLASH",     BrRed),
                ("Handover",  "CAT:HANDOVER",  BrTeal),
                ("WIP",       "CDE:WIP",       BrFgSub),
                ("Shared",    "CDE:SHARED",    BrGreen),
                ("Published", "CDE:PUBLISHED", BrGreen),
                ("Overdue",   "OVERDUE",       BrRed),
                ("Critical",  "PRIORITY:CRITICAL", BrRed),
            })
            {
                var btn = new Button
                {
                    Content = label, Tag = filter,
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(2), FontSize = 10,
                    Background = Brushes.White, Foreground = brush,
                    BorderBrush = brush, BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                btn.Click += (s, e) =>
                {
                    _currentFilter = filter;
                    _view?.Refresh();
                    UpdateCounts();
                };
                filterWrap.Children.Add(btn);
            }
            filterBar.Child = filterWrap;
            DockPanel.SetDock(filterBar, Dock.Top);
            panel.Children.Add(filterBar);

            // ListView with enhanced columns
            _listView = new ListView
            {
                BorderThickness = new Thickness(0),
                FontSize = 11,
                ItemsSource = _view,
                SelectionMode = SelectionMode.Extended,  // GAP OP-04: multi-select
                AllowDrop = true  // DM-04: enable drag-drop
            };
            // Performance: virtualise list for large datasets (1000+ items)
            VirtualizingStackPanel.SetIsVirtualizing(_listView, true);
            VirtualizingStackPanel.SetVirtualizationMode(_listView, VirtualizationMode.Recycling);
            ScrollViewer.SetIsDeferredScrollingEnabled(_listView, true);
            _listView.AlternationCount = 2; // For alternating row colors

            // DM-04: Drag-drop — drag files from Explorer into the list to import
            _listView.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effects = DragDropEffects.Copy;
                else
                    e.Effects = DragDropEffects.None;
                e.Handled = true;
            };
            _listView.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files == null || files.Length == 0) return;
                    int imported = 0;
                    foreach (string file in files)
                    {
                        string ext = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
                        string targetFolder = "BRIEFCASE";
                        if (ProjectFolderEngine.ExportTypeToFolder.TryGetValue(ext, out string fid))
                            targetFolder = fid;
                        string result = ProjectFolderEngine.ImportFile(_doc, file, targetFolder);
                        if (result != null) imported++;
                        else if (!ProjectFolderEngine.IsAllowedExtension(ext))
                            MessageBox.Show($"Unsupported file type: .{ext}\n{Path.GetFileName(file)}",
                                "STING Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    if (imported > 0)
                    {
                        MessageBox.Show($"Imported {imported} of {files.Length} files.",
                            "STING Import", MessageBoxButton.OK, MessageBoxImage.Information);
                        RefreshData();
                    }
                }
            };

            var gridView = new GridView();
            gridView.Columns.Add(MakeCol("Type", "Type", 42));
            gridView.Columns.Add(MakeCol("ID / Name", "Title", 210));
            gridView.Columns.Add(MakeCol("Status", "Status", 52));
            gridView.Columns.Add(MakeCol("CDE", "CDE", 62));
            gridView.Columns.Add(MakeCol("Rev", "Revision", 36));
            gridView.Columns.Add(MakeCol("Disc", "Discipline", 32));
            gridView.Columns.Add(MakeCol("Folder", "Folder", 90));
            gridView.Columns.Add(MakeCol("Fmt", "FileFormat", 35));
            gridView.Columns.Add(MakeCol("Size", "Size", 50));
            gridView.Columns.Add(MakeCol("Date", "Date", 78));
            gridView.Columns.Add(MakeCol("Priority", "Priority", 52));
            gridView.Columns.Add(MakeCol("Age", "Aging", 36));       // GAP GRID-02
            gridView.Columns.Add(MakeCol("Elements", "ElementCount", 48));  // GAP GRID-01
            gridView.Columns.Add(MakeCol("Assigned", "AssignedTo", 75));
            gridView.Columns.Add(MakeCol("SLA", "SLADeadline", 70));  // GAP GRID-02
            _listView.View = gridView;
            _listView.MouseDoubleClick += ListView_DoubleClick;

            // UX-05: Row coloring by status (overdue=red, critical=orange, alternating rows)
            _listView.ItemContainerStyle = BuildRowStyle();

            // Right-click context menu — will be set when window context is available
            _listView.Tag = "NEEDS_CONTEXT_MENU";

            // UI-02: Column sorting
            _listView.AddHandler(GridViewColumnHeader.ClickEvent,
                new RoutedEventHandler(ColumnHeader_Click));

            panel.Children.Add(_listView);
            UpdateCounts();
            return panel;
        }

        private static GridViewColumn MakeCol(string header, string binding, double width)
        {
            return new GridViewColumn
            {
                Header = header,
                DisplayMemberBinding = new Binding(binding),
                Width = width
            };
        }

        private static void ListView_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_listView.SelectedItem is not DocItemVM item) return;

            // Sticky note: inline edit
            if (item.Category == "STICKY")
            {
                EditStickyNote(item);
                return;
            }

            // File: open in default app
            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                try { Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
                catch (Exception ex) { StingLog.Warn($"DocMgr open: {ex.Message}"); }
                return;
            }

            // Compliance item: show detail
            if (item.Category == "COMPLIANCE" || item.Category == "DATADROP")
            {
                MessageBox.Show($"{item.Title}\n\nStatus: {item.Status}\nDate: {item.Date}",
                    "STING Detail", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static void EditStickyNote(DocItemVM item)
        {
            string newText = PromptForText("Edit Sticky Note", "Update note text:", item.Title);
            if (string.IsNullOrEmpty(newText) || newText == item.Title) return;

            try
            {
                string bimDir = GetBimManagerDir(_doc);
                string stickyPath = Path.Combine(bimDir, "sticky_notes.json");
                if (!File.Exists(stickyPath)) return;

                var arr = JArray.Parse(File.ReadAllText(stickyPath));
                var note = arr.FirstOrDefault(n => n["note_id"]?.ToString() == item.Id);
                if (note != null)
                {
                    note["text"] = newText;
                    note["modified"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    File.WriteAllText(stickyPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    ProjectFolderEngine.LogActivity(_doc, "EDIT_NOTE", item.Id, $"Updated: {newText.Substring(0, Math.Min(50, newText.Length))}");
                    item.Title = newText;
                    _view?.Refresh();
                }
            }
            catch (Exception ex) { StingLog.Warn($"EditStickyNote: {ex.Message}"); }
        }

        // UI-02: Column sorting
        private static string _lastSortProperty;
        private static ListSortDirection _lastSortDir = ListSortDirection.Ascending;

        private static void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header) return;
            if (header.Column?.DisplayMemberBinding is not Binding binding) return;
            string prop = binding.Path.Path;
            if (string.IsNullOrEmpty(prop)) return;

            if (prop == _lastSortProperty)
                _lastSortDir = _lastSortDir == ListSortDirection.Ascending
                    ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else
            {
                _lastSortProperty = prop;
                _lastSortDir = ListSortDirection.Ascending;
            }

            if (_view != null)
            {
                _view.SortDescriptions.Clear();
                _view.SortDescriptions.Add(new SortDescription(prop, _lastSortDir));
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ACTION BAR (Enhanced: GAP OP-04 bulk ops, OP-06 publish)
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildActionBar(Document doc, Window win)
        {
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                Padding = new Thickness(0),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var tabs = new TabControl
            {
                TabStripPlacement = Dock.Top,
                FontSize = 10,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0)
            };

            // ── TAB 1: FILE & BULK ──
            var fileWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            fileWrap.Children.Add(MakeSectionLabel("FILE"));
            fileWrap.Children.Add(MakeActBtn("Open", BrAccent, (s, e) => OpenSelected()));
            fileWrap.Children.Add(MakeActBtn("Open Folder", BrAccent, (s, e) => OpenFolder(doc)));
            fileWrap.Children.Add(MakeActBtn("Rename", BrFgDark, (s, e) => RenameSelected()));
            fileWrap.Children.Add(MakeActBtn("Delete", BrRed, (s, e) => DeleteSelected()));
            fileWrap.Children.Add(MakeActBtn("Move To", BrPurple, (s, e) => MoveSelected(doc)));
            fileWrap.Children.Add(MakeSep());
            fileWrap.Children.Add(MakeSectionLabel("BULK"));
            fileWrap.Children.Add(MakeActBtn("Bulk Move", BrPurple, (s, e) => BulkMove(doc)));
            fileWrap.Children.Add(MakeActBtn("Bulk Delete", BrRed, (s, e) => BulkDelete()));
            fileWrap.Children.Add(MakeActBtn("Close Issues", BrGreen, (s, e) => BulkCloseIssues(doc)));
            fileWrap.Children.Add(MakeActBtn("Delete Notes", BrRed, (s, e) => BulkDeleteStickyNotes(doc)));
            fileWrap.Children.Add(MakeActBtn("Update CDE", BrTeal, (s, e) => BulkUpdateCDE(doc)));
            fileWrap.Children.Add(MakeActBtn("Update Trans", BrTeal, (s, e) => BulkUpdateTransmittalStatus(doc)));
            fileWrap.Children.Add(MakeSep());
            fileWrap.Children.Add(MakeSectionLabel("RECOVER"));
            fileWrap.Children.Add(MakeActBtn("Restore", BrGreen, (s, e) => RestoreFromRecycle(doc)));
            fileWrap.Children.Add(MakeSep());
            fileWrap.Children.Add(MakeSectionLabel("EXPORT"));
            fileWrap.Children.Add(MakeActBtn("Export Visible CSV", BrGreen, (s, e) => ExportVisibleToCSV()));
            fileWrap.Children.Add(MakeActBtn("Code Legend", BrPurple, (s, e) => ShowCodeLegend()));
            tabs.Items.Add(new TabItem { Header = "FILE / BULK", Content = fileWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 2: DOCS & CDE ──
            var docsWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            docsWrap.Children.Add(MakeSectionLabel("REGISTER"));
            docsWrap.Children.Add(MakeDispatchBtn("Doc Register", "DocumentRegister", BrAccent, win));
            docsWrap.Children.Add(MakeDispatchBtn("Add Doc", "AddDocument", BrGreen, win));
            docsWrap.Children.Add(MakeDispatchBtn("Tag Register", "TagRegisterExport", BrPurple, win));
            docsWrap.Children.Add(MakeDispatchBtn("Naming Check", "ValidateDocNaming", BrFgDark, win));
            docsWrap.Children.Add(MakeSep());
            docsWrap.Children.Add(MakeSectionLabel("CDE"));
            docsWrap.Children.Add(MakeDispatchBtn("Publish CDE", "CDEPackage", BrTeal, win));
            docsWrap.Children.Add(MakeDispatchBtn("CDE Status", "CDEStatus", BrTeal, win));
            docsWrap.Children.Add(MakeDispatchBtn("MIDP Tracker", "MidpTracker", BrTeal, win));
            docsWrap.Children.Add(MakeDispatchBtn("Review Tracker", "ReviewTracker", BrTeal, win));
            docsWrap.Children.Add(MakeSep());
            docsWrap.Children.Add(MakeSectionLabel("TRANSMITTAL"));
            docsWrap.Children.Add(MakeDispatchBtn("Create", "CreateTransmittal", BrGreen, win));
            docsWrap.Children.Add(MakeActBtn("Quick Transmittal", BrGreen, (s, e) => QuickTransmittal(doc)));
            docsWrap.Children.Add(MakeDispatchBtn("Distribution", "RevisionDistribution", BrTeal, win));
            tabs.Items.Add(new TabItem { Header = "DOCS / CDE", Content = docsWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 3: ISSUES ──
            var issueWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            issueWrap.Children.Add(MakeSectionLabel("CREATE"));
            issueWrap.Children.Add(MakeDispatchBtn("Raise Issue", "RaiseIssue", BrOrange, win));
            issueWrap.Children.Add(MakeActBtn("Quick RFI", BrOrange, (s, e) => QuickIssue(doc, "RFI")));
            issueWrap.Children.Add(MakeActBtn("Quick NCR", BrRed, (s, e) => QuickIssue(doc, "NCR")));
            issueWrap.Children.Add(MakeActBtn("Quick SI", BrOrange, (s, e) => QuickIssue(doc, "SI")));
            issueWrap.Children.Add(MakeSep());
            issueWrap.Children.Add(MakeSectionLabel("MANAGE"));
            issueWrap.Children.Add(MakeDispatchBtn("Dashboard", "IssueDashboard", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Update", "UpdateIssue", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Filter", "IssueFilter", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Batch Update", "IssueBatchUpdate", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Select Elements", "SelectIssueElements", BrOrange, win));
            issueWrap.Children.Add(MakeSep());
            issueWrap.Children.Add(MakeSectionLabel("REPORT"));
            issueWrap.Children.Add(MakeDispatchBtn("Timeline", "IssueTimeline", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Statistics", "IssueStatistics", BrOrange, win));
            issueWrap.Children.Add(MakeDispatchBtn("Export CSV", "IssueExport", BrOrange, win));
            issueWrap.Children.Add(MakeActBtn("Overdue Report", BrRed, (s, e) => { _currentFilter = "OVERDUE"; _view?.Refresh(); UpdateCounts(); }));
            tabs.Items.Add(new TabItem { Header = "ISSUES", Content = issueWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 4: REVISIONS ──
            var revWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            revWrap.Children.Add(MakeSectionLabel("CREATE"));
            revWrap.Children.Add(MakeDispatchBtn("Create Rev", "CreateRevision", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Auto Cloud", "AutoRevisionCloud", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Bulk Stamp", "BulkRevisionStamp", BrPurple, win));
            revWrap.Children.Add(MakeSep());
            revWrap.Children.Add(MakeSectionLabel("TRACK"));
            revWrap.Children.Add(MakeDispatchBtn("Dashboard", "RevisionDashboard", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Compare", "RevisionCompare", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Track Elements", "TrackElementRevisions", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Schedule", "RevisionSchedule", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Naming Enforce", "RevisionNamingEnforce", BrPurple, win));
            revWrap.Children.Add(MakeSep());
            revWrap.Children.Add(MakeSectionLabel("DISTRIBUTE"));
            revWrap.Children.Add(MakeDispatchBtn("Issue Sheets", "IssueSheetsForRevision", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Export", "RevisionExport", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Tag Integration", "RevisionTagIntegration", BrPurple, win));
            revWrap.Children.Add(MakeDispatchBtn("Auto on Tag Change", "AutoRevisionOnTagChange", BrPurple, win));
            tabs.Items.Add(new TabItem { Header = "REVISIONS", Content = revWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 5: COORDINATION ──
            var coordWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            coordWrap.Children.Add(MakeSectionLabel("CLASHES"));
            coordWrap.Children.Add(MakeDispatchBtn("Run Clashes", "ClashDetection", BrRed, win));
            coordWrap.Children.Add(MakeDispatchBtn("BCF Export", "BCFExport", BrRed, win));
            coordWrap.Children.Add(MakeDispatchBtn("BCF Import", "BCFImport", BrRed, win));
            coordWrap.Children.Add(MakeSep());
            coordWrap.Children.Add(MakeSectionLabel("REVIEW"));
            coordWrap.Children.Add(MakeDispatchBtn("Review Tracker", "ReviewTracker", BrTeal, win));
            coordWrap.Children.Add(MakeDispatchBtn("Model Health", "ModelHealthDashboard", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Full Compliance", "FullComplianceDashboard", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Stage Gate", "StageComplianceGate", BrGreen, win));
            coordWrap.Children.Add(MakeSep());
            coordWrap.Children.Add(MakeSectionLabel("EXCHANGE"));
            coordWrap.Children.Add(MakeDispatchBtn("Excel Export", "ExportToExcel", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Excel Import", "ImportFromExcel", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Round-Trip", "ExcelRoundTrip", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Platform Sync", "PlatformSync", BrAccent, win));
            coordWrap.Children.Add(MakeSep());
            coordWrap.Children.Add(MakeSectionLabel("BIM"));
            coordWrap.Children.Add(MakeDispatchBtn("Project Dash", "ProjectDashboard", BrAccent, win));
            coordWrap.Children.Add(MakeDispatchBtn("Bulk Export", "BulkBIMExport", BrGreen, win));
            coordWrap.Children.Add(MakeDispatchBtn("Quantities", "MeasuredQuantities", BrTeal, win));
            coordWrap.Children.Add(MakeDispatchBtn("Element Summary", "ElementCountSummary", BrTeal, win));
            tabs.Items.Add(new TabItem { Header = "COORDINATION", Content = coordWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 6: HANDOVER ──
            var handWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            handWrap.Children.Add(MakeSectionLabel("COBie"));
            handWrap.Children.Add(MakeDispatchBtn("COBie Export", "COBieExport", BrTeal, win));
            handWrap.Children.Add(MakeDispatchBtn("Streaming", "StreamingCOBieExport", BrTeal, win));
            handWrap.Children.Add(MakeSep());
            handWrap.Children.Add(MakeSectionLabel("FM / O&M"));
            handWrap.Children.Add(MakeDispatchBtn("FM Handover", "HandoverManual", BrTeal, win));
            handWrap.Children.Add(MakeDispatchBtn("Maintenance", "MaintenanceSchedule", BrTeal, win));
            handWrap.Children.Add(MakeDispatchBtn("Asset Health", "AssetHealthReport", BrTeal, win));
            handWrap.Children.Add(MakeDispatchBtn("Space Handover", "SpaceHandover", BrTeal, win));
            handWrap.Children.Add(MakeSep());
            handWrap.Children.Add(MakeSectionLabel("PUBLISH"));
            handWrap.Children.Add(MakeDispatchBtn("ACC Publish", "ACCPublish", BrAccent, win));
            handWrap.Children.Add(MakeDispatchBtn("SharePoint", "SharePointExport", BrAccent, win));
            handWrap.Children.Add(MakeDispatchBtn("Export Health", "ExportModelHealth", BrGreen, win));
            handWrap.Children.Add(MakeSep());
            handWrap.Children.Add(MakeSectionLabel("4D / 5D"));
            handWrap.Children.Add(MakeDispatchBtn("4D Timeline", "Export4DTimeline", BrPurple, win));
            handWrap.Children.Add(MakeDispatchBtn("5D Cost Data", "Export5DCostData", BrPurple, win));
            tabs.Items.Add(new TabItem { Header = "HANDOVER", Content = handWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 7: NOTES & BEP ──
            var notesWrap = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            notesWrap.Children.Add(MakeSectionLabel("NOTES"));
            notesWrap.Children.Add(MakeActBtn("Quick Note", BrFgDark, (s, e) => CreateInlineStickyNote(doc)));
            notesWrap.Children.Add(MakeDispatchBtn("Add Note", "StickyNote", BrFgDark, win));
            notesWrap.Children.Add(MakeDispatchBtn("Dashboard", "StickyDashboard", BrFgDark, win));
            notesWrap.Children.Add(MakeDispatchBtn("Search", "StickySearch", BrFgDark, win));
            notesWrap.Children.Add(MakeDispatchBtn("Export", "ExportStickyNotes", BrFgDark, win));
            notesWrap.Children.Add(MakeSep());
            notesWrap.Children.Add(MakeSectionLabel("BRIEFCASE"));
            notesWrap.Children.Add(MakeDispatchBtn("Briefcase", "DocumentBriefcase", BrTeal, win));
            notesWrap.Children.Add(MakeDispatchBtn("View", "BriefcaseView", BrTeal, win));
            notesWrap.Children.Add(MakeSep());
            notesWrap.Children.Add(MakeSectionLabel("BEP"));
            notesWrap.Children.Add(MakeDispatchBtn("Create BEP", "CreateBEP", BrGreen, win));
            notesWrap.Children.Add(MakeDispatchBtn("Generate BEP", "GenerateBEP", BrGreen, win));
            notesWrap.Children.Add(MakeDispatchBtn("Update BEP", "UpdateBEP", BrTeal, win));
            notesWrap.Children.Add(MakeDispatchBtn("Export BEP", "ExportBEP", BrGreen, win));
            notesWrap.Children.Add(MakeDispatchBtn("ISO 19650 Ref", "ISO19650Reference", BrFgDark, win));
            tabs.Items.Add(new TabItem { Header = "NOTES / BEP", Content = notesWrap, Padding = new Thickness(8, 2, 8, 2) });

            // ── TAB 8: MEETINGS ──
            var meetWrap = new WrapPanel { Margin = new Thickness(4) };
            meetWrap.Children.Add(MakeSectionLabel("PREPARE"));
            meetWrap.Children.Add(MakeActBtn("New Meeting", BrAccent, (s, e) => CreateMeeting(doc)));
            meetWrap.Children.Add(MakeActBtn("Auto Agenda", BrGreen, (s, e) => GenerateAutoAgenda(doc)));
            meetWrap.Children.Add(MakeActBtn("Meeting Templates", BrTeal, (s, e) => ShowMeetingTemplates(doc)));
            meetWrap.Children.Add(MakeSep());
            meetWrap.Children.Add(MakeSectionLabel("DURING"));
            meetWrap.Children.Add(MakeActBtn("Log Minutes", BrAccent, (s, e) => LogMeetingMinutes(doc)));
            meetWrap.Children.Add(MakeActBtn("Add Action", BrGreen, (s, e) => AddActionItem(doc)));
            meetWrap.Children.Add(MakeActBtn("Quick Issue", BrRed, (s, e) => QuickIssue(doc, "ACTION")));
            meetWrap.Children.Add(MakeSep());
            meetWrap.Children.Add(MakeSectionLabel("REVIEW"));
            meetWrap.Children.Add(MakeActBtn("Meeting History", BrTeal, (s, e) => ShowMeetingHistory(doc)));
            meetWrap.Children.Add(MakeActBtn("Open Actions", BrRed, (s, e) => ShowOpenActions(doc)));
            meetWrap.Children.Add(MakeActBtn("Export Minutes", BrGreen, (s, e) => ExportMeetingMinutes(doc)));
            tabs.Items.Add(new TabItem { Header = "MEETINGS", Content = meetWrap, Padding = new Thickness(8, 2, 8, 2) });

            bar.Child = tabs;
            return bar;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CODE LEGEND — all ISO 19650 codes in a compact reference dialog
        // ══════════════════════════════════════════════════════════════════

        private static void ShowCodeLegend()
        {
            var win = new Window
            {
                Title = "STING Code Legend — ISO 19650 Quick Reference",
                Width = 780, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize, Background = BrBg
            };
            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var root = new StackPanel();

            root.Children.Add(MakeLegendHeader("CDE STATUS (ISO 19650-1 §12)"));
            root.Children.Add(MakeLegendRow("WIP", "Work In Progress — being developed by originator", BrFgSub));
            root.Children.Add(MakeLegendRow("SHARED", "Shared — issued for coordination/review", BrTeal));
            root.Children.Add(MakeLegendRow("PUBLISHED", "Published — approved for use", BrGreen));
            root.Children.Add(MakeLegendRow("ARCHIVE", "Archive — retained for reference only", BrFgSub));

            root.Children.Add(MakeLegendHeader("SUITABILITY CODES (ISO 19650 Table 9)"));
            root.Children.Add(MakeLegendRow("S0", "Work In Progress — preliminary, not for sharing", BrFgSub));
            root.Children.Add(MakeLegendRow("S1", "Fit for Coordination — internal team collaboration", BrTeal));
            root.Children.Add(MakeLegendRow("S2", "Fit for Information — shared with external parties", BrGreen));
            root.Children.Add(MakeLegendRow("S3", "Fit for Review & Comment — client/stakeholder input", BrOrange));
            root.Children.Add(MakeLegendRow("S4", "Fit for Stage Approval — RIBA stage gate sign-off", BrGreen));
            root.Children.Add(MakeLegendRow("S5", "Fit for Manufacturing / Procurement", BrPurple));
            root.Children.Add(MakeLegendRow("S6", "Fit for PIM Authorisation — production model", BrPurple));
            root.Children.Add(MakeLegendRow("S7", "Fit for AIM Authorisation — asset handover", BrPurple));
            root.Children.Add(MakeLegendRow("CR", "As-Constructed Record Document", BrFgDark));
            root.Children.Add(MakeLegendRow("AB", "Abandoned / Superseded — obsolete version", BrRed));

            root.Children.Add(MakeLegendHeader("DOCUMENT STATUS CODES (ISO 19650-2 §5.6)"));
            foreach (var kv in BIMManager.DocStatusCodes.All.Take(20))
                root.Children.Add(MakeLegendRow(kv.Key, kv.Value, BrFgDark));

            root.Children.Add(MakeLegendHeader("DOCUMENT TYPE CODES (UK BIM Framework)"));
            foreach (var kv in BIMManager.BIMManagerEngine.DocumentTypes.Take(20))
                root.Children.Add(MakeLegendRow(kv.Key, kv.Value, BrAccent));

            root.Children.Add(MakeLegendHeader("ISSUE TYPES (BCF 2.1 + NEC/JCT)"));
            foreach (var kv in BIMManager.BIMManagerEngine.IssueTypes)
                root.Children.Add(MakeLegendRow(kv.Key, kv.Value, BrOrange));

            root.Children.Add(MakeLegendHeader("ISSUE STATUS"));
            foreach (var kv in BIMManager.BIMManagerEngine.IssueStatuses)
                root.Children.Add(MakeLegendRow(kv.Key, kv.Value, BrFgDark));

            root.Children.Add(MakeLegendHeader("ISSUE PRIORITY & SLA"));
            root.Children.Add(MakeLegendRow("CRITICAL", "Immediate action — 2 day SLA", BrRed));
            root.Children.Add(MakeLegendRow("HIGH", "Urgent — 5 day SLA", BrOrange));
            root.Children.Add(MakeLegendRow("MEDIUM", "Standard — 14 day SLA", BrAccent));
            root.Children.Add(MakeLegendRow("LOW", "Minor — 30 day SLA", BrGreen));
            root.Children.Add(MakeLegendRow("INFO", "For information only — no SLA", BrFgSub));

            root.Children.Add(MakeLegendHeader("TRANSMITTAL STATUS"));
            root.Children.Add(MakeLegendRow("DRAFT", "Being prepared, not yet sent", BrFgSub));
            root.Children.Add(MakeLegendRow("SENT", "Transmitted to recipient", BrTeal));
            root.Children.Add(MakeLegendRow("RECEIVED", "Receipt acknowledged by recipient", BrGreen));
            root.Children.Add(MakeLegendRow("ACKNOWLEDGED", "Content acknowledged and reviewed", BrGreen));
            root.Children.Add(MakeLegendRow("SIGNED", "Formally signed off", BrPurple));
            root.Children.Add(MakeLegendRow("REJECTED", "Returned with comments", BrRed));
            root.Children.Add(MakeLegendRow("SUPERSEDED", "Replaced by newer transmittal", BrFgSub));

            root.Children.Add(MakeLegendHeader("DISCIPLINE CODES (STING)"));
            root.Children.Add(MakeLegendRow("M", "Mechanical — HVAC, heating, ventilation", new SolidColorBrush(Colors.Blue)));
            root.Children.Add(MakeLegendRow("E", "Electrical — power, lighting, comms", new SolidColorBrush(Colors.Goldenrod)));
            root.Children.Add(MakeLegendRow("P", "Plumbing — DHW, DCW, sanitary, drainage", BrGreen));
            root.Children.Add(MakeLegendRow("A", "Architectural — walls, doors, windows, floors", BrFgSub));
            root.Children.Add(MakeLegendRow("S", "Structural — columns, beams, foundations", BrRed));
            root.Children.Add(MakeLegendRow("FP", "Fire Protection — alarms, sprinklers, suppression", BrOrange));
            root.Children.Add(MakeLegendRow("LV", "Low Voltage — data, CCTV, access control", BrPurple));
            root.Children.Add(MakeLegendRow("G", "General — multi-discipline / unclassified", BrFgDark));

            root.Children.Add(MakeLegendHeader("DATA DROP MILESTONES (ISO 19650-2 §5.3)"));
            root.Children.Add(MakeLegendRow("DD1", "Stage 2 — Concept Design (BEP, tag structure, COBie schema)", BrTeal));
            root.Children.Add(MakeLegendRow("DD2", "Stage 3 — Developed Design (tagged elements, spatial data, schedules)", BrTeal));
            root.Children.Add(MakeLegendRow("DD3", "Stage 4 — Technical Design (full tagging, BEP compliance, COBie draft)", BrTeal));
            root.Children.Add(MakeLegendRow("DD4", "Stage 5-6 — Construction/Handover (COBie final, O&M, FM handover)", BrTeal));

            root.Children.Add(MakeLegendHeader("ISO 19650 FILE NAMING CONVENTION"));
            root.Children.Add(new TextBlock
            {
                Text = "Format: Project-Originator-Volume-Level-Type-Role-Classification-Number\n" +
                    "Example: PRJ-ARC-ZZ-L01-DR-A-0001\n" +
                    "  PRJ = Project code  |  ARC = Originator  |  ZZ = All volumes\n" +
                    "  L01 = Level 01  |  DR = Drawing  |  A = Architecture  |  0001 = Sequential",
                FontSize = 10, Foreground = BrFgSub, Margin = new Thickness(8, 2, 0, 8),
                TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas")
            });

            root.Children.Add(MakeLegendHeader("RAG COMPLIANCE STATUS"));
            root.Children.Add(MakeLegendRow("GREEN", "≥80% tag compliance — model is well-tagged", BrGreen));
            root.Children.Add(MakeLegendRow("AMBER", "50-79% tag compliance — needs attention", BrAmber));
            root.Children.Add(MakeLegendRow("RED", "<50% tag compliance — critical gaps", BrRed));

            // Close button
            var btnClose = new Button
            {
                Content = "Close", Width = 80, Height = 28,
                Background = BrAccent, Foreground = Brushes.White,
                FontSize = 11, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnClose.Click += (s, e) => win.Close();
            root.Children.Add(btnClose);

            scroll.Content = root;
            win.Content = scroll;
            win.ShowDialog();
        }

        private static TextBlock MakeLegendHeader(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = BrHeader, Margin = new Thickness(0, 10, 0, 4),
                Padding = new Thickness(4, 2, 0, 2),
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5))
            };
        }

        private static DockPanel MakeLegendRow(string code, string desc, SolidColorBrush color)
        {
            var row = new DockPanel { Margin = new Thickness(8, 1, 0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = code, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = color, Width = 65, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            });
            row.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 10, Foreground = BrFgDark,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            return row;
        }

        // ══════════════════════════════════════════════════════════════════
        //  QUICK INLINE OPERATIONS — no dialog dispatch needed
        // ══════════════════════════════════════════════════════════════════

        private static void ExportVisibleToCSV()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Visible Items to CSV",
                Filter = "CSV files|*.csv",
                FileName = $"STING_DocMgr_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Type,ID,Title,Status,CDE,Suitability,Rev,Disc,Folder,Format,Size,Date,Priority,Age,Elements,Assigned,SLA,Overdue,CreatedBy");
            foreach (var obj in _view)
            {
                if (obj is DocItemVM it)
                    sb.AppendLine($"\"{it.Type}\",\"{it.Id}\",\"{it.Title?.Replace("\"", "\"\"")}\",\"{it.Status}\",\"{it.CDE}\"," +
                        $"\"{it.Suitability}\",\"{it.Revision}\",\"{it.Discipline}\",\"{it.Folder}\",\"{it.FileFormat}\"," +
                        $"\"{it.Size}\",\"{it.Date}\",\"{it.Priority}\",\"{it.Aging}\",\"{it.ElementCount}\"," +
                        $"\"{it.AssignedTo}\",\"{it.SLADeadline}\",\"{it.IsOverdue}\",\"{it.CreatedBy}\"");
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
            if (_doc != null) ProjectFolderEngine.LogActivity(_doc, "EXPORT_CSV", Path.GetFileName(dlg.FileName), $"{_view.Count} rows");
            MessageBox.Show($"Exported {_view.Count} rows to:\n{dlg.FileName}", "STING Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Quick transmittal creation from selected document items.</summary>
        private static void QuickTransmittal(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => i.Category == "DOCUMENT" && !string.IsNullOrEmpty(i.FilePath)).ToList();
            if (selected == null || selected.Count == 0)
            {
                MessageBox.Show("Select document files in the list to create a quick transmittal.", "STING Transmittal");
                return;
            }

            string recipient = PromptForText("Quick Transmittal", $"Recipient for {selected.Count} document(s):", "");
            if (string.IsNullOrEmpty(recipient)) return;

            try
            {
                string bimDir = GetBimManagerDir(doc);
                string transPath = Path.Combine(bimDir, "transmittals.json");
                JArray arr;
                try { arr = File.Exists(transPath) ? JArray.Parse(File.ReadAllText(transPath)) : new JArray(); }
                catch { arr = new JArray(); }

                string transId = $"TX-{arr.Count + 1:D4}";
                var docList = new JArray(selected.Select(s => s.Title).ToArray());
                var trans = new JObject
                {
                    ["transmittal_id"] = transId,
                    ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                    ["recipient"] = recipient,
                    ["status"] = "DRAFT",
                    ["revision"] = selected.FirstOrDefault()?.Revision ?? "",
                    ["documents"] = docList,
                    ["created_by"] = Environment.UserName,
                    ["title"] = transId,
                    ["status_history"] = $"{DateTime.Now:yyyy-MM-dd HH:mm} CREATED by {Environment.UserName}"
                };
                arr.Add(trans);
                File.WriteAllText(transPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "CREATE_TRANSMITTAL", transId, $"{selected.Count} docs to {recipient}");
                MessageBox.Show($"Transmittal {transId} created:\n{selected.Count} documents → {recipient}\n\nStatus: DRAFT",
                    "STING Transmittal", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshData();
            }
            catch (Exception ex2) { StingLog.Warn($"QuickTransmittal: {ex2.Message}"); MessageBox.Show($"Error: {ex2.Message}"); }
        }

        /// <summary>Quick issue creation with type pre-set (RFI/NCR/SI/TQ).</summary>
        private static void QuickIssue(Document doc, string issueType)
        {
            string title = PromptForText($"Quick {issueType}", $"Enter {issueType} title/description:", "");
            if (string.IsNullOrEmpty(title)) return;

            // Priority selection
            var priorities = new List<string> { "CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO" };
            string priority = StingListPicker.Show($"{issueType} Priority", "Select priority:", priorities);
            if (string.IsNullOrEmpty(priority)) priority = "MEDIUM";

            // Discipline (optional — based on current filter context)
            string disc = "";
            if (_currentFilter.StartsWith("DISC:")) disc = _currentFilter.Substring(5);

            try
            {
                string bimDir = GetBimManagerDir(doc);
                string issuePath = Path.Combine(bimDir, "issues.json");
                JArray arr;
                try { arr = File.Exists(issuePath) ? JArray.Parse(File.ReadAllText(issuePath)) : new JArray(); }
                catch { arr = new JArray(); }

                string issueId = $"{issueType}-{arr.Count(i => i["type"]?.ToString() == issueType) + 1:D4}";
                var issue = new JObject
                {
                    ["issue_id"] = issueId,
                    ["type"] = issueType,
                    ["title"] = title,
                    ["status"] = "OPEN",
                    ["priority"] = priority,
                    ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                    ["assigned_to"] = "",
                    ["discipline"] = disc,
                    ["description"] = title,
                    ["created_by"] = Environment.UserName,
                    ["linked_elements"] = new JArray(),
                    ["status_history"] = $"{DateTime.Now:yyyy-MM-dd HH:mm} OPEN — created by {Environment.UserName}"
                };

                // Auto-detect revision
                try
                {
                    string rev = PhaseAutoDetect.DetectProjectRevision(doc);
                    if (!string.IsNullOrEmpty(rev)) issue["revision"] = rev;
                }
                catch (Exception ex2) { StingLog.Warn($"QuickIssue rev detect: {ex2.Message}"); }

                arr.Add(issue);
                File.WriteAllText(issuePath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "CREATE_ISSUE", issueId, $"{issueType}: {title}");
                MessageBox.Show($"Issue {issueId} created:\n\nType: {issueType}\nPriority: {priority}\nTitle: {title}",
                    "STING Issues", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshData();
            }
            catch (Exception ex2) { StingLog.Warn($"QuickIssue: {ex2.Message}"); MessageBox.Show($"Error: {ex2.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  MEETING MANAGER — Agenda, Minutes, Action Items
        // ══════════════════════════════════════════════════════════════════

        private static string GetMeetingsPath(Document doc) =>
            Path.Combine(GetBimManagerDir(doc), "meetings.json");

        /// <summary>Create a new meeting with attendees, series support, and follow-up from previous.</summary>
        private static void CreateMeeting(Document doc)
        {
            var types = new List<string>
            {
                "BIM Coordination Meeting", "Design Review", "Client Review",
                "Handover Review", "Clash Resolution", "Ad-hoc / Other"
            };
            string meetingType = StingListPicker.Show("New Meeting", "Select meeting type:", types);
            if (string.IsNullOrEmpty(meetingType)) return;

            string bimDir = GetBimManagerDir(doc);
            if (!Directory.Exists(bimDir)) Directory.CreateDirectory(bimDir);
            string path = GetMeetingsPath(doc);
            var meetings = File.Exists(path) ? JArray.Parse(File.ReadAllText(path)) : new JArray();

            // Calculate series number (sequential within same type)
            int seriesNum = meetings.Count(m => m["type"]?.ToString() == meetingType) + 1;
            int nextId = meetings.Count + 1;

            // Find previous meeting of same type to carry forward open actions
            var prevMeeting = meetings.LastOrDefault(m => m["type"]?.ToString() == meetingType);
            int carryForwardActions = 0;
            if (prevMeeting?["actions"] is JArray prevActs)
                carryForwardActions = prevActs.Count(a => a["status"]?.ToString() != "CLOSED");

            // Attendee input dialog
            var attendeeWin = new Window
            {
                Title = $"Meeting Attendees — {meetingType} #{seriesNum}",
                Width = 500, Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            var attStack = new StackPanel { Margin = new Thickness(12) };
            attStack.Children.Add(new TextBlock
            {
                Text = $"Meeting: {meetingType} #{seriesNum}\nDate: {DateTime.Now:yyyy-MM-dd}",
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8)
            });
            if (carryForwardActions > 0)
            {
                attStack.Children.Add(new TextBlock
                {
                    Text = $"\u2139 {carryForwardActions} open action(s) will carry forward from previous meeting",
                    Foreground = BrTeal, Margin = new Thickness(0, 0, 0, 8), FontStyle = FontStyles.Italic
                });
            }
            attStack.Children.Add(new TextBlock { Text = "Chair:", Margin = new Thickness(0, 4, 0, 2) });
            var chairBox = new System.Windows.Controls.TextBox { Text = Environment.UserName };
            attStack.Children.Add(chairBox);
            attStack.Children.Add(new TextBlock
            {
                Text = "Attendees (one per line — Name, Role, Discipline):",
                Margin = new Thickness(0, 8, 0, 2)
            });
            var attBox = new System.Windows.Controls.TextBox
            {
                AcceptsReturn = true, Height = 140, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = $"{Environment.UserName}, BIM Coordinator, M\n"
            };
            attStack.Children.Add(attBox);
            var createBtn = new Button
            {
                Content = "Create Meeting", Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(16, 6, 16, 6)
            };
            bool created = false;
            createBtn.Click += (s, e) => { created = true; attendeeWin.DialogResult = true; attendeeWin.Close(); };
            attStack.Children.Add(createBtn);
            attendeeWin.Content = attStack;
            attendeeWin.ShowDialog();
            if (!created) return;

            // Parse attendees
            var attendees = new JArray();
            foreach (string line in attBox.Text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                attendees.Add(new JObject
                {
                    ["name"] = parts.Length > 0 ? parts[0] : "",
                    ["role"] = parts.Length > 1 ? parts[1] : "",
                    ["discipline"] = parts.Length > 2 ? parts[2] : "",
                    ["present"] = true
                });
            }

            var meeting = new JObject
            {
                ["id"] = $"MTG-{nextId:D4}",
                ["type"] = meetingType,
                ["series_num"] = seriesNum,
                ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["time"] = DateTime.Now.ToString("HH:mm"),
                ["status"] = "PLANNED",
                ["chair"] = chairBox.Text,
                ["created_by"] = Environment.UserName,
                ["previous_meeting"] = prevMeeting?["id"]?.ToString() ?? "",
                ["attendees"] = attendees,
                ["agenda"] = new JArray(),
                ["minutes"] = "",
                ["actions"] = new JArray()
            };

            // Carry forward open actions from previous meeting
            if (prevMeeting?["actions"] is JArray prevActions)
            {
                foreach (var a in prevActions.Where(a => a["status"]?.ToString() != "CLOSED"))
                {
                    var carried = a.DeepClone();
                    carried["carried_from"] = prevMeeting["id"]?.ToString();
                    (meeting["actions"] as JArray)?.Add(carried);
                }
            }

            meetings.Add(meeting);
            File.WriteAllText(path, meetings.ToString(Newtonsoft.Json.Formatting.Indented));
            ProjectFolderEngine.LogActivity(doc, "MEETING_CREATED", meeting["id"].ToString(),
                $"{meetingType} #{seriesNum}, {attendees.Count} attendees" +
                (carryForwardActions > 0 ? $", {carryForwardActions} actions carried forward" : ""));

            // Show confirmation in result panel
            var panel = StingResultPanel.Create("Meeting Created")
                .SetSubtitle($"{meetingType} #{seriesNum}")
                .AddSection("DETAILS")
                .Metric("Meeting ID", meeting["id"].ToString())
                .Metric("Type", meetingType)
                .Metric("Series #", seriesNum.ToString())
                .Metric("Date", DateTime.Now.ToString("yyyy-MM-dd"))
                .Metric("Chair", chairBox.Text)
                .Metric("Attendees", attendees.Count.ToString());
            if (carryForwardActions > 0)
                panel.AddSection("CARRIED FORWARD")
                    .Info($"{carryForwardActions} open action items carried from {prevMeeting["id"]}");

            panel.AddSection("NEXT STEPS")
                .Text("Use 'Auto Agenda' to populate agenda from issues, transmittals, and revisions")
                .Text("Use 'Log Minutes' during the meeting to record notes")
                .Text("Use 'Add Action' to assign action items");
            panel.Show();
            RefreshData();
        }

        /// <summary>Auto-generate meeting agenda from open issues, pending transmittals, and recent revisions.</summary>
        private static void GenerateAutoAgenda(Document doc)
        {
            string bimDir = GetBimManagerDir(doc);
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings found. Create a meeting first."); return; }

            var meetings = JArray.Parse(File.ReadAllText(meetPath));
            var planned = meetings.Where(m => m["status"]?.ToString() == "PLANNED").ToList();
            if (planned.Count == 0) { MessageBox.Show("No planned meetings. Create a meeting first."); return; }

            // Select meeting
            var meetList = planned.Select(m => $"{m["id"]} — {m["type"]} ({m["date"]})").ToList();
            string pick = StingListPicker.Show("Select Meeting", "Generate agenda for:", meetList);
            if (string.IsNullOrEmpty(pick)) return;
            string meetId = pick.Split(' ')[0];
            var target = meetings.FirstOrDefault(m => m["id"]?.ToString() == meetId);
            if (target == null) return;

            var agenda = new JArray();
            int itemNum = 1;

            // 1. Review previous action items
            var allActions = new JArray();
            foreach (var m in meetings)
            {
                if (m["actions"] is JArray acts)
                    foreach (var a in acts)
                        if (a["status"]?.ToString() != "CLOSED")
                            allActions.Add(a);
            }
            if (allActions.Count > 0)
                agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = $"Review open action items ({allActions.Count} outstanding)", ["source"] = "ACTIONS", ["duration_min"] = 10 });

            // 2. Open issues
            string issuesPath = Path.Combine(bimDir, "issues.json");
            int openIssues = 0;
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    openIssues = issues.Count(i => i["status"]?.ToString() != "CLOSED" && i["status"]?.ToString() != "RESOLVED");
                    if (openIssues > 0)
                    {
                        // Group by type
                        var byType = issues.Where(i => i["status"]?.ToString() != "CLOSED" && i["status"]?.ToString() != "RESOLVED")
                            .GroupBy(i => i["type"]?.ToString() ?? "OTHER");
                        foreach (var g in byType.OrderByDescending(g => g.Count()))
                            agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = $"Review {g.Key} issues ({g.Count()} open)", ["source"] = "ISSUES", ["duration_min"] = 5 });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"AutoAgenda issues: {ex.Message}"); }
            }

            // 3. Pending transmittals
            string txPath = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txPath))
            {
                try
                {
                    var txs = JArray.Parse(File.ReadAllText(txPath));
                    var pending = txs.Where(t => t["status"]?.ToString() == "DRAFT" || t["status"]?.ToString() == "SENT").ToList();
                    if (pending.Count > 0)
                        agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = $"Transmittal status update ({pending.Count} pending)", ["source"] = "TRANSMITTALS", ["duration_min"] = 5 });
                }
                catch (Exception ex) { StingLog.Warn($"AutoAgenda transmittals: {ex.Message}"); }
            }

            // 4. Recent revisions
            string revPath = Path.Combine(bimDir, "revisions.json");
            if (File.Exists(revPath))
            {
                try
                {
                    var revs = JArray.Parse(File.ReadAllText(revPath));
                    var recent = revs.Where(r =>
                    {
                        if (DateTime.TryParse(r["date"]?.ToString(), out DateTime dt))
                            return (DateTime.Now - dt).TotalDays <= 14;
                        return false;
                    }).ToList();
                    if (recent.Count > 0)
                        agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = $"Recent revisions ({recent.Count} in last 14 days)", ["source"] = "REVISIONS", ["duration_min"] = 5 });
                }
                catch (Exception ex) { StingLog.Warn($"AutoAgenda revisions: {ex.Message}"); }
            }

            // 5. Compliance status
            try
            {
                var scan = ComplianceScan.Scan(doc);
                string ragEmoji = scan.RAGStatus == "GREEN" ? "\u2705" : scan.RAGStatus == "AMBER" ? "\u26A0" : "\u274C";
                agenda.Add(new JObject { ["num"] = itemNum++,
                    ["topic"] = $"{ragEmoji} Model compliance: {scan.CompliancePercent:F0}% — {scan.Untagged} untagged, {scan.StaleCount} stale",
                    ["source"] = "COMPLIANCE", ["duration_min"] = 5, ["section"] = "Compliance & Quality" });

                // Per-discipline breakdown if available
                if (scan.ByDisc != null && scan.ByDisc.Count > 0)
                {
                    var lowDisc = scan.ByDisc.Where(d => d.Value.CompliancePct < 50).OrderBy(d => d.Value.CompliancePct).ToList();
                    if (lowDisc.Count > 0)
                        agenda.Add(new JObject { ["num"] = itemNum++,
                            ["topic"] = $"Low compliance disciplines: {string.Join(", ", lowDisc.Select(d => $"{d.Key}={d.Value.CompliancePct:F0}%"))}",
                            ["source"] = "COMPLIANCE", ["duration_min"] = 3, ["section"] = "Compliance & Quality" });
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoAgenda compliance: {ex.Message}"); }

            // 6. Model upload / submission status (check linked models)
            try
            {
                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
                if (links.Count > 0)
                    agenda.Add(new JObject { ["num"] = itemNum++,
                        ["topic"] = $"Linked model status: {links.Count} models linked",
                        ["source"] = "MODELS", ["duration_min"] = 3, ["section"] = "Model Status" });
            }
            catch (Exception ex) { StingLog.Warn($"AutoAgenda links: {ex.Message}"); }

            // 7. Upcoming milestones / next meeting
            agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = "Upcoming milestones and MIDP deadlines", ["source"] = "STANDARD", ["duration_min"] = 5, ["section"] = "Planning" });

            // 8. AOB
            agenda.Add(new JObject { ["num"] = itemNum++, ["topic"] = "Any other business", ["source"] = "STANDARD", ["duration_min"] = 5, ["section"] = "AOB" });

            target["agenda"] = agenda;
            int totalMin = agenda.Sum(a => a["duration_min"]?.Value<int>() ?? 5);
            File.WriteAllText(meetPath, meetings.ToString(Newtonsoft.Json.Formatting.Indented));

            // Show agenda in result panel, grouped by section
            var panel = StingResultPanel.Create("Meeting Agenda")
                .SetSubtitle($"{target["type"]} — {target["date"]} | Est. {totalMin} minutes | {agenda.Count} items");

            // Group agenda items by section
            var sections = agenda.GroupBy(a => a["section"]?.ToString() ?? a["source"]?.ToString() ?? "General");
            foreach (var section in sections)
            {
                panel.AddSection(section.Key.ToUpperInvariant());
                foreach (var item in section)
                    panel.Metric($"{item["num"]}.", item["topic"]?.ToString(), $"{item["duration_min"]}min");
            }

            panel.AddSection("MEETING SUMMARY")
                .Metric("Meeting ID", meetId)
                .Metric("Total agenda items", agenda.Count.ToString())
                .Metric("Estimated duration", $"{totalMin} minutes")
                .Metric("Open action items", allActions.Count.ToString())
                .Metric("Open issues", openIssues.ToString());

            panel.Show();
            RefreshData();
        }

        private static void ShowMeetingTemplates(Document doc)
        {
            var templates = new List<string>
            {
                "Weekly BIM Coordination — Review clashes, actions, model health, transmittals",
                "Design Review — Present design intent, review comments, agree next steps",
                "Client Stage Review — RIBA stage deliverables, compliance, sign-off",
                "Handover Review — COBie readiness, O&M data, asset register completeness",
                "Clash Resolution — Discipline-pair clash walkthrough with assignments"
            };
            string pick = StingListPicker.Show("Meeting Templates", "Select template to apply:", templates);
            if (!string.IsNullOrEmpty(pick))
                MessageBox.Show($"Template selected: {pick.Split('—')[0].Trim()}\n\nCreate a meeting and use 'Auto Agenda' to generate agenda items based on current project data.",
                    "STING Meeting Templates");
        }

        private static void LogMeetingMinutes(Document doc)
        {
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings found."); return; }
            var meetings = JArray.Parse(File.ReadAllText(meetPath));
            var active = meetings.Where(m => m["status"]?.ToString() != "CLOSED").ToList();
            if (active.Count == 0) { MessageBox.Show("No active meetings."); return; }

            var meetList = active.Select(m => $"{m["id"]} — {m["type"]} ({m["date"]})").ToList();
            string pick = StingListPicker.Show("Log Minutes", "Select meeting:", meetList);
            if (string.IsNullOrEmpty(pick)) return;
            string meetId = pick.Split(' ')[0];
            var target = meetings.FirstOrDefault(m => m["id"]?.ToString() == meetId);
            if (target == null) return;

            // Simple input for minutes text
            var inputWin = new Window
            {
                Title = $"Meeting Minutes — {meetId}", Width = 520, Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.CanResize
            };
            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = "Enter meeting minutes:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new System.Windows.Controls.TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = target["minutes"]?.ToString() ?? ""
            };
            stack.Children.Add(tb);
            var saveBtn = new Button { Content = "Save Minutes", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(16, 6, 16, 6) };
            saveBtn.Click += (s, e) =>
            {
                target["minutes"] = tb.Text;
                target["status"] = "IN_PROGRESS";
                File.WriteAllText(meetPath, meetings.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "MINUTES_LOGGED", meetId, $"{tb.Text.Length} chars");
                inputWin.DialogResult = true;
                inputWin.Close();
            };
            stack.Children.Add(saveBtn);
            inputWin.Content = stack;
            inputWin.ShowDialog();
            RefreshData();
        }

        private static void AddActionItem(Document doc)
        {
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings found."); return; }
            var meetings = JArray.Parse(File.ReadAllText(meetPath));
            var active = meetings.Where(m => m["status"]?.ToString() != "CLOSED").ToList();
            if (active.Count == 0) { MessageBox.Show("No active meetings."); return; }

            var meetList = active.Select(m => $"{m["id"]} — {m["type"]} ({m["date"]})").ToList();
            string pick = StingListPicker.Show("Add Action Item", "Add action to which meeting:", meetList);
            if (string.IsNullOrEmpty(pick)) return;
            string meetId = pick.Split(' ')[0];
            var target = meetings.FirstOrDefault(m => m["id"]?.ToString() == meetId);
            if (target == null) return;

            // Quick input dialog
            var inputWin = new Window
            {
                Title = $"New Action Item — {meetId}", Width = 450, Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void AddRow(int row, string label, out System.Windows.Controls.TextBox box)
            {
                var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Controls.Grid.SetRow(lbl, row);
                System.Windows.Controls.Grid.SetColumn(lbl, 0);
                grid.Children.Add(lbl);
                box = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 2, 0, 2) };
                System.Windows.Controls.Grid.SetRow(box, row);
                System.Windows.Controls.Grid.SetColumn(box, 1);
                grid.Children.Add(box);
            }

            AddRow(0, "Description:", out var descBox);
            AddRow(1, "Assigned To:", out var assignBox);
            assignBox.Text = Environment.UserName;
            AddRow(2, "Due Date:", out var dueBox);
            dueBox.Text = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");

            var saveBtn = new Button
            {
                Content = "Add Action", Margin = new Thickness(100, 8, 0, 0),
                Padding = new Thickness(16, 6, 16, 6)
            };
            System.Windows.Controls.Grid.SetRow(saveBtn, 3);
            System.Windows.Controls.Grid.SetColumnSpan(saveBtn, 2);
            grid.Children.Add(saveBtn);
            saveBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(descBox.Text)) { MessageBox.Show("Description required."); return; }
                var actions = target["actions"] as JArray ?? new JArray();
                actions.Add(new JObject
                {
                    ["id"] = $"ACT-{meetId.Replace("MTG-", "")}-{actions.Count + 1:D2}",
                    ["description"] = descBox.Text,
                    ["assigned_to"] = assignBox.Text,
                    ["due_date"] = dueBox.Text,
                    ["status"] = "OPEN",
                    ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                });
                target["actions"] = actions;
                File.WriteAllText(meetPath, meetings.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "ACTION_ADDED", meetId, descBox.Text);
                inputWin.DialogResult = true;
                inputWin.Close();
            };

            inputWin.Content = grid;
            inputWin.ShowDialog();
            RefreshData();
        }

        private static void ShowMeetingHistory(Document doc)
        {
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings recorded."); return; }
            var meetings = JArray.Parse(File.ReadAllText(meetPath));
            if (meetings.Count == 0) { MessageBox.Show("No meetings recorded."); return; }

            var panel = StingResultPanel.Create("Meeting History")
                .SetSubtitle($"{meetings.Count} meetings recorded");

            foreach (var m in meetings.Reverse())
            {
                string status = m["status"]?.ToString() ?? "PLANNED";
                var brush = status == "CLOSED"
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x75, 0x75, 0x75))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x23, 0x7E));
                panel.AddSection($"{m["id"]} — {m["type"]} ({m["date"]})", brush)
                    .Metric("Status", status)
                    .Metric("Created by", m["created_by"]?.ToString() ?? "")
                    .Metric("Agenda items", (m["agenda"] as JArray)?.Count.ToString() ?? "0")
                    .Metric("Action items", (m["actions"] as JArray)?.Count.ToString() ?? "0");

                // Show open actions
                if (m["actions"] is JArray acts)
                {
                    foreach (var a in acts.Where(a => a["status"]?.ToString() != "CLOSED"))
                        panel.Alert($"[{a["status"]}] {a["description"]} → {a["assigned_to"]} (due {a["due_date"]})");
                }
            }

            panel.Show();
        }

        private static void ShowOpenActions(Document doc)
        {
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings recorded."); return; }
            var meetings = JArray.Parse(File.ReadAllText(meetPath));

            var openActions = new List<(string MeetId, JToken Action)>();
            foreach (var m in meetings)
            {
                if (m["actions"] is JArray acts)
                    foreach (var a in acts.Where(a => a["status"]?.ToString() != "CLOSED"))
                        openActions.Add((m["id"]?.ToString() ?? "", a));
            }

            if (openActions.Count == 0) { MessageBox.Show("No open action items."); return; }

            var panel = StingResultPanel.Create("Open Action Items")
                .SetSubtitle($"{openActions.Count} actions outstanding");

            // Group by due date
            var overdue = openActions.Where(a =>
                DateTime.TryParse(a.Action["due_date"]?.ToString(), out var dt) && dt < DateTime.Now).ToList();
            var upcoming = openActions.Except(overdue).ToList();

            if (overdue.Count > 0)
            {
                panel.AddSection($"OVERDUE ({overdue.Count})", new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28)));
                foreach (var (meetId, a) in overdue)
                    panel.Alert($"[{meetId}] {a["description"]} → {a["assigned_to"]} (due {a["due_date"]})");
            }

            panel.AddSection($"UPCOMING ({upcoming.Count})");
            foreach (var (meetId, a) in upcoming)
                panel.Metric($"[{meetId}] {a["assigned_to"]}", a["description"]?.ToString(), $"due {a["due_date"]}");

            panel.Show();
        }

        private static void ExportMeetingMinutes(Document doc)
        {
            string meetPath = GetMeetingsPath(doc);
            if (!File.Exists(meetPath)) { MessageBox.Show("No meetings recorded."); return; }
            var meetings = JArray.Parse(File.ReadAllText(meetPath));
            if (meetings.Count == 0) { MessageBox.Show("No meetings recorded."); return; }

            var meetList = meetings.Select(m => $"{m["id"]} — {m["type"]} ({m["date"]})").ToList();
            string pick = StingListPicker.Show("Export Minutes", "Export minutes for:", meetList);
            if (string.IsNullOrEmpty(pick)) return;
            string meetId = pick.Split(' ')[0];
            var target = meetings.FirstOrDefault(m => m["id"]?.ToString() == meetId);
            if (target == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"╔══════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║  MEETING MINUTES — {target["id"],-38} ║");
            sb.AppendLine($"╚══════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine($"  Type:       {target["type"]}");
            sb.AppendLine($"  Series #:   {target["series_num"]}");
            sb.AppendLine($"  Date:       {target["date"]} {target["time"]}");
            sb.AppendLine($"  Chair:      {target["chair"]}");
            sb.AppendLine($"  Status:     {target["status"]}");
            sb.AppendLine($"  Created by: {target["created_by"]}");
            if (!string.IsNullOrEmpty(target["previous_meeting"]?.ToString()))
                sb.AppendLine($"  Previous:   {target["previous_meeting"]}");
            sb.AppendLine();

            // Attendees
            sb.AppendLine("── ATTENDEES ──");
            if (target["attendees"] is JArray attArr)
            {
                sb.AppendLine($"  {"Name",-25} {"Role",-20} {"Disc",-5} {"Present",-8}");
                sb.AppendLine($"  {new string('─', 60)}");
                foreach (var att in attArr)
                    sb.AppendLine($"  {att["name"]?.ToString() ?? "",-25} {att["role"]?.ToString() ?? "",-20} " +
                        $"{att["discipline"]?.ToString() ?? "",-5} {(att["present"]?.Value<bool>() ?? true ? "Yes" : "No"),-8}");
            }
            sb.AppendLine();

            // Agenda
            sb.AppendLine("── AGENDA ──");
            if (target["agenda"] is JArray agendaArr)
            {
                foreach (var item in agendaArr)
                    sb.AppendLine($"  {item["num"]}. [{item["source"]}] {item["topic"]} ({item["duration_min"]}min)");
            }
            sb.AppendLine();

            // Minutes
            sb.AppendLine("── MINUTES ──");
            sb.AppendLine(target["minutes"]?.ToString() ?? "(no minutes recorded)");
            sb.AppendLine();

            // Action items
            sb.AppendLine("── ACTION ITEMS ──");
            if (target["actions"] is JArray actsArr && actsArr.Count > 0)
            {
                sb.AppendLine($"  {"ID",-16} {"Status",-12} {"Assignee",-15} {"Due",-12} Description");
                sb.AppendLine($"  {new string('─', 80)}");
                foreach (var a in actsArr)
                {
                    sb.AppendLine($"  {a["id"]?.ToString() ?? "",-16} {a["status"]?.ToString() ?? "",-12} " +
                        $"{a["assigned_to"]?.ToString() ?? "",-15} {a["due_date"]?.ToString() ?? "",-12} {a["description"]}");
                    if (!string.IsNullOrEmpty(a["carried_from"]?.ToString()))
                        sb.AppendLine($"                 (carried from {a["carried_from"]})");
                }
            }
            else
                sb.AppendLine("  (no action items)");

            try
            {
                string exportPath = OutputLocationHelper.GetTimestampedPath(doc, $"STING_Minutes_{meetId}", ".txt");
                File.WriteAllText(exportPath, sb.ToString());
                Process.Start(new ProcessStartInfo(exportPath) { UseShellExecute = true });
                ProjectFolderEngine.LogActivity(doc, "MINUTES_EXPORTED", meetId, exportPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExportMinutes: {ex.Message}");
                // Fallback: copy to clipboard
                try { Clipboard.SetText(sb.ToString()); MessageBox.Show("Minutes copied to clipboard."); }
                catch (Exception ex2) { StingLog.Warn($"Clipboard: {ex2.Message}"); }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ACTION BAR HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 8, FontWeight = FontWeights.Bold,
                Foreground = BrFgSub, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0)
            };
        }

        private static Button MakeActBtn(string label, SolidColorBrush fg, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = label, Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(2), FontSize = 10, FontWeight = FontWeights.SemiBold,
                Background = Brushes.White, Foreground = fg,
                BorderBrush = fg, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = GetButtonTooltip(label)
            };
            btn.Click += handler;
            return btn;
        }

        private static Button MakeDispatchBtn(string label, string op, SolidColorBrush fg, Window win)
        {
            var btn = new Button
            {
                Content = label, Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(2), FontSize = 10,
                Background = Brushes.White, Foreground = fg,
                BorderBrush = fg, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = GetButtonTooltip(label, op)
            };
            btn.Click += (s, e) => { _selectedOperation = op; win.DialogResult = true; win.Close(); };
            return btn;
        }

        /// <summary>Rich tooltips for all action buttons — context-aware descriptions.</summary>
        private static string GetButtonTooltip(string label, string op = "")
        {
            return label switch
            {
                // File ops
                "Open" => "Open selected file in default application (double-click also works)",
                "Open Folder" => "Open containing folder in Windows Explorer",
                "Rename" => "Rename the selected file (validates ISO 19650 naming)",
                "Delete" => "Move selected file to _RECYCLE folder (recoverable)",
                "Move To" => "Move selected file to a different project folder",
                // Bulk ops
                "Bulk Move" => "Move all selected files to a chosen folder (multi-select with Ctrl/Shift)",
                "Bulk Delete" => "Delete all selected files to recycle bin (Ctrl+Shift+click to multi-select)",
                "Close Issues" => "Set status to CLOSED for all selected issues with audit trail",
                "Delete Notes" => "Remove all selected sticky notes from the project",
                "Update CDE" => "Change CDE status (WIP/SHARED/PUBLISHED/ARCHIVE) for selected docs. Auto-creates transmittal on SHARED/PUBLISHED",
                "Update Trans Status" => "Transition transmittal status (DRAFT→SENT→RECEIVED→ACKNOWLEDGED→SIGNED)",
                // Docs
                "Doc Register" => "View and filter all registered project documents with ISO 19650 metadata (10+ columns, export to CSV)",
                "Add Doc" => "Register a new document: select direction (IN/OUT), type code, and suitability. Auto-generates ISO 19650 Document ID",
                "Tag Register" => "Export comprehensive 40+ column asset register (tags, identity, spatial, MEP, cost, validation) to CSV",
                "Naming Check" => "Audit all sheet names against ISO 19650 naming convention with auto-correction suggestions",
                "Transmittal" => "Create ISO 19650 transmittal record with document list, recipient, and delivery tracking",
                "Publish CDE" => "Create ISO 19650 CDE folder package with discipline sub-folders and deliverable manifest",
                "CDE Status" => "View/update Common Data Environment suitability codes (S0-S7) for project containers",
                "Review Tracker" => "Track model review cycles, approval workflows, and information exchanges",
                "MIDP Tracker" => "Master Information Delivery Plan — track deliverable progress per discipline and RIBA stage",
                // Issues
                "Raise Issue" => "Create new issue (RFI, TQ, NCR, EWN, SI, VO, AI, CVI, CE, PMI, CLASH, DESIGN, SNAGGING, RISK, ACTION, COMMENT). Links to BCF",
                "Issue Dash" => "Full issue dashboard with statistics, trend analysis, priority breakdown, and overdue tracking",
                "Update Issue" => "Update an existing issue: change status, priority, assignee, or add comments",
                "Issue Filter" => "Advanced issue filtering by type, priority, status, assignee, discipline, and date range",
                "Timeline" => "View issue timeline showing creation, updates, and resolution dates on a visual timeline",
                "Statistics" => "Issue statistics: open vs closed, resolution time averages, SLA compliance, overdue rate",
                "Batch Update" => "Bulk update multiple issues at once: change priority, status, or assignee for selected items",
                "Export CSV" => "Export all issues to CSV file with full metadata (ID, type, priority, status, assignee, dates, elements)",
                "Select Elements" => "Select Revit elements linked to the selected issue in the model view",
                // Revisions
                "Rev Dash" => "Revision dashboard showing all revisions, issue status, and cloud counts",
                "Create Rev" => "Create a new revision with ISO 19650 naming (P01, C01 format)",
                "Rev Compare" => "Compare tag snapshots between two revisions — shows changed elements and parameter deltas",
                "Track Elements" => "Track which elements changed between revisions with parameter diff",
                "Rev Schedule" => "View revision schedule with sequence numbers, dates, and sheet assignments",
                "Rev Export" => "Export revision data to CSV with element counts and cloud statistics",
                "Bulk Stamp" => "Apply revision stamp to multiple sheets at once",
                "Auto Cloud" => "Automatically create revision clouds around changed elements",
                "Naming Enforce" => "Enforce ISO 19650 revision naming conventions (P01 preliminary, C01 construction)",
                // Clashes
                "Run Clashes" => "Run clash detection between discipline models with grouping by category",
                "BCF Export" => "Export issues and clashes as BCF 2.1 XML with camera viewpoints for external tools (Navisworks, Solibri, BIMcollab)",
                "BCF Import" => "Import BCF file and create issues from external clash detection tools",
                // Handover
                "COBie Export" => "Full COBie V2.4 spreadsheet export (19 worksheets) with project type presets",
                "Streaming COBie" => "Memory-efficient streaming COBie export for large models (5000+ elements)",
                "FM Handover" => "Generate FM handover manual: asset register, spatial summary, system descriptions, compliance report",
                "Maintenance" => "PPM and reactive maintenance schedule per ASTM E2018 / SFG20 standards",
                "Asset Health" => "Asset condition scoring (0-100) with ISO 15686 lifecycle assessment and replacement forecasting",
                "Space Handover" => "Room-by-room handover report with area, finishes, and services breakdown",
                // Compliance
                "Model Health" => "Model health dashboard: element counts, parameter completeness, quality metrics with RAG status",
                "Export Health" => "Export model health data to JSON/CSV for trend tracking",
                "Full Compliance" => "Comprehensive compliance dashboard: tag completeness, naming, COBie readiness, BEP compliance",
                "Stage Gate" => "RIBA stage compliance gate: checks DD1-DD4 data drop readiness with pass/fail criteria",
                // Exchange
                "Excel Export" => "Export element data to Excel (30+ columns: tags, identity, spatial, MEP) with column selection",
                "Excel Import" => "Import data from Excel with validation, change preview, and audit trail",
                "Excel Round-Trip" => "One-click export → edit → import cycle with change tracking",
                "ACC Publish" => "Package deliverables for Autodesk Construction Cloud (ACC/BIM 360)",
                "Platform Sync" => "Bidirectional sync with CDE platform — detect and merge changes",
                "SharePoint" => "Export deliverables to SharePoint/Microsoft Teams document library",
                // Notes & Briefcase
                "Quick Note" => "Create a text note directly in the Document Manager (no element selection needed)",
                "Add Note" => "Create a sticky note linked to selected Revit elements (requires element selection in model)",
                "Note Dash" => "Sticky note dashboard showing all notes by category, element, and date",
                "Note Search" => "Search sticky notes by text content, category, or linked element",
                "Export Notes" => "Export all sticky notes to CSV or JSON",
                "Briefcase" => "Full briefcase manager: 8-file package (project info, tag register, compliance, stats, sheets)",
                "View Briefcase" => "Browse reference documents in the project briefcase (BEP, standards, specifications)",
                // BEP
                "Create BEP" => "Create BIM Execution Plan from 22 project type presets with 23 ISO 19650-2 §5.3 sections",
                "Export BEP" => "Export BEP to JSON with compliance scan enrichment and deliverable manifest",
                "ISO 19650 Ref" => "Quick reference guide for ISO 19650 codes, suitability statuses, and BIM terminology",
                _ => op
            };
        }

        private static Border MakeSep()
        {
            return new Border { Width = 2, Height = 22, Background = BrBorder, Margin = new Thickness(4, 0, 4, 0) };
        }

        /// <summary>Build row style with conditional coloring by status.</summary>
        private static Style BuildRowStyle()
        {
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(2, 1, 2, 1)));
            style.Setters.Add(new Setter(ListViewItem.FontSizeProperty, 11.0));

            // Alternating row colors
            var altTrigger = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
            altTrigger.Setters.Add(new Setter(ListViewItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xFA))));
            style.Triggers.Add(altTrigger);

            // Overdue items: light red background
            var overdueTrigger = new DataTrigger
            {
                Binding = new Binding("IsOverdue"), Value = true
            };
            overdueTrigger.Setters.Add(new Setter(ListViewItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE))));
            overdueTrigger.Setters.Add(new Setter(ListViewItem.ForegroundProperty, BrRed));
            style.Triggers.Add(overdueTrigger);

            // Critical priority: light orange background
            var critTrigger = new DataTrigger
            {
                Binding = new Binding("Priority"), Value = "CRITICAL"
            };
            critTrigger.Setters.Add(new Setter(ListViewItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0))));
            critTrigger.Setters.Add(new Setter(ListViewItem.FontWeightProperty, FontWeights.Bold));
            style.Triggers.Add(critTrigger);

            // RED compliance: light red tint
            var redTrigger = new DataTrigger
            {
                Binding = new Binding("Status"), Value = "RED"
            };
            redTrigger.Setters.Add(new Setter(ListViewItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE))));
            style.Triggers.Add(redTrigger);

            // GREEN compliance: light green tint
            var greenTrigger = new DataTrigger
            {
                Binding = new Binding("Status"), Value = "GREEN"
            };
            greenTrigger.Setters.Add(new Setter(ListViewItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))));
            style.Triggers.Add(greenTrigger);

            // CLOSED issues: grey italic
            var closedTrigger = new DataTrigger
            {
                Binding = new Binding("Status"), Value = "CLOSED"
            };
            closedTrigger.Setters.Add(new Setter(ListViewItem.ForegroundProperty, BrFgSub));
            closedTrigger.Setters.Add(new Setter(ListViewItem.FontStyleProperty, FontStyles.Italic));
            style.Triggers.Add(closedTrigger);

            return style;
        }

        /// <summary>Restore a file from the _RECYCLE folder.</summary>
        private static void RestoreFromRecycle(Document doc)
        {
            string rootPath = ProjectFolderEngine.GetRootPath(doc);
            string recyclePath = string.IsNullOrEmpty(rootPath) ? "" : Path.Combine(rootPath, "_RECYCLE");
            if (!Directory.Exists(recyclePath) || Directory.GetFiles(recyclePath).Length == 0)
            {
                MessageBox.Show("Recycle bin is empty.", "STING Restore");
                return;
            }
            var files = Directory.GetFiles(recyclePath)
                .Select(f => Path.GetFileName(f)).OrderByDescending(f => f).ToList();
            string pick = StingListPicker.Show("Restore File", "Select file to restore from recycle bin:", files);
            if (string.IsNullOrEmpty(pick)) return;

            string srcPath = Path.Combine(recyclePath, pick);
            // Let user choose destination folder
            var folders = ProjectFolderEngine.Folders.Select(f => $"{f.Id}: {f.Name}").ToList();
            string destPick = StingListPicker.Show("Restore To", "Restore to which folder?", folders);
            if (string.IsNullOrEmpty(destPick)) return;

            string folderId = destPick.Split(':')[0].Trim();
            string destDir = ProjectFolderEngine.GetFolderPath(doc, folderId);
            if (!string.IsNullOrEmpty(destDir))
            {
                try
                {
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    string destPath = Path.Combine(destDir, pick);
                    File.Move(srcPath, destPath);
                    ProjectFolderEngine.LogActivity(doc, "RESTORE", pick, $"From recycle to {folderId}");
                    MessageBox.Show($"Restored: {pick}\nTo: {folderId}", "STING Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshData();
                }
                catch (Exception ex2) { StingLog.Warn($"Restore: {ex2.Message}"); MessageBox.Show($"Error: {ex2.Message}"); }
            }
        }

        // ── File operation implementations ──

        private static void OpenSelected()
        {
            if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }

        private static void OpenFolder(Document doc)
        {
            string dir = null;
            if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath))
                dir = Path.GetDirectoryName(item.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = ProjectFolderEngine.GetRootPath(doc);
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }

        private static void RenameSelected()
        {
            if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
            string currentName = Path.GetFileName(item.FilePath);
            string newName = PromptForText("Rename File", "Enter new filename:", currentName);
            if (!string.IsNullOrEmpty(newName) && newName != currentName)
            {
                if (ProjectFolderEngine.RenameFile(item.FilePath, newName))
                {
                    // Validate against ISO 19650 naming
                    var (valid, suggested, errors) = ProjectFolderEngine.ValidateFileName(_doc, newName);
                    if (!valid && errors.Count > 0)
                    {
                        MessageBox.Show($"Warning: filename may not be ISO 19650 compliant:\n\n" +
                            string.Join("\n", errors.Take(3)),
                            "STING Naming", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    ProjectFolderEngine.LogActivity(_doc, "RENAME", item.Id ?? "", $"{currentName} -> {newName}");
                    RefreshData();
                }
            }
        }

        private static void DeleteSelected()
        {
            if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
            if (MessageBox.Show($"Delete?\n\n{item.Title}", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                ProjectFolderEngine.LogActivity(_doc, "DELETE", item.Id ?? item.Title, item.FilePath ?? "");
                ProjectFolderEngine.DeleteFile(item.FilePath);
                _allItems.Remove(item);
                UpdateCounts();
            }
        }

        private static void MoveSelected(Document doc)
        {
            if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
            var folders = ProjectFolderEngine.Folders.Select(f => $"{f.Id}: {f.Name} — {f.Description}").ToList();
            string pick = StingListPicker.Show("Move To Folder", "Select destination:", folders);
            if (string.IsNullOrEmpty(pick)) return;
            string folderId = pick.Split(':')[0].Trim();
            if (ProjectFolderEngine.MoveFile(doc, item.FilePath, folderId))
                RefreshData();
        }

        // ── Bulk operations (GAP OP-04) ──

        private static void BulkMove(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => !string.IsNullOrEmpty(i.FilePath)).ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select files to move."); return; }
            var folders = ProjectFolderEngine.Folders.Select(f => $"{f.Id}: {f.Name}").ToList();
            string pick = StingListPicker.Show("Bulk Move", $"Move {selected.Count} files to:", folders);
            if (string.IsNullOrEmpty(pick)) return;
            string folderId = pick.Split(':')[0].Trim();
            int moved = 0;
            foreach (var item in selected)
            {
                if (ProjectFolderEngine.MoveFile(doc, item.FilePath, folderId)) moved++;
            }
            MessageBox.Show($"Moved {moved} of {selected.Count} files.");
            RefreshData();
        }

        private static void BulkDelete()
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => !string.IsNullOrEmpty(i.FilePath)).ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select files to delete."); return; }
            if (MessageBox.Show($"Delete {selected.Count} files?\n\nThis cannot be undone.",
                "Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int deleted = 0;
            foreach (var item in selected)
            {
                if (ProjectFolderEngine.DeleteFile(item.FilePath)) { _allItems.Remove(item); deleted++; }
            }
            MessageBox.Show($"Deleted {deleted} files.");
            UpdateCounts();
        }

        private static void BulkCloseIssues(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => i.Category == "ISSUE" && i.Status != "CLOSED").ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select open issues to close."); return; }
            if (MessageBox.Show($"Close {selected.Count} issues?",
                "Bulk Close", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            // Update issues.json
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string path = Path.Combine(bimDir, "issues.json");
                if (File.Exists(path))
                {
                    var arr = JArray.Parse(File.ReadAllText(path));
                    int closed = 0;
                    foreach (var item in selected)
                    {
                        var issue = arr.FirstOrDefault(i => i["issue_id"]?.ToString() == item.Id);
                        if (issue != null)
                        {
                            issue["status"] = "CLOSED";
                            issue["closed_date"] = DateTime.Now.ToString("yyyy-MM-dd");
                            issue["status_history"] = (issue["status_history"]?.ToString() ?? "")
                                + $"|{DateTime.Now:yyyy-MM-dd HH:mm} CLOSED (bulk)";
                            closed++;
                        }
                    }
                    File.WriteAllText(path, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    MessageBox.Show($"Closed {closed} issues.");
                    RefreshData();
                }
            }
            catch (Exception ex) { StingLog.Warn($"BulkClose: {ex.Message}"); }
        }

        // ── CDE state machine — ISO 19650 valid transitions ──
        private static readonly Dictionary<string, string[]> CDETransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            [""] = new[] { "WIP" },
            ["WIP"] = new[] { "SHARED" },
            ["SHARED"] = new[] { "WIP", "PUBLISHED" },  // Can return to WIP for rework
            ["PUBLISHED"] = new[] { "ARCHIVE" },
            ["ARCHIVE"] = Array.Empty<string>()  // Terminal state
        };

        private static void BulkUpdateCDE(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => !string.IsNullOrEmpty(i.FilePath) && i.Category == "DOCUMENT").ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select documents to update."); return; }

            // Determine valid transitions based on current state of first selected doc
            string currentCDE = selected.FirstOrDefault()?.CDE ?? "WIP";
            var validTargets = CDETransitions.TryGetValue(currentCDE, out string[] targets) ? targets : new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };

            // Check for mixed CDE states in selection
            var distinctStates = selected.Select(s => s.CDE ?? "WIP").Distinct().ToList();
            if (distinctStates.Count > 1)
            {
                // Mixed states — show all options but warn
                validTargets = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
                var mixedMsg = $"Selected documents have mixed CDE states: {string.Join(", ", distinctStates)}.\n\n" +
                    "ISO 19650 recommends transitioning documents with the same CDE state together.\nProceed anyway?";
                if (MessageBox.Show(mixedMsg, "Mixed CDE States", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            else if (validTargets.Length == 0)
            {
                MessageBox.Show($"Documents in '{currentCDE}' state cannot transition further (terminal state).", "CDE Transition");
                return;
            }

            var cdeOptions = validTargets.Select(t =>
            {
                string desc = t switch
                {
                    "WIP" => "WIP — Return to Work In Progress for rework",
                    "SHARED" => "SHARED — Issue for coordination/review (S1-S4)",
                    "PUBLISHED" => "PUBLISHED — Approve and publish (A-codes, contractual)",
                    "ARCHIVE" => "ARCHIVE — Superseded, retained for audit",
                    _ => t
                };
                return desc;
            }).ToList();

            string pick = StingListPicker.Show("Update CDE Status",
                $"Current state: {currentCDE}\nTransition {selected.Count} document(s) to:", cdeOptions);
            if (string.IsNullOrEmpty(pick)) return;
            string newCDE = pick.Split(' ')[0].Trim();

            // Move files to corresponding CDE folder
            string targetFolder = newCDE.ToUpperInvariant() switch
            {
                "WIP" => "WIP", "SHARED" => "SHARED",
                "PUBLISHED" => "PUBLISHED", "ARCHIVE" => "ARCHIVE",
                _ => "WIP"
            };
            int moved = 0;
            var movedPaths = new List<string>();
            foreach (var item in selected)
            {
                if (ProjectFolderEngine.MoveFile(doc, item.FilePath, targetFolder))
                {
                    moved++;
                    movedPaths.Add(item.FilePath);
                    // Log activity for each file
                    ProjectFolderEngine.LogActivity(doc, "CDE_UPDATE", item.Id ?? item.Title,
                        $"Moved to {newCDE}");
                }
            }
            // Auto-generate transmittal when moving to SHARED or PUBLISHED
            ProjectFolderEngine.AutoLogTransmittal(doc, movedPaths, newCDE.ToUpperInvariant());

            // OP-003: Sync document register with new CDE status
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string regPath = Path.Combine(bimDir, "document_register.json");
                if (File.Exists(regPath))
                {
                    var regArr = JArray.Parse(File.ReadAllText(regPath));
                    int synced = 0;
                    foreach (var item in selected)
                    {
                        string docId = item.Id ?? "";
                        var entry = regArr.FirstOrDefault(d => d["doc_id"]?.ToString() == docId);
                        if (entry != null)
                        {
                            string oldCDE = entry["cde_status"]?.ToString() ?? "WIP";
                            string oldSuit = entry["suitability"]?.ToString() ?? "S0";
                            entry["cde_status"] = newCDE.ToUpperInvariant();
                            entry["date"] = DateTime.Now.ToString("yyyy-MM-dd");
                            // Map CDE to default suitability per ISO 19650
                            string suit = newCDE.ToUpperInvariant() switch
                            {
                                "WIP" => "S0",
                                "SHARED" => "S3",       // Fit for review & comment
                                "PUBLISHED" => "S4",    // Fit for stage approval (2021 UK NA)
                                "ARCHIVE" => "AB",      // Abandoned/superseded
                                _ => "S0"
                            };
                            entry["suitability"] = suit;
                            entry["status_code"] = newCDE.ToUpperInvariant() switch
                            {
                                "PUBLISHED" => "IFA",   // Issued for Approval
                                "SHARED" => "IFC",      // Issued for Coordination
                                "ARCHIVE" => "IFR",     // Issued for Record
                                _ => "IFI"              // Issued for Information
                            };
                            // CDE-03: Log suitability transition with audit trail
                            string history = entry["status_history"]?.ToString() ?? "";
                            entry["status_history"] = history +
                                $"|{DateTime.Now:yyyy-MM-dd HH:mm} CDE: {oldCDE}->{newCDE} Suit: {oldSuit}->{suit} by {Environment.UserName}";
                            synced++;
                        }
                    }
                    if (synced > 0)
                        File.WriteAllText(regPath, regArr.ToString(Newtonsoft.Json.Formatting.Indented));
                }
            }
            catch (Exception ex) { StingLog.Warn($"BulkUpdateCDE register sync: {ex.Message}"); }

            MessageBox.Show($"Updated CDE status and moved {moved} files to {targetFolder}." +
                (newCDE == "SHARED" || newCDE == "PUBLISHED" ? "\nAuto-transmittal record created." : ""));
            RefreshData();
        }

        // ── OP-003: Bulk delete sticky notes ──
        private static void BulkDeleteStickyNotes(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => i.Category == "STICKY").ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select sticky notes to delete."); return; }
            if (MessageBox.Show($"Delete {selected.Count} sticky notes?",
                "Bulk Delete Notes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string stickyPath = Path.Combine(bimDir, "sticky_notes.json");
                if (!File.Exists(stickyPath)) return;
                var arr = JArray.Parse(File.ReadAllText(stickyPath));
                int deleted = 0;
                foreach (var item in selected)
                {
                    var note = arr.FirstOrDefault(n => n["note_id"]?.ToString() == item.Id);
                    if (note != null) { arr.Remove(note); deleted++; _allItems.Remove(item); }
                }
                File.WriteAllText(stickyPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "BULK_DELETE_NOTES", $"{deleted}", $"Deleted {deleted} notes");
                MessageBox.Show($"Deleted {deleted} sticky notes.");
                UpdateCounts();
            }
            catch (Exception ex) { StingLog.Warn($"BulkDeleteNotes: {ex.Message}"); }
        }

        // ── OP-005: Transmittal status transition ──
        private static void BulkUpdateTransmittalStatus(Document doc)
        {
            var selected = _listView?.SelectedItems?.Cast<DocItemVM>()
                .Where(i => i.Category == "TRANSMITTAL").ToList();
            if (selected == null || selected.Count == 0) { MessageBox.Show("Select transmittals to update."); return; }
            var statusOptions = ValidTransmittalStatuses.OrderBy(s => s).ToList();
            string newStatus = StingListPicker.Show("Update Transmittal Status",
                $"Set status for {selected.Count} transmittals:", statusOptions);
            if (string.IsNullOrEmpty(newStatus)) return;
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string transPath = Path.Combine(bimDir, "transmittals.json");
                if (!File.Exists(transPath)) return;
                var arr = JArray.Parse(File.ReadAllText(transPath));
                int updated = 0;
                foreach (var item in selected)
                {
                    var trans = arr.FirstOrDefault(t => t["transmittal_id"]?.ToString() == item.Id);
                    if (trans != null)
                    {
                        string oldStatus = trans["status"]?.ToString() ?? "";
                        trans["status"] = newStatus;
                        trans["status_history"] = (trans["status_history"]?.ToString() ?? "")
                            + $"|{DateTime.Now:yyyy-MM-dd HH:mm} {oldStatus}->{newStatus}";
                        updated++;
                    }
                }
                File.WriteAllText(transPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "TRANS_STATUS", $"{updated}", $"→ {newStatus}");
                MessageBox.Show($"Updated {updated} transmittal(s) to {newStatus}.");
                RefreshData();
            }
            catch (Exception ex) { StingLog.Warn($"BulkUpdateTransStatus: {ex.Message}"); }
        }

        // ── DM-05: Inline sticky note creation ──
        private static void CreateInlineStickyNote(Document doc)
        {
            string text = PromptForText("Create Sticky Note", "Enter note text:", "");
            if (string.IsNullOrEmpty(text)) return;

            var categories = new List<string> { "GENERAL", "OBSERVATION", "ACTION", "WARNING", "COORDINATION", "QA" };
            string category = StingListPicker.Show("Note Category", "Select category:", categories);
            if (string.IsNullOrEmpty(category)) category = "GENERAL";

            try
            {
                string bimDir = GetBimManagerDir(doc);
                string stickyPath = Path.Combine(bimDir, "sticky_notes.json");
                JArray arr;
                if (File.Exists(stickyPath))
                    arr = JArray.Parse(File.ReadAllText(stickyPath));
                else
                    arr = new JArray();

                string user = "";
                try { user = doc?.Application?.Username ?? Environment.UserName; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); user = Environment.UserName; }

                string noteId = $"NOTE-{DateTime.Now:yyyyMMdd-HHmmss}";
                arr.Add(new JObject
                {
                    ["note_id"] = noteId,
                    ["text"] = text,
                    ["category"] = category,
                    ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    ["user"] = user,
                    ["element_ids"] = new JArray()
                });
                File.WriteAllText(stickyPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                ProjectFolderEngine.LogActivity(doc, "CREATE_NOTE", noteId, $"{category}: {text.Substring(0, Math.Min(50, text.Length))}");
                RefreshData();
            }
            catch (Exception ex) { StingLog.Warn($"CreateInlineStickyNote: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FOOTER
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildFooter(Window win, DocumentManagementResult result)
        {
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                Padding = new Thickness(12, 5, 12, 5),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 10, Foreground = BrFgSub,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, // UI-04: prevent overflow
                MaxWidth = 900
            };
            System.Windows.Controls.Grid.SetColumn(_statusText, 0);
            grid.Children.Add(_statusText);

            var btnClose = new Button
            {
                Content = "Close", Width = 80, Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                Foreground = BrFgDark, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, FontSize = 11
            };
            btnClose.Click += (s, e) => { result.Confirmed = false; win.DialogResult = false; win.Close(); };
            System.Windows.Controls.Grid.SetColumn(btnClose, 1);
            grid.Children.Add(btnClose);

            footer.Child = grid;
            UpdateStatusText();
            return footer;
        }

        private static void UpdateStatusText()
        {
            if (_statusText == null) return;
            string root = ProjectFolderEngine.RootPath ?? "(not set)";
            int docs = _allItems.Count(i => i.Category == "DOCUMENT");
            int issues = _allItems.Count(i => i.Category == "ISSUE");
            int open = _allItems.Count(i => i.Category == "ISSUE" && i.Status == "OPEN");
            int overdue = _allItems.Count(i => i.Category == "ISSUE" && i.IsOverdue);
            int revs = _allItems.Count(i => i.Category == "REVISION");
            int clashes = _allItems.Count(i => i.Category == "CLASH");
            string overdueStr = overdue > 0 ? $"  OVERDUE: {overdue}" : "";
            _statusText.Text = $"Root: {root}  |  {docs} docs  {issues} issues (open: {open}){overdueStr}  {revs} revisions  {clashes} clashes  |  Total: {_allItems.Count}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATA LOADING (Enhanced: aging, SLA, sticky notes, model health)
        // ══════════════════════════════════════════════════════════════════

        private static void LoadAllData(Document doc)
        {
            _allItems.Clear();
            LoadProjectFiles(doc);
            LoadDocumentRegister(doc);
            LoadIssues(doc);          // Enhanced with aging/SLA
            LoadRevisions(doc);
            LoadClashData(doc);
            LoadTransmittals(doc);
            LoadComplianceData(doc);
            LoadStickyNotes(doc);     // GAP DM-05
            LoadModelHealthTrend(doc); // GAP DM-06
            LoadActivityLog(doc);      // Activity feed
            LoadDataDropStatus(doc);   // Data drop milestones
            LoadBEPData(doc);          // BEP documents
            LoadExportIndex(doc);      // STING_Exports indexing
            LinkIssuesAndRevisions();  // CROSS-01: Issue ↔ Revision join
        }

        private static void LoadProjectFiles(Document doc)
        {
            try
            {
                var files = ProjectFolderEngine.GetAllFiles(doc);
                foreach (var f in files)
                {
                    _allItems.Add(new DocItemVM
                    {
                        Id = Path.GetFileNameWithoutExtension(f.FileName),
                        Title = f.FileName,
                        Type = f.Extension, TypeDesc = f.Extension,
                        CDE = f.CDEStatus,
                        Folder = f.FolderName, FolderId = f.FolderId,
                        FilePath = f.FilePath, FileFormat = f.Extension,
                        Size = f.SizeDisplay,
                        Date = f.Modified.ToString("yyyy-MM-dd HH:mm"),
                        Category = "DOCUMENT", Direction = "OUT"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadFiles: {ex.Message}"); }
        }

        private static void LoadDocumentRegister(Document doc)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string regPath = Path.Combine(bimDir, "document_register.json");
                if (!File.Exists(regPath)) return;

                var arr = JArray.Parse(File.ReadAllText(regPath));
                foreach (JToken d in arr)
                {
                    string docId = d["doc_id"]?.ToString() ?? "";
                    if (_allItems.Any(i => i.Id == docId)) continue;

                    string statusCode = d["status_code"]?.ToString() ?? "";
                    string statusDesc = BIMManager.DocStatusCodes.All.TryGetValue(statusCode, out string sd) ? sd : statusCode;
                    string docType = d["doc_type"]?.ToString() ?? "";
                    string typeDesc = BIMManager.BIMManagerEngine.DocumentTypes.TryGetValue(docType, out string td) ? td : docType;

                    _allItems.Add(new DocItemVM
                    {
                        Id = docId, Title = d["title"]?.ToString() ?? docId,
                        Type = docType, TypeDesc = typeDesc,
                        Status = statusCode, StatusDesc = statusDesc,
                        CDE = d["cde_status"]?.ToString() ?? "WIP",
                        Revision = d["revision"]?.ToString() ?? "",
                        Date = d["date"]?.ToString() ?? "",
                        Direction = d["direction"]?.ToString() ?? "OUT",
                        FilePath = d["file_path"]?.ToString() ?? "",
                        FileFormat = d["file_format"]?.ToString() ?? "",
                        Suitability = d["suitability"]?.ToString() ?? "",
                        CreatedBy = d["created_by"]?.ToString() ?? "",
                        Category = "DOCUMENT", Folder = "15_REGISTERS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadDocReg: {ex.Message}"); }
        }

        private static void LoadIssues(Document doc)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (!File.Exists(issuePath)) return;

                var arr = JArray.Parse(File.ReadAllText(issuePath));
                foreach (JToken issue in arr)
                {
                    string issueType = issue["type"]?.ToString() ?? "RFI";
                    string typeDesc = BIMManager.BIMManagerEngine.IssueTypes.TryGetValue(issueType, out string td) ? td : issueType;
                    string status = issue["status"]?.ToString() ?? "OPEN";
                    string statusDesc = BIMManager.BIMManagerEngine.IssueStatuses.TryGetValue(status, out string sd) ? sd : status;
                    string dateStr = issue["date"]?.ToString() ?? "";
                    string priority = issue["priority"]?.ToString() ?? "MEDIUM";

                    // GAP GRID-02: Compute issue aging & SLA
                    int daysOpen = 0;
                    string aging = "";
                    bool isOverdue = false;
                    string slaDeadline = "";
                    if (DateTime.TryParse(dateStr, out DateTime issueDate) && status != "CLOSED" && status != "VOID")
                    {
                        daysOpen = (int)(DateTime.Now - issueDate).TotalDays;
                        aging = daysOpen < 7 ? $"{daysOpen}d" : daysOpen < 30 ? $"{daysOpen / 7}w" : $"{daysOpen / 30}m";

                        // SLA: CRITICAL=2d, HIGH=5d, MEDIUM=14d, LOW=30d
                        int slaDays = priority switch
                        {
                            "CRITICAL" => 2, "HIGH" => 5, "MEDIUM" => 14, "LOW" => 30, _ => 30
                        };
                        DateTime deadline = issueDate.AddDays(slaDays);
                        slaDeadline = deadline.ToString("yyyy-MM-dd");
                        isOverdue = DateTime.Now > deadline;
                    }

                    // GAP GRID-01: Element count from linked elements
                    int elementCount = 0;
                    if (issue["linked_elements"] is JArray elems) elementCount = elems.Count;

                    // GAP PERSIST-02: Status history
                    string statusHistory = issue["status_history"]?.ToString() ?? "";

                    // GAP CROSS-01: Linked revision
                    string linkedRev = issue["revision"]?.ToString() ?? "";

                    _allItems.Add(new DocItemVM
                    {
                        Id = issue["issue_id"]?.ToString() ?? "",
                        Title = issue["title"]?.ToString() ?? "(untitled)",
                        Type = issueType, TypeDesc = typeDesc,
                        Status = status, StatusDesc = statusDesc,
                        Revision = linkedRev,
                        Date = dateStr,
                        Priority = priority,
                        AssignedTo = issue["assigned_to"]?.ToString() ?? "",
                        Discipline = issue["discipline"]?.ToString() ?? "",
                        Category = "ISSUE", Folder = "11_ISSUES",
                        DaysOpen = daysOpen, Aging = aging,
                        IsOverdue = isOverdue, SLADeadline = slaDeadline,
                        ElementCount = elementCount,
                        StatusHistory = statusHistory,
                        LinkedRevision = linkedRev
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadIssues: {ex.Message}"); }
        }

        private static void LoadRevisions(Document doc)
        {
            if (doc == null) return;
            try
            {
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderBy(r => r.SequenceNumber);

                foreach (var rev in revisions)
                {
                    string revNum = "";
                    try { revNum = rev.RevisionNumber; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    string desc = "";
                    try { desc = rev.Description; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    string date = "";
                    try { date = rev.RevisionDate; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    string issuedBy = "";
                    try { issuedBy = rev.IssuedBy; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    string issuedTo = "";
                    try { issuedTo = rev.IssuedTo; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                    // GAP GRID-07: Count revision clouds
                    int cloudCount = 0;
                    try
                    {
                        cloudCount = new FilteredElementCollector(doc)
                            .OfClass(typeof(RevisionCloud))
                            .Cast<RevisionCloud>()
                            .Count(c => c.RevisionId == rev.Id);
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                    _allItems.Add(new DocItemVM
                    {
                        Id = $"REV-{rev.SequenceNumber:D3}",
                        Title = $"Rev {revNum}: {desc}",
                        Type = "REV", TypeDesc = "Revision",
                        Status = rev.Issued ? "ISSUED" : "DRAFT",
                        CDE = rev.Issued ? "PUBLISHED" : "WIP",
                        Revision = revNum, Date = date,
                        AssignedTo = $"{issuedBy} -> {issuedTo}",
                        ElementCount = cloudCount, // GAP GRID-07: cloud count
                        Category = "REVISION", Folder = "14_REVISIONS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadRevisions: {ex.Message}"); }
        }

        private static void LoadClashData(Document doc)
        {
            try
            {
                string clashDir = ProjectFolderEngine.GetFolderPath(doc, "CLASHES");
                if (Directory.Exists(clashDir))
                {
                    foreach (string file in Directory.GetFiles(clashDir, "*.*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(file);
                        if (fi.Name.StartsWith(".")) continue;
                        _allItems.Add(new DocItemVM
                        {
                            Id = Path.GetFileNameWithoutExtension(fi.Name),
                            Title = fi.Name,
                            Type = fi.Extension.TrimStart('.').ToUpperInvariant(),
                            TypeDesc = "Clash Report",
                            Status = "ACTIVE", Date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            FilePath = fi.FullName,
                            FileFormat = fi.Extension.TrimStart('.').ToUpperInvariant(),
                            Size = FormatSize(fi.Length),
                            Category = "CLASH", Folder = "12_CLASHES"
                        });
                    }
                }

                // Also load CLASH-type issues
                string bimDir = GetBimManagerDir(doc);
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (File.Exists(issuePath))
                {
                    var arr = JArray.Parse(File.ReadAllText(issuePath));
                    foreach (JToken issue in arr.Where(i => i["type"]?.ToString() == "CLASH"))
                    {
                        if (_allItems.Any(i => i.Id == issue["issue_id"]?.ToString())) continue;
                        _allItems.Add(new DocItemVM
                        {
                            Id = issue["issue_id"]?.ToString() ?? "",
                            Title = issue["title"]?.ToString() ?? "(clash)",
                            Type = "CLASH", TypeDesc = "Coordination Clash",
                            Status = issue["status"]?.ToString() ?? "OPEN",
                            Priority = issue["priority"]?.ToString() ?? "HIGH",
                            Date = issue["date"]?.ToString() ?? "",
                            Category = "CLASH", Folder = "12_CLASHES"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadClash: {ex.Message}"); }
        }

        private static void LoadTransmittals(Document doc)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string transPath = Path.Combine(bimDir, "transmittals.json");
                if (!File.Exists(transPath)) return;

                var arr = JArray.Parse(File.ReadAllText(transPath));
                foreach (JToken t in arr)
                {
                    // GAP GRID-04: transmittal contents count
                    int docCount = 0;
                    if (t["documents"] is JArray docs) docCount = docs.Count;

                    // GRID-02: Compute age for transmittals too
                    string tDateStr = t["date"]?.ToString() ?? "";
                    int tDaysOpen = 0;
                    string tAging = "";
                    if (DateTime.TryParse(tDateStr, out DateTime tDate))
                    {
                        tDaysOpen = (int)(DateTime.Now - tDate).TotalDays;
                        tAging = tDaysOpen < 7 ? $"{tDaysOpen}d" : tDaysOpen < 30 ? $"{tDaysOpen / 7}w" : $"{tDaysOpen / 30}m";
                    }

                    _allItems.Add(new DocItemVM
                    {
                        Id = t["transmittal_id"]?.ToString() ?? "",
                        Title = BuildTransmittalTitle(t, docCount),
                        Type = "TR", TypeDesc = "Transmittal",
                        Status = ValidateTransmittalStatus(t["status"]?.ToString()),
                        StatusHistory = t["status_history"]?.ToString() ?? "", // PERSIST-02
                        CDE = "SHARED",
                        Revision = t["revision"]?.ToString() ?? "",
                        Date = tDateStr,
                        AssignedTo = t["recipient"]?.ToString() ?? "",
                        CreatedBy = t["created_by"]?.ToString() ?? "",
                        ElementCount = docCount,
                        DaysOpen = tDaysOpen, Aging = tAging, // GRID-02
                        Category = "TRANSMITTAL", Folder = "10_TRANSMITTALS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadTrans: {ex.Message}"); }
        }

        private static void LoadComplianceData(Document doc)
        {
            if (doc == null || _complianceResult == null) return;
            try
            {
                var scan = _complianceResult;
                _allItems.Add(new DocItemVM
                {
                    Id = "COMPLIANCE-LIVE",
                    Title = $"Live: {scan.CompliancePercent:F0}% ({scan.RAGStatus}) Strict: {scan.StrictPercent:F0}%",
                    Type = "RPT", TypeDesc = "Compliance Report",
                    Status = scan.RAGStatus, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Category = "COMPLIANCE", Folder = "16_COMPLIANCE"
                });

                if (scan.ByDisc != null)
                {
                    foreach (var kv in scan.ByDisc.OrderBy(x => x.Key))
                    {
                        _allItems.Add(new DocItemVM
                        {
                            Id = $"COMP-{kv.Key}",
                            Title = $"{kv.Key}: {kv.Value.CompliancePct:F0}% ({kv.Value.Tagged}/{kv.Value.Total}) Missing: LOC={kv.Value.MissingLoc} SYS={kv.Value.MissingSys} PROD={kv.Value.MissingProd}",
                            Type = "RPT", TypeDesc = "Discipline Compliance",
                            Status = kv.Value.CompliancePct >= 80 ? "GREEN" : kv.Value.CompliancePct >= 50 ? "AMBER" : "RED",
                            Discipline = kv.Key, Date = DateTime.Now.ToString("yyyy-MM-dd"),
                            Category = "COMPLIANCE", Folder = "16_COMPLIANCE"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadCompliance: {ex.Message}"); }
        }

        // GAP DM-05: Load sticky notes
        private static void LoadStickyNotes(Document doc)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string stickyPath = Path.Combine(bimDir, "sticky_notes.json");
                if (!File.Exists(stickyPath)) return;

                var arr = JArray.Parse(File.ReadAllText(stickyPath));
                foreach (JToken note in arr)
                {
                    // GRID-01: element count for sticky notes
                    int noteElemCount = 0;
                    if (note["element_ids"] is JArray noteElems) noteElemCount = noteElems.Count;

                    _allItems.Add(new DocItemVM
                    {
                        Id = note["note_id"]?.ToString() ?? "",
                        Title = note["text"]?.ToString() ?? "(note)",
                        Type = "NOTE", TypeDesc = "Sticky Note",
                        Status = note["category"]?.ToString() ?? "GENERAL",
                        Date = note["date"]?.ToString() ?? "",
                        ElementCount = noteElemCount,
                        CreatedBy = note["user"]?.ToString() ?? "",
                        Category = "STICKY", Folder = "20_MISC"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadSticky: {ex.Message}"); }
        }

        // GAP DM-06: Load model health trend
        private static void LoadModelHealthTrend(Document doc)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string healthPath = Path.Combine(bimDir, "model_health.json");
                if (!File.Exists(healthPath)) return;

                var obj = JObject.Parse(File.ReadAllText(healthPath));
                if (obj["trend"] is JArray trend)
                {
                    foreach (JToken entry in trend.Reverse().Take(5)) // Last 5 entries
                    {
                        _allItems.Add(new DocItemVM
                        {
                            Id = $"HEALTH-{entry["date"]}",
                            Title = $"Health: {entry["score"]}% ({entry["status"]})",
                            Type = "RPT", TypeDesc = "Model Health",
                            Status = entry["status"]?.ToString() ?? "",
                            Date = entry["date"]?.ToString() ?? "",
                            Category = "COMPLIANCE", Folder = "16_COMPLIANCE"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadHealth: {ex.Message}"); }
        }

        // Activity feed loading
        private static void LoadActivityLog(Document doc)
        {
            try
            {
                var entries = ProjectFolderEngine.GetRecentActivity(doc, 30);
                foreach (var entry in entries)
                {
                    _allItems.Add(new DocItemVM
                    {
                        Id = $"ACT-{entry.Timestamp}",
                        Title = $"{entry.Action}: {entry.DocId} — {entry.Details}",
                        Type = "LOG", TypeDesc = "Activity",
                        Status = entry.Action,
                        Date = entry.Timestamp,
                        AssignedTo = entry.User,
                        Category = "ACTIVITY", Folder = ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadActivity: {ex.Message}"); }
        }

        // Data drop milestone loading
        private static void LoadDataDropStatus(Document doc)
        {
            try
            {
                var drops = ProjectFolderEngine.CheckAllDataDrops(doc);
                foreach (var dd in drops)
                {
                    string status = dd.ReadyPercent >= 100 ? "READY" : dd.ReadyPercent >= 50 ? "PARTIAL" : "NOT READY";
                    string missingItems = string.Join(", ",
                        dd.Items.Where(i => !i.HasFiles).Select(i => i.ExportType));

                    _allItems.Add(new DocItemVM
                    {
                        Id = dd.DataDropId,
                        Title = $"{dd.DataDropId}: {dd.Stage} — {dd.ReadyPercent:F0}% ready" +
                            (string.IsNullOrEmpty(missingItems) ? "" : $" (missing: {missingItems})"),
                        Type = "DD", TypeDesc = "Data Drop",
                        Status = status,
                        Date = "",
                        Category = "DATADROP", Folder = ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadDataDrop: {ex.Message}"); }
        }

        // Load BEP documents from project folder
        private static void LoadBEPData(Document doc)
        {
            try
            {
                // Check BIM manager dir for project_bep.json
                string bimDir = GetBimManagerDir(doc);
                string bepPath = Path.Combine(bimDir, "project_bep.json");
                if (File.Exists(bepPath))
                {
                    var fi = new FileInfo(bepPath);
                    _allItems.Add(new DocItemVM
                    {
                        Id = "BEP-PROJECT",
                        Title = $"Project BEP ({FormatSize(fi.Length)})",
                        Type = "BEP", TypeDesc = "BIM Execution Plan",
                        Status = "ACTIVE", CDE = "PUBLISHED",
                        Date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        FilePath = bepPath, FileFormat = "JSON",
                        Size = FormatSize(fi.Length),
                        Category = "BEP", Folder = "09_BEP"
                    });
                }

                // Also scan BEP folder in project structure
                string bepFolder = ProjectFolderEngine.GetFolderPath(doc, "BEP");
                if (Directory.Exists(bepFolder))
                {
                    foreach (string file in Directory.GetFiles(bepFolder, "*.*", SearchOption.AllDirectories))
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Name.StartsWith(".")) continue;
                        if (_allItems.Any(i => i.FilePath == file)) continue;
                        _allItems.Add(new DocItemVM
                        {
                            Id = $"BEP-{Path.GetFileNameWithoutExtension(fileInfo.Name)}",
                            Title = fileInfo.Name,
                            Type = "BEP", TypeDesc = "BIM Execution Plan",
                            Status = "ACTIVE",
                            Date = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            FilePath = file, FileFormat = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                            Size = FormatSize(fileInfo.Length),
                            Category = "BEP", Folder = "09_BEP"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadBEPData: {ex.Message}"); }
        }

        // Index all files in STING_Exports folder (legacy exports not in project folder)
        private static void LoadExportIndex(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return;
                string projDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(projDir)) return;
                string exportsDir = Path.Combine(projDir, "STING_Exports");
                if (!Directory.Exists(exportsDir)) return;

                foreach (string file in Directory.GetFiles(exportsDir, "*.*", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(file);
                    if (fi.Name.StartsWith(".")) continue;
                    if (_allItems.Any(i => i.FilePath == file)) continue;

                    string ext = fi.Extension.TrimStart('.').ToUpperInvariant();
                    string cat = "DOCUMENT";
                    if (ext == "CSV" || ext == "XLSX") cat = "DOCUMENT";

                    _allItems.Add(new DocItemVM
                    {
                        Id = Path.GetFileNameWithoutExtension(fi.Name),
                        Title = fi.Name,
                        Type = ext, TypeDesc = $"Export ({ext})",
                        Status = "IFI", CDE = "WIP",
                        Date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        FilePath = file, FileFormat = ext,
                        Size = FormatSize(fi.Length),
                        Category = cat, Folder = "STING_Exports",
                        FolderId = "MISC"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadExportIndex: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FILTERING (Enhanced with time/discipline/priority/overdue)
        // ══════════════════════════════════════════════════════════════════

        private static bool FilterItem(object obj)
        {
            if (obj is not DocItemVM item) return false;

            // Text search
            if (!string.IsNullOrEmpty(_searchText))
            {
                string s = _searchText;
                bool match = Contains(item.Title, s) || Contains(item.Id, s)
                    || Contains(item.Type, s) || Contains(item.TypeDesc, s)
                    || Contains(item.Status, s) || Contains(item.StatusDesc, s)
                    || Contains(item.Folder, s) || Contains(item.AssignedTo, s)
                    || Contains(item.Priority, s) || Contains(item.Discipline, s)
                    || Contains(item.Revision, s) || Contains(item.SLADeadline, s);
                if (!match) return false;
            }

            if (_currentFilter == "ALL") return true;

            if (_currentFilter.StartsWith("CAT:"))
                return Eq(item.Category, _currentFilter.Substring(4));
            if (_currentFilter.StartsWith("CDE:"))
                return Eq(item.CDE, _currentFilter.Substring(4));
            if (_currentFilter.StartsWith("STATUS:"))
                return Eq(item.Status, _currentFilter.Substring(7));
            if (_currentFilter.StartsWith("FOLDER:"))
                return Eq(item.FolderId, _currentFilter.Substring(7));
            if (_currentFilter.StartsWith("ISSUE:"))
                return item.Category == "ISSUE" && Eq(item.Type, _currentFilter.Substring(6));
            if (_currentFilter.StartsWith("REV:"))
                return item.Category == "REVISION" && Eq(item.Revision, _currentFilter.Substring(4));
            if (_currentFilter.StartsWith("DISC:"))
                return Eq(item.Discipline, _currentFilter.Substring(5));
            if (_currentFilter.StartsWith("PRIORITY:"))
                return item.Category == "ISSUE" && Eq(item.Priority, _currentFilter.Substring(9));
            if (_currentFilter.StartsWith("ISSUESTATUS:"))
                return item.Category == "ISSUE" && Eq(item.Status, _currentFilter.Substring(12));
            if (_currentFilter == "OVERDUE")
                return item.IsOverdue;
            if (_currentFilter.StartsWith("DD:"))
                return item.Category == "DATADROP" && Eq(item.Id, _currentFilter.Substring(3));
            if (_currentFilter.StartsWith("CLASHDISC:"))
                return item.Category == "CLASH" && Eq(item.Discipline, _currentFilter.Substring(10));
            if (_currentFilter.StartsWith("STICKYCAT:"))
                return item.Category == "STICKY" && Eq(item.Status, _currentFilter.Substring(10));

            // Time-based filters (GAP NAV-02)
            if (_currentFilter == "TIME:TODAY") return IsToday(item.Date);
            if (_currentFilter == "TIME:WEEK") return IsThisWeek(item.Date);
            if (_currentFilter == "TIME:MONTH") return IsThisMonth(item.Date);
            if (_currentFilter == "TIME:OLDER")
                return !string.IsNullOrEmpty(item.Date) && !IsThisMonth(item.Date);

            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static void RefreshData()
        {
            if (_doc == null) return;
            try { _complianceResult = ComplianceScan.Scan(_doc); }
            catch (Exception ex) { StingLog.Warn($"DocMgr refresh scan: {ex.Message}"); }
            _allItems.Clear();
            LoadAllData(_doc);
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_allItems);
            _view.Filter = FilterItem;
            if (_listView != null) _listView.ItemsSource = _view;
            PopulateTree();
            RefreshDashboard(_doc);
            UpdateCounts();
            UpdateStatusText();
        }

        private static void UpdateCounts()
        {
            int total = _allItems.Count;
            int visible = _view?.Cast<object>().Count() ?? 0;
            if (_countText != null) _countText.Text = $"{visible} of {total}";
            UpdateStatusText();
        }

        private static string GetBimManagerDir(Document doc)
        {
            string projDir = "";
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                projDir = Path.GetDirectoryName(doc.PathName) ?? "";
            string bimDir = Path.Combine(projDir, "STING_BIM_MANAGER");
            if (!Directory.Exists(bimDir))
            {
                try { Directory.CreateDirectory(bimDir); }
                catch (Exception ex) { StingLog.Warn($"DocMgr dir: {ex.Message}"); }
            }
            return bimDir;
        }

        private static string FormatSize(long bytes) => ProjectFolderEngine.FormatSize(bytes);

        private static bool Contains(string val, string search)
        {
            return !string.IsNullOrEmpty(val) && val.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Eq(string val, string target)
        {
            return (val ?? "").Equals(target, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsToday(string dateStr)
        {
            return DateTime.TryParse(dateStr, out DateTime d) && d.Date == DateTime.Today;
        }

        private static bool IsThisWeek(string dateStr)
        {
            if (!DateTime.TryParse(dateStr, out DateTime d)) return false;
            var diff = (DateTime.Today - d.Date).TotalDays;
            return diff >= 0 && diff < 7;
        }

        private static bool IsThisMonth(string dateStr)
        {
            if (!DateTime.TryParse(dateStr, out DateTime d)) return false;
            return d.Year == DateTime.Today.Year && d.Month == DateTime.Today.Month;
        }

        private static string PromptForText(string title, string prompt, string defaultValue)
        {
            var win = new Window
            {
                Title = title, Width = 420, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = prompt, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            var tb = new System.Windows.Controls.TextBox
            {
                Text = defaultValue, FontSize = 11, Padding = new Thickness(4, 3, 4, 3)
            };
            stack.Children.Add(tb);
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var btnOk = new Button
            {
                Content = "OK", Width = 70, Height = 26,
                Background = BrAccent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            btnOk.Click += (s, e) => { win.DialogResult = true; win.Close(); };
            var btnCancel = new Button
            {
                Content = "Cancel", Width = 70, Height = 26,
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { win.DialogResult = false; win.Close(); };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            stack.Children.Add(btnPanel);
            win.Content = stack;
            tb.SelectAll(); tb.Focus();
            return win.ShowDialog() == true ? tb.Text : null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI-03: Tree search helpers
        // ══════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════
        //  RIGHT-CLICK CONTEXT MENU (copy, file ops, status transitions)
        // ══════════════════════════════════════════════════════════════════

        private static ContextMenu BuildContextMenu(Document doc, Window win)
        {
            var menu = new ContextMenu();

            // ── Clipboard ──
            menu.Items.Add(MakeMenuItem("Copy Title", "Copy document title to clipboard", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item)
                    Clipboard.SetText(item.Title ?? "");
            }));
            menu.Items.Add(MakeMenuItem("Copy ID", "Copy document ID to clipboard", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item)
                    Clipboard.SetText(item.Id ?? "");
            }));
            menu.Items.Add(MakeMenuItem("Copy File Path", "Copy full file path to clipboard", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath))
                    Clipboard.SetText(item.FilePath);
            }));
            menu.Items.Add(MakeMenuItem("Copy Row as CSV", "Copy all columns as comma-separated text", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item)
                {
                    string csv = $"{item.Type},{item.Id},{item.Title},{item.Status},{item.CDE}," +
                        $"{item.Revision},{item.Discipline},{item.Folder},{item.FileFormat}," +
                        $"{item.Size},{item.Date},{item.Priority},{item.AssignedTo}";
                    Clipboard.SetText(csv);
                }
            }));
            menu.Items.Add(MakeMenuItem("Copy All Visible as CSV", "Export all visible rows to clipboard as CSV", (s, e) =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Type,ID,Title,Status,CDE,Rev,Disc,Folder,Format,Size,Date,Priority,Age,Elements,Assigned,SLA");
                foreach (var obj in _view)
                {
                    if (obj is DocItemVM it)
                        sb.AppendLine($"\"{it.Type}\",\"{it.Id}\",\"{it.Title}\",\"{it.Status}\",\"{it.CDE}\"," +
                            $"\"{it.Revision}\",\"{it.Discipline}\",\"{it.Folder}\",\"{it.FileFormat}\"," +
                            $"\"{it.Size}\",\"{it.Date}\",\"{it.Priority}\",\"{it.Aging}\",\"{it.ElementCount}\"," +
                            $"\"{it.AssignedTo}\",\"{it.SLADeadline}\"");
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show($"Copied {_view.Count} rows to clipboard.", "STING", MessageBoxButton.OK, MessageBoxImage.Information);
            }));

            menu.Items.Add(new Separator());

            // ── File operations ──
            menu.Items.Add(MakeMenuItem("Open File", "Open in default application", (s, e) => OpenSelected()));
            menu.Items.Add(MakeMenuItem("Open Containing Folder", "Show in Windows Explorer", (s, e) => OpenFolder(doc)));
            menu.Items.Add(MakeMenuItem("Copy File to...", "Copy file to another location", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
                if (!File.Exists(item.FilePath)) return;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Copy file to...",
                    FileName = Path.GetFileName(item.FilePath),
                    Filter = "All files|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    File.Copy(item.FilePath, dlg.FileName, true);
                    ProjectFolderEngine.LogActivity(doc, "COPY_FILE", Path.GetFileName(item.FilePath), dlg.FileName);
                }
            }));
            menu.Items.Add(MakeMenuItem("Rename...", "Rename this file", (s, e) => RenameSelected()));
            menu.Items.Add(MakeMenuItem("Move To Folder...", "Move to different project folder", (s, e) => MoveSelected(doc)));
            menu.Items.Add(MakeMenuItem("Delete (Recycle)", "Move to recycle bin", (s, e) => DeleteSelected()));
            menu.Items.Add(MakeMenuItem("Auto-correct Name", "Auto-rename to ISO 19650 compliant format", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
                string currentName = Path.GetFileName(item.FilePath);
                string corrected = ProjectFolderEngine.AutoCorrectFileName(doc, currentName);
                if (corrected == currentName) { MessageBox.Show("Filename is already ISO 19650 compliant.", "STING"); return; }
                if (MessageBox.Show($"Auto-correct filename?\n\nBefore: {currentName}\nAfter:  {corrected}",
                    "Auto-correct", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    if (ProjectFolderEngine.RenameFile(item.FilePath, corrected))
                    {
                        ProjectFolderEngine.LogActivity(doc, "AUTO_RENAME", item.Id ?? "", $"{currentName} -> {corrected}");
                        RefreshData();
                    }
                }
            }));
            menu.Items.Add(MakeMenuItem("Restore from Recycle", "Recover deleted files", (s, e) => RestoreFromRecycle(doc)));

            menu.Items.Add(new Separator());

            // ── CDE / Status ──
            var cdeMenu = new MenuItem { Header = "Set CDE Status" };
            foreach (string cde in new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" })
            {
                string c = cde;
                cdeMenu.Items.Add(MakeMenuItem(c, $"Move to {c} folder and update register", (s, e) =>
                {
                    if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath))
                    {
                        ProjectFolderEngine.MoveFile(doc, item.FilePath, c);
                        RefreshData();
                    }
                }));
            }
            menu.Items.Add(cdeMenu);

            var statusMenu = new MenuItem { Header = "Set Document Status" };
            foreach (var kv in BIMManager.DocStatusCodes.All.Take(20))
            {
                string code = kv.Key;
                statusMenu.Items.Add(MakeMenuItem($"{code} — {kv.Value}", $"Set status to {code}", (s, e) =>
                {
                    if (_listView?.SelectedItem is DocItemVM item)
                    {
                        UpdateDocRegisterField(doc, item.Id, "status_code", code);
                        item.Status = code;
                        item.StatusDesc = kv.Value;
                        _view?.Refresh();
                    }
                }));
            }
            menu.Items.Add(statusMenu);

            var suitMenu = new MenuItem { Header = "Set Suitability Code" };
            foreach (var kv in BIMManager.BIMManagerEngine.SuitabilityCodes)
            {
                string code = kv.Key;
                suitMenu.Items.Add(MakeMenuItem($"{code} — {kv.Value}", "", (s, e) =>
                {
                    if (_listView?.SelectedItem is DocItemVM item)
                    {
                        UpdateDocRegisterField(doc, item.Id, "suitability", code);
                        item.Suitability = code;
                        _view?.Refresh();
                    }
                }));
            }
            menu.Items.Add(suitMenu);

            menu.Items.Add(new Separator());

            // ── Issue operations ──
            menu.Items.Add(MakeMenuItem("Link to Revision...", "Associate this issue with a revision", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || item.Category != "ISSUE") return;
                var revItems = _allItems.Where(i => i.Category == "REVISION")
                    .Select(i => $"{i.Id}: {i.Title}").ToList();
                if (revItems.Count == 0) { MessageBox.Show("No revisions found."); return; }
                string pick = StingListPicker.Show("Link to Revision", "Select revision:", revItems);
                if (string.IsNullOrEmpty(pick)) return;
                string revId = pick.Split(':')[0].Trim();
                item.LinkedRevision = revId;
                UpdateIssueField(doc, item.Id, "revision", revId);
                _view?.Refresh();
            }));
            menu.Items.Add(MakeMenuItem("Change Priority...", "Update issue priority", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || item.Category != "ISSUE") return;
                var priorities = new List<string> { "CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO" };
                string pick = StingListPicker.Show("Change Priority", "Select new priority:", priorities);
                if (string.IsNullOrEmpty(pick)) return;
                item.Priority = pick;
                UpdateIssueField(doc, item.Id, "priority", pick);
                _view?.Refresh();
            }));
            menu.Items.Add(MakeMenuItem("Assign To...", "Assign issue to team member", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || item.Category != "ISSUE") return;
                string name = PromptForText("Assign To", "Enter assignee name:", item.AssignedTo ?? "");
                if (!string.IsNullOrEmpty(name))
                {
                    item.AssignedTo = name;
                    UpdateIssueField(doc, item.Id, "assigned_to", name);
                    _view?.Refresh();
                }
            }));
            menu.Items.Add(MakeMenuItem("Close Issue", "Set status to CLOSED", (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || item.Category != "ISSUE") return;
                UpdateIssueField(doc, item.Id, "status", "CLOSED");
                item.Status = "CLOSED";
                _view?.Refresh();
            }));

            menu.Items.Add(new Separator());

            // ── Edit/View ──
            menu.Items.Add(MakeMenuItem("Edit Note...", "Edit sticky note text", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item && item.Category == "STICKY")
                    EditStickyNote(item);
            }));
            menu.Items.Add(MakeMenuItem("View Details", "Show full item details", (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"ID:          {item.Id}");
                    sb.AppendLine($"Title:       {item.Title}");
                    sb.AppendLine($"Type:        {item.Type} ({item.TypeDesc})");
                    sb.AppendLine($"Status:      {item.Status} ({item.StatusDesc})");
                    sb.AppendLine($"CDE:         {item.CDE}");
                    sb.AppendLine($"Suitability: {item.Suitability}");
                    sb.AppendLine($"Revision:    {item.Revision}");
                    sb.AppendLine($"Date:        {item.Date}");
                    sb.AppendLine($"Discipline:  {item.Discipline}");
                    sb.AppendLine($"Priority:    {item.Priority}");
                    sb.AppendLine($"Assigned To: {item.AssignedTo}");
                    sb.AppendLine($"Created By:  {item.CreatedBy}");
                    sb.AppendLine($"Folder:      {item.Folder}");
                    sb.AppendLine($"File:        {item.FilePath}");
                    sb.AppendLine($"Format:      {item.FileFormat}");
                    sb.AppendLine($"Size:        {item.Size}");
                    sb.AppendLine($"Age:         {item.Aging} ({item.DaysOpen} days)");
                    sb.AppendLine($"SLA:         {item.SLADeadline} {(item.IsOverdue ? "OVERDUE" : "")}");
                    sb.AppendLine($"Elements:    {item.ElementCount}");
                    sb.AppendLine($"Linked Rev:  {item.LinkedRevision}");
                    sb.AppendLine($"Linked Issues: {item.LinkedIssues}");
                    if (!string.IsNullOrEmpty(item.StatusHistory))
                        sb.AppendLine($"\nStatus History:\n{item.StatusHistory.Replace("|", "\n")}");
                    MessageBox.Show(sb.ToString(), "STING Document Details", MessageBoxButton.OK);
                }
            }));

            return menu;
        }

        private static MenuItem MakeMenuItem(string header, string tooltip, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header };
            if (!string.IsNullOrEmpty(tooltip)) item.ToolTip = tooltip;
            item.Click += handler;
            return item;
        }

        // ── JSON field update helpers for right-click operations ──

        private static void UpdateDocRegisterField(Document doc, string docId, string field, string value)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string regPath = Path.Combine(bimDir, "document_register.json");
                if (!File.Exists(regPath)) return;
                var arr = JArray.Parse(File.ReadAllText(regPath));
                var entry = arr.FirstOrDefault(d => d["doc_id"]?.ToString() == docId);
                if (entry != null)
                {
                    entry[field] = value;
                    File.WriteAllText(regPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    ProjectFolderEngine.LogActivity(doc, "UPDATE_DOC", docId, $"{field}={value}");
                }
            }
            catch (Exception ex) { StingLog.Warn($"UpdateDocRegister: {ex.Message}"); }
        }

        private static void UpdateIssueField(Document doc, string issueId, string field, string value)
        {
            try
            {
                string bimDir = GetBimManagerDir(doc);
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (!File.Exists(issuePath)) return;
                var arr = JArray.Parse(File.ReadAllText(issuePath));
                var entry = arr.FirstOrDefault(i => i["issue_id"]?.ToString() == issueId);
                if (entry != null)
                {
                    string old = entry[field]?.ToString() ?? "";
                    entry[field] = value;
                    entry["status_history"] = (entry["status_history"]?.ToString() ?? "")
                        + $"|{DateTime.Now:yyyy-MM-dd HH:mm} {field}: {old}->{value}";
                    File.WriteAllText(issuePath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    ProjectFolderEngine.LogActivity(doc, "UPDATE_ISSUE", issueId, $"{field}={value}");
                }
            }
            catch (Exception ex) { StingLog.Warn($"UpdateIssue: {ex.Message}"); }
        }

        // DM-03: Valid transmittal statuses
        private static readonly HashSet<string> ValidTransmittalStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "DRAFT", "SENT", "RECEIVED", "ACKNOWLEDGED", "SIGNED",
            "REJECTED", "SUPERSEDED", "AUTO_GENERATED", "VOID"
        };

        private static string ValidateTransmittalStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return "DRAFT";
            return ValidTransmittalStatuses.Contains(status) ? status : "DRAFT";
        }

        /// <summary>Build transmittal title showing file count and contents summary.</summary>
        private static string BuildTransmittalTitle(JToken t, int docCount)
        {
            string title = t["title"]?.ToString() ?? t["transmittal_id"]?.ToString() ?? "";
            if (docCount > 0)
            {
                // Show file list preview if available
                if (t["documents"] is JArray docs && docs.Count > 0)
                {
                    var fileNames = docs.Take(3).Select(d => d.ToString()).ToList();
                    string preview = string.Join(", ", fileNames);
                    if (docs.Count > 3) preview += $" +{docs.Count - 3} more";
                    return $"{title} ({docCount} files: {preview})";
                }
                return $"{title} ({docCount} files)";
            }
            return title;
        }

        private static void SetAllTreeItemsVisible(ItemsControl parent, bool visible)
        {
            foreach (var item in parent.Items)
            {
                if (item is TreeViewItem tvi)
                {
                    tvi.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    SetAllTreeItemsVisible(tvi, visible);
                }
            }
        }

        private static bool FilterTreeItems(ItemsControl parent, string search)
        {
            bool anyVisible = false;
            foreach (var item in parent.Items)
            {
                if (item is TreeViewItem tvi)
                {
                    string header = tvi.Header?.ToString() ?? "";
                    bool childVisible = FilterTreeItems(tvi, search);
                    bool selfMatch = header.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool show = selfMatch || childVisible;
                    tvi.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show)
                    {
                        anyVisible = true;
                        if (childVisible) tvi.IsExpanded = true;
                    }
                }
            }
            return anyVisible;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CROSS-01: Issue ↔ Revision join (post-load)
        // ══════════════════════════════════════════════════════════════════

        private static void LinkIssuesAndRevisions()
        {
            // Build revision lookup
            var revItems = _allItems.Where(i => i.Category == "REVISION").ToList();
            var issueItems = _allItems.Where(i => i.Category == "ISSUE").ToList();
            if (revItems.Count == 0 || issueItems.Count == 0) return;

            foreach (var issue in issueItems)
            {
                // Link by revision field
                if (!string.IsNullOrEmpty(issue.Revision))
                {
                    var matchingRev = revItems.FirstOrDefault(r =>
                        (r.Revision ?? "").Equals(issue.Revision, StringComparison.OrdinalIgnoreCase));
                    if (matchingRev != null)
                    {
                        issue.LinkedRevision = matchingRev.Id;
                        // Append issue to revision's linked list
                        string existing = matchingRev.LinkedIssues ?? "";
                        matchingRev.LinkedIssues = string.IsNullOrEmpty(existing)
                            ? issue.Id : $"{existing}, {issue.Id}";
                    }
                }

                // Also link by date proximity (issues within 2 days of revision date)
                if (string.IsNullOrEmpty(issue.LinkedRevision) && DateTime.TryParse(issue.Date, out DateTime issDate))
                {
                    var closest = revItems
                        .Where(r => DateTime.TryParse(r.Date, out DateTime rd) && Math.Abs((rd - issDate).TotalDays) <= 2)
                        .OrderBy(r => DateTime.TryParse(r.Date, out DateTime rd2) ? Math.Abs((rd2 - issDate).TotalDays) : 999)
                        .FirstOrDefault();
                    if (closest != null)
                    {
                        issue.LinkedRevision = closest.Id;
                        string ex2 = closest.LinkedIssues ?? "";
                        closest.LinkedIssues = string.IsNullOrEmpty(ex2) ? issue.Id : $"{ex2}, {issue.Id}";
                    }
                }
            }
        }
    }
}
