// §5.5 — ClassificationAuditValidator.
//
// Walks family-bearing categories (plumbing fixtures, mechanical / electrical
// equipment, doors, walls) and flags elements that lack a Uniclass 2015
// classification, NBS spec clause, or an RFI URL. BOQ grouping,
// HandoverManualCommand auto-linking and BIMIssueRaiseCommand use the five
// parameters read through ClassificationReader — missing data shows up as
// un-grouped rows, blank spec columns, and "no RFI target" warnings.
//
// Reports Info (not Warning) so it never fails the RunAll gate. A later
// pack can tighten to Warning once corporate families have been back-filled.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class ClassificationAuditValidator
    {
        public string Name => "ClassificationAuditValidator";
        private const string ValidatorTag = "ClassificationAuditValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_SpecialityEquipment,
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                int totalTypes = 0, missingUniclass = 0, missingNbs = 0, missingRfi = 0;
                var seenTypes = new HashSet<ElementId>();
                foreach (var el in col)
                {
                    var typeId = el.GetTypeId();
                    if (typeId == ElementId.InvalidElementId) continue;
                    if (!seenTypes.Add(typeId)) continue;   // once per type
                    totalTypes++;

                    var info = Classification.ClassificationReader.Read(el);
                    if (!info.HasAnyUniclass) missingUniclass++;
                    if (string.IsNullOrEmpty(info.NbsCode)) missingNbs++;
                    if (string.IsNullOrEmpty(info.AssetRfiUrl)) missingRfi++;
                }

                if (totalTypes == 0) return results;

                results.Add(new ValidationResult(ElementId.InvalidElementId,
                    ValidationSeverity.Info,
                    "CLS.SCAN",
                    $"Classification audit: {totalTypes} type(s) scanned, " +
                    $"{missingUniclass} missing Uniclass, " +
                    $"{missingNbs} missing NBS, " +
                    $"{missingRfi} missing RFI URL",
                    ValidatorTag));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClassificationAuditValidator: scan failed: {ex.Message}");
            }
            return results;
        }
    }
}
