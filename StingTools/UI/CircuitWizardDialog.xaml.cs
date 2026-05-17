using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.CircuitWizard;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Modal WPF wizard for proposing and creating circuits in batch. Reads
    /// the document inside the constructor (read-only), drives
    /// <see cref="CircuitWizardEngine"/> for proposals, and on confirm hands
    /// the editable list to <see cref="CircuitWizardCommand"/> via static
    /// PendingCircuits / PendingPanelName.
    /// </summary>
    public partial class CircuitWizardDialog : Window
    {
        public ObservableCollection<ProposedCircuitVm> Proposals { get; }
            = new ObservableCollection<ProposedCircuitVm>();
        public ObservableCollection<UnconnectedElement> UnconnectedElements { get; }
            = new ObservableCollection<UnconnectedElement>();

        private readonly UIApplication _app;
        private readonly Document _doc;
        private WireTableSet _wireTables;

        public CircuitWizardDialog(UIApplication app)
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            _app = app;
            _doc = app?.ActiveUIDocument?.Document;
            ProposalGrid.ItemsSource = Proposals;
            UnconnectedGrid.ItemsSource = UnconnectedElements;

            try
            {
                _wireTables = WireTableSet.Load(StingToolsApp.DataPath);
                PopulatePanels();
                RefreshUnconnectedElements();
            }
            catch (Exception ex) { StingLog.Warn($"CircuitWizard ctor: {ex.Message}"); }
            UpdateCreateButton();
        }

        private void PopulatePanels()
        {
            if (_doc == null) return;
            cmbWzPanel.Items.Clear();
            foreach (var p in new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>())
            {
                cmbWzPanel.Items.Add(p.Name ?? "");
            }
            if (cmbWzPanel.Items.Count > 0) cmbWzPanel.SelectedIndex = 0;
        }

        private void RefreshUnconnectedElements()
        {
            UnconnectedElements.Clear();
            if (_doc == null) return;
            try
            {
                var roomIndex = SpatialAutoDetect.BuildRoomIndex(_doc);
                string projLoc = SpatialAutoDetect.DetectProjectLoc(_doc) ?? "";

                var all = new List<FamilyInstance>();
                all.AddRange(new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>());
                all.AddRange(new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>());

                foreach (var fi in all)
                {
                    if (HasCircuit(fi)) continue;
                    var (loadVA, voltage, poles) = ReadConnectorData(fi);
                    string family = fi.Symbol?.FamilyName ?? fi.Name ?? "";
                    string cat = fi.Category?.Name ?? "";
                    var ue = new UnconnectedElement
                    {
                        Id = fi.Id,
                        FamilyName = family,
                        Mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                        RoomName = SpatialAutoDetect.DetectLoc(_doc, fi, roomIndex, projLoc) ?? "",
                        LoadVA = loadVA,
                        VoltageV = voltage > 0 ? voltage : 230.0,
                        RequiredPoles = poles > 0 ? poles : 1,
                        LoadClass = CircuitWizardEngine.ClassifyLoad(family, cat)
                    };
                    var pt = (fi.Location as LocationPoint)?.Point;
                    if (pt != null) { ue.X = pt.X; ue.Y = pt.Y; ue.Z = pt.Z; }
                    UnconnectedElements.Add(ue);
                }
            }
            catch (Exception ex) { StingLog.Warn($"RefreshUnconnectedElements: {ex.Message}"); }
        }

        private static bool HasCircuit(FamilyInstance fi)
        {
            try
            {
                var sets = fi.MEPModel?.GetElectricalSystems();
                return sets != null && sets.Count > 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static (double loadVA, double voltage, int poles) ReadConnectorData(FamilyInstance fi)
        {
            double load = 0, voltageV = 0;
            int poles = 1;
            try
            {
                load = fi.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0;
                var cm = fi.MEPModel?.ConnectorManager;
                if (cm != null)
                {
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c.Domain != Domain.DomainElectrical) continue;
                        try
                        {
                            // GetMEPConnectorInfo() returns the connector's MEP info; voltage
                            // and pole counts aren't exposed via a stable cross-version API on
                            // it, so we only call it for forward-compatibility and fall back
                            // to family parameters below.
                            var _ = c.GetMEPConnectorInfo();
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                }
                // Voltage from family / system param (defaults to 230V).
                try
                {
                    var vp = fi.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE);
                    if (vp != null) voltageV = vp.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"ReadConnectorData: {ex.Message}"); }
            return (load, voltageV, poles);
        }

        // ── propose / edit / create ─────────────────────────────────────

        private void BtnPropose_Click(object sender, RoutedEventArgs e)
        {
            string panel = cmbWzPanel.SelectedItem as string ?? "";
            if (string.IsNullOrEmpty(panel))
            {
                MessageBoxAlt("Select a target panel before proposing circuits.");
                return;
            }
            if (!double.TryParse(txtMaxLoadPct.Text, out double pct)) pct = 80;
            string standard = ((cmbWzStandard.SelectedItem as ComboBoxItem)?.Tag as string) ?? "BS7671";

            try
            {
                var proposed = CircuitWizardEngine.ProposeCircuits(
                    UnconnectedElements, panel, pct / 100.0, standard, _wireTables);
                Proposals.Clear();
                foreach (var p in proposed) Proposals.Add(new ProposedCircuitVm(p, standard, _wireTables));
            }
            catch (Exception ex) { StingLog.Error("Propose", ex); MessageBoxAlt($"Propose failed: {ex.Message}"); }
            UpdateSummary();
            UpdateCreateButton();
        }

        private void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            var sel = ProposalGrid.SelectedItems.OfType<ProposedCircuitVm>().ToList();
            if (sel.Count < 2) { MessageBoxAlt("Select two or more circuits to merge."); return; }
            var first = sel[0];
            for (int i = 1; i < sel.Count; i++)
            {
                foreach (var el in sel[i].Source.Elements) first.Source.Elements.Add(el);
                Proposals.Remove(sel[i]);
            }
            first.Refresh();
            UpdateSummary();
            UpdateCreateButton();
        }

        private void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            var sel = ProposalGrid.SelectedItem as ProposedCircuitVm;
            if (sel == null || sel.Source.Elements.Count < 2) { MessageBoxAlt("Select a circuit with 2+ elements to split."); return; }
            int half = sel.Source.Elements.Count / 2;
            var newProp = new ProposedCircuit
            {
                PanelName     = sel.Source.PanelName,
                ProposedLabel = sel.Source.ProposedLabel + " (split)",
                LoadClass     = sel.Source.LoadClass,
                Phase         = sel.Source.Phase,
                VoltageV      = sel.Source.VoltageV,
                Poles         = sel.Source.Poles
            };
            for (int i = sel.Source.Elements.Count - 1; i >= half; i--)
            {
                newProp.Elements.Insert(0, sel.Source.Elements[i]);
                sel.Source.Elements.RemoveAt(i);
            }
            sel.Refresh();
            Proposals.Add(new ProposedCircuitVm(newProp, "BS7671", _wireTables));
            UpdateSummary();
            UpdateCreateButton();
        }

        private void BtnAddElement_Click(object sender, RoutedEventArgs e)
        {
            var sel = ProposalGrid.SelectedItem as ProposedCircuitVm;
            var elSel = UnconnectedGrid.SelectedItem as UnconnectedElement;
            if (sel == null || elSel == null) { MessageBoxAlt("Select a circuit AND an unconnected element."); return; }
            sel.Source.Elements.Add(elSel);
            UnconnectedElements.Remove(elSel);
            sel.Refresh();
            UpdateSummary();
        }

        private void BtnRemoveElement_Click(object sender, RoutedEventArgs e)
        {
            var sel = ProposalGrid.SelectedItem as ProposedCircuitVm;
            if (sel == null || sel.Source.Elements.Count == 0) return;
            var last = sel.Source.Elements[sel.Source.Elements.Count - 1];
            sel.Source.Elements.RemoveAt(sel.Source.Elements.Count - 1);
            UnconnectedElements.Add(last);
            sel.Refresh();
            UpdateSummary();
            UpdateCreateButton();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            Proposals.Clear();
            RefreshUnconnectedElements();
            UpdateSummary();
            UpdateCreateButton();
        }

        private void UnconnectedGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            BtnAddElement_Click(sender, e);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string panel = cmbWzPanel.SelectedItem as string ?? "";
            if (string.IsNullOrEmpty(panel) || Proposals.Count == 0)
            {
                MessageBoxAlt("Nothing to create.");
                return;
            }
            CircuitWizardCommand.PendingCircuits = Proposals.Select(p => p.Source).ToList();
            CircuitWizardCommand.PendingPanelName = panel;
            try { StingElectricalCommandHandler.Instance?.SetCommand("Circuit_CreateWizard"); }
            catch (Exception ex) { StingLog.Warn($"CreateWizard dispatch: {ex.Message}"); }
            DialogResult = true;
            Close();
        }

        private void UpdateSummary()
        {
            double a = 0, b = 0, c = 0;
            foreach (var p in Proposals)
            {
                if (p.Source.Phase == "A") a += p.Source.TotalLoadVA;
                else if (p.Source.Phase == "B") b += p.Source.TotalLoadVA;
                else if (p.Source.Phase == "C") c += p.Source.TotalLoadVA;
                else if (p.Source.Phase == "ABC")
                { a += p.Source.TotalLoadVA / 3; b += p.Source.TotalLoadVA / 3; c += p.Source.TotalLoadVA / 3; }
            }
            double total = a + b + c;
            double imb = total > 0 ? (Math.Max(a, Math.Max(b, c)) - Math.Min(a, Math.Min(b, c))) / (total / 3) * 100 : 0;
            txtPhaseSummary.Text = $"Phase A: {a:0} VA  |  Phase B: {b:0} VA  |  Phase C: {c:0} VA  |  Imbalance: {imb:0}%";
        }

        private void UpdateCreateButton()
        {
            btnCreate.Content = $"▶ Create {Proposals.Count} Circuit{(Proposals.Count == 1 ? "" : "s")}";
            btnCreate.IsEnabled = Proposals.Count > 0;
        }

        private void MessageBoxAlt(string msg)
            => System.Windows.MessageBox.Show(msg, "STING Circuit Wizard");
    }

    public class ProposedCircuitVm : INotifyPropertyChanged
    {
        public ProposedCircuit Source { get; }
        private readonly string _standard;
        private readonly WireTableSet _wireTables;

        public ProposedCircuitVm(ProposedCircuit src, string standard, WireTableSet wireTables)
        {
            Source = src;
            _standard = standard;
            _wireTables = wireTables;
        }
        public string ProposedLabel
        {
            get => Source.ProposedLabel;
            set { Source.ProposedLabel = value; OnChanged(nameof(ProposedLabel)); }
        }
        public string LoadClass
        {
            get => Source.LoadClass;
            set { Source.LoadClass = value; OnChanged(nameof(LoadClass)); }
        }
        public string Phase
        {
            get => Source.Phase;
            set { Source.Phase = value; OnChanged(nameof(Phase)); }
        }
        public int ElementsCount => Source.Elements.Count;
        public string TotalVADisplay => $"{Source.TotalLoadVA:0}";
        public string UtilDisplay => $"{Source.UtilisationPct:0}%";
        public string ProposedRatingDisplay => $"{Source.ProposedRatingA:0}A";
        public string ProposedCsaDisplay => $"{Source.ProposedCsaMm2:0.#}mm²";
        public void Refresh()
        {
            CircuitWizardEngine.RecalculateCircuit(Source, _standard, _wireTables);
            OnChanged(nameof(ElementsCount));
            OnChanged(nameof(TotalVADisplay));
            OnChanged(nameof(UtilDisplay));
            OnChanged(nameof(ProposedRatingDisplay));
            OnChanged(nameof(ProposedCsaDisplay));
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
