using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using WpfColor = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING BEP Wizard WPF dialog.
    /// 6-page wizard that collects all BIM Execution Plan inputs
    /// per ISO 19650-2 §5.3, then returns a <see cref="BEPWizardData"/>
    /// to the calling command for BEP generation.
    /// </summary>
    public partial class BEPWizard : Window
    {
        private const int TotalPages = 6;
        private int _currentPage = 0;

        /// <summary>Result data — populated when user clicks Create.</summary>
        public BEPWizardData WizardData { get; private set; }

        /// <summary>True when user clicked Create BEP (not Cancel).</summary>
        public bool CreateRequested { get; private set; }

        public BEPWizard()
        {
            InitializeComponent();
            BuildStepIndicators();
            UpdateNavigation();
        }

        // ── Pre-populate from Revit data ─────────────────────────────

        /// <summary>
        /// Pre-populates wizard fields from the current Revit document.
        /// Call this before ShowDialog().
        /// </summary>
        public void PrePopulate(Document doc)
        {
            try
            {
                ProjectInfo pi = doc.ProjectInformation;
                if (pi != null)
                {
                    if (!string.IsNullOrEmpty(pi.Name))
                        txtBepProjectName.Text = pi.Name;
                    if (!string.IsNullOrEmpty(pi.Number))
                        txtBepProjectNumber.Text = pi.Number;
                    if (!string.IsNullOrEmpty(pi.ClientName))
                        txtBepClientName.Text = pi.ClientName;
                    if (!string.IsNullOrEmpty(pi.Address))
                        txtBepAddress.Text = pi.Address;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BEP Wizard pre-populate: {ex.Message}");
            }
        }

        // ── Step Indicators ──────────────────────────────────────────

        private readonly string[] _pageNames =
        {
            "Project Info", "Template & Stage", "Team",
            "BIM Uses", "Standards & CDE", "Review"
        };

        private Border[] _stepBorders;
        private TextBlock[] _stepLabels;

        private void BuildStepIndicators()
        {
            _stepBorders = new Border[TotalPages];
            _stepLabels = new TextBlock[TotalPages];

            for (int i = 0; i < TotalPages; i++)
            {
                var border = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                    Margin = new Thickness(2), Background = new SolidColorBrush(WpfColor.FromRgb(0xE0, 0xE0, 0xE0))
                };
                var num = new TextBlock
                {
                    Text = (i + 1).ToString(), FontWeight = FontWeights.Bold,
                    FontSize = 12, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = num;
                _stepBorders[i] = border;

                var sp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 0, 8, 0) };
                sp.Children.Add(border);
                var lbl = new TextBlock
                {
                    Text = _pageNames[i], FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(0x99, 0x99, 0x99))
                };
                sp.Children.Add(lbl);
                _stepLabels[i] = lbl;
                stepIndicatorPanel.Children.Add(sp);
            }
        }

        private void UpdateStepIndicators()
        {
            for (int i = 0; i < TotalPages; i++)
            {
                if (i < _currentPage)
                {
                    _stepBorders[i].Background = new SolidColorBrush(WpfColor.FromRgb(0x4C, 0xAF, 0x50));
                    _stepLabels[i].Foreground = new SolidColorBrush(WpfColor.FromRgb(0x4C, 0xAF, 0x50));
                }
                else if (i == _currentPage)
                {
                    _stepBorders[i].Background = new SolidColorBrush(WpfColor.FromRgb(0x6A, 0x1B, 0x9A));
                    _stepLabels[i].Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6A, 0x1B, 0x9A));
                    _stepLabels[i].FontWeight = FontWeights.Bold;
                }
                else
                {
                    _stepBorders[i].Background = new SolidColorBrush(WpfColor.FromRgb(0xE0, 0xE0, 0xE0));
                    _stepLabels[i].Foreground = new SolidColorBrush(WpfColor.FromRgb(0x99, 0x99, 0x99));
                    _stepLabels[i].FontWeight = FontWeights.Normal;
                }
            }
            txtPageTitle.Text = $"Step {_currentPage + 1}: {_pageNames[_currentPage]}";
        }

        // ── Navigation ───────────────────────────────────────────────

        private void UpdateNavigation()
        {
            wizardTabs.SelectedIndex = _currentPage;
            UpdateStepIndicators();

            btnBack.Visibility = _currentPage > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            btnNext.Visibility = _currentPage < TotalPages - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            btnCreate.Visibility = _currentPage == TotalPages - 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            if (_currentPage == TotalPages - 1)
                BuildReviewSummary();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0) { _currentPage--; UpdateNavigation(); }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < TotalPages - 1) { _currentPage++; UpdateNavigation(); }
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            WizardData = CollectData();
            CreateRequested = true;
            DialogResult = true;
            Close();
        }

        // ── Data Collection ──────────────────────────────────────────

        private BEPWizardData CollectData()
        {
            var data = new BEPWizardData();

            // Page 1: Project Info
            data.ProjectName = txtBepProjectName.Text.Trim();
            data.ProjectNumber = txtBepProjectNumber.Text.Trim();
            data.ClientName = txtBepClientName.Text.Trim();
            data.ProjectAddress = txtBepAddress.Text.Trim();
            data.ProjectDescription = txtBepDescription.Text.Trim();
            data.SiteReference = txtBepSiteRef.Text.Trim();

            // Page 2: Template & Stage
            data.PresetKey = cmbBepPreset.SelectedIndex switch
            {
                0 => "UK_GOV",
                1 => "NBS_STANDARD",
                2 => "RESIDENTIAL",
                3 => "MINIMAL",
                _ => "NBS_STANDARD"
            };
            data.RIBAStage = cmbRibaStage.SelectedIndex >= 0 ? cmbRibaStage.SelectedIndex : 2;
            data.ProjectType = (cmbProjectType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "New Build";
            data.ProcurementRoute = (cmbProcurement.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Design and Build";

            // Page 3: Team
            data.LeadDesignerRole = cmbLeadDesigner.SelectedIndex switch
            {
                0 => "A",
                1 => "M",
                2 => "S",
                3 => "Multi",
                _ => "A"
            };
            data.LeadCompany = txtLeadCompany.Text.Trim();
            data.LeadContact = txtLeadContact.Text.Trim();
            data.BIMManager = txtBimManager.Text.Trim();
            data.BIMCoordinator = txtBimCoordinator.Text.Trim();

            data.DisciplineTeam = new Dictionary<string, string>();
            if (chkDiscA.IsChecked == true) data.DisciplineTeam["A"] = txtCompanyA.Text.Trim();
            if (chkDiscM.IsChecked == true) data.DisciplineTeam["M"] = txtCompanyM.Text.Trim();
            if (chkDiscE.IsChecked == true) data.DisciplineTeam["E"] = txtCompanyE.Text.Trim();
            if (chkDiscP.IsChecked == true) data.DisciplineTeam["P"] = txtCompanyP.Text.Trim();
            if (chkDiscS.IsChecked == true) data.DisciplineTeam["S"] = txtCompanyS.Text.Trim();
            if (chkDiscQ.IsChecked == true) data.DisciplineTeam["Q"] = txtCompanyQ.Text.Trim();
            if (chkDiscC.IsChecked == true) data.DisciplineTeam["C"] = txtCompanyC.Text.Trim();
            if (chkDiscFP.IsChecked == true) data.DisciplineTeam["FP"] = txtCompanyFP.Text.Trim();
            if (chkDiscL.IsChecked == true) data.DisciplineTeam["L"] = txtCompanyL.Text.Trim();

            // Page 4: BIM Uses
            data.BIMUses = new List<string>();
            if (chkUseDesignAuthoring.IsChecked == true) data.BIMUses.Add("Design Authoring");
            if (chkUseCoordination.IsChecked == true) data.BIMUses.Add("3D Coordination");
            if (chkUseQTO.IsChecked == true) data.BIMUses.Add("Quantity Take-Off");
            if (chkUseFM.IsChecked == true) data.BIMUses.Add("Facility Management");
            if (chkUseCOBie.IsChecked == true) data.BIMUses.Add("COBie Data Drops");
            if (chkUseVisualisation.IsChecked == true) data.BIMUses.Add("Visualisation");
            if (chkUseSimulation.IsChecked == true) data.BIMUses.Add("4D Scheduling");
            if (chkUseCostMgmt.IsChecked == true) data.BIMUses.Add("5D Cost Management");
            if (chkUseRecordModel.IsChecked == true) data.BIMUses.Add("Record Model");
            if (chkUseSustainability.IsChecked == true) data.BIMUses.Add("Sustainability Analysis");
            if (chkUseHealthSafety.IsChecked == true) data.BIMUses.Add("Health & Safety");
            data.AdditionalGoals = txtAdditionalGoals.Text.Trim();

            // Page 5: Standards & CDE
            data.CDEPlatform = (cmbCdePlatform.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "To be confirmed";
            data.ClassificationSystem = (cmbClassification.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Uniclass 2015";
            data.UnitsLength = (cmbUnitsLength.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Millimeters";
            data.UnitsArea = (cmbUnitsArea.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Square Meters";
            data.NamingConvention = txtNamingConvention.Text.Trim();
            data.FormatNative = txtFormatNative.Text.Trim();
            data.FormatExchange = txtFormatExchange.Text.Trim();
            data.FormatDrawing = txtFormatDrawing.Text.Trim();
            data.FormatData = txtFormatData.Text.Trim();

            return data;
        }

        // ── Review Summary ───────────────────────────────────────────

        private void BuildReviewSummary()
        {
            var data = CollectData();
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("  BIM EXECUTION PLAN — REVIEW SUMMARY");
            sb.AppendLine("  ISO 19650-2 §5.3 Pre-Contract BEP");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine("1. PROJECT INFORMATION");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"  Project Name:     {data.ProjectName}");
            sb.AppendLine($"  Project Number:   {data.ProjectNumber}");
            sb.AppendLine($"  Client:           {data.ClientName}");
            sb.AppendLine($"  Address:          {data.ProjectAddress}");
            if (!string.IsNullOrEmpty(data.ProjectDescription))
                sb.AppendLine($"  Description:      {data.ProjectDescription}");
            if (!string.IsNullOrEmpty(data.SiteReference))
                sb.AppendLine($"  Site Reference:   {data.SiteReference}");
            sb.AppendLine();

            sb.AppendLine("2. TEMPLATE & STAGE");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"  BEP Template:     {data.PresetKey}");
            sb.AppendLine($"  RIBA Stage:       {data.RIBAStage}");
            sb.AppendLine($"  Project Type:     {data.ProjectType}");
            sb.AppendLine($"  Procurement:      {data.ProcurementRoute}");
            sb.AppendLine();

            sb.AppendLine("3. DISCIPLINES & TEAM");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"  Lead Designer:    {data.LeadDesignerRole}");
            if (!string.IsNullOrEmpty(data.LeadCompany))
                sb.AppendLine($"  Lead Company:     {data.LeadCompany}");
            if (!string.IsNullOrEmpty(data.LeadContact))
                sb.AppendLine($"  Lead Contact:     {data.LeadContact}");
            if (!string.IsNullOrEmpty(data.BIMManager))
                sb.AppendLine($"  BIM Manager:      {data.BIMManager}");
            if (!string.IsNullOrEmpty(data.BIMCoordinator))
                sb.AppendLine($"  BIM Coordinator:  {data.BIMCoordinator}");
            sb.AppendLine($"  Disciplines:      {string.Join(", ", data.DisciplineTeam.Keys)}");
            foreach (var kv in data.DisciplineTeam)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                    sb.AppendLine($"    [{kv.Key}] {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("4. BIM USES");
            sb.AppendLine("───────────────────────────────────────────────────────");
            foreach (string use in data.BIMUses)
                sb.AppendLine($"  • {use}");
            if (!string.IsNullOrEmpty(data.AdditionalGoals))
                sb.AppendLine($"  Additional: {data.AdditionalGoals}");
            sb.AppendLine();

            sb.AppendLine("5. STANDARDS & CDE");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"  CDE Platform:     {data.CDEPlatform}");
            sb.AppendLine($"  Classification:   {data.ClassificationSystem}");
            sb.AppendLine($"  Units:            {data.UnitsLength} / {data.UnitsArea}");
            sb.AppendLine($"  Naming:           {data.NamingConvention}");
            sb.AppendLine($"  Native Format:    {data.FormatNative}");
            sb.AppendLine($"  Exchange Format:  {data.FormatExchange}");
            sb.AppendLine($"  Drawing Format:   {data.FormatDrawing}");
            sb.AppendLine($"  Data Format:      {data.FormatData}");
            sb.AppendLine();

            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("  Click 'Create BEP' to generate the full BEP.");
            sb.AppendLine("  Output: JSON (internal) + XLSX (standard format)");

            txtBepSummary.Text = sb.ToString();
        }
    }

    // ── BEP Wizard Data Class ────────────────────────────────────

    /// <summary>
    /// Data collected by the BEP Wizard for BEP generation.
    /// </summary>
    public class BEPWizardData
    {
        // Page 1: Project Info
        public string ProjectName { get; set; } = "";
        public string ProjectNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string ProjectAddress { get; set; } = "";
        public string ProjectDescription { get; set; } = "";
        public string SiteReference { get; set; } = "";

        // Page 2: Template & Stage
        public string PresetKey { get; set; } = "NBS_STANDARD";
        public int RIBAStage { get; set; } = 2;
        public string ProjectType { get; set; } = "New Build";
        public string ProcurementRoute { get; set; } = "Design and Build";

        // Page 3: Team
        public string LeadDesignerRole { get; set; } = "A";
        public string LeadCompany { get; set; } = "";
        public string LeadContact { get; set; } = "";
        public string BIMManager { get; set; } = "";
        public string BIMCoordinator { get; set; } = "";
        public Dictionary<string, string> DisciplineTeam { get; set; } = new();

        // Page 4: BIM Uses
        public List<string> BIMUses { get; set; } = new();
        public string AdditionalGoals { get; set; } = "";

        // Page 5: Standards & CDE
        public string CDEPlatform { get; set; } = "To be confirmed";
        public string ClassificationSystem { get; set; } = "Uniclass 2015";
        public string UnitsLength { get; set; } = "Millimeters";
        public string UnitsArea { get; set; } = "Square Meters";
        public string NamingConvention { get; set; } = "Project-Originator-Volume-Level-Type-Role-Class-Number";
        public string FormatNative { get; set; } = "Autodesk Revit (.rvt)";
        public string FormatExchange { get; set; } = "IFC 4 / IFC 2x3";
        public string FormatDrawing { get; set; } = "PDF/A-1b";
        public string FormatData { get; set; } = "COBie V2.4 (XLSX)";

        /// <summary>
        /// Get the lead designer description for display.
        /// </summary>
        public string LeadDesignerDescription => LeadDesignerRole switch
        {
            "A" => "Architect",
            "M" => "MEP Engineer",
            "S" => "Structural Engineer",
            "Multi" => "Multi-Discipline Lead",
            _ => LeadDesignerRole
        };
    }
}
