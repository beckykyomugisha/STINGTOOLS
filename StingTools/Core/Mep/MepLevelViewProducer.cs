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
        ///
        /// One lazy collector pass over every category any rule reads (it used to be
        /// five passes, each materialised with ToElements); the per-element rules are
        /// <see cref="MepPresenceClassifier"/>. Callers that need the answer more than
        /// once in a run should compute it once and pass it on — see
        /// BatchProduceCommons.RoutedMepPerLevel(doc, presence).
        /// </summary>
        public static Dictionary<string, HashSet<ElementId>> LevelsByDiscipline(Document doc)
        {
            var map = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return map;

            var found = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(CategoryMap.Keys.ToList()));
                foreach (var el in collector)
                {
                    var catId = el.Category?.Id;
                    if (catId == null || !CategoryMap.TryGetValue((BuiltInCategory)catId.Value, out var cat)) continue;
                    var discs = MepPresenceClassifier.Classify(cat,
                        () => IsFireProtection(el),
                        () =>
                        {
                            try { return ParameterHelpers.GetString(el, ParamRegistry.DISC); }
                            catch (Exception ex) { StingLog.Warn($"MepLevelViewProducer DISC read {el.Id}: {ex.Message}"); return null; }
                        });
                    if (discs.Count == 0) continue;
                    var lid = LevelOf(el);
                    if (lid == ElementId.InvalidElementId) continue;
                    foreach (var d in discs)
                    {
                        if (!found.TryGetValue(d, out var set)) found[d] = set = new HashSet<ElementId>();
                        set.Add(lid);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MepLevelViewProducer collect: {ex.Message}");
            }

            // Keys in the order the per-rule passes used to add them.
            foreach (var d in new[] { "M", "E", "FP", "P", "MG" })
                if (found.TryGetValue(d, out var set)) map[d] = set;
            return map;
        }

        /// <summary>Every category a presence rule reads → its Revit-free name.</summary>
        private static readonly Dictionary<BuiltInCategory, MepPresenceCategory> CategoryMap =
            new Dictionary<BuiltInCategory, MepPresenceCategory>
            {
                { BuiltInCategory.OST_DuctCurves,           MepPresenceCategory.DuctCurve },
                { BuiltInCategory.OST_FlexDuctCurves,       MepPresenceCategory.FlexDuctCurve },
                { BuiltInCategory.OST_DuctTerminal,         MepPresenceCategory.DuctTerminal },
                { BuiltInCategory.OST_MechanicalEquipment,  MepPresenceCategory.MechanicalEquipment },
                { BuiltInCategory.OST_ElectricalFixtures,   MepPresenceCategory.ElectricalFixture },
                { BuiltInCategory.OST_ElectricalEquipment,  MepPresenceCategory.ElectricalEquipment },
                { BuiltInCategory.OST_LightingFixtures,     MepPresenceCategory.LightingFixture },
                { BuiltInCategory.OST_LightingDevices,      MepPresenceCategory.LightingDevice },
                { BuiltInCategory.OST_DataDevices,          MepPresenceCategory.DataDevice },
                { BuiltInCategory.OST_FireAlarmDevices,     MepPresenceCategory.FireAlarmDevice },
                { BuiltInCategory.OST_Sprinklers,           MepPresenceCategory.Sprinkler },
                { BuiltInCategory.OST_PipeCurves,           MepPresenceCategory.PipeCurve },
                { BuiltInCategory.OST_FlexPipeCurves,       MepPresenceCategory.FlexPipeCurve },
                { BuiltInCategory.OST_PlumbingFixtures,     MepPresenceCategory.PlumbingFixture },
                { BuiltInCategory.OST_PipeAccessory,        MepPresenceCategory.PipeAccessory },
                { BuiltInCategory.OST_SpecialityEquipment,  MepPresenceCategory.SpecialityEquipment },
                { BuiltInCategory.OST_MedicalEquipment,     MepPresenceCategory.MedicalEquipment },
            };

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
