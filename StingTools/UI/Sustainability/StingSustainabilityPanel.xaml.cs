// StingSustainabilityPanel — code-behind for the STING Sustainability Center
// dockable panel (Phase 195). Modelled on StingPlumbingPanel: SETUP / DASHBOARD
// / MATERIALS / COST tabs. The SETUP tab is the zero-hardcoding options surface
// (§2.5). Every Button.Click dispatches via StingSustainabilityCommandHandler so
// Revit API calls run on the API thread. Commands push results back through
// RefreshFromRun.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core;
using StingTools.Core.Sustainability;
using Button   = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace StingTools.UI.Sustainability
{
    public partial class StingSustainabilityPanel : Page
    {
        public ObservableCollection<EndUseRow>  EnergyRows  { get; } = new ObservableCollection<EndUseRow>();
        public ObservableCollection<HotspotRow> HotspotRows { get; } = new ObservableCollection<HotspotRow>();
        public ObservableCollection<LccRow>     LccRows     { get; } = new ObservableCollection<LccRow>();
        // WS B3 — mixed-use zones grid (opt-in; overrides the single building use).
        private readonly ObservableCollection<ZoneRow> _zoneRows = new ObservableCollection<ZoneRow>();

        private static StingSustainabilityPanel _instance;
        public static StingSustainabilityPanel Instance => _instance;

        public StingSustainabilityPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme non-fatal */ }
            _instance = this;

            EnergyGrid.ItemsSource  = EnergyRows;
            HotspotGrid.ItemsSource = HotspotRows;
            LccGrid.ItemsSource     = LccRows;
            dgZones.ItemsSource     = _zoneRows;   // WS B3 mixed-use zones grid

            // Seed the SETUP form with defaults so dropdowns are populated.
            try { LoadSetupForm(SustainProjectSetup.CreateDefault()); }
            catch (Exception ex) { StingLog.Warn($"Sus ctor LoadSetupForm: {ex.Message}"); }
            UpdateStatus("Ready");
        }

        /// <summary>Friendly display name for a command tag (no raw tags in the UI).</summary>
        private static string FriendlyName(string tag)
        {
            switch (tag)
            {
                case "Sustain_ProjectSetup": return "Save project setup";
                case "Sustain_Dashboard":    return "Running dashboard";
                case "Sustain_SetBaseline":  return "Setting baseline";
                case "Sustain_SupplyConfig": return "Saving supply config";
                case "Sustain_AutoFill":     return "Reading from model";
                case "Sustain_EdgeExport":   return "EDGE export";
                case "Sustain_LccBenefit":   return "Life-cycle cost";
                case "Sustain_EpdAssign":    return "EPD register";
                case "Sustain_LeedScorecard":return "LEED scorecard";
                case "Sustain_PublishToServer": return "Publishing to server";
                default:                     return "Working";
            }
        }

        // ── Unified click dispatcher ─────────────────────────────────────
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button btn) || !(btn.Tag is string tag) || string.IsNullOrEmpty(tag)) return;
                StingSustainabilityCommandHandler.Instance?.SetCommand(tag);
                StingSustainabilityCommandHandler.Event?.Raise();
                UpdateStatus(FriendlyName(tag) + "…");
            }
            catch (Exception ex)
            {
                MessageBox.Show("STING Sustainability dispatch error: " + ex.Message);
                StingLog.Error("Sus Cmd_Click", ex);
            }
        }

        public void UpdateStatus(string text)
        {
            try { Dispatcher.Invoke(() => txtSusStatus.Text = text ?? ""); }
            catch (Exception ex) { StingLog.Warn($"Sus UpdateStatus: {ex.Message}"); }
        }

        // ── SETUP form <-> project setup ─────────────────────────────────

        public void LoadSetupForm(SustainProjectSetup s)
        {
            try
            {
                if (s == null) s = SustainProjectSetup.CreateDefault();
                chkSchemeEdge.IsChecked = s.Schemes.Any(x => x.Equals("EDGE", StringComparison.OrdinalIgnoreCase));
                chkSchemeLeed.IsChecked = s.Schemes.Any(x => x.Equals("LEED", StringComparison.OrdinalIgnoreCase));
                SetComboByTag(cmbEdgeLevel, s.LevelFor("EDGE", "Advanced"));
                SetComboByTag(cmbUnits, s.Units.ToString());
                cmbCountry.Text     = string.IsNullOrWhiteSpace(s.Country) ? "*" : s.Country;
                txtClimateSite.Text = s.ClimateSiteId ?? "";
                cmbClimateZone.Text = s.ClimateZone ?? "";
                SetComboByContent(cmbBuildingUse, s.DominantBuildingUse);
                txtOccupancy.Text   = s.TotalOccupancy > 0 ? s.TotalOccupancy.ToString() : "";

                var z0 = s.Zones?.FirstOrDefault();
                txtFloorArea.Text = (s.TotalFloorAreaM2 > 0)
                    ? s.TotalFloorAreaM2.ToString("0", CultureInfo.InvariantCulture) : "";
                // Blank when 0 so it reads "use baseline COP", not a misleading 0.0.
                txtCop.Text = (z0 != null && z0.CoolingCop > 0)
                    ? z0.CoolingCop.ToString("0.0", CultureInfo.InvariantCulture) : "";

                var sup = s.Supply ?? new SupplyConfig();
                SetComboByContent(cmbSupplyMode, sup.Mode);
                txtPvKwp.Text        = sup.PvKwp.ToString("0", CultureInfo.InvariantCulture);
                txtPvPr.Text         = sup.PvPerformanceRatio.ToString("0.00", CultureInfo.InvariantCulture);
                txtGridCarbon.Text   = sup.GridCarbonKgco2eKwh.ToString("0.00", CultureInfo.InvariantCulture);
                txtDieselCarbon.Text = sup.DieselCarbonKgco2eKwh.ToString("0.00", CultureInfo.InvariantCulture);
                txtDieselFrac.Text   = sup.DieselFraction.ToString("0.00", CultureInfo.InvariantCulture);

                // WS B5 — restore any recorded EDGE-app official % (blank when unset).
                var eo = s.EdgeOfficial ?? new EdgeOfficialFigures();
                txtEnergyOfficial.Text = PctOrBlank(eo.EnergySavingsPct);
                txtWaterOfficial.Text  = PctOrBlank(eo.WaterSavingsPct);
                txtMatOfficial.Text    = PctOrBlank(eo.MaterialsSavingsPct);

                // WS B3 — show the mixed-use grid only for multi-zone projects; a
                // single-zone project keeps using the building-use fields above.
                _zoneRows.Clear();
                if (s.Zones != null && s.Zones.Count > 1)
                    foreach (var z in s.Zones)
                        _zoneRows.Add(new ZoneRow
                        {
                            Use = z.BuildingUse, AreaM2 = z.FloorAreaM2,
                            Occupancy = z.Occupancy, CoolingCop = z.CoolingCop
                        });
            }
            catch (Exception ex) { StingLog.Warn($"Sus LoadSetupForm: {ex.Message}"); }
        }

        public SustainProjectSetup ReadSetupForm()
        {
            var s = SustainProjectSetup.CreateDefault();
            try
            {
                s.Schemes = new List<string>();
                if (chkSchemeEdge.IsChecked == true) s.Schemes.Add("EDGE");
                if (chkSchemeLeed.IsChecked == true) s.Schemes.Add("LEED");
                if (s.Schemes.Count == 0) s.Schemes.Add("EDGE");

                s.TargetLevels = new Dictionary<string, string> { { "EDGE", TagOf(cmbEdgeLevel, "Advanced") } };
                s.Units = string.Equals(TagOf(cmbUnits, "SI"), "IP", StringComparison.OrdinalIgnoreCase)
                    ? SustainUnits.IP : SustainUnits.SI;
                string country = cmbCountry.Text?.Trim();
                s.Country       = string.IsNullOrWhiteSpace(country) ? "*" : country;
                s.ClimateSiteId = txtClimateSite.Text?.Trim();
                s.ClimateZone   = cmbClimateZone.Text?.Trim();

                string use = ContentOf(cmbBuildingUse, "office");
                int occ = ParseInt(txtOccupancy.Text, 0);
                double cop = ParseDouble(txtCop.Text, 0);
                double area = ParseDouble(txtFloorArea.Text, 0);
                s.Zones = new List<ZoneSetup>
                {
                    new ZoneSetup { ZoneId = "whole-building", BuildingUse = use,
                                    FloorAreaM2 = area, Occupancy = occ, CoolingCop = cop }
                };

                s.Supply = new SupplyConfig
                {
                    Mode = ContentOf(cmbSupplyMode, "grid_tied"),
                    PvKwp = ParseDouble(txtPvKwp.Text, 0),
                    PvPerformanceRatio = ParseDouble(txtPvPr.Text, 0.75),
                    GridCarbonKgco2eKwh = ParseDouble(txtGridCarbon.Text, 0.45),
                    DieselCarbonKgco2eKwh = ParseDouble(txtDieselCarbon.Text, 0.8),
                    DieselFraction = ParseDouble(txtDieselFrac.Text, 0.0)
                };

                // WS B5 — capture the EDGE-app official % (null when blank). These
                // override the indicative figures and make the EDGE level reflect the
                // certified numbers when the user records them.
                s.EdgeOfficial = new EdgeOfficialFigures
                {
                    EnergySavingsPct    = ParseNullable(txtEnergyOfficial.Text),
                    WaterSavingsPct     = ParseNullable(txtWaterOfficial.Text),
                    MaterialsSavingsPct = ParseNullable(txtMatOfficial.Text)
                };

                // WS B3 — when the mixed-use grid has rows, it is the authoritative
                // zone list (area-weighted energy/materials, occupancy-weighted water
                // roll up downstream). Otherwise the single-zone fields above stand.
                var gridZones = _zoneRows
                    .Where(r => r != null && r.AreaM2 > 0)
                    .Select(r => new ZoneSetup
                    {
                        ZoneId = string.IsNullOrWhiteSpace(r.Use) ? "zone" : r.Use.Trim(),
                        BuildingUse = string.IsNullOrWhiteSpace(r.Use) ? "office" : r.Use.Trim(),
                        FloorAreaM2 = r.AreaM2, Occupancy = r.Occupancy, CoolingCop = r.CoolingCop
                    })
                    .ToList();
                if (gridZones.Count > 0) s.Zones = gridZones;
            }
            catch (Exception ex) { StingLog.Warn($"Sus ReadSetupForm: {ex.Message}"); }
            return s;
        }

        // ── Push results from the engine ─────────────────────────────────

        public void RefreshFromRun(SustainabilityRunResult res)
        {
            if (res == null) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var edge = res.Schemes?.FirstOrDefault(s => s.SchemeId == "EDGE");
                    var energy = edge?.Gates?.FirstOrDefault(g => g.GateId == "energy");
                    var water  = edge?.Gates?.FirstOrDefault(g => g.GateId == "water");
                    var mat    = edge?.Gates?.FirstOrDefault(g => g.GateId == "materials");

                    txtEnergyIndic.Text = IndicOf(energy);
                    txtWaterIndic.Text  = IndicOf(water);
                    txtMatIndic.Text    = IndicOf(mat);
                    txtEnergyStatus.Text = StatusOf(energy);
                    txtWaterStatus.Text  = StatusOf(water);
                    txtMatStatus.Text    = StatusOf(mat);
                    txtEdgeOverall.Text  = edge != null
                        ? $"EDGE level (STING-determinable): {edge.AchievedLevel} (target {edge.TargetLevel}) — " +
                          $"{(edge.Passed ? "energy+water meet target; confirm materials in EDGE app" : "below target")}"
                        : "EDGE level: —";

                    txtProxyLog.Text = res.Baseline?.Summary ?? "(no baseline)";

                    // Surface the engine notes so 0s / not-computed gates are explained.
                    var notes = (res.Warnings ?? new List<string>()).Distinct().Take(8).ToList();
                    txtWarnings.Text = notes.Count > 0
                        ? "• " + string.Join("\n• ", notes)
                        : "All gates computed from model data.";

                    // Active display units (SI default / IP when the setup selects it).
                    var u = res.Setup?.Units ?? SustainUnits.SI;
                    try { if (EnergyGrid.Columns.Count > 1) EnergyGrid.Columns[1].Header = SustainUnitConverter.EnergyAbsUnit(u); } catch { }

                    EnergyRows.Clear();
                    var e = res.Energy?.Design;
                    if (e != null)
                    {
                        AddEnergy("Cooling", SustainUnitConverter.EnergyAbs(e.CoolingKwh, u));
                        AddEnergy("Heating", SustainUnitConverter.EnergyAbs(e.HeatingKwh, u));
                        AddEnergy("Fans/pumps", SustainUnitConverter.EnergyAbs(e.FansKwh, u));
                        AddEnergy("Lighting", SustainUnitConverter.EnergyAbs(e.LightingKwh, u));
                        AddEnergy("Equipment", SustainUnitConverter.EnergyAbs(e.EquipmentKwh, u));
                        AddEnergy("DHW", SustainUnitConverter.EnergyAbs(e.DhwKwh, u));
                        AddEnergy("TOTAL", SustainUnitConverter.EnergyAbs(e.TotalKwh, u));
                    }

                    double carbonI = SustainUnitConverter.CarbonIntensity(res.Materials?.CarbonIntensityKgM2 ?? 0, u);
                    double energyI = SustainUnitConverter.EnergyIntensityMj(res.Materials?.EnergyIntensityMjM2 ?? 0, u);
                    txtCarbonIntensity.Text = $"Embodied carbon: {carbonI:F1} {SustainUnitConverter.CarbonIntensityUnit(u)} (A1-A3 GWP, EN 15978)";
                    txtEnergyIntensity.Text = $"Embodied energy: {energyI:F0} {SustainUnitConverter.EnergyIntensityUnit(u)} (CED — EDGE materials track, indicative)";
                    if (res.Materials != null)
                    {
                        bool matOk = res.Materials.Computed;
                        txtMatCoverage.Text = matOk
                            ? $"{res.Materials.TotalLines} material(s) measured · {res.Materials.CarbonStampedLines} carbon-stamped · {res.Materials.LinesFromEpd} from EPD."
                            : $"NOT COMPUTED — {res.Materials.TotalLines} measured, {res.Materials.CarbonStampedLines} carbon-stamped" +
                              (res.Materials.FloorAreaM2 <= 0 ? " · no floor area (set GFA in Setup)" : "") +
                              ". Stamp STING_EMB_CARBON_NR / SUS_EPD_REF_TXT and re-run.";
                    }
                    HotspotRows.Clear();
                    if (res.Materials?.Hotspots != null)
                        foreach (var h in res.Materials.Hotspots)
                            HotspotRows.Add(new HotspotRow { Material = h.Material, CarbonKg = $"{h.CarbonKg:F0}", Share = $"{h.SharePct:F0}%" });

                    UpdateStatus($"Dashboard run · EDGE {edge?.AchievedLevel ?? "—"}");
                });
            }
            catch (Exception ex) { StingLog.Warn($"Sus RefreshFromRun: {ex.Message}"); }
        }

        private void AddEnergy(string k, double v) => EnergyRows.Add(new EndUseRow { EndUse = k, Kwh = $"{v:F0}" });

        /// <summary>Populate the COST tab grid from the LCC command's rows
        /// (string[]: Name, Gate, CostKey, Capex, AnnualSaving, LifetimeSaving, NetBenefit).</summary>
        public void ApplyLcc(IEnumerable<string[]> rows)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LccRows.Clear();
                    foreach (var r in rows ?? Enumerable.Empty<string[]>())
                        if (r != null && r.Length >= 7)
                            LccRows.Add(new LccRow { Measure = r[0], Gate = r[1], Capex = r[3], NetBenefit = r[6] });
                    UpdateStatus($"LCC: {LccRows.Count} measure(s)");
                });
            }
            catch (Exception ex) { StingLog.Warn($"Sus ApplyLcc: {ex.Message}"); }
        }

        /// <summary>Indicative-value cell. Shows the % only when the gate was
        /// computed from model data; otherwise "Not computed" / "EDGE app" so a
        /// zero-design 100% or a hardcoded default never reads as a real number.</summary>
        private static string IndicOf(GateResult g)
        {
            if (g == null) return "—";
            if (g.Delegated) return "→ EDGE app";
            if (!g.Computed) return "Not computed";
            return $"{g.IndicativeValue:F1}% (≥{g.Threshold:F0}%)";
        }

        private static string StatusOf(GateResult g)
        {
            if (g == null) return "—";
            if (g.NotEvaluated) return "n/a";
            if (g.Delegated) return "→ EDGE app";
            if (!g.Computed) return "not computed";
            return g.Passed ? "✓ pass" : "✗ below";
        }

        /// <summary>Receive area + occupancy from the Sustain_AutoFill command
        /// (runs on the Revit API thread) and write them into the SETUP form.</summary>
        public void ApplyAutoFill(double floorAreaM2, int occupancy, string climateZone = null)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (floorAreaM2 > 0) txtFloorArea.Text = floorAreaM2.ToString("0", CultureInfo.InvariantCulture);
                    if (occupancy > 0)   txtOccupancy.Text = occupancy.ToString();
                    if (!string.IsNullOrWhiteSpace(climateZone) && string.IsNullOrWhiteSpace(cmbClimateZone.Text))
                        cmbClimateZone.Text = climateZone;
                    UpdateStatus($"Auto-filled from model: {floorAreaM2:0} m² · {occupancy} occ");
                });
            }
            catch (Exception ex) { StingLog.Warn($"Sus ApplyAutoFill: {ex.Message}"); }
        }

        /// <summary>WS B4 — repopulate the building-use dropdown from the data-driven
        /// catalogue (registry union), preserving the current selection. Replaces the
        /// hardcoded 5-item XAML list.</summary>
        public void PopulateBuildingUses(IEnumerable<string> uses)
        {
            try
            {
                if (uses == null) return;
                string current = ContentOf(cmbBuildingUse, "office");
                cmbBuildingUse.Items.Clear();
                foreach (var u in uses)
                    if (!string.IsNullOrWhiteSpace(u))
                        cmbBuildingUse.Items.Add(new ComboBoxItem { Content = u });
                if (cmbBuildingUse.Items.Count == 0)
                    cmbBuildingUse.Items.Add(new ComboBoxItem { Content = "office" });
                SetComboByContent(cmbBuildingUse, current);
                if (cmbBuildingUse.SelectedItem == null) cmbBuildingUse.SelectedIndex = 0;
            }
            catch (Exception ex) { StingLog.Warn($"Sus PopulateBuildingUses: {ex.Message}"); }
        }

        // ── WS B3 — mixed-use zones grid handlers ─────────────────────────
        private void AddZone_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                // Seed the first row from the single-zone fields so the user's entry
                // isn't lost when they switch to mixed-use.
                if (_zoneRows.Count == 0)
                {
                    double area = ParseDouble(txtFloorArea.Text, 0);
                    int occ = ParseInt(txtOccupancy.Text, 0);
                    if (area > 0 || occ > 0)
                        _zoneRows.Add(new ZoneRow
                        {
                            Use = ContentOf(cmbBuildingUse, "office"),
                            AreaM2 = area, Occupancy = occ, CoolingCop = ParseDouble(txtCop.Text, 0)
                        });
                }
                _zoneRows.Add(new ZoneRow { Use = ContentOf(cmbBuildingUse, "office") });
            }
            catch (Exception ex) { StingLog.Warn($"Sus AddZone: {ex.Message}"); }
        }

        private void RemoveZone_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (dgZones.SelectedItem is ZoneRow r) _zoneRows.Remove(r);
                else if (_zoneRows.Count > 0) _zoneRows.RemoveAt(_zoneRows.Count - 1);
            }
            catch (Exception ex) { StingLog.Warn($"Sus RemoveZone: {ex.Message}"); }
        }

        // ── small form helpers ───────────────────────────────────────────
        private static string PctOrBlank(double? v)
            => v.HasValue ? v.Value.ToString("0.#", CultureInfo.InvariantCulture) : "";
        private static double? ParseNullable(string s)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        private static void SetComboByTag(ComboBox c, string tag)
        {
            if (c == null || string.IsNullOrEmpty(tag)) return;
            foreach (var it in c.Items.OfType<ComboBoxItem>())
                if (string.Equals(it.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
        }
        private static void SetComboByContent(ComboBox c, string content)
        {
            if (c == null || string.IsNullOrEmpty(content)) return;
            foreach (var it in c.Items.OfType<ComboBoxItem>())
                if (string.Equals(it.Content as string, content, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
        }
        private static string TagOf(ComboBox c, string fallback)
            => (c?.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;
        private static string ContentOf(ComboBox c, string fallback)
            => (c?.SelectedItem as ComboBoxItem)?.Content as string ?? fallback;
        private static int ParseInt(string s, int fb)
            => int.TryParse((s ?? "").Trim(), out var v) ? v : fb;
        private static double ParseDouble(string s, double fb)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fb;
    }

    // ── Row view-models ──────────────────────────────────────────────────
    public class EndUseRow  { public string EndUse { get; set; } public string Kwh { get; set; } }
    public class HotspotRow { public string Material { get; set; } public string CarbonKg { get; set; } public string Share { get; set; } }
    public class LccRow     { public string Measure { get; set; } public string Gate { get; set; } public string Capex { get; set; } public string NetBenefit { get; set; } }
    public class ZoneRow    { public string Use { get; set; } = "office"; public double AreaM2 { get; set; } public int Occupancy { get; set; } public double CoolingCop { get; set; } }
}
