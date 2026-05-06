using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogCommandLinkId = Autodesk.Revit.UI.TaskDialogCommandLinkId;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
// CS0104 — both Autodesk.Revit.DB and System.Windows.Media define Transform.
// The wizard reads BoundingBoxXYZ.Transform (Revit) for scope-box rotation
// math; alias the bare name to the Revit type so existing code resolves.
using Transform = Autodesk.Revit.DB.Transform;

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
        // 8 pages: Project Info → Levels → Grids → Disciplines → Disc. Config
        //          → Automation → Region → Review.
        private const int TotalPages = 8;
        private int _currentPage = 0;

        /// <summary>Result data — populated when user clicks Run.</summary>
        public ProjectSetupData SetupData { get; private set; }

        /// <summary>True when user clicked Run (not Cancel).</summary>
        public bool RunRequested { get; private set; }

        /// <summary>Bound to the Levels DataGrid on page 2.</summary>
        public ObservableCollection<LevelRow> LevelRows { get; } = new ObservableCollection<LevelRow>();

        /// <summary>Bound to the Scope-Box DataGrid on page 3.</summary>
        public ObservableCollection<ScopeBoxRow> ScopeBoxRows { get; } = new ObservableCollection<ScopeBoxRow>();

        /// <summary>Allowed values for the 'Type' column in the levels grid.</summary>
        private static readonly string[] LevelTypeOptions =
        {
            "Standard", "Structural", "Reference", "Ceiling", "Sub-level", "Roof", "Parapet"
        };

        public ProjectSetupWizard()
        {
            InitializeComponent();

            // Bind level grid
            dgLevels.ItemsSource = LevelRows;
            colLevelType.ItemsSource = LevelTypeOptions;
            LevelRows.CollectionChanged += (_, __) => RenumberLevelRows();

            // Bind scope-box grid
            dgScopeBoxes.ItemsSource = ScopeBoxRows;

            BuildStepIndicators();
            UpdateNavigation();
            PopulateRegionPresets();
        }

        // Standards & Region — populate the listbox from
        // ProjectStandardsManager.RegionalPresets and pre-select the
        // active region so the wizard reflects whatever was already set.
        private void PopulateRegionPresets()
        {
            try
            {
                var mgr = StingTools.Standards.ProjectStandardsManager.Instance;
                var regions = mgr.GetAvailableRegions().ToList();
                lstRegionPresets.ItemsSource = regions;
                string current = mgr.Region;
                int idx = !string.IsNullOrEmpty(current)
                    ? regions.FindIndex(r => string.Equals(r, current, StringComparison.OrdinalIgnoreCase))
                    : -1;
                lstRegionPresets.SelectedIndex = idx >= 0 ? idx : regions.IndexOf("International");
            }
            catch (System.Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PopulateRegionPresets: {ex.Message}");
            }
        }

        private void LstRegionPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (lstRegionPresets.SelectedItem is string key &&
                    StingTools.Standards.ProjectStandardsManager.RegionalPresets.TryGetValue(key, out var p))
                {
                    txtRegElec.Text   = p.ElectricalStandard;
                    txtRegHvac.Text   = p.HVACStandard;
                    txtRegPlumb.Text  = p.PlumbingStandard;
                    txtRegStruct.Text = p.StructuralStandard;
                    txtRegFire.Text   = p.FireProtectionStandard;
                    txtRegLight.Text  = p.LightingStandard;
                    txtRegEnergy.Text = p.EnergyStandard;
                    txtRegUnits.Text  = p.UnitSystem.ToString();
                }
            }
            catch (System.Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LstRegionPresets_SelectionChanged: {ex.Message}");
            }
        }

        private void RenumberLevelRows()
        {
            for (int i = 0; i < LevelRows.Count; i++)
                LevelRows[i].Index = i + 1;
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

                    // Pre-select the regional preset from PROJECT_REGION when
                    // it's already been set on this document (e.g. by a prior
                    // wizard run or the SetRegionCommand). Falls back to the
                    // singleton's current region otherwise.
                    try
                    {
                        string projRegion = pi.LookupParameter("PROJECT_REGION")?.AsString();
                        if (string.IsNullOrWhiteSpace(projRegion))
                            projRegion = StingTools.Standards.ProjectStandardsManager.Instance.Region;
                        if (!string.IsNullOrWhiteSpace(projRegion) && lstRegionPresets.Items.Count > 0)
                        {
                            for (int i = 0; i < lstRegionPresets.Items.Count; i++)
                            {
                                if (string.Equals(lstRegionPresets.Items[i] as string, projRegion,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    lstRegionPresets.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception rex) { StingLog.Warn($"PrePopulate region: {rex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read ProjectInfo: {ex.Message}");
            }

            // Existing levels — populate editable DataGrid
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                LevelRows.Clear();
                foreach (var lv in levels)
                {
                    double elevM = UnitUtils.ConvertFromInternalUnits(lv.Elevation, UnitTypeId.Meters);
                    LevelRows.Add(new LevelRow
                    {
                        Name = lv.Name,
                        ElevationText = elevM.ToString("F2", CultureInfo.InvariantCulture),
                        LevelType = GuessLevelType(lv.Name),
                        IsStory = IsStoryLevel(lv),
                        ExistingId = lv.Id.Value
                    });
                }
                RenumberLevelRows();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read levels: {ex.Message}");
            }

            // Existing scope boxes — populate the scope-box DataGrid
            try
            {
                var scopeBoxes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .OrderBy(e => e.Name)
                    .ToList();

                ScopeBoxRows.Clear();
                foreach (var sb in scopeBoxes)
                {
                    double rotDeg = GetScopeBoxRotationDegrees(sb);
                    ScopeBoxRows.Add(new ScopeBoxRow
                    {
                        Include = true,
                        CurrentName = sb.Name,
                        NewName = sb.Name,
                        RotationDegrees = rotDeg,
                        RevitIdValue = sb.Id.Value
                    });
                }
                lblScopeBoxEmpty.Visibility = ScopeBoxRows.Count == 0
                    ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not read scope boxes: {ex.Message}");
            }

            // Title blocks — populate the default dropdown and every per-discipline dropdown.
            if (titleBlockNames != null && titleBlockNames.Count > 0)
            {
                foreach (string name in titleBlockNames)
                    cmbTitleBlock.Items.Add(new ComboBoxItem { Content = name });

                PopulateDisciplineTitleBlock(cmbTbArch,   titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbStruct, titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbMech,   titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbElec,   titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbPlumb,  titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbFire,   titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbLV,     titleBlockNames);
                PopulateDisciplineTitleBlock(cmbTbGen,    titleBlockNames);
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

        /// <summary>Populate a per-discipline title block ComboBox with (Default) + all loaded title blocks.</summary>
        private static void PopulateDisciplineTitleBlock(ComboBox combo, List<string> titleBlockNames)
        {
            if (combo == null) return;
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "(Default)", IsSelected = true });
            foreach (string n in titleBlockNames)
                combo.Items.Add(new ComboBoxItem { Content = n });
        }

        // ── ISO 19650 level naming ──────────────────────────────────

        /// <summary>Valid ISO 19650 level code pattern: B##, GF, L##, MZ##, RF, UR, XX.</summary>
        private static readonly System.Text.RegularExpressions.Regex IsoLevelRegex =
            new System.Text.RegularExpressions.Regex(
                @"^(B\d{1,2}|GF|L\d{2,3}|MZ\d{1,2}|RF\d?|UR|XX)(\s*[-–_]\s*.+)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>True when the level name starts with an ISO 19650 code (B01, GF, L02, MZ01, RF, etc.).</summary>
        internal static bool IsIsoLevelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return IsoLevelRegex.IsMatch(name.Trim());
        }

        /// <summary>Propose an ISO 19650 code from a level's current name + elevation.
        /// Basements elevate below zero, ground floor ≈ 0, mezzanine hints via name.</summary>
        internal static string ProposeIsoLevelCode(string currentName, double elevationMetres, int sequenceAboveGround)
        {
            string n = (currentName ?? "").ToUpperInvariant().Trim();

            // Explicit keywords win
            if (n.Contains("ROOF") || n.StartsWith("RF")) return "RF";
            if (n.Contains("PARAPET")) return "UR";
            if (n.Contains("MEZZANINE") || n.StartsWith("MZ")) return "MZ01";
            if (n.Contains("BASEMENT") || n.StartsWith("B ") || n.StartsWith("B0"))
            {
                // Try to read the basement number from the name
                var m = System.Text.RegularExpressions.Regex.Match(n, @"B[ _-]?0*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int bn) && bn > 0)
                    return $"B{bn:D2}";
            }
            if (n.StartsWith("GROUND") || n == "GF" || Math.Abs(elevationMetres) < 0.01)
                return "GF";

            // Elevation-based fallback
            if (elevationMetres < -0.1)
            {
                // Basement — deeper = higher number (B01 is top basement)
                int bn = Math.Max(1, (int)Math.Round(-elevationMetres / Math.Max(3.0, 3.5)));
                return $"B{bn:D2}";
            }
            if (elevationMetres > 0.1)
            {
                int ln = Math.Max(1, sequenceAboveGround);
                return $"L{ln:D2}";
            }
            return "GF";
        }

        private void LevelIsoNormalize_Click(object sender, RoutedEventArgs e)
        {
            // Preserve any descriptive suffix after " - " but enforce an ISO prefix on each row.
            var sorted = LevelRows
                .Select((r, idx) => new { r, idx })
                .OrderBy(x =>
                {
                    return double.TryParse(x.r.ElevationText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) ? v : 0.0;
                })
                .ToList();

            // Number ground-up levels (L01, L02, ...) by ascending elevation above 0
            int aboveGroundSeq = 0;
            var proposed = new Dictionary<int, string>(); // original row index → ISO code
            foreach (var item in sorted)
            {
                double elev = double.TryParse(item.r.ElevationText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double v) ? v : 0.0;
                if (elev > 0.1) aboveGroundSeq++;
                proposed[item.idx] = ProposeIsoLevelCode(item.r.Name, elev, aboveGroundSeq);
            }

            // Ensure uniqueness (if two rows hash to the same code, suffix -2, -3, ...)
            var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int changed = 0;
            for (int i = 0; i < LevelRows.Count; i++)
            {
                string code = proposed.TryGetValue(i, out string c) ? c : "L01";
                // Keep any nice descriptive suffix the user entered after " - "
                string descriptive = "";
                string originalName = LevelRows[i].Name ?? "";
                int sep = originalName.IndexOf(" - ", StringComparison.Ordinal);
                if (sep > 0) descriptive = originalName.Substring(sep); // includes " - "

                string candidate = code + descriptive;

                // Uniquify
                string unique = candidate;
                while (taken.ContainsKey(unique))
                {
                    taken[code] = taken.TryGetValue(code, out int n) ? n + 1 : 2;
                    unique = $"{code}-{taken[code]}" + descriptive;
                }
                taken[unique] = 1;

                if (!string.Equals(LevelRows[i].Name, unique, StringComparison.Ordinal))
                {
                    LevelRows[i].Name = unique;
                    changed++;
                }
            }

            MessageBox.Show(
                $"ISO 19650 normalization applied to {changed} level(s).\n" +
                $"Codes used: B## (basement), GF (ground), L## (above ground), MZ## (mezzanine), RF (roof), UR (upper roof/parapet).",
                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Best-effort guess of a level's type from its name.</summary>
        private static string GuessLevelType(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Standard";
            string n = name.ToUpperInvariant();
            if (n.Contains("ROOF")) return "Roof";
            if (n.Contains("PARAPET")) return "Parapet";
            if (n.Contains("CEILING") || n.Contains("SOFIT") || n.Contains("SOFFIT")) return "Ceiling";
            if (n.Contains("RING BEAM") || n.Contains("BEAM") || n.Contains("FOOTING") || n.Contains("SLAB")) return "Structural";
            if (n.Contains("SILL") || n.Contains("DATUM") || n.Contains("REF")) return "Reference";
            if (n.Contains("MEZZANINE") || n.Contains("SUB")) return "Sub-level";
            return "Standard";
        }

        /// <summary>Detect whether a Revit level is flagged as a building story.</summary>
        private static bool IsStoryLevel(Level lv)
        {
            try
            {
                Parameter p = lv.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                if (p != null) return p.AsInteger() != 0;
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Return the rotation (degrees) of a scope box's primary axis relative to world X.
        /// Uses the element's bounding-box Transform (which carries the box's orientation).
        /// </summary>
        internal static double GetScopeBoxRotationDegrees(Element scopeBox)
        {
            try
            {
                BoundingBoxXYZ bb = scopeBox.get_BoundingBox(null);
                if (bb == null) return 0.0;
                Transform t = bb.Transform ?? Transform.Identity;
                XYZ bx = t.BasisX;
                if (bx == null || bx.IsZeroLength()) return 0.0;
                double angleRad = Math.Atan2(bx.Y, bx.X);
                double deg = angleRad * 180.0 / Math.PI;
                // Normalise to [-180, 180]
                while (deg > 180) deg -= 360;
                while (deg < -180) deg += 360;
                return Math.Round(deg, 1);
            }
            catch
            {
                return 0.0;
            }
        }

        // ── Step indicators ──────────────────────────────────────────

        private readonly List<Border> _stepBorders = new List<Border>();
        private readonly string[] _stepLabels = { "1", "2", "3", "4", "5", "6", "7", "8" };
        private readonly string[] _stepTitles =
        {
            "Project Info", "Levels", "Grids",
            "Disciplines", "Disc. Config", "Automation", "Region", "Review"
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
                    // Future step — #B48CC8 light purple has ~1.7:1 contrast
                    // against white text. Darkened to #4A148C (deep purple)
                    // so the step number is clearly visible.
                    border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 20, 140));
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

                case 1: // Levels — require at least one and validate each row
                    {
                        var parsed = ParseLevels();
                        if (parsed.Count == 0)
                        {
                            MessageBox.Show(
                                "Please define at least one building level.\n" +
                                "Use a preset or add rows to the table (Name + Elevation in metres).",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                        // Flag invalid elevation rows
                        var bad = LevelRows
                            .Where(r => !string.IsNullOrWhiteSpace(r.Name)
                                        && !double.TryParse(r.ElevationText, NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out _))
                            .ToList();
                        if (bad.Count > 0)
                        {
                            MessageBox.Show(
                                $"{bad.Count} row(s) have an invalid elevation. Use a number in metres (e.g. 0.0, 3.5, -1.20).",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }
                        // Duplicate names
                        var dupNames = LevelRows
                            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                            .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();
                        if (dupNames.Count > 0)
                        {
                            MessageBox.Show(
                                $"Duplicate level names: {string.Join(", ", dupNames)}.\nEach level must have a unique name.",
                                "STING Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return false;
                        }

                        // ISO 19650 name audit — non-blocking. Offer the user a one-click fix.
                        var nonIso = LevelRows
                            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !IsIsoLevelName(r.Name))
                            .Select(r => r.Name)
                            .ToList();
                        if (nonIso.Count > 0)
                        {
                            var dlg = new TaskDialog("ISO 19650 Level Naming")
                            {
                                MainInstruction = $"{nonIso.Count} level(s) do not follow ISO 19650 codes",
                                MainContent =
                                    "ISO 19650 expects level codes like B01 (basement), GF (ground), L01/L02 (above ground), MZ01 (mezzanine), RF (roof).\n\n" +
                                    "Non-conformant: " + string.Join(", ", nonIso.Take(5)) +
                                    (nonIso.Count > 5 ? $" (+{nonIso.Count - 5} more)" : "") + "\n\n" +
                                    "Descriptive suffixes after ' - ' are preserved (e.g. 'L02 - Office Level').",
                                CommonButtons = TaskDialogCommonButtons.Cancel
                            };
                            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                                "Auto-fix with ISO codes (recommended)",
                                "Rewrites level names to ISO 19650 codes. Descriptive suffixes are preserved.");
                            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                                "Continue with non-conformant names",
                                "Names are left as-is. Downstream ISO-19650 checks may flag them.");

                            var res = dlg.Show();
                            if (res == TaskDialogResult.CommandLink1)
                            {
                                LevelIsoNormalize_Click(null, null);
                                // Re-run duplicate check since names changed
                                return false; // stay on page — user will click Next again to accept
                            }
                            if (res == TaskDialogResult.Cancel)
                                return false;
                            // CommandLink2 → continue
                        }
                        return true;
                    }

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
            if (double.TryParse(txtFloorHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h > 0)
                floorH = h;

            LevelRows.Clear();

            void Add(string name, double elev, string type, bool isStory = true)
            {
                LevelRows.Add(new LevelRow
                {
                    Name = name,
                    ElevationText = elev.ToString("F2", CultureInfo.InvariantCulture),
                    LevelType = type,
                    IsStory = isStory
                });
            }

            switch (tag)
            {
                case "residential":
                    Add("Ground Floor", 0.00, "Standard");
                    for (int i = 1; i <= 3; i++) Add($"Level {i:D2}", i * floorH, "Standard");
                    Add("Roof", 4 * floorH, "Roof", isStory: false);
                    break;

                case "commercial":
                    Add("Basement 01", -floorH, "Sub-level");
                    Add("Ground Floor", 0.00, "Standard");
                    for (int i = 1; i <= 5; i++) Add($"Level {i:D2}", i * floorH, "Standard");
                    Add("Roof", 6 * floorH, "Roof", isStory: false);
                    break;

                case "highrise":
                    Add("Basement 02", -2 * floorH, "Sub-level");
                    Add("Basement 01", -floorH, "Sub-level");
                    Add("Ground Floor", 0.00, "Standard");
                    for (int i = 1; i <= 15; i++) Add($"Level {i:D2}", i * floorH, "Standard");
                    Add("Roof", 16 * floorH, "Roof", isStory: false);
                    break;

                case "industrial":
                    Add("Ground Floor", 0.00, "Standard");
                    Add("Mezzanine", floorH, "Sub-level");
                    Add("Roof", 2 * floorH, "Roof", isStory: false);
                    break;

                case "clear":
                    // already cleared
                    break;
            }
            RenumberLevelRows();
        }

        // ── Level toolbar handlers ───────────────────────────────────

        private void LevelAdd_Click(object sender, RoutedEventArgs e)
        {
            // Guess next elevation = last row + floor height
            double nextElev = 0.0;
            double floorH = 3.5;
            if (double.TryParse(txtFloorHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h > 0)
                floorH = h;
            var last = LevelRows.LastOrDefault();
            if (last != null && double.TryParse(last.ElevationText,
                NumberStyles.Float, CultureInfo.InvariantCulture, out double lastEl))
            {
                nextElev = lastEl + floorH;
            }

            int insertAt = dgLevels.SelectedIndex >= 0 ? dgLevels.SelectedIndex + 1 : LevelRows.Count;
            var row = new LevelRow
            {
                Name = $"Level {LevelRows.Count + 1:D2}",
                ElevationText = nextElev.ToString("F2", CultureInfo.InvariantCulture),
                LevelType = "Standard",
                IsStory = true
            };
            if (insertAt >= LevelRows.Count) LevelRows.Add(row); else LevelRows.Insert(insertAt, row);
            RenumberLevelRows();
            dgLevels.SelectedItem = row;
            dgLevels.ScrollIntoView(row);
        }

        private void LevelDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgLevels.SelectedItems.OfType<LevelRow>().ToList();
            if (selected.Count == 0) return;
            foreach (var r in selected) LevelRows.Remove(r);
            RenumberLevelRows();
        }

        private void LevelMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgLevels.SelectedItems.OfType<LevelRow>().ToList();
            if (selected.Count == 0) return;
            foreach (var r in selected.OrderBy(x => LevelRows.IndexOf(x)))
            {
                int i = LevelRows.IndexOf(r);
                if (i > 0) LevelRows.Move(i, i - 1);
            }
            RenumberLevelRows();
        }

        private void LevelMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgLevels.SelectedItems.OfType<LevelRow>().ToList();
            if (selected.Count == 0) return;
            foreach (var r in selected.OrderByDescending(x => LevelRows.IndexOf(x)))
            {
                int i = LevelRows.IndexOf(r);
                if (i >= 0 && i < LevelRows.Count - 1) LevelRows.Move(i, i + 1);
            }
            RenumberLevelRows();
        }

        private void LevelSort_Click(object sender, RoutedEventArgs e)
        {
            var sorted = LevelRows
                .OrderBy(r =>
                {
                    return double.TryParse(r.ElevationText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) ? v : double.MaxValue;
                })
                .ToList();
            LevelRows.Clear();
            foreach (var r in sorted) LevelRows.Add(r);
            RenumberLevelRows();
        }

        // ── Scope-box pattern handler ────────────────────────────────

        private void ScopeBoxPatternApply_Click(object sender, RoutedEventArgs e)
        {
            string pattern = txtScopeBoxPattern.Text?.Trim();
            if (string.IsNullOrEmpty(pattern))
            {
                MessageBox.Show(
                    "Enter a rename pattern (e.g. {BLD}-{ZONE}-{INDEX}).",
                    "STING Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var locs = ParseCodes(txtLocCodes.Text);
            var zones = ParseCodes(txtZoneCodes.Text);
            string bld = locs.FirstOrDefault() ?? "BLD1";

            int idx = 1;
            foreach (var sb in ScopeBoxRows)
            {
                if (!sb.Include) continue;
                string loc = locs.Count > 0 ? locs[(idx - 1) % locs.Count] : bld;
                string zone = zones.Count > 0 ? zones[(idx - 1) % zones.Count] : "Z01";
                string newName = pattern
                    .Replace("{BLD}", bld)
                    .Replace("{LOC}", loc)
                    .Replace("{ZONE}", zone)
                    .Replace("{INDEX}", idx.ToString("D2"))
                    .Replace("{NAME}", sb.CurrentName ?? "");
                sb.NewName = newName;
                idx++;
            }
        }

        // ── Parse helpers ────────────────────────────────────────────

        /// <summary>Read the Levels DataGrid into strongly-typed definitions.</summary>
        private List<LevelDefinition> ParseLevels()
        {
            var result = new List<LevelDefinition>();
            foreach (var row in LevelRows)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                if (!double.TryParse(row.ElevationText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double elev))
                    continue;
                result.Add(new LevelDefinition
                {
                    Name = row.Name.Trim(),
                    ElevationMeters = elev,
                    LevelType = row.LevelType ?? "Standard",
                    IsStory = row.IsStory
                });
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
            if (double.TryParse(txtGridHSpacing.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ghs)) data.GridHSpacing = ghs;
            if (double.TryParse(txtGridHLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ghl)) data.GridHLength = ghl;
            if (int.TryParse(txtGridVCount.Text, out int gvc)) data.GridVCount = gvc;
            if (double.TryParse(txtGridVSpacing.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gvs)) data.GridVSpacing = gvs;
            if (double.TryParse(txtGridVLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gvl)) data.GridVLength = gvl;

            // Page 3: Scope-box integration
            data.AlignToScopeBoxOrientation = chkAlignToScopeBoxes.IsChecked == true;
            data.AssignGridsToScopeBoxes = chkAssignGridsToScopeBoxes.IsChecked == true;
            data.TwoSectionsPerScopeBox = chkTwoSectionsPerScopeBox.IsChecked == true;
            data.RenameScopeBoxes = chkRenameScopeBoxes.IsChecked == true;
            data.ScopeBoxRenamePattern = txtScopeBoxPattern.Text?.Trim() ?? "";
            data.ScopeBoxRenames = ScopeBoxRows
                .Where(sb => sb.Include
                             && !string.IsNullOrWhiteSpace(sb.NewName)
                             && !string.Equals(sb.NewName, sb.CurrentName, StringComparison.Ordinal))
                .ToDictionary(sb => sb.CurrentName, sb => sb.NewName.Trim());
            data.ScopeBoxSelection = ScopeBoxRows
                .Where(sb => sb.Include)
                .Select(sb => sb.CurrentName)
                .ToList();

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

            // Per-discipline title blocks (override the default above when set).
            data.TitleBlockByDiscipline.Clear();
            void CollectTb(string disc, ComboBox combo)
            {
                string v = GetComboValue(combo);
                if (!string.IsNullOrEmpty(v) && v != "(Default)")
                    data.TitleBlockByDiscipline[disc] = v;
            }
            CollectTb("A",  cmbTbArch);
            CollectTb("S",  cmbTbStruct);
            CollectTb("M",  cmbTbMech);
            CollectTb("E",  cmbTbElec);
            CollectTb("P",  cmbTbPlumb);
            CollectTb("FP", cmbTbFire);
            CollectTb("LV", cmbTbLV);
            CollectTb("G",  cmbTbGen);

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
            data.FastMode = chkFastMode?.IsChecked == true;
            data.SuspendUIUpdates = chkSuspendUIUpdates?.IsChecked == true;
            data.UseLatestTemplateSetup = chkUseLatestTemplateSetup.IsChecked == true;
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

            // Page 7: Standards & Region
            data.Region = lstRegionPresets.SelectedItem as string;

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
                mc.HVACDuctMaterial = GetComboValue(cmbMechDuctMat);
                mc.HWSPipeMaterial = GetComboValue(cmbMechHWSPipeMat);
                mc.DHWPipeMaterial = GetComboValue(cmbMechDHWPipeMat);
                mc.GASPipeMaterial = GetComboValue(cmbMechGASPipeMat);
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
                ec.PowerCable = GetComboValue(cmbElecPowerCable);
                ec.LightingCable = GetComboValue(cmbElecLightingCable);
            }

            // Plumbing
            if (data.Disciplines.Contains("P"))
            {
                var pc = data.PlumbConfig;
                pc.IncludeDCW = chkPlumbDCW.IsChecked == true;
                pc.IncludeSAN = chkPlumbSAN.IsChecked == true;
                pc.IncludeRWD = chkPlumbRWD.IsChecked == true;
                pc.DCWPipeMaterial = GetComboValue(cmbPlumbDCWMat);
                pc.SANPipeMaterial = GetComboValue(cmbPlumbSANMat);
                pc.RWDPipeMaterial = GetComboValue(cmbPlumbRWDMat);
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
                fc.SprinklerPipeMaterial = GetComboValue(cmbFireSprinklerMat);
                fc.AlarmCable = GetComboValue(cmbFireAlarmCable);
                fc.SuppressionPipeMaterial = GetComboValue(cmbFireSuppressMat);
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

            // Standards & Region
            sb.AppendLine();
            sb.AppendLine("STANDARDS & REGION");
            sb.AppendLine(new string('─', 55));
            if (!string.IsNullOrEmpty(data.Region) &&
                StingTools.Standards.ProjectStandardsManager.RegionalPresets.TryGetValue(data.Region, out var preset))
            {
                sb.AppendLine($"  Region:        {preset.Name} ({data.Region})");
                sb.AppendLine($"  Electrical:    {preset.ElectricalStandard}");
                sb.AppendLine($"  HVAC:          {preset.HVACStandard}");
                sb.AppendLine($"  Plumbing:      {preset.PlumbingStandard}");
                sb.AppendLine($"  Structural:    {preset.StructuralStandard}");
                sb.AppendLine($"  Fire:          {preset.FireProtectionStandard}");
                sb.AppendLine($"  Lighting:      {preset.LightingStandard}");
                sb.AppendLine($"  Energy:        {preset.EnergyStandard}");
                sb.AppendLine($"  Units:         {preset.UnitSystem}");
            }
            else
            {
                sb.AppendLine("  Region:        (not set — defaults to International)");
            }

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

            // Scope boxes
            int sbChecked = data.ScopeBoxSelection?.Count ?? 0;
            sb.AppendLine();
            sb.AppendLine("SCOPE BOXES");
            sb.AppendLine(new string('─', 55));
            sb.AppendLine($"  Checked:          {sbChecked}");
            sb.AppendLine($"  Align to orient.: {(data.AlignToScopeBoxOrientation ? "YES (tilted-aware)" : "NO")}");
            sb.AppendLine($"  Scope grids:      {(data.AssignGridsToScopeBoxes && sbChecked > 0 ? "YES" : "NO")}");
            sb.AppendLine($"  2 sections each:  {(data.TwoSectionsPerScopeBox && sbChecked > 0 ? $"YES ({sbChecked * 2} sections)" : "NO")}");
            int renames = data.ScopeBoxRenames?.Count ?? 0;
            sb.AppendLine($"  Rename on Run:    {(data.RenameScopeBoxes && renames > 0 ? $"YES ({renames} rename(s))" : "NO")}");
            if (data.RenameScopeBoxes && renames > 0)
            {
                foreach (var kv in data.ScopeBoxRenames)
                    sb.AppendLine($"      {kv.Key}  →  {kv.Value}");
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
            if (data.TitleBlockByDiscipline != null && data.TitleBlockByDiscipline.Count > 0)
            {
                sb.AppendLine("  Per-discipline overrides:");
                foreach (var kv in data.TitleBlockByDiscipline)
                    sb.AppendLine($"      [{kv.Key}] → {kv.Value}");
            }

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
            if (data.UseLatestTemplateSetup)
            {
                // 15-step Template Setup Wizard ordering (latest)
                AddStep(true, "[LATEST] Fill patterns (12 ISO)");
                AddStep(true, "[LATEST] Line patterns (10 ISO 128)");
                AddStep(true, "[LATEST] Line styles (16)");
                AddStep(true, "[LATEST] Object styles (40)");
                AddStep(true, "[LATEST] Text styles (12 ISO 3098)");
                AddStep(true, "[LATEST] Dimension styles (7)");
                AddStep(true, "[LATEST] View filters (28+)");
                AddStep(true, "[LATEST] View templates (23 w/ VG)");
                AddStep(true, "[LATEST] Apply filters to templates");
                AddStep(true, "[LATEST] VG overrides (5-layer)");
                AddStep(true, "[LATEST] Batch family parameters (CSV)");
                AddStep(true, "[LATEST] Template metadata schedules");
            }
            else
            {
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
            bool doTemplatePost = data.UseLatestTemplateSetup || data.CreateTemplates;
            AddStep(doTemplatePost, "Auto-assign view templates by view type / name / phase / level");
            AddStep(doTemplatePost, "Auto-fix template health (fill missing settings per view type)");
            AddStep(data.LoadParams, "Full auto-populate (tokens + dims + MEP + formulas)");
            AddStep(data.LoadParams && !data.UseLatestTemplateSetup, "Batch add family parameters");
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
                        if (mc.IncludeHVAC) sb.AppendLine($"      HVAC  → Duct: {mc.HVACDuctMaterial}");
                        if (mc.IncludeHWS)  sb.AppendLine($"      HWS   → Pipe: {mc.HWSPipeMaterial}");
                        if (mc.IncludeDHW)  sb.AppendLine($"      DHW   → Pipe: {mc.DHWPipeMaterial}");
                        if (mc.IncludeGAS)  sb.AppendLine($"      GAS   → Pipe: {mc.GASPipeMaterial}");
                        if (mc.InsulationMm > 0)
                            sb.AppendLine($"      Insulation: {mc.InsulationMm}mm");
                        break;

                    case "E":
                        var ec = data.ElecConfig;
                        if (ec.IncludePower)    sb.AppendLine($"      Power    → Cable: {ec.PowerCable}");
                        if (ec.IncludeLighting) sb.AppendLine($"      Lighting → Cable: {ec.LightingCable}");
                        sb.AppendLine($"      {ec.Voltage}, {ec.PhaseSystem}");
                        sb.AppendLine($"      Containment: {ec.Containment}, {ec.IPRating}");
                        break;

                    case "P":
                        var pc = data.PlumbConfig;
                        if (pc.IncludeDCW) sb.AppendLine($"      DCW → Pipe: {pc.DCWPipeMaterial}");
                        if (pc.IncludeSAN) sb.AppendLine($"      SAN → Pipe: {pc.SANPipeMaterial}");
                        if (pc.IncludeRWD) sb.AppendLine($"      RWD → Pipe: {pc.RWDPipeMaterial}");
                        sb.AppendLine($"      Fixtures: {pc.FixtureStandard}");
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
                        if (fc.IncludeSprinkler)   sb.AppendLine($"      Sprinkler   → Pipe: {fc.SprinklerPipeMaterial}");
                        if (fc.IncludeAlarm)       sb.AppendLine($"      Alarm       → Cable: {fc.AlarmCable}");
                        if (fc.IncludeSuppression) sb.AppendLine($"      Suppression → Pipe: {fc.SuppressionPipeMaterial}");
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
        /// <summary>User-facing classification (Standard, Structural, Reference, Ceiling, Sub-level, Roof, Parapet).</summary>
        public string LevelType { get; set; } = "Standard";
        /// <summary>Whether the level is marked as a building story in Revit.</summary>
        public bool IsStory { get; set; } = true;
    }

    /// <summary>Row bound to the Levels DataGrid.</summary>
    public class LevelRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private int _index; public int Index { get => _index; set { _index = value; Raise(nameof(Index)); } }
        private string _name; public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
        private string _elev = "0.00"; public string ElevationText { get => _elev; set { _elev = value; Raise(nameof(ElevationText)); } }
        private string _type = "Standard"; public string LevelType { get => _type; set { _type = value; Raise(nameof(LevelType)); } }
        private bool _story = true; public bool IsStory { get => _story; set { _story = value; Raise(nameof(IsStory)); } }

        /// <summary>Revit ElementId.Value (Int64) if this row came from an existing level; 0 for new.</summary>
        public long ExistingId { get; set; }
    }

    /// <summary>Row bound to the Scope-Box DataGrid.</summary>
    public class ScopeBoxRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private bool _include = true; public bool Include { get => _include; set { _include = value; Raise(nameof(Include)); } }
        public string CurrentName { get; set; }
        private string _newName; public string NewName { get => _newName; set { _newName = value; Raise(nameof(NewName)); } }
        public double RotationDegrees { get; set; }
        public string RotationText => RotationDegrees == 0 ? "0°" : $"{RotationDegrees:F1}°";
        /// <summary>Revit ElementId.Value (Int64) for the scope box.</summary>
        public long RevitIdValue { get; set; }
    }

    /// <summary>
    /// All data collected by the Project Setup Wizard.
    /// Passed to <see cref="Temp.ProjectSetupCommand"/> for execution.
    /// </summary>
    public class ProjectSetupData
    {
        // Page 7: Standards regional preset (UK / USA / EastAfrica / Uganda /
        // Kenya / SouthAfrica / Australia / Europe / International). Run step
        // calls ProjectStandardsManager.ApplyRegionalPreset and writes
        // PROJECT_REGION onto Project Information so the choice travels with
        // the .rvt and OnDocumentOpened can re-sync the singleton.
        public string Region { get; set; }

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

        // Page 3: Scope-box integration
        /// <summary>When true, align grids/sections/elevations to the checked scope boxes' orientation (handles tilted boxes).</summary>
        public bool AlignToScopeBoxOrientation { get; set; }
        /// <summary>When true, assign created grids to the checked scope boxes (DATUM_VOLUME_OF_INTEREST).</summary>
        public bool AssignGridsToScopeBoxes { get; set; }
        /// <summary>When true, create two building sections (through centre, both directions) per checked scope box.</summary>
        public bool TwoSectionsPerScopeBox { get; set; }
        /// <summary>When true, rename scope boxes using the <see cref="ScopeBoxRenames"/> map on Run.</summary>
        public bool RenameScopeBoxes { get; set; }
        /// <summary>User-entered rename pattern (for audit; the actual map is in <see cref="ScopeBoxRenames"/>).</summary>
        public string ScopeBoxRenamePattern { get; set; } = "";
        /// <summary>Current-name → new-name map for checked scope boxes where the name actually changed.</summary>
        public Dictionary<string, string> ScopeBoxRenames { get; set; } = new Dictionary<string, string>();
        /// <summary>Current names of checked scope boxes (used for grid scoping + section creation).</summary>
        public List<string> ScopeBoxSelection { get; set; } = new List<string>();

        // Page 4: Disciplines
        public List<string> Disciplines { get; set; } = new List<string>();
        public string TitleBlockName { get; set; }

        /// <summary>Per-discipline title block overrides ("A"/"S"/"M"/"E"/"P"/"FP"/"LV"/"G" → "Family : Type").
        /// Missing entries fall back to <see cref="TitleBlockName"/>, then to first available.</summary>
        public Dictionary<string, string> TitleBlockByDiscipline { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        /// <summary>When true, Phase 3 runs the full 15-step TemplateSetupWizard ordering (latest pipeline).</summary>
        public bool UseLatestTemplateSetup { get; set; } = true;
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

        /// <summary>Fast setup mode — scan document first, skip bulk steps (materials, schedules, templates)
        /// when ≥80% of target already exists. Typically reduces 30+ min re-runs to under 5 min.</summary>
        public bool FastMode { get; set; } = true;

        /// <summary>Switch to a blank drafting view during setup so Revit skips active-view regen
        /// on every transaction. Restored to the original view when setup finishes.</summary>
        public bool SuspendUIUpdates { get; set; } = true;

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

        // Per-system materials — picked from the Disc. Config page dropdowns.
        // HVAC uses ducts; HWS/DHW/GAS use pipes. Each system can have its own material.
        public string HVACDuctMaterial { get; set; } = "Galvanised Steel";
        public string HWSPipeMaterial { get; set; } = "Steel (Black)";
        public string DHWPipeMaterial { get; set; } = "Copper";
        public string GASPipeMaterial { get; set; } = "Steel (Black)";
        public int InsulationMm { get; set; } = 25;

        // Back-compat: old single-material props delegate to the primary HVAC duct/HWS pipe.
        public string DuctMaterial
        {
            get => HVACDuctMaterial;
            set => HVACDuctMaterial = value;
        }
        public string PipeMaterial
        {
            get => HWSPipeMaterial;
            set => HWSPipeMaterial = value;
        }

        /// <summary>Get the material for a given system code (HVAC / HWS / DHW / GAS).</summary>
        public string GetMaterialFor(string systemCode)
        {
            switch ((systemCode ?? "").ToUpperInvariant())
            {
                case "HVAC": return HVACDuctMaterial;
                case "HWS":  return HWSPipeMaterial;
                case "DHW":  return DHWPipeMaterial;
                case "GAS":  return GASPipeMaterial;
                default:     return "";
            }
        }
    }

    public class ElectricalConfig
    {
        public bool IncludePower { get; set; } = true;
        public bool IncludeLighting { get; set; } = true;
        public string Voltage { get; set; } = "230V (UK/EU)";
        public string PhaseSystem { get; set; } = "Single Phase";
        public string Containment { get; set; } = "Cable Tray + Conduit";
        public string IPRating { get; set; } = "IP20";

        // Per-system cable type (BS 7671-aware).
        public string PowerCable { get; set; } = "XLPE/SWA (LSZH)";
        public string LightingCable { get; set; } = "Twin & Earth (6242Y)";
    }

    public class PlumbingConfig
    {
        public bool IncludeDCW { get; set; } = true;
        public bool IncludeSAN { get; set; } = true;
        public bool IncludeRWD { get; set; }

        // Per-system pipe materials.
        public string DCWPipeMaterial { get; set; } = "Copper";
        public string SANPipeMaterial { get; set; } = "Cast Iron";
        public string RWDPipeMaterial { get; set; } = "uPVC";

        public string FixtureStandard { get; set; } = "Commercial Grade";

        // Back-compat facade (delegates to DCW).
        public string PipeMaterial
        {
            get => DCWPipeMaterial;
            set => DCWPipeMaterial = value;
        }

        /// <summary>Get the pipe material for a given plumbing system code (DCW / SAN / RWD).</summary>
        public string GetMaterialFor(string systemCode)
        {
            switch ((systemCode ?? "").ToUpperInvariant())
            {
                case "DCW": return DCWPipeMaterial;
                case "SAN": return SANPipeMaterial;
                case "RWD": return RWDPipeMaterial;
                default:    return "";
            }
        }
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

        // Per-system materials.
        public string SprinklerPipeMaterial { get; set; } = "Steel (Black, Welded)";
        public string AlarmCable { get; set; } = "FP200 Gold (Std Fire)";
        public string SuppressionPipeMaterial { get; set; } = "Steel (Galvanised)";
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
