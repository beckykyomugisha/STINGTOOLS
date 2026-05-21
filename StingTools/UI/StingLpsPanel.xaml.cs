using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core;
using StingTools.Core.Lightning;

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

            // Seed default risk factor rows so the RISK tab is non-empty.
            try { SeedDefaultRiskFactors(); }
            catch (Exception ex) { StingLog.Warn($"SeedDefaultRiskFactors: {ex.Message}"); }
        }

        private void SeedDefaultRiskFactors()
        {
            RiskFactorRows.Clear();
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cb — Building type",     Description = "Construction (metal=0.5 · concrete=1.0 · masonry=1.0 · thatched=2.0)",       Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cc — Internal content",  Description = "Contents (ordinary=1.0 · valuable=2.0 · cultural=3.0 · explosive=5.0)",       Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cd — Occupant hazard",   Description = "Occupants (low=1.0 · medium=2.0 · high panic=5.0 · hospital=10.0)",            Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Ce — Consequence",       Description = "Loss (no impact=1.0 · service loss=2.0 · cultural loss=5.0 · social=10.0)",   Value = 1.0 });
            RiskFactorRows.Add(new RiskFactorRow { Factor = "Cd_loc — Location",      Description = "Location (isolated=2.0 · low hill=1.0 · surrounded by taller=0.25 · in city=0.5)", Value = 1.0 });
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
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"SetRiskResult: {ex.Message}"); }
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
}
