// StingTools — Sustainability material-carbon resolution (Phase 195, WS A3).
//
// Pure POCO / Revit-free + unit-tested. This is the single, testable place that
// turns ONE material's resolved factors into the carbon + energy + mass numbers
// the MaterialsRollup consumes. The Revit-facing SustainabilityEngine resolves
// the raw factors from the model (CarbonFactorResolver, MaterialLookupCsv,
// material params) and passes them here as plain numbers, so the arithmetic
// below stays Revit-free and verifiable.
//
// WS A3 closes four gaps the old GatherMaterialLines had vs. the BOQ takeoff:
//   1) per-kg carbon factors were dropped (only per-m³ honoured) — now applied
//      via material density, mirroring BOQCostManager.ComputeElementCarbon.
//   2) no wastage allowance — now grosses the measured volume by the same
//      COST_DEFAULT_WASTE_PCT knob the BOQ uses (BOQ/WasteFactor).
//   3) biogenic sequestration never credited — now folds the A1-A3 biogenic
//      term into net carbon (BOQ/BiogenicCarbon split), so a timber scheme's
//      net upfront carbon is correct, not fossil-only.
//   4) SustainProjectSetup.FactorSources was stored and never used — now drives
//      whether a mass-based DB (ICE/Ecoinvent) per-kg fallback is permitted and
//      records EPD preference, so the project's dataset order changes the result.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.BOQ;   // WasteFactor + BiogenicCarbon (pure, Revit-free)

namespace StingTools.Core.Sustainability
{
    /// <summary>Which basis a material's carbon factor was applied on.</summary>
    public enum MaterialFactorBasis { None, PerM3, PerKgViaDensity }

    /// <summary>Raw, model-resolved factors for one material. The Revit adapter
    /// fills these; every field is a plain number so the compute stays pure.</summary>
    public class MaterialCarbonInputs
    {
        public string Material   { get; set; } = "";
        public double VolumeM3   { get; set; }
        public double DensityKgM3{ get; set; }
        public double WastePercent { get; set; }

        /// <summary>Net carbon factor expressed per m³ (material param / lookup-csv);
        /// 0 when no volumetric factor resolved.</summary>
        public double NetFactorPerM3 { get; set; }
        /// <summary>Net carbon factor expressed per kg (legacy ICE/mass DB); 0 when
        /// no mass-based factor resolved.</summary>
        public double NetFactorPerKg { get; set; }
        /// <summary>True when the per-m³ factor came from a product-specific EPD
        /// (SUS_EPD_REF_TXT / material param) rather than a generic library row.</summary>
        public bool   FactorIsEpdSpecific { get; set; }

        /// <summary>A1-A3 WLCA fossil + biogenic split, per m³ (BOQ/CarbonFactorResolver
        /// GetCarbonFossilPerM3 / GetCarbonBiogenicPerM3). Biogenic is ≤ 0. Both 0 ⇒
        /// the compute derives a timber split from BiogenicCarbon constants when the
        /// material is bio-based.</summary>
        public double FossilFactorPerM3 { get; set; }
        public double BiogenicFactorPerM3 { get; set; }

        /// <summary>Embodied-energy factors. Per-m³ (material/EPD PERT+PENRT) preferred;
        /// per-kg (ICE MJ) used when a mass-energy DB is permitted; else the documented
        /// ratio fallback is applied to the fossil carbon.</summary>
        public double EnergyMjPerM3 { get; set; }
        public double EnergyMjPerKg { get; set; }
    }

    public class MaterialCarbonOutputs
    {
        public double GrossVolumeM3 { get; set; }
        public double MassKg        { get; set; }
        public double NetCarbonKg   { get; set; }
        public double FossilCarbonKg { get; set; }
        public double BiogenicCarbonKg { get; set; }
        public double EnergyMj      { get; set; }
        public MaterialFactorBasis Basis { get; set; } = MaterialFactorBasis.None;
        public string CarbonSource  { get; set; } = "none";
        public string EnergySource  { get; set; } = "none";
        public bool   FromEpd       { get; set; }
        /// <summary>True when the carbon came from a GENERIC per-material-class
        /// indicative factor (no EPD / library / WLCA-split match). The figure is
        /// a rough order-of-magnitude estimate — it populates the hotspots + an
        /// indicative kgCO₂e/m² display but MUST NOT be treated as a real WBLCA
        /// (the rollup keeps it out of CarbonStampedLines / Computed).</summary>
        public bool   IndicativeOnly { get; set; }
    }

    public static class SustainMaterialCarbon
    {
        /// <summary>Indicative CED:GWP ratio (~12 MJ per kgCO₂e of upfront fossil
        /// carbon for common construction materials) — a documented placeholder used
        /// only when no real embodied-energy factor (EPD PERT+PENRT / ICE MJ) is
        /// available. Matches the legacy GatherMaterialLines fallback.</summary>
        public const double IndicativeMjPerKgCo2e = 12.0;

