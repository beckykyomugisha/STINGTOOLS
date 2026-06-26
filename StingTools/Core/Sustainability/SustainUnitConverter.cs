// StingTools — SI/IP unit conversion for the Sustainability module.
//
// Makes the SETUP "Units" option functional: every displayed/exported absolute
// value (intensities + annual energy + per-person water + area) converts to IP
// when the project setup selects it. The EDGE/LEED gates themselves are %, so
// they are unit-independent — only the supporting numbers convert.
//
// Pure POCO — no Revit dependency. Unit-tested.

namespace StingTools.Core.Sustainability
{
    public static class SustainUnitConverter
    {
        // SI → IP factors.
        public const double KwhM2ToKbtuFt2 = 0.316998;   // kWh/m²·yr → kBtu/ft²·yr
        public const double KgM2ToLbFt2    = 0.204816;   // kgCO₂e/m² → lbCO₂e/ft²
        public const double MjM2ToKbtuFt2  = 0.088055;   // MJ/m²     → kBtu/ft²
        public const double KwhToKbtu      = 3.412142;   // kWh       → kBtu
        public const double LToGal         = 0.264172;   // litre     → US gallon
        public const double M2ToFt2        = 10.763910;  // m²        → ft²

        public static bool IsIp(SustainUnits u) => u == SustainUnits.IP;

        public static double Eui(double siKwhM2, SustainUnits u) => IsIp(u) ? siKwhM2 * KwhM2ToKbtuFt2 : siKwhM2;
        public static string EuiUnit(SustainUnits u)             => IsIp(u) ? "kBtu/ft²·yr" : "kWh/m²·yr";

        public static double CarbonIntensity(double siKgM2, SustainUnits u) => IsIp(u) ? siKgM2 * KgM2ToLbFt2 : siKgM2;
        public static string CarbonIntensityUnit(SustainUnits u)            => IsIp(u) ? "lbCO₂e/ft²" : "kgCO₂e/m²";

        public static double EnergyIntensityMj(double siMjM2, SustainUnits u) => IsIp(u) ? siMjM2 * MjM2ToKbtuFt2 : siMjM2;
        public static string EnergyIntensityUnit(SustainUnits u)             => IsIp(u) ? "kBtu/ft²" : "MJ/m²";

        public static double EnergyAbs(double siKwh, SustainUnits u) => IsIp(u) ? siKwh * KwhToKbtu : siKwh;
        public static string EnergyAbsUnit(SustainUnits u)          => IsIp(u) ? "kBtu/yr" : "kWh/yr";

        public static double WaterPerPersonDay(double siL, SustainUnits u) => IsIp(u) ? siL * LToGal : siL;
        public static string WaterPerPersonDayUnit(SustainUnits u)        => IsIp(u) ? "gal/person·day" : "L/person·day";

        public static double Area(double siM2, SustainUnits u) => IsIp(u) ? siM2 * M2ToFt2 : siM2;
        public static string AreaUnit(SustainUnits u)         => IsIp(u) ? "ft²" : "m²";
    }
}
