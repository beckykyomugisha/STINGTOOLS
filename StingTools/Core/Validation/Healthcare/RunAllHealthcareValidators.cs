using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>Aggregates all 8 healthcare validators into a single sweep.
    /// Gated on PRJ_ORG_HEALTH_FACILITY_TYPE_TXT being non-empty so generic
    /// projects do not pay the cost.</summary>
    public static class RunAllHealthcareValidators
    {
        public static List<ValidationResult> Validate(Document doc)
        {
            var all = new List<ValidationResult>();
            if (doc == null) return all;

            // Gate: skip entirely when the facility-type stamp is empty.
            string facType = "";
            try
            {
                var pi = doc.ProjectInformation;
                var p = pi?.LookupParameter("PRJ_ORG_HEALTH_FACILITY_TYPE_TXT");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    facType = (p.AsString() ?? "").Trim();
            }
            catch { }
            if (string.IsNullOrEmpty(facType)) return all;

            all.AddRange(new PressureRegimeValidator().Validate(doc));
            all.AddRange(new MgasFlowValidator().Validate(doc));
            all.AddRange(new EesBranchValidator().Validate(doc));
            all.AddRange(new WaterSafetyValidator().Validate(doc));
            all.AddRange(new RadShieldValidator().Validate(doc));
            all.AddRange(new AdjacencyValidator().Validate(doc));
            all.AddRange(new AntiLigatureValidator().Validate(doc));
            all.AddRange(new RdsCompletenessValidator().Validate(doc));
            return all;
        }
    }
}
