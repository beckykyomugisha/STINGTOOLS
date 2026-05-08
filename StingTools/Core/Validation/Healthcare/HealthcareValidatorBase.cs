// Healthcare Pack — H-5 validator base.
//
// All healthcare validators expose the same shape as the existing v4
// validators (Validate(Document) → List<ValidationResult>) so they
// chain into RunAllValidatorsCommand without changes. They are also
// re-aggregated by RunAllHealthcareValidatorsCommand so non-healthcare
// projects don't pay the cost.
//
// Healthcare findings are routed back through WarningsManager via
// existing WARN_BLE_MEDICAL_* / WARN_RGL_STD_MEDICAL_* / WARN_ASS_LEAD_TIME_*
// channels (Phase H-1 §5.0.3) so the existing warnings dashboard
// surfaces them without UI changes.

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace StingTools.Core.Validation.Healthcare
{
    public abstract class HealthcareValidatorBase
    {
        public abstract string Name { get; }
        public abstract List<ValidationResult> Validate(Document doc);

        protected static string GetParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Double) return p.AsDouble().ToString("F4");
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.ElementId) return p.AsElementId().IntegerValue.ToString();
                return p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        protected static double? GetParamDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return (double)p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out var v)) return v;
                return null;
            }
            catch { return null; }
        }

        protected static bool GetParamBool(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return false;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
                if (p.StorageType == StorageType.String)
                {
                    var s = (p.AsString() ?? "").Trim().ToUpperInvariant();
                    return s == "1" || s == "Y" || s == "YES" || s == "TRUE";
                }
                return false;
            }
            catch { return false; }
        }
    }
}
