using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StingTools.UI
{
    // ── Healthcare tab — partial of StingDockPanel ─────────────────────
    //
    // Inline-interactive Healthcare tab (replaces the button-only block at
    // StingDockPanel.xaml:5009). Mirrors the Tag Studio pattern: named
    // controls flushed to StingCommandHandler.SetExtraParam right before
    // any Healthcare_* tag dispatches.
    //
    // ExtraParam keys are namespaced "Hc." so they don't collide with
    // Tag Studio (ElbowMode, TagTextSize, …) or v4 (Placement_*) keys.
    //
    // Existing 33 healthcare commands ignore the new params today; the
    // tab still triggers them with the right tags so behaviour is
    // backward-compatible. Commands can opt in to read Hc.* params in a
    // later pass.
    public partial class StingDockPanel
    {
        // ── Validator grid row model ───────────────────────────────────
        public sealed class HcValidatorRow
        {
            public bool   Run         { get; set; } = true;
            public string Key         { get; set; } = "";   // dispatch tag suffix
            public string DisplayName { get; set; } = "";
            public string Standard    { get; set; } = "";
            public string Status      { get; set; } = "—";  // ●Green/Amber/Red/Skipped
            public string Result      { get; set; } = "";
            public string LastRun     { get; set; } = "";
        }

        // ── Room-data-sheet grid row model ─────────────────────────────
        public sealed class HcRoomRow
        {
            public bool   Run         { get; set; }
            public string RoomNumber  { get; set; } = "";
            public string RoomName    { get; set; } = "";
            public string ClassCode   { get; set; } = "";
            public double PressurePa  { get; set; }
            public double Ach         { get; set; }
            public bool   Tmv3        { get; set; }
            public string RdsIssued   { get; set; } = "—";
        }

        // Backing collections (ItemsSource for the two main DataGrids).
        // Re-bound on first init; stays stable across tab switches.
        public ObservableCollection<HcValidatorRow> HcValidators { get; }
            = new ObservableCollection<HcValidatorRow>();
        public ObservableCollection<HcRoomRow> HcRooms { get; }
            = new ObservableCollection<HcRoomRow>();

        // Specialist card lookup — set once, swapped via Visibility.
        private readonly Dictionary<string, FrameworkElement> _hcSpecialistCards
            = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private bool _hcInitDone;

        // ── Init: called from constructor after InitializeComponent ────
        // Wires the validator grid rows, specialist cards, and combo
        // change handlers. Idempotent.
        private void InitializeHealthcareTab()
        {
            if (_hcInitDone) return;

            try
            {
                if (dgHcValidators != null)
                {
                    SeedValidatorRows();
                    dgHcValidators.ItemsSource = HcValidators;
                }
                if (dgHcRooms != null)
                {
                    dgHcRooms.ItemsSource = HcRooms;
                }
                BuildSpecialistCardMap();
                UpdateSpecialistCardVisibility();

                if (cmbHcSpecialistKind != null)
                    cmbHcSpecialistKind.SelectionChanged += (_, __) => UpdateSpecialistCardVisibility();

                if (cmbHcRadCalc != null)
                    cmbHcRadCalc.SelectionChanged += (_, __) => UpdateRadLiveReadout();

                _hcInitDone = true;
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"InitializeHealthcareTab: {ex.Message}");
            }
        }

        private void SeedValidatorRows()
        {
            HcValidators.Clear();
            void Add(string key, string name, string std)
                => HcValidators.Add(new HcValidatorRow { Key = key, DisplayName = name, Standard = std });

            Add("PressureAudit",     "Pressure regime",   "HTM 03-01 / ASHRAE 170");
            Add("MgasAudit",         "MGPS network",      "HTM 02-01 / NFPA 99");
            Add("MgasVerify",        "MGPS verify",       "NFPA 99 §5.1.12");
            Add("EesBranch",         "EES branches",      "NFPA 99 §6.4 / NEC 517");
            Add("WaterSafety",       "Water safety",      "HTM 04-01");
            Add("RadShield",         "Rad shield",        "NCRP 147");
            Add("AdvancedRadShield", "Adv Rad PET/NM",    "NCRP 151 / IAEA 47");
            Add("AdjacencyAudit",    "Adjacency / flow",  "HBN");
            Add("AntiLigature",      "Anti-ligature",     "HBN 03-01 / FGI Pt 2");
            Add("StructuralLoad",    "Structural loads",  "VC vibration");
            Add("Acoustic",          "Acoustic",          "HTM 08-01");
            Add("EndoscopeTrace",    "Endoscope trace",   "HTM 01-06");
            Add("EesResilience",     "EES resilience",    "NFPA 110");
            Add("RtlsCoverage",      "RTLS coverage",     "");
            Add("WasteFlow",         "Waste flow",        "HTM 07-01");
            Add("IoTStaleness",      "IoT staleness",     "");
        }

        private void BuildSpecialistCardMap()
        {
            _hcSpecialistCards.Clear();
            void Map(string key, FrameworkElement card)
            {
                if (card != null) _hcSpecialistCards[key] = card;
            }
            Map("HybridOr",          cardHcHybridOr);
            Map("PharmacyUsp",       cardHcPharmacyUsp);
            Map("BehaviouralHealth", cardHcBehavioural);
            Map("Mortuary",          cardHcMortuary);
            Map("MaternityNicu",     cardHcMaternity);
            Map("Hsdu",              cardHcHsdu);
            Map("Dialysis",          cardHcDialysis);
            Map("Hbo",               cardHcHbo);
        }

        private void UpdateSpecialistCardVisibility()
        {
            string sel = SelectedComboTag(cmbHcSpecialistKind, "HybridOr");
            foreach (var kv in _hcSpecialistCards)
                kv.Value.Visibility = string.Equals(kv.Key, sel, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // Live "required Pb" readout. Approximation only — the authoritative
        // calc lives in NCRP147Calculator and runs server-side at dispatch.
        // Goal here: give the user an instant ballpark while sliders move.
        private void UpdateRadLiveReadout()
        {
            if (lblHcRadRequiredPb == null) return;
            try
            {
                double kvp = ParseDoubleOr(txtHcRadKvp?.Text, 125);
                double w   = ParseDoubleOr(txtHcRadW?.Text, 50);
                double u   = ParseDoubleOr(txtHcRadU?.Text, 1);
                double t   = ParseDoubleOr(txtHcRadT?.Text, 1);
                double d   = ParseDoubleOr(txtHcRadD?.Text, 2);
                if (d <= 0) d = 1;

                // Coarse first-pass: P (Sv/wk) target based on area type,
                // K1 (mGy·m²/(mA·min)) approximated per kVp band, then a
                // rough TVL-based mm-Pb. Replaced by NCRP 147 at run-time.
                double pTarget = (rbHcRadAreaUncontrolled?.IsChecked == true) ? 0.02 : 0.1; // mSv/wk
                double k1 = kvp >= 140 ? 4.5 : (kvp >= 120 ? 3.0 : (kvp >= 100 ? 2.0 : 1.4));
                double bRequired = (pTarget / 1000.0) * (d * d) / (w * u * t * k1);
                double tvlPb = kvp >= 140 ? 0.86 : (kvp >= 120 ? 0.75 : (kvp >= 100 ? 0.55 : 0.42));
                double n = (bRequired > 0 && bRequired < 1)
                    ? -Math.Log10(bRequired)
                    : 0;
                double mmPb = Math.Max(0, n * tvlPb);

                lblHcRadRequiredPb.Text = $"Required Pb ≈ {mmPb:F2} mm  (live preview — NCRP 147 runs at dispatch)";
            }
            catch
            {
                lblHcRadRequiredPb.Text = "Required Pb — enter inputs";
            }
        }

        // ── Pre-dispatch flush ─────────────────────────────────────────
        // Called from Cmd_Click for every Healthcare_* tag. Reads every
        // named control on the Healthcare tab and writes the value into
        // the StingCommandHandler.SetExtraParam channel. Each key is
        // prefixed "Hc." so nothing collides with Tag Studio's params.
        internal void SetHealthcareOptions()
        {
            try
            {
                // Sticky context bar
                StingCommandHandler.SetExtraParam("Hc.FacilityType", SelectedComboTag(cmbHcFacility, "ACUTE"));
                string scope = "Project";
                if (rbHcScopeView?.IsChecked == true) scope = "ActiveView";
                else if (rbHcScopeSelection?.IsChecked == true) scope = "Selection";
                StingCommandHandler.SetExtraParam("Hc.Scope", scope);
                StingCommandHandler.SetExtraParam("Hc.SkipUnclassified", BoolStr(chkHcSkipUnclassified?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.IncludeLinks",     BoolStr(chkHcIncludeLinks?.IsChecked));

                // Validator selection: comma-separated keys of ticked rows
                if (HcValidators != null)
                {
                    var picked = new List<string>();
                    foreach (var row in HcValidators)
                        if (row.Run) picked.Add(row.Key);
                    StingCommandHandler.SetExtraParam("Hc.SelectedValidators", string.Join(",", picked));
                }

                // Pressure / Water / Adjacency / Endoscope / EES / IoT / Rad thresholds
                StingCommandHandler.SetExtraParam("Hc.DpMinPa",       NumStr(sldHcDpMin?.Value, 2.5));
                StingCommandHandler.SetExtraParam("Hc.AchMin",        NumStr(sldHcAchMin?.Value, 12));
                StingCommandHandler.SetExtraParam("Hc.AnteroomStrict",BoolStr(chkHcAnteroomStrict?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.DeadLegMaxM",   NumStr(sldHcDeadLegMaxM?.Value, 1));
                StingCommandHandler.SetExtraParam("Hc.AdjacencyDepth",SelectedComboTag(cmbHcAdjacencyDepth, "3"));
                StingCommandHandler.SetExtraParam("Hc.EndoMinReaders",NumStr(sldHcRfidMin?.Value, 4));
                StingCommandHandler.SetExtraParam("Hc.UpsMaxAgeYrs",  NumStr(sldHcUpsMaxAgeYrs?.Value, 5));
                StingCommandHandler.SetExtraParam("Hc.IotStaleMins",  NumStr(sldHcIotStaleMins?.Value, 30));
                StingCommandHandler.SetExtraParam("Hc.IotProtocol",   SelectedComboTag(cmbHcIotProtocol, ""));
                StingCommandHandler.SetExtraParam("Hc.RadRequireQe",  BoolStr(chkHcRadRequireQeSignoff?.IsChecked));

                // MGPS sub-tab
                StingCommandHandler.SetExtraParam("Hc.Mgas.Gas",       SelectedComboTag(cmbHcMgasGas, "O2"));
                StingCommandHandler.SetExtraParam("Hc.Mgas.Zone",      SelectedComboTag(cmbHcMgasZone, ""));
                StingCommandHandler.SetExtraParam("Hc.Mgas.Verifier",  txtHcMgasVerifier?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Mgas.Step",      SelectedComboTag(cmbHcMgasStep, "1"));
                StingCommandHandler.SetExtraParam("Hc.Mgas.SignAtEnd", BoolStr(chkHcMgasSignAtEnd?.IsChecked));

                // Radiation sub-tab
                StingCommandHandler.SetExtraParam("Hc.Rad.CalcType", SelectedComboTag(cmbHcRadCalc, "CT"));
                StingCommandHandler.SetExtraParam("Hc.Rad.Kvp",      txtHcRadKvp?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rad.W",        txtHcRadW?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rad.U",        txtHcRadU?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rad.T",        txtHcRadT?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rad.D",        txtHcRadD?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rad.Area",
                    rbHcRadAreaControlled?.IsChecked == true ? "Controlled" : "Uncontrolled");
                StingCommandHandler.SetExtraParam("Hc.Rad.AutoApply", BoolStr(chkHcRadAutoApply?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Rad.QeName",    txtHcRadQeName?.Text ?? "");

                // MRI zoning
                StingCommandHandler.SetExtraParam("Hc.Mri.Zone",        SelectedComboTag(cmbHcMriZone, "Z1"));
                StingCommandHandler.SetExtraParam("Hc.Mri.FaradayFlag", BoolStr(chkHcMriFaradayFlag?.IsChecked));

                // Rooms / RDS sub-tab
                StingCommandHandler.SetExtraParam("Hc.Rds.ClassFilter", SelectedComboTag(cmbHcRoomClassFilter, "All"));
                StingCommandHandler.SetExtraParam("Hc.Rds.Search",      txtHcRoomSearch?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Rds.MissingOnly", BoolStr(chkHcRoomMissingOnly?.IsChecked));
                if (HcRooms != null)
                {
                    var picked = new List<string>();
                    foreach (var r in HcRooms)
                        if (r.Run && !string.IsNullOrEmpty(r.RoomNumber)) picked.Add(r.RoomNumber);
                    StingCommandHandler.SetExtraParam("Hc.Rds.PickedRooms", string.Join(",", picked));
                }

                // Specialist — kind selector + every per-card control. Cards
                // not on screen still flush so their wrapping commands always
                // see a non-empty value (defaults match HcOptions).
                StingCommandHandler.SetExtraParam("Hc.Specialist.Kind", SelectedComboTag(cmbHcSpecialistKind, "HybridOr"));

                // HybridOr / CathLab / IR
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hor.Room",       cmbHcHorRoom?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hor.MinAreaM2",  NumStr(sldHcHorMinAreaM2?.Value, 70));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hor.IncludeIr",  BoolStr(chkHcHorIncludeIr?.IsChecked));

                // Pharmacy USP
                string uspStd = (rbHcUsp800?.IsChecked == true) ? "USP-800" : "USP-797";
                StingCommandHandler.SetExtraParam("Hc.Specialist.Usp.Standard",   uspStd);
                StingCommandHandler.SetExtraParam("Hc.Specialist.Usp.AchMin",     NumStr(sldHcUspAch?.Value, 30));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Usp.DpPa",       NumStr(sldHcUspDp?.Value, 2.5));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Usp.HasBuffer",  BoolStr(chkHcUspBuffer?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Usp.HasAnteroom",BoolStr(chkHcUspAnteroom?.IsChecked));

                // Behavioural
                StingCommandHandler.SetExtraParam("Hc.Specialist.Bh.UseFgi",      BoolStr(chkHcBhFgi?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Bh.UseHbn",      BoolStr(chkHcBhHbn?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Bh.RiskLevel",   NumStr(sldHcBhRiskLevel?.Value, 3));

                // Mortuary
                StingCommandHandler.SetExtraParam("Hc.Specialist.Mort.BedCount",     txtHcMortBeds?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Specialist.Mort.PctBaysOfBeds",NumStr(sldHcMortPctBays?.Value, 0.5));

                // Maternity / NICU
                StingCommandHandler.SetExtraParam("Hc.Specialist.Mat.Maternity",  BoolStr(chkHcMatMaternity?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Mat.Nicu",       BoolStr(chkHcMatNicu?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Mat.NicuNrLimit",NumStr(sldHcMatNrLimit?.Value, 35));

                // HSDU
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hsdu.Room",     cmbHcHsduRoom?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hsdu.Wash",     BoolStr(chkHcHsduWashCheck?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hsdu.Pack",     BoolStr(chkHcHsduPackCheck?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hsdu.Sterile",  BoolStr(chkHcHsduSterileCheck?.IsChecked));

                // Dialysis
                StingCommandHandler.SetExtraParam("Hc.Specialist.Dial.Stations",      txtHcDialStations?.Text ?? "");
                StingCommandHandler.SetExtraParam("Hc.Specialist.Dial.RequireRoLoop", BoolStr(chkHcDialRoLoopRequired?.IsChecked));

                // Hyperbaric / Cytotoxic / IVF
                string hboMode = "HBO";
                if (rbHcHboCytotoxic?.IsChecked == true) hboMode = "Cytotoxic";
                else if (rbHcHboIvf?.IsChecked == true)  hboMode = "IVF";
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hbo.Mode",          hboMode);
                StingCommandHandler.SetExtraParam("Hc.Specialist.Hbo.RequireNfpa14", BoolStr(chkHcHboNfpaCh14?.IsChecked));

                // Workflow sub-tab
                StingCommandHandler.SetExtraParam("Hc.Wf.Preset",     SelectedComboTag(cmbHcWorkflowPreset, ""));
                StingCommandHandler.SetExtraParam("Hc.Wf.DryRun",     BoolStr(chkHcWfDryRun?.IsChecked));
                StingCommandHandler.SetExtraParam("Hc.Wf.StopOnFail", BoolStr(chkHcWfStopOnFail?.IsChecked));
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"SetHealthcareOptions: {ex.Message}");
            }
        }

        // ── Inline result strip ────────────────────────────────────────
        //
        // Commands (e.g. HealthcareValidatorReporter.Report) call
        // StingDockPanel.PushHcResult(...) to show a one-line summary in
        // the bottom strip without opening a TaskDialog. Thread-safe —
        // marshals to the WPF dispatcher.
        //
        // ragPct: 0 (all errors) → 100 (clean). Drives the bar fill width;
        //         >= 80 green, >= 50 amber, otherwise red.

        public static bool PushHcResult(string title, int total, int errors, int warnings, int info)
        {
            var inst = LastInstance;
            if (inst == null) return false;        // panel not open — caller falls back to TaskDialog

            // Compute a single % score: errors and warnings discount the total.
            // 1.0 per error, 0.5 per warning, 0 per info.
            double penalty = errors + 0.5 * warnings;
            double effective = total > 0 ? Math.Max(0.0, total - penalty) : 0.0;
            double pct = total > 0 ? Math.Round(100.0 * effective / total, 1) : 100.0;
            string ts = DateTime.Now.ToString("HH:mm");

            void Apply()
            {
                try
                {
                    if (inst.lblHcLastRun != null)
                    {
                        inst.lblHcLastRun.Text = $"Last run {ts} · {title} · {total} findings · errors {errors} · warnings {warnings} · info {info}";
                    }
                    if (inst.prgHcRag != null)
                    {
                        inst.prgHcRag.Value = pct;
                        Color c = pct >= 80 ? Color.FromRgb(76, 175, 80)    // green
                                : pct >= 50 ? Color.FromRgb(255, 152, 0)   // amber
                                            : Color.FromRgb(244, 67, 54);  // red
                        var brush = new SolidColorBrush(c);
                        brush.Freeze();
                        inst.prgHcRag.Foreground = brush;
                    }
                }
                catch (Exception ex)
                {
                    Core.StingLog.Warn($"PushHcResult Apply: {ex.Message}");
                }
            }

            try
            {
                if (inst.Dispatcher.CheckAccess()) Apply();
                else inst.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Apply));
                return true;
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"PushHcResult dispatch: {ex.Message}");
                return false;
            }
        }

        // ── Rich inline result panel ───────────────────────────────────
        //
        // Hosts a full StingResultPanel content tree (sections / metrics /
        // tables / RAG bars) inside the Expander on the sticky bottom bar.
        // Caller builds a StingResultPanel.Builder, this method swaps it in
        // and auto-expands. Returns false when the panel is not realised
        // (caller should fall back to TaskDialog or just to PushHcResult
        // for the 1-line summary).
        //
        // Parameter is `object` (not `StingResultPanel.Builder`) on purpose:
        // the WPF MarkupCompilePass1 runs a temporary "*_wpftmp" build that
        // references sibling .cs files via the obj/ assembly IL. On
        // incremental builds that IL can lag the .cs source by one build
        // (StingResultPanel still showing as internal in IL even after
        // bumping to public), tripping CS0051. Boxing through `object`
        // sidesteps the signature-accessibility check entirely. The cast
        // inside resolves against the live StingResultPanel.Builder type
        // because the method body is compiled fresh.
        public static bool PushHcResultPanel(object builderObj)
        {
            var inst = LastInstance;
            if (inst == null || builderObj == null) return false;
            var b = builderObj as StingResultPanel.Builder;
            if (b == null) return false;

            void Apply()
            {
                try
                {
                    var element = StingResultPanel.BuildInlineContent(b);
                    if (inst.hostHcResultPanel != null)
                        inst.hostHcResultPanel.Content = element;
                    if (inst.expHcResultDetails != null)
                        inst.expHcResultDetails.IsExpanded = true;
                }
                catch (Exception ex)
                {
                    Core.StingLog.Warn($"PushHcResultPanel Apply: {ex.Message}");
                }
            }

            try
            {
                if (inst.Dispatcher.CheckAccess()) Apply();
                else inst.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Apply));
                return true;
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"PushHcResultPanel dispatch: {ex.Message}");
                return false;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────
        private static string BoolStr(bool? v) => (v == true) ? "1" : "0";

        private static string NumStr(double? v, double fallback)
            => (v ?? fallback).ToString("F2", CultureInfo.InvariantCulture);

        private static double ParseDoubleOr(string s, double fallback)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        // Reads a ComboBox's selected ComboBoxItem.Tag (preferred) or
        // Content as a string. Falls back to the supplied default when
        // nothing is selected.
        private static string SelectedComboTag(ComboBox cb, string fallback)
        {
            if (cb?.SelectedItem is ComboBoxItem cbi)
            {
                if (cbi.Tag is string tag && !string.IsNullOrEmpty(tag)) return tag;
                if (cbi.Content is string txt && !string.IsNullOrEmpty(txt)) return txt;
            }
            else if (cb?.SelectedItem is string s)
            {
                return s;
            }
            return fallback;
        }

        // HEALTHCARE tab "Run" for the Specialist card — sets the button Tag
        // from the selected specialist kind, then routes through the universal
        // Cmd_Click dispatcher (matches old-lucid wiring).
        private void HcSpecialistRun_Click(object sender, RoutedEventArgs e)
        {
            if (btnHcSpecialistRun == null) return;
            string kind = SelectedComboTag(cmbHcSpecialistKind, "HybridOr");
            btnHcSpecialistRun.Tag = "Healthcare_" + kind;
            Cmd_Click(btnHcSpecialistRun, e);
        }
    }
}
