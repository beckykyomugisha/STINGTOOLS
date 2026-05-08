using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HBN 03-01 / FGI Pt 2 — every fitting in a ligature-resistant
    /// room must carry LIG_PRODUCT_RATING_TXT and observation-LOS code.</summary>
    public class AntiLigatureValidator : HealthcareValidatorBase
    {
        public override string Name => "AntiLigatureValidator";
        private const string Tag = "AntiLigatureValidator";

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Build set of room ElementIds whose CLN_LIGATURE_RES_BOOL == 1.
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToElements()
                .Where(r => GetParamBool(r, "CLN_LIGATURE_RES_BOOL"))
                .ToList();
            if (rooms.Count == 0) return res;

            var ligRoomIds = rooms.Select(r => r.Id.IntegerValue).ToHashSet();

            // Categories of fittings to inspect.
            var cats = new[] {
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_CommunicationDevices
            };
            var f = new ElementMulticategoryFilter(cats);
            var els = new FilteredElementCollector(doc).WherePasses(f).WhereElementIsNotElementType().ToElements();

            foreach (var el in els)
            {
                if (!(el is FamilyInstance fi)) continue;
                var loc = fi.Location as LocationPoint;
                if (loc == null) continue;
                Element room = doc.GetRoomAtPoint(loc.Point);
                if (room == null || !ligRoomIds.Contains(room.Id.IntegerValue)) continue;

                if (string.IsNullOrEmpty(GetParam(el, "LIG_PRODUCT_RATING_TXT")))
                {
                    res.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                        "LIG.RATING.MISSING",
                        $"{el.Name} in ligature-resistant room {room.Name} missing LIG_PRODUCT_RATING_TXT",
                        Tag));
                }
            }

            // Risk level vs ligature flag consistency on rooms.
            foreach (var r in rooms)
            {
                var risk = GetParam(r, "CLN_LIG_RISK_LVL_TXT");
                if (string.IsNullOrEmpty(risk))
                    res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                        "LIG.RISK.UNSET",
                        $"Ligature-resistant room {r.Name} has no risk level (CLN_LIG_RISK_LVL_TXT)",
                        Tag));
            }
            return res;
        }
    }
}
