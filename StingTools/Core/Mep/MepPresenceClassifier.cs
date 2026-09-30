// StingTools — which MEP disciplines one modelled element counts towards (Revit-free).
//
// MepLevelViewProducer.LevelsByDiscipline used to answer this with five collector
// passes, one per rule below, materialising every element each time. It now makes one
// pass over the union of the categories and asks this classifier per element. The
// rules are unchanged; they live here so they can be tested without Revit.
//
//   M   ducts, flex ducts, air terminals, mechanical equipment
//   E   electrical equipment / fixtures, lighting fixtures / devices, data, fire alarm
//   FP  sprinklers; pipes / flex pipes / plumbing fixtures on a fire-protection system
//   P   pipes / flex pipes / plumbing fixtures otherwise
//   MG  pipes, pipe accessories, mechanical / speciality / medical equipment and
//       plumbing fixtures whose DISC token is MG — IN ADDITION to the above

using System;
using System.Collections.Generic;

namespace StingTools.Core.Mep
{
    /// <summary>The categories LevelsByDiscipline looks at, named without Revit.</summary>
    public enum MepPresenceCategory
    {
        Other,
        DuctCurve, FlexDuctCurve, DuctTerminal, MechanicalEquipment,
        ElectricalFixture, ElectricalEquipment, LightingFixture, LightingDevice, DataDevice, FireAlarmDevice,
        Sprinkler,
        PipeCurve, FlexPipeCurve, PlumbingFixture,
        PipeAccessory, SpecialityEquipment, MedicalEquipment,
    }

    public static class MepPresenceClassifier
    {
        /// <summary>
        /// The disciplines an element of <paramref name="cat"/> counts towards, in the
        /// order the old passes added them. <paramref name="isFireProtection"/> and
        /// <paramref name="discToken"/> are only called for the categories whose rule
        /// needs them, so an element pays for at most one system and one DISC read.
        /// </summary>
        public static List<string> Classify(MepPresenceCategory cat, Func<bool> isFireProtection, Func<string> discToken)
        {
            var result = new List<string>(2);
            switch (cat)
            {
                case MepPresenceCategory.DuctCurve:
                case MepPresenceCategory.FlexDuctCurve:
                case MepPresenceCategory.DuctTerminal:
                case MepPresenceCategory.MechanicalEquipment:
                    result.Add("M");
                    break;
                case MepPresenceCategory.ElectricalFixture:
                case MepPresenceCategory.ElectricalEquipment:
                case MepPresenceCategory.LightingFixture:
                case MepPresenceCategory.LightingDevice:
                case MepPresenceCategory.DataDevice:
                case MepPresenceCategory.FireAlarmDevice:
                    result.Add("E");
                    break;
                case MepPresenceCategory.Sprinkler:
                    result.Add("FP");
                    break;
                case MepPresenceCategory.PipeCurve:
                case MepPresenceCategory.FlexPipeCurve:
                case MepPresenceCategory.PlumbingFixture:
                    result.Add(isFireProtection != null && isFireProtection() ? "FP" : "P");
                    break;
            }

            if (CountsForMedicalGas(cat))
            {
                string disc = null;
                try { disc = discToken?.Invoke(); }
                catch { disc = null; }   // the caller's reader logs its own failure
                if (string.Equals((disc ?? "").Trim(), "MG", StringComparison.OrdinalIgnoreCase))
                    result.Add("MG");
            }
            return result;
        }

        /// <summary>The categories the MG (DISC token) rule reads. Flex pipe and the
        /// duct / electrical / sprinkler categories are not among them.</summary>
        public static bool CountsForMedicalGas(MepPresenceCategory cat)
        {
            switch (cat)
            {
                case MepPresenceCategory.PipeCurve:
                case MepPresenceCategory.PipeAccessory:
                case MepPresenceCategory.MechanicalEquipment:
                case MepPresenceCategory.SpecialityEquipment:
                case MepPresenceCategory.PlumbingFixture:
                case MepPresenceCategory.MedicalEquipment:
                    return true;
                default:
                    return false;
            }
        }
    }
}
