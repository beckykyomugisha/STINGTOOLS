// ============================================================================
// SustainabilityEngine.cs — Phase 69: BREEAM, LCA & Sustainability Analysis
//
// Provides environmental assessment and lifecycle analysis:
//   1. BREEAMAssessor         — BREEAM v6.0 credit scoring (15+ categories)
//   2. LifecycleAssessment    — BS EN 15978 whole-life carbon (A1-C4)
//   3. EmbodiedCarbonDetail   — ICE Database v3.0 material carbon
//   4. OperationalCarbon      — CIBSE TM46 energy benchmarking
//   5. WaterLifecycle         — BREEAM Wat 01 water consumption
//   6. WasteAssessment        — BREEAM Wst 01 construction waste
//   7. CircularityScorer      — Material reuse/recycled content
//
// Standards: BREEAM v6.0 (2024), BS EN 15978, RICS WLC, PAS 2080,
//            CIBSE TM46, Part L 2021, LETI 2020, RIBA 2030 Challenge
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    //  BREEAM CREDIT & CATEGORY DEFINITIONS
    // ════════════════════════════════════════════════════════════════

    internal enum BREEAMRating { Unclassified, Pass, Good, VeryGood, Excellent, Outstanding }

    internal class BREEAMCredit
    {
        public string Category { get; set; }
        public string CreditId { get; set; }
        public string Title { get; set; }
        public int MaxCredits { get; set; }
        public int AwardedCredits { get; set; }
        public double WeightingPct { get; set; }
        public string Evidence { get; set; }
        public string Recommendation { get; set; }
    }

    internal class BREEAMResult
    {
        public double TotalScore { get; set; }
        public BREEAMRating Rating { get; set; }
        public List<BREEAMCredit> Credits { get; set; } = new List<BREEAMCredit>();
        public Dictionary<string, double> CategoryScores { get; set; } = new Dictionary<string, double>();

        public static BREEAMRating ScoreToRating(double score)
        {
            if (score >= 85) return BREEAMRating.Outstanding;
            if (score >= 70) return BREEAMRating.Excellent;
            if (score >= 55) return BREEAMRating.VeryGood;
            if (score >= 45) return BREEAMRating.Good;
            if (score >= 30) return BREEAMRating.Pass;
            return BREEAMRating.Unclassified;
        }
    }

    internal class LCAResult
    {
        public double A1_A3_ProductKgCO2 { get; set; }
        public double A4_TransportKgCO2 { get; set; }
        public double A5_ConstructionKgCO2 { get; set; }
        public double B1_B7_InUseKgCO2 { get; set; }
        public double B6_OperationalEnergyKgCO2 { get; set; }
        public double C1_C4_EndOfLifeKgCO2 { get; set; }
        public double D_BeyondLifeKgCO2 { get; set; }

        public double WholeLifeCarbon => A1_A3_ProductKgCO2 + A4_TransportKgCO2 +
            A5_ConstructionKgCO2 + B1_B7_InUseKgCO2 + B6_OperationalEnergyKgCO2 +
            C1_C4_EndOfLifeKgCO2;
        public double GrossFloorAreaM2 { get; set; }
        public double KgCO2PerM2 => GrossFloorAreaM2 > 0 ? WholeLifeCarbon / GrossFloorAreaM2 : 0;

        public string LETIBenchmark => KgCO2PerM2 switch
        {
            <= 350 => "LETI 2030 Target (≤350 kgCO2e/m²) — PASS",
            <= 500 => "RIBA 2030 Target (≤500 kgCO2e/m²) — PASS",
            <= 800 => "Current Practice (~800 kgCO2e/m²) — Typical",
            _ => "Above average — reduce embodied carbon"
        };

        public List<(string Material, double KgCO2, double Pct)> MaterialBreakdown { get; set; }
            = new List<(string, double, double)>();
    }

    // ════════════════════════════════════════════════════════════════
    //  BREEAM ASSESSOR — v6.0 Credit Assessment
    // ════════════════════════════════════════════════════════════════

    internal static class BREEAMAssessor
    {
        // BREEAM v6.0 category weightings (%)
        private static readonly Dictionary<string, double> _categoryWeights = new Dictionary<string, double>
        {
            { "Management",      12.0 },
            { "Health",          15.0 },
            { "Energy",          19.0 },
            { "Transport",        8.0 },
            { "Water",            6.0 },
            { "Materials",       12.5 },
            { "Waste",            7.5 },
            { "Land Use",        10.0 },
            { "Pollution",        6.5 },
            { "Innovation",      10.0 },
        };

        /// <summary>Assess BREEAM score from model data.</summary>
        public static BREEAMResult Assess(Document doc, double gfaM2, double operationalEnergyKWhM2 = 0)
        {
            var result = new BREEAMResult();

            try
            {
                // Management credits
                AssessManagement(doc, result);
                // Health & Wellbeing
                AssessHealth(doc, result, gfaM2);
                // Energy
                AssessEnergy(doc, result, operationalEnergyKWhM2);
                // Water
                AssessWater(doc, result);
                // Materials
                AssessMaterials(doc, result);
                // Waste
                AssessWaste(doc, result);
                // Land Use & Ecology
                AssessLandUse(doc, result);
                // Pollution
                AssessPollution(doc, result);

                // Calculate weighted score
                foreach (var cat in _categoryWeights)
                {
                    var catCredits = result.Credits.Where(c => c.Category == cat.Key).ToList();
                    int maxPossible = catCredits.Sum(c => c.MaxCredits);
                    int awarded = catCredits.Sum(c => c.AwardedCredits);
                    double catPct = maxPossible > 0 ? (double)awarded / maxPossible * 100.0 : 0;
                    result.CategoryScores[cat.Key] = catPct;
                    result.TotalScore += catPct * cat.Value / 100.0;
                }

                result.Rating = BREEAMResult.ScoreToRating(result.TotalScore);
            }
            catch (Exception ex)
            {
                StingLog.Error("BREEAMAssessor.Assess", ex);
            }

            return result;
        }

        private static void AssessManagement(Document doc, BREEAMResult result)
        {
            // Man 01: Project brief and design — check if BEP exists
            bool hasBep = System.IO.File.Exists(
                System.IO.Path.Combine(StingToolsApp.DataPath ?? "", "project_bep.json"));
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Management", CreditId = "Man 01", Title = "Project brief and design",
                MaxCredits = 4, AwardedCredits = hasBep ? 2 : 0,
                WeightingPct = 12.0,
                Evidence = hasBep ? "BEP file found" : "No BEP",
                Recommendation = hasBep ? "Submit BEP for 2 more credits" : "Create BEP (use STING BEP Wizard)"
            });

            // Man 04: Commissioning and handover
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Management", CreditId = "Man 04", Title = "Commissioning and handover",
                MaxCredits = 4, AwardedCredits = 1,
                WeightingPct = 12.0,
                Evidence = "BIM model present — seasonal commissioning requires O&M evidence",
                Recommendation = "Complete commissioning plan and O&M manual"
            });
        }

        private static void AssessHealth(Document doc, BREEAMResult result, double gfaM2)
        {
            // Hea 01: Visual comfort — check for windows/glazing
            var windows = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType().GetElementCount();
            int heaCredits = windows > 0 ? 2 : 0;
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Health", CreditId = "Hea 01", Title = "Visual comfort (daylighting)",
                MaxCredits = 4, AwardedCredits = heaCredits,
                Evidence = $"{windows} windows found",
                Recommendation = heaCredits < 4 ? "Demonstrate 2% average daylight factor" : ""
            });

            // Hea 02: Indoor air quality
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Health", CreditId = "Hea 02", Title = "Indoor air quality",
                MaxCredits = 3, AwardedCredits = 1,
                Evidence = "Mechanical ventilation assumed",
                Recommendation = "Provide ventilation strategy per CIBSE Guide A"
            });

            // Hea 05: Acoustic performance
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Health", CreditId = "Hea 05", Title = "Acoustic performance",
                MaxCredits = 3, AwardedCredits = 0,
                Evidence = "Run STING Acoustic Analysis for evidence",
                Recommendation = "Complete acoustic analysis per BS 8233 and demonstrate compliance"
            });
        }

        private static void AssessEnergy(Document doc, BREEAMResult result, double energyKWhM2)
        {
            // Ene 01: Reduction of energy use
            int eneCredits = 0;
            string evidence = "";
            if (energyKWhM2 > 0)
            {
                // CIBSE TM46 benchmarks: office typical = 120 kWh/m²/yr
                double reduction = (120.0 - energyKWhM2) / 120.0 * 100.0;
                eneCredits = reduction >= 40 ? 9 : reduction >= 25 ? 6 : reduction >= 10 ? 3 : 1;
                evidence = $"{energyKWhM2:F0} kWh/m²/yr = {reduction:F0}% reduction vs TM46";
            }
            else
            {
                evidence = "No energy data — provide kWh/m²/yr for scoring";
            }

            result.Credits.Add(new BREEAMCredit
            {
                Category = "Energy", CreditId = "Ene 01", Title = "Reduction of energy use and carbon",
                MaxCredits = 15, AwardedCredits = eneCredits,
                Evidence = evidence,
                Recommendation = eneCredits < 9 ? "Target ≤72 kWh/m²/yr (40% reduction) for 9 credits" : ""
            });

            // Ene 04: Low carbon design
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Energy", CreditId = "Ene 04", Title = "Low carbon design",
                MaxCredits = 3, AwardedCredits = 1,
                Evidence = "BIM energy model exists",
                Recommendation = "Complete passive design analysis and feasibility study"
            });
        }

        private static void AssessWater(Document doc, BREEAMResult result)
        {
            // Wat 01: Water consumption
            var plumbFixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType().GetElementCount();

            int watCredits = plumbFixtures > 0 ? 2 : 0;
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Water", CreditId = "Wat 01", Title = "Water consumption",
                MaxCredits = 5, AwardedCredits = watCredits,
                Evidence = $"{plumbFixtures} plumbing fixtures in model",
                Recommendation = "Specify water-efficient fixtures (6/4L dual flush, 6L/min taps) for full credits"
            });

            // Wat 02: Water monitoring
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Water", CreditId = "Wat 02", Title = "Water monitoring",
                MaxCredits = 1, AwardedCredits = 0,
                Evidence = "No water metering detected",
                Recommendation = "Add pulsed water meter on mains supply"
            });
        }

        private static void AssessMaterials(Document doc, BREEAMResult result)
        {
            // Mat 01: Environmental impacts of materials (LCA)
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Materials", CreditId = "Mat 01", Title = "Environmental impacts (LCA)",
                MaxCredits = 6, AwardedCredits = 2,
                Evidence = "Embodied carbon calculation available via STING",
                Recommendation = "Complete BS EN 15978 LCA for superstructure, substructure, envelope"
            });

            // Mat 03: Responsible sourcing
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Materials", CreditId = "Mat 03", Title = "Responsible sourcing",
                MaxCredits = 4, AwardedCredits = 1,
                Evidence = "BRE-certified materials assumed for 1 credit",
                Recommendation = "Specify FSC timber, BES 6001 concrete for additional credits"
            });
        }

        private static void AssessWaste(Document doc, BREEAMResult result)
        {
            // Wst 01: Construction waste management
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Waste", CreditId = "Wst 01", Title = "Construction waste management",
                MaxCredits = 4, AwardedCredits = 1,
                Evidence = "Site Waste Management Plan assumed",
                Recommendation = "Target <7.5 tonnes/100m² and 70% diversion from landfill"
            });

            // Wst 06: Design for disassembly
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Waste", CreditId = "Wst 06", Title = "Design for disassembly and adaptability",
                MaxCredits = 2, AwardedCredits = 0,
                Recommendation = "Include disassembly plan and material passport for circular economy"
            });
        }

        private static void AssessLandUse(Document doc, BREEAMResult result)
        {
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Land Use", CreditId = "LE 01", Title = "Site selection",
                MaxCredits = 3, AwardedCredits = 1,
                Evidence = "Previously developed land assumed",
                Recommendation = "Confirm brownfield site for 2 credits, ecology report for 3"
            });
        }

        private static void AssessPollution(Document doc, BREEAMResult result)
        {
            result.Credits.Add(new BREEAMCredit
            {
                Category = "Pollution", CreditId = "Pol 01", Title = "Impact of refrigerants",
                MaxCredits = 3, AwardedCredits = 1,
                Evidence = "Low-GWP refrigerant assumed",
                Recommendation = "Specify R-32 or R-290 systems for 3 credits"
            });

            result.Credits.Add(new BREEAMCredit
            {
                Category = "Pollution", CreditId = "Pol 03", Title = "Surface water run-off",
                MaxCredits = 5, AwardedCredits = 1,
                Evidence = "SuDS assumed",
                Recommendation = "Size attenuation for 1-in-100yr + climate change"
            });
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFECYCLE ASSESSMENT ENGINE — BS EN 15978
    // ════════════════════════════════════════════════════════════════

    internal static class LifecycleAssessmentEngine
    {
        // CA-3 — the ICE factor table + transport constants removed; carbon
        // now resolves through CarbonFactorResolver via the canonical stage tracker.

        /// <summary>Run full BS EN 15978 lifecycle assessment.</summary>
        public static LCAResult Assess(Document doc, double gfaM2, double buildingLifeYears = 60,
            double operationalEnergyKWhM2Yr = 100)
        {
            var result = new LCAResult { GrossFloorAreaM2 = gfaM2 };
            try
            {
                // CA-3 — the parallel LCA take-off (own ICE table, per-kg mass,
                // Take(5000) walk, hardcoded UK grid 0.233, net timber -1.0) is
                // retired. Delegate to the canonical EN 15978 stage tracker, which
                // walks the shared WBLCA scope and resolves every factor through
                // CarbonFactorResolver (EPD -> material param -> lookup CSV -> legacy)
                // and the GridCarbonRegistry (per-project country) -- so this command
                // and the EDGE dashboard never disagree on the carbon.
                var stage = StingTools.V6.CarbonStageTracker.Compute(doc);

                result.A1_A3_ProductKgCO2   = stage.TotalA1A3;
                result.A4_TransportKgCO2    = stage.TotalA4;
                result.A5_ConstructionKgCO2 = stage.TotalA5;
                result.C1_C4_EndOfLifeKgCO2 = stage.TotalC1 + stage.TotalC2 + stage.TotalC3C4;
                // B6 operational over the building life (canonical annual x years;
                // grid factor from GridCarbonRegistry, not the old 0.233).
                result.B6_OperationalEnergyKgCO2 = stage.TotalB6AnnualKgYr * buildingLifeYears;
                // B1-B7 in-use: a documented maintenance proxy (~1%/yr of A1-A3) on
                // the canonical A1-A3 -- not a second take-off.
                result.B1_B7_InUseKgCO2 = Math.Abs(stage.TotalA1A3) * 0.01 * buildingLifeYears;
                // Module D needs per-material recovery factors the canonical stage
                // walk does not produce; declared excluded (0) rather than fabricated.
                result.D_BeyondLifeKgCO2 = 0;

                // Per-discipline A1-A3 breakdown surfaced in the MaterialBreakdown slot.
                double total = Math.Abs(stage.TotalA1A3);
                if (stage.ByDisciplineA1A3 != null)
                    foreach (var kv in stage.ByDisciplineA1A3.OrderByDescending(x => Math.Abs(x.Value)))
                        result.MaterialBreakdown.Add((kv.Key, kv.Value,
                            total > 0 ? Math.Abs(kv.Value) / total * 100.0 : 0));

                StingLog.Info($"LCA (canonical stage tracker): A1-A3={stage.TotalA1A3:F0} kgCO2e, " +
                    $"B6/yr={stage.TotalB6AnnualKgYr:F0}, WLC={result.WholeLifeCarbon:F0} kgCO2e");
            }
            catch (Exception ex)
            {
                StingLog.Error("LifecycleAssessment.Assess", ex);
            }

            return result;
        }

        // CA-3 — ExtractMaterialQuantities + MapToICEKey removed: the
        // parallel ICE take-off is retired (Assess delegates to CarbonStageTracker).
    }

    // ════════════════════════════════════════════════════════════════
    //  CIRCULARITY & MATERIAL REUSE SCORER
    // ════════════════════════════════════════════════════════════════

    internal static class CircularityScorer
    {
        /// <summary>Calculate material circularity index (0-100%).</summary>
        public static double CalculateCircularity(Document doc)
        {
            try
            {
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .Take(2000)
                    .ToList();

                int totalMaterials = 0;
                int recyclableMaterials = 0;
                int reusableMaterials = 0;

                foreach (var el in elements)
                {
                    try
                    {
                        var matIds = el.GetMaterialIds(false);
                        foreach (var matId in matIds)
                        {
                            var mat = doc.GetElement(matId) as Material;
                            if (mat == null) continue;

                            totalMaterials++;
                            string name = mat.Name.ToLower();

                            // Materials with high recyclability
                            if (name.Contains("steel") || name.Contains("alumin") ||
                                name.Contains("copper") || name.Contains("glass"))
                                recyclableMaterials++;

                            // Materials with reuse potential
                            if (name.Contains("timber") || name.Contains("steel") ||
                                name.Contains("brick") || name.Contains("stone"))
                                reusableMaterials++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Circularity el {el.Id}: {ex.Message}");
                    }
                }

                if (totalMaterials == 0) return 0;
                // Circularity = weighted average of recyclable + reusable
                return (recyclableMaterials * 0.6 + reusableMaterials * 0.4) / totalMaterials * 100.0;
            }
            catch (Exception ex)
            {
                StingLog.Error("CircularityScorer", ex);
                return 0;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SUSTAINABILITY ORCHESTRATOR
    // ════════════════════════════════════════════════════════════════

    internal static class SustainabilityOrchestrator
    {
        /// <summary>Run comprehensive sustainability assessment.</summary>
        public static (BREEAMResult Breeam, LCAResult Lca, double CircularityPct) Assess(
            Document doc, double gfaM2 = 0, double operationalEnergyKWhM2 = 100)
        {
            if (gfaM2 <= 0)
            {
                // Auto-detect GFA from rooms
                try
                {
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .Where(r => r.Area > 0)
                        .ToList();
                    gfaM2 = rooms.Sum(r => r.Area * 0.0929); // sqft to m²
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"GFA auto-detect: {ex.Message}");
                    gfaM2 = 1000; // fallback
                }
            }

            var breeam = BREEAMAssessor.Assess(doc, gfaM2, operationalEnergyKWhM2);
            var lca = LifecycleAssessmentEngine.Assess(doc, gfaM2, 60, operationalEnergyKWhM2);
            double circularity = CircularityScorer.CalculateCircularity(doc);

            StingLog.Info($"Sustainability: BREEAM={breeam.TotalScore:F1}% ({breeam.Rating}), " +
                $"WLC={lca.KgCO2PerM2:F0} kgCO2/m², Circularity={circularity:F0}%");

            return (breeam, lca, circularity);
        }
    }
}
