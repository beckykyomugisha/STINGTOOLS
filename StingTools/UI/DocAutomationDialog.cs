using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the DocAutomationDialog containing the selected operation and options.
    /// </summary>
    public class DocAutomationResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Unified 4-tab WPF dialog for documentation automation operations.
    /// Provides SHEETS, VIEWS, VIEWPORTS, and EXPORT tabs with operation cards,
    /// scope selectors, and configuration options. Returns the selected operation
    /// so the caller can dispatch to the appropriate command.
    /// </summary>
    internal static class DocAutomationDialog
    {
        // ── Theme colours (light theme with orange accents) ─────────────
        private static readonly Color BgLight = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color BgWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color BgHeader = Color.FromRgb(0x2D, 0x2D, 0x30);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color AccentOrangeHover = Color.FromRgb(0xF0, 0xA0, 0x45);
        private static readonly Color FgDark = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgSubtle = Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly Color BorderLight = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color CardBg = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardHover = Color.FromRgb(0xFD, 0xF0, 0xE0);
        private static readonly Color CardSelected = Color.FromRgb(0xFB, 0xE4, 0xC8);
        private static readonly Color TabSelected = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color TabDefault = Color.FromRgb(0xE0, 0xE0, 0xE0);

        private static readonly SolidColorBrush BrBgLight = new(BgLight);
        private static readonly SolidColorBrush BrBgWhite = new(BgWhite);
        private static readonly SolidColorBrush BrBgHeader = new(BgHeader);
        private static readonly SolidColorBrush BrAccent = new(AccentOrange);
        private static readonly SolidColorBrush BrFgDark = new(FgDark);
        private static readonly SolidColorBrush BrFgSubtle = new(FgSubtle);
        private static readonly SolidColorBrush BrBorder = new(BorderLight);
        private static readonly SolidColorBrush BrCardBg = new(CardBg);
        private static readonly SolidColorBrush BrCardHover = new(CardHover);
        private static readonly SolidColorBrush BrCardSelected = new(CardSelected);

        // ── State ───────────────────────────────────────────────────────
        private static string _selectedOperation;
        private static string _selectedScope;
        private static string _alignDirection;
        private static string _outputPath;
        private static string _exportFormat;
        private static Border _activeCard;
        private static TextBlock _statusText;

        /// <summary>
        /// Show the documentation automation dialog and return the user's selection.
        /// </summary>
        /// <param name="doc">The active Revit Document (used for context display).</param>
        /// <returns>DocAutomationResult with Confirmed=true and the selected operation, or Confirmed=false if cancelled.</returns>
        public static DocAutomationResult Show(Autodesk.Revit.DB.Document doc)
        {
            _selectedOperation = null;
            _selectedScope = "ActiveView";
            _alignDirection = "Top";
            _outputPath = "";
            _exportFormat = "PDF";
            _activeCard = null;

            string projectName = "Unknown";
            try { if (doc != null) projectName = doc.Title ?? "Untitled"; }
            catch (Exception ex) { StingLog.Warn($"DocAutomationDialog get title: {ex.Message}"); }

            var result = new DocAutomationResult();

            var win = new Window
            {
                Title = "STING Document Automation",
                Width = 700,
                Height = 520,
                MinWidth = 680,
                MinHeight = 500,
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
            catch (Exception ex) { StingLog.Warn($"DocAutomationDialog set owner: {ex.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });              // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });              // Bottom bar

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Document Automation",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentOrange)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"Project: {projectName}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBgLight,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            tabs.Items.Add(BuildSheetsTab());
            tabs.Items.Add(BuildViewsTab());
            tabs.Items.Add(BuildViewportsTab());
            tabs.Items.Add(BuildExportTab());

            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            // ── Bottom bar ──────────────────────────────────────────────
            var bottomBar = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Select an operation to continue.",
                FontSize = 11,
                Foreground = BrFgSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            bottomGrid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

            var cancelBtn = MakeButton("Cancel", false);
            cancelBtn.Click += (s, e) =>
            {
                result.Confirmed = false;
                win.Close();
            };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.IsCancel = true;
            btnStack.Children.Add(cancelBtn);

            var okBtn = MakeButton("Run", true);
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(_selectedOperation))
                {
                    _statusText.Text = "Please select an operation first.";
                    _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
                    return;
                }
                result.Confirmed = true;
                result.Operation = _selectedOperation;
                result.Options["Scope"] = _selectedScope;
                result.Options["AlignDirection"] = _alignDirection;
                result.Options["OutputPath"] = _outputPath;
                result.Options["ExportFormat"] = _exportFormat;
                win.Close();
            };
            okBtn.IsDefault = true;
            btnStack.Children.Add(okBtn);

            Grid.SetColumn(btnStack, 1);
            bottomGrid.Children.Add(btnStack);

            bottomBar.Child = bottomGrid;
            Grid.SetRow(bottomBar, 2);
            root.Children.Add(bottomBar);

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
            return result.Confirmed ? result : new DocAutomationResult { Confirmed = false };
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: SHEETS
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildSheetsTab()
        {
            var tab = MakeTab("\U0001F4C4  SHEETS");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("SHEET OPERATIONS"));

            var cardGrid = new WrapPanel { Orientation = Orientation.Horizontal };
            cardGrid.Children.Add(MakeOperationCard(
                "Organize Sheets",
                "Group sheets by discipline prefix (M, E, P, A, S)",
                "OrganizeSheets"));
            cardGrid.Children.Add(MakeOperationCard(
                "Auto-Number Sheets",
                "Sequential numbering within discipline groups",
                "AutoNumberSheets"));
            cardGrid.Children.Add(MakeOperationCard(
                "Sheet Naming Check",
                "ISO 19650 naming compliance audit",
                "SheetNamingCheck"));
            cardGrid.Children.Add(MakeOperationCard(
                "Create Sheet Index",
                "Create sheet index schedule in project",
                "CreateSheetIndex"));
            content.Children.Add(cardGrid);

            content.Children.Add(MakeScopeSelector());

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: VIEWS
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildViewsTab()
        {
            var tab = MakeTab("\U0001F441  VIEWS");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("VIEW OPERATIONS"));

            var cardGrid = new WrapPanel { Orientation = Orientation.Horizontal };
            cardGrid.Children.Add(MakeOperationCard(
                "Batch Create Views",
                "Create views by level and discipline",
                "BatchCreateViews"));
            cardGrid.Children.Add(MakeOperationCard(
                "Duplicate View",
                "Detailing, View-only, or Dependent mode",
                "DuplicateView"));
            cardGrid.Children.Add(MakeOperationCard(
                "Delete Unused Views",
                "Remove views not placed on any sheet",
                "DeleteUnusedViews"));
            cardGrid.Children.Add(MakeOperationCard(
                "Browser Organizer",
                "Organize project browser by discipline",
                "BrowserOrganizer"));
            content.Children.Add(cardGrid);

            content.Children.Add(MakeScopeSelector());

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: VIEWPORTS
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildViewportsTab()
        {
            var tab = MakeTab("\U0001F4D0  VIEWPORTS");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("VIEWPORT OPERATIONS"));

            var cardGrid = new WrapPanel { Orientation = Orientation.Horizontal };
            cardGrid.Children.Add(MakeOperationCard(
                "Align Viewports",
                "Align viewports on sheet by direction",
                "AlignViewports"));
            cardGrid.Children.Add(MakeOperationCard(
                "Renumber Viewports",
                "Left-to-right, top-to-bottom order",
                "RenumberViewports"));
            cardGrid.Children.Add(MakeOperationCard(
                "Auto-Place Viewports",
                "Auto-place and scale on sheets",
                "AutoPlaceViewports"));
            cardGrid.Children.Add(MakeOperationCard(
                "Batch Align",
                "Multi-view alignment across sheets",
                "BatchAlignViewports"));
            content.Children.Add(cardGrid);

            // ── Alignment direction selector ────────────────────────────
            content.Children.Add(MakeSectionLabel("ALIGNMENT DIRECTION"));
            var dirPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var dirWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            string[] directions = { "Top", "Left", "Center H", "Center V", "Bottom", "Right" };
            RadioButton firstDir = null;
            foreach (string dir in directions)
            {
                var rb = new RadioButton
                {
                    Content = dir,
                    GroupName = "AlignDir",
                    IsChecked = dir == "Top",
                    Foreground = BrFgDark,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 16, 2),
                    Padding = new Thickness(2)
                };
                string captured = dir;
                rb.Checked += (s, e) =>
                {
                    _alignDirection = captured;
                    if (_statusText != null)
                        _statusText.Text = $"Alignment direction: {captured}";
                };
                if (dir == "Top") firstDir = rb;
                dirWrap.Children.Add(rb);
            }
            dirPanel.Children.Add(dirWrap);
            content.Children.Add(dirPanel);

            tab.Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: EXPORT
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildExportTab()
        {
            var tab = MakeTab("\U0001F4E4  EXPORT");
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(MakeSectionLabel("EXPORT OPERATIONS"));

            var cardGrid = new WrapPanel { Orientation = Orientation.Horizontal };
            cardGrid.Children.Add(MakeOperationCard(
                "Document Transmittal",
                "ISO 19650 transmittal report",
                "DocumentTransmittal"));
            cardGrid.Children.Add(MakeOperationCard(
                "Drawing Register",
                "Comprehensive drawing register export",
                "DrawingRegister"));
            cardGrid.Children.Add(MakeOperationCard(
                "FM Handover Manual",
                "FM handover with asset register",
                "FMHandoverManual"));
            cardGrid.Children.Add(MakeOperationCard(
                "Doc Package",
                "Full documentation package export",
                "DocPackage"));
            content.Children.Add(cardGrid);

            // ── Output path ─────────────────────────────────────────────
            content.Children.Add(MakeSectionLabel("OUTPUT"));

            var pathPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = "",
                FontSize = 12,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                Background = BrBgWhite,
                Foreground = BrFgDark,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            pathBox.TextChanged += (s, e) => { _outputPath = pathBox.Text; };
            Grid.SetColumn(pathBox, 0);
            pathPanel.Children.Add(pathBox);

            var browseBtn = new Button
            {
                Content = "Browse...",
                Width = 75,
                Height = 28,
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
                Background = BrBgWhite,
                Foreground = BrFgDark,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            browseBtn.Click += (s, e) =>
            {
                // Use SaveFileDialog as a folder picker workaround (standard WPF pattern)
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Select output folder (enter any filename)",
                    Filter = "Folder|*.folder",
                    FileName = "select_folder"
                };
                if (dlg.ShowDialog() == true)
                {
                    string folder = System.IO.Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        pathBox.Text = folder;
                        _outputPath = folder;
                    }
                }
            };
            Grid.SetColumn(browseBtn, 1);
            pathPanel.Children.Add(browseBtn);

            content.Children.Add(pathPanel);

            // ── Format selector ─────────────────────────────────────────
            content.Children.Add(MakeSectionLabel("FORMAT"));
            var formatPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            string[] formats = { "PDF", "CSV", "XLSX" };
            foreach (string fmt in formats)
            {
                var rb = new RadioButton
                {
                    Content = fmt,
                    GroupName = "ExportFormat",
                    IsChecked = fmt == "PDF",
                    Foreground = BrFgDark,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 20, 2),
                    Padding = new Thickness(2)
                };
                string captured = fmt;
                rb.Checked += (s, e) => { _exportFormat = captured; };
                formatPanel.Children.Add(rb);
            }
            content.Children.Add(formatPanel);

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
                Background = new SolidColorBrush(TabDefault),
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
        /// Creates an operation card (150x80) with title, description, and click handler.
        /// </summary>
        private static Border MakeOperationCard(string title, string description, string operationKey)
        {
            var card = new Border
            {
                Width = 150,
                Height = 80,
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 8, 10, 8),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            card.Child = stack;

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

            // Click to select
            card.MouseLeftButtonDown += (s, e) =>
            {
                // Deselect previous
                if (_activeCard != null)
                {
                    _activeCard.Background = BrCardBg;
                    _activeCard.BorderBrush = BrBorder;
                }

                // Select this card
                _activeCard = card;
                card.Background = BrCardSelected;
                card.BorderBrush = BrAccent;
                _selectedOperation = operationKey;

                if (_statusText != null)
                {
                    _statusText.Foreground = BrFgSubtle;
                    _statusText.Text = $"Selected: {title}";
                }
            };

            return card;
        }

        /// <summary>
        /// Creates a scope selector panel with Active View / Entire Project radio buttons.
        /// </summary>
        private static StackPanel MakeScopeSelector()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(MakeSectionLabel("SCOPE"));

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var rbView = new RadioButton
            {
                Content = "Active view",
                GroupName = "Scope",
                IsChecked = true,
                Foreground = BrFgDark,
                FontSize = 12,
                Margin = new Thickness(0, 2, 20, 2),
                Padding = new Thickness(2)
            };
            rbView.Checked += (s, e) => { _selectedScope = "ActiveView"; };

            var rbProject = new RadioButton
            {
                Content = "Entire project",
                GroupName = "Scope",
                Foreground = BrFgDark,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(2)
            };
            rbProject.Checked += (s, e) => { _selectedScope = "EntireProject"; };

            wrap.Children.Add(rbView);
            wrap.Children.Add(rbProject);
            panel.Children.Add(wrap);

            return panel;
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
                btn.Foreground = new SolidColorBrush(Colors.White);
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
