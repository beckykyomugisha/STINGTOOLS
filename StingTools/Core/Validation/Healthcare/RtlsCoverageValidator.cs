// Healthcare Pack H-29 — RTLS coverage / RF dead-zone validator.
using System;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class RtlsCoverageValidator : HealthcareValidatorBase
    {
        public override string Name => "RtlsCoverageValidator";
        private const string Tag = "RtlsCoverageValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;
            // RTLS anchors / readers ship as Data Devices, Communication Devices,
            // Nurse Call Devices, or sometimes Medical Equipment depending on the manufacturer.
            var anchorCats = new ElementMulticategoryFilter(new[] {
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_MedicalEquipment });
            var anchors = new FilteredElementCollector(doc).WherePasses(anchorCats)
                .WhereElementIsNotElementType().ToElements()
                .Where(e => !string.IsNullOrEmpty(GetParam(e, "ICT_RTLS_ANCHOR_ID_TXT")))
                .ToList();
            foreach (var a in anchors)
            {
                Element room = null;
                try
                {
                    if (a is FamilyInstance fi)
                    {
                        room = fi.Room ?? (fi.Location is LocationPoint lp ? doc.GetRoomAtPoint(lp.Point) : null);
                    }
                } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (room == null) continue;

                var rfShield = GetParamBool(room, "CLN_RF_SHIELD_BOOL");
                int mriZone = (int)(GetParamDouble(room, "CLN_MRI_ZONE_INT") ?? 0);
                var tech = GetParam(a, "ICT_RTLS_TECH_TXT").ToUpperInvariant();
                bool isWireless = tech is "BLE" or "UWB" or "WIFI-RSSI" or "RFID-1356";

                if (rfShield && tech != "IR" && tech != "")
                    res.Add(new ValidationResult(a.Id, ValidationSeverity.Warning, "RTLS.RF_DEADZONE",
                        $"Anchor {GetParam(a,"ICT_RTLS_ANCHOR_ID_TXT")} ({tech}) inside RF-shielded room {room.Name} — only IR / UWB realistic", Tag));
                if (mriZone >= 3 && isWireless)
                    res.Add(new ValidationResult(a.Id, ValidationSeverity.Error, "RTLS.MRI_ZONE",
                        $"Anchor {GetParam(a,"ICT_RTLS_ANCHOR_ID_TXT")} ({tech}) within MRI Z{mriZone} — wireless transmitter prohibited", Tag));
            }
            return res;
        }
    }
}
