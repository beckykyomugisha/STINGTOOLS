using StingTools.Core.Validation;
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>Flags any CLN_ROOM_CLASS_TXT value that is neither a canonical
    /// room-class code nor a known alias (see RoomClassCodes /
    /// HEALTHCARE_ROOM_CLASSES.json). Without this, an unrecognised spelling
    /// silently falls through every design/pressure/ACH/acoustic lookup instead
    /// of surfacing as a warning — the exact fragmentation this pack unifies.</summary>
    public class RoomClassCodeValidator : HealthcareValidatorBase
    {
        public override string Name => "RoomClassCodeValidator";
        private const string Tag = "RoomClassCodeValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var table = RoomClassCodes.Get(doc);
            foreach (var room in GetClinicalRoomsCached(doc))
            {
                // Read the RAW value (not the canonicalised one) so genuine drift
                // is caught rather than masked by the resolver.
                var raw = GetParam(room, "CLN_ROOM_CLASS_TXT");
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (table.IsRecognised(raw)) continue;

                res.Add(new ValidationResult(room.Id, ValidationSeverity.Warning,
                    "ROOM.CLASS.UNKNOWN",
                    $"Room '{room.Name}' CLN_ROOM_CLASS_TXT = '{raw}' is not a canonical room class " +
                    "or known alias — design/pressure/ACH/acoustic lookups will skip it. " +
                    "Add it (or an alias) to HEALTHCARE_ROOM_CLASSES.json or correct the value.",
                    Tag));
            }
            return res;
        }
    }
}
