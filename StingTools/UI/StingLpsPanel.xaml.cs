using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Lightning;
using StingTools.Core.Fabrication;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Lightning Protection Center dockable panel.
    /// 7 tabs: RISK · AIR-TERM · CONDUCTORS · EARTH · SPD · ZONES · RPRT.
    /// Every button click dispatches via <see cref="StingLpsCommandHandler"/>
    /// so Revit API calls run on the Revit API thread.
    ///
    /// Mirrors the StingHvacPanel / StingElectricalPanel architecture.
    /// </summary>
    public partial class StingLpsPanel : Page
    {
        // ── Grid view-models ────────────────────────────────────────────
        public ObservableCollection<RiskFactorRow>      RiskFactorRows      { get; } = new();
        public ObservableCollection<AirTerminalRow>     AirTerminalRows     { get; } = new();
        public ObservableCollection<DownConductorRow>   DownConductorRows   { get; } = new();
        public ObservableCollection<EarthElectrodeRow>  EarthElectrodeRows  { get; } = new();
        public ObservableCollection<BondingRow>         BondingRows         { get; } = new();
        public ObservableCollection<SpdRowVm>           SpdRows             { get; } = new();
        public ObservableCollection<LpzZoneRow>         ZoneRows            { get; } = new();
        public ObservableCollection<InspectionRow>      InspectionRows      { get; } = new();
        public ObservableCollection<LpsWorkflowRow>     WorkflowRows        { get; } = new();
        public ObservableCollection<ResidualRiskRow>    ResidualRiskRows    { get; } = new();
        public ObservableCollection<LossTypeRow>        LossTypeRows        { get; } = new();

        // ── Header state ────────────────────────────────────────────────
        public string SelectedStandard  { get; private set; } = "BS_EN_62305";
        public string SelectedLpsClass  { get; private set; } = "II";
        public string SelectedMethod    { get; private set; } = "ROLLING_SPHERE";
        public string SelectedMaterial  { get; private set; } = "COPPER";
        public string SelectedRegion    { get; private set; } = "UK";
        public string SelectedScope     { get; private set; } = "ActiveView";
        public double SelectedEquipmentWithstandKv { get; private set; } = 1.5;
        public double SelectedSoilResistivityOhmM  { get; private set; } = 100.0;

        // ── RISK tab inputs (bound by name from XAML) ───────────────────
        public double RiskPlanLengthM       { get; private set; } = 30.0;
        public double RiskPlanWidthM        { get; private set; } = 20.0;
        public double RiskHeightM           { get; private set; } = 12.0;
        public double RiskNgFlashDensity    { get; private set; } = 0.0; // 0 ⇒ use region default
        public double RiskBuildingTypeCb    { get; private set; } = 1.0;
        public double RiskInternalContentCc { get; private set; } = 1.0;
        public double RiskOccupantHazardCd  { get; private set; } = 1.0;
        public double RiskConsequenceCe     { get; private set; } = 1.0;
        public double RiskLocationFactorCd  { get; private set; } = 1.0;
        public double RiskTolerableRisk     { get; private set; } = 1e-5;
        public bool   RiskAutoApplyClass    { get; private set; } = true;

        private static StingLpsPanel _instance;
        public static StingLpsPanel Instance => _instance;

        public StingLpsPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }

            _instance = this;

            // Bind grids
            try { RiskFactorGrid.ItemsSource     = RiskFactorRows; }     catch (Exception ex) { StingLog.Warn($"RiskFactorGrid bind: {ex.Message}"); }
            try { AirTerminalGrid.ItemsSource    = AirTerminalRows; }    catch (Exception ex) { StingLog.Warn($"AirTerminalGrid bind: {ex.Message}"); }
            try { DownConductorGrid.ItemsSource  = DownConductorRows; }  catch (Exception ex) { StingLog.Warn($"DownConductorGrid bind: {ex.Message}"); }
            try { EarthElectrodeGrid.ItemsSource = EarthElectrodeRows; } catch (Exception ex) { StingLog.Warn($"EarthElectrodeGrid bind: {ex.Message}"); }
            try { BondingGrid.ItemsSource        = BondingRows; }        catch (Exception ex) { StingLog.Warn($"BondingGrid bind: {ex.Message}"); }
            try { SpdGrid.ItemsSource            = SpdRows; }            catch (Exception ex) { StingLog.Warn($"SpdGrid bind: {ex.Message}"); }
            try { ZoneGrid.ItemsSource           = ZoneRows; }           catch (Exception ex) { StingLog.Warn($"ZoneGrid bind: {ex.Message}"); }
            try { InspectionGrid.ItemsSource     = InspectionRows; }     catch (Exception ex) { StingLog.Warn($"InspectionGrid bind: {ex.Message}"); }
            try { WorkflowGrid.ItemsSource       = WorkflowRows; }       catch (Exception ex) { StingLog.Warn($"WorkflowGrid bind: {ex.Message}"); }
            try { ResidualGrid.ItemsSource       = ResidualRiskRows; }   catch (Exception ex) { StingLog.Warn($"ResidualGrid bind: {ex.Message}"); }
            try { LossTypeGrid.ItemsSource       = LossTypeRows; }       catch (Exception ex) { StingLog.Warn($"LossTypeGrid bind: {ex.Message}"); }

            // Wave 1 — populate the RISK-tab catalogue combos from
            // STING_LPS_RISK_FACTORS.json so labels match the data the
            // engine actually consumes (closes the "Cb metal/concrete"
            // confusion). Catalogue load is one-shot at construction.
            try { LoadRiskCatalogue(); }
            catch (Exception ex) { StingLog.Warn($"LoadRiskCatalogue: {ex.Message}"); }

            // Seed default risk factor rows so the RISK tab is non-empty.
            try { SeedDefaultRiskFactors(); }
            catch (Exception ex) { StingLog.Warn($"SeedDefaultRiskFactors: {ex.Message}"); }
        }

        private void SeedDefaultRiskFactors()
        {
            // Labels now match the BS EN 62305-2 occupancy-type model
            // used by STING_LPS_RISK_FACTORS.json (closes the
            // "construction material" confusion). The grid stays as a
            // manual-override surface; the combo above is the primary
            // input. Values match commercial / ordinary defaults.
            RiskFactorRows.Clear();
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cb — Building occupancy",  Description = "Drives life-safety risk; e.g. residential=0.5 · commercial=1.0 · industrial=1.5 · healthcare=2.5 · hazardous=3.0", Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cc — Internal contents",   Description = "Ordinary=1.0 · valuable=2.0 · irreplaceable=3.0 · high-fire=2.5 · explosive=5.0",                              Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cd — Occupant hazard",     Description = "Low (few occupants)=1.0 · medium (public access)=2.0 · high (vulnerable/mass)=5.0",                          Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Ce — Consequence of failure", Description = "Low=1.0 · medium=2.0 · high=5.0 · extreme=10.0",                                                          Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cd_loc — Location factor",  Description = "BS EN 62305-2 §A.4: isolated=2.0 · low hill=1.0 · surrounded by taller=0.25 · in city=0.5",                  Value = 1.0 });
        }

        // ──────────────────────────────────────────────────────────────
        //  Wave 1 — Risk catalogue loader. Reads STING_LPS_RISK_FACTORS.json
        //  and seeds the RISK-tab combos so users pick from real BS EN 62305-2
        //  categories rather than typing arbitrary coefficient values. Each
        //  combo carries (label, value) pairs in (.Content, .Tag); the shared
        //  cmbRiskCatalogue_SelectionChanged handler updates the matching
        //  RiskFactorRows row Value when an item is picked.
        // ──────────────────────────────────────────────────────────────
        private void LoadRiskCatalogue()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_LPS_RISK_FACTORS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("LoadRiskCatalogue: STING_LPS_RISK_FACTORS.json not found.");
                    return;
                }
                var root = JObject.Parse(File.ReadAllText(path));
                PopulateCombo(cmbBldType,        root["buildingTypes"]     as JArray, "cb");
                PopulateCombo(cmbContent,        root["internalContent"]   as JArray, "cc");
                PopulateCombo(cmbOccupant,       root["occupantHazard"]    as JArray, "cd");
                PopulateCombo(cmbConsequence,    root["consequenceOfFailure"] as JArray, "ce");
                PopulateCombo(cmbServiceFactor,  root["serviceFactors"]    as JArray, "ct");
                PopulateLocationFactorCombo(cmbLocFactor);
            }
            catch (Exception ex) { StingLog.Warn($"LoadRiskCatalogue: {ex.Message}"); }
        }

        private static void PopulateCombo(ComboBox combo, JArray arr, string valueKey)
        {
            if (combo == null || arr == null) return;
            combo.Items.Clear();
            foreach (var entry in arr)
            {
                string label = entry["label"]?.ToString() ?? entry["id"]?.ToString() ?? "—";
                double v = entry[valueKey]?.Value<double>() ?? 1.0;
                combo.Items.Add(new ComboBoxItem
                {
                    Content = $"{label}  ({valueKey} = {v:F2})",
                    Tag     = v.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            // Default = first entry with value closest to 1.0 (typical).
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static void PopulateLocationFactorCombo(ComboBox combo)
        {
            if (combo == null) return;
            combo.Items.Clear();
            // BS EN 62305-2 Annex A.4 — Cd location factor lookup table.
            // Hard-coded because the data file's serviceFactors array is
            // for service-entry Ct, not location Cd.
            var locOptions = new (string Label, double Value)[]
            {
                ("Surrounded by taller objects (city)",            0.25),
                ("Within taller-object cluster (suburban)",        0.5),
                ("Isolated structure (no taller objects nearby)",  1.0),
                ("Isolated on hill / mountain top",                2.0),
            };
            foreach (var o in locOptions)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = $"{o.Label}  (Cd = {o.Value:F2})",
                    Tag     = o.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            combo.SelectedIndex = 2; // isolated = 1.0 default
        }

        private void cmbRiskCatalogue_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (RiskFactorRows.Count < 5) return;
                if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && item.Tag is string tag
                    && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    if (cb == cmbBldType)        RiskFactorRows[0].Value = v;
                    else if (cb == cmbContent)   RiskFactorRows[1].Value = v;
                    else if (cb == cmbOccupant)  RiskFactorRows[2].Value = v;
                    else if (cb == cmbConsequence) RiskFactorRows[3].Value = v;
                    else if (cb == cmbLocFactor) RiskFactorRows[4].Value = v;
                    // Service-entry Ct doesn't feed the simplified risk
                    // model directly — kept in the UI so it shows up in
                    // reports and Phase 3 when STING wires §B.3 properly.
                    try { RiskFactorGrid?.Items.Refresh(); } catch { }
                }
            }
            catch (Exception ex) { StingLog.Warn($"cmbRiskCatalogue_SelectionChanged: {ex.Message}"); }
        }

        // ── Click dispatch ──────────────────────────────────────────────
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                PullRiskInputsFromUI();
                PushSnapshotToHandler();
                StingLpsCommandHandler.Instance?.SetCommand(tag);
                UpdateStatus($"Running: {tag}");
            }
        }

        private void PullRiskInputsFromUI()
        {
            // Parse all RISK textboxes back into typed fields. Done at click time
            // (not on every keystroke) so users can scrub values freely without
            // generating per-character validation noise.
            try
            {
                RiskPlanLengthM    = ParseD(txtRiskLen?.Text,  RiskPlanLengthM);
                RiskPlanWidthM     = ParseD(txtRiskWid?.Text,  RiskPlanWidthM);
                RiskHeightM        = ParseD(txtRiskHgt?.Text,  RiskHeightM);
                RiskNgFlashDensity = ParseD(txtRiskNg?.Text,   RiskNgFlashDensity);
                RiskTolerableRisk  = ParseD(txtRiskRt?.Text,   RiskTolerableRisk);
                RiskAutoApplyClass = chkRiskAutoApply?.IsChecked == true;

                // Pull C-factors from the editable grid by row index.
                if (RiskFactorRows.Count >= 5)
                {
                    RiskBuildingTypeCb    = RiskFactorRows[0].Value;
                    RiskInternalContentCc = RiskFactorRows[1].Value;
                    RiskOccupantHazardCd  = RiskFactorRows[2].Value;
                    RiskConsequenceCe     = RiskFactorRows[3].Value;
                    RiskLocationFactorCd  = RiskFactorRows[4].Value;
                }

                // Update factor inputs from the bonding grid soil resistivity if present
                if (cmbSoil?.SelectedItem is ComboBoxItem si && si.Tag is string st
                    && double.TryParse(st, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double rho))
                    SelectedSoilResistivityOhmM = rho;
            }
            catch (Exception ex) { StingLog.Warn($"PullRiskInputs: {ex.Message}"); }
        }

        private static double ParseD(string s, double fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        private void PushSnapshotToHandler()
        {
            try
            {
                StingLpsCommandHandler.SetInputs(
                    standard:              SelectedStandard,
                    lpsClass:              SelectedLpsClass,
                    airTermMethod:         SelectedMethod,
                    material:              SelectedMaterial,
                    region:                SelectedRegion,
                    scope:                 SelectedScope,
                    equipmentWithstandKv:  SelectedEquipmentWithstandKv,
                    soilResistivityOhmM:   SelectedSoilResistivityOhmM);
            }
            catch (Exception ex) { StingLog.Warn($"PushSnapshot: {ex.Message}"); }
        }

        private void UpdateStatus(string text)
        {
            try { if (txtLpsStatus != null) txtLpsStatus.Text = text; }
            catch (Exception ex) { StingLog.Warn($"Status update: {ex.Message}"); }
        }

        // ── Header combo handlers ───────────────────────────────────────
        private void cmbStandard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStandard?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedStandard = tag;
        }

        private void cmbLpsClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbLpsClass?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedLpsClass = tag;
        }

        private void cmbMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbMaterial?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedMaterial = tag;
        }

        private void cmbRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRegion?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedRegion = tag;
        }

        private void cmbUw_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbUw?.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                SelectedEquipmentWithstandKv = v;
        }

        private void cmbSoil_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSoil?.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                SelectedSoilResistivityOhmM = v;
        }

        private void Method_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedMethod = tag;
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedScope = tag;
        }

        // ── Public API surface for commands ─────────────────────────────

        /// <summary>Called by LpsRiskAssessmentInlineCommand after running the engine.</summary>
        public void SetRiskResult(LpsRiskResult result)
        {
            try
            {
                Action act = () =>
                {
                    if (result == null) return;
                    if (txtRiskAe   != null) txtRiskAe.Text   = $"{result.CollectionAreaM2:F0} m²";
                    if (txtRiskNd   != null) txtRiskNd.Text   = $"{result.AnnualStrikeFrequency:F4} /yr";
                    double r1 = 0; result.RiskComponents.TryGetValue("R1_Direct", out r1);
                    if (txtRiskR1   != null) txtRiskR1.Text   = r1.ToString("E2");
                    if (txtRiskRt2  != null) txtRiskRt2.Text  = result.TolerableRisk.ToString("E2");
                    if (txtRiskNeed != null) txtRiskNeed.Text = result.RequiresLps ? "YES" : "NO";
                    if (txtRiskCls  != null) txtRiskCls.Text  = result.RecommendedClass ?? "—";

                    // Loss-type R1–R4 grid — Wave 1 accuracy upgrade.
                    LossTypeRows.Clear();
                    if (result.RiskByLossType != null)
                    {
                        string[] order = { "L1", "L2", "L3", "L4" };
                        foreach (var k in order)
                        {
                            if (!result.RiskByLossType.TryGetValue(k, out double rv)) continue;
                            result.TolerableByLossType.TryGetValue(k, out double rt);
                            bool ok = rt <= 0 ? false : rv <= rt;
                            LossTypeRows.Add(new LossTypeRow
                            {
                                LossType  = k,
                                Risk      = rv.ToString("E2"),
                                Tolerable = rt > 0 ? rt.ToString("E2") : "—",
                                StatusDot = ok ? "✓ PASS" : "✗ FAIL — LPS required"
                            });
                        }
                    }

                    // Inline residual-risk-by-class grid — replaces the modal
                    // wizard's residual-risk table. Pass / fail vs worst-case
                    // tolerable risk (min over all 4 loss types).
                    ResidualRiskRows.Clear();
                    if (result.ResidualRiskByClass != null)
                    {
                        double worstRt = result.TolerableByLossType.Count > 0
                            ? result.TolerableByLossType.Values.Min()
                            : result.TolerableRisk;
                        foreach (var kv in result.ResidualRiskByClass.OrderBy(k => k.Key))
                        {
                            bool ok = kv.Value <= worstRt;
                            ResidualRiskRows.Add(new ResidualRiskRow
                            {
                                Class       = kv.Key,
                                Residual    = kv.Value.ToString("E2"),
                                StatusDot   = ok ? "✓ PASS" : "⚠ WARN",
                                Recommended = string.Equals(kv.Key, result.RecommendedClass, StringComparison.OrdinalIgnoreCase) ? "★" : ""
                            });
                        }
                    }
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"SetRiskResult: {ex.Message}"); }
        }

        // ──────────────────────────────────────────────────────────────
        //  Document loaders — Phase 2 follow-up that closes the
        //  "grids seeded empty" caveat. Each loader walks the doc
        //  with LpsEngine.CollectLpsFamily (param-aware + family-name
        //  fallback) and projects each FamilyInstance into the
        //  matching row VM. Called by LpsLoadModelCommand on demand
        //  (panel toolbar) so the cost of the FilteredElementCollector
        //  passes only fires when the user asks.
        // ──────────────────────────────────────────────────────────────

        public void LoadAllFromDoc(Document doc)
        {
            if (doc == null) return;
            try
            {
                Action act = () =>
                {
                    LoadAirTerminalsFromDoc(doc);
                    LoadDownConductorsFromDoc(doc);
                    LoadEarthElectrodesFromDoc(doc);
                    LoadBondingFromDoc(doc);
                    LoadZonesFromDoc(doc);
                    LoadInspectionsFromDoc(doc);
                    LoadSpdsFromDoc(doc);
                    UpdateStatus(
                        $"Loaded: {AirTerminalRows.Count} AT · {DownConductorRows.Count} DC · " +
                        $"{EarthElectrodeRows.Count} earth · {BondingRows.Count} bond · " +
                        $"{ZoneRows.Count} rooms · {SpdRows.Count} SPD");
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"LoadAllFromDoc: {ex.Message}"); }
        }

        private void LoadAirTerminalsFromDoc(Document doc)
        {
            try
            {
                AirTerminalRows.Clear();
                string projClass = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
                double projR     = LpsEngine.GetDoubleParam(doc.ProjectInformation, LpsParams.ROLLING_SPHERE_RADIUS_M);
                if (projR <= 0)
                {
                    var def = LpsEngine.LoadClass(string.IsNullOrWhiteSpace(projClass) ? "II" : projClass);
                    projR = def?.RollingSphereRadiusM ?? 30;
                }

                int n = 1;
                foreach (var at in LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin", "Air-Terminal"))
                {
                    double rM = LpsEngine.GetDoubleParam(at, LpsParams.ROLLING_SPHERE_RADIUS_M);
                    if (rM <= 0) rM = projR;
                    double hM = 0;
                    try
                    {
                        var bb = at.get_BoundingBox(null);
                        if (bb != null)
                            hM = UnitUtils.ConvertFromInternalUnits(bb.Max.Z - bb.Min.Z, UnitTypeId.Meters);
                    }
                    catch (Exception ex) { StingLog.Warn($"AT bbox: {ex.Message}"); }
                    AirTerminalRows.Add(new AirTerminalRow
                    {
                        Tag      = ParameterHelpers.GetString(at, ParamRegistry.SEQ) is string s && !string.IsNullOrEmpty(s) ? $"AT-{s}" : $"AT-{n:D3}",
                        HeightM  = Math.Round(hM, 2),
                        Family   = at.Symbol?.FamilyName ?? "",
                        RadiusM  = Math.Round(rM, 1),
                        StatusDot = "•"
                    });
                    n++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadAirTerminals: {ex.Message}"); }
        }

        private void LoadDownConductorsFromDoc(Document doc)
        {
            try
            {
                DownConductorRows.Clear();
                int count = 0;
                var dcs = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
                double kc = LpsEngine.ComputeKcFactor(dcs.Count);
                string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
                if (string.IsNullOrWhiteSpace(classId)) classId = "II";

                foreach (var dc in dcs)
                {
                    count++;
                    double L   = LpsEngine.GetConductorLengthM(dc);
                    string mat = ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT);
                    if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                    double s  = LpsEngine.GetDoubleParam(dc, LpsParams.SEPARATION_DISTANCE_MM);
                    if (s <= 0) s = LpsEngine.ComputeSeparationDistance(classId, L, mat, kc);
                    double cs = LpsEngine.GetDoubleParam(dc, LpsParams.CONDUCTOR_CROSS_SECT_MM2);

                    string status = ParameterHelpers.GetString(dc, LpsParams.COMPLIANCE_STATUS_TXT);
                    string dot = status?.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase) >= 0 ? "✗"
                               : status?.IndexOf("OK",   StringComparison.OrdinalIgnoreCase) >= 0 ? "✓" : "•";

                    DownConductorRows.Add(new DownConductorRow
                    {
                        Tag             = $"DC-{count:D3}",
                        LengthM         = Math.Round(L, 2),
                        Material        = mat,
                        SpacingM        = 0, // computed in the spacing checker per-pair
                        SeparationMm    = Math.Round(s, 0),
                        CrossSectionMm2 = cs,
                        StatusDot       = dot
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadDownConductors: {ex.Message}"); }
        }

        private void LoadEarthElectrodesFromDoc(Document doc)
        {
            try
            {
                EarthElectrodeRows.Clear();
                int n = 1;
                foreach (var el in LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth_Rod", "Earth Electrode"))
                {
                    double L = 0;
                    try
                    {
                        var bb = el.get_BoundingBox(null);
                        if (bb != null) L = UnitUtils.ConvertFromInternalUnits(bb.Max.Z - bb.Min.Z, UnitTypeId.Meters);
                    }
                    catch (Exception ex) { StingLog.Warn($"EE bbox: {ex.Message}"); }
                    double r = LpsEngine.GetDoubleParam(el, LpsParams.EARTH_RESISTANCE_OHM);
                    string type = ParameterHelpers.GetString(el, LpsParams.EARTH_TYPE_TXT);
                    if (string.IsNullOrWhiteSpace(type)) type = "A";
                    string status = ParameterHelpers.GetString(el, LpsParams.COMPLIANCE_STATUS_TXT);
                    string dot = status?.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase) >= 0 ? "✗"
                               : status?.IndexOf("OK",   StringComparison.OrdinalIgnoreCase) >= 0 ? "✓"
                               : (r > 0 ? "•" : "?");
                    EarthElectrodeRows.Add(new EarthElectrodeRow
                    {
                        Tag           = $"EE-{n:D3}",
                        ArrangeType   = type,
                        LengthM       = Math.Round(L, 2),
                        ResistanceOhm = Math.Round(r, 2),
                        StatusDot     = dot
                    });
                    n++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadEarthElectrodes: {ex.Message}"); }
        }

        private void LoadBondingFromDoc(Document doc)
        {
            try
            {
                BondingRows.Clear();
                // Pull any element with BOND_TYPE_TXT set — bonding inventory
                // writes that param on all candidates. Keep it lightweight
                // (3-category sweep) rather than the full 6-cat collector
                // the inventory command uses; the grid is editable so the
                // user can extend.
                BuiltInCategory[] cats = {
                    BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray
                };
                int n = 1;
                foreach (var bic in cats)
                {
                    try
                    {
                        var coll = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                        foreach (var el in coll)
                        {
                            string bond = ParameterHelpers.GetString(el, LpsParams.BOND_TYPE_TXT);
                            if (string.IsNullOrWhiteSpace(bond)) continue;
                            BondingRows.Add(new BondingRow
                            {
                                Tag       = $"B-{n:D3}",
                                FromLpz   = "",
                                ToLpz     = "",
                                BondType  = bond,
                                StatusDot = string.Equals(bond, "REVIEW REQUIRED", StringComparison.OrdinalIgnoreCase) ? "⚠" : "✓"
                            });
                            n++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"LoadBonding {bic}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadBonding: {ex.Message}"); }
        }

        private void LoadZonesFromDoc(Document doc)
        {
            try
            {
                ZoneRows.Clear();
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .Where(r => (r as Room)?.Area > 0)
                    .Cast<Room>()
                    .OrderBy(r => doc.GetElement(r.LevelId)?.Name ?? "")
                    .ThenBy(r => r.Number ?? "")
                    .ToList();
                foreach (var r in rooms)
                {
                    string lpz = ParameterHelpers.GetString(r, LpsParams.ZONE_TXT);
                    ZoneRows.Add(new LpzZoneRow
                    {
                        RoomName = $"{r.Number} {r.Name}",
                        Level    = doc.GetElement(r.LevelId)?.Name ?? "",
                        Lpz      = string.IsNullOrWhiteSpace(lpz) ? "LPZ1" : lpz,
                        Colour   = "" // populated by Lps_ColourZones
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadZones: {ex.Message}"); }
        }

        private void LoadInspectionsFromDoc(Document doc)
        {
            try
            {
                InspectionRows.Clear();
                string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
                int defaultInterval = LpsEngine.LoadClass(
                    string.IsNullOrWhiteSpace(classId) ? "II" : classId)?.InspectionIntervalMonths ?? 12;

                var all = new List<Autodesk.Revit.DB.FamilyInstance>();
                all.AddRange(LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin"));
                all.AddRange(LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor"));
                all.AddRange(LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod"));

                int n = 1;
                DateTime today = DateTime.Today;
                foreach (var el in all)
                {
                    string lastStr = ParameterHelpers.GetString(el, LpsParams.TEST_DATE_TXT);
                    int interval = (int)Math.Round(LpsEngine.GetDoubleParam(el, LpsParams.INSPECTION_INTERVAL_MONTHS));
                    if (interval <= 0) interval = defaultInterval;
                    DateTime nextDue = today;
                    string status = "DUE NOW";
                    if (DateTime.TryParse(lastStr, out var lastTest))
                    {
                        nextDue = lastTest.AddMonths(interval);
                        int days = (nextDue - today).Days;
                        status = days < 0 ? $"OVERDUE {-days}d"
                               : days < 30 ? $"DUE IN {days}d"
                               : "OK";
                    }
                    InspectionRows.Add(new InspectionRow
                    {
                        Tag      = $"LPS-{n:D3} ({el.Category?.Name ?? ""})",
                        LastTest = string.IsNullOrEmpty(lastStr) ? "—" : lastStr,
                        NextDue  = nextDue.ToString("yyyy-MM-dd"),
                        Status   = status
                    });
                    n++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadInspections: {ex.Message}"); }
        }

        private void LoadSpdsFromDoc(Document doc)
        {
            try
            {
                // Don't wipe a user-edited grid — only load when empty.
                if (SpdRows.Count > 0) return;
                foreach (var fi in LpsEngine.CollectLpsFamily(doc, "SPD", "Surge"))
                {
                    string loc = ParameterHelpers.GetString(fi, LpsParams.SURGE_PROTECTION_LVL_TXT);
                    SpdRows.Add(new SpdRowVm
                    {
                        Tag           = fi.Symbol?.FamilyName ?? "SPD",
                        LocationId    = loc ?? "MAIN_INCOMER",
                        LocationLabel = loc ?? "Main incomer",
                        Type          = 12,
                        IimpKa        = 12.5,
                        InKa          = 20.0,
                        UpKv          = 1.5,
                        Manufacturer  = "",
                        Model         = "",
                        StatusDot     = "•"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadSpds: {ex.Message}"); }
        }

        /// <summary>Called by SpdCoordinationCheckCommand to push status dots back.</summary>
        public void UpdateSpdStatus(IReadOnlyList<LpsComplianceItem> items)
        {
            try
            {
                Action act = () =>
                {
                    if (items == null) return;
                    bool anyFail = items.Any(i => i.Severity == LpsSeverity.Fail);
                    bool anyWarn = items.Any(i => i.Severity == LpsSeverity.Warn);
                    string dot = anyFail ? "✗" : anyWarn ? "⚠" : "✓";
                    foreach (var r in SpdRows) r.StatusDot = dot;
                    SpdGrid?.Items.Refresh();
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"UpdateSpdStatus: {ex.Message}"); }
        }

        /// <summary>Called by SpdRecommendCommand to add a recommended SPD row.</summary>
        public void AddSpdRow(SpdRowVm row)
        {
            try
            {
                Action act = () => { if (row != null) SpdRows.Add(row); };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"AddSpdRow: {ex.Message}"); }
        }

        /// <summary>Called by Reload catalogue — re-seed any registry-derived rows.</summary>
        public void RefreshAll()
        {
            // Risk factor defaults are seeded once at construction; nothing
            // else in the panel is registry-backed right now. Hook future
            // catalogue-driven seeding (e.g. SPD recommendations defaults)
            // here.
        }

        /// <summary>Push a workflow row to the RPRT tab.</summary>
        public void PushRunRow(string name, string statusDot)
        {
            try
            {
                Action act = () =>
                {
                    int next = WorkflowRows.Count + 1;
                    WorkflowRows.Insert(0, new LpsWorkflowRow
                    {
                        Number    = "#" + next,
                        Name      = name ?? "",
                        StatusDot = statusDot ?? "•",
                        Timestamp = DateTime.Now.ToString("HH:mm:ss")
                    });
                    while (WorkflowRows.Count > 100) WorkflowRows.RemoveAt(WorkflowRows.Count - 1);
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"PushRunRow: {ex.Message}"); }
        }
    }

    // ── View-model rows (POCOs) ─────────────────────────────────────────

    public class RiskFactorRow
    {
        public string Factor      { get; set; } = "";
        public string Description { get; set; } = "";
        public double Value       { get; set; }
    }

    public class AirTerminalRow
    {
        public bool   IsSelected { get; set; }
        public string Tag        { get; set; } = "";
        public double HeightM    { get; set; }
        public string Family     { get; set; } = "";
        public double RadiusM    { get; set; }
        public string StatusDot  { get; set; } = "•";
    }

    public class DownConductorRow
    {
        public bool   IsSelected     { get; set; }
        public string Tag            { get; set; } = "";
        public double LengthM        { get; set; }
        public string Material       { get; set; } = "COPPER";
        public double SpacingM       { get; set; }
        public double SeparationMm   { get; set; }
        public double CrossSectionMm2{ get; set; }
        public string StatusDot      { get; set; } = "•";
    }

    public class EarthElectrodeRow
    {
        public bool   IsSelected   { get; set; }
        public string Tag          { get; set; } = "";
        public string ArrangeType  { get; set; } = "A"; // A | B
        public double LengthM      { get; set; }
        public double ResistanceOhm{ get; set; }
        public string StatusDot    { get; set; } = "•";
    }

    public class BondingRow
    {
        public bool   IsSelected { get; set; }
        public string Tag        { get; set; } = "";
        public string FromLpz    { get; set; } = "";
        public string ToLpz      { get; set; } = "";
        public string BondType   { get; set; } = "DIRECT"; // DIRECT | SPD | ISOLATING_SPARK_GAP
        public string StatusDot  { get; set; } = "•";
    }

    public class SpdRowVm
    {
        public string Tag           { get; set; } = "";
        public string LocationId    { get; set; } = "";
        public string LocationLabel { get; set; } = "";
        public int    Type          { get; set; }
        public double IimpKa        { get; set; }
        public double InKa          { get; set; }
        public double UpKv          { get; set; }
        public string Manufacturer  { get; set; } = "";
        public string Model         { get; set; } = "";
        public double CableSeparationM { get; set; }
        public string StatusDot     { get; set; } = "•";
    }

    public class LpzZoneRow
    {
        public bool   IsSelected { get; set; }
        public string RoomName   { get; set; } = "";
        public string Level      { get; set; } = "";
        public string Lpz        { get; set; } = "LPZ1"; // LPZ0A | LPZ0B | LPZ1 | LPZ2 | LPZ3
        public string Colour     { get; set; } = "";
    }

    public class InspectionRow
    {
        public string Tag      { get; set; } = "";
        public string LastTest { get; set; } = "";
        public string NextDue  { get; set; } = "";
        public string Status   { get; set; } = "";
    }

    public class LpsWorkflowRow
    {
        public string Number    { get; set; } = "";
        public string Name      { get; set; } = "";
        public string StatusDot { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public class ResidualRiskRow
    {
        public string Class       { get; set; } = "";
        public string Residual    { get; set; } = "";
        public string StatusDot   { get; set; } = "";
        public string Recommended { get; set; } = "";
    }

    public class LossTypeRow
    {
        public string LossType  { get; set; } = "";   // L1 / L2 / L3 / L4
        public string Risk      { get; set; } = "";
        public string Tolerable { get; set; } = "";
        public string StatusDot { get; set; } = "";
    }
}
