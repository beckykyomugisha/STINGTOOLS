// StingTools — MEP unit conversion helpers.
//
// Centralises the CFM↔L/s and ft↔mm conversions that previously lived
// as magic literals (0.4719, * 304.8) scattered across HVAC commands.
// Reads parameters through UnitUtils so the result is correct regardless
// of the project's display units or the parameter's internal spec.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Mep
{
    public static class MepUnits
    {
        /// <summary>1 CFM = 0.4719474 L/s. Kept as a public constant so
        /// callers that genuinely have CFM (e.g. legacy import) can convert
        /// without repeating the magic number.</summary>
        public const double CfmToLitersPerSecond = 0.4719474;

        /// <summary>Read an air-flow parameter and return it in L/s,
        /// honouring the parameter's actual storage spec. Falls back to
        /// raw AsDouble() for non-flow parameters (e.g. user-bound
        /// shared Number params expressed directly in L/s).</summary>
        public static double ReadAirFlowLs(Element el, string paramName)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return 0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.Double) return 0;
                return ConvertParamToLs(p);
            }
            catch { return 0; }
        }

        /// <summary>Read the Revit built-in duct/pipe flow parameter and
        /// return L/s. Internal units for `RBS_DUCT_FLOW_PARAM` are CFM,
        /// but going through UnitUtils removes the dependency on that
        /// assumption surviving across Revit versions.</summary>
        public static double ReadBuiltInFlowLs(Element el, BuiltInParameter bip)
        {
            if (el == null) return 0;
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || p.StorageType != StorageType.Double) return 0;
                return ConvertParamToLs(p);
            }
            catch { return 0; }
        }

        private static double ConvertParamToLs(Parameter p)
        {
            double raw = p.AsDouble();
            if (raw == 0) return 0;
            try
            {
                // If the parameter is typed as AirFlow / Flow, Revit holds
                // it in internal CFM (HVAC) or GPM (Hydronic) — convert.
                var spec = p.Definition?.GetDataType();
                if (spec != null && (spec == SpecTypeId.AirFlow || spec == SpecTypeId.Flow))
                    return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.LitersPerSecond);
            }
            catch { /* fall through to raw */ }
            // Generic Number parameter: assume the user already entered L/s.
            return raw;
        }

        /// <summary>Read a length-like double parameter in millimetres.
        /// Revit internal length is feet; the conversion factor is 304.8.</summary>
        public static double ReadLengthMm(Element el, string paramName)
        {
            if (el == null) return 0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.Double) return 0;
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            }
            catch { return 0; }
        }
    }
}
