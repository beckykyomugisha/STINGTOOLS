// StingPlumbingPanel — code-behind for the STING Plumbing Center dockable
// panel. Electrical-grade rebuild: rich UI (project context strip, 8 tabs
// with DataGrids + Expanders + ComboBoxes + Search filters) modelled on
// StingElectricalPanel. The visual tree lives in StingPlumbingPanel.xaml;
// every Button.Click here dispatches via StingPlumbingCommandHandler so
// Revit API calls run on the API thread.
//
// All ~50 command tags from the previous button-list panel are preserved
// verbatim so StingPlumbingCommandHandler's switch keeps routing without
// any changes.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using StingTools.Core;
using Button       = System.Windows.Controls.Button;
using ComboBox     = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBox      = System.Windows.Controls.TextBox;

namespace StingTools.UI.Plumbing
{
    /// <summary>
    /// Code-behind for the STING Plumbing Center dockable panel.
    /// Mirrors the <see cref="StingTools.UI.StingElectricalPanel"/> architecture:
    /// project context strip + 8 tabs (SYSTEM / SUPPLY / DRAINAGE / ROUTE /
    /// STORM / SPECIALTY / AUDIT / DOCS) populated by ObservableCollection&lt;T&gt;
    /// snapshots pushed back from commands.
    /// </summary>
    public partial class StingPlumbingPanel : Page
    {
        public ObservableCollection<PlumbFixtureRow>   FixtureRows  { get; }
            = new ObservableCollection<PlumbFixtureRow>();
        public ObservableCollection<PlumbPrvRow>       PrvRows      { get; }
            = new ObservableCollection<PlumbPrvRow>();
        public ObservableCollection<PlumbTmvRow>       TmvRows      { get; }
            = new ObservableCollection<PlumbTmvRow>();
        public ObservableCollection<PlumbDeadLegRow>   DeadLegRows  { get; }
            = new ObservableCollection<PlumbDeadLegRow>();
        public ObservableCollection<PlumbPipeRow>      DrainagePipes { get; }
            = new ObservableCollection<PlumbPipeRow>();
        public ObservableCollection<PlumbStackRow>     StackRows    { get; }
            = new ObservableCollection<PlumbStackRow>();
        public ObservableCollection<PlumbBackflowRow>  BackflowRows { get; }
            = new ObservableCollection<PlumbBackflowRow>();
        public ObservableCollection<PlumbAuditFinding> AuditFindings { get; }
            = new ObservableCollection<PlumbAuditFinding>();
        public ObservableCollection<PlumbManholeRow>   ManholeRows  { get; }
            = new ObservableCollection<PlumbManholeRow>();
        public ObservableCollection<PlumbBoqRow>       BoqRows      { get; }
            = new ObservableCollection<PlumbBoqRow>();

        public string SelectedStandard { get; private set; } = "BS_EN_12056";
        public string SelectedBuilding { get; private set; } = "Office";
        public string SelectedRegime   { get; private set; } = "DCW_DHW_Recirc";

        private static StingPlumbingPanel _instance;
        public static StingPlumbingPanel Instance => _instance;

        public StingPlumbingPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme non-fatal */ }
            _instance = this;

            FixtureGrid.ItemsSource        = FixtureRows;
            PrvGrid.ItemsSource            = PrvRows;
            TmvGrid.ItemsSource            = TmvRows;
            DeadLegGrid.ItemsSource        = DeadLegRows;
            DrainagePipeGrid.ItemsSource   = DrainagePipes;
            StackGrid.ItemsSource          = StackRows;
            BackflowGrid.ItemsSource       = BackflowRows;
            AuditFindingsGrid.ItemsSource  = AuditFindings;
            ManholeGrid.ItemsSource        = ManholeRows;
            BoqGrid.ItemsSource            = BoqRows;

