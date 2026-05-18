using StingTools.Core.Validation;
using System;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>RDS completeness — a clinical room must carry the minimum
    /// HBN/FGI/ADB attributes before its Room Data Sheet can be issued.</summary>
    public class RdsCompletenessValidator : HealthcareValidatorBase
    {
        public override string Name => "RdsCompletenessValidator";
        private const string Tag = "RdsCompletenessValidator";

        // Required parameters per clinical room.
        private static readonly string[] RequiredParams = new[]
        {
            "CLN_ROOM_CLASS_TXT",
            "ASS_ROOM_NAME_TXT",
            "ASS_ROOM_NUM_TXT",
            "ASS_ROOM_AREA_SQ_M",
            "ASS_DESIGN_OCCUPANCY_INT",
            "HVC_AIR_CHANGES_PER_HR",
            "PER_ENVIRONMENTAL_TEMP_DESIGN_C",
            "PER_ENVIRONMENTAL_HUMIDITY_DESIGN_PCT",
            "BLE_ROOM_FINISH_FLOOR_TXT",
            "BLE_ROOM_FINISH_CEILING_TXT",
            "BLE_ROOM_FINISH_WALL_TXT",
            "LTG_DESIGN_ILLUMINANCE_LUX",
            "PER_ACOUSTICS_BACKGROUND_NOISE_DB",
            "FLS_COMPARTMENT_ID_TXT"
        };

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var missing = new List<string>();
                foreach (var p in RequiredParams)
                    if (string.IsNullOrEmpty(GetParam(r, p))) missing.Add(p);

                if (missing.Count > 0)
                {
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                        "RDS.INCOMPLETE",
                        $"Room {r.Name} ({GetParam(r,"CLN_ROOM_CLASS_TXT")}) RDS missing: {string.Join(", ", missing)}",
                        Tag));
                }
            }
            return res;
        }
    }
}
