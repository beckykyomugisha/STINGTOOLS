// ============================================================================
// AcousticAnalysisEngine.cs — Phase 69: Acoustic Performance Analysis
//
// Provides BS EN 12354 / Approved Document E acoustic analysis:
//   1. SoundInsulationChecker   — Rw (dB) weighted sound reduction index
//   2. ReverbTimeCalculator     — Sabine/Eyring RT60 reverberation time
//   3. AcousticCompositeBuilder — Multi-layer sound performance
//   4. NoisePathTracer          — Flanking path identification
//   5. AcousticPropagationEngine— Source→Path→Receiver noise modelling
//   6. ImpactSoundChecker       — L'nT,w impact sound insulation
//
// Standards: BS EN 12354-1/2/3, BS EN ISO 10140, Approved Document E,
//            BS 8233:2014, BB93 (schools), HTM 08-01 (healthcare)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    //  ACOUSTIC MATERIAL DATABASE
    // ════════════════════════════════════════════════════════════════

    /// <summary>Acoustic properties for a material layer.</summary>
    internal class AcousticMaterialData
    {
        public string Name { get; set; }
        public double DensityKgM3 { get; set; }
        public double ThicknessMm { get; set; }
        public double SoundReductionRwDb { get; set; }
        public double AbsorptionCoeff { get; set; }      // α (0.0-1.0) at 500Hz
        public double ImpactImprovementDeltaLw { get; set; } // ΔLw improvement
        public bool IsResilient { get; set; }              // resilient layer flag
        public bool IsAirGap { get; set; }                 // air gap (adds ~6dB per doubling)

        public double SurfaceMassKgM2 => DensityKgM3 * ThicknessMm / 1000.0;
    }

    /// <summary>Result of an acoustic analysis check.</summary>
    internal class AcousticResult
    {
        public string CheckName { get; set; }
        public double CalculatedValue { get; set; }
        public double RequiredValue { get; set; }
        public string Unit { get; set; }
        public bool Pass => CheckName.Contains("RT60")
            ? CalculatedValue <= RequiredValue
            : CalculatedValue >= RequiredValue;
        public string Standard { get; set; }
        public string Recommendation { get; set; }

        public override string ToString() =>
            $"{CheckName}: {CalculatedValue:F1}{Unit} vs {RequiredValue:F1}{Unit} — {(Pass ? "PASS" : "FAIL")}";
    }

    /// <summary>Flanking path identification result.</summary>
    internal class FlankingPath
    {
        public string Description { get; set; }
        public string PathType { get; set; }   // Direct, Flanking-Floor, Flanking-Wall, Flanking-Ceiling, Junction
        public double EstimatedLossDb { get; set; }
        public ElementId SourceElementId { get; set; }
        public ElementId PathElementId { get; set; }
        public string Mitigation { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //  SOUND INSULATION CHECKER — BS EN 12354-1
    // ════════════════════════════════════════════════════════════════

    internal static class SoundInsulationChecker
    {
        // Approved Document E minimum standards (DnT,w + Ctr dB)
        private static readonly Dictionary<string, double> _partEMinima = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "wall_between_dwellings",       45.0 },
            { "floor_between_dwellings",      45.0 },
            { "wall_within_dwelling",         40.0 },
            { "wall_bedroom_to_living",       43.0 },
            { "wall_corridor_to_dwelling",    43.0 },
            { "floor_corridor_to_dwelling",   43.0 },
            { "wall_refuse_to_dwelling",      48.0 },
            { "wall_plant_to_dwelling",       50.0 },
            { "wall_classroom_to_classroom",  45.0 },  // BB93
            { "wall_classroom_to_corridor",   40.0 },  // BB93
            { "wall_ward_to_ward",            48.0 },  // HTM 08-01
            { "wall_theatre_to_adjacent",     55.0 },  // HTM 08-01
        };

        // Impact sound — L'nT,w maximum (lower is better)
        private static readonly Dictionary<string, double> _impactMaxima = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "floor_between_dwellings",      62.0 },
            { "floor_corridor_to_dwelling",   62.0 },
            { "floor_classroom",              60.0 },  // BB93
            { "floor_ward",                   55.0 },  // HTM 08-01
        };

        /// <summary>Calculate weighted sound reduction index Rw for a single-leaf element.</summary>
        public static double CalculateRwSingleLeaf(double surfaceMassKgM2)
        {
            // Mass law: Rw ≈ 20 log10(m × f) - 47 at reference 500Hz
            // Simplified: Rw ≈ 20 log10(m) + 12 for typical building frequencies
            if (surfaceMassKgM2 <= 0) return 0;
            return 20.0 * Math.Log10(surfaceMassKgM2) + 12.0;
        }

        /// <summary>Calculate Rw for a double-leaf construction with air gap.</summary>
        public static double CalculateRwDoubleLeaf(double mass1KgM2, double mass2KgM2, double airGapMm)
        {
            double rw1 = CalculateRwSingleLeaf(mass1KgM2);
            double rw2 = CalculateRwSingleLeaf(mass2KgM2);
            // ACOUSTIC-CAVITY-01: Replace the flat air-gap-bin bonus with the
            // BS EN 12354-1 Annex B.3 frequency-weighted Rw bonus, scaled by the
            // air-gap depth. Below 50 mm the bonus is heavily reduced (mostly
            // resonance lift); above 100 mm the bonus saturates near the
            // weighted value. This matches measured-value handbooks better
            // than the previous 3 / 6 / 10 dB step function.
            double weightedBonus = StingTools.BIMManager.AcousticCavityBonus.WeightedRwBonus();
            double depthScale = airGapMm <= 0
                ? 0
                : airGapMm >= 100 ? 1.0
                : airGapMm >= 50  ? 0.75
                : 0.4;
            double cavityBonus = weightedBonus * depthScale;
            // Absorption in cavity adds ~5dB with mineral wool
            return rw1 + rw2 + cavityBonus - 6.0; // -6dB coupling correction
        }

        /// <summary>Calculate composite Rw for multi-layer wall/floor construction.</summary>
        public static double CalculateRwComposite(List<AcousticMaterialData> layers)
        {
            if (layers == null || layers.Count == 0) return 0;

            // Separate into leaf groups split by air gaps
            var leafGroups = new List<List<AcousticMaterialData>>();
            var current = new List<AcousticMaterialData>();

            foreach (var layer in layers)
            {
                if (layer.IsAirGap)
                {
                    if (current.Count > 0) leafGroups.Add(current);
                    current = new List<AcousticMaterialData>();
                    // Store air gap thickness for cavity bonus
                }
                else
                {
                    current.Add(layer);
                }
            }
            if (current.Count > 0) leafGroups.Add(current);

            if (leafGroups.Count == 0) return 0;

            if (leafGroups.Count == 1)
            {
                double totalMass = leafGroups[0].Sum(l => l.SurfaceMassKgM2);
                double resilientBonus = leafGroups[0].Any(l => l.IsResilient) ? 5.0 : 0;
                return CalculateRwSingleLeaf(totalMass) + resilientBonus;
            }

            // Double/multi-leaf
            double mass1 = leafGroups[0].Sum(l => l.SurfaceMassKgM2);
            double mass2 = leafGroups[leafGroups.Count - 1].Sum(l => l.SurfaceMassKgM2);
            var airGap = layers.FirstOrDefault(l => l.IsAirGap);
            double gapMm = airGap?.ThicknessMm ?? 50;
            double rw = CalculateRwDoubleLeaf(mass1, mass2, gapMm);

            // Resilient mount bonus
            if (layers.Any(l => l.IsResilient)) rw += 8.0;
            // Absorption in cavity
            bool hasCavityAbsorber = layers.Any(l => !l.IsAirGap && l.AbsorptionCoeff > 0.5);
            if (hasCavityAbsorber) rw += 5.0;

            return Math.Min(rw, 75.0); // physical upper limit
        }

        /// <summary>Validate airborne sound insulation against Part E / BB93 / HTM requirements.</summary>
        public static AcousticResult ValidateAirborne(List<AcousticMaterialData> layers, string separationType)
        {
            double rw = CalculateRwComposite(layers);
            double required = 45.0; // default
            string standard = "Approved Document E";

            if (_partEMinima.TryGetValue(separationType, out double minRw))
                required = minRw;

            if (separationType.Contains("classroom")) standard = "BB93";
            else if (separationType.Contains("ward") || separationType.Contains("theatre")) standard = "HTM 08-01";

            return new AcousticResult
            {
                CheckName = $"Airborne Sound Insulation (DnT,w+Ctr)",
                CalculatedValue = rw,
                RequiredValue = required,
                Unit = " dB",
                Standard = standard,
                Recommendation = rw < required
                    ? $"Increase mass by {(required - rw) * 2:F0} kg/m² or add resilient layer (+8dB)"
                    : "Meets requirement"
            };
        }

        /// <summary>Calculate impact sound level L'nT,w for floor construction.</summary>
        public static AcousticResult ValidateImpact(List<AcousticMaterialData> layers, string floorType)
        {
            // Base impact level for concrete slab (mass law inverse)
            double totalMass = layers.Sum(l => l.SurfaceMassKgM2);
            double baseLntw = 110.0 - 30.0 * Math.Log10(Math.Max(totalMass, 1));

            // Floating floor / resilient layer improvement
            double improvement = layers.Sum(l => l.ImpactImprovementDeltaLw);
            double lntw = baseLntw - improvement;

            double maxAllowed = 62.0;
            string standard = "Approved Document E";
            if (_impactMaxima.TryGetValue(floorType, out double max))
            {
                maxAllowed = max;
                if (floorType.Contains("classroom")) standard = "BB93";
                else if (floorType.Contains("ward")) standard = "HTM 08-01";
            }

            return new AcousticResult
            {
                CheckName = $"Impact Sound Insulation (L'nT,w)",
                CalculatedValue = lntw,
                RequiredValue = maxAllowed,
                Unit = " dB",
                Standard = standard,
                Recommendation = lntw > maxAllowed
                    ? $"Add floating floor (ΔLw ≥ {lntw - maxAllowed + 5:F0} dB) or resilient underlay"
                    : "Meets requirement"
            };
        }

        /// <summary>Get minimum required Rw for a given separation type.</summary>
        public static double GetMinimumRw(string separationType)
        {
            return _partEMinima.TryGetValue(separationType, out double v) ? v : 45.0;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  REVERBERATION TIME CALCULATOR — BS EN 12354-6 / BS 8233
    // ════════════════════════════════════════════════════════════════

    internal static class ReverbTimeCalculator
    {
        // BS 8233:2014 recommended RT60 by room type (seconds)
        private static readonly Dictionary<string, (double Min, double Max)> _rt60Limits =
            new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            { "office_open",        (0.5, 0.8) },
            { "office_private",     (0.4, 0.6) },
            { "classroom",          (0.4, 0.6) },  // BB93 Table 1.2
            { "lecture_hall",        (0.6, 1.0) },
            { "hospital_ward",      (0.5, 0.8) },  // HTM 08-01
            { "operating_theatre",  (0.4, 0.6) },
            { "corridor",           (0.8, 1.2) },
            { "restaurant",         (0.6, 1.0) },
            { "concert_hall",       (1.5, 2.2) },
            { "cinema",             (0.8, 1.2) },
            { "library",            (0.6, 1.0) },
            { "residential_living", (0.4, 0.8) },
            { "residential_bedroom",(0.4, 0.6) },
            { "swimming_pool",      (1.0, 2.0) },
            { "sports_hall",        (1.0, 1.5) },
            { "worship",            (1.5, 3.0) },
        };

        /// <summary>Calculate RT60 using Sabine equation: T = 0.161V/A with geometry correction.</summary>
        public static double CalculateSabine(double volumeM3, double totalAbsorptionM2,
            double lengthM = 0, double widthM = 0, double heightM = 0)
        {
            if (volumeM3 <= 0 || totalAbsorptionM2 <= 0) return 0;
            double rt60 = 0.161 * volumeM3 / totalAbsorptionM2;

            // Room geometry correction factor per Fitzroy (1959)
            // Long/narrow rooms (L/W > 3) have higher low-frequency RT60
            // Flat rooms (H/W < 0.3) have lower RT60 due to floor/ceiling dominance
            if (lengthM > 0 && widthM > 0 && heightM > 0)
            {
                double aspectRatio = lengthM / Math.Max(widthM, 0.1);
                double heightRatio = heightM / Math.Max(widthM, 0.1);

                // Geometry correction: narrow rooms +10-30%, flat rooms -10%
                double geoFactor = 1.0;
                if (aspectRatio > 3.0) geoFactor += 0.1 * Math.Min(aspectRatio - 3.0, 3.0); // up to +30%
                if (heightRatio < 0.3) geoFactor -= 0.1;

                rt60 *= geoFactor;
            }

            return rt60;
        }

        /// <summary>Calculate RT60 using Eyring equation (more accurate for high absorption).</summary>
        public static double CalculateEyring(double volumeM3, double totalSurfaceAreaM2, double avgAbsorptionCoeff)
        {
            if (volumeM3 <= 0 || totalSurfaceAreaM2 <= 0 || avgAbsorptionCoeff <= 0) return 0;
            double denominator = -totalSurfaceAreaM2 * Math.Log(1.0 - Math.Min(avgAbsorptionCoeff, 0.99));
            if (denominator <= 0) return 0;
            return 0.161 * volumeM3 / denominator;
        }

        /// <summary>Calculate total absorption area A = Σ(αi × Si) + furniture/people.</summary>
        public static double CalculateTotalAbsorption(
            List<(double AreaM2, double AbsorptionCoeff)> surfaces,
            int occupantCount = 0,
            double furnitureAbsorptionM2 = 0)
        {
            double A = surfaces.Sum(s => s.AreaM2 * s.AbsorptionCoeff);
            // Each person adds ~0.5 m² Sabins at 500Hz (seated, typical clothing)
            A += occupantCount * 0.5;
            A += furnitureAbsorptionM2;
            return A;
        }

        /// <summary>Validate RT60 against BS 8233 / BB93 / HTM recommendations.</summary>
        public static AcousticResult ValidateRT60(double volumeM3, double totalAbsorptionM2, string roomType)
        {
            double rt60 = CalculateSabine(volumeM3, totalAbsorptionM2);
            double maxAllowed = 1.0;
            string standard = "BS 8233:2014";

            if (_rt60Limits.TryGetValue(roomType, out var limits))
            {
                maxAllowed = limits.Max;
                if (roomType.Contains("classroom")) standard = "BB93";
                else if (roomType.Contains("hospital") || roomType.Contains("theatre")) standard = "HTM 08-01";
            }

            double minAllowed = _rt60Limits.TryGetValue(roomType, out var limitsForMin) && limitsForMin.Min > 0
                ? limitsForMin.Min : 0.3;

            string rec = "Meets requirement";
            if (rt60 > maxAllowed)
            {
                double extraAbsorption = 0.161 * volumeM3 / maxAllowed - totalAbsorptionM2;
                rec = $"Add {extraAbsorption:F1} m² absorption (e.g., {extraAbsorption / 0.8:F0} m² acoustic panel α=0.8)";
            }
            else if (rt60 < minAllowed)
            {
                rec = "Room may sound overly dead — reduce absorption";
            }

            return new AcousticResult
            {
                CheckName = "RT60 Reverberation Time",
                CalculatedValue = rt60,
                RequiredValue = maxAllowed,
                Unit = " s",
                Standard = standard,
                Recommendation = rec
            };
        }

        /// <summary>Get RT60 limits for a room type.</summary>
        public static (double Min, double Max) GetLimits(string roomType)
        {
            return _rt60Limits.TryGetValue(roomType, out var l) ? l : (0.4, 1.0);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  NOISE PATH TRACER — Flanking & Direct Transmission
    // ════════════════════════════════════════════════════════════════

    internal static class NoisePathTracer
    {
        /// <summary>Identify flanking paths around a separating element.</summary>
        public static List<FlankingPath> TracePaths(Document doc, Element separatingElement)
        {
            var paths = new List<FlankingPath>();
            if (separatingElement == null) return paths;

            try
            {
                var bb = separatingElement.get_BoundingBox(null);
                if (bb == null) return paths;

                // Direct path through the separating element
                paths.Add(new FlankingPath
                {
                    Description = "Direct transmission through separating element",
                    PathType = "Direct",
                    EstimatedLossDb = EstimateElementRw(separatingElement),
                    SourceElementId = separatingElement.Id,
                    PathElementId = separatingElement.Id,
                    Mitigation = "Increase mass or add decoupled leaf"
                });

                // Find adjacent elements within 500mm that could flank
                var outline = new Outline(
                    new XYZ(bb.Min.X - 0.5, bb.Min.Y - 0.5, bb.Min.Z - 0.5),
                    new XYZ(bb.Max.X + 0.5, bb.Max.Y + 0.5, bb.Max.Z + 0.5));

                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var nearby = new FilteredElementCollector(doc)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Id != separatingElement.Id)
                    .ToList();

                foreach (var adj in nearby)
                {
                    string catName = adj.Category?.Name ?? "";

                    if (catName.Contains("Floor") || catName.Contains("Slab"))
                    {
                        paths.Add(new FlankingPath
                        {
                            Description = $"Flanking via floor/slab: {adj.Name}",
                            PathType = "Flanking-Floor",
                            EstimatedLossDb = EstimateElementRw(adj) - 10, // flanking penalty
                            SourceElementId = separatingElement.Id,
                            PathElementId = adj.Id,
                            Mitigation = "Add resilient junction detail or structural break"
                        });
                    }
                    else if (catName.Contains("Wall"))
                    {
                        paths.Add(new FlankingPath
                        {
                            Description = $"Flanking via wall: {adj.Name}",
                            PathType = "Flanking-Wall",
                            EstimatedLossDb = EstimateElementRw(adj) - 10,
                            SourceElementId = separatingElement.Id,
                            PathElementId = adj.Id,
                            Mitigation = "Discontinuous construction or flexible tie at junction"
                        });
                    }
                    else if (catName.Contains("Ceiling") || catName.Contains("Roof"))
                    {
                        paths.Add(new FlankingPath
                        {
                            Description = $"Flanking via ceiling/roof: {adj.Name}",
                            PathType = "Flanking-Ceiling",
                            EstimatedLossDb = EstimateElementRw(adj) - 15,
                            SourceElementId = separatingElement.Id,
                            PathElementId = adj.Id,
                            Mitigation = "Extend separating element to structural soffit"
                        });
                    }
                }

                // Check for penetrations (doors, openings)
                var inserts = (separatingElement as Wall)?.FindInserts(true, true, true, true);
                if (inserts != null)
                {
                    foreach (var insertId in inserts)
                    {
                        var insert = doc.GetElement(insertId);
                        if (insert != null)
                        {
                            paths.Add(new FlankingPath
                            {
                                Description = $"Weak point — penetration: {insert.Name}",
                                PathType = "Junction",
                                EstimatedLossDb = 15, // doors typically ~25-30 dB, much less than wall
                                SourceElementId = separatingElement.Id,
                                PathElementId = insertId,
                                Mitigation = "Acoustic door/seal or infill penetration with acoustic mastic"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"NoisePathTracer: {ex.Message}");
            }

            return paths;
        }

        /// <summary>Estimate Rw from element's material and thickness.</summary>
        private static double EstimateElementRw(Element el)
        {
            try
            {
                // Try to get width parameter for mass estimation
                var widthParam = el.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                double thicknessFt = widthParam?.AsDouble() ?? 0;
                if (thicknessFt <= 0)
                {
                    var hostObj = el as HostObject;
                    if (hostObj != null)
                        thicknessFt = hostObj.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0.5;
                }
                if (thicknessFt <= 0) thicknessFt = 0.5; // default ~150mm

                double thicknessMm = thicknessFt * 304.8;
                // Estimate density: concrete ~2300, masonry ~1800, timber ~500, plasterboard ~800
                double density = 1800; // conservative masonry default
                string typeName = el.Name?.ToLower() ?? "";
                if (typeName.Contains("concrete")) density = 2300;
                else if (typeName.Contains("timber") || typeName.Contains("wood")) density = 500;
                else if (typeName.Contains("plaster") || typeName.Contains("gypsum")) density = 800;
                else if (typeName.Contains("steel") || typeName.Contains("metal")) density = 7800;

                double massKgM2 = density * thicknessMm / 1000.0;
                return SoundInsulationChecker.CalculateRwSingleLeaf(massKgM2);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"EstimateElementRw: {ex.Message}");
                return 40; // safe default
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ACOUSTIC PROPAGATION ENGINE — Source→Path→Receiver
    // ════════════════════════════════════════════════════════════════

    internal static class AcousticPropagationEngine
    {
        /// <summary>Calculate noise level at receiver through a construction element.</summary>
        public static double CalculateReceiverLevel(
            double sourceLevelDbA,
            double transmissionLossDb,
            double receiverRoomVolumeM3,
            double receiverAbsorptionM2,
            double distanceM = 1.0)
        {
            // Sound power through partition: Lw = Ls - TL + 10*log10(S/A)
            // where S = partition area, A = receiver absorption
            // Simplified for room-to-room:
            double roomCorrection = receiverAbsorptionM2 > 0
                ? 10.0 * Math.Log10(receiverAbsorptionM2 / Math.Max(receiverRoomVolumeM3 * 0.161, 0.1))
                : 0;

            // Distance attenuation (free field: -6dB per doubling)
            double distAtten = distanceM > 1 ? 20.0 * Math.Log10(distanceM) : 0;

            return sourceLevelDbA - transmissionLossDb - distAtten + roomCorrection;
        }

        /// <summary>Calculate combined transmission through multiple flanking paths.</summary>
        public static double CombinedTransmission(List<FlankingPath> paths)
        {
            if (paths == null || paths.Count == 0) return 0;

            // Combine multiple paths: L_total = 10*log10(Σ 10^(Li/10))
            double sum = 0;
            foreach (var path in paths)
            {
                // Each path contributes inversely to its TL
                sum += Math.Pow(10, -path.EstimatedLossDb / 10.0);
            }
            return sum > 0 ? -10.0 * Math.Log10(sum) : 0;
        }

        /// <summary>Predict noise level in receiving room from source via all paths.</summary>
        public static AcousticResult PredictReceiverNoise(
            double sourceLevelDbA,
            List<FlankingPath> paths,
            double receiverVolumeM3,
            double receiverAbsorptionM2,
            string receiverRoomType)
        {
            double combinedTL = CombinedTransmission(paths);
            double receiverLevel = CalculateReceiverLevel(
                sourceLevelDbA, combinedTL, receiverVolumeM3, receiverAbsorptionM2);

            // BS 8233:2014 noise criteria
            double maxAllowed = 35.0; // default residential
            if (receiverRoomType.Contains("bedroom")) maxAllowed = 30.0;
            else if (receiverRoomType.Contains("office")) maxAllowed = 40.0;
            else if (receiverRoomType.Contains("classroom")) maxAllowed = 35.0;  // BB93
            else if (receiverRoomType.Contains("ward")) maxAllowed = 35.0;        // HTM
            else if (receiverRoomType.Contains("operating")) maxAllowed = 35.0;
            else if (receiverRoomType.Contains("library")) maxAllowed = 35.0;

            return new AcousticResult
            {
                CheckName = "Predicted Receiver Noise Level",
                CalculatedValue = receiverLevel,
                RequiredValue = maxAllowed,
                Unit = " dB(A)",
                Standard = "BS 8233:2014",
                Recommendation = receiverLevel > maxAllowed
                    ? $"Reduce by {receiverLevel - maxAllowed:F1} dB — upgrade partition TL or add absorption"
                    : "Meets BS 8233 indoor ambient criteria"
            };
        }

        /// <summary>Calculate duct-borne noise attenuation per metre of ductwork.</summary>
        public static double DuctAttenuation(double ductWidthMm, double lengthM, bool isLined)
        {
            // CIBSE Guide B3: natural attenuation ~0.3 dB/m for unlined rectangular duct
            // Lined duct: ~1.5-6 dB/m depending on lining thickness and frequency
            double attPerM = isLined ? 3.0 : 0.3;
            // Smaller ducts attenuate more per metre
            if (ductWidthMm < 300) attPerM *= 1.5;
            else if (ductWidthMm > 600) attPerM *= 0.7;

            return attPerM * lengthM;
        }

        /// <summary>Calculate silencer insertion loss for a given silencer type.</summary>
        public static double SilencerInsertionLoss(string silencerType, double lengthMm)
        {
            // Typical insertion losses (dB at 250Hz)
            double lossPerM = silencerType switch
            {
                "rectangular_splitter" => 15.0,
                "circular_pod" => 10.0,
                "acoustic_louvre" => 8.0,
                "flexible_connector" => 5.0,
                "90_degree_bend" => 3.0,
                _ => 5.0
            };
            return lossPerM * (lengthMm / 1000.0);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ACOUSTIC ANALYSIS ORCHESTRATOR
    // ════════════════════════════════════════════════════════════════

    internal static class AcousticAnalysisOrchestrator
    {
        /// <summary>Run full acoustic analysis on a Revit model.</summary>
        public static List<AcousticResult> AnalyseModel(Document doc)
        {
            var results = new List<AcousticResult>();
            try
            {
                // 1. Check wall partitions between rooms
                var walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Take(500)
                    .ToList();

                int wallsChecked = 0;
                foreach (var wall in walls)
                {
                    try
                    {
                        var layers = ExtractAcousticLayers(doc, wall);
                        if (layers.Count == 0) continue;

                        double rw = SoundInsulationChecker.CalculateRwComposite(layers);
                        double minRw = SoundInsulationChecker.GetMinimumRw("wall_between_dwellings");

                        if (rw < minRw)
                        {
                            results.Add(new AcousticResult
                            {
                                CheckName = $"Wall {wall.Name} Airborne Rw",
                                CalculatedValue = rw,
                                RequiredValue = minRw,
                                Unit = " dB",
                                Standard = "Approved Document E",
                                Recommendation = $"Rw={rw:F0}dB < {minRw:F0}dB — add mass or decouple"
                            });
                        }
                        wallsChecked++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AcousticAnalysis wall {wall.Id}: {ex.Message}");
                    }
                }

                // 2. Check floor/slab impact sound
                var floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WhereElementIsNotElementType()
                    .Cast<Floor>()
                    .Take(200)
                    .ToList();

                foreach (var floor in floors)
                {
                    try
                    {
                        var layers = ExtractAcousticLayers(doc, floor);
                        if (layers.Count == 0) continue;

                        var impactResult = SoundInsulationChecker.ValidateImpact(layers, "floor_between_dwellings");
                        if (!impactResult.Pass)
                            results.Add(impactResult);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AcousticAnalysis floor {floor.Id}: {ex.Message}");
                    }
                }

                // 3. Check room reverberation times
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Take(200)
                    .ToList();

                foreach (var roomEl in rooms)
                {
                    try
                    {
                        var room = roomEl as Autodesk.Revit.DB.Architecture.Room;
                        if (room == null || room.Area <= 0) continue;

                        double areaM2 = room.Area * 0.0929; // sqft to m²
                        double heightM = (room.UnboundedHeight > 0 ? room.UnboundedHeight : 10) * 0.3048;
                        double volumeM3 = areaM2 * heightM;

                        // Estimate total surface area: 2×floor/ceiling + walls×height
                        // For unknown aspect ratio, assume 1.5:1 (typical office/classroom)
                        // which gives perimeter = 2×(L + L/1.5) = 2×L×(1 + 1/1.5) ≈ 3.33×L
                        // where L = √(area × 1.5), so perimeter ≈ 2×(√(1.5×area) + √(area/1.5))
                        // This is more accurate than 4×√area which assumes a square room
                        double sqrtArea = Math.Sqrt(Math.Max(areaM2, 0.01));
                        double estPerimeter = 2.0 * (sqrtArea * 1.2247 + sqrtArea * 0.8165); // aspect 1.5:1
                        double totalSurface = 2 * areaM2 + estPerimeter * heightM;
                        double avgAlpha = 0.15; // bare typical
                        double totalAbsorption = totalSurface * avgAlpha;

                        string roomDept = room.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                        string roomType = InferRoomType(room.Name, roomDept);
                        var rtResult = ReverbTimeCalculator.ValidateRT60(volumeM3, totalAbsorption, roomType);
                        if (!rtResult.Pass)
                            results.Add(rtResult);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AcousticAnalysis room {roomEl.Id}: {ex.Message}");
                    }
                }

                StingLog.Info($"AcousticAnalysis: {wallsChecked} walls, {floors.Count} floors, {rooms.Count} rooms → {results.Count} issues");
            }
            catch (Exception ex)
            {
                StingLog.Error("AcousticAnalysisOrchestrator.AnalyseModel", ex);
            }
            return results;
        }

        /// <summary>Extract acoustic layer data from a compound host element.</summary>
        public static List<AcousticMaterialData> ExtractAcousticLayers(Document doc, HostObject host)
        {
            var layers = new List<AcousticMaterialData>();
            try
            {
                // GetCompoundStructure is on the element type, not the instance
                var hostType = host.Document?.GetElement(host.GetTypeId()) as HostObjAttributes;
                var cs = hostType?.GetCompoundStructure();
                if (cs == null) return layers;

                foreach (var layerIdx in Enumerable.Range(0, cs.LayerCount))
                {
                    var layer = cs.GetLayers()[layerIdx];
                    double thicknessMm = layer.Width * 304.8;

                    var matId = layer.MaterialId;
                    var mat = matId != ElementId.InvalidElementId ? doc.GetElement(matId) as Material : null;
                    string matName = mat?.Name ?? "Unknown";

                    // Infer acoustic properties from material name and function
                    var acousticData = InferAcousticProperties(matName, thicknessMm, layer.Function);
                    layers.Add(acousticData);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExtractAcousticLayers: {ex.Message}");
            }
            return layers;
        }

        private static AcousticMaterialData InferAcousticProperties(string materialName, double thicknessMm,
            MaterialFunctionAssignment function)
        {
            string name = materialName.ToLower();
            double density = 1800;
            double alpha = 0.05;
            double deltaLw = 0;
            bool resilient = false;
            bool airGap = false;

            if (name.Contains("concrete"))         { density = 2300; alpha = 0.02; }
            else if (name.Contains("masonry") || name.Contains("brick"))  { density = 1800; alpha = 0.03; }
            else if (name.Contains("plasterboard") || name.Contains("gypsum")) { density = 800; alpha = 0.05; }
            else if (name.Contains("timber") || name.Contains("wood"))    { density = 500; alpha = 0.10; }
            else if (name.Contains("mineral wool") || name.Contains("rockwool") || name.Contains("insulation"))
                { density = 60; alpha = 0.85; deltaLw = 10; }
            else if (name.Contains("acoustic") && name.Contains("panel")) { density = 400; alpha = 0.80; }
            else if (name.Contains("carpet"))      { density = 200; alpha = 0.30; deltaLw = 20; }
            else if (name.Contains("vinyl") || name.Contains("rubber"))   { density = 1200; alpha = 0.05; deltaLw = 15; }
            else if (name.Contains("resilient") || name.Contains("neoprene"))
                { density = 100; alpha = 0.10; deltaLw = 18; resilient = true; }
            else if (name.Contains("air") || name.Contains("cavity"))
                { density = 1; alpha = 0.0; airGap = true; }
            else if (name.Contains("steel") || name.Contains("metal"))    { density = 7800; alpha = 0.01; }
            else if (name.Contains("glass"))       { density = 2500; alpha = 0.04; }

            if (function == MaterialFunctionAssignment.Membrane) resilient = true;

            return new AcousticMaterialData
            {
                Name = materialName,
                DensityKgM3 = density,
                ThicknessMm = thicknessMm,
                SoundReductionRwDb = SoundInsulationChecker.CalculateRwSingleLeaf(density * thicknessMm / 1000.0),
                AbsorptionCoeff = alpha,
                ImpactImprovementDeltaLw = deltaLw,
                IsResilient = resilient,
                IsAirGap = airGap
            };
        }

        private static string InferRoomType(string roomName, string department)
        {
            string combined = (roomName + " " + department).ToLower();
            if (combined.Contains("bedroom") || combined.Contains("sleep")) return "residential_bedroom";
            if (combined.Contains("living") || combined.Contains("lounge")) return "residential_living";
            if (combined.Contains("office") && combined.Contains("open")) return "office_open";
            if (combined.Contains("office")) return "office_private";
            if (combined.Contains("class") || combined.Contains("teaching")) return "classroom";
            if (combined.Contains("lecture") || combined.Contains("auditorium")) return "lecture_hall";
            if (combined.Contains("ward") || combined.Contains("patient")) return "hospital_ward";
            if (combined.Contains("operat") || combined.Contains("surgery")) return "operating_theatre";
            if (combined.Contains("corridor") || combined.Contains("hall")) return "corridor";
            if (combined.Contains("restaurant") || combined.Contains("dining")) return "restaurant";
            if (combined.Contains("library") || combined.Contains("study")) return "library";
            if (combined.Contains("pool") || combined.Contains("swim")) return "swimming_pool";
            if (combined.Contains("sport") || combined.Contains("gym")) return "sports_hall";
            if (combined.Contains("worship") || combined.Contains("chapel")) return "worship";
            return "office_private"; // safe default
        }
    }
}
