using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.FeederSizing
{
    /// <summary>Snapshot of the FEEDER SIZING expander on the dock panel.</summary>
    public class FeederSettingsSnapshot
    {
        public double DerateFactor;
        /// <summary>Applied to the supply-circuit apparent load; 100 = none.</summary>
        public double DiversityPct;
        public string InstallMethod;
        /// <summary>Feeder VD limit. Defaults to the BS 7671 Appendix 12 'other' limit
        /// (5 %); was hard-coded 2 %, which upsized every feeder against a limit no
        /// standard sets.</summary>
        public double VDLimitPct = DefaultVdLimitPct;
        public bool VDLimitUserSet;

        /// <summary>BS 7671 Appendix 12 Table 4Ab, other uses (public supply).</summary>
        public const double DefaultVdLimitPct = 5.0;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FeederSizerCommand : IExternalCommand
    {
        public static List<FeederSizeResult> LastResults { get; private set; } = new();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var settings = StingElectricalCommandHandler.CurrentFeederSettings
                ?? new FeederSettingsSnapshot
                { DerateFactor = 0.8, DiversityPct = 100,
                  InstallMethod = "C", VDLimitPct = FeederSettingsSnapshot.DefaultVdLimitPct };

            var root = StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Feeders", "No SLD hierarchy found. Place an incomer panel first.");
                return Result.Cancelled;
            }

            var inputs = new List<FeederSizeInput>();
            CollectInputs(root, settings, inputs, isRoot: true);

            var wireTables = WireTableSet.Load(StingToolsApp.DataPath);
            var results = FeederSizerEngine.CalculateAll(inputs, wireTables);
            LastResults = results;

            int written = 0, vdFails = 0, notSized = 0, onDefaults = 0;
            var notSizedLines = new List<string>();
            using (var tx = new Transaction(doc, "STING Size Feeders"))
            {
                tx.Start();
                foreach (var r in results)
                {
                    if (r.DefaultsUsed.Count > 0) onDefaults++;
                    // A refused / skipped feeder must not be stamped as a 0 mm² cable.
                    if (!r.Sized)
                    {
                        notSized++;
                        if (notSizedLines.Count < 8) notSizedLines.Add($"  {r.PanelName}: {r.Warning}");
                        StingLog.Warn($"Feeder '{r.PanelName}' not sized: {r.Warning}");
                        continue;
                    }
                    try
                    {
                        var panel = FindPanelByName(doc, r.PanelName);
                        if (panel == null) continue;
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_FEEDER_CSA,
                            $"{r.ProposedCsaMm2:0.#}", overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_FEEDER_RATING_A,
                            $"{r.ProposedRatingA:0}", overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_CKT_VD_PCT,
                            $"{r.ActualVDPct:0.00}", overwrite: true);
                        written++;
                        if (!r.VDCompliant) vdFails++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Feeder write: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            var defaults = results.SelectMany(r => r.DefaultsUsed.Select(d => $"{r.PanelName}: {d}")).Take(8).ToList();
            StingLog.Info($"FeederSizer: {results.Count} feeder(s), stamped {written}, not sized {notSized}, " +
                          $"on defaults {onDefaults}, VD fails {vdFails}.");
            TaskDialog.Show("STING Feeders",
                $"Feeders: {results.Count}. Stamped {written}. Not sized: {notSized}. VD exceedances: {vdFails}.\n" +
                $"VD limit: {settings.VDLimitPct:0.##} % " +
                (settings.VDLimitUserSet ? "(user-set for feeders)" : "(BS 7671 Appendix 12 'other' limit)") +
                $". Diversity: {(settings.DiversityPct > 0 ? settings.DiversityPct : 100):0.#} %.\n" +
                (notSized > 0 ? "\nNot sized:\n" + string.Join("\n", notSizedLines) + "\n" : "") +
                (onDefaults > 0
                    ? $"\n{onDefaults} feeder(s) used DEFAULT inputs (not model data) — check before issue:\n" +
                      string.Join("\n", defaults.Select(d => "  " + d))
                    : ""));
            return Result.Succeeded;
        }

        /// <summary>The circuit that FEEDS a panel: one of its electrical systems whose base
        /// equipment is another panel (not this one). Null when the panel has no supply circuit
        /// in the model.</summary>
        private static ElectricalSystem SupplyCircuit(FamilyInstance panel)
        {
            try
            {
                var systems = panel?.MEPModel?.GetElectricalSystems();
                if (systems == null) return null;
                foreach (ElectricalSystem s in systems)
                {
                    try
                    {
                        if (s.BaseEquipment == null || s.BaseEquipment.Id != panel.Id) return s;
                    }
                    catch (Exception ex) { StingLog.Warn($"Feeder supply circuit: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Feeder GetElectricalSystems {panel?.Id}: {ex.Message}"); }
            return null;
        }

        private void CollectInputs(StingTools.Core.SLD.SLDNode node, FeederSettingsSnapshot s,
            List<FeederSizeInput> output, bool isRoot)
        {
            if (node == null) return;
            if (!isRoot && node.IsPanel)
            {
                var input = new FeederSizeInput
                {
                    PanelName       = node.Label ?? "",
                    DerateFactor    = s.DerateFactor,
                    DiversityFactor = s.DiversityPct > 0 ? s.DiversityPct / 100.0 : 1.0,
                    InstallMethod   = s.InstallMethod ?? "C",
                    Material        = "Cu",
                    Insulation      = "PVC70",
                    VDLimitPct      = s.VDLimitPct > 0 ? s.VDLimitPct : FeederSettingsSnapshot.DefaultVdLimitPct,
                    Standard        = "BS7671"
                };
                // Cable type is a stated assumption (PVC70 multicore, Table 4D2A — the only
                // Appendix 4 table shipped); it appears in every result's Basis.

                // ELEC-3: length, voltage, poles and load come from the circuit that FEEDS
                // this panel. They were hard-coded (10 m / 415 V / 3-ph / PF 0.85 on the SLD
                // node's load, which is the last DOWNSTREAM circuit read, not the feed).
                ElectricalSystem feed = SupplyCircuit(node.RevitElement);
                if (feed == null)
                {
                    input.SkipReason = "no supply circuit in the model — connect the panel to its upstream board " +
                                       "(its feeder length, voltage and load are read from that circuit).";
                }
                else
                {
                    double va = StingTools.Core.Electrical.ElecUnits.ApparentLoadVA(feed);
                    if (va > 0)
                    {
                        // Apparent kVA with PF = 1 → Ib is the circuit's apparent line current.
                        input.DemandKW = va / 1000.0;
                        input.PowerFactor = 1.0;
                    }
                    else input.SkipReason = "supply circuit has no apparent load.";

                    double v = StingTools.Core.Electrical.ElecUnits.Volts(feed);
                    int poles = 0;
                    try { poles = feed.PolesNumber; } catch (Exception ex) { StingLog.Warn($"Feeder poles: {ex.Message}"); }
                    input.Phases = poles >= 3 ? 3 : 1;
                    if (poles <= 0) input.DefaultsUsed.Add("single-phase (supply circuit pole count unreadable)");
                    if (v > 0) input.SystemVoltageV = v;
                    else
                    {
                        input.SystemVoltageV = input.Phases == 3 ? 400.0 : 230.0;
                        input.DefaultsUsed.Add($"voltage {input.SystemVoltageV:0} V (supply circuit has none)");
                    }

                    double lengthFt = 0;
                    try { lengthFt = feed.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0; }
                    catch (Exception ex) { StingLog.Warn($"Feeder length: {ex.Message}"); }
                    if (lengthFt > 0)
                        input.FeederLengthM = UnitUtils.ConvertFromInternalUnits(lengthFt, UnitTypeId.Meters);
                    else
                    {
                        input.FeederLengthM = 10.0;
                        input.DefaultsUsed.Add("length 10 m (supply circuit has no path length — draw its path)");
                    }
                }
                output.Add(input);
            }
            foreach (var child in node.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                CollectInputs(child, s, output, isRoot: false);
        }

        private static FamilyInstance FindPanelByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
