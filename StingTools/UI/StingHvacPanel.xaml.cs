using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING HVAC Center dockable panel.
    /// 7 tabs: EQPT · SYS · CALCS · DUCT · LOADS · FAB · RPRT.
    /// Every button click dispatches via <see cref="StingHvacCommandHandler"/>
    /// so Revit API calls run on the Revit API thread.
    ///
    /// Mirrors the StingElectricalPanel / StingPlumbingPanel architecture:
    ///   - ObservableCollection per data-grid
    ///   - Header dropdowns drive a shared "current state" snapshot consumed
    ///     by the command handler
    ///   - Singleton instance exposed via <see cref="Instance"/> so command
    ///     classes can refresh grids after a run
    /// </summary>
    public partial class StingHvacPanel : Page
    {
        // ── Grid view-models ────────────────────────────────────────────
        public ObservableCollection<HvacEquipmentRow>  EquipmentRows  { get; } = new();
        public ObservableCollection<HvacSystemRow>     SystemRows     { get; } = new();
        public ObservableCollection<SizingRoleRow>     SizingRoleRows { get; } = new();
        public ObservableCollection<HvacIssueRow>      IssueRows      { get; } = new();
        public ObservableCollection<HvacDuctTypeRow>   DuctTypeRows   { get; } = new();
        public ObservableCollection<HvacStandardSizeRow> StandardSizeRows { get; } = new();
        public ObservableCollection<HvacSpaceLoadRow>  SpaceLoadRows  { get; } = new();
        public ObservableCollection<HvacSpoolRow>      SpoolRows      { get; } = new();
        public ObservableCollection<HvacDriftRow>      DriftRows      { get; } = new();
        public ObservableCollection<HvacWorkflowRunRow> WorkflowRows  { get; } = new();

        // ── Header state ────────────────────────────────────────────────
        public string SelectedStandard  { get; private set; } = "CIBSE";
        public string SelectedPressure  { get; private set; } = "low";
        public string SelectedRegion    { get; private set; } = "UK_SI";
        public double SelectedAirDensity { get; private set; } = 1.20;
        public string SelectedStrategy  { get; private set; } = "velocity";
        public string SelectedScope     { get; private set; } = "ActiveView";

        private static StingHvacPanel _instance;
        public static StingHvacPanel Instance => _instance;

        public StingHvacPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }

            _instance = this;

            // Bind grids
            try { EquipmentGrid.ItemsSource    = EquipmentRows; }    catch (Exception ex) { StingLog.Warn($"EquipmentGrid bind: {ex.Message}"); }
            try { SystemGrid.ItemsSource       = SystemRows; }       catch (Exception ex) { StingLog.Warn($"SystemGrid bind: {ex.Message}"); }
            try { SizingRoleGrid.ItemsSource   = SizingRoleRows; }   catch (Exception ex) { StingLog.Warn($"SizingRoleGrid bind: {ex.Message}"); }
            try { IssueGrid.ItemsSource        = IssueRows; }        catch (Exception ex) { StingLog.Warn($"IssueGrid bind: {ex.Message}"); }
            try { DuctTypeGrid.ItemsSource     = DuctTypeRows; }     catch (Exception ex) { StingLog.Warn($"DuctTypeGrid bind: {ex.Message}"); }
            try { StandardSizeGrid.ItemsSource = StandardSizeRows; } catch (Exception ex) { StingLog.Warn($"StandardSizeGrid bind: {ex.Message}"); }
            try { SpaceLoadGrid.ItemsSource    = SpaceLoadRows; }    catch (Exception ex) { StingLog.Warn($"SpaceLoadGrid bind: {ex.Message}"); }
            try { SpoolGrid.ItemsSource        = SpoolRows; }        catch (Exception ex) { StingLog.Warn($"SpoolGrid bind: {ex.Message}"); }
            try { DriftGrid.ItemsSource        = DriftRows; }        catch (Exception ex) { StingLog.Warn($"DriftGrid bind: {ex.Message}"); }
            try { WorkflowGrid.ItemsSource     = WorkflowRows; }     catch (Exception ex) { StingLog.Warn($"WorkflowGrid bind: {ex.Message}"); }

            // Seed sizing-role rows from the registry on first show so the CALCS tab is non-empty.
            try { SeedSizingRolesFromRegistry(); }
            catch (Exception ex) { StingLog.Warn($"SeedSizingRoles: {ex.Message}"); }
        }

        private void SeedSizingRolesFromRegistry()
        {
            // Load JSON via the registry so the panel reflects whatever ships with the build.
            // Document-aware load happens later when a project is open; here we use the
            // baseline only.
            try
            {
                var rules = StingTools.Core.Mep.MepSizingRegistry.Get(null);
                SizingRoleRows.Clear();
                foreach (var r in rules.DuctRoles)
                {
                    SizingRoleRows.Add(new SizingRoleRow
                    {
                        Role           = r.Label,
                        MaxVelocityMs  = r.MaxVelocityMs,
                        FrictionPaPerM = r.MaxFrictionPaPerM,
                        AspectMax      = r.AspectMax,
                        Source         = r.Source
                    });
                }
                foreach (var size in rules.DuctSizesForRegion(rules.DuctDefaultRegion))
                {
                    StandardSizeRows.Add(new HvacStandardSizeRow { IsEnabled = true, SizeMm = size });
                }
            }
            catch (Exception ex) { StingLog.Warn($"Registry seed failed: {ex.Message}"); }
        }

        // ── Click dispatch ──────────────────────────────────────────────
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                PushSnapshotToHandler();
                StingHvacCommandHandler.Instance?.SetCommand(tag);
                UpdateStatus($"Running: {tag}");
            }
        }

        private void PushSnapshotToHandler()
        {
            try
            {
                StingHvacCommandHandler.CurrentRegion           = SelectedRegion;
                StingHvacCommandHandler.CurrentStandard         = SelectedStandard;
                StingHvacCommandHandler.CurrentPressureClassId  = SelectedPressure;
                StingHvacCommandHandler.CurrentAirDensityKgM3   = SelectedAirDensity;
                StingHvacCommandHandler.CurrentSizingStrategyId = SelectedStrategy;
                StingHvacCommandHandler.CurrentScope            = SelectedScope;
            }
            catch (Exception ex) { StingLog.Warn($"PushSnapshot: {ex.Message}"); }
        }

        private void UpdateStatus(string text)
        {
            try { if (txtHvacStatus != null) txtHvacStatus.Text = text; }
            catch (Exception ex) { StingLog.Warn($"Status update: {ex.Message}"); }
        }

        // ── Header combo handlers ───────────────────────────────────────
        private void cmbStandard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStandard?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedStandard = tag;
        }

        private void cmbPressure_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPressure?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedPressure = tag;
        }

        private void cmbRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRegion?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedRegion = tag;
        }

        private void cmbDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDensity?.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                SelectedAirDensity = v;
            }
        }

        private void Strategy_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedStrategy = tag;
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedScope = tag;
        }
    }

    // ── View-model rows (POCOs) ─────────────────────────────────────────

    public class HvacEquipmentRow
    {
        public bool   IsSelected   { get; set; }
        public string Tag          { get; set; } = "";
        public string Type         { get; set; } = "";
        public double CapacityKw   { get; set; }
        public double FlowLs       { get; set; }
        public string System       { get; set; } = "";
        public string StatusDot    { get; set; } = "•";   // ⬤ ⬡ ✗
        public string Manufacturer { get; set; } = "";
        public string Model        { get; set; } = "";
    }

    public class HvacSystemRow
    {
        public bool   IsSelected { get; set; }
        public string Name       { get; set; } = "";
        public string Class      { get; set; } = "";
        public string Equipment  { get; set; } = "";
        public double FlowLs     { get; set; }
        public double DropPa     { get; set; }
        public int    Nc         { get; set; }
        public string StatusDot  { get; set; } = "•";
    }

    public class SizingRoleRow
    {
        public string Role           { get; set; } = "";
        public double MaxVelocityMs  { get; set; }
        public double FrictionPaPerM { get; set; }
        public double AspectMax      { get; set; }
        public string Source         { get; set; } = "";
    }

    public class HvacIssueRow
    {
        public bool   IsSelected { get; set; }
        public string Severity   { get; set; } = "";   // ⚠ / ✖
        public string Element    { get; set; } = "";
        public string Issue      { get; set; } = "";
        public string Suggestion { get; set; } = "";
    }

    public class HvacDuctTypeRow
    {
        public bool   IsSelected    { get; set; }
        public string Name          { get; set; } = "";
        public string Shape         { get; set; } = "";
        public string Material      { get; set; } = "";
        public string PressureClass { get; set; } = "";
    }

    public class HvacStandardSizeRow
    {
        public bool   IsEnabled { get; set; } = true;
        public double SizeMm    { get; set; }
    }

    public class HvacSpaceLoadRow
    {
        public bool   IsSelected { get; set; }
        public string SpaceName  { get; set; } = "";
        public string SpaceType  { get; set; } = "";
        public double AreaM2     { get; set; }
        public int    People     { get; set; }
        public double HeatingKw  { get; set; }
        public double CoolingKw  { get; set; }
        public double OAls       { get; set; }
        public string Warning    { get; set; } = "";
    }

    public class HvacSpoolRow
    {
        public bool   IsSelected   { get; set; }
        public string Tag          { get; set; } = "";
        public string System       { get; set; } = "";
        public double LengthM      { get; set; }
        public double WeightKg     { get; set; }
        public int    FittingCount { get; set; }
        public string ShopReady    { get; set; } = "";   // ✔ / ✗
    }

    public class HvacDriftRow
    {
        public string Severity { get; set; } = "";
        public string Element  { get; set; } = "";
        public string WasNow   { get; set; } = "";
        public string Reason   { get; set; } = "";
    }

    public class HvacWorkflowRunRow
    {
        public string Number    { get; set; } = "";
        public string Name      { get; set; } = "";
        public string StatusDot { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }
}
