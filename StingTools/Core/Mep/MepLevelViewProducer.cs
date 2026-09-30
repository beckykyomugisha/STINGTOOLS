// StingTools — MEP Level View Producer (Phase F): which levels host which MEP discipline.
//
// This file used to BE the producer behind HVAC → SYS → "Per-level + sheets": it made
// its own floor plans, put each on a sheet with the FIRST title block in the project,
// numbered only when the routed drawing type had a pattern (Electrical never did), never
// stamped view or sheet, and knew nothing of fire protection or medical gas. Its sheets
// were invisible to Doctor, Renumber and Produce & Export, and a drawing-type production
// made a second set.
//
// The command now produces through DrawingProducer (MepProduceMepViewsByLevelCommand).
// What stays here is the part only this file knew: on which levels each discipline has
// anything modelled, so a level with no ductwork gets no HVAC plan.
//
//   M   ducts, flex ducts, air terminals, mechanical equipment
//   P   pipes and plumbing fixtures, except fire-protection systems
//   E   electrical equipment / fixtures, lighting, data, fire-alarm devices
//   FP  sprinklers, and pipes on a fire-protection system (wet / dry / pre-action / other)
//   MG  anything whose DISC token is MG (medical-gas pipework, outlets, AVSUs, alarms)

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Mep
{
    public static class MepLevelViewProducer
    {
        /// <summary>The MEP disciplines this answers for, in report order.</summary>
        public static readonly string[] Disciplines = { "M", "E", "P", "FP", "MG" };

        /// <summary>
        /// Discipline code → the levels hosting at least one element of it. A discipline
        /// with nothing modelled is absent from the map.
        /// </summary>
        public static Dictionary<string, HashSet<ElementId>> LevelsByDiscipline(Document doc)
        {
            var map = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return map;

            void Add(string disc, Element el)
            {
                var lid = LevelOf(el);
                if (lid == ElementId.InvalidElementId) return;
                if (!map.TryGetValue(disc, out var set)) map[disc] = set = new HashSet<ElementId>();
                set.Add(lid);
            }

            foreach (var el in Collect(doc, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves,
                                            BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_MechanicalEquipment))
                Add("M", el);

            foreach (var el in Collect(doc, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalEquipment,
                                            BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices,
                                            BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_FireAlarmDevices))
                Add("E", el);

            foreach (var el in Collect(doc, BuiltInCategory.OST_Sprinklers))
                Add("FP", el);

            foreach (var el in Collect(doc, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves,
                                            BuiltInCategory.OST_PlumbingFixtures))
                Add(IsFireProtection(el) ? "FP" : "P", el);

            foreach (var el in Collect(doc, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeAccessory,
                                            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_SpecialityEquipment,
                                            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_MedicalEquipment))
            {
                string disc = null;
                try { disc = ParameterHelpers.GetString(el, ParamRegistry.DISC); }
                catch (Exception ex) { StingLog.Warn($"MepLevelViewProducer DISC read {el.Id}: {ex.Message}"); }
                if (string.Equals((disc ?? "").Trim(), "MG", StringComparison.OrdinalIgnoreCase)) Add("MG", el);
            }
            return map;
        }

        private static IEnumerable<Element> Collect(Document doc, params BuiltInCategory[] cats)
        {
            try
            {
                return new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(cats)).ToElements();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MepLevelViewProducer collect {string.Join(",", cats)}: {ex.Message}");
                return Enumerable.Empty<Element>();
            }
        }

        private static bool IsFireProtection(Element el)
        {
            try
            {
                if (!(el is MEPCurve curve) || !(curve.MEPSystem is PipingSystem ps)) return false;
                switch (ps.SystemType)
                {
                    case PipeSystemType.FireProtectWet:
                    case PipeSystemType.FireProtectDry:
                    case PipeSystemType.FireProtectPreaction:
                    case PipeSystemType.FireProtectOther:
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MepLevelViewProducer system type {el?.Id}: {ex.Message}"); return false; }
        }

        private static ElementId LevelOf(Element el)
        {
            try
            {
                if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId) return el.LevelId;
                var p = el.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId) return p.AsElementId();
                var p2 = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (p2 != null && p2.StorageType == StorageType.ElementId) return p2.AsElementId();
            }
            catch (Exception ex) { StingLog.Warn($"MepLevelViewProducer LevelOf {el?.Id}: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }
    }
}
