using StingTools.Core.Validation;
using System;
// Healthcare Pack H-26 — HTM 01-06 endoscope traceability validator.
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class EndoscopeTraceValidator : HealthcareValidatorBase
    {
        public override string Name => "EndoscopeTraceValidator";
        private const string Tag = "EndoscopeTraceValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            var endoRooms = GetClinicalRoomsCached(doc)
                .Where(r => GetRoomClassCached(r) is "ENDOSCOPY" or "ENDO-DECON")
                .ToList();
            if (endoRooms.Count == 0) return res;

            // Each endoscopy decon room needs at least 4 reader IDs.
            var readers = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DataDevices)
                .WhereElementIsNotElementType().ToElements()
                .Where(e => !string.IsNullOrEmpty(GetParam(e, "ICT_ENDO_READER_ID_TXT")))
                .ToList();
            foreach (var room in endoRooms)
            {
                var roomReaders = readers.Count(rdr =>
                {
                    if (rdr is FamilyInstance fi)
                    {
                        var rm = fi.Room ?? (fi.Location is LocationPoint lp
                                              ? doc.GetRoomAtPoint(lp.Point) : null);
                        return rm != null && rm.Id.Value == room.Id.Value;
                    }
                    return false;
                });
                if (roomReaders < 4)
                    res.Add(new ValidationResult(room.Id, ValidationSeverity.Warning, "ENDO.READER.LOW",
                        $"{room.Name} has {roomReaders} RFID readers — HTM 01-06 chain needs at minimum 4 (soak / AER / drying / storage)", Tag));
            }
            return res;
        }
    }
}
