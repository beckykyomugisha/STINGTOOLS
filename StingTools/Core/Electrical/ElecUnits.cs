// ElecUnits — reads electrical parameters in SI units (V, VA, W, A).
//
// Revit stores voltage and power in internal units derived from FEET:
// 1 V = 10.7639 internal units (kg·ft²/(s³·A)), and the same factor applies
// to VA and W. Amperes are a base unit and are stored unchanged. A raw
// AsDouble() on a 230 V circuit therefore returns ~2475.7, which made voltage
// drop ~10.8× too small and every kW/kVA figure ~10.8× too large.
//
// The conversion is chosen from the parameter's OWN spec, never from the
// caller's expectation, so it cannot be applied to the wrong kind of value:
// a length or a unitless number passes through untouched.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    internal static class ElecUnits
    {
        /// <summary>
        /// Value of a Double parameter in SI: volts for electrical potential,
        /// VA for apparent power, W for electrical/HVAC power and wattage,
        /// A for current. Any other spec is returned raw. 0 when absent.
        /// </summary>
        public static double ToSi(Parameter p)
        {
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return 0;
            double raw = p.AsDouble();
            ForgeTypeId unit = SiUnitFor(p);
            if (unit == null) return raw;
            try { return UnitUtils.ConvertFromInternalUnits(raw, unit); }
            catch (Exception ex)
            {
                StingLog.Warn($"ElecUnits.ToSi '{p.Definition?.Name}': {ex.Message}");
                return raw;
            }
        }

        /// <summary>SI value of a built-in parameter on an element; 0 when absent.</summary>
        public static double Read(Element el, BuiltInParameter bip)
        {
            try { return ToSi(el?.get_Parameter(bip)); }
            catch (Exception ex) { StingLog.Warn($"ElecUnits.Read {bip}: {ex.Message}"); return 0; }
        }

        /// <summary>Volts from RBS_ELEC_VOLTAGE; 0 when absent.</summary>
        public static double Volts(Element el) => Read(el, BuiltInParameter.RBS_ELEC_VOLTAGE);

        /// <summary>Apparent load in VA from RBS_ELEC_APPARENT_LOAD; 0 when absent.</summary>
        public static double ApparentLoadVA(Element el) => Read(el, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);

        /// <summary>
        /// The SI unit a Double parameter should be read in, or null when the
        /// spec is not an electrical quantity that needs converting.
        /// </summary>
        public static ForgeTypeId SiUnitFor(Parameter p)
        {
            ForgeTypeId spec;
            try { spec = p?.Definition?.GetDataType(); }
            catch (Exception ex) { StingLog.Warn($"ElecUnits.SiUnitFor: {ex.Message}"); return null; }
            if (spec == null) return null;

            if (spec == SpecTypeId.ElectricalPotential) return UnitTypeId.Volts;
            if (spec == SpecTypeId.ApparentPower) return UnitTypeId.VoltAmperes;
            if (spec == SpecTypeId.ElectricalPower
                || spec == SpecTypeId.Wattage
                || spec == SpecTypeId.HvacPower) return UnitTypeId.Watts;
            if (spec == SpecTypeId.Current) return UnitTypeId.Amperes;
            return null;
        }
    }
}
