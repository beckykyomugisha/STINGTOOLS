// StingTools — Materials rollup, DUAL metric (Phase 195, spec §9).
//
// Two material metric tracks, never conflated:
//   A — embodied CARBON  kgCO2e/m2 GWP (EN 15978)  -> LEED v5 Reduce-EC + hotspots + dashboard
//   B — embodied ENERGY  MJ/m2 (CED)               -> EDGE materials gate (indicative)
//
// This pure-POCO core takes already-resolved material line items (name, quantity,
// volume, embodied-carbon kgCO2e, embodied-energy MJ — both PER LINE, totalled)
// and rolls them up into kgCO2e/m2 + MJ/m2, identifies the three largest carbon
// hotspots, and computes savings % vs an embodied baseline.
//
// The Revit command builds the line items: quantities from BOQ takeoff, carbon
// via CarbonFactorResolver TIER-1 (NOT CarbonTrackingEngine.EnsureLoaded() which
// is dead at runtime), MJ via SUS_MAT_ENERGY_MJ_M2_NR / EPD PERT+PENRT, with an
// EPD-specific factor preferred when SUS_EPD_REF_TXT is set. The split keeps this
// engine Revit-free + unit-testable.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Sustainability
{
    /// <summary>One resolved material line (carbon + energy already computed by
    /// the caller via the resolver chain).</summary>
    public class MaterialLine
    {
        public string Material   { get; set; } = "";
        public string Category   { get; set; } = "";
        public double VolumeM3   { get; set; }
        /// <summary>Embodied carbon for this line, kgCO2e (A1-A3 GWP).</summary>
        public double CarbonKg   { get; set; }
        /// <summary>Embodied energy for this line, MJ (CED / PERT+PENRT).</summary>
        public double EnergyMj   { get; set; }
        /// <summary>True when this line used a product-specific EPD (SUS_EPD_REF_TXT).</summary>
        public bool   FromEpd    { get; set; }
        /// <summary>Provenance of the carbon factor (material-param / lookup-csv / epd).</summary>
        public string CarbonSource { get; set; } = "";
        public string EnergySource { get; set; } = "";
    }

    public class MaterialHotspot
    {
        public string Material  { get; set; } = "";
        public double CarbonKg  { get; set; }
        public double SharePct  { get; set; }
    }

    public class MaterialsRollupResult
    {
        public double TotalCarbonKg { get; set; }
        public double TotalEnergyMj { get; set; }
        public double FloorAreaM2   { get; set; }

        public double CarbonIntensityKgM2 => FloorAreaM2 > 0 ? TotalCarbonKg / FloorAreaM2 : 0;
        public double EnergyIntensityMjM2 => FloorAreaM2 > 0 ? TotalEnergyMj / FloorAreaM2 : 0;

        /// <summary>Three largest embodied-carbon hotspots (LEED v5 prerequisite).</summary>
        public List<MaterialHotspot> Hotspots { get; } = new List<MaterialHotspot>();

        /// <summary>WBLCA completed (A1-A3 GWP computed over a non-empty set).</summary>
        public bool WblcaCompleted { get; set; }

        /// <summary>kgCO2e reduction % vs the embodied-carbon baseline (LEED Reduce-EC).</summary>
        public double GwpReductionPct { get; set; }
        /// <summary>Embodied-energy savings % vs the embodied-energy baseline (EDGE,
        /// INDICATIVE — the EDGE app owns the certified number).</summary>
        public double EmbodiedEnergySavingsPct { get; set; }

        public int LinesFromEpd { get; set; }
        public int TotalLines   { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class MaterialsRollup
    {
        /// <summary>
        /// Roll up resolved material lines into the dual metric.
        /// <paramref name="carbonBaselineKgM2"/> / <paramref name="energyBaselineMjM2"/>
        /// may be 0/null when no baseline exists (savings % then 0; EDGE materials %
        /// is the EDGE app's number anyway). The kgCO2e number is STING's own
        /// indicative WBLCA figure — never claimed as the EDGE materials %.
        /// </summary>
        public static MaterialsRollupResult Rollup(
            IEnumerable<MaterialLine> lines,
            double floorAreaM2,
            double carbonBaselineKgM2 = 0,
            double? energyBaselineMjM2 = null)
        {
            var res = new MaterialsRollupResult { FloorAreaM2 = floorAreaM2 };
            var list = lines?.Where(l => l != null).ToList() ?? new List<MaterialLine>();
            res.TotalLines = list.Count;

            foreach (var l in list)
            {
                res.TotalCarbonKg += l.CarbonKg;
                res.TotalEnergyMj += l.EnergyMj;
                if (l.FromEpd) res.LinesFromEpd++;
            }

            res.WblcaCompleted = list.Count > 0 && res.TotalCarbonKg > 0;

            // Three largest carbon hotspots (group by material name).
            var grouped = list
                .GroupBy(l => string.IsNullOrWhiteSpace(l.Material) ? l.Category : l.Material,
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => new MaterialHotspot
                {
                    Material = g.Key ?? "(unnamed)",
                    CarbonKg = g.Sum(x => x.CarbonKg)
                })
                .Where(h => h.CarbonKg > 0)
                .OrderByDescending(h => h.CarbonKg)
                .ToList();
            double tot = res.TotalCarbonKg > 0 ? res.TotalCarbonKg : 1;
            foreach (var h in grouped.Take(3))
            {
                h.SharePct = h.CarbonKg / tot * 100.0;
                res.Hotspots.Add(h);
            }

            // Savings %: kgCO2e (LEED) + MJ (EDGE indicative). Both vs intensities.
            res.GwpReductionPct = carbonBaselineKgM2 > 0
                ? (carbonBaselineKgM2 - res.CarbonIntensityKgM2) / carbonBaselineKgM2 * 100.0
                : 0;
            if (energyBaselineMjM2.HasValue && energyBaselineMjM2.Value > 0)
                res.EmbodiedEnergySavingsPct =
                    (energyBaselineMjM2.Value - res.EnergyIntensityMjM2) / energyBaselineMjM2.Value * 100.0;
            else
                res.Warnings.Add("No embodied-energy baseline — EDGE materials % is the EDGE app's number (delegated).");

            if (res.TotalCarbonKg <= 0)
                res.Warnings.Add("No embodied-carbon data resolved — stamp STING_EMB_CARBON_NR / SUS_EPD_REF_TXT and re-run.");

            return res;
        }
    }
}
