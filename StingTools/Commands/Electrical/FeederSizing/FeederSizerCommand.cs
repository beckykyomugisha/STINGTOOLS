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
        public string DiversityMode;
        public double DiversityPct;
        public string InstallMethod;
        public double VDLimitPct;
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
                { DerateFactor = 0.8, DiversityMode = "None", DiversityPct = 100,
                  InstallMethod = "C", VDLimitPct = 2.0 };

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

            int written = 0, vdFails = 0;
            using (var tx = new Transaction(doc, "STING Size Feeders"))
            {
                tx.Start();
                foreach (var r in results)
                {
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
            TaskDialog.Show("STING Feeders",
                $"Sized {results.Count} feeder(s). Stamped {written}. VD exceedances: {vdFails}.");
            return Result.Succeeded;
        }

        private void CollectInputs(StingTools.Core.SLD.SLDNode node, FeederSettingsSnapshot s,
            List<FeederSizeInput> output, bool isRoot)
        {
            if (node == null) return;
            if (!isRoot && node.IsPanel)
            {
                output.Add(new FeederSizeInput
                {
                    PanelName       = node.Label ?? "",
                    DemandKW        = node.LoadKW > 0 ? node.LoadKW : 0,
                    PowerFactor     = 0.85,
                    SystemVoltageV  = 415.0,
                    Phases          = 3,
                    DerateFactor    = s.DerateFactor,
                    DiversityFactor = s.DiversityPct > 0 ? s.DiversityPct / 100.0 : 1.0,
                    InstallMethod   = s.InstallMethod ?? "C",
                    Material        = "Cu",
                    Insulation      = "XLPE90",
                    FeederLengthM   = 10.0,
                    VDLimitPct      = s.VDLimitPct > 0 ? s.VDLimitPct : 2.0,
                    Standard        = "BS7671"
                });
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