            // Seed the SYSTEM-tab inline form with defaults so dropdowns are
            // populated immediately; PlumbLoadSystemConfigCommand pushes the
            // real on-disk values back via LoadSystemForm when invoked.
            try { LoadSystemForm(StingTools.Core.Plumbing.PlumbingSystemConfig.Defaults()); }
            catch (Exception ex) { StingLog.Warn($"ctor LoadSystemForm: {ex.Message}"); }

            UpdateStatus("Ready");
        }

        public void UpdateStatus(string text)
        {
            try { Dispatcher.Invoke(() => txtPlumbStatus.Text = text ?? ""); }
            catch (Exception ex) { StingLog.Warn($"Plumbing UpdateStatus: {ex.Message}"); }
        }

        public void SetStatus(string text)
        {
            try { Dispatcher.Invoke(() => txtPlumbFooter.Text = text ?? ""); }
            catch (Exception ex) { StingLog.Warn($"Plumbing SetStatus: {ex.Message}"); }
        }

        // ── Unified click dispatcher — every Button.Click in the XAML hits
        // this method, which forwards via StingPlumbingCommandHandler so the
        // command runs on the Revit API thread.
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button btn) || !(btn.Tag is string tag) || string.IsNullOrEmpty(tag))
                    return;
                StingPlumbingCommandHandler.Instance?.SetCommand(tag);
                StingPlumbingCommandHandler.Event?.Raise();
                UpdateStatus($"Running: {tag}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("STING Plumbing dispatch error: " + ex.Message);
                StingLog.Error("Plumbing Cmd_Click", ex);
            }
        }

        // ── Project context combo handlers ───────────────────────────────
        private void cmbStandard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStandard?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedStandard = tag;
        }

        private void cmbBuilding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbBuilding?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedBuilding = tag;
        }

        private void cmbRegime_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRegime?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedRegime = tag;
        }

        // ── Search filter ────────────────────────────────────────────────
        private void txtFixtureFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string f = (txtFixtureFilter?.Text ?? "").Trim().ToLowerInvariant();
            foreach (var row in FixtureRows)
                row.IsHidden = !string.IsNullOrEmpty(f) &&
                               (row.TypeName ?? "").ToLowerInvariant().IndexOf(f) < 0;
        }

        // ── Snapshot push — commands hand back data via this single entry
        // point; the panel marshals onto the WPF dispatcher and refreshes
        // every bound grid + label. Mirrors StingElectricalPanel.RefreshFromData.
        public void RefreshFromData(PlumbingPanelSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (snapshot.Fixtures != null)       PopulateFixtures(snapshot.Fixtures);
                    if (snapshot.Prvs != null)           PopulatePrvs(snapshot.Prvs);
                    if (snapshot.Tmvs != null)           PopulateTmvs(snapshot.Tmvs);
                    if (snapshot.DeadLegs != null)       PopulateDeadLegs(snapshot.DeadLegs);
                    if (snapshot.DrainagePipes != null)  PopulateDrainagePipes(snapshot.DrainagePipes);
                    if (snapshot.Stacks != null)         PopulateStacks(snapshot.Stacks);
                    if (snapshot.Backflow != null)       PopulateBackflow(snapshot.Backflow);
                    if (snapshot.Findings != null)       PopulateFindings(snapshot.Findings);
                    if (snapshot.Manholes != null)       PopulateManholes(snapshot.Manholes);
                    if (snapshot.Boq != null)            PopulateBoq(snapshot.Boq);
                    if (snapshot.ConfigSummary != null)  PopulateConfigSummary(snapshot.ConfigSummary);
                    if (snapshot.Rag != null)            PopulateRag(snapshot.Rag);
                });
            }
            catch (Exception ex) { StingLog.Warn($"Plumbing RefreshFromData: {ex.Message}"); }
        }

        // ── Populate helpers ─────────────────────────────────────────────
        private void PopulateFixtures(IEnumerable<PlumbFixtureRow> rows)
        {
            FixtureRows.Clear();
            int types = 0, qty = 0;
            double duTotal = 0, luTotal = 0;
            foreach (var r in rows)
            {
                FixtureRows.Add(r);
                types++;
                qty     += r.Qty;
                duTotal += r.Du * r.Qty;
                luTotal += (r.LuCw + r.LuHw) * r.Qty;
            }
            txtFixtureSummary.Text =
                $"{types} fixture types · {qty} fixtures · {duTotal:0.#} DU total · {luTotal:0.#} LU total";
        }

        private void PopulatePrvs(IEnumerable<PlumbPrvRow> rows)
        {
            PrvRows.Clear();
            foreach (var r in rows) PrvRows.Add(r);
        }

        private void PopulateTmvs(IEnumerable<PlumbTmvRow> rows)
        {
            TmvRows.Clear();
            foreach (var r in rows) TmvRows.Add(r);
        }

        private void PopulateDeadLegs(IEnumerable<PlumbDeadLegRow> rows)
        {
            DeadLegRows.Clear();
            foreach (var r in rows) DeadLegRows.Add(r);
        }

        private void PopulateDrainagePipes(IEnumerable<PlumbPipeRow> rows)
        {
            DrainagePipes.Clear();
            foreach (var r in rows) DrainagePipes.Add(r);
        }

        private void PopulateStacks(IEnumerable<PlumbStackRow> rows)
        {
            StackRows.Clear();
            foreach (var r in rows) StackRows.Add(r);
        }

        private void PopulateBackflow(IEnumerable<PlumbBackflowRow> rows)
        {
            BackflowRows.Clear();
            foreach (var r in rows) BackflowRows.Add(r);
        }

        private void PopulateFindings(IEnumerable<PlumbAuditFinding> rows)
        {
            AuditFindings.Clear();
            foreach (var r in rows) AuditFindings.Add(r);
        }

        private void PopulateManholes(IEnumerable<PlumbManholeRow> rows)
        {
            ManholeRows.Clear();
            foreach (var r in rows) ManholeRows.Add(r);
        }

        private void PopulateBoq(IEnumerable<PlumbBoqRow> rows)
        {
            BoqRows.Clear();
            foreach (var r in rows) BoqRows.Add(r);
        }

        // PopulateConfigSummary is retained for snapshot-pushed values (the
        // SYSTEM tab is now an editable inline form, so we route the snapshot
        // into the same controls — first selected item / textbox values).
        private void PopulateConfigSummary(PlumbConfigSummary cfg)
        {
            try
            {
                if (cfg == null) return;
                if (!string.IsNullOrEmpty(cfg.DrainageStandard))
                    SetComboByText(cmbCfgDrainStd, cfg.DrainageStandard);
                if (!string.IsNullOrEmpty(cfg.SupplyStandard))
                    SetComboByText(cmbCfgSupplyStd, cfg.SupplyStandard);
                if (!string.IsNullOrEmpty(cfg.KFactorText))
                    txtCfgKFactor.Text = cfg.KFactorText;
                if (!string.IsNullOrEmpty(cfg.MaterialDcw))
                    SetComboByText(cmbCfgMatDCW, cfg.MaterialDcw);
                if (!string.IsNullOrEmpty(cfg.MaterialDhw))
                    SetComboByText(cmbCfgMatDHW, cfg.MaterialDhw);
            }
            catch (Exception ex) { StingLog.Warn($"PopulateConfigSummary: {ex.Message}"); }
        }

        /// <summary>
        /// Populate the SYSTEM-tab inline form from a PlumbingSystemConfig.
        /// Idempotent — called on panel load and after every save / reload.
        /// </summary>
        public void LoadSystemForm(StingTools.Core.Plumbing.PlumbingSystemConfig cfg)
        {
            try
            {
                if (cfg == null) cfg = StingTools.Core.Plumbing.PlumbingSystemConfig.Defaults();

                // First call seeds the dropdown lists from PlumbingTables.
                EnsureSystemFormSeeded();

                SetComboByText(cmbCfgBldgType,  cfg.BuildingType);
                SetComboByText(cmbCfgDrainStd,  cfg.DrainStandard);
                SetComboByText(cmbCfgSupplyStd, cfg.SupplyStandard);
                txtCfgKFactor.Text       = cfg.KFactor.ToString("F2");
                chkCfgFlushValve.IsChecked = cfg.FlushValveMajority;
                txtCfgOccupancy.Text     = cfg.OccupancyCount.ToString();
                txtCfgBeds.Text          = cfg.BedsOrWorkstations.ToString();

                SetComboByText(cmbCfgMatDCW,      cfg.MaterialFor("DCW"));
                SetComboByText(cmbCfgMatDHW,      cfg.MaterialFor("DHW"));
                SetComboByText(cmbCfgMatDrainage, cfg.MaterialFor("Drainage"));
                SetComboByText(cmbCfgMatStorm,    cfg.MaterialFor("Storm"));
                SetComboByText(cmbCfgMatVent,     cfg.MaterialFor("Vent"));

                txtCfgVelDCWMax.Text   = cfg.VelocityMaxFor("DCW_Max").ToString("F2");
                txtCfgVelDHWMax.Text   = cfg.VelocityMaxFor("DHW_Max").ToString("F2");
                txtCfgVelDrnSelf.Text  = cfg.VelocityMaxFor("Drain_SelfCleansing").ToString("F2");
                txtCfgVelDrnMax.Text   = cfg.VelocityMaxFor("Drain_Max").ToString("F2");

                txtCfgSlopeDn3250.Text  = SlopeForKey(cfg, "DN32_50",  2.0).ToString("F2");
                txtCfgSlopeDn75100.Text = SlopeForKey(cfg, "DN75_100", 1.0).ToString("F2");
                txtCfgSlopeDn150.Text   = SlopeForKey(cfg, "DN150",    0.67).ToString("F2");
                txtCfgSlopeTarget.Text  = SlopeForKey(cfg, "Target",   1.25).ToString("F2");

                txtCfgSupplyPres.Text   = cfg.SupplyPressureBarAtEntry.ToString("F2");
                txtCfgMaxDpPaPerM.Text  = cfg.MaxPressureDropPaPerM.ToString("F0");
                SetComboByText(cmbCfgHeadLossMethod,
                    string.IsNullOrEmpty(cfg.HeadLossMethod) ? "HAZEN-WILLIAMS" : cfg.HeadLossMethod);
                SetComboByText(cmbCfgSizingStrategy,
                    string.IsNullOrEmpty(cfg.SupplySizingStrategy) ? "VELOCITY" : cfg.SupplySizingStrategy);
                SetComboByText(cmbCfgDxfAcad,
                    string.IsNullOrEmpty(cfg.DxfAutoCadVersion) ? "R2010" : cfg.DxfAutoCadVersion);
            }
            catch (Exception ex) { StingLog.Warn($"LoadSystemForm: {ex.Message}"); }
        }

        /// <summary>
        /// Read every SYSTEM-tab inline control and produce a PlumbingSystemConfig.
        /// Falls back to <paramref name="baseCfg"/> for fields the form doesn't expose.
        /// </summary>
        public StingTools.Core.Plumbing.PlumbingSystemConfig ReadSystemForm(
            StingTools.Core.Plumbing.PlumbingSystemConfig baseCfg)
        {
            var cfg = baseCfg ?? StingTools.Core.Plumbing.PlumbingSystemConfig.Defaults();
            try
            {
                cfg.BuildingType        = (cmbCfgBldgType?.SelectedItem  as string) ?? cfg.BuildingType;
                cfg.DrainStandard       = (cmbCfgDrainStd?.SelectedItem  as string) ?? cfg.DrainStandard;
                cfg.SupplyStandard      = (cmbCfgSupplyStd?.SelectedItem as string) ?? cfg.SupplyStandard;
                cfg.FlushValveMajority  = chkCfgFlushValve.IsChecked == true;
                if (double.TryParse(txtCfgKFactor.Text,    out var k))  cfg.KFactor = k;
                if (int.TryParse(txtCfgOccupancy.Text,     out var oc)) cfg.OccupancyCount = oc;
                if (int.TryParse(txtCfgBeds.Text,          out var bd)) cfg.BedsOrWorkstations = bd;
                if (double.TryParse(txtCfgSupplyPres.Text, out var sp)) cfg.SupplyPressureBarAtEntry = sp;
                if (double.TryParse(txtCfgMaxDpPaPerM.Text, out var dp)) cfg.MaxPressureDropPaPerM = dp;

                cfg.HeadLossMethod       = (cmbCfgHeadLossMethod?.SelectedItem as string) ?? cfg.HeadLossMethod;
                cfg.SupplySizingStrategy = (cmbCfgSizingStrategy?.SelectedItem as string) ?? cfg.SupplySizingStrategy;
                cfg.DxfAutoCadVersion    = (cmbCfgDxfAcad?.SelectedItem as string)        ?? cfg.DxfAutoCadVersion;

                cfg.Materials = new Dictionary<string, string>
                {
                    { "DCW",      (cmbCfgMatDCW?.SelectedItem as string) ?? cfg.MaterialFor("DCW") },
                    { "DHW",      (cmbCfgMatDHW?.SelectedItem as string) ?? cfg.MaterialFor("DHW") },
                    { "Drainage", (cmbCfgMatDrainage?.SelectedItem as string) ?? cfg.MaterialFor("Drainage") },
                    { "Storm",    (cmbCfgMatStorm?.SelectedItem as string) ?? cfg.MaterialFor("Storm") },
                    { "Vent",     (cmbCfgMatVent?.SelectedItem as string) ?? cfg.MaterialFor("Vent") },
                };

                cfg.VelocityMps = new Dictionary<string, double>
                {
                    { "DCW_Max",             ParseDoubleOr(txtCfgVelDCWMax.Text, 2.0) },
                    { "DHW_Max",             ParseDoubleOr(txtCfgVelDHWMax.Text, 1.5) },
                    { "Drain_SelfCleansing", ParseDoubleOr(txtCfgVelDrnSelf.Text, 0.7) },
                    { "Drain_Max",           ParseDoubleOr(txtCfgVelDrnMax.Text, 3.5) },
                };

                cfg.SlopePctMin = new Dictionary<string, double>
                {
                    { "DN32_50",  ParseDoubleOr(txtCfgSlopeDn3250.Text,  2.0) },
                    { "DN75_100", ParseDoubleOr(txtCfgSlopeDn75100.Text, 1.0) },
                    { "DN150",    ParseDoubleOr(txtCfgSlopeDn150.Text,   0.67) },
                    { "Target",   ParseDoubleOr(txtCfgSlopeTarget.Text,  1.25) },
                };
            }
            catch (Exception ex) { StingLog.Warn($"ReadSystemForm: {ex.Message}"); }
            return cfg;
        }

        private bool _systemFormSeeded;
        private void EnsureSystemFormSeeded()
        {
            if (_systemFormSeeded) return;
            _systemFormSeeded = true;

            FillCombo(cmbCfgBldgType,  new[] { "Dwelling", "Office", "Hospital", "School",
                "Hotel", "Restaurant", "Factory", "Sports", "PublicWC", "Custom" });
            FillCombo(cmbCfgDrainStd,  new[] { "BS-EN-12056", "IPC-2021", "MANUAL" });
            FillCombo(cmbCfgSupplyStd, new[] { "BS-EN-806", "HUNTER-WSFU", "MANUAL" });

            // Materials are loaded from STING_PIPE_MATERIALS_HYDRAULIC.json so
            // adding PPR / PB / CPVC etc. flows through without panel edits.
            var matKeys = StingTools.Core.Plumbing.PlumbingTables.Materials?
                .Select(m => m.Key).Distinct().ToArray()
                ?? new[] { "COPPER_R250", "UPVC_DRAIN", "MDPE_BLUE", "PEX_AL_PEX" };
            foreach (var combo in new[] { cmbCfgMatDCW, cmbCfgMatDHW,
                cmbCfgMatDrainage, cmbCfgMatStorm, cmbCfgMatVent })
                FillCombo(combo, matKeys);

            FillCombo(cmbCfgHeadLossMethod, new[] { "HAZEN-WILLIAMS", "DARCY-WEISBACH" });
            FillCombo(cmbCfgSizingStrategy, new[] { "VELOCITY", "HEAD-LOSS" });
            FillCombo(cmbCfgDxfAcad,        new[] { "R2007", "R2010", "R2013", "R2018", "DEFAULT" });
        }

        private void cmbCfgBldgType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbCfgBldgType?.SelectedItem is string bt && txtCfgKFactor != null)
                {
                    var def = StingTools.Core.Plumbing.PlumbingSystemConfig.DefaultsForBuildingType(bt);
                    txtCfgKFactor.Text = def.KFactor.ToString("F2");
                }
            }
            catch (Exception ex) { StingLog.Warn($"cmbCfgBldgType: {ex.Message}"); }
        }

        // Reload routes through the existing Plumb_LoadSystemConfig command so
        // the disk read happens on the Revit API thread; the command's panel
        // hook then pushes values back into the form via LoadSystemForm.
        private void btnCfgReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StingPlumbingCommandHandler.Instance?.SetCommand("Plumb_LoadSystemConfig");
                StingPlumbingCommandHandler.Event?.Raise();
                UpdateStatus("Reloading system config…");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"btnCfgReload: {ex.Message}");
                UpdateStatus("Reload failed — see log.");
            }
        }

        // ── form helpers ────────────────────────────────────────────────
        private static void FillCombo(ComboBox c, IEnumerable<string> items)
        {
            if (c == null) return;
            c.Items.Clear();
            foreach (var i in items) c.Items.Add(i);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
        }

        private static void SetComboByText(ComboBox c, string value)
        {
            if (c == null || string.IsNullOrEmpty(value)) return;
            if (!c.Items.Contains(value)) c.Items.Add(value);
            c.SelectedItem = value;
        }

        private static double ParseDoubleOr(string s, double fallback)
            => double.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) ? d : fallback;

        private static double SlopeForKey(
            StingTools.Core.Plumbing.PlumbingSystemConfig cfg, string key, double fallback)
            => (cfg?.SlopePctMin != null && cfg.SlopePctMin.TryGetValue(key, out var v)) ? v : fallback;

        private void PopulateRag(PlumbRagSummary rag)
        {
            txtRagSupplyR.Text = rag.SupplyRed.ToString();
            txtRagSupplyA.Text = rag.SupplyAmber.ToString();
            txtRagSupplyG.Text = rag.SupplyGreen.ToString();
            txtRagDrainR.Text  = rag.DrainageRed.ToString();
            txtRagDrainA.Text  = rag.DrainageAmber.ToString();
            txtRagDrainG.Text  = rag.DrainageGreen.ToString();
            txtRagVentsR.Text  = rag.VentsRed.ToString();
            txtRagVentsA.Text  = rag.VentsAmber.ToString();
            txtRagVentsG.Text  = rag.VentsGreen.ToString();
            txtRagBfR.Text     = rag.BackflowRed.ToString();
            txtRagBfA.Text     = rag.BackflowAmber.ToString();
            txtRagBfG.Text     = rag.BackflowGreen.ToString();
            txtRagHtmR.Text    = rag.HtmRed.ToString();
            txtRagHtmA.Text    = rag.HtmAmber.ToString();
            txtRagHtmG.Text    = rag.HtmGreen.ToString();
        }

        // ── Input snapshot collectors — commands call into these to read
        // the live form state without having to know about WPF.
        public PlumbSizingInputs ReadSizingInputs() => new PlumbSizingInputs
        {
            Standard       = SelectedStandard,
            Building       = SelectedBuilding,
            Regime         = SelectedRegime,
            VelocityMaxMps = ParseDouble(txtSupVelocity?.Text, 2.0),
            SupplyMethod   = ((cmbSupMethod?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Hunter",
            MinPressureKpa = ParseDouble(txtSupMinPressure?.Text, 100),
            MaxPressureKpa = ParseDouble(txtSupMaxPressure?.Text, 500),
            DrnMinSlopePct = ParseDouble(txtDrnMinSlope?.Text, 1.0),
            DrnMaxSlopePct = ParseDouble(txtDrnMaxSlope?.Text, 6.0),
            DrnSelfCleanseMps = ParseDouble(txtDrnVMin?.Text, 0.7),
            DrnRoughnessMm = ParseDouble(txtDrnRoughness?.Text, 0.6),
        };

        public PlumbRoutingInputs ReadRoutingInputs() => new PlumbRoutingInputs
        {
            MaxSearchRadiusMm = ParseDouble(txtRtRadius?.Text, 3000),
            SnapToCorridor    = chkRtSnap?.IsChecked == true,
            PreviewBeforeCommit = chkRtPreview?.IsChecked == true,
        };

        public PlumbStormInputs ReadStormInputs() => new PlumbStormInputs
        {
            RoofAreaM2        = ParseDouble(txtRoofArea?.Text, 1000),
            RunoffCoefficient = ParseDouble(txtRunoff?.Text, 1.0),
            RainIntensityMmH  = ParseDouble(txtRainIntensity?.Text, 75),
            ClimateUpliftPct  = ParseDouble(txtClimateUplift?.Text, 20),
        };

        // ── Tiny utils ───────────────────────────────────────────────────
        private static double ParseDouble(string s, double fallback)
            => double.TryParse((s ?? "").Trim(), out double v) ? v : fallback;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  View-models + snapshot DTOs — populated by command handlers and
    //  pushed back through StingPlumbingPanel.RefreshFromData.
    // ═════════════════════════════════════════════════════════════════════

    public class PlumbNotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class PlumbFixtureRow : PlumbNotifyBase
    {
        public bool   IsHidden { get; set; }
        public string TypeName { get; set; }
        public int    Qty      { get; set; }
        public double Du       { get; set; }
        public double LuCw     { get; set; }
        public double LuHw     { get; set; }
        public double Wsfu     { get; set; }
        public string DuDisplay   => Du   > 0 ? $"{Du:0.#}"   : "—";
        public string LuCwDisplay => LuCw > 0 ? $"{LuCw:0.#}" : "—";
        public string LuHwDisplay => LuHw > 0 ? $"{LuHw:0.#}" : "—";
        public string WsfuDisplay => Wsfu > 0 ? $"{Wsfu:0.#}" : "—";
    }

    public class PlumbPrvRow
    {
        public string Tag      { get; set; }
        public string Location { get; set; }
        public string SetKpa   { get; set; }
        public string InletKpa { get; set; }
        public string Status   { get; set; }
    }

    public class PlumbTmvRow
    {
        public string Tag      { get; set; }
        public string Class    { get; set; }
        public string SetC     { get; set; }
        public string Location { get; set; }
        public string Status   { get; set; }
    }

    public class PlumbDeadLegRow
    {
        public string PipeId  { get; set; }
        public string Service { get; set; }
        public double LengthM { get; set; }
        public double LOverD  { get; set; }
        public string Verdict { get; set; }
        public string LengthDisplay => $"{LengthM:0.0}";
        public string LdDisplay     => $"{LOverD:0.#}";
    }

    public class PlumbPipeRow
    {
        public string PipeId   { get; set; }
        public double Du       { get; set; }
        public int    Dn       { get; set; }
        public double SlopePct { get; set; }
        public double Velocity { get; set; }
        public string Status   { get; set; }
        public string DuDisplay       => Du   > 0 ? $"{Du:0.#}"   : "—";
        public string DnDisplay       => Dn   > 0 ? Dn.ToString() : "—";
        public string SlopeDisplay    => SlopePct > 0 ? $"{SlopePct:0.0}" : "—";
        public string VelocityDisplay => Velocity > 0 ? $"{Velocity:0.0}" : "—";
    }

    public class PlumbStackRow
    {
        public string StackId    { get; set; }
        public int    Dn         { get; set; }
        public double Du         { get; set; }
        public double CapacityDU { get; set; }
        public double UtilPct    { get; set; }
        public string Status     { get; set; }
        public string DnDisplay       => Dn > 0 ? Dn.ToString() : "—";
        public string DuDisplay       => $"{Du:0.#}";
        public string CapacityDisplay => $"{CapacityDU:0.#}";
        public string UtilDisplay     => $"{UtilPct:0.#}%";
    }

    public class PlumbBackflowRow
    {
        public string PipeId   { get; set; }
        public string Category { get; set; }
        public string Device   { get; set; }
        public string Status   { get; set; }
    }

    public class PlumbAuditFinding
    {
        public string Severity   { get; set; }
        public string Domain     { get; set; }
        public string Message    { get; set; }
        public string ElementRef { get; set; }
    }

    public class PlumbManholeRow
    {
        public string Tag      { get; set; }
        public double UsInvM   { get; set; }
        public double DsInvM   { get; set; }
        public double CoverM   { get; set; }
        public double DepthM   { get; set; }
        public string Location { get; set; }
        public string UsInvDisplay => $"{UsInvM:0.000}";
        public string DsInvDisplay => $"{DsInvM:0.000}";
        public string CoverDisplay => $"{CoverM:0.000}";
        public string DepthDisplay => $"{DepthM:0.000}";
    }

    public class PlumbBoqRow
    {
        public string Item     { get; set; }
        public string Material { get; set; }
        public string Dn       { get; set; }
        public double Qty      { get; set; }
        public string Unit     { get; set; }
        public string QtyDisplay => $"{Qty:0.##}";
    }

    public class PlumbConfigSummary
    {
        public string DrainageStandard { get; set; }
        public string SupplyStandard   { get; set; }
        public string VelocityMaxText  { get; set; }
        public string MinSlopeText     { get; set; }
        public string KFactorText      { get; set; }
        public string MaterialDcw      { get; set; }
        public string MaterialDhw      { get; set; }
    }

    public class PlumbRagSummary
    {
        public int SupplyRed, SupplyAmber, SupplyGreen;
        public int DrainageRed, DrainageAmber, DrainageGreen;
        public int VentsRed, VentsAmber, VentsGreen;
        public int BackflowRed, BackflowAmber, BackflowGreen;
        public int HtmRed, HtmAmber, HtmGreen;
    }

    public class PlumbingPanelSnapshot
    {
        public List<PlumbFixtureRow>   Fixtures;
        public List<PlumbPrvRow>       Prvs;
        public List<PlumbTmvRow>       Tmvs;
        public List<PlumbDeadLegRow>   DeadLegs;
        public List<PlumbPipeRow>      DrainagePipes;
        public List<PlumbStackRow>     Stacks;
        public List<PlumbBackflowRow>  Backflow;
        public List<PlumbAuditFinding> Findings;
        public List<PlumbManholeRow>   Manholes;
        public List<PlumbBoqRow>       Boq;
        public PlumbConfigSummary      ConfigSummary;
        public PlumbRagSummary         Rag;
    }

    public class PlumbSizingInputs
    {
        public string Standard, Building, Regime, SupplyMethod;
        public double VelocityMaxMps, MinPressureKpa, MaxPressureKpa;
        public double DrnMinSlopePct, DrnMaxSlopePct, DrnSelfCleanseMps, DrnRoughnessMm;
    }

    public class PlumbRoutingInputs
    {
        public double MaxSearchRadiusMm;
        public bool   SnapToCorridor;
        public bool   PreviewBeforeCommit;
    }

    public class PlumbStormInputs
    {
        public double RoofAreaM2, RunoffCoefficient, RainIntensityMmH, ClimateUpliftPct;
    }
}
