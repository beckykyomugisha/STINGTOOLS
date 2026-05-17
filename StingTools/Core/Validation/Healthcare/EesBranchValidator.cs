using System;
using Autodesk.Revit.DB;
using StingTools.Standards.NFPA99;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>NFPA 99 / NEC 517 — Essential Electrical System branch tagging
    /// and ATS-time + IPS rules per anaesthetising-location.</summary>
    public class EesBranchValidator : HealthcareValidatorBase
    {
        public override string Name => "EesBranchValidator";
        private const string Tag = "EesBranchValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var cats = new[] {
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_Conduit
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                var br = GetParam(el, "ELC_EES_BRANCH_TXT");
                var branch = NFPA99Standards.ParseBranch(br);

                // Patient-care receptacles must declare an EES branch.
                var receptType = GetParam(el, "ELC_RECEPT_TYPE_TXT");
                var hostRoom = TryGetHostRoom(doc, el);
                // Cache-aware: O(1) dictionary hit when running in the chain.
                var hostClass = hostRoom != null ? GetRoomClassCached(hostRoom) : "";

                bool isPatientCare = !string.IsNullOrEmpty(hostClass) &&
                    (hostClass.StartsWith("OR") || hostClass == "ICU" || hostClass == "CATHLAB" ||
                     hostClass == "IR" || hostClass == "RECOV-1" || hostClass == "MAT-LDR" ||
                     hostClass == "NICU" || hostClass == "DIAL");

                if (isPatientCare && (branch == NFPA99Standards.EesBranch.Unknown ||
                                       branch == NFPA99Standards.EesBranch.Normal))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "ELC.EES.MISSING",
                        $"{el.Name} in patient-care room ({hostClass}) not on Life Safety or Critical branch (got '{br}')",
                        Tag));
                }

                // ATS time within NFPA 99 limits per branch.
                var atsTime = GetParamDouble(el, "ELC_ATS_TIME_S_NR");
                if (atsTime.HasValue)
                {
                    double maxS = branch switch
                    {
                        NFPA99Standards.EesBranch.LifeSafety => NFPA99Standards.AtsTransferTimeMaxLifeSafetyS,
                        NFPA99Standards.EesBranch.Critical   => NFPA99Standards.AtsTransferTimeMaxCriticalS,
                        NFPA99Standards.EesBranch.Equipment  => NFPA99Standards.AtsTransferTimeMaxEquipmentS,
                        _ => double.MaxValue
                    };
                    if (atsTime.Value > maxS)
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "ELC.EES.ATS_SLOW",
                            $"{el.Name} branch {branch} ATS={atsTime:F1}s exceeds {maxS:F0}s [NFPA 99 §6.4]",
                            Tag));
                }

                // IPS / IT-Cardiac in wet locations.
                if (NFPA99Standards.RequiresIPS(hostClass))
                {
                    if (!GetParamBool(el, "ELC_IPS_BOOL"))
                        res.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELC.IPS.MISSING",
                            $"{el.Name} in wet-location ({hostClass}) missing Isolated Power flag [NEC 517.19]",
                            Tag));
                }
            }
            return res;
        }

        private static Element TryGetHostRoom(Document doc, Element el)
        {
            try
            {
                if (el is FamilyInstance fi)
                {
                    if (fi.Room != null) return fi.Room;
                    if (fi.Location is LocationPoint lp)
                    {
                        var room = doc.GetRoomAtPoint(lp.Point);
                        if (room != null) return room;
                    }
                }
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }
    }
}
