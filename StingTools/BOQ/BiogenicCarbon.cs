namespace StingTools.BOQ
{
    /// <summary>
    /// Z-25b — WLCA fossil/biogenic carbon split for the embodied-carbon report.
    ///
    /// PROFESSIONAL CONVENTION (chartered carbon engineer call):
    ///   • HEADLINE / benchmark figure = A1-A3 FOSSIL (gross upfront carbon),
    ///     sequestration EXCLUDED.
    ///   • A1-A3 BIOGENIC reported as a SEPARATE, informational line (≤ 0).
    ///   • NET (fossil + biogenic) reported as a third whole-life-context line.
    ///
    /// Basis:
    ///   • RICS "Whole Life Carbon Assessment for the Built Environment",
    ///     2nd ed. (2023): biogenic carbon is reported separately and is NOT
    ///     netted into the headline embodied/upfront figure used for
    ///     benchmarking; a −biogenic uptake must be balanced by a +biogenic
    ///     release at end of life (Module C) unless permanent storage is
    ///     evidenced — so leading with net would flatter a timber scheme.
    ///   • RIBA 2030 Climate Challenge: upfront-carbon targets (kgCO₂e/m²) are
    ///     GROSS; sequestration is excluded from the benchmark comparison.
    ///   • LETI Embodied Carbon Primer / Climate Emergency Design Guide:
    ///     report sequestered carbon separately, never netted into upfront.
    ///
    /// Pure (no Revit dependency) so it is unit-tested in StingTools.Boq.Tests.
    /// </summary>
    public static class BiogenicCarbon
    {
        /// <summary>ICE v3.0 sawn-softwood A1-A3 FOSSIL factor (kgCO₂e/kg),
        /// FSC/PEFC — matches the Z-25 PROP_CARBON_FOSSIL_KG_M3 split.</summary>
        public const double TimberFossilPerKg = 0.263;

        /// <summary>ICE v3.0 / RICS WLCA biogenic sequestration (kgCO₂e/kg),
        /// carbon-content method — species-independent, ≤ 0.</summary>
        public const double TimberBiogenicPerKg = -1.64;

        /// <summary>True for carbon-sequestering (bio-based) materials —
        /// timber / wood. These are the only rows that carry a biogenic term.</summary>
        public static bool IsBiogenic(string materialName)
        {
            string n = (materialName ?? "").ToLowerInvariant();
            return n.Contains("timber") || n.Contains("wood")
                || n.Contains("softwood") || n.Contains("hardwood")
                || n.Contains("plywood") || n.Contains("ply ") || n.Contains("mdf")
                || n.Contains("clt") || n.Contains("glulam");
        }

        /// <summary>
        /// A1-A3 FOSSIL factor (kgCO₂e/kg) — the HEADLINE basis. For bio-based
        /// materials returns the ICE fossil-only value (sequestration excluded);
        /// otherwise the caller's gross per-kg factor is already fossil-only and
        /// is returned unchanged via <paramref name="grossFactorPerKg"/>.
        /// </summary>
        public static double FossilFactorPerKg(string materialName, double grossFactorPerKg)
            => IsBiogenic(materialName) ? TimberFossilPerKg : grossFactorPerKg;

        /// <summary>A1-A3 BIOGENIC factor (kgCO₂e/kg) — ≤ 0 for timber, 0 for
        /// everything else (steel / concrete / glass / etc. sequester nothing).</summary>
        public static double BiogenicFactorPerKg(string materialName)
            => IsBiogenic(materialName) ? TimberBiogenicPerKg : 0.0;
    }
}
