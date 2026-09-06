// StingTools — BOQ take-off unit conversion.
//
// Deliberately Revit-free and in its own file so it can be <Compile Include>d into
// StingTools.Boq.Tests. TakeoffRule.cs imports Autodesk.Revit.DB, which the
// Revit-free test projects cannot link, so the conversion table living there was
// unreachable by any automated test — and a wrong factor here is a silent
// mispricing, not a crash: the quantity is plausible, the rate is plausible, and
// only the total is absurd.
//
// TakeoffRule.ApplyConversion delegates here; this is the only copy of the table.

namespace StingTools.BOQ
{
    /// <summary>Unit conversions a take-off rule can apply between the model's source
    /// value and the unit its rate is priced in.</summary>
    public static class TakeoffUnitConversion
    {
        /// <summary>Convert <paramref name="value"/> per the named conversion. An
        /// unrecognised or absent name returns the value unchanged — a take-off rule
        /// naming a conversion that does not exist must not silently zero a quantity.</summary>
        public static double Apply(double value, string conversion)
        {
            switch ((conversion ?? "none").ToLowerInvariant())
            {
                // A per-tonne rate rule reading a kilogram mass source must scale, or the
                // line overcharges by 1000x. Steel and rebar are the common case: the
                // model carries mass in kg and the rate book prices per tonne.
                case "kg_to_tonne": return value / 1000.0;
                case "ft2_to_m2":   return value * 0.092903;
                case "ft3_to_m3":   return value * 0.0283168;
                case "ft_to_m":     return value * 0.3048;
                case "none":
                default:            return value;
            }
        }
    }
}
