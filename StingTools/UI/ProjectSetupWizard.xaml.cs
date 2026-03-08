using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Project Setup Wizard WPF dialog.
    /// 7-page wizard that collects all project setup inputs including
    /// discipline-specific configuration, then returns a
    /// <see cref="ProjectSetupData"/> to the calling command.
    /// </summary>
    public partial class ProjectSetupWizard : Window
    {
        private const int TotalPages = 7;
        private int _currentPage = 0;

        /// <summary>Result data — populated when user clicks Run.</summary>
        public ProjectSetupData SetupData { get; private set; }

        /// <summary>True when user clicked Run (not Cancel).</summary>
        public bool RunRequested { get; private set; }

        public ProjectSetupWizard()
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
        public void PrePopulate(Document doc, List<string> titleBlockNames)
        {
            // Project Information
            try
            {
                ProjectInfo pi = doc.ProjectInformation;
                if (pi != null)
                {
                    if (!string.IsNullOrEmpty(pi.Name))
                        txtProjectName.Text = pi.Name;
                    if (!string.IsNullOrEmpty(pi.Number))
                        txtProjectNumber.Text = pi.Number;
                    if (!string.IsNullOrEmpty(pi.ClientName))
                        txtClientName.Text = pi.ClientName;
                    if (!string.IsNullOrEmpty(pi.OrganizationName))
                        txtOrganisation.Text = pi.OrganizationName;
                    if (!string.IsNullOrEmpty(pi.Author))
                        txtAuthor.Text = pi.Author;
                    if (!string.IsNullOrEmpty(pi.BuildingName))
                        txtBuildingName.Text = pi.BuildingName;
                    if (!string.IsNullOrEmpty(pi.Address))
                        txtAddress.Text = pi.Address;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read ProjectInfo: {ex.Message}");
            }

            // Existing levels
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
                if (levels.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var lv in levels)
                    {
                        double elevM = UnitUtils.ConvertFromInternalUnits(
                            lv.Elevation, UnitTypeId.Meters);
                        sb.AppendLine($"{lv.Name} | {elevM:F2}");
                    }
                    txtLevels.Text = sb.ToString().TrimEnd();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read levels: {ex.Message}");
            }

            // Title blocks
            if (titleBlockNames != null && titleBlockNames.Count > 0)
            {
                foreach (string name in titleBlockNames)
                    cmbTitleBlock.Items.Add(new ComboBoxItem { Content = name });
            }

            // Pre-check worksharing
            try
            {
                chkWorksharing.IsChecked = doc.IsWorkshared;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read workshared state: {ex.Message}");
            }
        }

        // ── Step indicators ──────────────────────────────────────────

        private readonly List<Border> _stepBorders = new List<Border>();
        private readonly string[] _stepLabels = { "1", "2", "3", "4", "5", "6", "7" };
        private readonly string[] _stepTitles =
        {
            "Project Info", "Levels", "Grids",
            "Disciplines", "Disc. Config", "Automation", "Review"
        };

        private void BuildStepIndicators()
        {
            StepIndicators.Children.Clear();
            _stepBorders.Clear();

            for (int i = 0; i < TotalPages; i++)
            {
                var sp = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                };

                var border = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = i == 0
                        ? Brushes.White
                        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 140, 200)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var txt = new TextBlock
                {
                    Text = _stepLabels[i],
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = i == 0 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(106, 27, 154)) : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = txt;
                sp.Children.Add(border);

                var label = new TextBlock
                {
                    Text = _stepTitles[i],
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                sp.Children.Add(label);

                // Connector line between steps
                if (i < TotalPages - 1)
                {
                    var line = new Border
                    {
                        Width = 16,
                        Height = 2,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 140, 200)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 14)
                    };
                    StepIndicators.Children.Add(sp);
                    StepIndicators.Children.Add(line);
                }
                else
                {
                    StepIndicators.Children.Add(sp);
                }

                _stepBorders.Add(border);
            }
        }

        private void UpdateStepIndicators()
        {
            for (int i = 0; i < _stepBorders.Count; i++)
            {
                var border = _stepBorders[i];
                var txt = (TextBlock)border.Child;

                if (i < _currentPage)
                {
                    // Completed step
                    border.Background = Brushes.White;
                    txt.Text = "\u2713"; // checkmark
                    txt.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                }
                else if (i == _currentPage)
                {
                    // Active step
                    border.Background = Brushes.White;
                    txt.Text = _stepLabels[i];
                    txt.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(106, 27, 154));
                }
                else
                {
                    // Future step
                    border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 140, 200));
                    txt.Text = _stepLabels[i];
                    txt.Foreground = Brushes.White;
                }
            }
        }

        // ── Navigation ───────────────────────────────────────────────

        private void UpdateNavigation()
        {
            WizardPages.SelectedIndex = _currentPage;
            UpdateStepIndicators();

            btnBack.IsEnabled = _currentPage > 0;
            btnNext.Visibility = _currentPage < TotalPages - 1
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            btnRun.Visibility = _currentPage == TotalPages - 1
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            txtPageInfo.Text = $"Step {_currentPage + 1} of {TotalPages} — {_stepTitles[_currentPage]}";

            // When entering discipline config page (page 4), show/hide discipline sections
            if (_currentPage == 4)
                UpdateDisciplineConfigVisibility();

            // Build review summary when entering last page
            if (_currentPage == TotalPages - 1)
                BuildReviewSummary();
        }

        /// <summary>Show only discipline config sections for checked disciplines.</summary>
        private void UpdateDisciplineConfigVisibility()
        {
            expMech.Visibility = chkDiscMech.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expElec.Visibility = chkDiscElec.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expPlumb.Visibility = chkDiscPlumb.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expArch.Visibility = chkDiscArch.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expStruct.Visibility = chkDiscStruct.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expFire.Visibility = chkDiscFire.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expLV.Visibility = chkDiscLV.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            expGen.Visibility = chkDiscGen.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // Auto-expand the first visible discipline
            bool expanded = false;
            foreach (var exp in new[] { expMech, expElec, expPlumb, expArch, expStruct, expFire, expLV, expGen })
            {
                if (exp.Visibility == System.Windows.Visibility.Visible && !expanded)
                {
                    exp.IsExpanded = true;
                    expanded = true;
                }
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                UpdateNavigation();
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            // Validate current page before advancing
            if (!ValidateCurrentPage())
                return;

            if (_currentPage < TotalPages - 1)
            {
                _currentPage++;
                UpdateNavigation();
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            SetupData = CollectData();
            RunRequested = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            RunRequested = false;
            DialogResult = false;
            Close();
        }

        // ── Validation ───────────────────────────────────────────────

        private bool ValidateCurrentPage()
        {
            switch (_currentPage)
            {
                case 0: // Project Info — require at least name or number
                    if (string.IsNullOrWhiteSpace(txtProjectName.Text) &&
                        string.IsNullOrWhiteSpace(txtProjectNumber.Text))
                    {
                        MessageBox.Show(
                            "Please enter at least a Project Name or Project Number.",
                            "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 1: // Levels — require at least one
                    var levels = ParseLevels();
                    if (levels.Count == 0)
                    {
                        MessageBox.Show(
                            "Please define at least one building level.\n" +
                            "Use a preset or enter lines in the format: Name | Elevation(m)",
                            "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;

                case 2: // Grids — validate numeric inputs if grids are enabled
                    if (chkCreateGrids.IsChecked == true)
                    {
                        if (!int.TryParse(txtGridHCount.Text, out int hc) || hc < 0 ||
                            !int.TryParse(txtGridVCount.Text, out int vc) || vc < 0)
                        {
                            MessageBox.Show(
                                "Grid count must be a positive whole number.",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                        if (!double.TryParse(txtGridHSpacing.Text, out double hs) || hs <= 0 ||
                            !double.TryParse(txtGridVSpacing.Text, out double vs) || vs <= 0)
                        {
                            MessageBox.Show(
                                "Grid spacing must be a positive number (in meters).",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                    }
                    return true;

                case 3: // Disciplines — require at least one
                {
                    bool anyDisc = chkDiscArch.IsChecked == true || chkDiscStruct.IsChecked == true ||
                                   chkDiscMech.IsChecked == true || chkDiscElec.IsChecked == true ||
                                   chkDiscPlumb.IsChecked == true || chkDiscFire.IsChecked == true ||
                                   chkDiscLV.IsChecked == true || chkDiscGen.IsChecked == true;
                    if (!anyDisc)
                    {
                        MessageBox.Show(
                            "Please select at least one discipline.",
                            "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    // Validate True North range
                    if (double.TryParse(txtTrueNorth.Text, out double tn))
                    {
                        if (tn < -360 || tn > 360)
                        {
                            MessageBox.Show(
                                "True North angle must be between -360 and 360 degrees.",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(txtTrueNorth.Text))
                    {
                        MessageBox.Show(
                            "True North must be a valid number (degrees).",
                            "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;
                }

                default:
                    return true;
            }
        }

        // ── Level presets ────────────────────────────────────────────

        private void LevelPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag)
                return;

            double floorH = 3.5;
            if (double.TryParse(txtFloorHeight.Text, out double h) && h > 0)
                floorH = h;

            var lines = new List<string>();
            switch (tag)
            {
                case "residential":
                    lines.Add("Ground Floor | 0.00");
                    for (int i = 1; i <= 3; i++)
                        lines.Add($"Level {i:D2} | {(i * floorH):F2}");
                    lines.Add($"Roof | {(4 * floorH):F2}");
                    break;

                case "commercial":
                    lines.Add($"Basement 01 | {(-floorH):F2}");
                    lines.Add("Ground Floor | 0.00");
                    for (int i = 1; i <= 5; i++)
                        lines.Add($"Level {i:D2} | {(i * floorH):F2}");
                    lines.Add($"Roof | {(6 * floorH):F2}");
                    break;

                case "highrise":
                    lines.Add($"Basement 02 | {(-2 * floorH):F2}");
                    lines.Add($"Basement 01 | {(-floorH):F2}");
                    lines.Add("Ground Floor | 0.00");
                    for (int i = 1; i <= 15; i++)
                        lines.Add($"Level {i:D2} | {(i * floorH):F2}");
                    lines.Add($"Roof | {(16 * floorH):F2}");
                    break;

                case "industrial":
                    lines.Add("Ground Floor | 0.00");
                    lines.Add($"Mezzanine | {(floorH):F2}");
                    lines.Add($"Roof | {(2 * floorH):F2}");
                    break;

                case "clear":
                    lines.Clear();
                    break;
            }

            txtLevels.Text = string.Join(Environment.NewLine, lines);
        }

        // ── Parse helpers ────────────────────────────────────────────

        private List<LevelDefinition> ParseLevels()
        {
            var result = new List<LevelDefinition>();
            if (string.IsNullOrWhiteSpace(txtLevels.Text))
                return result;

            foreach (string line in txtLevels.Text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                string[] parts = trimmed.Split('|');
                if (parts.Length < 2)
                    continue;

                string name = parts[0].Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (double.TryParse(parts[1].Trim(), out double elev))
                    result.Add(new LevelDefinition { Name = name, ElevationMeters = elev });
            }
            return result;
        }

        private static List<string> ParseCodes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
            return text.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        private static string GetComboValue(ComboBox cmb)
        {
            return (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        }

        // ── Collect all data ─────────────────────────────────────────

        private ProjectSetupData CollectData()
        {
            var data = new ProjectSetupData();

            // Page 1: Project info
            data.ProjectName = txtProjectName.Text.Trim();
            data.ProjectNumber = txtProjectNumber.Text.Trim();
            data.ClientName = txtClientName.Text.Trim();
            data.Organisation = txtOrganisation.Text.Trim();
            data.Author = txtAuthor.Text.Trim();
            data.BuildingName = txtBuildingName.Text.Trim();
            data.Address = txtAddress.Text.Trim();
            data.Status = GetComboValue(cmbStatus);
            if (string.IsNullOrEmpty(data.Status)) data.Status = "Work in Progress";
            data.LocCodes = ParseCodes(txtLocCodes.Text);
            data.ZoneCodes = ParseCodes(txtZoneCodes.Text);

            // Page 2: Levels
            data.Levels = ParseLevels();

            // Page 3: Grids
            data.CreateGrids = chkCreateGrids.IsChecked == true;
            if (int.TryParse(txtGridHCount.Text, out int ghc)) data.GridHCount = ghc;
            if (double.TryParse(txtGridHSpacing.Text, out double ghs)) data.GridHSpacing = ghs;
            if (double.TryParse(txtGridHLength.Text, out double ghl)) data.GridHLength = ghl;
            if (int.TryParse(txtGridVCount.Text, out int gvc)) data.GridVCount = gvc;
            if (double.TryParse(txtGridVSpacing.Text, out double gvs)) data.GridVSpacing = gvs;
            if (double.TryParse(txtGridVLength.Text, out double gvl)) data.GridVLength = gvl;

            // Page 4: Disciplines
            data.Disciplines = new List<string>();
            if (chkDiscArch.IsChecked == true) data.Disciplines.Add("A");
            if (chkDiscStruct.IsChecked == true) data.Disciplines.Add("S");
            if (chkDiscMech.IsChecked == true) data.Disciplines.Add("M");
            if (chkDiscElec.IsChecked == true) data.Disciplines.Add("E");
            if (chkDiscPlumb.IsChecked == true) data.Disciplines.Add("P");
            if (chkDiscFire.IsChecked == true) data.Disciplines.Add("FP");
            if (chkDiscLV.IsChecked == true) data.Disciplines.Add("LV");
            if (chkDiscGen.IsChecked == true) data.Disciplines.Add("G");

            data.TitleBlockName = GetComboValue(cmbTitleBlock);
            if (data.TitleBlockName == "(Use first available)" || string.IsNullOrEmpty(data.TitleBlockName))
                data.TitleBlockName = null;
            data.EnableWorksharing = chkWorksharing.IsChecked == true;

            // Units & orientation
            string unitSel = GetComboValue(cmbUnits);
            if (unitSel.Contains("Meters"))
                data.UnitSystem = "Meters";
            else if (unitSel.Contains("Imperial"))
                data.UnitSystem = "Imperial";
            else
                data.UnitSystem = "Millimeters";
            if (double.TryParse(txtTrueNorth.Text, out double tnAngle)) data.TrueNorthAngle = tnAngle;

            // Page 5: Discipline-specific configuration
            CollectDisciplineConfig(data);

            // Page 6: Automation
            data.LoadParams = chkLoadParams.IsChecked == true;
            data.CreateMaterials = chkCreateMaterials.IsChecked == true;
            data.CreateFamilyTypes = chkCreateFamilyTypes.IsChecked == true;
            data.CreateSchedules = chkCreateSchedules.IsChecked == true;
            data.CreateStyles = chkCreateStyles.IsChecked == true;
            data.CreateFilters = chkCreateFilters.IsChecked == true;
            data.CreateTemplates = chkCreateTemplates.IsChecked == true;
            data.CreatePhases = chkCreatePhases.IsChecked == true;
            data.CreateViews = chkCreateViews.IsChecked == true;
            data.CreateDependents = chkCreateDependents.IsChecked == true;
            data.CreateSheets = chkCreateSheets.IsChecked == true;
            data.CreateSections = chkCreateSections.IsChecked == true;
            data.CreateElevations = chkCreateElevations.IsChecked == true;

            return data;
        }

        /// <summary>Collect discipline-specific configuration from Page 5 expanders.</summary>
        private void CollectDisciplineConfig(ProjectSetupData data)
        {
            // Mechanical
            if (data.Disciplines.Contains("M"))
            {
                var mc = data.MechConfig;
                mc.IncludeHVAC = chkMechHVAC.IsChecked == true;
                mc.IncludeHWS = chkMechHWS.IsChecked == true;
                mc.IncludeDHW = chkMechDHW.IsChecked == true;
                mc.IncludeGAS = chkMechGAS.IsChecked == true;
                mc.DuctMaterial = GetComboValue(cmbMechDuctMat);
                mc.PipeMaterial = GetComboValue(cmbMechPipeMat);
                if (int.TryParse(txtMechInsulation.Text, out int insul)) mc.InsulationMm = insul;
            }

            // Electrical
            if (data.Disciplines.Contains("E"))
            {
                var ec = data.ElecConfig;
                ec.IncludePower = chkElecPower.IsChecked == true;
                ec.IncludeLighting = chkElecLighting.IsChecked == true;
                ec.Voltage = GetComboValue(cmbElecVoltage);
                ec.PhaseSystem = GetComboValue(cmbElecPhases);
                ec.Containment = GetComboValue(cmbElecContain);
                ec.IPRating = GetComboValue(cmbElecIP);
            }

            // Plumbing
            if (data.Disciplines.Contains("P"))
            {
                var pc = data.PlumbConfig;
                pc.IncludeDCW = chkPlumbDCW.IsChecked == true;
                pc.IncludeSAN = chkPlumbSAN.IsChecked == true;
                pc.IncludeRWD = chkPlumbRWD.IsChecked == true;
                pc.PipeMaterial = GetComboValue(cmbPlumbPipeMat);
                pc.FixtureStandard = GetComboValue(cmbPlumbStd);
            }

            // Architectural
            if (data.Disciplines.Contains("A"))
            {
                var ac = data.ArchConfig;
                ac.IncludeFinishes = chkArchFinishes.IsChecked == true;
                ac.IncludeFurniture = chkArchFurniture.IsChecked == true;
                ac.IncludeCurtainWall = chkArchCurtainWall.IsChecked == true;
                ac.WallConstruction = GetComboValue(cmbArchWallType);
                ac.GlazingType = GetComboValue(cmbArchGlazing);
            }

            // Structural
            if (data.Disciplines.Contains("S"))
            {
                var sc = data.StructConfig;
                sc.PrimaryMaterial = GetComboValue(cmbStructMat);
                sc.ConcreteClass = GetComboValue(cmbStructConc);
                sc.SteelGrade = GetComboValue(cmbStructSteel);
            }

            // Fire Protection
            if (data.Disciplines.Contains("FP"))
            {
                var fc = data.FireConfig;
                fc.IncludeSprinkler = chkFireSprinkler.IsChecked == true;
                fc.IncludeAlarm = chkFireAlarm.IsChecked == true;
                fc.IncludeSuppression = chkFireSuppress.IsChecked == true;
                fc.FireRating = GetComboValue(cmbFireRating);
            }

            // Low Voltage
            if (data.Disciplines.Contains("LV"))
            {
                var lc = data.LVConfig;
                lc.IncludeComms = chkLVComms.IsChecked == true;
                lc.IncludeData = chkLVData.IsChecked == true;
                lc.IncludeSecurity = chkLVSecurity.IsChecked == true;
                lc.IncludeNurseCall = chkLVNurse.IsChecked == true;
                lc.CableStandard = GetComboValue(cmbLVCable);
            }

            // Generic
            if (data.Disciplines.Contains("G"))
            {
                var gc = data.GenConfig;
                gc.IncludeSpecialty = chkGenSpecialty.IsChecked == true;
                gc.IncludeMedical = chkGenMedical.IsChecked == true;
            }
        }

        // ── Review summary ───────────────────────────────────────────

        private void BuildReviewSummary()
        {
            var data = CollectData();
            var sb = new StringBuilder();

            sb.AppendLine("STING PROJECT SETUP — REVIEW");
            sb.AppendLine(new string('═', 55));

            // Project Info
            sb.AppendLine();
            sb.AppendLine("PROJECT INFORMATION");
            sb.AppendLine(new string('─', 55));
            if (!string.IsNullOrEmpty(data.ProjectName))
                sb.AppendLine($"  Name:          {data.ProjectName}");
            if (!string.IsNullOrEmpty(data.ProjectNumber))
                sb.AppendLine($"  Number:        {data.ProjectNumber}");
            if (!string.IsNullOrEmpty(data.ClientName))
                sb.AppendLine($"  Client:        {data.ClientName}");
            if (!string.IsNullOrEmpty(data.Organisation))
                sb.AppendLine($"  Organisation:  {data.Organisation}");
            if (!string.IsNullOrEmpty(data.Author))
                sb.AppendLine($"  Author:        {data.Author}");
            if (!string.IsNullOrEmpty(data.BuildingName))
                sb.AppendLine($"  Building:      {data.BuildingName}");
            if (!string.IsNullOrEmpty(data.Address))
                sb.AppendLine($"  Address:       {data.Address}");
            sb.AppendLine($"  Status:        {data.Status}");
            sb.AppendLine($"  LOC codes:     {string.Join(", ", data.LocCodes)}");
            sb.AppendLine($"  ZONE codes:    {string.Join(", ", data.ZoneCodes)}");

            // Levels
            sb.AppendLine();
            sb.AppendLine($"BUILDING LEVELS ({data.Levels.Count})");
            sb.AppendLine(new string('─', 55));
            foreach (var lv in data.Levels)
                sb.AppendLine($"  {lv.Name,-20} {lv.ElevationMeters,8:F2} m");

            // Grids
            sb.AppendLine();
            sb.AppendLine("GRID SYSTEM");
            sb.AppendLine(new string('─', 55));
            if (data.CreateGrids)
            {
                sb.AppendLine($"  Horizontal:    {data.GridHCount} grids @ {data.GridHSpacing}m spacing, {data.GridHLength}m long");
                sb.AppendLine($"  Vertical:      {data.GridVCount} grids @ {data.GridVSpacing}m spacing, {data.GridVLength}m long");
                sb.AppendLine($"  Total grids:   {data.GridHCount + data.GridVCount}");
            }
            else
            {
                sb.AppendLine("  Grids:         SKIPPED");
            }

            // Disciplines
            sb.AppendLine();
            sb.AppendLine("DISCIPLINES");
            sb.AppendLine(new string('─', 55));
            sb.AppendLine($"  Active:        {string.Join(", ", data.Disciplines)}");
            sb.AppendLine($"  Worksharing:   {(data.EnableWorksharing ? "YES (35 worksets)" : "NO")}");
            sb.AppendLine($"  Units:         {data.UnitSystem}");
            if (data.TrueNorthAngle != 0)
                sb.AppendLine($"  True North:    {data.TrueNorthAngle:F1}° from Project North");
            if (!string.IsNullOrEmpty(data.TitleBlockName))
                sb.AppendLine($"  Title block:   {data.TitleBlockName}");
            else
                sb.AppendLine("  Title block:   (first available)");

            // Discipline-specific details
            sb.AppendLine();
            sb.AppendLine("DISCIPLINE CONFIGURATION");
            sb.AppendLine(new string('─', 55));
            BuildDisciplineReview(sb, data);

            // Automation steps — count accurately
            sb.AppendLine();
            sb.AppendLine("AUTOMATION PIPELINE");
            sb.AppendLine(new string('─', 55));

            int stepCount = 0;
            int enabledCount = 0;
            void AddStep(bool enabled, string label)
            {
                stepCount++;
                if (enabled) enabledCount++;
                sb.AppendLine($"  {stepCount,2}. [{(enabled ? "X" : " ")}] {label}");
            }

            // Phase 1: Foundation (always runs)
            sb.AppendLine("  Phase 1: Foundation");
            AddStep(true, $"Set display units ({data.UnitSystem})");
            AddStep(true, "Set Project Information");
            AddStep(true, $"Create/update {data.Levels.Count} building levels");
            AddStep(data.CreateGrids, $"Create {data.GridHCount + data.GridVCount} grids");
            AddStep(data.TrueNorthAngle != 0, $"Set True North ({data.TrueNorthAngle:F1}°)");
            AddStep(data.EnableWorksharing, "Enable worksharing");

            // Phase 2: Infrastructure
            sb.AppendLine("  Phase 2: Infrastructure");
            AddStep(data.LoadParams, "Load shared parameters (200+)");
            AddStep(data.CreateMaterials, "Create BLE materials (815)");
            AddStep(data.CreateMaterials, "Create MEP materials (464)");
            if (data.CreateFamilyTypes)
            {
                AddStep(true, "Create wall types");
                AddStep(true, "Create floor types");
                AddStep(true, "Create ceiling types");
                AddStep(true, "Create roof types");
                AddStep(true, "Create duct types");
                AddStep(true, "Create pipe types");
                AddStep(true, "Create cable tray types");
                AddStep(true, "Create conduit types");
            }
            else
            {
                AddStep(false, "Create family types (8 sub-steps)");
            }
            AddStep(data.CreateSchedules, "Batch create schedules (168)");
            AddStep(data.CreateSchedules, "Create template schedules (13)");

            // Phase 3: Standards
            sb.AppendLine("  Phase 3: Standards");
            if (data.CreateStyles)
            {
                AddStep(true, "Create fill patterns");
                AddStep(true, "Create line patterns (10 ISO 128)");
                AddStep(true, "Create line styles");
                AddStep(true, "Create object styles");
                AddStep(true, "Create text styles");
                AddStep(true, "Create dimension styles");
            }
            else
            {
                AddStep(false, "Create styles (6 sub-steps)");
            }
            AddStep(data.CreateFilters, "Create view filters (28+)");
            if (data.CreateTemplates)
            {
                AddStep(true, "Create view templates (23)");
                AddStep(true, "Apply filters to templates");
                AddStep(true, "Apply VG overrides (5-layer)");
            }
            else
            {
                AddStep(false, "Create view templates (3 sub-steps)");
            }
            AddStep(data.CreatePhases, "Audit project phases (report only)");
            AddStep(data.EnableWorksharing, "Create worksets (35 ISO 19650)");

            // Phase 4: Documentation
            sb.AppendLine("  Phase 4: Documentation");
            AddStep(data.CreateViews, $"Create views ({data.Disciplines.Count} disc x {data.Levels.Count} levels)");
            AddStep(data.CreateDependents, "Create dependent views from scope boxes");
            AddStep(data.CreateSheets, "Create sheets with viewports");
            AddStep(data.CreateSections, "Create building sections from grids");
            AddStep(data.CreateElevations, "Create 4 exterior elevations");
            AddStep(data.CreateViews || data.CreateSheets, "Organize project browser");
            AddStep(data.CreateSheets, "Create sheet index schedule");

            // Phase 5: Intelligence
            sb.AppendLine("  Phase 5: Intelligence");
            AddStep(data.CreateTemplates, "Auto-assign templates (5-layer)");
            AddStep(data.CreateTemplates, "Auto-fix template health");
            AddStep(data.LoadParams, "Full auto-populate (tokens + dims + MEP + formulas)");
            AddStep(data.LoadParams, "Batch add family parameters");
            AddStep(true, "Set starting view");

            sb.AppendLine();
            sb.AppendLine(new string('═', 55));
            sb.AppendLine($"  TOTAL: {enabledCount} of {stepCount} steps will execute");
            sb.AppendLine("  Each step runs independently. Use Ctrl+Z to undo.");
            sb.AppendLine();
            sb.AppendLine("  Click 'Run Setup' to begin.");

            txtReviewSummary.Text = sb.ToString();
        }

        /// <summary>Append discipline-specific configuration to the review summary.</summary>
        private static void BuildDisciplineReview(StringBuilder sb, ProjectSetupData data)
        {
            var discNames = new Dictionary<string, string>
            {
                {"A","Architectural"}, {"S","Structural"}, {"M","Mechanical"},
                {"E","Electrical"}, {"P","Plumbing"}, {"FP","Fire Protection"},
                {"LV","Low Voltage"}, {"G","Generic"}
            };

            foreach (string disc in data.Disciplines)
            {
                string name = discNames.TryGetValue(disc, out string n) ? n : disc;
                sb.AppendLine($"  [{disc}] {name}");

                switch (disc)
                {
                    case "M":
                        var mc = data.MechConfig;
                        var sysList = new List<string>();
                        if (mc.IncludeHVAC) sysList.Add("HVAC");
                        if (mc.IncludeHWS) sysList.Add("HWS");
                        if (mc.IncludeDHW) sysList.Add("DHW");
                        if (mc.IncludeGAS) sysList.Add("GAS");
                        sb.AppendLine($"      Systems: {string.Join(", ", sysList)}");
                        sb.AppendLine($"      Duct: {mc.DuctMaterial}, Pipe: {mc.PipeMaterial}");
                        if (mc.InsulationMm > 0)
                            sb.AppendLine($"      Insulation: {mc.InsulationMm}mm");
                        break;

                    case "E":
                        var ec = data.ElecConfig;
                        var eSys = new List<string>();
                        if (ec.IncludePower) eSys.Add("Power");
                        if (ec.IncludeLighting) eSys.Add("Lighting");
                        sb.AppendLine($"      Systems: {string.Join(", ", eSys)}");
                        sb.AppendLine($"      {ec.Voltage}, {ec.PhaseSystem}");
                        sb.AppendLine($"      Containment: {ec.Containment}, {ec.IPRating}");
                        break;

                    case "P":
                        var pc = data.PlumbConfig;
                        var pSys = new List<string>();
                        if (pc.IncludeDCW) pSys.Add("DCW");
                        if (pc.IncludeSAN) pSys.Add("SAN");
                        if (pc.IncludeRWD) pSys.Add("RWD");
                        sb.AppendLine($"      Systems: {string.Join(", ", pSys)}");
                        sb.AppendLine($"      Pipe: {pc.PipeMaterial}, Fixtures: {pc.FixtureStandard}");
                        break;

                    case "A":
                        var ac = data.ArchConfig;
                        var scope = new List<string>();
                        if (ac.IncludeFinishes) scope.Add("Finishes");
                        if (ac.IncludeFurniture) scope.Add("Furniture");
                        if (ac.IncludeCurtainWall) scope.Add("Curtain Wall");
                        sb.AppendLine($"      Scope: {string.Join(", ", scope)}");
                        sb.AppendLine($"      Walls: {ac.WallConstruction}, Glazing: {ac.GlazingType}");
                        break;

                    case "S":
                        var sc = data.StructConfig;
                        sb.AppendLine($"      Primary: {sc.PrimaryMaterial}");
                        sb.AppendLine($"      Concrete: {sc.ConcreteClass}, Steel: {sc.SteelGrade}");
                        break;

                    case "FP":
                        var fc = data.FireConfig;
                        var fSys = new List<string>();
                        if (fc.IncludeSprinkler) fSys.Add("Sprinkler");
                        if (fc.IncludeAlarm) fSys.Add("Alarm");
                        if (fc.IncludeSuppression) fSys.Add("Suppression");
                        sb.AppendLine($"      Systems: {string.Join(", ", fSys)}");
                        sb.AppendLine($"      Fire rating: {fc.FireRating}");
                        break;

                    case "LV":
                        var lc = data.LVConfig;
                        var lvSys = new List<string>();
                        if (lc.IncludeComms) lvSys.Add("COM");
                        if (lc.IncludeData) lvSys.Add("ICT");
                        if (lc.IncludeSecurity) lvSys.Add("SEC");
                        if (lc.IncludeNurseCall) lvSys.Add("NCL");
                        sb.AppendLine($"      Systems: {string.Join(", ", lvSys)}");
                        sb.AppendLine($"      Cable: {lc.CableStandard}");
                        break;

                    case "G":
                        var gc = data.GenConfig;
                        var gScope = new List<string>();
                        if (gc.IncludeSpecialty) gScope.Add("Specialty");
                        if (gc.IncludeMedical) gScope.Add("Medical");
                        sb.AppendLine($"      Scope: {(gScope.Count > 0 ? string.Join(", ", gScope) : "General")}");
                        break;
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Data model
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Level definition for the wizard.</summary>
    public class LevelDefinition
    {
        public string Name { get; set; }
        public double ElevationMeters { get; set; }
    }

    /// <summary>
    /// All data collected by the Project Setup Wizard.
    /// Passed to <see cref="Temp.ProjectSetupCommand"/> for execution.
    /// </summary>
    public class ProjectSetupData
    {
        // Page 1: Project info
        public string ProjectName { get; set; }
        public string ProjectNumber { get; set; }
        public string ClientName { get; set; }
        public string Organisation { get; set; }
        public string Author { get; set; }
        public string BuildingName { get; set; }
        public string Address { get; set; }
        public string Status { get; set; }
        public List<string> LocCodes { get; set; } = new List<string>();
        public List<string> ZoneCodes { get; set; } = new List<string>();

        // Page 2: Levels
        public List<LevelDefinition> Levels { get; set; } = new List<LevelDefinition>();

        // Page 3: Grids
        public bool CreateGrids { get; set; }
        public int GridHCount { get; set; }
        public double GridHSpacing { get; set; }
        public double GridHLength { get; set; }
        public int GridVCount { get; set; }
        public double GridVSpacing { get; set; }
        public double GridVLength { get; set; }

        // Page 4: Disciplines
        public List<string> Disciplines { get; set; } = new List<string>();
        public string TitleBlockName { get; set; }
        public bool EnableWorksharing { get; set; }
        public string UnitSystem { get; set; } = "Millimeters";
        public double TrueNorthAngle { get; set; }

        // Page 5: Discipline-specific configuration
        public MechanicalConfig MechConfig { get; set; } = new MechanicalConfig();
        public ElectricalConfig ElecConfig { get; set; } = new ElectricalConfig();
        public PlumbingConfig PlumbConfig { get; set; } = new PlumbingConfig();
        public ArchitecturalConfig ArchConfig { get; set; } = new ArchitecturalConfig();
        public StructuralConfig StructConfig { get; set; } = new StructuralConfig();
        public FireProtectionConfig FireConfig { get; set; } = new FireProtectionConfig();
        public LowVoltageConfig LVConfig { get; set; } = new LowVoltageConfig();
        public GenericConfig GenConfig { get; set; } = new GenericConfig();

        // Page 6: Automation
        public bool LoadParams { get; set; }
        public bool CreateMaterials { get; set; }
        public bool CreateFamilyTypes { get; set; }
        public bool CreateSchedules { get; set; }
        public bool CreateStyles { get; set; }
        public bool CreateFilters { get; set; }
        public bool CreateTemplates { get; set; }
        public bool CreatePhases { get; set; }
        public bool CreateViews { get; set; }
        public bool CreateDependents { get; set; }
        public bool CreateSheets { get; set; }
        public bool CreateSections { get; set; }
        public bool CreateElevations { get; set; }

        /// <summary>Get all active system codes from discipline configs.</summary>
        public List<string> GetActiveSystemCodes()
        {
            var codes = new List<string>();
            if (Disciplines.Contains("M"))
            {
                if (MechConfig.IncludeHVAC) codes.Add("HVAC");
                if (MechConfig.IncludeHWS) codes.Add("HWS");
                if (MechConfig.IncludeDHW) codes.Add("DHW");
                if (MechConfig.IncludeGAS) codes.Add("GAS");
            }
            if (Disciplines.Contains("E"))
            {
                if (ElecConfig.IncludePower) codes.Add("LV");
                if (ElecConfig.IncludeLighting) codes.Add("LV");
            }
            if (Disciplines.Contains("P"))
            {
                if (PlumbConfig.IncludeDCW) codes.Add("DCW");
                if (PlumbConfig.IncludeSAN) codes.Add("SAN");
                if (PlumbConfig.IncludeRWD) codes.Add("RWD");
            }
            if (Disciplines.Contains("FP"))
            {
                codes.Add("FP");
                if (FireConfig.IncludeAlarm) codes.Add("FLS");
            }
            if (Disciplines.Contains("LV"))
            {
                if (LVConfig.IncludeComms) codes.Add("COM");
                if (LVConfig.IncludeData) codes.Add("ICT");
                if (LVConfig.IncludeSecurity) codes.Add("SEC");
                if (LVConfig.IncludeNurseCall) codes.Add("NCL");
            }
            if (Disciplines.Contains("A")) codes.Add("ARC");
            if (Disciplines.Contains("S")) codes.Add("STR");
            if (Disciplines.Contains("G")) codes.Add("GEN");
            return codes.Distinct().ToList();
        }
    }

    // ── Discipline-specific config classes ────────────────────────────

    public class MechanicalConfig
    {
        public bool IncludeHVAC { get; set; } = true;
        public bool IncludeHWS { get; set; } = true;
        public bool IncludeDHW { get; set; }
        public bool IncludeGAS { get; set; }
        public string DuctMaterial { get; set; } = "Galvanised Steel";
        public string PipeMaterial { get; set; } = "Copper";
        public int InsulationMm { get; set; } = 25;
    }

    public class ElectricalConfig
    {
        public bool IncludePower { get; set; } = true;
        public bool IncludeLighting { get; set; } = true;
        public string Voltage { get; set; } = "230V (UK/EU)";
        public string PhaseSystem { get; set; } = "Single Phase";
        public string Containment { get; set; } = "Cable Tray + Conduit";
        public string IPRating { get; set; } = "IP20";
    }

    public class PlumbingConfig
    {
        public bool IncludeDCW { get; set; } = true;
        public bool IncludeSAN { get; set; } = true;
        public bool IncludeRWD { get; set; }
        public string PipeMaterial { get; set; } = "Copper";
        public string FixtureStandard { get; set; } = "Commercial Grade";
    }

    public class ArchitecturalConfig
    {
        public bool IncludeFinishes { get; set; } = true;
        public bool IncludeFurniture { get; set; }
        public bool IncludeCurtainWall { get; set; }
        public string WallConstruction { get; set; } = "Cavity Wall";
        public string GlazingType { get; set; } = "Double Glazed";
    }

    public class StructuralConfig
    {
        public string PrimaryMaterial { get; set; } = "Reinforced Concrete";
        public string ConcreteClass { get; set; } = "C30/37";
        public string SteelGrade { get; set; } = "S355";
    }

    public class FireProtectionConfig
    {
        public bool IncludeSprinkler { get; set; } = true;
        public bool IncludeAlarm { get; set; } = true;
        public bool IncludeSuppression { get; set; }
        public string FireRating { get; set; } = "60 min";
    }

    public class LowVoltageConfig
    {
        public bool IncludeComms { get; set; } = true;
        public bool IncludeData { get; set; } = true;
        public bool IncludeSecurity { get; set; }
        public bool IncludeNurseCall { get; set; }
        public string CableStandard { get; set; } = "CAT6A";
    }

    public class GenericConfig
    {
        public bool IncludeSpecialty { get; set; }
        public bool IncludeMedical { get; set; }
    }
}
