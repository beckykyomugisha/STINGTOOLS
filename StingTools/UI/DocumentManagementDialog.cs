using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Color = System.Windows.Media.Color;

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
    //  DOCUMENT ITEM VIEW MODEL
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Unified row model for the document grid — covers files, issues, revisions, etc.</summary>
    public class DocItemVM : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }          // DR, SH, RFI, NCR, REV, etc.
        public string TypeDesc { get; set; }
        public string Status { get; set; }         // S0-S7 / OPEN / CLOSED / WIP / etc.
        public string StatusDesc { get; set; }
        public string CDE { get; set; }            // WIP / SHARED / PUBLISHED / ARCHIVE
        public string Revision { get; set; }       // P01, C01
        public string Date { get; set; }
        public string Discipline { get; set; }
        public string Folder { get; set; }
        public string FolderId { get; set; }
        public string FilePath { get; set; }
        public string FileFormat { get; set; }
        public string Size { get; set; }
        public string Direction { get; set; }      // IN / OUT
        public string Priority { get; set; }       // CRITICAL / HIGH / MEDIUM / LOW / INFO
        public string AssignedTo { get; set; }
        public string Category { get; set; }       // grouping category for tree
        public string Icon { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TREE NODE
    // ══════════════════════════════════════════════════════════════════════

    public class NavNode
    {
        public string Label { get; set; }
        public string Tag { get; set; }          // filter key
        public string Icon { get; set; }
        public int Count { get; set; }
        public List<NavNode> Children { get; set; } = new();
        public override string ToString() => Count > 0 ? $"{Icon} {Label} ({Count})" : $"{Icon} {Label}";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ENHANCED DOCUMENT MANAGEMENT DIALOG
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ISO 19650 Document Management Center — comprehensive WPF dialog with:
    ///   - Left: TreeView navigator (folders, issues, revisions, clashes, handover, CDE status)
    ///   - Center: Filterable data grid showing documents/issues/revisions
    ///   - Bottom: Action bar with file operations (open, rename, delete, move, import, drag-to-view)
    ///   - Status bar with file counts and project info
    ///
    /// 10 navigation sections:
    ///   ALL DOCUMENTS | FOLDERS | CDE STATUS | ISSUES | REVISIONS |
    ///   CLASHES | HANDOVER | REGISTERS | TRANSMITTALS | COMPLIANCE
    /// </summary>
    internal static class DocumentManagementDialog
    {
        // ── Theme ─────────────────────────────────────────────────────────
        private static readonly SolidColorBrush BrHeader   = new(Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly SolidColorBrush BrAccent   = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrBg       = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush BrWhite    = Brushes.White;
        private static readonly SolidColorBrush BrFgDark   = new(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush BrFgSub    = new(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush BrBorder   = new(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidColorBrush BrTreeSel  = new(Color.FromRgb(0xE3, 0xF2, 0xFD));
        private static readonly SolidColorBrush BrGreen    = new(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush BrOrange   = new(Color.FromRgb(0xE6, 0x51, 0x00));
        private static readonly SolidColorBrush BrRed      = new(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly SolidColorBrush BrPurple   = new(Color.FromRgb(0x6A, 0x1B, 0x9A));
        private static readonly SolidColorBrush BrTeal     = new(Color.FromRgb(0x00, 0x69, 0x5C));

        // ── State ─────────────────────────────────────────────────────────
        private static ObservableCollection<DocItemVM> _allItems;
        private static ListCollectionView _view;
        private static string _currentFilter = "ALL";
        private static string _searchText = "";
        private static TextBlock _statusText;
        private static TextBlock _countText;
        private static ListView _listView;
        private static TreeView _treeView;
        private static Document _doc;
        private static string _selectedOperation;

        // ══════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════════════

        public static DocumentManagementResult Show(Document doc)
        {
            _doc = doc;
            _selectedOperation = null;
            _allItems = new ObservableCollection<DocItemVM>();
            var result = new DocumentManagementResult();

            // Load data
            LoadAllData(doc);

            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_allItems);
            _view.Filter = FilterItem;

            // ── Window ────────────────────────────────────────────────
            var win = new Window
            {
                Title = "STING Document Management Center",
                Width = 1200, Height = 780,
                MinWidth = 900, MinHeight = 600,
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
            DockPanel.SetDock(BuildHeader(doc), Dock.Top);
            root.Children.Add(BuildHeader(doc));

            // Footer / status bar
            var footer = BuildFooter(win, result);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Action bar
            var actionBar = BuildActionBar(doc, win);
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            // Main content: TreeView left + ListView right
            var splitter = new System.Windows.Controls.Grid();
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Tree navigator
            var leftPanel = BuildTreePanel(doc);
            System.Windows.Controls.Grid.SetColumn(leftPanel, 0);
            splitter.Children.Add(leftPanel);

            // Splitter
            var gridSplitter = new GridSplitter
            {
                Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder
            };
            System.Windows.Controls.Grid.SetColumn(gridSplitter, 1);
            splitter.Children.Add(gridSplitter);

            // Right: Document list + search
            var rightPanel = BuildDocumentPanel();
            System.Windows.Controls.Grid.SetColumn(rightPanel, 2);
            splitter.Children.Add(rightPanel);

            root.Children.Add(splitter);
            win.Content = root;

            // Show
            bool? dialogResult = win.ShowDialog();
            if (dialogResult == true && _selectedOperation != null)
            {
                result.Confirmed = true;
                result.Operation = _selectedOperation;
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HEADER
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildHeader(Document doc)
        {
            var header = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 10, 16, 10)
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
            try { projName = doc?.ProjectInformation?.Name ?? ""; } catch { }
            left.Children.Add(new TextBlock
            {
                Text = $"Project: {projName}  |  ISO 19650 Compliant",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            System.Windows.Controls.Grid.SetColumn(left, 0);
            g.Children.Add(left);

            // Quick action buttons in header
            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
            rightBtns.Children.Add(MakeHeaderBtn("Create Folders", "CreateFolders"));
            rightBtns.Children.Add(MakeHeaderBtn("Import File", "ImportFile"));
            rightBtns.Children.Add(MakeHeaderBtn("Set Output Dir", "SetOutputDirectory"));
            rightBtns.Children.Add(MakeHeaderBtn("Refresh", "Refresh"));
            System.Windows.Controls.Grid.SetColumn(rightBtns, 1);
            g.Children.Add(rightBtns);

            header.Child = g;
            return header;
        }

        private static Button MakeHeaderBtn(string label, string tag)
        {
            var btn = new Button
            {
                Content = label,
                Tag = tag,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrAccent,
                Foreground = Brushes.White,
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
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
                        Filter = "All files|*.*|PDF|*.pdf|Excel|*.xlsx;*.csv|Images|*.png;*.jpg",
                        Multiselect = true
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        string targetFolder = "BRIEFCASE";
                        // Attempt to auto-route by extension
                        foreach (string file in dlg.FileNames)
                        {
                            string ext = Path.GetExtension(file).ToUpperInvariant().TrimStart('.');
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
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  TREE NAVIGATOR (Left Panel)
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildTreePanel(Document doc)
        {
            var border = new Border
            {
                Background = BrWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Margin = new Thickness(0)
            };

            var stack = new DockPanel { LastChildFill = true };

            // Tree header
            var treeHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                Padding = new Thickness(10, 6, 10, 6)
            };
            treeHeader.Child = new TextBlock
            {
                Text = "NAVIGATOR",
                FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = BrFgDark
            };
            DockPanel.SetDock(treeHeader, Dock.Top);
            stack.Children.Add(treeHeader);

            _treeView = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = BrWhite,
                Padding = new Thickness(4)
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

        private static void PopulateTree()
        {
            _treeView.Items.Clear();

            // ── ALL DOCUMENTS ──
            var allNode = MakeTreeItem("ALL DOCUMENTS", "ALL", true);
            _treeView.Items.Add(allNode);

            // ── FOLDERS ──
            var foldersNode = MakeTreeItem("FOLDERS", "FOLDER_ROOT", false);
            var stats = ProjectFolderEngine.GetFolderStats(_doc);
            foreach (var fs in stats.Where(s => s.FileCount > 0 || s.Exists))
            {
                string label = $"{fs.FolderName.Substring(3)} ({fs.FileCount})";
                foldersNode.Items.Add(MakeTreeItem(label, $"FOLDER:{fs.FolderId}", false));
            }
            if (foldersNode.Items.Count == 0)
                foldersNode.Items.Add(MakeTreeItem("(no folders — click Create Folders)", "", false));
            _treeView.Items.Add(foldersNode);

            // ── CDE STATUS ──
            var cdeNode = MakeTreeItem("CDE STATUS", "CDE_ROOT", false);
            foreach (var cde in new[] { ("WIP", "Work In Progress"), ("SHARED", "Shared"),
                                         ("PUBLISHED", "Published"), ("ARCHIVE", "Archive") })
            {
                int count = _allItems.Count(i => i.CDE == cde.Item1);
                cdeNode.Items.Add(MakeTreeItem($"{cde.Item1} — {cde.Item2} ({count})", $"CDE:{cde.Item1}", false));
            }
            _treeView.Items.Add(cdeNode);

            // ── DOCUMENT STATUS ──
            var statusNode = MakeTreeItem("DOCUMENT STATUS", "STATUS_ROOT", false);
            foreach (var kv in BIMManager.DocStatusCodes.All.Take(15))
            {
                int count = _allItems.Count(i => i.Status == kv.Key);
                if (count > 0)
                    statusNode.Items.Add(MakeTreeItem($"{kv.Key} — {kv.Value} ({count})", $"STATUS:{kv.Key}", false));
            }
            // Add IFI, AFD etc. explicitly
            foreach (string code in new[] { "IFI", "AFD", "IFR", "IFC", "IFD", "IFT", "IFM", "IFA",
                                             "IFB", "IFP", "IFQ", "IFO", "IFS", "IFW" })
            {
                int count = _allItems.Count(i => i.Status == code);
                if (count > 0 || BIMManager.DocStatusCodes.All.ContainsKey(code))
                {
                    string desc = BIMManager.DocStatusCodes.All.TryGetValue(code, out string d) ? d : code;
                    statusNode.Items.Add(MakeTreeItem($"{code} — {desc} ({count})", $"STATUS:{code}", false));
                }
            }
            _treeView.Items.Add(statusNode);

            // ── ISSUES ──
            var issuesNode = MakeTreeItem("ISSUES & RFIs", "CAT:ISSUE", false);
            foreach (var kv in BIMManager.BIMManagerEngine.IssueTypes.Take(12))
            {
                int count = _allItems.Count(i => i.Category == "ISSUE" && i.Type == kv.Key);
                issuesNode.Items.Add(MakeTreeItem($"{kv.Key} — {kv.Value} ({count})", $"ISSUE:{kv.Key}", false));
            }
            _treeView.Items.Add(issuesNode);

            // ── REVISIONS ──
            var revNode = MakeTreeItem("REVISIONS", "CAT:REVISION", false);
            var revItems = _allItems.Where(i => i.Category == "REVISION").GroupBy(i => i.Revision ?? "?");
            foreach (var g in revItems.OrderBy(x => x.Key))
                revNode.Items.Add(MakeTreeItem($"Rev {g.Key} ({g.Count()})", $"REV:{g.Key}", false));
            _treeView.Items.Add(revNode);

            // ── CLASHES ──
            var clashNode = MakeTreeItem("CLASHES", "CAT:CLASH", false);
            int clashCount = _allItems.Count(i => i.Category == "CLASH");
            clashNode.Items.Add(MakeTreeItem($"All Clashes ({clashCount})", "CAT:CLASH", false));
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
            transNode.Items.Add(MakeTreeItem("All Transmittals", "CAT:TRANSMITTAL", false));
            transNode.Items.Add(MakeTreeItem("CDE Packages", "FOLDER:TRANSMITTALS", false));
            _treeView.Items.Add(transNode);

            // ── COMPLIANCE ──
            var compNode = MakeTreeItem("COMPLIANCE", "CAT:COMPLIANCE", false);
            compNode.Items.Add(MakeTreeItem("Validation Reports", "FOLDER:COMPLIANCE", false));
            compNode.Items.Add(MakeTreeItem("Model Health", "CAT:MODELHEALTH", false));
            _treeView.Items.Add(compNode);

            // ── BEP ──
            var bepNode = MakeTreeItem("BEP", "FOLDER:BEP", false);
            _treeView.Items.Add(bepNode);

            // Expand first node
            allNode.IsExpanded = true;
            allNode.IsSelected = true;
        }

        private static TreeViewItem MakeTreeItem(string label, string filter, bool expanded)
        {
            var item = new TreeViewItem
            {
                Header = label,
                Tag = filter,
                IsExpanded = expanded,
                Padding = new Thickness(2, 3, 2, 3),
                FontSize = 11
            };
            return item;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DOCUMENT LIST PANEL (Right)
        // ══════════════════════════════════════════════════════════════════

        private static DockPanel BuildDocumentPanel()
        {
            var panel = new DockPanel { LastChildFill = true, Background = BrWhite };

            // ── Search bar ──
            var searchBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8)),
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var searchGrid = new System.Windows.Controls.Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            searchGrid.Children.Add(new TextBlock
            {
                Text = "Search: ",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11, Foreground = BrFgDark,
                Margin = new Thickness(0, 0, 4, 0)
            });

            var searchBox = new System.Windows.Controls.TextBox
            {
                FontSize = 11, Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = BrBorder, BorderThickness = new Thickness(1),
                ToolTip = "Search by name, ID, type, status..."
            };
            searchBox.TextChanged += (s, e) =>
            {
                _searchText = searchBox.Text ?? "";
                _view?.Refresh();
                UpdateCounts();
            };
            System.Windows.Controls.Grid.SetColumn(searchBox, 1);
            searchGrid.Children.Add(searchBox);

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

            // ── Quick filter bar ──
            var filterBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var filterWrap = new WrapPanel();
            foreach (var (label, filter, brush) in new (string, string, SolidColorBrush)[]
            {
                ("All",          "ALL",           BrFgDark),
                ("Documents",    "CAT:DOCUMENT",  BrAccent),
                ("Issues",       "CAT:ISSUE",     BrOrange),
                ("Revisions",    "CAT:REVISION",  BrPurple),
                ("Clashes",      "CAT:CLASH",     BrRed),
                ("Handover",     "CAT:HANDOVER",  BrTeal),
                ("WIP",          "CDE:WIP",       BrFgSub),
                ("Shared",       "CDE:SHARED",    BrGreen),
                ("Published",    "CDE:PUBLISHED", BrGreen),
            })
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = filter,
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(2),
                    FontSize = 10,
                    Background = Brushes.White,
                    Foreground = brush,
                    BorderBrush = brush,
                    BorderThickness = new Thickness(1),
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

            // ── ListView (main grid) ──
            _listView = new ListView
            {
                BorderThickness = new Thickness(0),
                FontSize = 11,
                ItemsSource = _view
            };

            var gridView = new GridView();
            gridView.Columns.Add(MakeCol("Type", "Type", 45));
            gridView.Columns.Add(MakeCol("ID / Name", "Title", 240));
            gridView.Columns.Add(MakeCol("Status", "Status", 55));
            gridView.Columns.Add(MakeCol("CDE", "CDE", 65));
            gridView.Columns.Add(MakeCol("Rev", "Revision", 40));
            gridView.Columns.Add(MakeCol("Disc", "Discipline", 35));
            gridView.Columns.Add(MakeCol("Folder", "Folder", 110));
            gridView.Columns.Add(MakeCol("Format", "FileFormat", 45));
            gridView.Columns.Add(MakeCol("Size", "Size", 55));
            gridView.Columns.Add(MakeCol("Date", "Date", 80));
            gridView.Columns.Add(MakeCol("Priority", "Priority", 55));
            gridView.Columns.Add(MakeCol("Assigned", "AssignedTo", 80));
            _listView.View = gridView;

            _listView.MouseDoubleClick += ListView_DoubleClick;

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
            if (_listView.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath))
            {
                try
                {
                    if (File.Exists(item.FilePath))
                        Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
                    else
                        MessageBox.Show($"File not found:\n{item.FilePath}", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex) { StingLog.Warn($"DocMgr open file: {ex.Message}"); }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ACTION BAR (Bottom)
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildActionBar(Document doc, Window win)
        {
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var wrap = new WrapPanel();

            // ── File operations ──
            wrap.Children.Add(MakeActionBtn("Open", "Open selected file in default application", BrAccent, (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                    Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            }));

            wrap.Children.Add(MakeActionBtn("Open Folder", "Open containing folder in Explorer", BrAccent, (s, e) =>
            {
                if (_listView?.SelectedItem is DocItemVM item && !string.IsNullOrEmpty(item.FilePath))
                {
                    string dir = Path.GetDirectoryName(item.FilePath);
                    if (Directory.Exists(dir))
                        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
                else
                {
                    string root = ProjectFolderEngine.GetRootPath(doc);
                    if (Directory.Exists(root))
                        Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
                }
            }));

            wrap.Children.Add(MakeActionBtn("Rename", "Rename selected file", BrFgDark, (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
                string currentName = Path.GetFileName(item.FilePath);
                string newName = PromptForText("Rename File", "Enter new filename:", currentName);
                if (!string.IsNullOrEmpty(newName) && newName != currentName)
                {
                    if (ProjectFolderEngine.RenameFile(item.FilePath, newName))
                    {
                        item.Title = newName;
                        RefreshData();
                    }
                }
            }));

            wrap.Children.Add(MakeActionBtn("Delete", "Delete selected file", BrRed, (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
                var confirm = MessageBox.Show($"Delete file?\n\n{item.Title}\n\nThis cannot be undone.",
                    "STING — Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                {
                    ProjectFolderEngine.DeleteFile(item.FilePath);
                    _allItems.Remove(item);
                    UpdateCounts();
                }
            }));

            wrap.Children.Add(MakeActionBtn("Move To...", "Move file to different folder", BrPurple, (s, e) =>
            {
                if (_listView?.SelectedItem is not DocItemVM item || string.IsNullOrEmpty(item.FilePath)) return;
                var folders = ProjectFolderEngine.Folders.Select(f => $"{f.Id}: {f.Name} — {f.Description}").ToList();
                string pick = StingListPicker.Show("Move To Folder", "Select destination folder:", folders);
                if (string.IsNullOrEmpty(pick)) return;
                string folderId = pick.Split(':')[0].Trim();
                if (ProjectFolderEngine.MoveFile(doc, item.FilePath, folderId))
                    RefreshData();
            }));

            // Separator
            wrap.Children.Add(new Border { Width = 2, Height = 24, Background = BrBorder, Margin = new Thickness(6, 0, 6, 0) });

            // ── Command operations ──
            wrap.Children.Add(MakeActionBtn("Raise Issue", "Create new RFI/TQ/NCR/EWN", BrOrange, (s, e) =>
            {
                _selectedOperation = "RaiseIssue";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("COBie Export", "Export COBie V2.4 spreadsheet", BrTeal, (s, e) =>
            {
                _selectedOperation = "COBieExport";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Create Transmittal", "Generate ISO 19650 transmittal", BrGreen, (s, e) =>
            {
                _selectedOperation = "CreateTransmittal";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Tag Register", "Export tag register CSV", BrPurple, (s, e) =>
            {
                _selectedOperation = "TagRegisterExport";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Doc Register", "View document register", BrAccent, (s, e) =>
            {
                _selectedOperation = "DocumentRegister";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Add Document", "Register new document", BrGreen, (s, e) =>
            {
                _selectedOperation = "AddDocument";
                win.DialogResult = true; win.Close();
            }));

            // Separator
            wrap.Children.Add(new Border { Width = 2, Height = 24, Background = BrBorder, Margin = new Thickness(6, 0, 6, 0) });

            // ── Handover & compliance ──
            wrap.Children.Add(MakeActionBtn("FM Handover", "Generate FM handover manual", BrTeal, (s, e) =>
            {
                _selectedOperation = "HandoverManual";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Revision Dash", "Open revision dashboard", BrPurple, (s, e) =>
            {
                _selectedOperation = "RevisionDashboard";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Issue Dash", "Open issue dashboard", BrOrange, (s, e) =>
            {
                _selectedOperation = "IssueDashboard";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Clash Report", "Run clash detection", BrRed, (s, e) =>
            {
                _selectedOperation = "ClashDetection";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Validate Naming", "Check ISO 19650 naming", BrFgDark, (s, e) =>
            {
                _selectedOperation = "ValidateDocNaming";
                win.DialogResult = true; win.Close();
            }));

            wrap.Children.Add(MakeActionBtn("Model Health", "Run model health dashboard", BrGreen, (s, e) =>
            {
                _selectedOperation = "ModelHealthDashboard";
                win.DialogResult = true; win.Close();
            }));

            bar.Child = wrap;
            return bar;
        }

        private static Button MakeActionBtn(string label, string tooltip, SolidColorBrush fg, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = label,
                ToolTip = tooltip,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(2),
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                Background = Brushes.White,
                Foreground = fg,
                BorderBrush = fg,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.Click += handler;
            return btn;
        }

        // ══════════════════════════════════════════════════════════════════
        //  FOOTER / STATUS BAR
        // ══════════════════════════════════════════════════════════════════

        private static Border BuildFooter(Window win, DocumentManagementResult result)
        {
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                Padding = new Thickness(12, 6, 12, 6),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 10, Foreground = BrFgSub,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(_statusText, 0);
            grid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var btnClose = new Button
            {
                Content = "Close", Width = 80, Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                Foreground = BrFgDark, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, FontSize = 11
            };
            btnClose.Click += (s, e) => { result.Confirmed = false; win.DialogResult = false; win.Close(); };
            btnPanel.Children.Add(btnClose);

            System.Windows.Controls.Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);

            footer.Child = grid;

            // Update status text
            string root = ProjectFolderEngine.RootPath ?? "(not set)";
            _statusText.Text = $"Root: {root}  |  {_allItems.Count} items loaded";

            return footer;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATA LOADING
        // ══════════════════════════════════════════════════════════════════

        private static void LoadAllData(Document doc)
        {
            _allItems.Clear();

            // 1. Load files from project folder structure
            LoadProjectFiles(doc);

            // 2. Load document register entries
            LoadDocumentRegister(doc);

            // 3. Load issues
            LoadIssues(doc);

            // 4. Load revisions from Revit model
            LoadRevisions(doc);

            // 5. Load clash data
            LoadClashData(doc);

            // 6. Load transmittals
            LoadTransmittals(doc);

            // 7. Load compliance/model health
            LoadComplianceData(doc);
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
                        Type = f.Extension,
                        TypeDesc = f.Extension,
                        Status = "",
                        CDE = f.CDEStatus,
                        Folder = f.FolderName,
                        FolderId = f.FolderId,
                        FilePath = f.FilePath,
                        FileFormat = f.Extension,
                        Size = f.SizeDisplay,
                        Date = f.Modified.ToString("yyyy-MM-dd HH:mm"),
                        Category = "DOCUMENT",
                        Direction = "OUT"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadProjectFiles: {ex.Message}"); }
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
                    // Skip if already loaded as a project file
                    if (_allItems.Any(i => i.Id == docId)) continue;

                    string statusCode = d["status_code"]?.ToString() ?? "";
                    string statusDesc = BIMManager.DocStatusCodes.All.TryGetValue(statusCode, out string sd) ? sd : statusCode;
                    string docType = d["doc_type"]?.ToString() ?? "";
                    string typeDesc = BIMManager.BIMManagerEngine.DocumentTypes.TryGetValue(docType, out string td) ? td : docType;

                    _allItems.Add(new DocItemVM
                    {
                        Id = docId,
                        Title = d["title"]?.ToString() ?? docId,
                        Type = docType,
                        TypeDesc = typeDesc,
                        Status = statusCode,
                        StatusDesc = statusDesc,
                        CDE = d["cde_status"]?.ToString() ?? "WIP",
                        Revision = d["revision"]?.ToString() ?? "",
                        Date = d["date"]?.ToString() ?? "",
                        Direction = d["direction"]?.ToString() ?? "OUT",
                        FilePath = d["file_path"]?.ToString() ?? "",
                        FileFormat = d["file_format"]?.ToString() ?? "",
                        Category = "DOCUMENT",
                        Folder = "15_REGISTERS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadDocRegister: {ex.Message}"); }
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

                    _allItems.Add(new DocItemVM
                    {
                        Id = issue["issue_id"]?.ToString() ?? "",
                        Title = issue["title"]?.ToString() ?? "(untitled issue)",
                        Type = issueType,
                        TypeDesc = typeDesc,
                        Status = status,
                        StatusDesc = statusDesc,
                        CDE = "",
                        Revision = issue["revision"]?.ToString() ?? "",
                        Date = issue["date"]?.ToString() ?? "",
                        Priority = issue["priority"]?.ToString() ?? "MEDIUM",
                        AssignedTo = issue["assigned_to"]?.ToString() ?? "",
                        Discipline = issue["discipline"]?.ToString() ?? "",
                        Category = "ISSUE",
                        Folder = "11_ISSUES"
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
                    try { revNum = rev.RevisionNumber; } catch { }
                    string desc = "";
                    try { desc = rev.Description; } catch { }
                    string date = "";
                    try { date = rev.RevisionDate; } catch { }
                    string issuedBy = "";
                    try { issuedBy = rev.IssuedBy; } catch { }
                    string issuedTo = "";
                    try { issuedTo = rev.IssuedTo; } catch { }

                    _allItems.Add(new DocItemVM
                    {
                        Id = $"REV-{rev.SequenceNumber:D3}",
                        Title = $"Rev {revNum}: {desc}",
                        Type = "REV",
                        TypeDesc = "Revision",
                        Status = rev.Issued ? "ISSUED" : "DRAFT",
                        CDE = rev.Issued ? "PUBLISHED" : "WIP",
                        Revision = revNum,
                        Date = date,
                        AssignedTo = $"{issuedBy} -> {issuedTo}",
                        Category = "REVISION",
                        Folder = "14_REVISIONS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadRevisions: {ex.Message}"); }
        }

        private static void LoadClashData(Document doc)
        {
            try
            {
                // Load BCF files from clash folder
                string clashDir = ProjectFolderEngine.GetFolderPath(doc, "CLASHES");
                if (!Directory.Exists(clashDir)) return;

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
                        Status = "ACTIVE",
                        CDE = "",
                        Date = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        FilePath = fi.FullName,
                        FileFormat = fi.Extension.TrimStart('.').ToUpperInvariant(),
                        Size = FormatSize(fi.Length),
                        Category = "CLASH",
                        Folder = "12_CLASHES"
                    });
                }

                // Load clash entries from issues.json (CLASH type)
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
                            Type = "CLASH",
                            TypeDesc = "Coordination Clash",
                            Status = issue["status"]?.ToString() ?? "OPEN",
                            Priority = issue["priority"]?.ToString() ?? "HIGH",
                            Date = issue["date"]?.ToString() ?? "",
                            Category = "CLASH",
                            Folder = "12_CLASHES"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadClashData: {ex.Message}"); }
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
                    _allItems.Add(new DocItemVM
                    {
                        Id = t["transmittal_id"]?.ToString() ?? "",
                        Title = t["title"]?.ToString() ?? t["transmittal_id"]?.ToString() ?? "",
                        Type = "TR",
                        TypeDesc = "Transmittal",
                        Status = t["status"]?.ToString() ?? "SENT",
                        CDE = "SHARED",
                        Revision = t["revision"]?.ToString() ?? "",
                        Date = t["date"]?.ToString() ?? "",
                        AssignedTo = t["recipient"]?.ToString() ?? "",
                        Category = "TRANSMITTAL",
                        Folder = "10_TRANSMITTALS"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadTransmittals: {ex.Message}"); }
        }

        private static void LoadComplianceData(Document doc)
        {
            if (doc == null) return;
            try
            {
                var scan = ComplianceScan.Scan(doc);
                if (scan == null) return;

                _allItems.Add(new DocItemVM
                {
                    Id = "COMPLIANCE-LIVE",
                    Title = $"Live Compliance: {scan.CompliancePercent:F0}% ({scan.RAGStatus})",
                    Type = "RPT",
                    TypeDesc = "Compliance Report",
                    Status = scan.RAGStatus,
                    CDE = "",
                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Category = "COMPLIANCE",
                    Folder = "16_COMPLIANCE"
                });

                // Per-discipline breakdown
                if (scan.ByDisc != null)
                {
                    foreach (var kv in scan.ByDisc.OrderBy(x => x.Key))
                    {
                        _allItems.Add(new DocItemVM
                        {
                            Id = $"COMP-{kv.Key}",
                            Title = $"{kv.Key}: {kv.Value.CompliancePct:F0}% ({kv.Value.Tagged}/{kv.Value.Total})",
                            Type = "RPT",
                            TypeDesc = "Discipline Compliance",
                            Status = kv.Value.CompliancePct >= 80 ? "GREEN" : kv.Value.CompliancePct >= 50 ? "AMBER" : "RED",
                            Discipline = kv.Key,
                            Date = DateTime.Now.ToString("yyyy-MM-dd"),
                            Category = "COMPLIANCE",
                            Folder = "16_COMPLIANCE"
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DocMgr.LoadCompliance: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FILTERING
        // ══════════════════════════════════════════════════════════════════

        private static bool FilterItem(object obj)
        {
            if (obj is not DocItemVM item) return false;

            // Text search
            if (!string.IsNullOrEmpty(_searchText))
            {
                string search = _searchText;
                bool match = (item.Title ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Id ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Type ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.TypeDesc ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Status ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.StatusDesc ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Folder ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.AssignedTo ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Priority ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Discipline ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) return false;
            }

            // Tree / button filter
            if (_currentFilter == "ALL") return true;

            if (_currentFilter.StartsWith("CAT:"))
            {
                string cat = _currentFilter.Substring(4);
                return (item.Category ?? "").Equals(cat, StringComparison.OrdinalIgnoreCase);
            }
            if (_currentFilter.StartsWith("CDE:"))
            {
                string cde = _currentFilter.Substring(4);
                return (item.CDE ?? "").Equals(cde, StringComparison.OrdinalIgnoreCase);
            }
            if (_currentFilter.StartsWith("STATUS:"))
            {
                string status = _currentFilter.Substring(7);
                return (item.Status ?? "").Equals(status, StringComparison.OrdinalIgnoreCase);
            }
            if (_currentFilter.StartsWith("FOLDER:"))
            {
                string folderId = _currentFilter.Substring(7);
                return (item.FolderId ?? "").Equals(folderId, StringComparison.OrdinalIgnoreCase);
            }
            if (_currentFilter.StartsWith("ISSUE:"))
            {
                string issueType = _currentFilter.Substring(6);
                return item.Category == "ISSUE" && (item.Type ?? "").Equals(issueType, StringComparison.OrdinalIgnoreCase);
            }
            if (_currentFilter.StartsWith("REV:"))
            {
                string rev = _currentFilter.Substring(4);
                return item.Category == "REVISION" && (item.Revision ?? "").Equals(rev, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static void RefreshData()
        {
            if (_doc == null) return;
            _allItems.Clear();
            LoadAllData(_doc);
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_allItems);
            _view.Filter = FilterItem;
            if (_listView != null) _listView.ItemsSource = _view;
            PopulateTree();
            UpdateCounts();
        }

        private static void UpdateCounts()
        {
            int total = _allItems.Count;
            int visible = _view?.Cast<object>().Count() ?? 0;
            int docs = _allItems.Count(i => i.Category == "DOCUMENT");
            int issues = _allItems.Count(i => i.Category == "ISSUE");
            int revs = _allItems.Count(i => i.Category == "REVISION");
            int clashes = _allItems.Count(i => i.Category == "CLASH");

            if (_countText != null)
                _countText.Text = $"{visible} of {total}";
            if (_statusText != null)
                _statusText.Text = $"Root: {ProjectFolderEngine.RootPath ?? "(not set)"}  |  " +
                    $"{docs} docs  {issues} issues  {revs} revisions  {clashes} clashes  |  Total: {total}";
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
                catch (Exception ex) { StingLog.Warn($"DocMgr: Cannot create BIM_MANAGER dir: {ex.Message}"); }
            }
            return bimDir;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static string PromptForText(string title, string prompt, string defaultValue)
        {
            var win = new Window
            {
                Title = title,
                Width = 420, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = prompt, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            var tb = new System.Windows.Controls.TextBox
            {
                Text = defaultValue, FontSize = 11,
                Padding = new Thickness(4, 3, 4, 3)
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
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { win.DialogResult = false; win.Close(); };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            stack.Children.Add(btnPanel);
            win.Content = stack;
            tb.SelectAll();
            tb.Focus();

            return win.ShowDialog() == true ? tb.Text : null;
        }
    }
}