        /// <summary>Generic per-material-class A1-A3 carbon factors, kgCO₂e per kg
        /// (rounded ICE v3 / RICS order-of-magnitude values). Used ONLY when no
        /// real factor (EPD / library per-m³ / per-kg / WLCA split) resolves AND
        /// the material isn't bio-based — so a model whose materials aren't in the
        /// carbon DB still produces an INDICATIVE figure instead of 0. Keyword
        /// match against the material name; longest/most-specific keyword wins.</summary>
        private static readonly (string Key, double KgCo2ePerKg)[] IndicativeClassFactors =
        {
            ("reinforced concrete", 0.16), ("concrete", 0.13), ("screed", 0.13),
            ("mortar", 0.20), ("cement", 0.83), ("blockwork", 0.10), ("block", 0.10),
            ("brick", 0.24), ("stone", 0.08), ("aggregate", 0.01), ("gravel", 0.01),
            ("stainless steel", 6.15), ("steel", 1.55), ("rebar", 1.40), ("metal", 1.55),
            ("aluminium", 8.50), ("aluminum", 8.50), ("copper", 2.80), ("zinc", 3.90),
            ("glass", 1.35), ("glazing", 1.35),
            ("plasterboard", 0.39), ("gypsum", 0.39), ("plaster", 0.13),
            ("insulation", 1.50), ("mineral wool", 1.20),
            ("ceramic", 0.78), ("tile", 0.78), ("porcelain", 0.78),
            ("pvc", 3.10), ("plastic", 3.10), ("polymer", 3.10), ("membrane", 2.50),
            ("asphalt", 0.07), ("bitumen", 0.45),
            ("plywood", 0.45), ("mdf", 0.30), ("chipboard", 0.30),
            ("paint", 2.10), ("render", 0.20),
        };

        /// <summary>Default indicative factor when no keyword matches — a broad
        /// mid-range construction-material value. Clearly flagged as indicative.</summary>
        public const double IndicativeDefaultKgCo2ePerKg = 0.50;

        /// <summary>Resolve a generic indicative carbon factor (kgCO₂e/kg) for a
        /// material name. Longest matching keyword wins; falls back to the broad
        /// default. Public for unit tests.</summary>
        public static double IndicativeClassFactorPerKg(string material)
        {
            string lc = (material ?? "").ToLowerInvariant();
            double best = 0; int bestLen = -1;
            foreach (var (key, f) in IndicativeClassFactors)
                if (lc.Contains(key) && key.Length > bestLen) { best = f; bestLen = key.Length; }
            return bestLen >= 0 ? best : IndicativeDefaultKgCo2ePerKg;
        }

        /// <summary>Mass-based carbon datasets (ICE / Ecoinvent) — when none of these
        /// appear in the project's FactorSources.EmbodiedCarbon order, the per-kg
        /// (mass × factor) fallback is disabled and a per-kg-only material resolves
        /// to 0 carbon (volumetric EPD/EC3 factors only).</summary>
        private static readonly string[] MassCarbonDbKeys =
            { "ICE_v3", "ICE", "Ecoinvent" };

        /// <summary>Mass-based energy datasets (ICE MJ) for the per-kg energy path.</summary>
        private static readonly string[] MassEnergyDbKeys =
            { "ICE_v3_MJ", "ICE_MJ", "regional_db" };

