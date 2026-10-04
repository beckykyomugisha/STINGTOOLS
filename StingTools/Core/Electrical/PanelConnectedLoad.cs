// PanelConnectedLoad — a board's connected TRUE power in kW (ROADMAP ELEC-27).
//
// ELC_PNL_CONNECTED_LOAD_KW (alias ELC_PNL_LOAD) was filled from
// RBS_ELEC_PANEL_TOTALLOAD_PARAM, which is the board's connected APPARENT power (VA),
// so a kVA figure sat under a kW name. Revit has no board-level true-power parameter;
// the true power of a board is the sum of RBS_ELEC_TRUE_LOAD (W) over the circuits it
// feeds (their BaseEquipment is the board). A sub-main to another board carries that
// board's load, so the sum is the board's whole connected load.
//
// The grouping is PanelConnectedLoadMath (Revit-free, tested); this file reads the model.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    internal static class PanelConnectedLoad
    {
        /// <summary>
        /// Connected true power per board element id, in kW, from every power circuit with a
        /// base equipment. A board with no outgoing circuit is absent (nothing to sum).
        /// </summary>
        public static Dictionary<long, double> ByBoardKw(Document doc)
        {
            var rows = new List<(long, double)>();
            if (doc == null) return new Dictionary<long, double>();
            foreach (ElectricalSystem sys in new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem)))
            {
                try
                {
                    if (sys.SystemType != ElectricalSystemType.PowerCircuit || sys.BaseEquipment == null) continue;
                    rows.Add((sys.BaseEquipment.Id.Value, ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_TRUE_LOAD)));
                }
                catch (Exception ex) { StingLog.Info($"Connected load of circuit {sys.Id}: {ex.Message}"); }
            }
            return PanelConnectedLoadMath.SumKw(rows);
        }

        /// <summary>
        /// One board's connected true power in kW, from the circuits it feeds
        /// (MEPModel.GetAssignedElectricalSystems). Null when it feeds none or they cannot be
        /// read — the caller writes nothing rather than a 0.
        /// </summary>
        public static double? BoardKw(Element board)
        {
            if (!(board is FamilyInstance fi) || fi.MEPModel == null) return null;
            try
            {
                var systems = fi.MEPModel.GetAssignedElectricalSystems();
                if (systems == null || systems.Count == 0) return null;
                var rows = new List<(long, double)>();
                foreach (ElectricalSystem sys in systems)
                    if (sys.SystemType == ElectricalSystemType.PowerCircuit)
                        rows.Add((fi.Id.Value, ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_TRUE_LOAD)));
                return PanelConnectedLoadMath.SumKw(rows).TryGetValue(fi.Id.Value, out double kw) ? kw : (double?)null;
            }
            catch (Exception ex) { StingLog.Info($"Connected load of board {board.Id}: {ex.Message}"); return null; }
        }
    }
}
