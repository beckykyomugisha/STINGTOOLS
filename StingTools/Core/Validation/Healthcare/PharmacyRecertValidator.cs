using StingTools.Core.Validation;
using StingTools.Standards.USP797800;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>USP &lt;797&gt;/&lt;800&gt; environmental-recertification escalation.
    /// For every pharmacy cleanroom (canonical room class PH-CSP-797 / PH-CSP-800)
    /// reads CLN_ENV_CERT_DUE_DT and escalates: Info when &gt; 30 days out, Warning
    /// within 30 days, Error when overdue or missing. The 6-monthly cycle constant
    /// and the cleanroom room-class set come from USPStandards; room-class matching
    /// runs through the canonical RoomClassCodes resolver (via GetRoomClassCached)
    /// so spelling variants still match.</summary>
    public class PharmacyRecertValidator : HealthcareValidatorBase
    {
        public override string Name => "PharmacyRecertValidator";
        private const string Tag = "PharmacyRecertValidator";

        // Escalation window (days) before the due date at which Warning begins.
        public int WarnWithinDays { get; set; } = 30;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var today = DateTime.UtcNow.Date;
            foreach (var room in GetClinicalRoomsCached(doc))
            {
                var rc = GetRoomClassCached(room);          // canonical (RoomClassCodes)
                if (!USPStandards.IsCleanroomRoomClass(rc)) continue;

                var raw = GetParam(room, "CLN_ENV_CERT_DUE_DT");
                if (string.IsNullOrWhiteSpace(raw) || !TryParseDate(raw, out var due))
                {
                    res.Add(new ValidationResult(room.Id, ValidationSeverity.Error,
                        "CLN.ENVCERT.MISSING",
                        $"Cleanroom {room.Name} ({rc}) has no valid CLN_ENV_CERT_DUE_DT — " +
                        $"USP environmental recertification is required every {USPStandards.RecertificationCycleMonths} months [USP <797>/<800>]",
                        Tag));
                    continue;
                }

                int days = (int)(due.Date - today).TotalDays;
                if (days < 0)
                    res.Add(new ValidationResult(room.Id, ValidationSeverity.Error,
                        "CLN.ENVCERT.OVERDUE",
                        $"Cleanroom {room.Name} ({rc}) environmental recertification overdue by {-days} day(s) (due {due:yyyy-MM-dd}) [USP <797>/<800>]",
                        Tag));
                else if (days <= WarnWithinDays)
                    res.Add(new ValidationResult(room.Id, ValidationSeverity.Warning,
                        "CLN.ENVCERT.DUE_SOON",
                        $"Cleanroom {room.Name} ({rc}) environmental recertification due in {days} day(s) ({due:yyyy-MM-dd}) — schedule sampling [USP <797>/<800>]",
                        Tag));
                else
                    res.Add(new ValidationResult(room.Id, ValidationSeverity.Info,
                        "CLN.ENVCERT.OK",
                        $"Cleanroom {room.Name} ({rc}) environmental recertification current — next due {due:yyyy-MM-dd} ({days} days)",
                        Tag));
            }
            return res;
        }

        private static bool TryParseDate(string s, out DateTime dt) =>
            DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt);
    }
}
