using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the DocumentManagementDialog containing the selected operation and options.
    /// </summary>
    public class DocumentManagementResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; }
        public string Tab { get; set; }
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// ISO 19650 Document Management Center — 6-tab unified WPF dialog providing
    /// comprehensive document control, CDE workflow, transmittals, handover and
    /// document register management.
    ///
    /// Tabs: REGISTER | CDE | TRANSMITTALS | NAMING | HANDOVER | BRIEFCASE
    /// </summary>
    internal static class DocumentManagementDialog
    {
        // ── Theme colours (light theme with blue accents) ─────────────
        private static readonly System.Windows.Media.Color BgLight = System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly System.Windows.Media.Color BgWhite = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly System.Windows.Media.Color BgHeader = System.Windows.Media.Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly System.Windows.Media.Color AccentBlue = System.Windows.Media.Color.FromRgb(0x15, 0x65, 0xC0);
        private static readonly System.Windows.Media.Color AccentBlueHover = System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5);
        private static readonly System.Windows.Media.Color FgDark = System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly System.Windows.Media.Color FgSubtle = System.Windows.Media.Color.FromRgb(0x77, 0x77, 0x77);
        private static readonly System.Windows.Media.Color BorderLight = System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly System.Windows.Media.Color CardBg = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly System.Windows.Media.Color CardHover = System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD);
        private static readonly System.Windows.Media.Color CardSelected = System.Windows.Media.Color.FromRgb(0xBB, 0xDE, 0xFB);

        private static readonly SolidColorBrush BrBgLight = new(BgLight);
        private static readonly SolidColorBrush BrBgWhite = new(BgWhite);
        private static readonly SolidColorBrush BrBgHeader = new(BgHeader);
        private static readonly SolidColorBrush BrAccent = new(AccentBlue);
        private static readonly SolidColorBrush BrFgDark = new(FgDark);
        private static readonly SolidColorBrush BrFgSubtle = new(FgSubtle);
        private static readonly SolidColorBrush BrBorder = new(BorderLight);
        private static readonly SolidColorBrush BrCardBg = new(CardBg);
        private static readonly SolidColorBrush BrCardHover = new(CardHover);
        private static readonly SolidColorBrush BrCardSelected = new(CardSelected);

        // ── State ───────────────────────────────────────────────────────
        private static string _selectedOperation;
        private static Border _activeCard;
        private static TextBlock _statusText;

        /// <summary>
        /// Show the document management dialog. Returns the user's selection or null if cancelled.
        /// </summary>
        public static DocumentManagementResult Show(Document doc)
        {
            _selectedOperation = null;
            _activeCard = null;

            var result = new DocumentManagementResult();

            var win = new Window
            {
                Title = "STING Document Management Center — ISO 19650",
                Width = 780,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Background = BrBgLight
            };

            try
            {
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"DocumentManagementDialog owner set failed: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header ──
            var header = new Border
            {
                Background = BrBgHeader,
                Padding = new Thickness(16, 12, 16, 12)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 DOCUMENT MANAGEMENT CENTER",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            string projectName = "";
            try { projectName = doc?.ProjectInformation?.Name ?? ""; }
            catch (Exception ex) { StingLog.Warn($"DocMgmt project name read failed: {ex.Message}"); }
            headerStack.Children.Add(new TextBlock
            {
                Text = $"Project: {projectName}",
                FontSize = 11, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBB, 0xDE, 0xFB)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──
            var footer = CreateFooter(win, result);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Tab control ──
            var tabControl = new TabControl
            {
                Margin = new Thickness(8),
                Background = BrBgWhite
            };

            tabControl.Items.Add(CreateRegisterTab());
            tabControl.Items.Add(CreateCDETab());
            tabControl.Items.Add(CreateTransmittalsTab());
            tabControl.Items.Add(CreateNamingTab());
            tabControl.Items.Add(CreateHandoverTab());
            tabControl.Items.Add(CreateBriefcaseTab());

            root.Children.Add(tabControl);
            win.Content = root;

            bool? dialogResult = win.ShowDialog();
            if (dialogResult == true && _selectedOperation != null)
            {
                result.Confirmed = true;
                result.Operation = _selectedOperation;
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TAB BUILDERS
        // ══════════════════════════════════════════════════════════════════

        private static TabItem CreateRegisterTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Manage project documents, add references, validate naming and track CDE status.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("DocumentRegister", "Document Register",
                "View and manage all registered project documents with ISO 19650 metadata",
                "\uD83D\uDCC4"));
            wrap.Children.Add(CreateCard("AddDocument", "Add Document",
                "Register a new document with naming validation and CDE suitability code",
                "\u2795"));
            wrap.Children.Add(CreateCard("ValidateDocNaming", "Validate Naming",
                "Check all documents against ISO 19650 naming conventions",
                "\u2714"));
            wrap.Children.Add(CreateCard("BulkBIMExport", "Bulk Export",
                "Export multiple BIM deliverables (IFC, PDF, COBie) in one batch",
                "\uD83D\uDCE6"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "REGISTER",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        private static TabItem CreateCDETab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Common Data Environment status tracking per ISO 19650-1 §12.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("CDEStatus", "CDE Status",
                "View and update suitability codes (S0-S6) for all project containers",
                "\uD83D\uDCCA"));
            wrap.Children.Add(CreateCard("ReviewTracker", "Review Tracker",
                "Track model reviews, approvals and information exchanges",
                "\uD83D\uDD0D"));
            wrap.Children.Add(CreateCard("MidpTracker", "MIDP Tracker",
                "Master Information Delivery Plan progress tracking",
                "\uD83D\uDDD3"));
            wrap.Children.Add(CreateCard("ISO19650Reference", "ISO 19650 Ref",
                "Quick reference guide for ISO 19650 codes and terminology",
                "\uD83D\uDCD6"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "CDE",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        private static TabItem CreateTransmittalsTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Create and manage ISO 19650 transmittals for information exchanges.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("CreateTransmittal", "Create Transmittal",
                "Generate ISO 19650 transmittal record with document list and recipient tracking",
                "\uD83D\uDCE8"));
            wrap.Children.Add(CreateCard("DocTransmittal", "Document Transmittal",
                "View transmittal report showing all registered transmittals",
                "\uD83D\uDCCB"));
            wrap.Children.Add(CreateCard("CDEPackage", "CDE Package",
                "Create ISO 19650 CDE folder structure with deliverables",
                "\uD83D\uDCC1"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "TRANSMITTALS",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        private static TabItem CreateNamingTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "ISO 19650 file and sheet naming convention validation and enforcement.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("SheetNamingCheck", "Sheet Naming Check",
                "Audit all sheets against ISO 19650 naming standard with correction suggestions",
                "\u2714"));
            wrap.Children.Add(CreateCard("AutoNumberSheets", "Auto-Number Sheets",
                "Sequentially renumber sheets within discipline groups",
                "\uD83D\uDD22"));
            wrap.Children.Add(CreateCard("RevisionNamingEnforce", "Revision Naming",
                "Enforce ISO 19650 revision naming conventions (P01, C01 etc.)",
                "\uD83C\uDD94"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "NAMING",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        private static TabItem CreateHandoverTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "FM/O&M handover exports for facilities management and asset operations.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("COBieExport", "COBie V2.4 Export",
                "Full COBie V2.4 spreadsheet with 19 worksheets and project type presets",
                "\uD83D\uDCCA"));
            wrap.Children.Add(CreateCard("HandoverManual", "FM Handover Manual",
                "Generate comprehensive FM handover manual with asset register",
                "\uD83D\uDCD5"));
            wrap.Children.Add(CreateCard("MaintenanceSchedule", "Maintenance Schedule",
                "PPM and reactive maintenance schedule per ASTM E2018",
                "\uD83D\uDD27"));
            wrap.Children.Add(CreateCard("AssetHealthReport", "Asset Health Report",
                "Asset condition scoring (0-100) with replacement forecasting",
                "\uD83C\uDFE5"));
            wrap.Children.Add(CreateCard("SpaceHandover", "Space Handover",
                "Room-by-room handover report with area, finishes and services",
                "\uD83C\uDFE2"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "HANDOVER",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        private static TabItem CreateBriefcaseTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "In-Revit reference document viewer. Access BEP, standards and specs without leaving Revit.",
                Foreground = BrFgSubtle, FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(CreateCard("BriefcaseView", "View Briefcase",
                "Browse all reference documents in the project briefcase",
                "\uD83D\uDCBC"));
            wrap.Children.Add(CreateCard("BriefcaseAddFile", "Add File",
                "Add a reference document (PDF, DOCX, XLSX) to the briefcase",
                "\u2795"));
            wrap.Children.Add(CreateCard("BriefcaseRead", "Read Document",
                "Open and read a briefcase document",
                "\uD83D\uDCD6"));
            wrap.Children.Add(CreateCard("DocumentBriefcase", "Full Briefcase",
                "Complete briefcase management with search and tagging",
                "\uD83D\uDCC2"));
            panel.Children.Add(wrap);

            return new TabItem
            {
                Header = "BRIEFCASE",
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static Border CreateCard(string tag, string title, string description, string icon)
        {
            var card = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4),
                Padding = new Thickness(12, 10, 12, 10),
                Width = 220,
                MinHeight = 80,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = tag
            };

            var stack = new StackPanel();
            var titlePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 18,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDark,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titlePanel);

            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            card.Child = stack;

            card.MouseEnter += (s, e) =>
            {
                if (card != _activeCard)
                    card.Background = BrCardHover;
            };
            card.MouseLeave += (s, e) =>
            {
                if (card != _activeCard)
                    card.Background = BrCardBg;
            };
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (_activeCard != null)
                    _activeCard.Background = BrCardBg;
                _activeCard = card;
                card.Background = BrCardSelected;
                _selectedOperation = tag;
                if (_statusText != null)
                    _statusText.Text = $"Selected: {title}";
            };

            return card;
        }

        private static Border CreateFooter(Window win, DocumentManagementResult result)
        {
            var footer = new Border
            {
                Background = BrBgWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Select an operation to continue",
                Foreground = BrFgSubtle,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(_statusText, 0);
            grid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80, Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                Foreground = BrFgDark,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCancel.Click += (s, e) =>
            {
                result.Confirmed = false;
                win.DialogResult = false;
                win.Close();
            };
            btnPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = "Execute",
                Width = 100, Height = 30,
                Background = BrAccent,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOk.Click += (s, e) =>
            {
                if (_selectedOperation == null)
                {
                    System.Windows.MessageBox.Show("Please select an operation first.",
                        "STING", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                result.Confirmed = true;
                result.Operation = _selectedOperation;
                win.DialogResult = true;
                win.Close();
            };
            btnPanel.Children.Add(btnOk);

            System.Windows.Controls.Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);

            footer.Child = grid;
            return footer;
        }
    }
}
