// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/CarbonStageTracker.cs — S6.8 (N-G13).
//
// Extends existing CarbonTrackingCommands to per-stage carbon:
//   A1-A3 product stage (manufacturing)    — from MATERIAL_LOOKUP
//   A4     transport to site               — MAT_DISTANCE_KM × factor
//   A5     construction-install             — CST_INSTALL_HRS × factor
//   B6     operational energy annual       — from MEP system sizing
//   C1-C4  deconstruction + waste + disposal — from MAT_EOL_FACTOR
//
// Writes the per-stage kgCO2e values into the v6 parameters (S1.1):
// CBN_A1_A3_KG_CO2E, CBN_A4_KG_CO2E, CBN_A5_KG_CO2E, CBN_B6_KG_CO2E_YR,
// CBN_C1_KG_CO2E, CBN_C2_KG_CO2E, CBN_C3_C4_KG_CO2E.
//
// Exports an ISO 14064-2 compliant report (CSV + PDF stub) with
// totals by stage, per-discipline breakdown, and LETI 2030 /
// RIBA 2030 Challenge benchmark comparison.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class CarbonStageResult
    {
        public double TotalA1A3 { get; set; }
        public double TotalA4 { get; set; }
        public double TotalA5 { get; set; }
        public double TotalB6AnnualKgYr { get; set; }
        public double TotalC1 { get; set; }
        public double TotalC2 { get; set; }
        public double TotalC3C4 { get; set; }
        public int ElementsProcessed { get; set; }
        public Dictionary<string, double> ByDisciplineA1A3 { get; set; } = new Dictionary<string, double>();
        public string ExportPath { get; set; } = string.Empty;

        public double TotalLifecycleOver60y() =>
            TotalA1A3 + TotalA4 + TotalA5 + (TotalB6AnnualKgYr * 60) + TotalC1 + TotalC2 + TotalC3C4;
    }

    public static class CarbonStageTracker
    {
        // Default emission factors (kgCO2e / unit) loaded from
        // STING_CARBON_FACTORS.json when available — values here are
        // ICE database v3.0 building-industry averages.
        public const double A4TransportFactorKgPerKmTonne = 0.105;
        public const double A5InstallFactorKgPerHr        = 6.2;
        // PM-1 — B6 is no longer a hard-coded 0.233 kgCO2e/kWh (UK grid); it comes
        // from GridCarbonRegistry per project country (Uganda 0.05). See ResolveGridFactor.
        public const double C1DeconstructKgPerM3          = 3.1;
        public const double C2TransportFactorKgPerKm       = 0.085;
        public const double C3C4DisposalKgPerKg            = 0.04;

        public static CarbonStageResult Compute(Document doc)
        {
            var res = new CarbonStageResult();
            if (doc == null) return res;

            var byDisc = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // PM-1 — B6 operational factor from GridCarbonRegistry (Uganda 0.05),
            // not the hard-coded 0.233 (≈5× too high for Uganda's hydro grid).
            double gridFactor = ResolveGridFactor(doc);

            try
            {
                TransactionHelper.RunInScope(doc, "STING carbon stage tracker", t =>
                {
                    // SUS-2 — walk the SAME WBLCA take-off scope the EDGE dashboard uses
                    // (was SharedParamGuids.AllCategoryEnums, which made the two whole-life
                    // numbers disagree). One shared category list ends that drift.
                    var col = new FilteredElementCollector(doc)
                        .WherePasses(new ElementMulticategoryFilter(
                            StingTools.Core.Sustainability.SustainabilityEngine.WblcaCategories))
                        .WhereElementIsNotElementType();
                    foreach (var el in col)
                    {
                        // A1-A3: reuse existing CarbonTrackingCommands
                        // embodied value if previously computed;
                        // otherwise estimate from volume × material.
                        double a1a3 = ReadDouble(el, "CBN_EMBODIED_KG_CO2E");
                        if (a1a3 <= 0) a1a3 = EstimateA1A3(el);
                        WriteDouble(el, ParamRegistry.CBN_A1_A3_KG_CO2E, a1a3);
                        // WP0 — also stamp the ONE canonical embodied-carbon store
                        // (CST_EMBODIED_CARBON_KG, the same param CostStamp/BOQ
                        // writes) so the EN 15978 tracker and the BOQ never report
                        // the embodied figure under two different parameter names.
                        WriteDouble(el, "CST_EMBODIED_CARBON_KG", a1a3);
                        res.TotalA1A3 += a1a3;
                        string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                        if (!string.IsNullOrEmpty(disc))
                            byDisc[disc] = byDisc.TryGetValue(disc, out var v) ? v + a1a3 : a1a3;

                        // A4 transport. Requires MAT_DISTANCE_KM shared
                        // parameter + MAT_WEIGHT_KG / 1000 (tonnes).
                        double distKm = ReadDouble(el, "MAT_DISTANCE_KM");
                        double massT  = ReadDouble(el, "MAT_WEIGHT_KG") / 1000.0;
                        double a4 = distKm * massT * A4TransportFactorKgPerKmTonne;
                        WriteDouble(el, ParamRegistry.CBN_A4_KG_CO2E, a4);
                        res.TotalA4 += a4;

                        // A5 install. CST_INSTALL_HRS (S1.1 new param).
                        double hrs = ReadDouble(el, ParamRegistry.CST_INSTALL_HRS);
                        double a5 = hrs * A5InstallFactorKgPerHr;
                        WriteDouble(el, ParamRegistry.CBN_A5_KG_CO2E, a5);
                        res.TotalA5 += a5;

                        // B6 operational (only MEP equipment). Uses
                        // RGL_ENERGY_KWH_YR (existing) if present.
                        double kwh = ReadDouble(el, "RGL_ENERGY_KWH_YR");
                        double b6  = kwh * gridFactor;   // PM-1 — region grid factor
                        WriteDouble(el, ParamRegistry.CBN_B6_KG_CO2E_YR, b6);
                        res.TotalB6AnnualKgYr += b6;

                        // C1 deconstruction from volume.
                        double volCuFt = ReadDouble(el, "MAT_VOLUME_CUFT");
                        double volM3   = volCuFt * 0.0283168;
                        double c1      = volM3 * C1DeconstructKgPerM3;
                        WriteDouble(el, ParamRegistry.CBN_C1_KG_CO2E, c1);
                        res.TotalC1 += c1;

                        // C2 transport to disposal (assume 50 km avg).
                        double c2 = 50.0 * (massT * 1000) * 1e-3 * C2TransportFactorKgPerKm;
                        WriteDouble(el, ParamRegistry.CBN_C2_KG_CO2E, c2);
                        res.TotalC2 += c2;

                        // C3-C4 waste processing + disposal.
                        double c3c4 = (massT * 1000) * C3C4DisposalKgPerKg;
                        WriteDouble(el, ParamRegistry.CBN_C3_C4_KG_CO2E, c3c4);
                        res.TotalC3C4 += c3c4;

                        res.ElementsProcessed++;
                    }
                });
            }
            catch (Exception ex)
            {
                StingLog.Error("CarbonStageTracker.Compute failed", ex);
            }

            res.ByDisciplineA1A3 = byDisc;

            // SUS-2 — surface the SAME A1–A3 the EDGE dashboard reports. The per-element
            // loop above (scope-aligned to WblcaCategories) stamps the param store + the
            // discipline split; the headline A1–A3 is then reconciled to the EDGE engine's
            // canonical WBLCA take-off (per-material aggregation of the same elements +
            // shared resolver) so CarbonStageTracker.TotalA1A3 == res.Materials.TotalCarbonKg.
            // The discipline split is rescaled to stay consistent with the reconciled total.
            try
            {
                double edgeA1A3 = StingTools.Core.Sustainability.SustainabilityEngine.WblcaA1A3Kg(doc);
                if (edgeA1A3 > 0)
                {
                    if (res.TotalA1A3 > 0)
                    {
                        double k = edgeA1A3 / res.TotalA1A3;
                        foreach (var key in byDisc.Keys.ToList()) byDisc[key] *= k;
                    }
                    res.TotalA1A3 = edgeA1A3;
                    res.ByDisciplineA1A3 = byDisc;
                }
            }
            catch (Exception ex) { StingLog.Warn($"CarbonStageTracker SUS-2 reconcile: {ex.Message}"); }

            res.ExportPath       = ExportIsoReport(doc, res);
            return res;
        }

        private static double EstimateA1A3(Element el)
        {
            // WP0 — delegate to the ONE canonical per-element carbon resolver
            // (BOQCostManager → CarbonFactorResolver: EPD → material param →
            // lookup CSV → legacy) instead of the flat 350 kgCO₂e/m³ concrete
            // proxy that diverged from the BOQ figure for every other material.
            double volM3 = ReadDouble(el, "MAT_VOLUME_CUFT") * 0.0283168;
            if (volM3 <= 0)
            {
                try
                {
                    var p = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    if (p != null && p.HasValue) volM3 = p.AsDouble() * 0.0283168;
                }
                catch (Exception ex) { StingLog.Warn($"CarbonStageTracker.EstimateA1A3 vol: {ex.Message}"); }
            }
            return StingTools.BOQ.BOQCostManager.ComputeElementCarbonKg(el, volM3);
        }

        /// <summary>PM-1 — the operational (B6) grid carbon factor (kgCO2e/kWh) for
        /// the project country via GridCarbonRegistry (corporate seed + project
        /// override). Country from PRJ_COUNTRY config, defaulting to Uganda ("UG")
        /// for the East-Africa deployment. Falls back to 0.05 (Uganda hydro grid)
        /// only if the registry can't be read.</summary>
        private static double ResolveGridFactor(Document doc)
        {
            try
            {
                string country = TagConfig.GetConfigValue("PRJ_COUNTRY");
                if (string.IsNullOrWhiteSpace(country)) country = "UG";
                var reg = StingTools.Core.Sustainability.SustainabilityRegistries.GridCarbon(doc);
                var res = reg?.Resolve(country);
                return (res != null && res.Factor > 0) ? res.Factor : 0.05;
            }
            catch (Exception ex) { StingLog.Warn($"CarbonStageTracker.ResolveGridFactor: {ex.Message}"); return 0.05; }
        }

        private static double ReadDouble(Element el, string paramName)
        {
            var p = el.LookupParameter(paramName);
            if (p == null || !p.HasValue) return 0.0;
            return p.StorageType == StorageType.Double ? p.AsDouble() : 0.0;
        }

        private static void WriteDouble(Element el, string paramName, double value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"CarbonStageTracker.WriteDouble('{paramName}'): {ex.Message}"); }
        }

        private static string ExportIsoReport(Document doc, CarbonStageResult r)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName) ??
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string name = $"STING_CARBON_ISO14064_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
                string path = Path.Combine(dir, name);
                var sb = new StringBuilder();
                sb.AppendLine("STING v6 ISO 14064-2 Whole-Life Carbon Report");
                sb.AppendLine($"Project: {Path.GetFileName(doc.PathName)}");
                sb.AppendLine($"Generated: {DateTime.UtcNow:u}");
                sb.AppendLine();
                sb.AppendLine("Stage,KgCO2e");
                sb.AppendLine($"A1-A3 Product,{r.TotalA1A3:F2}");
                sb.AppendLine($"A4 Transport,{r.TotalA4:F2}");
                sb.AppendLine($"A5 Install,{r.TotalA5:F2}");
                sb.AppendLine($"B6 Operational / year,{r.TotalB6AnnualKgYr:F2}");
                sb.AppendLine($"B6 60-year operational,{r.TotalB6AnnualKgYr * 60:F2}");
                sb.AppendLine($"C1 Deconstruction,{r.TotalC1:F2}");
                sb.AppendLine($"C2 Transport,{r.TotalC2:F2}");
                sb.AppendLine($"C3-C4 Disposal,{r.TotalC3C4:F2}");
                sb.AppendLine($"TOTAL (60y lifecycle),{r.TotalLifecycleOver60y():F2}");
                sb.AppendLine();
                // PM-1/PM-5 — benchmarks come from the SAME project/region config the
                // BOQ panel RAG uses (one source of truth), not hard-coded UK LETI/RIBA.
                double greenKgM2 = TagConfig.GetConfigDouble("COST_CARBON_RAG_GREEN_KGM2", 400.0);
                double amberKgM2 = TagConfig.GetConfigDouble("COST_CARBON_RAG_AMBER_KGM2", 700.0);
                sb.AppendLine("Benchmark,KgCO2e/m2,Source");
                sb.AppendLine($"Carbon-intensity green band,{greenKgM2:F0},project/region (COST_CARBON_RAG_GREEN_KGM2)");
                sb.AppendLine($"Carbon-intensity amber band,{amberKgM2:F0},project/region (COST_CARBON_RAG_AMBER_KGM2)");
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Error("CarbonStageTracker.ExportIsoReport", ex);
                return string.Empty;
            }
        }
    }
}