        /// <summary>Resolve one material's carbon + energy + mass, honouring waste,
        /// the WLCA fossil/biogenic split, and the project's FactorSources order.</summary>
        public static MaterialCarbonOutputs Compute(MaterialCarbonInputs input, FactorSourceOrder order)
        {
            var o = new MaterialCarbonOutputs();
            if (input == null || input.VolumeM3 <= 0) return o;

            order = order ?? new FactorSourceOrder();

            // ── 1. Waste-grossed measured volume (m³ is a measured quantity) ──
            o.GrossVolumeM3 = WasteFactor.Apply(input.VolumeM3, "m³", input.WastePercent);
            o.MassKg = input.DensityKgM3 > 0 ? o.GrossVolumeM3 * input.DensityKgM3 : 0;

            bool allowMassCarbon = OrderPermits(order.EmbodiedCarbon, MassCarbonDbKeys);

            // ── 2. Net carbon basis: volumetric factor wins; per-kg only when a
            //       mass DB is permitted by FactorSources AND density is known. ──
            double netCarbon = 0;
            if (input.NetFactorPerM3 > 0)
            {
                netCarbon = o.GrossVolumeM3 * input.NetFactorPerM3;
                o.Basis = MaterialFactorBasis.PerM3;
                o.CarbonSource = input.FactorIsEpdSpecific ? "epd-per-m3" : "lookup-per-m3";
                o.FromEpd = input.FactorIsEpdSpecific;
            }
            else if (input.NetFactorPerKg > 0 && allowMassCarbon && o.MassKg > 0)
            {
                netCarbon = o.MassKg * input.NetFactorPerKg;
                o.Basis = MaterialFactorBasis.PerKgViaDensity;
                o.CarbonSource = "ice-per-kg";
            }
            else
            {
                o.Basis = MaterialFactorBasis.None;
                o.CarbonSource = (input.NetFactorPerKg > 0 && !allowMassCarbon)
                    ? "per-kg-disabled-by-factorsources"
                    : "none";
            }

            // ── 3. Fossil / biogenic split — credit sequestration into net ──
            //   (a) explicit per-m³ split from the resolver (timber rows in the
            //       material library) — authoritative; net := fossil + biogenic.
            //   (b) else, bio-based material with a known mass → derive from the
            //       BiogenicCarbon ICE constants; net := fossil + biogenic.
            //   (c) else → fossil = net, biogenic = 0 (steel/concrete/glass etc.).
            if (input.FossilFactorPerM3 != 0 || input.BiogenicFactorPerM3 < 0)
            {
                o.FossilCarbonKg   = o.GrossVolumeM3 * input.FossilFactorPerM3;
                o.BiogenicCarbonKg = o.GrossVolumeM3 * input.BiogenicFactorPerM3;
                o.NetCarbonKg = o.FossilCarbonKg + o.BiogenicCarbonKg;
                if (o.Basis == MaterialFactorBasis.None) o.Basis = MaterialFactorBasis.PerM3;
                if (o.CarbonSource == "none") o.CarbonSource = "wlca-split-per-m3";
            }
            else if (BiogenicCarbon.IsBiogenic(input.Material) && o.MassKg > 0)
            {
                o.FossilCarbonKg   = o.MassKg * BiogenicCarbon.TimberFossilPerKg;
                o.BiogenicCarbonKg = o.MassKg * BiogenicCarbon.TimberBiogenicPerKg;
                o.NetCarbonKg = o.FossilCarbonKg + o.BiogenicCarbonKg;
                if (o.Basis == MaterialFactorBasis.None) o.Basis = MaterialFactorBasis.PerKgViaDensity;
                if (o.CarbonSource == "none") o.CarbonSource = "biogenic-constants";
            }
            else if (netCarbon != 0)
            {
                o.NetCarbonKg = netCarbon;
                o.FossilCarbonKg = netCarbon;
                o.BiogenicCarbonKg = 0;
            }
            else if (o.MassKg > 0 && input.NetFactorPerM3 <= 0 && input.NetFactorPerKg <= 0)
            {
                // No factor resolved AT ALL (material absent from the EPD / library /
                // mass DB — not merely disabled by FactorSources) but we know the
                // mass — apply a GENERIC class factor so the line carries an
                // INDICATIVE figure instead of 0. Flagged indicative so the rollup
                // keeps it out of the real WBLCA. A per-kg factor that exists but is
                // disabled by FactorSources is a deliberate exclusion → stays 0.
                double f = IndicativeClassFactorPerKg(input.Material);
                o.FossilCarbonKg   = o.MassKg * f;
                o.NetCarbonKg      = o.FossilCarbonKg;
                o.BiogenicCarbonKg = 0;
                o.Basis = MaterialFactorBasis.PerKgViaDensity;
                o.CarbonSource = "indicative-class";
                o.IndicativeOnly = true;
            }
            // else: no factor + no mass (unknown density) → genuinely 0; stays 0.

            // ── 4. Embodied energy (MJ) — real factor first, ratio last ──
            bool allowMassEnergy = OrderPermits(order.EmbodiedEnergy, MassEnergyDbKeys);
            if (input.EnergyMjPerM3 > 0)
            {
                o.EnergyMj = o.GrossVolumeM3 * input.EnergyMjPerM3;
                o.EnergySource = "epd-pert-penrt";
            }
            else if (input.EnergyMjPerKg > 0 && allowMassEnergy && o.MassKg > 0)
            {
                o.EnergyMj = o.MassKg * input.EnergyMjPerKg;
                o.EnergySource = "ice-mj-per-kg";
            }
            else
            {
                // Ratio fallback keys off the GROSS FOSSIL upfront carbon (process
                // energy correlates with fossil, not the biogenic credit). When no
                // split exists this equals |net|.
                double basisCarbon = o.FossilCarbonKg != 0 ? Math.Abs(o.FossilCarbonKg) : Math.Abs(o.NetCarbonKg);
                o.EnergyMj = basisCarbon * IndicativeMjPerKgCo2e;
                o.EnergySource = basisCarbon > 0 ? "indicative-ratio" : "none";
            }

            return o;
        }

        /// <summary>True when at least one of <paramref name="keys"/> appears in the
        /// project's dataset order (case-insensitive). A null/empty order is treated
        /// as permissive so a project that never configured FactorSources keeps the
        /// full legacy behaviour.</summary>
        private static bool OrderPermits(List<string> order, string[] keys)
        {
            if (order == null || order.Count == 0) return true;
            return order.Any(s => keys.Any(k => string.Equals(s?.Trim(), k, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
