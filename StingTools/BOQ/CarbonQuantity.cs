// CarbonQuantity — embodied carbon of a quantity of material, in the unit its factor is in.
//
// Revit-free so the arithmetic is tested without a host. A carbon factor comes either per
// m³ (a material's STING_EMB_CARBON_NR, MATERIAL_LOOKUP's CARBON_KG_PER_M3, EPDs, the
// Uganda/EDGE set) or per kg (the ICE keyword fallbacks). The carbon tracking command once
// multiplied every factor by mass, so a per-m³ figure came out about the material's
// density too large: C30 concrete at 300 kgCO₂e/m³ read as 300 × 2,450 kg per m³.

namespace StingTools.BOQ
{
    public enum CarbonFactorUnit { KgCo2ePerM3, KgCo2ePerKg, Unknown }

    public struct CarbonFactorResult
    {
        public double Factor;
        public CarbonFactorUnit PerUnit;
        public string Source;
    }

    public static class CarbonQuantity
    {
        /// <summary>
        /// A1-A3 fossil and biogenic kgCO₂e for <paramref name="volumeM3"/> of a material.
        /// Per-m³ factors multiply the volume; a per-m³ fossil/biogenic split, when the
        /// material has one, is used as given. Per-kg factors multiply the mass (volume ×
        /// density), and a bio-based material takes the timber fossil/biogenic per-kg pair
        /// in place of its gross factor. An unknown unit is no carbon, never a guess.
        /// </summary>
        public static (double FossilKg, double BiogenicKg) Split(
            CarbonFactorResult factor, double volumeM3, double densityKgM3, bool isBiogenic,
            double fossilPerM3 = 0, double biogenicPerM3 = 0)
        {
            if (volumeM3 <= 0) return (0, 0);
            switch (factor.PerUnit)
            {
                case CarbonFactorUnit.KgCo2ePerM3:
                    if (fossilPerM3 > 0 || biogenicPerM3 != 0)
                        return (volumeM3 * fossilPerM3, volumeM3 * biogenicPerM3);
                    if (isBiogenic && densityKgM3 > 0)
                        return (volumeM3 * densityKgM3 * BiogenicCarbon.TimberFossilPerKg,
                                volumeM3 * densityKgM3 * BiogenicCarbon.TimberBiogenicPerKg);
                    return (volumeM3 * factor.Factor, 0);
                case CarbonFactorUnit.KgCo2ePerKg:
                    if (densityKgM3 <= 0) return (0, 0);
                    double massKg = volumeM3 * densityKgM3;
                    return isBiogenic
                        ? (massKg * BiogenicCarbon.TimberFossilPerKg, massKg * BiogenicCarbon.TimberBiogenicPerKg)
                        : (massKg * factor.Factor, 0);
                default:
                    return (0, 0);
            }
        }
    }
}
