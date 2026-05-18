using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Button     = System.Windows.Controls.Button;
using ComboBox   = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Electrical Center dockable panel (Phase 177).
    /// 7 tabs: PNLS · CIRCTS · CALCS · CABLE · SLD · LITE · RPRT.
    /// All button clicks dispatch through <see cref="StingElectricalCommandHandler"/>
    /// so Revit API calls run on the Revit API thread.
    /// </summary>
    public partial class StingElectricalPanel : Page
    {
        public ObservableCollection<PanelRowViewModel>      PanelRows      { get; }
            = new ObservableCollection<PanelRowViewModel>();
        public ObservableCollection<CircuitRowViewModel>    CircuitRows    { get; }
            = new ObservableCollection<CircuitRowViewModel>();
        public ObservableCollection<SLDNodeViewModel>       SLDNodes       { get; }
            = new ObservableCollection<SLDNodeViewModel>();
        public ObservableCollection<ComplianceItemViewModel> ComplianceItems { get; }
            = new ObservableCollection<ComplianceItemViewModel>();
        public ObservableCollection<LightingRowViewModel>   LightingRows   { get; }
            = new ObservableCollection<LightingRowViewModel>();
        public ObservableCollection<LoadSummaryRowViewModel> LoadSummaryRows { get; }
            = new ObservableCollection<LoadSummaryRowViewModel>();
        public ObservableCollection<TemplateRuleViewModel>  TemplateRules  { get; }
            = new ObservableCollection<TemplateRuleViewModel>();
        public ObservableCollection<RoomTargetViewModel>    RoomTargets    { get; }
            = new ObservableCollection<RoomTargetViewModel>();
        public ObservableCollection<WireRefRowViewModel>    WireRefRows    { get; }
            = new ObservableCollection<WireRefRowViewModel>();
        public ObservableCollection<ConduitWireRowViewModel> ConduitWires  { get; }
            = new ObservableCollection<ConduitWireRowViewModel>();

        // Phase 178 — observable collections for the newly-unlocked grids
        public ObservableCollection<FeederData>      Feeders      { get; } = new ObservableCollection<FeederData>();
        public ObservableCollection<FaultData>       FaultResults { get; } = new ObservableCollection<FaultData>();
        public ObservableCollection<EmergAuditRow>   EmergAudit   { get; } = new ObservableCollection<EmergAuditRow>();
        public ObservableCollection<LpdRow>          LpdRows      { get; } = new ObservableCollection<LpdRow>();

        public string SelectedStandard { get; private set; } = "BS7671";
        public string SelectedVoltage  { get; private set; } = "415V3Ph";
        public string CalcStandard     { get; private set; } = "BS7671";

        private static StingElectricalPanel _instance;
        public static StingElectricalPanel Instance => _instance;

        public StingElectricalPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }
            _instance = this;

            PanelGrid.ItemsSource         = PanelRows;
            CircuitGrid.ItemsSource       = CircuitRows;
            SLDTree.ItemsSource           = SLDNodes;
            ComplianceList.ItemsSource    = ComplianceItems;
            LightingGrid.ItemsSource      = LightingRows;
            LoadSummaryGrid.ItemsSource   = LoadSummaryRows;
            TemplateRuleGrid.ItemsSource  = TemplateRules;
            RoomTargetGrid.ItemsSource    = RoomTargets;
            WireRefGrid.ItemsSource       = WireRefRows;
            ConduitWireGrid.ItemsSource   = ConduitWires;
            // Phase 178 — bind the newly-unlocked grids
            try { FeederResultsGrid.ItemsSource = Feeders;       } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { FaultResultsGrid.ItemsSource  = FaultResults;  } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { EmergAuditGrid.ItemsSource    = EmergAudit;    } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { LpdGrid.ItemsSource           = LpdRows;       } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            try { StingElectricalCommandHandler.Initialize(this); }
            catch (Exception ex) { Core.StingLog.Error("ElectricalPanel handler init", ex); }

            UpdateStatus("Ready");
        }

        public void UpdateStatus(string text)
        {
            try { Dispatcher.Invoke(() => txtElecStatus.Text = text ?? ""); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // ── Unified click dispatcher ─────────────────────────────────────
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                StingElectricalCommandHandler.Instance?.SetCommand(tag);
                UpdateStatus($"Running: {tag}");
            }
        }

        private void cmbStandard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStandard?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedStandard = tag;
        }

        private void cmbVoltage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbVoltage?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedVoltage = tag;
        }

        private void CalcStd_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag) CalcStandard = tag;
        }

        private void txtPanelFilter_TextChanged(object sender, TextChangedEventArgs e) => RefreshPanelFilter();
        private void txtCircuitFilter_TextChanged(object sender, TextChangedEventArgs e) => RefreshCircuitFilter();
        private void txtSLDFind_TextChanged(object sender, TextChangedEventArgs e) { /* tree filter is left for Phase 178 */ }
        private void cmbCircuitPanel_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshCircuitFilter();

        private void RefreshPanelFilter()
        {
            string f = (txtPanelFilter?.Text ?? "").Trim().ToLowerInvariant();
            foreach (var row in PanelRows)
                row.IsHidden = !string.IsNullOrEmpty(f) &&
                               (row.Name ?? "").ToLowerInvariant().IndexOf(f) < 0;
            // We don't filter the underlying ObservableCollection; the user sees
            // all rows. Hooks below let commands inspect IsHidden/IsSelected.
        }

        private void RefreshCircuitFilter()
        {
            string panel = (cmbCircuitPanel?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            string f = (txtCircuitFilter?.Text ?? "").Trim().ToLowerInvariant();
            foreach (var row in CircuitRows)
            {
                bool hide = false;
                if (panel != "" && panel != "ALL PANELS" && row.PanelName != panel) hide = true;
                if (!hide && !string.IsNullOrEmpty(f))
                {
                    string blob = $"{row.CircuitNumber} {row.Description} {row.WireSize}".ToLowerInvariant();
                    if (blob.IndexOf(f) < 0) hide = true;
                }
                if (chkCircuitSpare?.IsChecked == false && row.IsSpare) hide = true;
                if (chkCircuitSpace?.IsChecked == false && row.IsSpace) hide = true;
                if (chkCircuitActive?.IsChecked == false && !row.IsSpare && !row.IsSpace) hide = true;
                row.IsHidden = hide;
            }
        }

        private void SLDTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is SLDNodeViewModel node)
            {
                txtSLDSelected.Text   = $"Selected: {node.Label}";
                txtSLDConnLoad.Text   = $"Connected load: {node.LoadKW:0.0} kW";
                txtSLDDmdLoad.Text    = $"Demand load: {node.DemandKW:0.0} kW";
                txtSLDFeederA.Text    = $"Feeder rating: {node.RatingA} A";
                txtSLDFeederUtil.Text = $"Feeder utilisation: {node.UtilisationPct:0}%";
                txtSLDVDFeeder.Text   = $"VD feeder: {node.VDFeederPct:0.0}%";
                txtSLDFault.Text      = $"Fault level: {node.FaultLevelKA:0.0} kA";
            }
        }

        private void WireRef_Changed(object sender, SelectionChangedEventArgs e)
        {
            try { StingElectricalCommandHandler.Instance?.RefreshWireRefTable(this); }
            catch { /* command handler may not be ready yet */ }
        }

        // ── Phase 178 — new event handlers ───────────────────────────────

        private void FeederDiversityMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string tag = ((FeederDiversityMode?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "None";
                if (FeederDiversityPct != null)
                    FeederDiversityPct.IsEnabled = tag == "User";
                StingElectricalCommandHandler.CurrentFeederSettings = ReadFeederSettings();
            }
            catch (Exception ex) { Core.StingLog.Warn($"FeederDiversityMode_Changed: {ex.Message}"); }
        }

        private StingTools.Commands.Electrical.FeederSizing.FeederSettingsSnapshot ReadFeederSettings()
        {
            var s = new StingTools.Commands.Electrical.FeederSizing.FeederSettingsSnapshot
            {
                DerateFactor = ParseDouble(FeederDerateFactor?.Text, 0.8),
                DiversityMode = ((FeederDiversityMode?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "None",
                DiversityPct = ParseDouble(FeederDiversityPct?.Text, 100),
                InstallMethod = ((FeederInstallMethod?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "C",
                VDLimitPct = 2.0
            };
            return s;
        }

        private void UtilityFaultKa_TextChanged(object sender, TextChangedEventArgs e)
        {
            StingElectricalCommandHandler.CurrentUtilityFaultKa = ParseDouble(UtilityFaultKa?.Text, 25.0);
        }

        private void RiserOptions_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                StingElectricalCommandHandler.CurrentRiserOptions = new StingTools.Commands.SLD.RiserOptions
                {
                    Layout = ((RiserLayout?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Horizontal",
                    ShowFaultKa = RiserShowFaultKa?.IsChecked == true,
                    ShowFeederCsa = RiserShowFeederCsa?.IsChecked == true,
                    ShowLoadingPct = RiserShowLoadingPct?.IsChecked == true
                };
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private void SheetPlacementMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            StingElectricalCommandHandler.CurrentSheetPlacementMode =
                ((cmbSheetPlacementMode?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "GuidedManual";
        }

        private void LpdStandard_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string tag = ((LpdStandard?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "ASHRAE_90_1_2019";
                StingElectricalCommandHandler.CurrentLpdStandard = tag;
                if (LpdCustomRow != null)
                    LpdCustomRow.Visibility = tag == "Custom" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                StingElectricalCommandHandler.CurrentLpdCustomLimit = ParseDouble(LpdCustomLimit?.Text, 0);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private void ConduitAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sizeItem = cmbConduitWireSize?.SelectedItem as ComboBoxItem;
                string size = sizeItem?.Tag?.ToString() ?? "";
                if (!int.TryParse((txtConduitWireQty?.Text ?? "1").Trim(), out int qty)) qty = 1;
                if (qty < 1) qty = 1;
                ConduitWires.Add(new ConduitWireRowViewModel { Size = size, Qty = qty });
                btnConduitCalc.IsEnabled = ConduitWires.Count > 0;
            }
            catch (Exception ex) { Core.StingLog.Warn($"ConduitAdd: {ex.Message}"); }
        }

        /// <summary>
        /// Push a fresh data snapshot into the panel ViewModels. Called by the
        /// command handler after each command completes so the grids stay in
        /// sync with the model. Marshals onto the WPF dispatcher thread.
        /// </summary>
        public void RefreshFromData(ElectricalPanelSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (snapshot.Panels != null) PopulatePanels(snapshot.Panels);
                    if (snapshot.Circuits != null) PopulateCircuits(snapshot.Circuits);
                    if (snapshot.SLDRoot != null) PopulateSLD(snapshot.SLDRoot);
                    if (snapshot.LoadSummary != null) PopulateLoadSummary(snapshot.LoadSummary);
                    if (snapshot.TemplateRules != null) PopulateTemplateRules(snapshot.TemplateRules);
                    if (snapshot.LightingRows != null) PopulateLighting(snapshot.LightingRows);
                    if (snapshot.RoomTargets != null) PopulateRoomTargets(snapshot.RoomTargets);
                    if (snapshot.WireRefRows != null) PopulateWireRef(snapshot.WireRefRows);
                    if (snapshot.ComplianceItems != null) PopulateCompliance(snapshot.ComplianceItems);
                    // Phase 178 grids
                    if (snapshot.Feeders != null) { Feeders.Clear(); foreach (var r in snapshot.Feeders) Feeders.Add(r); }
                    if (snapshot.FaultResults != null) { FaultResults.Clear(); foreach (var r in snapshot.FaultResults) FaultResults.Add(r); }
                    if (snapshot.EmergAudit != null) { EmergAudit.Clear(); foreach (var r in snapshot.EmergAudit) EmergAudit.Add(r); }
                    if (snapshot.LpdRows != null) { LpdRows.Clear(); foreach (var r in snapshot.LpdRows) LpdRows.Add(r); }
                    if (!string.IsNullOrEmpty(snapshot.PhaseSummary))
                        txtPhaseSummary.Text = snapshot.PhaseSummary;
                    if (!string.IsNullOrEmpty(snapshot.ImbalanceText))
                        txtImbalance.Text = snapshot.ImbalanceText;
                    if (snapshot.Standard != null) SelectedStandard = snapshot.Standard;
                });
            }
            catch (Exception ex) { Core.StingLog.Warn($"RefreshFromData: {ex.Message}"); }
        }

        public void RefreshCableResult(StingTools.Commands.Electrical.CableSizer.CableSizeResult r)
        {
            if (r == null) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    txtCblResultCurrent.Text = $"Design current: {r.DesignCurrentA:0.0} A";
                    txtCblResultSize.Text    = $"Min cable size: {r.CsaLabel}";
                    txtCblResultVD.Text      = $"Actual VD: {r.ActualVoltDropPct:0.00}% " +
                        (r.VDCompliant ? "✅" : "⚠");
                    txtCblResultBreaker.Text = $"Breaker: {r.ProposedBreakerA} A";
                    txtCblResultNote.Text    = string.IsNullOrEmpty(r.Warning)
                        ? r.DerivationNote
                        : $"{r.Warning} — {r.DerivationNote}";
                    btnCblApply.IsEnabled = r.RecommendedCsaMm2 > 0 && string.IsNullOrEmpty(r.Warning);
                });
            }
            catch (Exception ex) { Core.StingLog.Warn($"RefreshCableResult: {ex.Message}"); }
        }

        public void RefreshConduitFillResult(string text, bool exceeds)
        {
            try { Dispatcher.Invoke(() => txtConduitFillResult.Text = text + (exceeds ? "  ⚠" : "  ✅")); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        public void RefreshBalancePreview(string before, string after)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(before)) txtBalanceBefore.Text = before;
                    if (!string.IsNullOrEmpty(after))  txtBalanceAfter.Text  = after;
                });
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        public void RefreshDescriptionPreview(string preview)
        {
            try { Dispatcher.Invoke(() => txtDescPreview.Text = preview ?? "(no preview)"); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // ── Snapshot collectors used by command handlers ─────────────────

        public CableSizerInputSnapshot ReadCableSizerInputs()
        {
            try
            {
                double load = ParseDouble(txtCblLoad?.Text, 5.5);
                double pf   = ParseDouble(txtCblPF?.Text, 0.85);
                double len  = ParseDouble(txtCblLength?.Text, 35);
                double vd   = ParseDouble(txtCblVDLimit?.Text, 3.0);
                string method = ((cmbCblMethod?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "C";
                string mat    = ((cmbCblMaterial?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Cu";
                string ins    = ((cmbCblInsulation?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "XLPE90";
                string std    = ((cmbCblStandard?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "BS7671";
                string vTag   = ((cmbCblVoltage?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "240,1";
                var parts = vTag.Split(',');
                double v = ParseDouble(parts.ElementAtOrDefault(0), 240);
                int phases = (int)ParseDouble(parts.ElementAtOrDefault(1), 1);
                return new CableSizerInputSnapshot
                {
                    LoadKW = load, VoltageV = v, Phases = phases, PowerFactor = pf,
                    LengthM = len, VDLimitPct = vd,
                    InstallMethod = method, Material = mat, Insulation = ins, Standard = std
                };
            }
            catch (Exception ex)
            {
                Core.StingLog.Warn($"ReadCableSizerInputs: {ex.Message}");
                return new CableSizerInputSnapshot();
            }
        }

        public ConduitFillInputSnapshot ReadConduitFillInputs()
        {
            string conduit = ((cmbConduitSize?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "PVC_BS4568_25mm";
            double max = ParseDouble(txtConduitMaxFill?.Text, 45);
            var wires = ConduitWires.Select(w => (ParseDouble(w.Size, 2.5), w.Qty)).ToList();
            return new ConduitFillInputSnapshot { ConduitKey = conduit, MaxFillPct = max, Wires = wires };
        }

        public DescriptionAutoFillOptions ReadDescriptionOptions()
        {
            return new DescriptionAutoFillOptions
            {
                Source1 = ComboText(cmbDescSrc1),
                Source2 = ComboText(cmbDescSrc2),
                Source3 = ComboText(cmbDescSrc3),
                Separator = txtDescSeparator?.Text ?? " — ",
                TitleCase = chkDescTitleCase?.IsChecked == true,
            };
        }

        public PanelParamsSnapshot ReadPanelParams()
        {
            var sel = PanelGrid?.SelectedItem as PanelRowViewModel;
            return new PanelParamsSnapshot
            {
                PanelName = sel?.Name ?? "",
                MainBreakerA = txtMainBreaker?.Text ?? "",
                FedFrom = (cmbFedFrom?.Text) ?? "",
                Location = txtLocation?.Text ?? "",
                IpRating = ((cmbIpRating?.SelectedItem as ComboBoxItem)?.Content?.ToString()) ?? "",
                Manufacturer = txtManufacturer?.Text ?? "",
                FaultKA = txtFaultKA?.Text ?? "",
                Enclosure = ((cmbEnclosure?.SelectedItem as ComboBoxItem)?.Content?.ToString()) ?? ""
            };
        }

        public VDOptionsSnapshot ReadVDOptions()
        {
            return new VDOptionsSnapshot
            {
                BranchLimitPct = ParseDouble(txtVDBranch?.Text, 3.0),
                FeederLimitPct = ParseDouble(txtVDFeeder?.Text, 2.0),
                Material = ((cmbConductor?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Cu",
                OperatingTempC = ParseDouble(((cmbWireTemp?.SelectedItem as ComboBoxItem)?.Tag as string), 70.0),
                Standard = CalcStandard
            };
        }

        public BreakerOptionsSnapshot ReadBreakerOptions()
        {
            return new BreakerOptionsSnapshot
            {
                Standard = ((cmbBreakerStd?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "BS_MCB",
                ContinuousFactor = chkContinuous?.IsChecked == true
            };
        }

        public BalanceOptionsSnapshot ReadBalanceOptions()
        {
            string algoText = ((cmbBalanceAlgo?.SelectedItem as ComboBoxItem)?.Content?.ToString()) ?? "Greedy (largest first)";
            return new BalanceOptionsSnapshot
            {
                Algorithm = algoText.StartsWith("Greedy") ? "Greedy" :
                            algoText.StartsWith("Sequential") ? "Sequential" : "Manual",
                RespectGrouped = chkRespectGrouped?.IsChecked == true,
                PreviewFirst = chkBalancePreview?.IsChecked == true
            };
        }

        // ── Populate helpers ─────────────────────────────────────────────
        private void PopulatePanels(IEnumerable<PanelData> panels)
        {
            PanelRows.Clear();
            int ok = 0, drift = 0, missing = 0, total = 0;
            foreach (var p in panels)
            {
                total++;
                if (p.ScheduleStatus == "OK") ok++;
                else if (p.ScheduleStatus == "Drifted") drift++;
                else if (p.ScheduleStatus == "Missing") missing++;
                PanelRows.Add(new PanelRowViewModel
                {
                    IsSelected = false,
                    Name = p.Name,
                    Voltage = p.Voltage,
                    Phase = p.Phase,
                    Ways = p.Ways,
                    ScheduleStatus = p.ScheduleStatus,
                    ScheduleStatusText = StatusToText(p.ScheduleStatus),
                    PanelDataRef = p
                });
            }
            txtPanelSummary.Text = $"{total} panels | {ok} OK | {drift} drift | {missing} missing";

            // Refresh fed-from combo
            cmbFedFrom.Items.Clear();
            foreach (var n in PanelRows.Select(r => r.Name).Distinct()) cmbFedFrom.Items.Add(n);

            // Refresh circuit panel filter combo
            cmbCircuitPanel.Items.Clear();
            cmbCircuitPanel.Items.Add(new ComboBoxItem { Content = "ALL PANELS", IsSelected = true });
            foreach (var n in PanelRows.Select(r => r.Name).Distinct())
                cmbCircuitPanel.Items.Add(new ComboBoxItem { Content = n });
        }

        private static string StatusToText(string s) => s switch
        {
            "OK" => "✅ Created",
            "Drifted" => "⚠ Drifted",
            "Missing" => "❌ Missing",
            "Skipped" => "⊘ Skipped",
            _ => s ?? ""
        };

        private void PopulateCircuits(IEnumerable<CircuitData> circuits)
        {
            CircuitRows.Clear();
            double aKW = 0, bKW = 0, cKW = 0;
            foreach (var c in circuits)
            {
                CircuitRows.Add(new CircuitRowViewModel
                {
                    IsSelected = false,
                    PanelName = c.PanelName,
                    CircuitNumber = c.CircuitNumber,
                    Description = c.Description,
                    Phase = c.Phase,
                    CurrentA = c.CurrentA,
                    LoadKW = c.LoadKW,
                    VoltDropPct = c.VoltDropPct,
                    WireSize = c.WireSize,
                    LengthM = c.LengthM,
                    IsSpare = c.IsSpare,
                    IsSpace = c.IsSpace,
                    CircuitDataRef = c
                });
                if (!c.IsSpare && !c.IsSpace)
                {
                    if (c.Phase == "A") aKW += c.LoadKW;
                    else if (c.Phase == "B") bKW += c.LoadKW;
                    else if (c.Phase == "C") cKW += c.LoadKW;
                }
            }
            txtPhaseSummary.Text = $"Phase A: {aKW:0.0} kW   B: {bKW:0.0} kW   C: {cKW:0.0} kW";
            double total = aKW + bKW + cKW;
            double max = Math.Max(aKW, Math.Max(bKW, cKW));
            double min = Math.Min(aKW, Math.Min(bKW, cKW));
            double imb = max - min;
            double pct = total > 0 ? imb / (total / 3.0) * 100.0 : 0;
            txtImbalance.Text = $"Imbalance: {imb:0.0} kW ({pct:0}%)";
        }

        private void PopulateSLD(StingTools.Core.SLD.SLDNode root)
        {
            SLDNodes.Clear();
            if (root == null) return;
            SLDNodes.Add(SLDNodeViewModel.From(root));
        }

        private void PopulateLoadSummary(IEnumerable<LoadSummaryRow> rows)
        {
            LoadSummaryRows.Clear();
            foreach (var r in rows)
                LoadSummaryRows.Add(new LoadSummaryRowViewModel
                {
                    Name = r.Name,
                    ConnectedKW = $"{r.ConnectedKW:0.0}",
                    DemandKW = $"{r.DemandKW:0.0}",
                    SparePct = $"{r.SparePct:0}%"
                });
        }

        private void PopulateTemplateRules(IEnumerable<TemplateRuleRow> rows)
        {
            TemplateRules.Clear();
            foreach (var r in rows)
                TemplateRules.Add(new TemplateRuleViewModel
                { Priority = r.Priority, Pattern = r.Pattern, Template = r.Template });
        }

        private void PopulateLighting(IEnumerable<LightingRow> rows)
        {
            LightingRows.Clear();
            double normal = 0, emerg = 0;
            foreach (var r in rows)
            {
                LightingRows.Add(new LightingRowViewModel
                {
                    IsSelected = false, FamilyType = r.FamilyType,
                    Watts = r.Watts, Qty = r.Qty,
                    Circuit = r.Circuit, LmPerW = r.LmPerW
                });
                double tot = r.Watts * r.Qty;
                if ((r.Circuit ?? "").IndexOf("emerg", StringComparison.OrdinalIgnoreCase) >= 0)
                    emerg += tot;
                else normal += tot;
            }
            txtLiteSummary.Text = $"Total: {normal:0} W normal | {emerg:0} W emergency";
        }

        private void PopulateRoomTargets(IEnumerable<RoomTargetRow> rows)
        {
            RoomTargets.Clear();
            foreach (var r in rows)
                RoomTargets.Add(new RoomTargetViewModel
                {
                    Room = r.Room, TargetLx = r.TargetLx,
                    EstimatedLx = r.EstimatedLx, Delta = r.Delta
                });
        }

        private void PopulateWireRef(IEnumerable<WireRefRow> rows)
        {
            WireRefRows.Clear();
            foreach (var r in rows)
                WireRefRows.Add(new WireRefRowViewModel
                {
                    Size = r.Size, Imax1Ph = r.Imax1Ph,
                    Imax3Ph = r.Imax3Ph, MohmPerM = r.MohmPerM
                });
        }

        private void PopulateCompliance(IEnumerable<ComplianceItemViewModel> items)
        {
            ComplianceItems.Clear();
            foreach (var it in items) ComplianceItems.Add(it);
        }

        // ── Tiny utils ───────────────────────────────────────────────────
        private static double ParseDouble(string s, double fallback)
        {
            if (double.TryParse((s ?? "").Trim(), out double v)) return v;
            return fallback;
        }
        private static string ComboText(ComboBox cb) =>
            (cb?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        public string GetWireRefMaterial() =>
            ((cmbWireRefMaterial?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Cu";
        public string GetWireRefInsulation() =>
            ((cmbWireRefInsulation?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "XLPE90";
        public string GetWireRefMethod() =>
            ((cmbWireRefMethod?.SelectedItem as ComboBoxItem)?.Tag as string) ?? "C";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ViewModels
    // ─────────────────────────────────────────────────────────────────────

    public class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class PanelRowViewModel : NotifyBase
    {
        public bool IsSelected { get; set; }
        public bool IsHidden { get; set; }
        public string Name { get; set; }
        public string Voltage { get; set; }
        public string Phase { get; set; }
        public int Ways { get; set; }
        public string ScheduleStatus { get; set; }
        public string ScheduleStatusText { get; set; }
        public PanelData PanelDataRef { get; set; }
    }

    public class CircuitRowViewModel : NotifyBase
    {
        public bool IsSelected { get; set; }
        public bool IsHidden { get; set; }
        public string PanelName { get; set; }
        public string CircuitNumber { get; set; }
        public string Description { get; set; }
        public string Phase { get; set; }
        public double CurrentA { get; set; }
        public double LoadKW { get; set; }
        public double VoltDropPct { get; set; }
        public string WireSize { get; set; }
        public double LengthM { get; set; }
        public bool IsSpare { get; set; }
        public bool IsSpace { get; set; }
        public CircuitData CircuitDataRef { get; set; }

        public string CurrentDisplay => CurrentA > 0 ? $"{CurrentA:0.0}" : "—";
        public string LoadDisplay    => LoadKW    > 0 ? $"{LoadKW:0.00}" : "—";
        public string VDDisplay      => VoltDropPct > 0 ? $"{VoltDropPct:0.0}" : "—";
        public string LengthDisplay  => LengthM   > 0 ? $"{LengthM:0.0}" : "—";
    }

    public class SLDNodeViewModel : NotifyBase
    {
        public string Icon { get; set; } = "🗂";
        public string Label { get; set; }
        public string Rating { get; set; }
        public double LoadKW { get; set; }
        public double DemandKW { get; set; }
        public int RatingA { get; set; }
        public double UtilisationPct { get; set; }
        public double VDFeederPct { get; set; }
        public double FaultLevelKA { get; set; }
        public ObservableCollection<SLDNodeViewModel> Children { get; }
            = new ObservableCollection<SLDNodeViewModel>();
        public bool IsExpanded { get; set; } = true;
        public long RevitElementIdValue { get; set; }
        public string LoadDisplay => LoadKW > 0 ? $"{LoadKW:0.0} kW" : "";

        public static SLDNodeViewModel From(StingTools.Core.SLD.SLDNode n)
        {
            if (n == null) return null;
            var vm = new SLDNodeViewModel
            {
                Label = n.Label ?? "(unnamed)",
                Rating = string.IsNullOrEmpty(n.Rating) ? "" : $"[{n.Rating}]",
                LoadKW = n.LoadKW,
                RatingA = ParseRatingA(n.Rating),
                Icon = n.IsPanel ? (n.HierarchyLevel == 0 ? "⚡" : "🗂") : "⚙",
                RevitElementIdValue = n.ElementId?.Value ?? 0
            };
            foreach (var child in n.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                vm.Children.Add(From(child));
            return vm;
        }

        private static int ParseRatingA(string rating)
        {
            if (string.IsNullOrEmpty(rating)) return 0;
            string digits = new string(rating.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out int v) ? v : 0;
        }
    }

    public class ComplianceItemViewModel
    {
        public string Icon { get; set; } = "✅";
        public string Message { get; set; }
        public string Severity { get; set; } = "info";
    }

    public class LightingRowViewModel : NotifyBase
    {
        public bool IsSelected { get; set; }
        public string FamilyType { get; set; }
        public double Watts { get; set; }
        public int Qty { get; set; }
        public string Circuit { get; set; }
        public double LmPerW { get; set; }
    }

    public class LoadSummaryRowViewModel
    {
        public string Name { get; set; }
        public string ConnectedKW { get; set; }
        public string DemandKW { get; set; }
        public string SparePct { get; set; }
    }

    public class TemplateRuleViewModel
    {
        public int Priority { get; set; }
        public string Pattern { get; set; }
        public string Template { get; set; }
    }

    public class RoomTargetViewModel
    {
        public string Room { get; set; }
        public string TargetLx { get; set; }
        public string EstimatedLx { get; set; }
        public string Delta { get; set; }
    }

    public class WireRefRowViewModel
    {
        public string Size { get; set; }
        public string Imax1Ph { get; set; }
        public string Imax3Ph { get; set; }
        public string MohmPerM { get; set; }
    }

    public class ConduitWireRowViewModel : NotifyBase
    {
        public string Size { get; set; }
        public int Qty { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Plain-data DTOs used by the snapshot push pattern
    // ─────────────────────────────────────────────────────────────────────

    public class PanelData
    {
        public Autodesk.Revit.DB.ElementId Id { get; set; }
        public string Name { get; set; }
        public string Voltage { get; set; }
        public string Phase { get; set; }
        public int Ways { get; set; }
        public string ScheduleStatus { get; set; } = "OK";
        public string MainBreakerA { get; set; }
        public string FedFrom { get; set; }
        public string Location { get; set; }
    }

    public class CircuitData
    {
        public Autodesk.Revit.DB.ElementId Id { get; set; }
        public string PanelName { get; set; }
        public string CircuitNumber { get; set; }
        public string Description { get; set; }
        public string Phase { get; set; }
        public double CurrentA { get; set; }
        public double LoadKW { get; set; }
        public double VoltDropPct { get; set; }
        public string WireSize { get; set; }
        public double LengthM { get; set; }
        public bool IsSpare { get; set; }
        public bool IsSpace { get; set; }
    }

    public class LoadSummaryRow
    {
        public string Name { get; set; }
        public double ConnectedKW { get; set; }
        public double DemandKW { get; set; }
        public double SparePct { get; set; }
    }

    public class TemplateRuleRow { public int Priority; public string Pattern; public string Template; }
    public class LightingRow
    {
        public string FamilyType; public double Watts; public int Qty;
        public string Circuit; public double LmPerW;
    }
    public class RoomTargetRow { public string Room, TargetLx, EstimatedLx, Delta; }
    public class WireRefRow    { public string Size, Imax1Ph, Imax3Ph, MohmPerM; }

    public class ElectricalPanelSnapshot
    {
        public List<PanelData>            Panels;
        public List<CircuitData>          Circuits;
        public StingTools.Core.SLD.SLDNode SLDRoot;
        public List<LoadSummaryRow>       LoadSummary;
        public List<TemplateRuleRow>      TemplateRules;
        public List<LightingRow>          LightingRows;
        public List<RoomTargetRow>        RoomTargets;
        public List<WireRefRow>           WireRefRows;
        public List<ComplianceItemViewModel> ComplianceItems;
        public string                     PhaseSummary;
        public string                     ImbalanceText;
        public string                     Standard;
        // Phase 178 additions
        public List<FeederData>           Feeders;
        public List<FaultData>            FaultResults;
        public List<ConduitFillData>      ConduitFills;
        public List<EmergAuditRow>        EmergAudit;
        public List<LpdRow>               LpdRows;
    }

    // ── Phase 178 DTOs (with bound display properties) ────────────────
    public class FeederData
    {
        public string PanelName        { get; set; }
        public double DemandKW         { get; set; }
        public double FeederCurrentA   { get; set; }
        public double ProposedCsaMm2   { get; set; }
        public double VoltDropPct      { get; set; }
        public double ProposedRatingA  { get; set; }
        public string Status           { get; set; }
        public string DemandDisplay  => $"{DemandKW:0.0}";
        public string CurrentDisplay => $"{FeederCurrentA:0.0}";
        public string CsaDisplay     => $"{ProposedCsaMm2:0.#}mm²";
        public string VDDisplay      => $"{VoltDropPct:0.00}";
        public string RatingDisplay  => $"{ProposedRatingA:0}A";
    }
    public class FaultData
    {
        public string PanelName     { get; set; }
        public string Voltage       { get; set; }
        public double FeederCsaMm2  { get; set; }
        public double ZtotalMohm    { get; set; }
        public double FaultKa       { get; set; }
        public double AicRequiredKa { get; set; }
        public string Status        { get; set; }
        public string CsaDisplay   => $"{FeederCsaMm2:0.#}";
        public string ZDisplay     => $"{ZtotalMohm:0.0}";
        public string FaultDisplay => $"{FaultKa:0.00}";
        public string AicDisplay   => $"{AicRequiredKa:0}";
    }
    public class ConduitFillData
    {
        public Autodesk.Revit.DB.ElementId ConduitId { get; set; }
        public string ConduitName { get; set; }
        public double FillPct     { get; set; }
        public double LimitPct    { get; set; }
        public bool   Passes      { get; set; }
    }
    public class EmergAuditRow
    {
        public string RoomName       { get; set; }
        public string OccupancyType  { get; set; }
        public int    NormalCircuits { get; set; }
        public int    EmergCircuits  { get; set; }
        public bool   SameCircuit    { get; set; }
        public string Status         { get; set; }
        public string SameCircuitDisplay => SameCircuit ? "yes" : "no";
    }
    public class LpdRow
    {
        public string RoomName    { get; set; }
        public double AreaM2      { get; set; }
        public double TotalW      { get; set; }
        public double WPerM2      { get; set; }
        public double LimitWPerM2 { get; set; }
        public string Status      { get; set; }
        public string AreaDisplay  => $"{AreaM2:0.0}";
        public string WattsDisplay => $"{TotalW:0}";
        public string LpdDisplay   => $"{WPerM2:0.00}";
        public string LimitDisplay => $"{LimitWPerM2:0.00}";
    }

    public class CableSizerInputSnapshot
    {
        public double LoadKW; public double VoltageV; public int Phases;
        public double PowerFactor; public double LengthM; public double VDLimitPct;
        public string InstallMethod, Material, Insulation, Standard;
    }

    public class ConduitFillInputSnapshot
    {
        public string ConduitKey;
        public double MaxFillPct;
        public List<(double csaMm2, int qty)> Wires;
    }

    public class DescriptionAutoFillOptions
    {
        public string Source1, Source2, Source3, Separator;
        public bool TitleCase;
    }

    public class PanelParamsSnapshot
    {
        public string PanelName, MainBreakerA, FedFrom, Location, IpRating,
            Manufacturer, FaultKA, Enclosure;
    }

    public class VDOptionsSnapshot
    {
        public double BranchLimitPct, FeederLimitPct, OperatingTempC;
        public string Material, Standard;
    }

    public class BreakerOptionsSnapshot { public string Standard; public bool ContinuousFactor; }
    public class BalanceOptionsSnapshot
    {
        public string Algorithm; public bool RespectGrouped; public bool PreviewFirst;
    }
}
