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
        /// <summary>Material mass for this line, kg (volume×density, waste-grossed).
        /// 0 when density was unavailable. WS A3.</summary>
        public double MassKg     { get; set; }
        /// <summary>Net embodied carbon for this line, kgCO2e (A1-A3 GWP) — INCLUDES
        /// the biogenic credit for bio-based materials (fossil + biogenic).</summary>
        public double CarbonKg   { get; set; }
        /// <summary>A1-A3 fossil (upfront) carbon, kgCO2e (≥ 0) — the RICS/RIBA headline
        /// basis, sequestration excluded. WS A3.</summary>
        public double FossilCarbonKg   { get; set; }
        /// <summary>A1-A3 biogenic carbon, kgCO2e (≤ 0 for timber, 0 otherwise). WS A3.</summary>
        public double BiogenicCarbonKg { get; set; }
        /// <summary>Wastage allowance applied to the measured volume, % (COST_DEFAULT_WASTE_PCT
        /// or per-element override) — same convention as the BOQ takeoff. WS A3.</summary>
        public double WastePercent { get; set; }
        /// <summary>Embodied energy for this line, MJ (CED / PERT+PENRT).</summary>
        public double EnergyMj   { get; set; }
        /// <summary>True when this line used a product-specific EPD (SUS_EPD_REF_TXT).</summary>
        public bool   FromEpd    { get; set; }
        /// <summary>Provenance of the carbon factor (material-param / lookup-csv / epd).</summary>
        public string CarbonSource { get; set; } = "";
        public string EnergySource { get; set; } = "";
        /// <summary>True when the carbon came from a generic indicative class factor
        /// (no EPD / library / WLCA match) — counted toward the indicative display
        /// but NOT the real WBLCA. WS gap fix #3.</summary>
        public bool   IndicativeOnly { get; set; }
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
        /// <summary>Σ A1-A3 fossil (upfront) carbon, kgCO2e — RICS/RIBA headline. WS A3.</summary>
        public double TotalFossilCarbonKg { get; set; }
        /// <summary>Σ A1-A3 biogenic carbon, kgCO2e (≤ 0) — reported separately. WS A3.</summary>
        public double TotalBiogenicCarbonKg { get; set; }
        /// <summary>Σ material mass, kg (waste-grossed). WS A3.</summary>
        public double TotalMassKg   { get; set; }
        public double TotalEnergyMj { get; set; }
        public double FloorAreaM2   { get; set; }

        /// <summary>NET embodied intensity (incl. biogenic credit), kgCO₂e/m² —
        /// the whole-life basis.</summary>
        public double CarbonIntensityKgM2 => FloorAreaM2 > 0 ? TotalCarbonKg / FloorAreaM2 : 0;
        /// <summary>CA-3 — A1-A3 FOSSIL intensity, kgCO₂e/m² — the RICS/RIBA
        /// upfront-carbon HEADLINE. This is the figure the EDGE dashboard now
        /// leads with so it matches the BOQ panel's fossil headline (the BOQ
        /// EmbodiedCarbonKg is also fossil). Net is reported separately as the
        /// whole-life line.</summary>
        public double FossilCarbonIntensityKgM2 => FloorAreaM2 > 0 ? TotalFossilCarbonKg / FloorAreaM2 : 0;
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
        /// <summary>How many measured lines resolved a REAL (EPD / library / WLCA)
        /// non-zero carbon factor. Drives WblcaCompleted + Computed — indicative
        /// class-factor lines are excluded.</summary>
        public int CarbonStampedLines { get; set; }
        /// <summary>How many lines carry carbon from a GENERIC indicative class
        /// factor only (no real DB/EPD match). WS gap fix #3.</summary>
        public int IndicativeCarbonLines { get; set; }
        /// <summary>True when the carbon total is made up wholly of indicative
        /// class-factor lines (no real factor resolved) — the kgCO₂e/m² is an
        /// order-of-magnitude estimate, not a WBLCA.</summary>
        public bool CarbonIsIndicative => CarbonStampedLines == 0 && IndicativeCarbonLines > 0;

        /// <summary>True when a real embodied-ENERGY baseline (MJ/m²) was available
        /// for the EDGE materials %; false ⇒ that % is delegated to the EDGE app.</summary>
        public bool HasEnergyBaseline { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>WS I5 — the single largest carbon contributor + its share. A very
        /// high share points to a quantity/factor error.</summary>
        public string DominantHotspotMaterial { get; set; } = "";
        public double DominantHotspotSharePct { get; set; }
        /// <summary>WS I5 — true when one hotspot dominates (&gt; the sane share) — a
        /// likely quantity/factor error worth surfacing prominently.</summary>
        public bool   DominantHotspotImplausible { get; set; }
        /// <summary>WS I5 — true when the carbon intensity exceeds a sane ceiling for a
        /// whole building (≈10× a normal office) — implausible total.</summary>
        public bool   IntensityImplausible { get; set; }

        /// <summary>WS I5 — one-line coverage figure surfaced on the dashboard + export,
        /// not only the Materials tab. e.g. "15/31 carbon-stamped, 0 EPD".</summary>
        public string CoverageSummary =>
            $"{CarbonStampedLines}/{TotalLines} carbon-stamped, {LinesFromEpd} EPD" +
            (IndicativeCarbonLines > 0 ? $", {IndicativeCarbonLines} indicative" : "");

        /// <summary>True only when a REAL carbon intensity was computed (floor area
        /// non-zero AND at least one line carried a real EPD/library/WLCA factor).
        /// Indicative-only carbon does NOT count as Computed — it's shown flagged
        /// as indicative, never claimed as a WBLCA or used to award a gate.</summary>
        public bool Computed => FloorAreaM2 > 0 && TotalCarbonKg > 0 && CarbonStampedLines > 0;

        /// <summary>WS L8 — fraction of measured lines that carry a real carbon factor.</summary>
        public double CarbonStampedCoverageFraction => TotalLines > 0 ? (double)CarbonStampedLines / TotalLines : 0;

        /// <summary>WS L8 — the embodied-carbon headline must read "indicative — review
        /// quantities" when a sanity flag fires (single-material dominance / implausible
        /// per-m² / indicative-only factors) OR stamped coverage is below ~80%. The
        /// number is still shown (with coverage), never hidden.</summary>
        public bool CarbonHeadlineFlagged =>
            DominantHotspotImplausible || IntensityImplausible || CarbonIsIndicative
            || CarbonStampedCoverageFraction < 0.80;
    }

    public static class MaterialsRollup
    {
        /// <summary>WS I5 — a single material contributing more than this share of the
        /// carbon total is flagged as a likely quantity/factor error.</summary>
        public const double DominantHotspotCeilingPct = 60.0;
        /// <summary>WS I5 — whole-building A1–A3 carbon intensity above this (kgCO₂e/m²)
        /// is implausible (a normal building is a few hundred; ≈10× ⇒ error).</summary>
        public const double IntensityCeilingKgM2 = 2000.0;

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
                res.TotalFossilCarbonKg += l.FossilCarbonKg;
                res.TotalBiogenicCarbonKg += l.BiogenicCarbonKg;
                res.TotalMassKg += l.MassKg;
                res.TotalEnergyMj += l.EnergyMj;
                if (l.FromEpd) res.LinesFromEpd++;
                // A non-zero carbon factor was applied (fossil ≥ 0 even when the net
                // is dragged ≤ 0 by a biogenic credit, so key off fossil OR net).
                bool hasCarbon = l.CarbonKg != 0 || l.FossilCarbonKg != 0;
                if (hasCarbon && l.IndicativeOnly) res.IndicativeCarbonLines++;
                else if (hasCarbon)               res.CarbonStampedLines++;
            }

            res.WblcaCompleted = list.Count > 0 && res.CarbonStampedLines > 0;

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

            // WS I5 — sanity checks: one material dominating the total, or an
            // implausibly high intensity, usually means a quantity/factor error.
            if (grouped.Count > 0 && res.TotalCarbonKg > 0)
            {
                res.DominantHotspotMaterial = grouped[0].Material;
                res.DominantHotspotSharePct = grouped[0].CarbonKg / tot * 100.0;
                if (res.DominantHotspotSharePct > DominantHotspotCeilingPct)
                {
                    res.DominantHotspotImplausible = true;
                    res.Warnings.Add($"Carbon sanity: '{res.DominantHotspotMaterial}' is " +
                                     $"{res.DominantHotspotSharePct:0}% of the total — likely a quantity/factor error " +
                                     "(check the material volume + carbon factor for that line).");
                }
            }
            if (res.FloorAreaM2 > 0 && res.CarbonIntensityKgM2 > IntensityCeilingKgM2)
            {
                res.IntensityImplausible = true;
                res.Warnings.Add($"Carbon sanity: {res.CarbonIntensityKgM2:0} kgCO₂e/m² is implausibly high " +
                                 $"(> {IntensityCeilingKgM2:0}; a typical building is a few hundred) — review quantities/factors.");
            }

            // Savings %: kgCO2e (LEED) + MJ (EDGE indicative). Both vs intensities,
            // guarded against NaN/∞/zero baseline (WS F).
            res.GwpReductionPct = SustainSavings.Pct(carbonBaselineKgM2, res.CarbonIntensityKgM2);
            if (energyBaselineMjM2.HasValue && energyBaselineMjM2.Value > 0)
            {
                res.HasEnergyBaseline = true;
                res.EmbodiedEnergySavingsPct = SustainSavings.Pct(energyBaselineMjM2.Value, res.EnergyIntensityMjM2);
            }
            else
                res.Warnings.Add("No embodied-energy baseline — EDGE materials % is the EDGE app's number (delegated).");

            if (res.FloorAreaM2 <= 0)
                res.Warnings.Add("Materials NOT computed — no floor area (GFA). Enter floor area in Setup, then re-run.");
            if (res.CarbonStampedLines <= 0 && res.IndicativeCarbonLines > 0)
                res.Warnings.Add($"Embodied carbon is INDICATIVE — {res.IndicativeCarbonLines} of {res.TotalLines} " +
                                 "line(s) used generic per-material-class factors (no EPD / library match). The " +
                                 "kgCO₂e/m² is order-of-magnitude only; stamp EPDs (SUS_EPD_REF_TXT) or library " +
                                 "factors for a real WBLCA.");
            else if (res.CarbonStampedLines <= 0)
                res.Warnings.Add($"Materials NOT computed — {res.TotalLines} material(s) measured, 0 carbon-stamped. " +
                                 "Stamp STING_EMB_CARBON_NR / SUS_EPD_REF_TXT (run a carbon pass) and re-run.");
            else if (res.IndicativeCarbonLines > 0)
                res.Warnings.Add($"{res.IndicativeCarbonLines} of {res.TotalLines} material line(s) used indicative " +
                                 "class factors (no EPD/library match); the total mixes real + indicative carbon.");

            return res;
        }
    }
}
