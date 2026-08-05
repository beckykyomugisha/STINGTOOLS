// ══════════════════════════════════════════════════════════════════════════
//  BOQTenderDialog.cs — Phase 108h
//  Pre-export tender dialog collecting every field a senior QS needs to
//  produce a proper NRM2 Bill of Quantities. Four-tab WPF dialog:
//    1. Project Identity        — employer, project, address, GIA, type
//    2. Professional Team       — QS, architect, engineers, contractor
//    3. Contract                — form, dates, retention, DLP, bond, LDs
//    4. Pricing & Output        — pricing mode, revision, watermark, sheet
//                                 inclusion toggles, carbon, currency
//  All fields are persisted to project_config.json under BOQ_TENDER_* keys
//  so the next export recalls the last-used values.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
// Disambiguate WPF vs Revit short names
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;   // collides with Autodesk.Revit.DB.Color

namespace StingTools.BOQ
{
    internal class BOQTenderDialog : Window
    {
        private static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5C));
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly Brush BorderColorLight = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
        private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFB));

        private readonly BOQTenderConfig _config;
        private readonly Document _doc;
        private bool _accepted;

        // ── Project types with their preamble profiles ─────────────────────
        // Keeping this list central so both the dialog dropdown and the
        // BOQProfessionalExportCommand preamble selector stay in sync.
        public static readonly string[] ProjectTypes = new[]
        {
            "Commercial Office",
            "Residential — Low-Rise",
            "Residential — High-Rise",
            "Healthcare — NHS / Public",
            "Healthcare — Private",
            "Education — Primary / Secondary",
            "Education — Higher / FE",
            "Retail",
            "Hotel / Hospitality",
            "Data Centre",
            "Industrial / Manufacturing",
            "Mixed-Use",
            "Heritage / Conservation",
            "Sports & Leisure",
            "Cultural — Museum / Gallery",
            "Laboratory / Research",
            "Transport / Infrastructure",
            "Defence / MOD",
            "Religious",
            "Other",
        };

        public static readonly string[] FormsOfContract = new[]
        {
            "JCT Standard Building Contract with Quantities (SBC/Q)",
            "JCT Standard Building Contract without Quantities (SBC/XQ)",
            "JCT Intermediate Building Contract (IC)",
            "JCT Minor Works Building Contract (MW)",
            "JCT Design and Build Contract (DB)",
            "NEC4 Engineering & Construction Contract — Option A (priced lump sum)",
            "NEC4 Engineering & Construction Contract — Option B (priced BoQ)",
            "NEC4 Engineering & Construction Contract — Option C (target contract)",
            "FIDIC Red Book — Construction (Employer's Design)",
            "FIDIC Yellow Book — Plant & Design-Build",
            "FIDIC Silver Book — EPC / Turnkey",
            "PPDA (Uganda) Standard Bidding Document — Works",
            "East African Procurement Standard — Works",
            "Bespoke / other",
        };

        public static readonly string[] RibaStages = new[]
        {
            "RIBA Stage 0 — Strategic Definition",
            "RIBA Stage 1 — Preparation and Briefing",
            "RIBA Stage 2 — Concept Design",
            "RIBA Stage 3 — Spatial Coordination",
            "RIBA Stage 4 — Technical Design",
            "RIBA Stage 5 — Manufacturing and Construction",
            "RIBA Stage 6 — Handover",
            "RIBA Stage 7 — Use",
        };

        public static readonly string[] Currencies = { "UGX", "USD", "GBP", "EUR", "KES", "TZS", "RWF", "ZAR" };

        public static readonly string[] WatermarkOptions = { "(none)", "DRAFT", "TENDER", "CONFIDENTIAL", "PRELIMINARY", "FOR REVIEW", "SUPERSEDED" };

        public BOQTenderDialog(BOQTenderConfig initialConfig, Document doc)
        {
            _config = initialConfig ?? new BOQTenderConfig();
            _doc = doc;
            Title = "Export Tender BOQ — Pre-Export Setup";
            Width = 960;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = PanelBg;
            FontFamily = new FontFamily("Segoe UI");
            BuildUi();
        }

        public BOQTenderConfig Result => _accepted ? _config : null;

        /// <summary>
        /// Config instance bound to the 4 tabs. Exposed for the inline
        /// panel flow (BOQCostManagerPanel.BuildTenderSetupView) so
        /// edits in the tab field handlers mutate this object directly,
        /// letting the caller persist it without closing a modal window.
        /// </summary>
        public BOQTenderConfig Config => _config;

        /// <summary>
        /// Build a fresh TabControl containing the same 4 tabs the modal
        /// dialog uses. Used by BOQCostManagerPanel to host the tender
        /// setup inline — no modal, no OS-level window. The tab builders
        /// write straight into this dialog's _config so the caller gets
        /// the edited values via the Config property.
        /// </summary>
        public TabControl CreateInlineTabs()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            tabs.Items.Add(new TabItem { Header = "1. Project Identity", Content = BuildProjectTab() });
            tabs.Items.Add(new TabItem { Header = "2. Professional Team", Content = BuildTeamTab() });
            tabs.Items.Add(new TabItem { Header = "3. Contract",          Content = BuildContractTab() });
            tabs.Items.Add(new TabItem { Header = "4. Pricing & Output",  Content = BuildOutputTab() });
            return tabs;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Layout
        // ══════════════════════════════════════════════════════════════════

        private void BuildUi()
        {
            var root = new DockPanel { LastChildFill = true };

            // Header strip
            var header = new Border { Background = Navy, Padding = new Thickness(16, 12, 16, 12) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "TENDER BILL OF QUANTITIES — EXPORT",
                Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Complete the fields below, then click Export to generate a tender-grade NRM2 BOQ. Values are persisted to project_config.json.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xE0)),
                FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer buttons
            var footer = new Border { Background = Brushes.White, BorderBrush = BorderColorLight,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 16, 10) };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusHint = new TextBlock
            {
                Text = "All fields are optional. Missing values are replaced with [Token] placeholders in the output.",
                Foreground = Brushes.Gray, FontSize = 10, FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusHint, 0);
            footerGrid.Children.Add(statusHint);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var cancelBtn = new Button
            {
                Content = "Cancel", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true, Cursor = Cursors.Hand
            };
            var exportBtn = new Button
            {
                Content = "Export ★", Width = 120, Height = 30, FontWeight = FontWeights.Bold,
                Background = Orange, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                IsDefault = true, Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { _accepted = false; DialogResult = false; Close(); };
            exportBtn.Click += (s, e) => { Accept(); };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(exportBtn);
            Grid.SetColumn(btnRow, 1);
            footerGrid.Children.Add(btnRow);

            footer.Child = footerGrid;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Tab content
            var tabs = new TabControl { Margin = new Thickness(12), Background = Brushes.Transparent };
            tabs.Items.Add(new TabItem { Header = "1. Project Identity", Content = BuildProjectTab() });
            tabs.Items.Add(new TabItem { Header = "2. Professional Team", Content = BuildTeamTab() });
            tabs.Items.Add(new TabItem { Header = "3. Contract",          Content = BuildContractTab() });
            tabs.Items.Add(new TabItem { Header = "4. Pricing & Output",  Content = BuildOutputTab() });
            root.Children.Add(tabs);

            Content = root;
        }

        // ── Small helpers ───────────────────────────────────────────────────

        private Grid TwoColumnGrid()
        {
            var g = new Grid { Margin = new Thickness(12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private int AddRow(Grid g, string label, UIElement input, int row)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock
            {
                Text = label, FontSize = 11, Foreground = Navy, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 10, 8), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            if (input is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(input, row); Grid.SetColumn(input, 1);
            g.Children.Add(input);
            return row + 1;
        }

        private int AddSectionHeader(Grid g, string text, int row)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new TextBlock
            {
                Text = text.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Orange, Margin = new Thickness(0, 14, 0, 4)
            };
            Grid.SetRow(header, row); Grid.SetColumnSpan(header, 2);
            g.Children.Add(header);
            return row + 1;
        }

        private TextBox MakeTextBox(string current, Action<string> onChange, bool multiline = false)
        {
            var tb = new TextBox
            {
                Text = current ?? "", FontSize = 11, Padding = new Thickness(6, 3, 6, 3),
                BorderBrush = BorderColorLight, Height = multiline ? 60 : 26,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                AcceptsReturn = multiline, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
            };
            tb.TextChanged += (s, e) => onChange(tb.Text ?? "");
            return tb;
        }

        private ComboBox MakeComboBox(IEnumerable<string> items, string current, Action<string> onChange, bool editable = false)
        {
            var cb = new ComboBox
            {
                FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Height = 26,
                IsEditable = editable, BorderBrush = BorderColorLight
            };
            foreach (var it in items) cb.Items.Add(it);
            cb.Text = current ?? "";
            if (cb.Items.Contains(current)) cb.SelectedItem = current;
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string v) onChange(v); };
            if (editable) cb.LostFocus += (s, e) => onChange(cb.Text ?? "");
            return cb;
        }

        private CheckBox MakeCheckBox(string label, bool current, Action<bool> onChange)
        {
            var cb = new CheckBox
            {
                Content = label, FontSize = 11, IsChecked = current, Margin = new Thickness(0, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked   += (s, e) => onChange(true);
            cb.Unchecked += (s, e) => onChange(false);
            return cb;
        }

        private TextBox MakeNumberBox(double current, Action<double> onChange, string format = "0.##")
        {
            var tb = MakeTextBox(current.ToString(format, CultureInfo.InvariantCulture), _ => { });
            tb.LostFocus += (s, e) =>
            {
                if (double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    onChange(v);
                else
                    tb.Text = current.ToString(format, CultureInfo.InvariantCulture);
            };
            return tb;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tab 1: Project Identity
        // ══════════════════════════════════════════════════════════════════

        private UIElement BuildProjectTab()
        {
            var g = TwoColumnGrid();
            int r = 0;
            r = AddSectionHeader(g, "Employer / Client", r);
            r = AddRow(g, "Employer name",     MakeTextBox(_config.Employer,         v => _config.Employer = v),         r);
            r = AddRow(g, "Employer address",  MakeTextBox(_config.EmployerAddress,  v => _config.EmployerAddress = v, true), r);

            r = AddSectionHeader(g, "Project", r);
            r = AddRow(g, "Project name",      MakeTextBox(_config.ProjectName,      v => _config.ProjectName = v),      r);
            r = AddRow(g, "Project number",    MakeTextBox(_config.ProjectNumber,    v => _config.ProjectNumber = v),    r);
            r = AddRow(g, "Project address",   MakeTextBox(_config.ProjectAddress,   v => _config.ProjectAddress = v, true), r);

            r = AddRow(g, "Work stage",        MakeComboBox(RibaStages, _config.WorkStage, v => _config.WorkStage = v, editable: true), r);
            r = AddRow(g, "Project type / sector", MakeComboBox(ProjectTypes, _config.ProjectType, v => _config.ProjectType = v, editable: true), r);

            r = AddSectionHeader(g, "Areas (ISO 9836 / RICS Code of Measuring Practice)", r);
            r = AddRow(g, "Gross Internal Area (m²)", MakeNumberBox(_config.GrossInternalAreaM2, v => _config.GrossInternalAreaM2 = v, "0.#"), r);
            r = AddRow(g, "Net Internal Area (m²)",   MakeNumberBox(_config.NetInternalAreaM2,   v => _config.NetInternalAreaM2   = v, "0.#"), r);

            var scroll = new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return scroll;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tab 2: Professional Team
        // ══════════════════════════════════════════════════════════════════

        private UIElement BuildTeamTab()
        {
            var g = TwoColumnGrid();
            int r = 0;
            r = AddSectionHeader(g, "Quantity Surveyor", r);
            r = AddRow(g, "QS firm",            MakeTextBox(_config.QsFirm,    v => _config.QsFirm = v),    r);
            r = AddRow(g, "QS office address",  MakeTextBox(_config.QsAddress, v => _config.QsAddress = v, true), r);
            r = AddRow(g, "QS contact person",  MakeTextBox(_config.QsContact, v => _config.QsContact = v), r);
            r = AddRow(g, "QS email / phone",   MakeTextBox(_config.QsEmail,   v => _config.QsEmail = v),   r);

            r = AddSectionHeader(g, "Design Team", r);
            r = AddRow(g, "Architect",          MakeTextBox(_config.Architect,          v => _config.Architect = v),          r);
            r = AddRow(g, "Structural Engineer",MakeTextBox(_config.StructuralEngineer, v => _config.StructuralEngineer = v), r);
            r = AddRow(g, "Services Engineer",  MakeTextBox(_config.ServicesEngineer,   v => _config.ServicesEngineer = v),   r);
            r = AddRow(g, "Principal Designer (CDM)", MakeTextBox(_config.PrincipalDesigner, v => _config.PrincipalDesigner = v), r);

            r = AddSectionHeader(g, "Employer Representation", r);
            r = AddRow(g, "Project Manager",    MakeTextBox(_config.ProjectManager,   v => _config.ProjectManager = v),   r);
            r = AddRow(g, "Employer's Agent",   MakeTextBox(_config.EmployersAgent,   v => _config.EmployersAgent = v),   r);

            r = AddSectionHeader(g, "Contractor", r);
            r = AddRow(g, "Main Contractor",    MakeTextBox(_config.Contractor,       v => _config.Contractor = v),       r);

            var scroll = new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return scroll;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tab 3: Contract
        // ══════════════════════════════════════════════════════════════════

        private UIElement BuildContractTab()
        {
            var g = TwoColumnGrid();
            int r = 0;
            r = AddSectionHeader(g, "Form of Contract", r);
            r = AddRow(g, "Form of contract",   MakeComboBox(FormsOfContract, _config.FormOfContract, v => _config.FormOfContract = v, editable: true), r);
            r = AddRow(g, "Contract period",    MakeTextBox(_config.ContractPeriod,       v => _config.ContractPeriod = v),       r);
            r = AddRow(g, "Date for possession",MakeTextBox(_config.DateForPossession,    v => _config.DateForPossession = v),    r);
            r = AddRow(g, "Sectional completion", MakeTextBox(_config.SectionalCompletion, v => _config.SectionalCompletion = v, true), r);

            r = AddSectionHeader(g, "Damages and Retention", r);
            r = AddRow(g, "Liquidated damages", MakeTextBox(_config.LiquidatedDamages,    v => _config.LiquidatedDamages = v),    r);
            r = AddRow(g, "Defects Liability",  MakeTextBox(_config.DefectsLiabilityPeriod, v => _config.DefectsLiabilityPeriod = v), r);
            r = AddRow(g, "Retention scheme",   MakeTextBox(_config.RetentionScheme,      v => _config.RetentionScheme = v),      r);

            r = AddSectionHeader(g, "Fluctuations, Bonds & Warranties", r);
            r = AddRow(g, "Fluctuations basis", MakeTextBox(_config.Fluctuations,         v => _config.Fluctuations = v),         r);
            r = AddRow(g, "Bond requirement",   MakeTextBox(_config.BondRequirement,      v => _config.BondRequirement = v, true), r);
            r = AddRow(g, "Warranty requirement", MakeTextBox(_config.WarrantyRequirement, v => _config.WarrantyRequirement = v, true), r);

            var scroll = new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return scroll;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Tab 4: Pricing & Output
        // ══════════════════════════════════════════════════════════════════

        private UIElement BuildOutputTab()
        {
            var g = TwoColumnGrid();
            int r = 0;

            r = AddSectionHeader(g, "Pricing Mode", r);
            var modeCombo = new ComboBox { FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Height = 26, BorderBrush = BorderColorLight };
            modeCombo.Items.Add("Tender Issue — blank rates (bidder prices)");
            modeCombo.Items.Add("Priced Copy — rates and totals visible (internal QS)");
            modeCombo.Items.Add("Contract Copy — executed, rates visible");
            modeCombo.Items.Add("As-Built — final agreed rates after variations");
            modeCombo.SelectedIndex = (int)_config.PricingMode;
            modeCombo.SelectionChanged += (s, e) => _config.PricingMode = (BOQPricingMode)modeCombo.SelectedIndex;
            r = AddRow(g, "Pricing mode", modeCombo, r);

            r = AddRow(g, "Tender submission deadline", MakeTextBox(_config.TenderSubmissionDeadline, v => _config.TenderSubmissionDeadline = v), r);
            r = AddRow(g, "Pricing notes", MakeTextBox(_config.PricingNotes, v => _config.PricingNotes = v, true), r);

            r = AddSectionHeader(g, "Currency & Markups", r);
            r = AddRow(g, "Currency",               MakeComboBox(Currencies, _config.Currency, v => _config.Currency = v, editable: true), r);
            r = AddRow(g, "Exchange rate (UGX / USD)", MakeNumberBox(_config.ExchangeRateUgxPerUsd, v => _config.ExchangeRateUgxPerUsd = v, "0.##"), r);
            r = AddRow(g, "Preliminaries %",        MakeNumberBox(_config.PreliminariesPct,    v => _config.PreliminariesPct = v, "0.##"),    r);
            r = AddRow(g, "Contingency %",          MakeNumberBox(_config.ContingencyPct,      v => _config.ContingencyPct = v, "0.##"),      r);
            r = AddRow(g, "Overhead & Profit %",    MakeNumberBox(_config.OverheadProfitPct,   v => _config.OverheadProfitPct = v, "0.##"),   r);
            r = AddRow(g, "VAT %",                  MakeNumberBox(_config.VatPct,              v => _config.VatPct = v, "0.##"),              r);

            r = AddSectionHeader(g, "Revision", r);
            r = AddRow(g, "Revision code",          MakeTextBox(_config.Revision,              v => _config.Revision = v),              r);
            r = AddRow(g, "Revision date",          MakeTextBox(string.IsNullOrEmpty(_config.RevisionDate) ? DateTime.Now.ToString("yyyy-MM-dd") : _config.RevisionDate,
                                                                v => _config.RevisionDate = v), r);
            r = AddRow(g, "Revision description",   MakeTextBox(_config.RevisionDescription,   v => _config.RevisionDescription = v),   r);
            r = AddRow(g, "Author",                 MakeTextBox(_config.Author,                v => _config.Author = v),                r);
            r = AddRow(g, "Checked by",             MakeTextBox(_config.CheckedBy,             v => _config.CheckedBy = v),             r);
            r = AddRow(g, "Approved by",            MakeTextBox(_config.ApprovedBy,            v => _config.ApprovedBy = v),            r);

            r = AddSectionHeader(g, "Output Options", r);
            r = AddRow(g, "Watermark",              MakeComboBox(WatermarkOptions, string.IsNullOrEmpty(_config.Watermark) ? "(none)" : _config.Watermark,
                                                                 v => _config.Watermark = v == "(none)" ? "" : v, editable: true), r);

            // Checkbox group
            var boxPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            boxPanel.Children.Add(MakeCheckBox("Include timestamp in footer",         _config.IncludeTimestamp,         v => _config.IncludeTimestamp = v));
            boxPanel.Children.Add(MakeCheckBox("Include Cover page",                  _config.IncludeCover,             v => _config.IncludeCover = v));
            boxPanel.Children.Add(MakeCheckBox("Include Document Control",            _config.IncludeDocumentControl,   v => _config.IncludeDocumentControl = v));
            boxPanel.Children.Add(MakeCheckBox("Include Contents",                    _config.IncludeContents,          v => _config.IncludeContents = v));
            boxPanel.Children.Add(MakeCheckBox("Include Preliminaries (NRM2 Part 1)", _config.IncludePreliminaries,     v => _config.IncludePreliminaries = v));
            boxPanel.Children.Add(MakeCheckBox("Include Preambles to Trades",         _config.IncludePreambles,         v => _config.IncludePreambles = v));
            boxPanel.Children.Add(MakeCheckBox("Include Collections",                 _config.IncludeCollections,       v => _config.IncludeCollections = v));
            boxPanel.Children.Add(MakeCheckBox("Include Grand Summary",               _config.IncludeGrandSummary,      v => _config.IncludeGrandSummary = v));
            boxPanel.Children.Add(MakeCheckBox("Include Annexure (PS + Dayworks)",    _config.IncludeAnnexure,          v => _config.IncludeAnnexure = v));
            boxPanel.Children.Add(MakeCheckBox("Include carbon data (EPDs / kgCO₂e)", _config.IncludeCarbonData,        v => _config.IncludeCarbonData = v));
            boxPanel.Children.Add(MakeCheckBox("Show Provisional Sums as separate rows", _config.ShowProvisionalSumsSeparately, v => _config.ShowProvisionalSumsSeparately = v));
            boxPanel.Children.Add(MakeCheckBox("Show dual currency (USD alongside UGX)", _config.ShowDualCurrency,       v => _config.ShowDualCurrency = v));
            boxPanel.Children.Add(MakeCheckBox("Apply client vocabulary overlay",     _config.UseClientVocabulary,      v => _config.UseClientVocabulary = v));
            boxPanel.Children.Add(MakeCheckBox("Include Drawing Schedule annexure",   _config.IncludeDrawingSchedule,   v => _config.IncludeDrawingSchedule = v));
            boxPanel.Children.Add(MakeCheckBox("Include Specification Schedule annexure", _config.IncludeSpecificationSchedule, v => _config.IncludeSpecificationSchedule = v));
            boxPanel.Children.Add(MakeCheckBox("Include Prime Cost / PC Sums schedule", _config.IncludePrimeCostSchedule, v => _config.IncludePrimeCostSchedule = v));
            boxPanel.Children.Add(MakeCheckBox("Include Dayworks framework",          _config.IncludeDayworksSchedule,  v => _config.IncludeDayworksSchedule = v));

            // Phase 108i — Paragraph automation block
            var enhHeader = new TextBlock
            {
                Text = "PARAGRAPH AUTOMATION", FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Orange, Margin = new Thickness(0, 12, 0, 4)
            };
            boxPanel.Children.Add(enhHeader);
            var enhHint = new TextBlock
            {
                Text = "These enhancements augment every BOQ description before the bill sheets are rendered.",
                FontSize = 10, Foreground = Brushes.Gray, FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 4)
            };
            boxPanel.Children.Add(enhHint);
            boxPanel.Children.Add(MakeCheckBox("P1 · Performance clauses (fire / Rw / U-value / velocity)", _config.EnablePerformanceClauses,   v => _config.EnablePerformanceClauses = v));
            boxPanel.Children.Add(MakeCheckBox("P2 · Compliance reference (BS / BS EN per category)",        _config.EnableComplianceClauses,    v => _config.EnableComplianceClauses = v));
            boxPanel.Children.Add(MakeCheckBox("P3 · Dimensional groupings (Schedule of Sizes annexure)",    _config.EnableDimensionalGroupings, v => _config.EnableDimensionalGroupings = v));
            boxPanel.Children.Add(MakeCheckBox("P4 · Auto-inclusion boilerplate (fixings / supports / commissioning)", _config.EnableAutoInclusionBoiler, v => _config.EnableAutoInclusionBoiler = v));
            boxPanel.Children.Add(MakeCheckBox("P5 · \"Or approved equivalent\"",                              _config.EnableOrApprovedEquivalent, v => _config.EnableOrApprovedEquivalent = v));
            boxPanel.Children.Add(MakeCheckBox("P6 · Conditional design clauses (Structural / Services Engineer)", _config.EnableConditionalClauses, v => _config.EnableConditionalClauses = v));
            boxPanel.Children.Add(MakeCheckBox("P7 · Client vocabulary overlay (BOQ_CLIENT_VOCABULARY.json)", _config.UseClientVocabulary,        v => _config.UseClientVocabulary = v));
            boxPanel.Children.Add(MakeCheckBox("P8 · Smart item naming (material + thickness + finish)",      _config.EnableSmartItemNaming,      v => _config.EnableSmartItemNaming = v));
            boxPanel.Children.Add(MakeCheckBox("P9 · Specification-clause cross-references",                  _config.EnableSpecClauseCrossRefs,  v => _config.EnableSpecClauseCrossRefs = v));
            boxPanel.Children.Add(MakeCheckBox("P10 · Emit CSV + JSON sidecars alongside .xlsx",              _config.EmitCsvJsonSidecars,        v => _config.EmitCsvJsonSidecars = v));

            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(boxPanel, r); Grid.SetColumnSpan(boxPanel, 2);
            g.Children.Add(boxPanel);
            r++;

            var scroll = new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return scroll;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Accept / persist
        // ══════════════════════════════════════════════════════════════════

        private void Accept()
        {
            if (string.IsNullOrWhiteSpace(_config.RevisionDate))
                _config.RevisionDate = DateTime.Now.ToString("yyyy-MM-dd");
            try { PersistToConfig(_config); }
            catch (Exception ex) { StingLog.Warn($"BOQTenderDialog persist: {ex.Message}"); }
            _accepted = true;
            DialogResult = true;
            Close();
        }

        // ── Persistence — each field has a stable BOQ_TENDER_* key ─────────

        public static BOQTenderConfig LoadFromConfig(Document doc)
        {
            var c = new BOQTenderConfig();
            string Get(string k) => TagConfig.GetConfigValue("BOQ_TENDER_" + k) ?? "";
            double GetD(string k, double d) => TagConfig.GetConfigDouble("BOQ_TENDER_" + k, d);
            bool GetB(string k, bool d)
            {
                string v = Get(k);
                if (string.IsNullOrEmpty(v)) return d;
                return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            c.Employer                 = Fallback(Get("EMPLOYER"),                ReadBip(doc, BuiltInParameter.CLIENT_NAME));
            c.EmployerAddress          = Get("EMPLOYER_ADDRESS");
            c.ProjectName              = Fallback(Get("PROJECT_NAME"),            ReadBip(doc, BuiltInParameter.PROJECT_NAME), doc?.Title);
            c.ProjectNumber            = Fallback(Get("PROJECT_NUMBER"),          ReadBip(doc, BuiltInParameter.PROJECT_NUMBER));
            c.ProjectAddress           = Fallback(Get("PROJECT_ADDRESS"),         ReadBip(doc, BuiltInParameter.PROJECT_ADDRESS));
            c.WorkStage                = Fallback(Get("WORK_STAGE"), c.WorkStage);
            c.ProjectType              = Fallback(Get("PROJECT_TYPE"), c.ProjectType);
            c.GrossInternalAreaM2      = GetD("GIA_M2", 0);
            c.NetInternalAreaM2        = GetD("NIA_M2", 0);
            c.QsFirm                   = Fallback(Get("QS_FIRM"), ReadBip(doc, BuiltInParameter.PROJECT_ORGANIZATION_NAME));
            c.QsAddress                = Get("QS_ADDRESS");
            c.QsContact                = Get("QS_CONTACT");
            c.QsEmail                  = Get("QS_EMAIL");
            c.Architect                = Get("ARCHITECT");
            c.StructuralEngineer       = Get("STRUCTURAL_ENGINEER");
            c.ServicesEngineer         = Get("SERVICES_ENGINEER");
            c.PrincipalDesigner        = Get("PRINCIPAL_DESIGNER");
            c.ProjectManager           = Get("PROJECT_MANAGER");
            c.EmployersAgent           = Get("EMPLOYERS_AGENT");
            c.Contractor               = Get("CONTRACTOR");
            c.FormOfContract           = Fallback(Get("FORM_OF_CONTRACT"), c.FormOfContract);
            c.ContractPeriod           = Get("CONTRACT_PERIOD");
            c.DateForPossession        = Get("DATE_FOR_POSSESSION");
            c.SectionalCompletion      = Get("SECTIONAL_COMPLETION");
            c.LiquidatedDamages        = Get("LIQUIDATED_DAMAGES");
            c.DefectsLiabilityPeriod   = Fallback(Get("DEFECTS_LIABILITY_PERIOD"), c.DefectsLiabilityPeriod);
            c.RetentionScheme          = Fallback(Get("RETENTION_SCHEME"), c.RetentionScheme);
            c.Fluctuations             = Fallback(Get("FLUCTUATIONS"), c.Fluctuations);
            c.BondRequirement          = Fallback(Get("BOND_REQUIREMENT"), c.BondRequirement);
            c.WarrantyRequirement      = Fallback(Get("WARRANTY_REQUIREMENT"), c.WarrantyRequirement);
            if (Enum.TryParse(Get("PRICING_MODE"), true, out BOQPricingMode pm)) c.PricingMode = pm;
            c.TenderSubmissionDeadline = Get("TENDER_DEADLINE");
            c.Currency                 = Fallback(Get("CURRENCY"), c.Currency);
            c.ExchangeRateUgxPerUsd    = GetD("UGX_PER_USD", c.ExchangeRateUgxPerUsd);
            c.PreliminariesPct         = GetD("PRELIMINARIES_PCT", c.PreliminariesPct);
            c.ContingencyPct           = GetD("CONTINGENCY_PCT", c.ContingencyPct);
            c.OverheadProfitPct        = GetD("OHP_PCT", c.OverheadProfitPct);
            c.VatPct                   = GetD("VAT_PCT", c.VatPct);
            c.Revision                 = Fallback(Get("REVISION"), c.Revision);
            c.RevisionDate             = Get("REVISION_DATE");
            c.RevisionDescription      = Fallback(Get("REVISION_DESCRIPTION"), c.RevisionDescription);
            c.Author                   = Get("AUTHOR");
            c.CheckedBy                = Get("CHECKED_BY");
            c.ApprovedBy               = Get("APPROVED_BY");
            c.Watermark                = Get("WATERMARK");
            c.PricingNotes             = Get("PRICING_NOTES");
            c.IncludeTimestamp                = GetB("INCLUDE_TIMESTAMP", true);
            c.IncludeCover                    = GetB("INCLUDE_COVER", true);
            c.IncludeDocumentControl          = GetB("INCLUDE_DOC_CONTROL", true);
            c.IncludeContents                 = GetB("INCLUDE_CONTENTS", true);
            c.IncludePreliminaries            = GetB("INCLUDE_PRELIMINARIES", true);
            c.IncludePreambles                = GetB("INCLUDE_PREAMBLES", true);
            c.IncludeCollections              = GetB("INCLUDE_COLLECTIONS", true);
            c.IncludeGrandSummary             = GetB("INCLUDE_GRAND_SUMMARY", true);
            c.IncludeAnnexure                 = GetB("INCLUDE_ANNEXURE", true);
            c.IncludeCarbonData               = GetB("INCLUDE_CARBON", false);
            c.ShowProvisionalSumsSeparately   = GetB("SHOW_PS_SEPARATE", true);
            c.ShowDualCurrency                = GetB("SHOW_DUAL_CURRENCY", false);
            c.UseClientVocabulary             = GetB("USE_CLIENT_VOCAB", false);
            c.IncludeDrawingSchedule          = GetB("INCLUDE_DRAWINGS", true);
            c.IncludeSpecificationSchedule    = GetB("INCLUDE_SPEC", false);
            c.IncludePrimeCostSchedule        = GetB("INCLUDE_PC", true);
            c.IncludeDayworksSchedule         = GetB("INCLUDE_DAYWORKS", true);
            // Phase 108i — paragraph automation
            c.EnablePerformanceClauses        = GetB("P1_PERFORMANCE", true);
            c.EnableComplianceClauses         = GetB("P2_COMPLIANCE", true);
            c.EnableDimensionalGroupings      = GetB("P3_DIM_GROUPS", false);
            c.EnableAutoInclusionBoiler       = GetB("P4_INCLUSION", true);
            c.EnableOrApprovedEquivalent      = GetB("P5_OR_EQUIV", true);
            c.EnableConditionalClauses        = GetB("P6_CONDITIONAL", true);
            c.EnableSmartItemNaming           = GetB("P8_SMART_NAME", false);
            c.EnableSpecClauseCrossRefs       = GetB("P9_SPEC_REF", true);
            c.EmitCsvJsonSidecars             = GetB("P10_SIDECARS", true);
            return c;
        }

        public static void PersistToConfig(BOQTenderConfig c)
        {
            void Set(string k, string v) => TagConfig.SetConfigValue("BOQ_TENDER_" + k, v ?? "");
            void SetD(string k, double v) => Set(k, v.ToString("G", CultureInfo.InvariantCulture));
            void SetB(string k, bool v) => Set(k, v ? "true" : "false");

            Set("EMPLOYER", c.Employer); Set("EMPLOYER_ADDRESS", c.EmployerAddress);
            Set("PROJECT_NAME", c.ProjectName); Set("PROJECT_NUMBER", c.ProjectNumber);
            Set("PROJECT_ADDRESS", c.ProjectAddress); Set("WORK_STAGE", c.WorkStage);
            Set("PROJECT_TYPE", c.ProjectType);
            SetD("GIA_M2", c.GrossInternalAreaM2); SetD("NIA_M2", c.NetInternalAreaM2);
            Set("QS_FIRM", c.QsFirm); Set("QS_ADDRESS", c.QsAddress);
            Set("QS_CONTACT", c.QsContact); Set("QS_EMAIL", c.QsEmail);
            Set("ARCHITECT", c.Architect); Set("STRUCTURAL_ENGINEER", c.StructuralEngineer);
            Set("SERVICES_ENGINEER", c.ServicesEngineer); Set("PRINCIPAL_DESIGNER", c.PrincipalDesigner);
            Set("PROJECT_MANAGER", c.ProjectManager); Set("EMPLOYERS_AGENT", c.EmployersAgent);
            Set("CONTRACTOR", c.Contractor);
            Set("FORM_OF_CONTRACT", c.FormOfContract); Set("CONTRACT_PERIOD", c.ContractPeriod);
            Set("DATE_FOR_POSSESSION", c.DateForPossession); Set("SECTIONAL_COMPLETION", c.SectionalCompletion);
            Set("LIQUIDATED_DAMAGES", c.LiquidatedDamages); Set("DEFECTS_LIABILITY_PERIOD", c.DefectsLiabilityPeriod);
            Set("RETENTION_SCHEME", c.RetentionScheme); Set("FLUCTUATIONS", c.Fluctuations);
            Set("BOND_REQUIREMENT", c.BondRequirement); Set("WARRANTY_REQUIREMENT", c.WarrantyRequirement);
            Set("PRICING_MODE", c.PricingMode.ToString()); Set("TENDER_DEADLINE", c.TenderSubmissionDeadline);
            Set("CURRENCY", c.Currency); SetD("UGX_PER_USD", c.ExchangeRateUgxPerUsd);
            SetD("PRELIMINARIES_PCT", c.PreliminariesPct); SetD("CONTINGENCY_PCT", c.ContingencyPct);
            SetD("OHP_PCT", c.OverheadProfitPct); SetD("VAT_PCT", c.VatPct);
            Set("REVISION", c.Revision); Set("REVISION_DATE", c.RevisionDate);
            Set("REVISION_DESCRIPTION", c.RevisionDescription); Set("AUTHOR", c.Author);
            Set("CHECKED_BY", c.CheckedBy); Set("APPROVED_BY", c.ApprovedBy);
            Set("WATERMARK", c.Watermark); Set("PRICING_NOTES", c.PricingNotes);
            SetB("INCLUDE_TIMESTAMP", c.IncludeTimestamp); SetB("INCLUDE_COVER", c.IncludeCover);
            SetB("INCLUDE_DOC_CONTROL", c.IncludeDocumentControl); SetB("INCLUDE_CONTENTS", c.IncludeContents);
            SetB("INCLUDE_PRELIMINARIES", c.IncludePreliminaries); SetB("INCLUDE_PREAMBLES", c.IncludePreambles);
            SetB("INCLUDE_COLLECTIONS", c.IncludeCollections); SetB("INCLUDE_GRAND_SUMMARY", c.IncludeGrandSummary);
            SetB("INCLUDE_ANNEXURE", c.IncludeAnnexure); SetB("INCLUDE_CARBON", c.IncludeCarbonData);
            SetB("SHOW_PS_SEPARATE", c.ShowProvisionalSumsSeparately); SetB("SHOW_DUAL_CURRENCY", c.ShowDualCurrency);
            SetB("USE_CLIENT_VOCAB", c.UseClientVocabulary);
            SetB("INCLUDE_DRAWINGS", c.IncludeDrawingSchedule); SetB("INCLUDE_SPEC", c.IncludeSpecificationSchedule);
            SetB("INCLUDE_PC", c.IncludePrimeCostSchedule); SetB("INCLUDE_DAYWORKS", c.IncludeDayworksSchedule);
            // Phase 108i — paragraph automation
            SetB("P1_PERFORMANCE", c.EnablePerformanceClauses);
            SetB("P2_COMPLIANCE",  c.EnableComplianceClauses);
            SetB("P3_DIM_GROUPS",  c.EnableDimensionalGroupings);
            SetB("P4_INCLUSION",   c.EnableAutoInclusionBoiler);
            SetB("P5_OR_EQUIV",    c.EnableOrApprovedEquivalent);
            SetB("P6_CONDITIONAL", c.EnableConditionalClauses);
            SetB("P8_SMART_NAME",  c.EnableSmartItemNaming);
            SetB("P9_SPEC_REF",    c.EnableSpecClauseCrossRefs);
            SetB("P10_SIDECARS",   c.EmitCsvJsonSidecars);
        }

        private static string ReadBip(Document doc, BuiltInParameter bip)
        {
            try { return doc?.ProjectInformation?.get_Parameter(bip)?.AsString() ?? ""; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        private static string Fallback(params string[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
