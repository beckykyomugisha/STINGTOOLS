using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the SheetManagerDialog.
    /// </summary>
    public class SheetManagerResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Dual-panel WPF Sheet Manager dialog.
    /// Left panel: TreeView of sheets grouped by discipline with viewport children.
    /// Right panel: context-sensitive property grid / action panel.
    /// Bottom: action buttons for layout, clone, create, arrange operations.
    ///
    /// Inspired by Ideate SheetManager and Naviate Sheet Tools.
    /// </summary>
    internal static class SheetManagerDialog
    {
        // ── Theme colours (light theme with orange accents) ─────────────
        private static readonly SolidColorBrush BrBgLight = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush BrBgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrBgHeader = new(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly SolidColorBrush BrAccent = new(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrAccentHover = new(Color.FromRgb(0xF0, 0xA0, 0x45));
        private static readonly SolidColorBrush BrFgDark = new(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly SolidColorBrush BrFgWhite = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BrFgSubtle = new(Color.FromRgb(0x77, 0x77, 0x77));
        private static readonly SolidColorBrush BrBorder = new(Color.FromRgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidColorBrush BrSelected = new(Color.FromRgb(0xFB, 0xE4, 0xC8));
        private static readonly SolidColorBrush BrHover = new(Color.FromRgb(0xFD, 0xF0, 0xE0));
        private static readonly SolidColorBrush BrGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrRed = new(Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush BrBlue = new(Color.FromRgb(0x21, 0x96, 0xF3));

        // ── State ───────────────────────────────────────────────────────
        private static string _selectedOperation;
        private static TreeViewItem _selectedTreeItem;
        private static TextBlock _statusText;
        private static StackPanel _detailPanel;
        private static Window _window;

        // ── Sheet/viewport data passed from caller ─────────────────────
        private static List<SheetNode> _sheetNodes;
        private static List<UnplacedViewNode> _unplacedViews;

        /// <summary>Data model for sheet tree nodes.</summary>
        internal class SheetNode
        {
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public string Discipline { get; set; }
            public int ViewportCount { get; set; }
            public string PaperSize { get; set; }
            public string TitleBlockName { get; set; }
            public string DrawableArea { get; set; }
            public object Tag { get; set; } // ElementId
            public List<ViewportNode> Viewports { get; set; } = new List<ViewportNode>();
        }

        /// <summary>Data model for viewport tree child nodes.</summary>
        internal class ViewportNode
        {
            public string ViewName { get; set; }
            public string Scale { get; set; }
            public string PaperSize { get; set; }
            public string Position { get; set; }
            public object Tag { get; set; } // ElementId
        }

        /// <summary>Data model for unplaced views.</summary>
        internal class UnplacedViewNode
        {
            public string ViewName { get; set; }
            public string ViewType { get; set; }
            public string Scale { get; set; }
            public object Tag { get; set; } // ElementId
        }


        /// <summary>
        /// Show the Sheet Manager dialog.
        /// </summary>
        /// <param name="sheets">Sheet data grouped by discipline.</param>
        /// <param name="unplacedViews">Views not yet placed on any sheet.</param>
        /// <returns>Result with selected operation and options.</returns>
        public static SheetManagerResult Show(List<SheetNode> sheets, List<UnplacedViewNode> unplacedViews)
        {
            _sheetNodes = sheets ?? new List<SheetNode>();
            _unplacedViews = unplacedViews ?? new List<UnplacedViewNode>();
            _selectedOperation = null;

            var result = new SheetManagerResult();

            _window = new Window
            {
                Title = "STING Sheet Manager",
                Width = 960,
                Height = 640,
                MinWidth = 700,
                MinHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBgLight,
                ResizeMode = ResizeMode.CanResizeWithGrip,
            };

            // Set owner to Revit main window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception) { /* non-critical */ }

            var root = new DockPanel { LastChildFill = true };

            // ── Header bar ──────────────────────────────────────────────
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 10, 16, 10),
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            headerStack.Children.Add(new TextBlock
            {
                Text = "STING Sheet Manager",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = BrAccent, VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"  |  {_sheetNodes.Count} sheets  |  {_unplacedViews.Count} unplaced views",
                FontSize = 12, Foreground = BrFgWhite,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Bottom action bar ───────────────────────────────────────
            var bottomBar = CreateBottomBar(result);
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            root.Children.Add(bottomBar);

            // ── Main content: left tree + right detail ──────────────────
            var splitter = new Grid();
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360), MinWidth = 250 });
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // splitter
            splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });

            // Left panel: TreeView
            var leftPanel = CreateLeftPanel();
            Grid.SetColumn(leftPanel, 0);
            splitter.Children.Add(leftPanel);

            // GridSplitter
            var gridSplitter = new GridSplitter
            {
                Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrBorder, Cursor = Cursors.SizeWE
            };
            Grid.SetColumn(gridSplitter, 1);
            splitter.Children.Add(gridSplitter);

            // Right panel: Detail/properties
            var rightPanel = CreateRightPanel();
            Grid.SetColumn(rightPanel, 2);
            splitter.Children.Add(rightPanel);

            root.Children.Add(splitter);
            _window.Content = root;

            // Show modal
            bool? dialogResult = _window.ShowDialog();
            result.Confirmed = dialogResult == true;
            result.Operation = _selectedOperation;

            return result;
        }


        // ═══════════════════════════════════════════════════════════════════
        //  LEFT PANEL — Sheet TreeView
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateLeftPanel()
        {
            var border = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0)
            };

            var stack = new DockPanel { LastChildFill = true };

            // Search box
            var searchBox = new TextBox
            {
                Margin = new Thickness(8, 8, 8, 4),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12,
                Background = BrBgLight,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Tag = "Search sheets..."
            };
            // Placeholder text
            searchBox.GotFocus += (s, e) =>
            {
                if (searchBox.Foreground == BrFgSubtle)
                {
                    searchBox.Text = "";
                    searchBox.Foreground = BrFgDark;
                }
            };
            searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(searchBox.Text))
                {
                    searchBox.Text = "Search sheets...";
                    searchBox.Foreground = BrFgSubtle;
                }
            };
            searchBox.Text = "Search sheets...";
            searchBox.Foreground = BrFgSubtle;
            DockPanel.SetDock(searchBox, Dock.Top);
            stack.Children.Add(searchBox);

            // TreeView
            var tree = new TreeView
            {
                Margin = new Thickness(4, 4, 4, 4),
                BorderThickness = new Thickness(0),
                Background = BrBgWhite
            };

            // Group sheets by discipline
            var grouped = _sheetNodes
                .GroupBy(s => s.Discipline)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var discItem = new TreeViewItem
                {
                    IsExpanded = true,
                    Header = CreateDiscHeader(group.Key, group.Count()),
                    Tag = $"DISC:{group.Key}"
                };

                foreach (var sheet in group)
                {
                    var sheetItem = new TreeViewItem
                    {
                        Header = CreateSheetHeader(sheet),
                        Tag = sheet
                    };

                    // Viewport children
                    foreach (var vp in sheet.Viewports)
                    {
                        var vpItem = new TreeViewItem
                        {
                            Header = CreateViewportHeader(vp),
                            Tag = vp
                        };
                        sheetItem.Items.Add(vpItem);
                    }

                    discItem.Items.Add(sheetItem);
                }

                tree.Items.Add(discItem);
            }

            // Unplaced views group
            if (_unplacedViews.Count > 0)
            {
                var unplacedItem = new TreeViewItem
                {
                    IsExpanded = false,
                    Header = CreateDiscHeader($"UNPLACED ({_unplacedViews.Count})", _unplacedViews.Count),
                    Tag = "UNPLACED"
                };

                foreach (var uv in _unplacedViews)
                {
                    var uvItem = new TreeViewItem
                    {
                        Header = CreateUnplacedHeader(uv),
                        Tag = uv
                    };
                    unplacedItem.Items.Add(uvItem);
                }

                tree.Items.Add(unplacedItem);
            }

            // Selection changed → update right panel
            tree.SelectedItemChanged += (s, e) =>
            {
                _selectedTreeItem = tree.SelectedItem as TreeViewItem;
                UpdateDetailPanel();
            };

            // Search filter
            searchBox.TextChanged += (s, e) =>
            {
                string filter = searchBox.Foreground == BrFgSubtle ? "" : searchBox.Text;
                FilterTree(tree, filter);
            };

            stack.Children.Add(tree);
            border.Child = stack;
            return border;
        }

        private static StackPanel CreateDiscHeader(string disc, int count)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = disc,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = BrFgDark
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"  ({count})",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private static StackPanel CreateSheetHeader(SheetNode sheet)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = sheet.SheetNumber,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = BrAccent,
                MinWidth = 60
            });
            sp.Children.Add(new TextBlock
            {
                Text = $" - {sheet.SheetName}",
                FontSize = 12,
                Foreground = BrFgDark
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"  [{sheet.ViewportCount}vp]",
                FontSize = 10,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private static TextBlock CreateViewportHeader(ViewportNode vp)
        {
            return new TextBlock
            {
                Text = $"    {vp.ViewName}  (1:{vp.Scale}, {vp.PaperSize})",
                FontSize = 11,
                Foreground = BrFgDark,
                Padding = new Thickness(0, 1, 0, 1)
            };
        }

        private static TextBlock CreateUnplacedHeader(UnplacedViewNode uv)
        {
            return new TextBlock
            {
                Text = $"    {uv.ViewName}  ({uv.ViewType}, 1:{uv.Scale})",
                FontSize = 11,
                Foreground = BrFgSubtle,
                Padding = new Thickness(0, 1, 0, 1)
            };
        }

        private static void FilterTree(TreeView tree, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                SetAllVisible(tree);
                return;
            }

            string lower = filter.ToLowerInvariant();
            foreach (TreeViewItem discItem in tree.Items)
            {
                bool anyChildVisible = false;
                foreach (TreeViewItem child in discItem.Items)
                {
                    bool match = false;
                    if (child.Tag is SheetNode sn)
                    {
                        match = sn.SheetNumber.ToLowerInvariant().Contains(lower)
                            || sn.SheetName.ToLowerInvariant().Contains(lower);
                    }
                    else if (child.Tag is UnplacedViewNode uv)
                    {
                        match = uv.ViewName.ToLowerInvariant().Contains(lower);
                    }

                    child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    if (match) anyChildVisible = true;
                }
                discItem.Visibility = anyChildVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void SetAllVisible(TreeView tree)
        {
            foreach (TreeViewItem item in tree.Items)
            {
                item.Visibility = Visibility.Visible;
                foreach (TreeViewItem child in item.Items)
                    child.Visibility = Visibility.Visible;
            }
        }


        // ═══════════════════════════════════════════════════════════════════
        //  RIGHT PANEL — Context-Sensitive Detail
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateRightPanel()
        {
            var border = new Border
            {
                Background = BrBgWhite,
                Padding = new Thickness(12)
            };

            _detailPanel = new StackPanel();

            // Default content — overview
            ShowOverviewDetail();

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _detailPanel
            };

            border.Child = scroll;
            return border;
        }

        private static void UpdateDetailPanel()
        {
            if (_selectedTreeItem == null) { ShowOverviewDetail(); return; }

            var tag = _selectedTreeItem.Tag;

            if (tag is SheetNode sn)
                ShowSheetDetail(sn);
            else if (tag is ViewportNode vn)
                ShowViewportDetail(vn);
            else if (tag is UnplacedViewNode uv)
                ShowUnplacedDetail(uv);
            else if (tag is string s && s.StartsWith("DISC:"))
                ShowDisciplineDetail(s.Substring(5));
            else if (tag is string s2 && s2 == "UNPLACED")
                ShowUnplacedGroupDetail();
            else
                ShowOverviewDetail();
        }

        private static void ShowOverviewDetail()
        {
            _detailPanel.Children.Clear();

            AddDetailHeader("Sheet Manager Overview");

            int totalVp = _sheetNodes.Sum(s => s.ViewportCount);
            AddDetailRow("Total Sheets", _sheetNodes.Count.ToString());
            AddDetailRow("Total Viewports", totalVp.ToString());
            AddDetailRow("Unplaced Views", _unplacedViews.Count.ToString());

            var disciplines = _sheetNodes.GroupBy(s => s.Discipline).OrderBy(g => g.Key);
            AddDetailSection("By Discipline");
            foreach (var g in disciplines)
            {
                AddDetailRow($"  {g.Key}", $"{g.Count()} sheets, {g.Sum(s => s.ViewportCount)} viewports");
            }

            if (_unplacedViews.Count > 0)
            {
                AddDetailSection("Quick Actions");
                AddActionButton("Auto-Place All Unplaced Views", "AutoPlaceUnplaced",
                    $"Place {_unplacedViews.Count} unplaced views on new sheets using shelf packing");
            }
        }

        private static void ShowSheetDetail(SheetNode sn)
        {
            _detailPanel.Children.Clear();

            AddDetailHeader($"Sheet: {sn.SheetNumber}");
            AddDetailRow("Name", sn.SheetName);
            AddDetailRow("Discipline", sn.Discipline);
            AddDetailRow("Title Block", sn.TitleBlockName ?? "(none)");
            AddDetailRow("Paper Size", sn.PaperSize ?? "Unknown");
            AddDetailRow("Drawable Area", sn.DrawableArea ?? "N/A");
            AddDetailRow("Viewports", sn.ViewportCount.ToString());

            if (sn.Viewports.Count > 0)
            {
                AddDetailSection("Viewports on Sheet");
                foreach (var vp in sn.Viewports)
                {
                    AddDetailRow($"  {vp.ViewName}", $"1:{vp.Scale}, {vp.PaperSize}");
                }
            }

            AddDetailSection("Sheet Actions");
            AddActionButton("Arrange Viewports", "ArrangeOnSheet",
                "Re-layout all viewports using shelf packing algorithm");
            AddActionButton("Clone Sheet", "CloneSheet",
                "Create a copy of this sheet with its title block and viewports");
            AddActionButton("Set Viewport Type", "SetViewportType",
                "Change viewport type for all viewports on this sheet");
            AddActionButton("Auto-Scale Viewports", "AutoScaleSheet",
                "Calculate and apply optimal scale for each viewport");
        }

        private static void ShowViewportDetail(ViewportNode vn)
        {
            _detailPanel.Children.Clear();

            AddDetailHeader($"Viewport: {vn.ViewName}");
            AddDetailRow("Scale", $"1:{vn.Scale}");
            AddDetailRow("Paper Size", vn.PaperSize);
            AddDetailRow("Position", vn.Position);

            AddDetailSection("Viewport Actions");
            AddActionButton("Optimal Scale", "OptimalScale",
                "Calculate the best standard scale to fit this view");
            AddActionButton("Move to Sheet", "MoveViewport",
                "Move this viewport to a different sheet");
        }

        private static void ShowUnplacedDetail(UnplacedViewNode uv)
        {
            _detailPanel.Children.Clear();

            AddDetailHeader($"Unplaced: {uv.ViewName}");
            AddDetailRow("Type", uv.ViewType);
            AddDetailRow("Scale", $"1:{uv.Scale}");

            AddDetailSection("Placement Actions");
            AddActionButton("Place on New Sheet", "PlaceOnNewSheet",
                "Create a new sheet and place this view on it");
            AddActionButton("Place on Existing Sheet", "PlaceOnExisting",
                "Place this view on an existing sheet with auto-layout");
        }

        private static void ShowDisciplineDetail(string disc)
        {
            _detailPanel.Children.Clear();

            var sheets = _sheetNodes.Where(s => s.Discipline == disc).ToList();
            AddDetailHeader($"Discipline: {disc}");
            AddDetailRow("Sheets", sheets.Count.ToString());
            AddDetailRow("Total Viewports", sheets.Sum(s => s.ViewportCount).ToString());

            var paperSizes = sheets.GroupBy(s => s.PaperSize).OrderByDescending(g => g.Count());
            AddDetailSection("Paper Sizes");
            foreach (var g in paperSizes)
            {
                AddDetailRow($"  {g.Key ?? "Unknown"}", $"{g.Count()} sheets");
            }

            AddDetailSection("Discipline Actions");
            AddActionButton("Arrange All Sheets", "ArrangeDisc",
                $"Re-layout viewports on all {sheets.Count} {disc} sheets");
            AddActionButton("Renumber Sheets", "RenumberDisc",
                $"Sequentially renumber all {disc} sheets");
        }

        private static void ShowUnplacedGroupDetail()
        {
            _detailPanel.Children.Clear();

            AddDetailHeader($"Unplaced Views ({_unplacedViews.Count})");

            var byType = _unplacedViews.GroupBy(v => v.ViewType).OrderBy(g => g.Key);
            AddDetailSection("By Type");
            foreach (var g in byType)
            {
                AddDetailRow($"  {g.Key}", g.Count().ToString());
            }

            AddDetailSection("Batch Actions");
            AddActionButton("Auto-Place All", "AutoPlaceUnplaced",
                $"Create sheets and place all {_unplacedViews.Count} views using shelf packing");
            AddActionButton("Place by Discipline", "PlaceByDiscipline",
                "Group views by discipline and create sheets per group");
        }


        // ═══════════════════════════════════════════════════════════════════
        //  BOTTOM ACTION BAR
        // ═══════════════════════════════════════════════════════════════════

        private static Border CreateBottomBar(SheetManagerResult result)
        {
            var border = new Border
            {
                Background = BrBgLight,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: status text
            _statusText = new TextBlock
            {
                Text = "Select a sheet or viewport, or use an action button.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            grid.Children.Add(_statusText);

            // Right: main action buttons
            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

            var btnAutoLayout = CreateButton("Auto-Layout", BrAccent, BrFgWhite);
            btnAutoLayout.Click += (s, e) =>
            {
                _selectedOperation = "AutoLayout";
                result.Operation = "AutoLayout";
                _window.DialogResult = true;
            };
            btnStack.Children.Add(btnAutoLayout);

            var btnClone = CreateButton("Clone Sheet", BrBlue, BrFgWhite);
            btnClone.Click += (s, e) =>
            {
                _selectedOperation = "CloneSheet";
                result.Operation = "CloneSheet";
                _window.DialogResult = true;
            };
            btnStack.Children.Add(btnClone);

            var btnCreate = CreateButton("New Sheet", BrGreen, BrFgWhite);
            btnCreate.Click += (s, e) =>
            {
                _selectedOperation = "CreateSheet";
                result.Operation = "CreateSheet";
                _window.DialogResult = true;
            };
            btnStack.Children.Add(btnCreate);

            var btnCancel = CreateButton("Close", BrBorder, BrFgDark);
            btnCancel.Click += (s, e) =>
            {
                _window.DialogResult = false;
            };
            btnStack.Children.Add(btnCancel);

            Grid.SetColumn(btnStack, 1);
            grid.Children.Add(btnStack);

            border.Child = grid;
            return border;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UI HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════

        private static void AddDetailHeader(string text)
        {
            _detailPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = BrFgDark,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        private static void AddDetailSection(string text)
        {
            _detailPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 16, 0, 6)
            });
            _detailPanel.Children.Add(new Border
            {
                Height = 1,
                Background = BrBorder,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        private static void AddDetailRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblBlock = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblBlock, 0);
            row.Children.Add(lblBlock);

            var valBlock = new TextBlock
            {
                Text = value ?? "",
                FontSize = 12,
                Foreground = BrFgDark,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valBlock, 1);
            row.Children.Add(valBlock);

            _detailPanel.Children.Add(row);
        }

        private static void AddActionButton(string text, string operation, string tooltip)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 4, 0, 4),
                Background = BrBgLight,
                Foreground = BrFgDark,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = tooltip
            };

            btn.MouseEnter += (s, e) => btn.Background = BrHover;
            btn.MouseLeave += (s, e) => btn.Background = BrBgLight;

            btn.Click += (s, e) =>
            {
                _selectedOperation = operation;

                // Pass context from selected tree node
                if (_selectedTreeItem?.Tag is SheetNode sn)
                {
                    _window.Tag = sn.Tag; // ElementId of selected sheet
                }
                else if (_selectedTreeItem?.Tag is ViewportNode vn)
                {
                    _window.Tag = vn.Tag;
                }

                _statusText.Text = $"Operation: {operation}";
                _window.DialogResult = true;
            };

            _detailPanel.Children.Add(btn);
        }

        private static Button CreateButton(string text, SolidColorBrush bg, SolidColorBrush fg)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(4, 0, 0, 0),
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var origBg = bg.Color;
            btn.MouseEnter += (s, e) =>
            {
                byte r = (byte)Math.Min(255, origBg.R + 20);
                byte g = (byte)Math.Min(255, origBg.G + 20);
                byte b = (byte)Math.Min(255, origBg.B + 20);
                btn.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            };
            btn.MouseLeave += (s, e) => btn.Background = bg;

            return btn;
        }
    }
}
