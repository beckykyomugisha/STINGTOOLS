using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using StingTools.Core;

namespace StingTools.UI
{
    internal class BEPDashboardResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = "";
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    }

    internal static class BEPDashboard
    {
        // Palette routed through ThemeManager so the dashboard tracks the
        // active theme (Corporate by default — navy header, orange accent).
        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static SolidColorBrush BgBrush      => ThemeManager.GetBrush("AltRowBg");
        private static SolidColorBrush HeaderBg     => ThemeManager.GetBrush("HeaderBg");
        private static SolidColorBrush AccentBrush  => ThemeManager.GetBrush("AccentBrush");
        private static SolidColorBrush PanelBg      => ThemeManager.GetBrush("CardBg");
        private static SolidColorBrush FgDark       => ThemeManager.GetBrush("PanelFg");
        private static SolidColorBrush FgWhite      => ThemeManager.GetBrush("HeaderFg");
        private static SolidColorBrush FgMuted      => ThemeManager.GetBrush("SubtleFg");
        private static SolidColorBrush BorderLight  => ThemeManager.GetBrush("BorderColor");
        private static SolidColorBrush GreenAccent  => ThemeManager.GetBrush("SuccessColor");
        private static SolidColorBrush BlueAccent   => ThemeManager.GetBrush("InfoBlue");
        private static SolidColorBrush RedAccent    => ThemeManager.GetBrush("ErrorColor");
        // Brand-fixed accent retained outside the theme matrix
        private static readonly SolidColorBrush PurpleAccent = FZ(Color.FromRgb(0x6A, 0x1B, 0x9A));

        // ── Field references for value collection ──
        private static TextBox _txtProjectName;
        private static TextBox _txtProjectNumber;
        private static TextBox _txtClientName;
        private static TextBox _txtAddress;
        private static ComboBox _cboPreset;
        private static ComboBox _cboRIBAStage;
        private static ComboBox _cboProjectType;
        private static ComboBox _cboProcurementRoute;

        private static ComboBox _cboLeadDesigner;
        private static TextBox _txtLeadCompany;
        private static TextBox _txtLeadContact;
        private static TextBox _txtBIMManager;
        private static TextBox _txtBIMCoordinator;
        private static readonly Dictionary<string, CheckBox> _disciplineChecks = new Dictionary<string, CheckBox>();
        private static readonly Dictionary<string, TextBox> _disciplineCompanies = new Dictionary<string, TextBox>();
        private static readonly Dictionary<string, CheckBox> _bimUseChecks = new Dictionary<string, CheckBox>();

        private static ComboBox _cboCDEPlatform;
        private static ComboBox _cboClassification;
        private static ComboBox _cboUnitsLength;
        private static ComboBox _cboUnitsArea;
        private static TextBox _txtNamingConvention;
        private static TextBox _txtFormatNative;
        private static TextBox _txtFormatExchange;
        private static TextBox _txtFormatDrawing;
        private static TextBox _txtFormatData;

        public static BEPDashboardResult Show()
        {
            _disciplineChecks.Clear();
            _disciplineCompanies.Clear();
            _bimUseChecks.Clear();

            var result = new BEPDashboardResult();

            var win = new Window
            {
                Title = "BEP Dashboard - BIM Execution Plan",
                Width = 820,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BgBrush,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                MinWidth = 680,
                MinHeight = 560
            };

            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"BEP dashboard window owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header ──
            var header = new Border
            {
                Background = HeaderBg,
                Padding = new Thickness(20, 14, 20, 14)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "BEP Dashboard",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = FgWhite
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 BIM Execution Plan - Create, Configure and Validate",
                FontSize = 12,
                Foreground = FZ(Color.FromRgb(0xBB, 0xBB, 0xDD)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──
            var footer = new Border
            {
                Background = PanelBg,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 10, 20, 10)
            };
            var footerPanel = new DockPanel();
            var btnClose = MakeButton("Close", 90, BorderLight, FgDark);
            btnClose.Click += (s, e) => win.DialogResult = false;
            DockPanel.SetDock(btnClose, Dock.Right);
            footerPanel.Children.Add(btnClose);
            footerPanel.Children.Add(new TextBlock
            {
                Text = "Select an operation to execute. All project info fields are collected automatically.",
                Foreground = FgMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Tab Control ──
            var tabs = new TabControl
            {
                Margin = new Thickness(12),
                Background = Brushes.Transparent,
                BorderBrush = BorderLight
            };

            tabs.Items.Add(BuildCreateConfigureTab(win, result));
            tabs.Items.Add(BuildProjectInfoTab());
            tabs.Items.Add(BuildTeamDisciplinesTab());
            tabs.Items.Add(BuildStandardsCDETab(win, result));

            root.Children.Add(tabs);
            win.Content = root;

            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) win.DialogResult = false;
            };

            bool? dialogResult = win.ShowDialog();
            if (dialogResult == true && result.Confirmed)
                return result;
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 1: CREATE & CONFIGURE
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildCreateConfigureTab(Window win, BEPDashboardResult result)
        {
            var tab = new TabItem { Header = "CREATE & CONFIGURE" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            stack.Children.Add(MakeSectionHeader("BEP Operations"));

            stack.Children.Add(MakeOperationCard(
                "Create BEP", "Generate a new BIM Execution Plan from current project data and configuration.",
                AccentBrush, "CreateBEP", win, result));

            stack.Children.Add(MakeOperationCard(
                "Generate BEP", "Auto-generate BEP with compliance enrichment, risk register, and training plan.",
                GreenAccent, "GenerateBEP", win, result));

            stack.Children.Add(MakeOperationCard(
                "Update BEP", "Update an existing BEP with current model data, compliance status, and team changes.",
                BlueAccent, "UpdateBEP", win, result));

            stack.Children.Add(MakeOperationCard(
                "Export BEP", "Export BEP document to JSON or XLSX format for distribution.",
                PurpleAccent, "ExportBEP", win, result));

            stack.Children.Add(MakeOperationCard(
                "BEP Stage Validation", "Validate current model against RIBA Plan of Work stage requirements.",
                RedAccent, "BEPStageValidation", win, result));

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 2: PROJECT INFO
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildProjectInfoTab()
        {
            var tab = new TabItem { Header = "PROJECT INFO" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            stack.Children.Add(MakeSectionHeader("Project Information"));

            _txtProjectName = AddLabeledTextBox(stack, "Project Name", "");
            _txtProjectNumber = AddLabeledTextBox(stack, "Project Number", "");
            _txtClientName = AddLabeledTextBox(stack, "Client Name", "");
            _txtAddress = AddLabeledTextBox(stack, "Address", "");

            stack.Children.Add(MakeSectionHeader("BEP Configuration"));

            var presetItems = new[]
            {
                "UK_GOV", "NBS_STANDARD", "RESIDENTIAL", "MINIMAL",
                "Commercial Office", "Healthcare NHS", "Healthcare Private",
                "Education School", "Education University", "Residential Standard",
                "Residential High-Rise", "Retail", "Hotel", "Data Centre",
                "Industrial", "Transport Station", "Transport Airport",
                "Defence MOD", "Heritage", "Mixed-Use", "Laboratory",
                "Sports/Leisure"
            };
            _cboPreset = AddLabeledComboBox(stack, "BEP Template Preset", presetItems, "NBS_STANDARD");

            var ribaStages = new[] { "0 - Strategic Definition", "1 - Preparation & Briefing", "2 - Concept Design",
                "3 - Spatial Coordination", "4 - Technical Design", "5 - Manufacturing & Construction",
                "6 - Handover", "7 - Use" };
            _cboRIBAStage = AddLabeledComboBox(stack, "RIBA Stage", ribaStages, "4 - Technical Design");

            var projectTypes = new[] { "New Build", "Refurbishment", "Extension", "Fit-Out", "Demolition" };
            _cboProjectType = AddLabeledComboBox(stack, "Project Type", projectTypes, "New Build");

            var procurementRoutes = new[] { "Traditional", "Design & Build", "Management", "Construction Management", "PFI/PPP" };
            _cboProcurementRoute = AddLabeledComboBox(stack, "Procurement Route", procurementRoutes, "Traditional");

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 3: TEAM & DISCIPLINES
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildTeamDisciplinesTab()
        {
            var tab = new TabItem { Header = "TEAM & DISCIPLINES" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Lead Designer section
            stack.Children.Add(MakeSectionHeader("Lead Designer"));

            var leadRoles = new[] { "Architect", "MEP", "Structural", "Multi" };
            _cboLeadDesigner = AddLabeledComboBox(stack, "Lead Designer Role", leadRoles, "Architect");
            _txtLeadCompany = AddLabeledTextBox(stack, "Lead Company", "");
            _txtLeadContact = AddLabeledTextBox(stack, "Lead Contact", "");
            _txtBIMManager = AddLabeledTextBox(stack, "BIM Manager", "");
            _txtBIMCoordinator = AddLabeledTextBox(stack, "BIM Coordinator", "");

            // Disciplines section
            stack.Children.Add(MakeSectionHeader("Active Disciplines"));

            var disciplines = new[]
            {
                ("A", "Architecture"), ("M", "Mechanical"), ("E", "Electrical"),
                ("P", "Plumbing"), ("S", "Structural"), ("Q", "Quantity Surveying"),
                ("C", "Civil"), ("FP", "Fire Protection"), ("L", "Landscape")
            };

            foreach (var (code, name) in disciplines)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var chk = new CheckBox
                {
                    Content = $"{code} - {name}",
                    FontSize = 12,
                    Foreground = FgDark,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = code == "A" || code == "M" || code == "E" || code == "P" || code == "S"
                };
                Grid.SetColumn(chk, 0);
                row.Children.Add(chk);
                _disciplineChecks[code] = chk;

                var txt = new TextBox
                {
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 6, 4),
                    BorderBrush = BorderLight,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(txt, 1);
                row.Children.Add(txt);
                _disciplineCompanies[code] = txt;

                stack.Children.Add(row);
            }

            // BIM Uses section
            stack.Children.Add(MakeSectionHeader("BIM Uses"));

            var bimUses = new[]
            {
                "Design Authoring", "3D Coordination", "Quantity Take-Off",
                "Facilities Management", "COBie Data", "Visualisation",
                "4D Scheduling", "5D Cost Estimation", "Record Model",
                "Sustainability Analysis", "Health & Safety"
            };

            var usesWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var use in bimUses)
            {
                var chk = new CheckBox
                {
                    Content = use,
                    FontSize = 12,
                    Foreground = FgDark,
                    Margin = new Thickness(0, 4, 16, 4),
                    IsChecked = use == "Design Authoring" || use == "3D Coordination" || use == "COBie Data"
                };
                usesWrap.Children.Add(chk);
                _bimUseChecks[use] = chk;
            }
            stack.Children.Add(usesWrap);

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // TAB 4: STANDARDS & CDE
        // ═══════════════════════════════════════════════════════════════
        private static TabItem BuildStandardsCDETab(Window win, BEPDashboardResult result)
        {
            var tab = new TabItem { Header = "STANDARDS & CDE" };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            stack.Children.Add(MakeSectionHeader("Common Data Environment"));

            var cdePlatforms = new[] { "ACC / BIM 360", "Aconex", "SharePoint", "Trimble Connect", "Bentley iTwin", "Viewpoint 4Projects", "Procore", "Other" };
            _cboCDEPlatform = AddLabeledComboBox(stack, "CDE Platform", cdePlatforms, "ACC / BIM 360");

            var classifications = new[] { "Uniclass 2015", "NBS Create", "OmniClass", "MasterFormat", "UniFormat" };
            _cboClassification = AddLabeledComboBox(stack, "Classification System", classifications, "Uniclass 2015");

            stack.Children.Add(MakeSectionHeader("Units"));

            var unitsLength = new[] { "Millimeters", "Meters", "Feet", "Inches" };
            _cboUnitsLength = AddLabeledComboBox(stack, "Length", unitsLength, "Millimeters");

            var unitsArea = new[] { "Square Meters", "Square Feet" };
            _cboUnitsArea = AddLabeledComboBox(stack, "Area", unitsArea, "Square Meters");

            stack.Children.Add(MakeSectionHeader("File Formats & Naming"));

            _txtNamingConvention = AddLabeledTextBox(stack, "Naming Convention", "Project-Originator-Volume-Level-Type-Role-Class-Number");
            _txtFormatNative = AddLabeledTextBox(stack, "Native Format", "Autodesk Revit (.rvt)");
            _txtFormatExchange = AddLabeledTextBox(stack, "Exchange Format", "IFC 4 / IFC 2x3");
            _txtFormatDrawing = AddLabeledTextBox(stack, "Drawing Format", "PDF/A-1b");
            _txtFormatData = AddLabeledTextBox(stack, "Data Format", "COBie V2.4 (XLSX)");

            stack.Children.Add(MakeSectionHeader("Validation"));

            stack.Children.Add(MakeOperationCard(
                "Validate BEP Compliance", "Check current BEP configuration against ISO 19650 requirements.",
                GreenAccent, "ValidateBepCompliance", win, result));

            stack.Children.Add(MakeOperationCard(
                "ISO 19650 Reference", "Display ISO 19650 standard reference and clause guidance.",
                BlueAccent, "ISO19650Reference", win, result));

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static Dictionary<string, string> CollectAllFields()
        {
            var opts = new Dictionary<string, string>();

            // Tab 2: Project Info
            opts["ProjectName"] = _txtProjectName?.Text ?? "";
            opts["ProjectNumber"] = _txtProjectNumber?.Text ?? "";
            opts["ClientName"] = _txtClientName?.Text ?? "";
            opts["ProjectAddress"] = _txtAddress?.Text ?? "";
            opts["PresetKey"] = GetComboText(_cboPreset);
            opts["RIBAStage"] = GetComboText(_cboRIBAStage);
            opts["ProjectType"] = GetComboText(_cboProjectType);
            opts["ProcurementRoute"] = GetComboText(_cboProcurementRoute);

            // Tab 3: Team & Disciplines
            opts["LeadDesignerRole"] = GetComboText(_cboLeadDesigner);
            opts["LeadCompany"] = _txtLeadCompany?.Text ?? "";
            opts["LeadContact"] = _txtLeadContact?.Text ?? "";
            opts["BIMManager"] = _txtBIMManager?.Text ?? "";
            opts["BIMCoordinator"] = _txtBIMCoordinator?.Text ?? "";

            var activeDisciplines = _disciplineChecks
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv =>
                {
                    string company = _disciplineCompanies.TryGetValue(kv.Key, out var cmp) ? cmp.Text : "";
                    return string.IsNullOrWhiteSpace(company) ? kv.Key : $"{kv.Key}:{company}";
                });
            opts["DisciplineTeam"] = string.Join(",", activeDisciplines);

            var activeUses = _bimUseChecks
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key);
            opts["BIMUses"] = string.Join(",", activeUses);

            // Tab 4: Standards & CDE
            opts["CDEPlatform"] = GetComboText(_cboCDEPlatform);
            opts["ClassificationSystem"] = GetComboText(_cboClassification);
            opts["UnitsLength"] = GetComboText(_cboUnitsLength);
            opts["UnitsArea"] = GetComboText(_cboUnitsArea);
            opts["NamingConvention"] = _txtNamingConvention?.Text ?? "";
            opts["FormatNative"] = _txtFormatNative?.Text ?? "";
            opts["FormatExchange"] = _txtFormatExchange?.Text ?? "";
            opts["FormatDrawing"] = _txtFormatDrawing?.Text ?? "";
            opts["FormatData"] = _txtFormatData?.Text ?? "";

            return opts;
        }

        private static string GetComboText(ComboBox cbo)
        {
            if (cbo == null) return "";
            var item = cbo.SelectedItem;
            if (item is ComboBoxItem cbi) return cbi.Content?.ToString() ?? "";
            return item?.ToString() ?? "";
        }

        private static Border MakeOperationCard(string title, string description, SolidColorBrush accentColor,
            string dispatchTag, Window win, BEPDashboardResult result)
        {
            var card = new Border
            {
                Background = PanelBg,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(0, 1, 1, 1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left accent border
            var accent = new Border
            {
                Background = accentColor,
                CornerRadius = new CornerRadius(3, 0, 0, 3)
            };
            Grid.SetColumn(accent, 0);
            grid.Children.Add(accent);

            // Text content
            var textStack = new StackPanel
            {
                Margin = new Thickness(12, 10, 8, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FgDark
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = FgMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Run button
            var btn = MakeButton("Run", 60, accentColor, FgWhite);
            btn.Margin = new Thickness(0, 0, 10, 0);
            btn.VerticalAlignment = VerticalAlignment.Center;
            btn.Click += (s, e) =>
            {
                result.Options = CollectAllFields();
                result.Operation = dispatchTag;
                result.Confirmed = true;
                win.DialogResult = true;
            };
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);

            card.Child = grid;

            // Hover effect
            card.MouseEnter += (s, e) => card.Background = FZ(Color.FromRgb(0xF0, 0xF0, 0xF8));
            card.MouseLeave += (s, e) => card.Background = PanelBg;

            return card;
        }

        private static TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeaderBg,
                Margin = new Thickness(0, 16, 0, 8)
            };
        }

        private static TextBox AddLabeledTextBox(StackPanel parent, string label, string defaultValue)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = FgDark,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var txt = new TextBox
            {
                Text = defaultValue,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = BorderLight,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 1);
            grid.Children.Add(txt);

            parent.Children.Add(grid);
            return txt;
        }

        private static ComboBox AddLabeledComboBox(StackPanel parent, string label, string[] items, string defaultValue)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = FgDark,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var cbo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = BorderLight,
                VerticalAlignment = VerticalAlignment.Center
            };

            int selectedIndex = 0;
            for (int i = 0; i < items.Length; i++)
            {
                cbo.Items.Add(items[i]);
                if (items[i] == defaultValue) selectedIndex = i;
            }
            cbo.SelectedIndex = selectedIndex;

            Grid.SetColumn(cbo, 1);
            grid.Children.Add(cbo);

            parent.Children.Add(grid);
            return cbo;
        }

        private static Button MakeButton(string text, double width, SolidColorBrush bg, SolidColorBrush fg)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = bg,
                Foreground = fg,
                BorderBrush = bg,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Padding = new Thickness(8, 2, 8, 2)
            };
        }
    }
}
