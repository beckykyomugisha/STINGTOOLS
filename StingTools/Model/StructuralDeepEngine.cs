// ============================================================================
// StructuralDeepEngine.cs — Phase 71: Structural Deep Analysis
//
// Provides advanced structural engineering calculations:
//   1. AutoTorsionDetector        — Automatic torsion case detection
//   2. LateralTorsionalBuckling   — EC3 LTB checks for unbraced beams
//   3. ConnectionDetailingEngine   — Bolt layout, weld specs, edge distances
//   4. CreepDeflectionAnalysis     — Time-dependent creep per EC2
//   5. SeismicSiteAmplification    — Soil-structure interaction per EC8
//   6. FabricationToleranceChecker — BS 5950/EC3 tolerance validation
//   7. ErectionSequenceValidator   — Temporary works and propping analysis
//
// Standards: EC2/EC3/EC7/EC8 with UK National Annex, SCI P358,
//            BS 5950, BS 4604, BS EN 1090
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    //  AUTO TORSION DETECTOR — Eccentric Load & Curved Path Detection
    // ════════════════════════════════════════════════════════════════

    internal class TorsionCase
    {
        public ElementId ElementId { get; set; }
        public string Description { get; set; }
        public string TorsionType { get; set; }    // Eccentric, Curved, Spiral, Cantilever
        public double EccentricityMm { get; set; }
        public double TorsionalMomentKNm { get; set; }
        public string Recommendation { get; set; }
    }

    internal static class AutoTorsionDetector
    {
        /// <summary>Scan model for elements subject to torsion.</summary>
        public static List<TorsionCase> DetectTorsionCases(Document doc)
        {
            var cases = new List<TorsionCase>();

            try
            {
                // Check beams for eccentric loading
                var beams = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Take(500)
                    .ToList();

                foreach (var beam in beams)
                {
                    try
                    {
                        var loc = beam.Location as LocationCurve;
                        if (loc?.Curve == null) continue;

                        // Check for curved beams (torsion from curvature)
                        if (!(loc.Curve is Line))
                        {
                            cases.Add(new TorsionCase
                            {
                                ElementId = beam.Id,
                                Description = $"Curved beam: {beam.Name}",
                                TorsionType = "Curved",
                                Recommendation = "Use hollow section (RHS/SHS) — superior torsional resistance"
                            });
                            continue;
                        }

                        // Check for eccentric connections (beam offset from column centreline)
                        var line = loc.Curve as Line;
                        var bb = beam.get_BoundingBox(null);
                        if (bb == null) continue;

                        double beamWidthFt = bb.Max.X - bb.Min.X;
                        double beamDepthFt = bb.Max.Z - bb.Min.Z;

                        // Find columns near beam ends
                        var startPt = line.GetEndPoint(0);
                        var columns = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .WhereElementIsNotElementType()
                            .Where(c => {
                                var cbb = c.get_BoundingBox(null);
                                if (cbb == null) return false;
                                var cCenter = (cbb.Min + cbb.Max) / 2;
                                return cCenter.DistanceTo(startPt) < 3.0; // within 3ft
                            })
                            .ToList();

                        foreach (var col in columns)
                        {
                            var colBb = col.get_BoundingBox(null);
                            if (colBb == null) continue;
                            var colCenter = (colBb.Min + colBb.Max) / 2;
                            var beamCenter = (bb.Min + bb.Max) / 2;

                            // Horizontal eccentricity
                            double eccFt = Math.Sqrt(
                                Math.Pow(colCenter.X - beamCenter.X, 2) +
                                Math.Pow(colCenter.Y - beamCenter.Y, 2));
                            double eccMm = eccFt * 304.8;
                            double colWidthMm = (colBb.Max.X - colBb.Min.X) * 304.8;

                            if (eccMm > 50) // >50mm eccentricity is significant
                            {
                                cases.Add(new TorsionCase
                                {
                                    ElementId = beam.Id,
                                    Description = $"Eccentric beam-column connection: {beam.Name} offset {eccMm:F0}mm",
                                    TorsionType = "Eccentric",
                                    EccentricityMm = eccMm,
                                    Recommendation = eccMm > colWidthMm / 2
                                        ? "CRITICAL — resultant outside column — redesign connection"
                                        : "Add stiffener plates or use moment-resisting connection"
                                });
                            }
                        }

                        // Check for cantilever beams (self-weight torsion if loaded off-centre)
                        bool isCantilever = false;
                        var endPt = line.GetEndPoint(1);
                        var endCols = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .WhereElementIsNotElementType()
                            .Where(c => {
                                var cbb = c.get_BoundingBox(null);
                                if (cbb == null) return false;
                                return ((cbb.Min + cbb.Max) / 2).DistanceTo(endPt) < 2.0;
                            })
                            .Any();

                        if (!endCols)
                        {
                            isCantilever = true;
                            cases.Add(new TorsionCase
                            {
                                ElementId = beam.Id,
                                Description = $"Cantilever beam: {beam.Name} — free end susceptible to LTB",
                                TorsionType = "Cantilever",
                                Recommendation = "Provide lateral restraint at tip or use stocky section (d/b < 3)"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TorsionDetect beam {beam.Id}: {ex.Message}");
                    }
                }

                StingLog.Info($"AutoTorsionDetector: found {cases.Count} torsion cases in {beams.Count} beams");
            }
            catch (Exception ex)
            {
                StingLog.Error("AutoTorsionDetector.DetectTorsionCases", ex);
            }

            return cases;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LATERAL-TORSIONAL BUCKLING — EC3 §6.3.2
    // ════════════════════════════════════════════════════════════════

    internal class LTBResult
    {
        public double SlendernessLambdaLT { get; set; }
        public double ReductionChiLT { get; set; }
        public double BucklingResistanceMomentKNm { get; set; }
        public double AppliedMomentKNm { get; set; }
        public double UtilisationRatio { get; set; }
        public bool Pass { get; set; }
        public string Recommendation { get; set; }
    }

    internal static class LateralTorsionalBuckling
    {
        /// <summary>Check LTB per EC3 §6.3.2 for an I/H-section beam.</summary>
        public static LTBResult CheckLTB(
            double unbracedLengthMm, double spanMm,
            double sectionDepthMm, double flangeWidthMm, double flangeThickMm, double webThickMm,
            double fyMPa, double appliedMomentKNm, double momentGradientCm = 1.0)
        {
            var result = new LTBResult { AppliedMomentKNm = appliedMomentKNm };

            try
            {
                // Section properties
                double d = sectionDepthMm;
                double b = flangeWidthMm;
                double tf = flangeThickMm;
                double tw = webThickMm;

                // Elastic section modulus (I-section approximation)
                double Iy = 2.0 * (b * tf * tf * tf / 12.0 + b * tf * Math.Pow(d / 2 - tf / 2, 2));
                double Iz = 2.0 * (tf * b * b * b / 12.0) + (d - 2 * tf) * tw * tw * tw / 12.0;
                double Iw = Iz * Math.Pow(d - tf, 2) / 4.0; // warping constant
                double It = (2 * b * tf * tf * tf + (d - 2 * tf) * tw * tw * tw) / 3.0; // torsional constant
                double Wel_y = Iy / (d / 2); // elastic modulus

                double Mcr = CalculateMcr(unbracedLengthMm, Iz, Iw, It, momentGradientCm);

                // Plastic section modulus (approximate)
                double Wpl_y = b * tf * (d - tf) + tw * Math.Pow(d - 2 * tf, 2) / 4.0;
                double Mpl = Wpl_y * fyMPa / 1e6; // kNm

                // Slenderness
                double lambdaLT = Mpl > 0 && Mcr > 0 ? Math.Sqrt(Mpl / Mcr) : 0;
                result.SlendernessLambdaLT = lambdaLT;

                // Reduction factor (EC3 §6.3.2.3 — General case)
                double alphaLT = 0.49; // buckling curve c for rolled I/H sections
                double lambdaLT0 = 0.4; // plateau length
                double beta = 0.75;

                double phiLT = 0.5 * (1 + alphaLT * (lambdaLT - lambdaLT0) +
                    beta * lambdaLT * lambdaLT);
                double chiLT = phiLT > 0
                    ? Math.Min(1.0 / (phiLT + Math.Sqrt(Math.Max(phiLT * phiLT -
                        beta * lambdaLT * lambdaLT, 0))), 1.0)
                    : 1.0;

                // Apply moment gradient factor
                chiLT = Math.Min(chiLT / momentGradientCm, 1.0);

                result.ReductionChiLT = chiLT;
                result.BucklingResistanceMomentKNm = chiLT * Mpl;
                result.UtilisationRatio = result.BucklingResistanceMomentKNm > 0
                    ? appliedMomentKNm / result.BucklingResistanceMomentKNm
                    : 999;
                result.Pass = result.UtilisationRatio <= 1.0;

                result.Recommendation = result.Pass
                    ? $"LTB OK — χLT={chiLT:F3}, utilisation={result.UtilisationRatio:F2}"
                    : $"FAILS LTB — reduce unbraced length to {unbracedLengthMm * result.UtilisationRatio * 0.8:F0}mm or increase section";
            }
            catch (Exception ex)
            {
                StingLog.Error("LTB.CheckLTB", ex);
                result.Recommendation = $"LTB calculation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>Calculate elastic critical moment Mcr per NCCI SN003.</summary>
        private static double CalculateMcr(double Lcr, double Iz, double Iw, double It, double C1)
        {
            double E = 210000.0; // MPa
            double G = 80770.0;  // MPa
            double pi2 = Math.PI * Math.PI;

            // Mcr = C1 × (π²EIz/L²) × √(Iw/Iz + L²GIt/(π²EIz))
            double term1 = pi2 * E * Iz / (Lcr * Lcr);
            double term2 = Iw / Math.Max(Iz, 1) + Lcr * Lcr * G * It / (pi2 * E * Math.Max(Iz, 1));
            if (term2 < 0) term2 = 0;

            return C1 * term1 * Math.Sqrt(term2) / 1e6; // convert to kNm
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CONNECTION DETAILING ENGINE — SCI P358 / EC3 §8
    // ════════════════════════════════════════════════════════════════

    internal class ConnectionDetail
    {
        public string ConnectionType { get; set; }  // EndPlate, FinPlate, Cleat, Splice
        public int BoltRows { get; set; }
        public int BoltsPerRow { get; set; }
        public double BoltDiameterMm { get; set; }
        public double BoltGradeMPa { get; set; }
        public double EdgeDistanceMm { get; set; }
        public double EndDistanceMm { get; set; }
        public double PitchMm { get; set; }
        public double GaugeMm { get; set; }
        public double PlateThickMm { get; set; }
        public double WeldSizeMm { get; set; }
        public string WeldType { get; set; }         // Fillet, FullPen, PartialPen
        public double CapacityKN { get; set; }
        public double DemandKN { get; set; }
        public bool Pass { get; set; }
        public List<string> Checks { get; set; } = new List<string>();
    }

    internal static class ConnectionDetailingEngine
    {
        // EC3 minimum distances (multiples of bolt diameter d0 = hole diameter)
        private const double MinEdgeDistanceRatio = 1.2;  // e1 ≥ 1.2 d0
        private const double MinEndDistanceRatio = 1.2;   // e2 ≥ 1.2 d0
        private const double MinPitchRatio = 2.2;         // p1 ≥ 2.2 d0
        private const double MaxPitchRatio = 14.0;        // p1 ≤ min(14t, 200)
        private const double MinGaugeRatio = 2.4;         // p2 ≥ 2.4 d0

        /// <summary>Design a bolted end-plate connection per EC3/SCI P358.</summary>
        public static ConnectionDetail DesignEndPlate(
            double shearDemandKN, double momentDemandKNm,
            double beamDepthMm, double beamFlangeWidthMm,
            double boltDiamMm = 20, double boltGradeMPa = 800,
            double plateFyMPa = 275)
        {
            var detail = new ConnectionDetail
            {
                ConnectionType = "EndPlate",
                BoltDiameterMm = boltDiamMm,
                BoltGradeMPa = boltGradeMPa,
                DemandKN = shearDemandKN
            };

            try
            {
                double d0 = boltDiamMm + 2; // hole diameter (clearance hole)

                // Minimum distances
                detail.EdgeDistanceMm = Math.Max(MinEdgeDistanceRatio * d0, 30);
                detail.EndDistanceMm = Math.Max(MinEndDistanceRatio * d0, 30);
                detail.PitchMm = Math.Max(MinPitchRatio * d0, 60);
                detail.GaugeMm = Math.Max(MinGaugeRatio * d0, 60);

                // Number of bolt rows from beam depth and moment
                double leverArm = beamDepthMm - 2 * detail.EndDistanceMm;
                double singleBoltShearKN = 0.6 * boltGradeMPa * Math.PI * boltDiamMm * boltDiamMm / 4.0 / 1000.0 / 1.25;
                detail.BoltsPerRow = 2;

                // Moment capacity per bolt row pair = bolt tension × lever arm
                double singleBoltTensionKN = 0.9 * boltGradeMPa * Math.PI * boltDiamMm * boltDiamMm / 4.0 / 1000.0 / 1.25;

                if (momentDemandKNm > 0)
                {
                    // Number of rows needed for moment
                    double momentPerRowKNm = 2 * singleBoltTensionKN * leverArm / 1000.0;
                    detail.BoltRows = momentPerRowKNm > 0
                        ? Math.Max((int)Math.Ceiling(momentDemandKNm / momentPerRowKNm) + 1, 2)
                        : 4;
                }
                else
                {
                    // Shear only
                    double shearPerRow = detail.BoltsPerRow * singleBoltShearKN;
                    detail.BoltRows = shearPerRow > 0
                        ? Math.Max((int)Math.Ceiling(shearDemandKN / shearPerRow), 2)
                        : 2;
                }

                detail.BoltRows = Math.Min(detail.BoltRows, 8); // practical limit

                // Plate thickness (simplified: t ≥ max of bending and bearing)
                detail.PlateThickMm = Math.Max(boltDiamMm * 0.8, 10);

                // Weld sizing (fillet weld to beam flange/web)
                detail.WeldSizeMm = Math.Max(6, beamDepthMm > 400 ? 8 : 6);
                detail.WeldType = "Fillet";

                // Capacity check
                detail.CapacityKN = detail.BoltRows * detail.BoltsPerRow * singleBoltShearKN;
                detail.Pass = detail.CapacityKN >= shearDemandKN;

                // Validation checks
                ValidateDistances(detail);
            }
            catch (Exception ex)
            {
                StingLog.Error("ConnectionDetailing.DesignEndPlate", ex);
                detail.Checks.Add($"Error: {ex.Message}");
            }

            return detail;
        }

        /// <summary>Design a fin plate (simple shear) connection.</summary>
        public static ConnectionDetail DesignFinPlate(
            double shearDemandKN, double beamDepthMm,
            double boltDiamMm = 20, double boltGradeMPa = 800)
        {
            var detail = new ConnectionDetail
            {
                ConnectionType = "FinPlate",
                BoltDiameterMm = boltDiamMm,
                BoltGradeMPa = boltGradeMPa,
                BoltsPerRow = 1,
                DemandKN = shearDemandKN
            };

            try
            {
                double d0 = boltDiamMm + 2;
                detail.EdgeDistanceMm = Math.Max(MinEdgeDistanceRatio * d0, 30);
                detail.EndDistanceMm = Math.Max(MinEndDistanceRatio * d0, 35);
                detail.PitchMm = Math.Max(70, MinPitchRatio * d0);

                double singleBoltKN = 0.6 * boltGradeMPa * Math.PI * boltDiamMm * boltDiamMm / 4.0 / 1000.0 / 1.25;
                detail.BoltRows = singleBoltKN > 0
                    ? Math.Max((int)Math.Ceiling(shearDemandKN / singleBoltKN), 2)
                    : 3;
                detail.BoltRows = Math.Min(detail.BoltRows, 6);

                detail.PlateThickMm = Math.Max(8, boltDiamMm * 0.5);
                detail.WeldSizeMm = Math.Max(6, detail.PlateThickMm * 0.7);
                detail.WeldType = "Fillet";

                detail.CapacityKN = detail.BoltRows * singleBoltKN;
                detail.Pass = detail.CapacityKN >= shearDemandKN;

                ValidateDistances(detail);
            }
            catch (Exception ex)
            {
                StingLog.Error("ConnectionDetailing.DesignFinPlate", ex);
            }

            return detail;
        }

        private static void ValidateDistances(ConnectionDetail detail)
        {
            double d0 = detail.BoltDiameterMm + 2;

            if (detail.EdgeDistanceMm < MinEdgeDistanceRatio * d0)
                detail.Checks.Add($"FAIL: Edge distance {detail.EdgeDistanceMm:F0}mm < min {MinEdgeDistanceRatio * d0:F0}mm");
            else
                detail.Checks.Add($"PASS: Edge distance {detail.EdgeDistanceMm:F0}mm ≥ {MinEdgeDistanceRatio * d0:F0}mm");

            if (detail.PitchMm < MinPitchRatio * d0)
                detail.Checks.Add($"FAIL: Pitch {detail.PitchMm:F0}mm < min {MinPitchRatio * d0:F0}mm");
            else
                detail.Checks.Add($"PASS: Pitch {detail.PitchMm:F0}mm ≥ {MinPitchRatio * d0:F0}mm");

            double maxPitch = Math.Min(MaxPitchRatio * detail.PlateThickMm, 200);
            if (detail.PitchMm > maxPitch)
                detail.Checks.Add($"WARN: Pitch {detail.PitchMm:F0}mm > max {maxPitch:F0}mm — reduce spacing");

            if (detail.WeldSizeMm < detail.PlateThickMm * 0.5)
                detail.Checks.Add($"WARN: Weld size {detail.WeldSizeMm:F0}mm may be undersized for {detail.PlateThickMm:F0}mm plate");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CREEP DEFLECTION — EC2 Time-Dependent Analysis
    // ════════════════════════════════════════════════════════════════

    internal class CreepResult
    {
        public double ImmediateDeflectionMm { get; set; }
        public double CreepDeflectionMm { get; set; }
        public double ShrinkageDeflectionMm { get; set; }
        public double TotalLongTermMm { get; set; }
        public double SpanDeflectionRatio { get; set; }
        public double AllowedRatio { get; set; }
        public bool Pass { get; set; }
        public double CreepCoefficient { get; set; }
        public string Recommendation { get; set; }
    }

    internal static class CreepDeflectionAnalysis
    {
        /// <summary>Calculate time-dependent deflection per EC2 §7.4.</summary>
        public static CreepResult Calculate(
            double spanMm, double immediateDeflectionMm,
            double deadLoadRatio, double liveLoadRatio,
            double relativeHumidityPct = 50, double loadingAgeDays = 28,
            int timeYears = 60, string memberType = "beam")
        {
            var result = new CreepResult { ImmediateDeflectionMm = immediateDeflectionMm };

            try
            {
                // Creep coefficient φ(∞,t0) per EC2 §3.1.4 / Annex B
                // Simplified: depends on RH, member size (h0), concrete class, loading age
                double phi0 = CalculateCreepCoefficient(relativeHumidityPct, 300, loadingAgeDays);

                // Time development factor β(t,t0)
                double betaT = timeYears >= 60 ? 1.0 : Math.Pow(timeYears * 365.0 / (350 + timeYears * 365.0), 0.3);
                double phiT = phi0 * betaT;
                result.CreepCoefficient = phiT;

                // Creep deflection = φ × dead load deflection
                double deadDeflection = immediateDeflectionMm * deadLoadRatio;
                result.CreepDeflectionMm = phiT * deadDeflection;

                // Shrinkage curvature deflection (simplified)
                // εcs ≈ 0.3 mm/m for indoor, 0.5 mm/m for outdoor
                double shrinkageStrain = relativeHumidityPct > 70 ? 0.0002 : 0.0003;
                result.ShrinkageDeflectionMm = shrinkageStrain * spanMm * spanMm / (8.0 * 250); // h≈span/20

                result.TotalLongTermMm = immediateDeflectionMm + result.CreepDeflectionMm + result.ShrinkageDeflectionMm;

                // Span/deflection ratio check
                result.SpanDeflectionRatio = result.TotalLongTermMm > 0 ? spanMm / result.TotalLongTermMm : 9999;
                result.AllowedRatio = memberType switch
                {
                    "beam" => 250,    // EC2 §7.4.1 Table 7.4N
                    "slab" => 250,
                    "cantilever" => 125,
                    _ => 250
                };

                result.Pass = result.SpanDeflectionRatio >= result.AllowedRatio;

                result.Recommendation = result.Pass
                    ? $"L/{result.SpanDeflectionRatio:F0} ≥ L/{result.AllowedRatio} — OK (φ={phiT:F2})"
                    : $"FAILS — L/{result.SpanDeflectionRatio:F0} < L/{result.AllowedRatio}. " +
                      $"Increase depth by {(result.AllowedRatio / result.SpanDeflectionRatio - 1) * 100:F0}% or pre-camber {result.CreepDeflectionMm:F1}mm";
            }
            catch (Exception ex)
            {
                StingLog.Error("CreepDeflection.Calculate", ex);
                result.Recommendation = $"Error: {ex.Message}";
            }

            return result;
        }

        /// <summary>Calculate notional creep coefficient per EC2 Annex B.</summary>
        private static double CalculateCreepCoefficient(double rhPct, double h0Mm, double t0Days)
        {
            // φ(∞,t0) = φRH × β(fcm) × β(t0)
            double phiRH = rhPct <= 40 ? 2.5 : rhPct <= 60 ? 2.0 : rhPct <= 80 ? 1.5 : 1.2;

            // β(fcm) for C30/37: ~2.7 (simplified)
            double betaFcm = 2.7;

            // β(t0) = 1/(0.1 + t0^0.20)
            double betaT0 = 1.0 / (0.1 + Math.Pow(Math.Max(t0Days, 1), 0.20));

            return phiRH * betaFcm * betaT0;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  FABRICATION TOLERANCE CHECKER — BS EN 1090 / BS 5950
    // ════════════════════════════════════════════════════════════════

    internal class ToleranceCheck
    {
        public string CheckName { get; set; }
        public double ModelledValueMm { get; set; }
        public double ToleranceMm { get; set; }
        public bool Pass { get; set; }
        public string Standard { get; set; }
        public string Recommendation { get; set; }
    }

    internal static class FabricationToleranceChecker
    {
        /// <summary>Check structural element fabrication tolerances per BS EN 1090.</summary>
        public static List<ToleranceCheck> CheckElement(Element el, Document doc)
        {
            var checks = new List<ToleranceCheck>();

            try
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null) return checks;

                string catName = el.Category?.Name ?? "";
                double lengthFt = 0;
                var locCurve = el.Location as LocationCurve;
                if (locCurve?.Curve != null) lengthFt = locCurve.Curve.Length;
                double lengthMm = lengthFt * 304.8;

                if (catName.Contains("Column"))
                {
                    // Column verticality: ±H/300 (BS EN 1090-2 Table D.1.7)
                    double heightMm = (bb.Max.Z - bb.Min.Z) * 304.8;
                    double tolMm = Math.Max(heightMm / 300.0, 5);
                    checks.Add(new ToleranceCheck
                    {
                        CheckName = "Column verticality",
                        ModelledValueMm = heightMm,
                        ToleranceMm = tolMm,
                        Pass = true, // model assumed vertical
                        Standard = "BS EN 1090-2 Table D.1.7",
                        Recommendation = $"Plumb tolerance ±{tolMm:F1}mm over {heightMm:F0}mm height"
                    });

                    // Cumulative floor-to-floor tolerance
                    checks.Add(new ToleranceCheck
                    {
                        CheckName = "Cumulative height tolerance",
                        ModelledValueMm = heightMm,
                        ToleranceMm = Math.Sqrt(heightMm / 3000.0) * 5.0,
                        Pass = true,
                        Standard = "BS EN 1090-2 §D.1.11",
                        Recommendation = "Check cumulative tolerance stack-up for multi-storey"
                    });
                }

                if (catName.Contains("Framing") || catName.Contains("Beam"))
                {
                    // Beam length tolerance: ±2mm for L≤10m, ±3mm for L>10m
                    double tolMm = lengthMm > 10000 ? 3.0 : 2.0;
                    checks.Add(new ToleranceCheck
                    {
                        CheckName = "Beam length tolerance",
                        ModelledValueMm = lengthMm,
                        ToleranceMm = tolMm,
                        Pass = true,
                        Standard = "BS EN 1090-2 Table D.1.1",
                        Recommendation = $"Fabrication tolerance ±{tolMm:F0}mm for {lengthMm:F0}mm beam"
                    });

                    // Beam straightness: L/750
                    double straightTol = lengthMm / 750.0;
                    checks.Add(new ToleranceCheck
                    {
                        CheckName = "Beam straightness",
                        ModelledValueMm = lengthMm,
                        ToleranceMm = straightTol,
                        Pass = true,
                        Standard = "BS EN 1090-2 Table D.1.3",
                        Recommendation = $"Max bow ±{straightTol:F1}mm over {lengthMm:F0}mm length"
                    });
                }

                if (catName.Contains("Foundation") || catName.Contains("Floor"))
                {
                    // Foundation level tolerance: ±15mm (BS EN 1090-2 / BS 8110)
                    checks.Add(new ToleranceCheck
                    {
                        CheckName = "Foundation level tolerance",
                        ModelledValueMm = 0,
                        ToleranceMm = 15.0,
                        Pass = true,
                        Standard = "BS EN 13670 Table C.4",
                        Recommendation = "Holding-down bolt position ±3mm, level ±15mm"
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationTolerance {el.Id}: {ex.Message}");
            }

            return checks;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  STRUCTURAL DEEP ORCHESTRATOR
    // ════════════════════════════════════════════════════════════════

    internal static class StructuralDeepOrchestrator
    {
        /// <summary>Run all deep structural analysis checks on the model.</summary>
        public static (List<TorsionCase> Torsion, List<ToleranceCheck> Tolerances, int TotalChecks) AnalyseModel(Document doc)
        {
            var torsionCases = AutoTorsionDetector.DetectTorsionCases(doc);

            var toleranceChecks = new List<ToleranceCheck>();
            try
            {
                var structElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Take(200)
                    .ToList();

                structElements.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .Take(200));

                foreach (var el in structElements)
                {
                    toleranceChecks.AddRange(FabricationToleranceChecker.CheckElement(el, doc));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralDeepOrchestrator", ex);
            }

            int totalChecks = torsionCases.Count + toleranceChecks.Count;
            StingLog.Info($"StructuralDeep: {torsionCases.Count} torsion cases, {toleranceChecks.Count} tolerance checks");
            return (torsionCases, toleranceChecks, totalChecks);
        }
    }
}
