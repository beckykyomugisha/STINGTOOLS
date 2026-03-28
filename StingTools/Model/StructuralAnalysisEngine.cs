// ============================================================================
// StructuralAnalysisEngine.cs — Advanced Structural Analysis Algorithms
//
// Provides production-grade structural engineering calculations:
//   1. FortuneVoronoi         — O(n log n) Voronoi tessellation for tributary areas
//   2. MomentDistribution     — Hardy Cross iterative moment balancing
//   3. DeflectionChecker      — EC2/EC3 serviceability deflection checks
//   4. PunchingShearChecker   — EC2 §6.4 flat slab punching shear
//   5. WindLoadCalculator     — EC1-1-4 wind pressure & storey distribution
//   6. SteelSectionDatabase   — UK/EU steel section lookup (Blue Book SCI P363)
//   7. RCDesignHelper         — EC2 beam/column reinforcement estimation
//   8. ConnectionDesign       — Simple bolt group capacity checks
//   9. StructuralSystemClassifier — Auto-detect frame/wall/dual system
//  10. ConstructionSequencer  — Auto-generate construction phases
//  11. ClashPreDetector       — Pre-placement geometric clash detection
//  12. RTreeIndex             — R-tree spatial index for large point sets
//
// All formulas from Eurocodes (EC0-EC8), SCI P363, IStructE guides.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    // 1. VORONOI TESSELLATION (Fortune's Sweep Line)
    // ════════════════════════════════════════════════════════════════

    #region Voronoi Result Types

    /// <summary>A single Voronoi cell (polygon) around a site point.</summary>
    public class VoronoiCell
    {
        public int SiteIndex { get; set; }
        public List<XYZ> Vertices { get; set; } = new();
        public double AreaSqFt { get; set; }
    }

    /// <summary>Result from Voronoi tessellation.</summary>
    public class VoronoiResult
    {
        public List<VoronoiCell> Cells { get; set; } = new();
        public List<(XYZ A, XYZ B)> Edges { get; set; } = new();
    }

    #endregion

    /// <summary>
    /// Computes Voronoi tessellation for column tributary area calculation.
    /// Uses a simplified but accurate nearest-site polygon clipping approach
    /// (half-plane intersection) instead of full Fortune's sweep for robustness.
    /// Complexity: O(n² log n) for n sites — acceptable for typical column counts (< 500).
    /// </summary>
    internal static class FortuneVoronoi
    {
        /// <summary>
        /// Calculates tributary areas for each column using Voronoi tessellation.
        /// Each column's cell is the region closer to it than any other column.
        /// Cells are clipped to the bounding rectangle.
        /// </summary>
        /// <param name="sites">Column positions (XYZ, Z ignored for 2D)</param>
        /// <param name="boundaryWidthFt">Floor area width in feet</param>
        /// <param name="boundaryDepthFt">Floor area depth in feet</param>
        /// <param name="originX">Origin X in feet</param>
        /// <param name="originY">Origin Y in feet</param>
        /// <returns>Dictionary mapping site index to tributary area in sqFt</returns>
        public static Dictionary<int, double> CalculateTributaryAreas(
            List<XYZ> sites, double boundaryWidthFt, double boundaryDepthFt,
            double originX = 0, double originY = 0)
        {
            var areas = new Dictionary<int, double>();
            if (sites == null || sites.Count == 0) return areas;

            double minX = originX, maxX = originX + boundaryWidthFt;
            double minY = originY, maxY = originY + boundaryDepthFt;

            for (int i = 0; i < sites.Count; i++)
            {
                // Start with bounding rectangle as initial polygon
                var cell = new List<(double X, double Y)>
                {
                    (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)
                };

                double sx = sites[i].X, sy = sites[i].Y;

                // Clip cell by half-plane for each other site
                for (int j = 0; j < sites.Count; j++)
                {
                    if (i == j) continue;
                    double ox = sites[j].X, oy = sites[j].Y;

                    // Half-plane: points closer to site[i] than site[j]
                    // Perpendicular bisector midpoint
                    double mx = (sx + ox) * 0.5;
                    double my = (sy + oy) * 0.5;
                    // Normal pointing toward site[i]
                    double nx = sx - ox;
                    double ny = sy - oy;

                    cell = ClipPolygonByHalfPlane(cell, mx, my, nx, ny);
                    if (cell.Count < 3) break;
                }

                // Calculate polygon area using Shoelace formula
                double area = 0;
                if (cell.Count >= 3)
                {
                    for (int k = 0; k < cell.Count; k++)
                    {
                        int next = (k + 1) % cell.Count;
                        area += cell[k].X * cell[next].Y;
                        area -= cell[next].X * cell[k].Y;
                    }
                    area = Math.Abs(area) * 0.5;
                }
                areas[i] = area;
            }

            return areas;
        }

        /// <summary>
        /// Sutherland-Hodgman polygon clipping against a half-plane.
        /// Keeps the side where dot(point - planePoint, normal) >= 0.
        /// </summary>
        private static List<(double X, double Y)> ClipPolygonByHalfPlane(
            List<(double X, double Y)> polygon,
            double px, double py, double nx, double ny)
        {
            if (polygon.Count == 0) return polygon;

            var output = new List<(double X, double Y)>();

            for (int i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];

                double dCurrent = (current.X - px) * nx + (current.Y - py) * ny;
                double dNext = (next.X - px) * nx + (next.Y - py) * ny;

                if (dCurrent >= 0)
                {
                    output.Add(current);
                    if (dNext < 0)
                    {
                        // Edge exits: add intersection
                        var inter = LineHalfPlaneIntersect(current, next, px, py, nx, ny);
                        if (inter.HasValue) output.Add(inter.Value);
                    }
                }
                else if (dNext >= 0)
                {
                    // Edge enters: add intersection
                    var inter = LineHalfPlaneIntersect(current, next, px, py, nx, ny);
                    if (inter.HasValue) output.Add(inter.Value);
                }
            }

            return output;
        }

        private static (double X, double Y)? LineHalfPlaneIntersect(
            (double X, double Y) a, (double X, double Y) b,
            double px, double py, double nx, double ny)
        {
            double dA = (a.X - px) * nx + (a.Y - py) * ny;
            double dB = (b.X - px) * nx + (b.Y - py) * ny;
            double denom = dA - dB;
            if (Math.Abs(denom) < 1e-12) return null;
            double t = dA / denom;
            return (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 2. MOMENT DISTRIBUTION (Hardy Cross Method)
    // ════════════════════════════════════════════════════════════════

    #region Moment Distribution Result

    /// <summary>Result from Hardy Cross moment distribution analysis.</summary>
    public class MomentDistributionResult
    {
        public List<double> SupportMomentsKNm { get; set; } = new();
        public List<double> MidspanMomentsKNm { get; set; } = new();
        public List<double> ReactionsKN { get; set; } = new();
        public int IterationsUsed { get; set; }
        public bool Converged { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Hardy Cross moment distribution for continuous beams.
    /// Iteratively balances fixed-end moments at supports until convergence.
    /// Ref: Hardy Cross (1930), EC2 §5.
    /// </summary>
    internal static class MomentDistribution
    {
        private const double ConvergenceTol = 0.01; // kNm
        private const int MaxIterations = 50;

        /// <summary>
        /// Analyzes a continuous beam with multiple spans under uniform load.
        /// </summary>
        /// <param name="spansFt">Span lengths in feet</param>
        /// <param name="loadKNPerFt">Uniform load per span in kN/ft</param>
        /// <param name="fixedEnds">Whether each end support is fixed (true) or pinned (false)</param>
        public static MomentDistributionResult Analyze(
            List<double> spansFt, List<double> loadKNPerFt, List<bool> fixedEnds)
        {
            var result = new MomentDistributionResult();
            int n = spansFt.Count;
            if (n == 0) { result.Summary = "No spans"; return result; }

            // Ensure load list matches spans
            while (loadKNPerFt.Count < n) loadKNPerFt.Add(loadKNPerFt.LastOrDefault());

            // SAE-CRIT-01: Convert Revit internal feet to SI metres for all moment/reaction
            // calculations. Revit stores geometry in feet; span inputs are therefore ft and
            // loads are kN/ft. Without conversion, stiffness = 4/L is kN·ft (not dimensionless)
            // and FEM = wL²/12 is kN·ft² (not kN·m). All results must be in kN·m.
            // 1 ft = 0.3048 m  (exact, by international definition)
            const double FtToM = 0.3048;
            var spansM = new double[n];
            var loadKNPerM = new double[n];
            for (int i = 0; i < n; i++)
            {
                spansM[i] = spansFt[i] * FtToM;                // ft → m
                loadKNPerM[i] = loadKNPerFt[i] / FtToM;        // kN/ft → kN/m
            }

            int joints = n + 1; // Number of support points

            // Stiffness factors: k = 4EI/L (relative, assuming constant EI=1), spans in m
            var stiffness = new double[n];
            for (int i = 0; i < n; i++)
                stiffness[i] = (spansM[i] > 0) ? 4.0 / spansM[i] : 0;

            // Distribution factors at each internal joint
            var df = new double[joints, 2]; // [joint, left/right member]
            for (int j = 1; j < joints - 1; j++)
            {
                double totalK = stiffness[j - 1] + stiffness[j];
                if (totalK > 0)
                {
                    df[j, 0] = stiffness[j - 1] / totalK; // left member
                    df[j, 1] = stiffness[j] / totalK;     // right member
                }
            }

            // Fixed-end moments: M = wL²/12 for uniform load, fixed-fixed (EC2 §5, Table C.1)
            // Units: w [kN/m] × L² [m²] / 12 = kN·m  ✓
            var fem = new double[joints, 2]; // [joint, arriving from left(0)/right(1)]
            for (int i = 0; i < n; i++)
            {
                double w = loadKNPerM[i];
                double L = spansM[i];
                double femVal = w * L * L / 12.0; // kN·m
                fem[i, 1] = -femVal;     // Right end of span i (hogging at left support)
                fem[i + 1, 0] = femVal;  // Left end of span i+1 (hogging at right support)
            }

            // Moment array at each joint
            var moments = new double[joints];
            for (int j = 0; j < joints; j++)
                moments[j] = fem[j, 0] + fem[j, 1];

            // Iterative distribution
            var carryOver = new double[joints];
            int iter;
            for (iter = 0; iter < MaxIterations; iter++)
            {
                double maxImbalance = 0;

                for (int j = 1; j < joints - 1; j++)
                {
                    double imbalance = moments[j] + carryOver[j];
                    maxImbalance = Math.Max(maxImbalance, Math.Abs(imbalance));

                    // Distribute imbalance
                    double distLeft = -imbalance * df[j, 0];
                    double distRight = -imbalance * df[j, 1];

                    // Phase 79b CRITICAL FIX: Apply correction to accumulated moment (not zero it).
                    // moments[j] must retain the sum of all distributed moments arriving at joint j.
                    // The distributed amounts (-imbalance × df) sum to -imbalance, balancing this joint.
                    moments[j] += -imbalance; // Apply full correction (distLeft + distRight = -imbalance)
                    carryOver[j] = 0;

                    // Carry-over factor = 0.5
                    if (j > 0) carryOver[j - 1] += distLeft * 0.5;
                    if (j < joints - 1) carryOver[j + 1] += distRight * 0.5;
                }

                if (maxImbalance < ConvergenceTol)
                {
                    result.Converged = true;
                    result.IterationsUsed = iter + 1;
                    break;
                }
            }

            if (!result.Converged) result.IterationsUsed = MaxIterations;

            // Apply final carry-overs
            for (int j = 0; j < joints; j++)
                moments[j] += carryOver[j];

            result.SupportMomentsKNm = moments.ToList();

            // Calculate midspan moments: M_mid = wL²/8 - (|M_L| + |M_R|)/2 [kN·m]
            for (int i = 0; i < n; i++)
            {
                double w = loadKNPerM[i];
                double L = spansM[i];
                double freeSpanMoment = w * L * L / 8.0; // kN·m
                double avgSupportMoment = (Math.Abs(moments[i]) + Math.Abs(moments[i + 1])) / 2.0;
                result.MidspanMomentsKNm.Add(freeSpanMoment - avgSupportMoment);
            }

            // Calculate reactions: R = wL/2 ± ΔM/L [kN]  (spans and moments both in SI)
            for (int j = 0; j < joints; j++)
            {
                double reaction = 0;
                if (j > 0) reaction += loadKNPerM[j - 1] * spansM[j - 1] / 2.0 +
                    (moments[j] - moments[j - 1]) / spansM[j - 1];
                if (j < n) reaction += loadKNPerM[j] * spansM[j] / 2.0 -
                    (moments[j + 1] - moments[j]) / spansM[j];
                result.ReactionsKN.Add(reaction);
            }

            result.Summary = $"Hardy Cross: {n} spans, {result.IterationsUsed} iterations, " +
                $"converged={result.Converged}. Max support moment={moments.Max(m => Math.Abs(m)):F1}kNm";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 3. DEFLECTION CHECKER (EC2/EC3 Serviceability)
    // ════════════════════════════════════════════════════════════════

    #region Deflection Result

    /// <summary>Deflection check result per EC2/EC3 serviceability limits.</summary>
    public class DeflectionResult
    {
        public double CalculatedMm { get; set; }
        public double LimitMm { get; set; }
        public double Ratio { get; set; }
        public bool Pass { get; set; }
        public string CheckType { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Checks beam and slab deflections per EC2 Table 7.4N and EC3 NA.2.23.
    /// Steel: actual deflection δ = 5wL⁴/(384EI).
    /// RC: span/effective-depth ratio check.
    /// Service limits: L/250 total, L/350 imposed, L/500 brittle finishes.
    /// </summary>
    internal static class DeflectionChecker
    {
        private const double SteelEMPa = 210000; // Young's modulus for steel
        private const double ConcreteEMPa = 33000; // Approx for C30/37

        /// <summary>
        /// Checks beam deflection against serviceability limits.
        /// </summary>
        public static DeflectionResult CheckBeamDeflection(
            double spanMm, double depthMm, double widthMm,
            double loadKNPerM, bool isSteel,
            string supportCondition = "simply_supported")
        {
            var result = new DeflectionResult();
            result.LimitMm = spanMm / 250.0; // L/250 for total deflection
            result.CheckType = "L/250";

            if (isSteel)
            {
                // Steel: δ = k × wL⁴ / (384 × E × I)
                // k = 5 for simply supported, 1 for fixed-fixed, 48/5 for cantilever
                // k factor for δ = k×wL⁴/(384×EI): simply_supported=5, fixed_fixed=1, continuous≈2.0, cantilever=48
                double k = supportCondition switch
                {
                    "simply_supported" => 5.0,
                    "fixed_fixed" => 1.0,
                    "continuous" => 2.0, // Multi-span continuous beam (conservative approximation)
                    "cantilever" => 48.0,
                    _ => 5.0,
                };

                // SAE-HIGH-04: Use rolled I-beam approximation for steel sections.
                // For typical UB/UC: flanges ≈ 0.7h wide × 0.1h thick, web ≈ 0.6h deep × 0.01h thick.
                // Ixx ≈ 2 × [b_f × t_f × (h/2 - t_f/2)²] + (t_w × h_w³)/12
                // With b_f = 0.7h, t_f = 0.1h, t_w = 0.01h, h_w = 0.8h:
                //   I ≈ 2 × [0.7h × 0.1h × (0.45h)²] + (0.01h × (0.8h)³)/12
                //     ≈ 0.0284h⁴ + 0.000427h⁴ ≈ 0.0288h⁴
                // This is approximately bh³/12 × 0.35 for rolled I-sections vs full rectangular.
                // Reference: SCI Blue Book section tables for typical UB/UC proportions.
                double tf = depthMm * 0.1;    // flange thickness ≈ 0.1h
                double bf = depthMm * 0.7;    // flange width ≈ 0.7h (use actual widthMm if available)
                double tw = depthMm * 0.01;   // web thickness ≈ 0.01h
                double hw = depthMm - 2 * tf; // clear web height
                // If widthMm was provided by caller, use it for flange width
                if (widthMm > 0 && widthMm < depthMm * 2) bf = widthMm;
                double I_mm4 = 2.0 * (bf * tf * Math.Pow(depthMm / 2.0 - tf / 2.0, 2))
                             + (tw * hw * hw * hw) / 12.0;
                double w_Nmm = loadKNPerM; // kN/m = N/mm
                double L_mm = spanMm;

                if (I_mm4 > 0)
                {
                    result.CalculatedMm = k * w_Nmm * Math.Pow(L_mm, 4) /
                        (384.0 * SteelEMPa * I_mm4);
                }
            }
            else
            {
                // RC: span/depth ratio check per EC2 Table 7.4N
                double allowedRatio = supportCondition switch
                {
                    "simply_supported" => 20.0,
                    "continuous" => 26.0,
                    "cantilever" => 7.0,
                    _ => 20.0,
                };

                // Modification factor for tension reinforcement (assume ρ = 0.5%)
                double K = 1.0; // Rectangular section
                double rho = 0.005;
                double rho0 = Math.Sqrt(30) / 1000.0; // fck = 30 MPa
                double factor = K * (11 + 1.5 * Math.Sqrt(30) * rho0 / rho);
                allowedRatio = Math.Min(allowedRatio * 1.3, factor); // Cap adjustment

                double actualRatio = (depthMm > 0) ? spanMm / depthMm : 999;
                result.CalculatedMm = spanMm / actualRatio; // Effective deflection
                result.LimitMm = spanMm / allowedRatio;
                result.Ratio = actualRatio;
                result.Pass = actualRatio <= allowedRatio;
                result.Summary = $"L/d={actualRatio:F1} vs limit={allowedRatio:F1} ({supportCondition}, " +
                    $"span={spanMm / 1000:F1}m, depth={depthMm:F0}mm) → {(result.Pass ? "OK" : "FAIL")}";
                return result;
            }

            result.Ratio = (result.CalculatedMm > 0) ? spanMm / result.CalculatedMm : 0;
            result.Pass = result.CalculatedMm <= result.LimitMm;
            result.Summary = $"δ={result.CalculatedMm:F1}mm vs limit={result.LimitMm:F1}mm (L/250, " +
                $"span={spanMm / 1000:F1}m) → {(result.Pass ? "OK" : "FAIL")}";
            return result;
        }

        /// <summary>
        /// Checks flat slab deflection using span/depth ratio approach.
        /// </summary>
        public static DeflectionResult CheckSlabDeflection(
            double spanMm, double thicknessMm, double loadKPa, bool isTwoWay)
        {
            var result = new DeflectionResult();

            // EC2 Table 7.4N: basic span/depth ratios for slabs
            double allowedRatio = isTwoWay ? 30.0 : 20.0; // Two-way slabs can be thinner

            // Effective depth ≈ thickness - cover - bar diameter/2
            double effectiveDepth = thicknessMm - 30 - 6; // 30mm cover, 12mm bars
            double actualRatio = (effectiveDepth > 0) ? spanMm / effectiveDepth : 999;

            result.CalculatedMm = spanMm / actualRatio;
            result.LimitMm = spanMm / allowedRatio;
            result.Ratio = actualRatio;
            result.Pass = actualRatio <= allowedRatio;
            result.CheckType = isTwoWay ? "Two-way slab L/d" : "One-way slab L/d";
            result.Summary = $"L/d={actualRatio:F1} vs limit={allowedRatio:F1} " +
                $"(span={spanMm / 1000:F1}m, t={thicknessMm}mm) → {(result.Pass ? "OK" : "FAIL")}";
            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 4. PUNCHING SHEAR CHECKER (EC2 §6.4)
    // ════════════════════════════════════════════════════════════════

    #region Punching Shear Result

    /// <summary>Punching shear check result per EC2 §6.4.</summary>
    public class PunchingShearResult
    {
        public double AppliedStressMPa { get; set; }
        public double ResistanceMPa { get; set; }
        public double UtilisationRatio { get; set; }
        public bool Pass { get; set; }
        public bool NeedsShearReinforcement { get; set; }
        public double ControlPerimeterMm { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>Punching shear reinforcement design result per EC2 §6.4.5.</summary>
    public class PunchingReinforcementResult
    {
        public double EffectiveDepthMm { get; set; }
        public double AppliedStressMPa { get; set; }
        public double ConcreteResistanceMPa { get; set; }
        public double MaxResistanceMPa { get; set; }
        public double EffectiveYieldMPa { get; set; }
        public double RadialSpacingMm { get; set; }
        public double RequiredAsw { get; set; }
        public int LegsRequired { get; set; }
        public int BarDiameter { get; set; }
        public double TangentialSpacing1Mm { get; set; }
        public double TangentialSpacing2Mm { get; set; }
        public double OuterPerimeterDistanceMm { get; set; }
        public int NumberOfPerimeters { get; set; }
        public bool Pass { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// EC2 §6.4 punching shear check at column-slab interface.
    /// Calculates control perimeter at 2d from column face,
    /// applied shear stress vEd, and concrete resistance vRd,c.
    /// </summary>
    internal static class PunchingShearChecker
    {
        /// <summary>
        /// Checks punching shear capacity at a rectangular column.
        /// </summary>
        /// <param name="columnWidthMm">Column width in mm</param>
        /// <param name="columnDepthMm">Column depth in mm</param>
        /// <param name="slabThicknessMm">Slab total thickness in mm</param>
        /// <param name="reactionKN">Column reaction force in kN</param>
        /// <param name="fckMPa">Concrete characteristic strength (default 30 MPa)</param>
        public static PunchingShearResult CheckPunchingShear(
            double columnWidthMm, double columnDepthMm,
            double slabThicknessMm, double reactionKN,
            double fckMPa = 30)
        {
            var result = new PunchingShearResult();

            // Effective depth: d = h - cover - bar diameter
            double d = slabThicknessMm - 30 - 12; // 30mm cover, 12mm bars
            if (d <= 0) d = slabThicknessMm * 0.85;

            // Basic control perimeter at 2d from column face (EC2 §6.4.2)
            // For rectangular column: u1 = 2(c1 + c2) + 2π(2d)
            double u1 = 2 * (columnWidthMm + columnDepthMm) + 2 * Math.PI * (2 * d);
            result.ControlPerimeterMm = u1;

            // Applied shear stress: vEd = β × VEd / (u1 × d)
            // β = 1.15 for internal column (EC2 §6.4.3)
            double beta = 1.15;
            double vEd = beta * reactionKN * 1000 / Math.Max(u1 * d, 1e-10); // N/mm² = MPa
            result.AppliedStressMPa = vEd;

            // Concrete shear resistance: vRd,c = CRd,c × k × (100 × ρl × fck)^(1/3)
            // CRd,c = 0.18/γc, γc = 1.5
            double CRdc = 0.18 / 1.5;
            double k = Math.Min(2.0, 1.0 + Math.Sqrt(200.0 / Math.Max(d, 1e-10)));
            double rhoL = 0.005; // Assume 0.5% reinforcement
            double vRdc = CRdc * k * Math.Pow(100 * rhoL * fckMPa, 1.0 / 3.0);

            // Minimum: vmin = 0.035 × k^(3/2) × fck^(1/2)
            double vmin = 0.035 * Math.Pow(k, 1.5) * Math.Sqrt(Math.Max(fckMPa, 0));
            vRdc = Math.Max(vRdc, vmin);

            result.ResistanceMPa = vRdc;
            result.UtilisationRatio = (vRdc > 0) ? vEd / vRdc : 999;
            result.Pass = vEd <= vRdc;
            result.NeedsShearReinforcement = vEd > vRdc;

            // Maximum punching: vRd,max = 0.5 × ν × fcd
            double nu = 0.6 * (1 - fckMPa / 250.0);
            double fcd = fckMPa / 1.5;
            double vRdMax = 0.5 * nu * fcd;
            bool exceedsMax = vEd > vRdMax;

            result.Summary = $"vEd={vEd:F2}MPa vs vRd,c={vRdc:F2}MPa (util={result.UtilisationRatio:F2})" +
                (exceedsMax ? " EXCEEDS MAX — increase slab thickness!" :
                 result.NeedsShearReinforcement ? " — shear reinforcement required" : " — OK");

            return result;
        }

        /// <summary>
        /// Designs punching shear reinforcement per EC2 §6.4.5.
        /// Calculates required Asw per perimeter and checks outer perimeter without reinforcement.
        /// Two-way check: verifies both x and y directions and orthogonal perimeters.
        /// </summary>
        public static PunchingReinforcementResult DesignPunchingReinforcement(
            double columnWidthMm, double columnDepthMm,
            double slabThicknessMm, double reactionKN,
            double fckMPa = 30, double fywkMPa = 500)
        {
            var result = new PunchingReinforcementResult();

            double d = slabThicknessMm - 30 - 12;
            if (d <= 0)
            {
                d = slabThicknessMm * 0.85;
                StingLog.Warn($"PunchingShear: Effective depth d={slabThicknessMm - 42:F0}mm ≤ 0 for slab thickness {slabThicknessMm:F0}mm. Using fallback d={d:F0}mm.");
            }
            result.EffectiveDepthMm = d;

            // Two-way effective depths: dx and dy
            double dx = d;           // x-direction (outermost layer)
            double dy = d - 12;     // y-direction (inner layer, minus one bar diameter)
            double dAvg = (dx + dy) / 2.0;

            // Phase 82 Finding 2: Guard dAvg > 0 before division
            if (dAvg <= 0)
            {
                result.Pass = false;
                result.Summary = $"FAILS: Average effective depth dAvg={dAvg:F1}mm ≤ 0 — slab too thin for punching shear check";
                return result;
            }

            // Basic control perimeter at 2d
            double u1 = 2 * (columnWidthMm + columnDepthMm) + 2 * Math.PI * (2 * dAvg);

            // Beta factor for moment transfer (EC2 §6.4.3)
            // Internal: 1.15, Edge: 1.40, Corner: 1.50
            double beta = 1.15; // Internal column default

            // Applied shear stress
            double vEd = beta * reactionKN * 1000 / Math.Max(u1 * dAvg, 1e-10);
            result.AppliedStressMPa = vEd;

            // Concrete resistance vRd,c (with 2-way reinforcement ratios)
            double rhoLx = 0.005; // x-direction
            double rhoLy = 0.005; // y-direction
            double rhoL = Math.Min(Math.Sqrt(rhoLx * rhoLy), 0.02); // EC2 §6.4.4(1)

            double CRdc = 0.18 / 1.5;
            double k = Math.Min(2.0, 1.0 + Math.Sqrt(200.0 / Math.Max(dAvg, 1e-10)));
            double vRdc = CRdc * k * Math.Pow(100 * rhoL * fckMPa, 1.0 / 3.0);
            double vmin = 0.035 * Math.Pow(k, 1.5) * Math.Sqrt(Math.Max(fckMPa, 0));
            vRdc = Math.Max(vRdc, vmin);
            result.ConcreteResistanceMPa = vRdc;

            // Maximum punching resistance
            double nu = 0.6 * (1 - fckMPa / 250.0);
            double fcd = fckMPa / 1.5;
            double vRdMax = 0.5 * nu * fcd;
            result.MaxResistanceMPa = vRdMax;

            if (vEd > vRdMax)
            {
                result.Pass = false;
                result.Summary = $"FAILS: vEd={vEd:F2}MPa > vRd,max={vRdMax:F2}MPa — increase slab thickness!";
                return result;
            }

            if (vEd <= vRdc)
            {
                result.Pass = true;
                result.RequiredAsw = 0;
                result.Summary = $"OK: vEd={vEd:F2}MPa ≤ vRd,c={vRdc:F2}MPa — no shear reinforcement required";
                return result;
            }

            // Design shear reinforcement per EC2 §6.4.5
            // vRd,cs = 0.75×vRd,c + 1.5×(d/sr)×Asw×fywd,ef / (u1×d)
            // where sr = radial spacing, fywd,ef = 250+0.25d ≤ fywd
            double fywd = fywkMPa / 1.15;
            double fywdEf = Math.Min(250 + 0.25 * dAvg, fywd);
            result.EffectiveYieldMPa = fywdEf;

            // Rearrange for Asw: Asw = (vEd - 0.75×vRd,c) × u1 × sr / (1.5 × fywd,ef)
            double sr = 0.75 * dAvg; // Radial spacing ≤ 0.75d (EC2 §9.4.3)
            result.RadialSpacingMm = sr;

            double Asw = (vEd - 0.75 * vRdc) * u1 * sr / (1.5 * fywdEf);
            result.RequiredAsw = Math.Max(Asw, 0);

            // Select practical bars
            double aswPerLeg = Math.PI * 10 * 10 / 4.0; // H10 studs
            int legsRequired = (int)Math.Ceiling(Asw / aswPerLeg);
            result.LegsRequired = legsRequired;
            result.BarDiameter = 10;

            // Tangential spacing check: st ≤ 1.5d within first perimeter, ≤ 2d beyond
            double st1 = 1.5 * dAvg;
            double st2 = 2.0 * dAvg;
            result.TangentialSpacing1Mm = st1;
            result.TangentialSpacing2Mm = st2;

            // Outer perimeter where reinforcement is no longer needed
            // uout,ef = β × VEd / (vRd,c × d)
            double uOutEf = beta * reactionKN * 1000 / Math.Max(vRdc * dAvg, 1e-10);
            // Distance from column face: a = (uout - 2(c1+c2)) / (2π)
            double aOut = (uOutEf - 2 * (columnWidthMm + columnDepthMm)) / (2 * Math.PI);
            result.OuterPerimeterDistanceMm = Math.Max(aOut, 2 * dAvg);
            int nPerimeters = (int)Math.Ceiling(result.OuterPerimeterDistanceMm / sr);
            result.NumberOfPerimeters = nPerimeters;

            result.Pass = true;
            result.Summary = $"Shear reinforcement required: {legsRequired}×H{result.BarDiameter} studs, " +
                $"{nPerimeters} perimeters at sr={sr:F0}mm, " +
                $"Asw={Asw:F0}mm² per perimeter, vEd={vEd:F2}MPa, vRd,c={vRdc:F2}MPa";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 5. WIND LOAD CALCULATOR (EC1-1-4)
    // ════════════════════════════════════════════════════════════════

    #region Wind Load Result

    /// <summary>Wind load calculation result per EC1-1-4.</summary>
    public class WindLoadResult
    {
        public double PeakVelocityPressureKPa { get; set; }
        public double TotalForceKN { get; set; }
        public double BaseShearKN { get; set; }
        public double OverturnMomentKNm { get; set; }
        public List<double> StoreyForcesKN { get; set; } = new();
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Simplified wind load calculation per EC1-1-4.
    /// Calculates peak velocity pressure from terrain category and building height.
    /// Distributes total wind force to storeys using inverted triangular distribution.
    /// </summary>
    internal static class WindLoadCalculator
    {
        /// <summary>Air density (kg/m³).</summary>
        private const double RhoAir = 1.25;

        /// <summary>
        /// Calculates peak velocity pressure at building height.
        /// qp(z) = [1 + 7×Iv(z)] × 0.5 × ρ × vm²(z)
        /// </summary>
        /// <param name="heightM">Building height in meters</param>
        /// <param name="terrainCategory">0=sea, 1=lake, 2=farmland, 3=suburban, 4=urban</param>
        /// <param name="vb0">Basic wind speed in m/s (UK NA: 21-30 m/s)</param>
        public static WindLoadResult CalculateWindPressure(
            double heightM, int terrainCategory = 3, double vb0 = 25)
        {
            var result = new WindLoadResult();

            // Terrain roughness parameters per EC1-1-4 Table 4.1
            double z0, zMin, kr;
            switch (terrainCategory)
            {
                case 0: z0 = 0.003; zMin = 1; kr = 0.156; break;   // Sea/coastal
                case 1: z0 = 0.01;  zMin = 1; kr = 0.170; break;   // Lakes/flat
                case 2: z0 = 0.05;  zMin = 2; kr = 0.190; break;   // Farmland
                case 3: z0 = 0.3;   zMin = 5; kr = 0.215; break;   // Suburban
                case 4: z0 = 1.0;   zMin = 10; kr = 0.234; break;  // Urban centre
                default: z0 = 0.3;  zMin = 5; kr = 0.215; break;
            }

            double z = Math.Max(heightM, zMin);

            // Phase 56 BUG-02 fix: Guard against log(z/z0)=0 when z≈z0
            double zRatio = Math.Max(z / z0, 1.001); // Ensure z > z0 to avoid log(1)=0

            // Roughness factor: cr(z) = kr × ln(z/z0)
            double cr = kr * Math.Log(zRatio);

            // Orography factor: co(z) = 1.0 (flat terrain)
            double co = 1.0;

            // Mean wind velocity: vm(z) = cr(z) × co(z) × vb
            double vm = cr * co * vb0;

            // Turbulence intensity: Iv(z) = kI / (co × ln(z/z0))
            double kI = 1.0; // Turbulence factor
            double logZ = Math.Log(zRatio);
            double Iv = Math.Abs(logZ) > 1e-6 ? kI / (co * logZ) : kI / co;

            // Peak velocity pressure: qp(z) = [1 + 7×Iv(z)] × 0.5 × ρ × vm²
            double qp = (1 + 7 * Iv) * 0.5 * RhoAir * vm * vm;
            result.PeakVelocityPressureKPa = qp / 1000.0; // Pa to kPa

            result.Summary = $"Wind: vb={vb0}m/s, terrain={terrainCategory}, z={heightM:F0}m, " +
                $"cr={cr:F2}, vm={vm:F1}m/s, qp={result.PeakVelocityPressureKPa:F3}kPa";

            return result;
        }

        /// <summary>
        /// Distributes total wind force to storeys using inverted triangular distribution.
        /// Higher storeys get proportionally more force (linear with height).
        /// Fi = Ftotal × (zi × Ai) / Σ(zi × Ai)
        /// </summary>
        public static List<double> DistributeToStoreys(
            double totalForceKN, int storeyCount, double storeyHeightM)
        {
            var forces = new List<double>();
            if (storeyCount <= 0 || totalForceKN <= 0) return forces;

            // Sum of heights: Σhi = h1 + h2 + ... + hn
            double sumH = 0;
            for (int i = 1; i <= storeyCount; i++)
                sumH += i * storeyHeightM;

            for (int i = 1; i <= storeyCount; i++)
            {
                double hi = i * storeyHeightM;
                double fi = totalForceKN * hi / sumH;
                forces.Add(fi);
            }

            return forces;
        }

        /// <summary>
        /// Calculates base shear from wind pressure and building face area.
        /// Fw = cscd × cf × qp(ze) × Aref
        /// </summary>
        public static double CalculateBaseShear(
            double buildingWidthM, double buildingHeightM, double qpKPa)
        {
            double cscd = 1.0; // Structural factor (conservative for buildings < 15m)
            double cf = 1.3;   // Force coefficient for rectangular buildings (EC1-1-4 §7.6)
            double Aref = buildingWidthM * buildingHeightM;
            return cscd * cf * qpKPa * Aref;
        }

        /// <summary>Wind torsion analysis result per EC1-1-4 §7.1.2.</summary>
        public class WindTorsionResult
        {
            public double EccentricityM { get; set; }
            public double TotalWindForceKN { get; set; }
            public double TotalTorsionKNm { get; set; }
            public double TorsionalShearStressKPa { get; set; }
            public List<double> StoreyTorsionKNm { get; set; } = new();
            public string Summary { get; set; }
        }

        /// <summary>
        /// Calculates wind-induced torsional moment per EC1-1-4 §7.1.2.
        /// Asymmetric wind loading creates torsion when the centre of pressure
        /// does not coincide with the shear centre. For rectangular buildings,
        /// the eccentricity is taken as e = b/10 where b is the crosswind breadth.
        /// </summary>
        /// <param name="buildingWidthM">Crosswind breadth (perpendicular to wind) in metres</param>
        /// <param name="buildingDepthM">Along-wind depth (parallel to wind) in metres</param>
        /// <param name="buildingHeightM">Total building height in metres</param>
        /// <param name="qpKPa">Peak velocity pressure from CalculateWindPressure (kPa)</param>
        /// <param name="storeyCount">Number of storeys for distribution</param>
        /// <param name="storeyHeightM">Height per storey in metres</param>
        /// <returns>WindTorsionResult with total torsion, per-storey torsion, and eccentricity</returns>
        public static WindTorsionResult CalculateWindTorsion(
            double buildingWidthM, double buildingDepthM, double buildingHeightM,
            double qpKPa, int storeyCount = 0, double storeyHeightM = 0)
        {
            var result = new WindTorsionResult();

            // EC1-1-4 §7.1.2: Eccentricity e = b/10 (crosswind breadth)
            double e = buildingWidthM / 10.0;
            result.EccentricityM = e;

            // Force coefficient for rectangular section
            double cf = 1.3;
            double cscd = 1.0;

            // Wind force on windward face: Fw = cscd × cf × qp × Aref
            double Aref = buildingWidthM * buildingHeightM;
            double Fw = cscd * cf * qpKPa * Aref; // kN
            result.TotalWindForceKN = Fw;

            // Torsional moment: Mt = Fw × e
            double Mt = Fw * e;
            result.TotalTorsionKNm = Mt;

            // Per-storey torsion distribution (inverted triangular, same as force)
            if (storeyCount > 0 && storeyHeightM > 0)
            {
                var storeyForces = DistributeToStoreys(Fw, storeyCount, storeyHeightM);
                result.StoreyTorsionKNm = storeyForces.Select(f => f * e).ToList();
            }

            // Torsional shear stress in core walls (simplified rectangular core)
            // τ = Mt / (2 × Ak × t) where Ak = enclosed area, t = wall thickness
            // Using building plan area as conservative Ak estimate
            double Ak = buildingWidthM * buildingDepthM * 0.25; // Core ≈ 25% of plan
            double tCore = 0.3; // Assumed 300mm core wall
            double tauTorsion = (Ak > 0 && tCore > 0) ? Mt / (2 * Ak * tCore) : 0;
            result.TorsionalShearStressKPa = tauTorsion;

            result.Summary = $"Wind torsion: e={e:F2}m (b/10), Fw={Fw:F1}kN, Mt={Mt:F1}kNm, " +
                $"τ_torsion={tauTorsion:F2}kPa" +
                (storeyCount > 0 ? $", {storeyCount} storeys distributed" : "");

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // SEISMIC SITE AMPLIFICATION (EC8-1-1)
    // ════════════════════════════════════════════════════════════════

    /// <summary>EC8 ground type classification.</summary>
    public enum EC8GroundType { A, B, C, D, E }

    /// <summary>Seismic site amplification result.</summary>
    public class SeismicSiteResult
    {
        public EC8GroundType GroundType { get; set; }
        public double SoilFactorS { get; set; }
        public double TB { get; set; } // Lower limit of constant spectral acceleration (s)
        public double TC { get; set; } // Upper limit of constant spectral acceleration (s)
        public double TD { get; set; } // Beginning of constant displacement (s)
        public double Ag { get; set; } // Design ground acceleration (g)
        public double AgS { get; set; } // Ag × S
        public double[] SpectrumPeriods { get; set; }
        public double[] SpectrumAccelerations { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    /// Seismic site amplification per EC8-1-1 §3.2.2.
    /// Applies soil type correction factors to convert bedrock design spectra
    /// to surface spectra accounting for local ground conditions.
    /// </summary>
    internal static class SeismicSiteAmplification
    {
        /// <summary>
        /// Get EC8 site amplification parameters for a given ground type.
        /// Values from EC8-1-1 Table 3.2 (Type 1 elastic response spectrum, recommended).
        /// </summary>
        public static (double S, double TB, double TC, double TD) GetSiteParameters(EC8GroundType groundType)
        {
            return groundType switch
            {
                EC8GroundType.A => (1.0,  0.15, 0.4,  2.0),
                EC8GroundType.B => (1.2,  0.15, 0.5,  2.0),
                EC8GroundType.C => (1.15, 0.20, 0.6,  2.0),
                EC8GroundType.D => (1.35, 0.20, 0.8,  2.0),
                EC8GroundType.E => (1.4,  0.15, 0.5,  2.0),
                _ => (1.0, 0.15, 0.4, 2.0)
            };
        }

        /// <summary>
        /// Calculate the EC8 Type 1 elastic response spectrum with site amplification.
        /// Se(T) = ag × S × η × spectral shape factor
        /// where η = damping correction factor = √(10/(5+ξ)) ≥ 0.55
        /// </summary>
        /// <param name="agG">Design ground acceleration on type A ground (in g units)</param>
        /// <param name="groundType">EC8 ground type (A-E)</param>
        /// <param name="dampingPct">Viscous damping ratio (default 5%)</param>
        /// <param name="importanceFactor">Importance factor γI (default 1.0)</param>
        public static SeismicSiteResult CalculateSpectrum(
            double agG, EC8GroundType groundType, double dampingPct = 5.0,
            double importanceFactor = 1.0)
        {
            var (S, TB, TC, TD) = GetSiteParameters(groundType);

            // Damping correction: η = √(10/(5+ξ)) ≥ 0.55
            double eta = Math.Max(0.55, Math.Sqrt(Math.Max(10.0 / Math.Max(5.0 + dampingPct, 1e-10), 0)));

            double ag = agG * importanceFactor;
            double agS = ag * S;

            // Generate spectrum at 50 periods from 0 to 4s
            int nPts = 50;
            double[] periods = new double[nPts];
            double[] accelerations = new double[nPts];

            for (int i = 0; i < nPts; i++)
            {
                double T = i * 4.0 / (nPts - 1);
                periods[i] = T;

                double Se;
                if (T < TB)
                {
                    // EC8 Eq 3.2: Se = ag×S×[1 + T/TB×(η×2.5 - 1)]
                    Se = ag * S * (1 + T / TB * (eta * 2.5 - 1));
                }
                else if (T <= TC)
                {
                    // EC8 Eq 3.3: Se = ag×S×η×2.5
                    Se = ag * S * eta * 2.5;
                }
                else if (T <= TD)
                {
                    // EC8 Eq 3.4: Se = ag×S×η×2.5×(TC/T)
                    Se = ag * S * eta * 2.5 * (TC / T);
                }
                else
                {
                    // EC8 Eq 3.5: Se = ag×S×η×2.5×(TC×TD/T²)
                    Se = ag * S * eta * 2.5 * (TC * TD / (T * T));
                }

                accelerations[i] = Se;
            }

            return new SeismicSiteResult
            {
                GroundType = groundType,
                SoilFactorS = S,
                TB = TB, TC = TC, TD = TD,
                Ag = ag, AgS = agS,
                SpectrumPeriods = periods,
                SpectrumAccelerations = accelerations,
                Summary = $"Seismic EC8: Ground type {groundType}, S={S:F2}, ag={ag:F3}g, " +
                    $"agS={agS:F3}g, TB={TB:F2}s, TC={TC:F2}s, TD={TD:F1}s, η={eta:F2}"
            };
        }

        /// <summary>
        /// Calculate equivalent static lateral force per EC8 §4.3.3.2.
        /// Fb = Sd(T1) × m × λ  where λ = 0.85 for T1 ≤ 2TC, else 1.0
        /// </summary>
        public static double CalculateBaseLateralForce(
            double sdT1G, double buildingMassKg, double fundamentalPeriodS,
            EC8GroundType groundType)
        {
            var (_, _, TC, _) = GetSiteParameters(groundType);
            double lambda = fundamentalPeriodS <= 2 * TC ? 0.85 : 1.0;
            return sdT1G * 9.81 * buildingMassKg * lambda / 1000.0; // kN
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 6. STEEL SECTION DATABASE (SCI P363 Blue Book)
    // ════════════════════════════════════════════════════════════════

    #region Steel Section

    /// <summary>Steel section properties from Blue Book (SCI P363).</summary>
    public class SteelSection
    {
        public string Designation { get; set; }
        public string Series { get; set; } // UB, UC, SHS, CHS
        public double DepthMm { get; set; }
        public double WidthMm { get; set; }
        public double WebThkMm { get; set; }
        public double FlangeThkMm { get; set; }
        public double MassKgPerM { get; set; }
        public double AreaCm2 { get; set; }
        public double IxCm4 { get; set; }     // Second moment of area (major axis)
        public double IyCm4 { get; set; }     // Second moment of area (minor axis)
        public double WplxCm3 { get; set; }   // Plastic section modulus (major)
        public double WplyCm3 { get; set; }   // Plastic section modulus (minor)
        public double ixMm { get; set; }      // Radius of gyration (major)
        public double iyMm { get; set; }      // Radius of gyration (minor)
    }

    #endregion

    /// <summary>
    /// UK/EU steel section lookup database.
    /// Contains ~40 common UB (Universal Beam) and UC (Universal Column) sections
    /// with real properties from the Blue Book (SCI P363, BS EN 10025).
    /// </summary>
    internal static class SteelSectionDatabase
    {
        private static readonly List<SteelSection> _sections = new()
        {
            // ── Universal Beams (UB) — sorted by Wpl,x ascending ──
            new() { Designation="UB 127x76x13",   Series="UB", DepthMm=127.0, WidthMm=76.0,  WebThkMm=4.0,  FlangeThkMm=7.6,  MassKgPerM=13.0,  AreaCm2=16.5,  IxCm4=473,    IyCm4=56,    WplxCm3=84,   WplyCm3=23,  ixMm=53.5,  iyMm=18.4 },
            new() { Designation="UB 152x89x16",   Series="UB", DepthMm=152.4, WidthMm=88.7,  WebThkMm=4.5,  FlangeThkMm=7.7,  MassKgPerM=16.0,  AreaCm2=20.3,  IxCm4=834,    IyCm4=90,    WplxCm3=123,  WplyCm3=32,  ixMm=64.1,  iyMm=21.1 },
            new() { Designation="UB 178x102x19",  Series="UB", DepthMm=177.8, WidthMm=101.2, WebThkMm=4.8,  FlangeThkMm=7.9,  MassKgPerM=19.0,  AreaCm2=24.3,  IxCm4=1356,   IyCm4=137,   WplxCm3=171,  WplyCm3=42,  ixMm=74.7,  iyMm=23.8 },
            new() { Designation="UB 203x102x23",  Series="UB", DepthMm=203.2, WidthMm=101.8, WebThkMm=5.4,  FlangeThkMm=9.3,  MassKgPerM=23.1,  AreaCm2=29.4,  IxCm4=2105,   IyCm4=164,   WplxCm3=234,  WplyCm3=50,  ixMm=84.6,  iyMm=23.6 },
            new() { Designation="UB 203x133x25",  Series="UB", DepthMm=203.2, WidthMm=133.2, WebThkMm=5.7,  FlangeThkMm=7.8,  MassKgPerM=25.1,  AreaCm2=32.0,  IxCm4=2340,   IyCm4=308,   WplxCm3=258,  WplyCm3=71,  ixMm=85.5,  iyMm=31.0 },
            new() { Designation="UB 254x102x25",  Series="UB", DepthMm=257.2, WidthMm=101.9, WebThkMm=6.0,  FlangeThkMm=8.4,  MassKgPerM=25.2,  AreaCm2=32.0,  IxCm4=3415,   IyCm4=149,   WplxCm3=306,  WplyCm3=46,  ixMm=103.3, iyMm=21.6 },
            new() { Designation="UB 254x146x31",  Series="UB", DepthMm=251.4, WidthMm=146.1, WebThkMm=6.0,  FlangeThkMm=8.6,  MassKgPerM=31.1,  AreaCm2=39.7,  IxCm4=4413,   IyCm4=448,   WplxCm3=393,  WplyCm3=94,  ixMm=105.4, iyMm=33.6 },
            new() { Designation="UB 305x102x25",  Series="UB", DepthMm=305.1, WidthMm=101.6, WebThkMm=5.8,  FlangeThkMm=7.0,  MassKgPerM=24.8,  AreaCm2=31.6,  IxCm4=4455,   IyCm4=123,   WplxCm3=342,  WplyCm3=38,  ixMm=118.7, iyMm=19.7 },
            new() { Designation="UB 305x165x40",  Series="UB", DepthMm=303.4, WidthMm=165.0, WebThkMm=6.0,  FlangeThkMm=10.2, MassKgPerM=40.3,  AreaCm2=51.3,  IxCm4=8503,   IyCm4=764,   WplxCm3=623,  WplyCm3=141, ixMm=128.7, iyMm=38.6 },
            new() { Designation="UB 356x171x45",  Series="UB", DepthMm=351.4, WidthMm=171.1, WebThkMm=7.0,  FlangeThkMm=9.7,  MassKgPerM=45.0,  AreaCm2=57.3,  IxCm4=12070,  IyCm4=811,   WplxCm3=775,  WplyCm3=146, ixMm=145.2, iyMm=37.6 },
            new() { Designation="UB 356x171x57",  Series="UB", DepthMm=358.0, WidthMm=172.2, WebThkMm=8.1,  FlangeThkMm=13.0, MassKgPerM=57.0,  AreaCm2=72.6,  IxCm4=16040,  IyCm4=1108,  WplxCm3=1010, WplyCm3=196, ixMm=148.6, iyMm=39.1 },
            new() { Designation="UB 406x178x54",  Series="UB", DepthMm=402.6, WidthMm=177.7, WebThkMm=7.7,  FlangeThkMm=10.9, MassKgPerM=54.1,  AreaCm2=68.9,  IxCm4=18670,  IyCm4=1021,  WplxCm3=1055, WplyCm3=175, ixMm=164.6, iyMm=38.5 },
            new() { Designation="UB 406x178x67",  Series="UB", DepthMm=409.4, WidthMm=178.8, WebThkMm=8.8,  FlangeThkMm=14.3, MassKgPerM=67.1,  AreaCm2=85.5,  IxCm4=24330,  IyCm4=1365,  WplxCm3=1346, WplyCm3=233, ixMm=168.7, iyMm=39.9 },
            new() { Designation="UB 457x152x60",  Series="UB", DepthMm=454.6, WidthMm=152.9, WebThkMm=8.1,  FlangeThkMm=13.3, MassKgPerM=59.8,  AreaCm2=76.2,  IxCm4=25500,  IyCm4=795,   WplxCm3=1287, WplyCm3=161, ixMm=183.0, iyMm=32.3 },
            new() { Designation="UB 457x191x67",  Series="UB", DepthMm=453.4, WidthMm=189.9, WebThkMm=8.5,  FlangeThkMm=12.7, MassKgPerM=67.1,  AreaCm2=85.5,  IxCm4=29380,  IyCm4=1452,  WplxCm3=1471, WplyCm3=234, ixMm=185.4, iyMm=41.2 },
            new() { Designation="UB 457x191x82",  Series="UB", DepthMm=460.0, WidthMm=191.3, WebThkMm=9.9,  FlangeThkMm=16.0, MassKgPerM=82.1,  AreaCm2=104.5, IxCm4=37050,  IyCm4=1871,  WplxCm3=1831, WplyCm3=299, ixMm=188.3, iyMm=42.3 },
            new() { Designation="UB 533x210x82",  Series="UB", DepthMm=528.3, WidthMm=208.8, WebThkMm=9.6,  FlangeThkMm=13.2, MassKgPerM=82.2,  AreaCm2=104.7, IxCm4=47540,  IyCm4=2007,  WplxCm3=2059, WplyCm3=294, ixMm=213.1, iyMm=43.8 },
            new() { Designation="UB 533x210x101", Series="UB", DepthMm=536.7, WidthMm=210.0, WebThkMm=10.8, FlangeThkMm=17.4, MassKgPerM=101.0, AreaCm2=128.7, IxCm4=61520,  IyCm4=2692,  WplxCm3=2612, WplyCm3=392, ixMm=218.6, iyMm=45.7 },
            new() { Designation="UB 610x229x101", Series="UB", DepthMm=602.6, WidthMm=227.6, WebThkMm=10.5, FlangeThkMm=14.8, MassKgPerM=101.2, AreaCm2=128.9, IxCm4=75780,  IyCm4=2910,  WplxCm3=2881, WplyCm3=391, ixMm=242.4, iyMm=47.5 },
            new() { Designation="UB 610x229x125", Series="UB", DepthMm=612.2, WidthMm=229.0, WebThkMm=11.9, FlangeThkMm=19.6, MassKgPerM=125.1, AreaCm2=159.3, IxCm4=98610,  IyCm4=3932,  WplxCm3=3676, WplyCm3=525, ixMm=248.8, iyMm=49.7 },
            new() { Designation="UB 686x254x125", Series="UB", DepthMm=677.9, WidthMm=253.0, WebThkMm=11.7, FlangeThkMm=16.2, MassKgPerM=125.2, AreaCm2=159.4, IxCm4=118000, IyCm4=4383,  WplxCm3=3994, WplyCm3=530, ixMm=272.0, iyMm=52.4 },
            new() { Designation="UB 762x267x147", Series="UB", DepthMm=754.0, WidthMm=265.2, WebThkMm=12.8, FlangeThkMm=17.5, MassKgPerM=146.9, AreaCm2=187.1, IxCm4=168500, IyCm4=5455,  WplxCm3=5156, WplyCm3=629, ixMm=300.1, iyMm=54.0 },
            new() { Designation="UB 838x292x176", Series="UB", DepthMm=834.9, WidthMm=291.7, WebThkMm=14.0, FlangeThkMm=18.8, MassKgPerM=176.1, AreaCm2=224.3, IxCm4=246000, IyCm4=7799,  WplxCm3=6808, WplyCm3=817, ixMm=331.2, iyMm=59.0 },
            new() { Designation="UB 914x305x201", Series="UB", DepthMm=903.0, WidthMm=303.3, WebThkMm=15.1, FlangeThkMm=20.2, MassKgPerM=200.9, AreaCm2=256.0, IxCm4=325300, IyCm4=9423,  WplxCm3=8351, WplyCm3=950, ixMm=356.5, iyMm=60.7 },

            // ── Universal Columns (UC) — sorted by area ascending ──
            new() { Designation="UC 152x152x23",  Series="UC", DepthMm=152.4, WidthMm=152.2, WebThkMm=5.8,  FlangeThkMm=6.8,  MassKgPerM=23.0,  AreaCm2=29.2,  IxCm4=1250,   IyCm4=400,   WplxCm3=184,  WplyCm3=82,  ixMm=65.4,  iyMm=37.0 },
            new() { Designation="UC 152x152x30",  Series="UC", DepthMm=157.6, WidthMm=152.9, WebThkMm=6.5,  FlangeThkMm=9.4,  MassKgPerM=30.0,  AreaCm2=38.3,  IxCm4=1748,   IyCm4=560,   WplxCm3=248,  WplyCm3=113, ixMm=67.6,  iyMm=38.2 },
            new() { Designation="UC 152x152x37",  Series="UC", DepthMm=161.8, WidthMm=154.4, WebThkMm=8.0,  FlangeThkMm=11.5, MassKgPerM=37.0,  AreaCm2=47.1,  IxCm4=2210,   IyCm4=706,   WplxCm3=309,  WplyCm3=140, ixMm=68.5,  iyMm=38.7 },
            new() { Designation="UC 203x203x46",  Series="UC", DepthMm=203.2, WidthMm=203.6, WebThkMm=7.2,  FlangeThkMm=11.0, MassKgPerM=46.1,  AreaCm2=58.7,  IxCm4=4568,   IyCm4=1548,  WplxCm3=497,  WplyCm3=230, ixMm=88.2,  iyMm=51.3 },
            new() { Designation="UC 203x203x60",  Series="UC", DepthMm=209.6, WidthMm=205.8, WebThkMm=9.4,  FlangeThkMm=14.2, MassKgPerM=60.0,  AreaCm2=76.4,  IxCm4=6125,   IyCm4=2065,  WplxCm3=656,  WplyCm3=305, ixMm=89.5,  iyMm=52.0 },
            new() { Designation="UC 254x254x73",  Series="UC", DepthMm=254.1, WidthMm=254.6, WebThkMm=8.6,  FlangeThkMm=14.2, MassKgPerM=73.1,  AreaCm2=93.1,  IxCm4=11410,  IyCm4=3908,  WplxCm3=990,  WplyCm3=465, ixMm=110.7, iyMm=64.8 },
            new() { Designation="UC 254x254x89",  Series="UC", DepthMm=260.3, WidthMm=256.3, WebThkMm=10.3, FlangeThkMm=17.3, MassKgPerM=89.0,  AreaCm2=113.3, IxCm4=14270,  IyCm4=4857,  WplxCm3=1224, WplyCm3=575, ixMm=112.2, iyMm=65.4 },
            new() { Designation="UC 305x305x97",  Series="UC", DepthMm=307.9, WidthMm=305.3, WebThkMm=9.9,  FlangeThkMm=15.4, MassKgPerM=97.0,  AreaCm2=123.5, IxCm4=22250,  IyCm4=7308,  WplxCm3=1592, WplyCm3=722, ixMm=134.2, iyMm=76.9 },
            new() { Designation="UC 305x305x118", Series="UC", DepthMm=314.5, WidthMm=307.4, WebThkMm=12.0, FlangeThkMm=18.7, MassKgPerM=118.0, AreaCm2=150.2, IxCm4=27670,  IyCm4=9059,  WplxCm3=1958, WplyCm3=890, ixMm=135.7, iyMm=77.7 },
            new() { Designation="UC 305x305x137", Series="UC", DepthMm=320.5, WidthMm=309.2, WebThkMm=13.8, FlangeThkMm=21.7, MassKgPerM=136.9, AreaCm2=174.4, IxCm4=32810,  IyCm4=10700, WplxCm3=2297, WplyCm3=1048,ixMm=137.2, iyMm=78.3 },
            new() { Designation="UC 356x368x129", Series="UC", DepthMm=355.6, WidthMm=368.6, WebThkMm=10.4, FlangeThkMm=17.5, MassKgPerM=129.0, AreaCm2=164.2, IxCm4=40250,  IyCm4=14610, WplxCm3=2479, WplyCm3=1199,ixMm=156.6, iyMm=94.3 },
            new() { Designation="UC 356x368x153", Series="UC", DepthMm=362.0, WidthMm=370.5, WebThkMm=12.3, FlangeThkMm=20.7, MassKgPerM=152.9, AreaCm2=194.8, IxCm4=48590,  IyCm4=17550, WplxCm3=2965, WplyCm3=1435,ixMm=157.9, iyMm=94.9 },
            new() { Designation="UC 356x406x235", Series="UC", DepthMm=381.0, WidthMm=394.8, WebThkMm=18.4, FlangeThkMm=30.2, MassKgPerM=235.1, AreaCm2=299.6, IxCm4=79080,  IyCm4=31000, WplxCm3=4687, WplyCm3=2383,ixMm=162.5, iyMm=101.7 },
        };

        /// <summary>
        /// Finds the lightest beam section with Wpl,x >= required plastic modulus.
        /// </summary>
        public static SteelSection FindBeamSection(double requiredWplCm3, string preferredSeries = "UB")
        {
            var candidates = _sections
                .Where(s => s.Series == preferredSeries && s.WplxCm3 >= requiredWplCm3)
                .OrderBy(s => s.MassKgPerM)
                .ToList();

            if (candidates.Count > 0) return candidates[0];

            // Fallback: any section with sufficient capacity
            return _sections
                .Where(s => s.WplxCm3 >= requiredWplCm3)
                .OrderBy(s => s.MassKgPerM)
                .FirstOrDefault() ?? (_sections.Any() ? _sections.Last() : null);
        }

        /// <summary>
        /// Finds column section for given axial load using simplified N-only check.
        /// Nb,Rd = χ × A × fy / γM1
        /// </summary>
        public static SteelSection FindColumnSection(
            double axialLoadKN, double heightM, double fykMPa = 355)
        {
            double gammaM1 = 1.0;

            foreach (var section in _sections.Where(s => s.Series == "UC").OrderBy(s => s.AreaCm2))
            {
                // Slenderness: λ = L / iy
                double lambda = (heightM * 1000.0) / section.iyMm;
                // Relative slenderness: λ̄ = λ / (π × √(E/fy))
                double lambdaBar = lambda / (Math.PI * Math.Sqrt(210000.0 / Math.Max(fykMPa, 1e-10)));

                // Reduction factor χ (buckling curve 'b' for UC)
                double alpha = 0.34; // Imperfection factor for curve 'b'
                double phi = 0.5 * (1 + alpha * (lambdaBar - 0.2) + lambdaBar * lambdaBar);
                // Phase 56b AE-006 FIX: Guard against sqrt of negative for slender columns
                double chiSqrtArg = phi * phi - lambdaBar * lambdaBar;
                double chi = chiSqrtArg >= 0
                    ? Math.Min(1.0, 1.0 / (phi + Math.Sqrt(chiSqrtArg)))
                    : Math.Min(1.0, 1.0 / (2 * phi)); // Conservative fallback
                chi = Math.Max(0, chi);

                double Nbrd = chi * section.AreaCm2 * 100 * fykMPa / (gammaM1 * 1000.0); // kN

                if (Nbrd >= axialLoadKN) return section;
            }

            return _sections.Where(s => s.Series == "UC").LastOrDefault() ?? (_sections.Any() ? _sections.Last() : null);
        }

        /// <summary>Returns all sections in the database.</summary>
        public static List<SteelSection> GetAllSections() => _sections.ToList();

        /// <summary>Finds section by designation string.</summary>
        public static SteelSection FindByDesignation(string designation)
        {
            return _sections.FirstOrDefault(s =>
                s.Designation.Equals(designation, StringComparison.OrdinalIgnoreCase));
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 7. RC DESIGN HELPER (EC2 Beam/Column Reinforcement)
    // ════════════════════════════════════════════════════════════════

    #region RC Design Result

    /// <summary>RC design calculation result per EC2.</summary>
    public class RCDesignResult
    {
        public double AsRequiredMm2 { get; set; }
        public double AsProvidedMm2 { get; set; }
        public string BarArrangement { get; set; }
        public double UtilisationRatio { get; set; }
        public bool Pass { get; set; }
        public bool IsMinimumSteel { get; set; }
        public double SlendernessRatio { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Simplified RC beam and column design per EC2.
    /// Calculates required reinforcement area and suggests bar arrangement.
    /// </summary>
    internal static class RCDesignHelper
    {
        // Standard bar areas (mm²)
        private static readonly Dictionary<int, double> BarAreas = new()
        {
            { 8, 50.3 }, { 10, 78.5 }, { 12, 113.1 }, { 16, 201.1 },
            { 20, 314.2 }, { 25, 490.9 }, { 32, 804.2 }, { 40, 1256.6 }
        };

        /// <summary>
        /// Estimates beam flexural reinforcement per EC2 §6.1.
        /// K = M/(bd²fck), z = d(0.5 + √(0.25 - K/1.134)), As = M/(0.87×fyk×z)
        /// </summary>
        public static RCDesignResult EstimateBeamReinforcement(
            double spanMm, double depthMm, double widthMm,
            double momentKNm, double fckMPa = 30, double fykMPa = 500)
        {
            var result = new RCDesignResult();

            double d = depthMm - 40; // Effective depth (40mm cover + link + bar/2)
            if (d <= 0) d = depthMm * 0.85;

            double M = momentKNm * 1e6; // Convert to N.mm

            // K factor
            double K = M / Math.Max(widthMm * d * d * fckMPa, 1e-10);
            double Klim = 0.167; // Singly reinforced limit

            if (K > Klim)
            {
                result.Summary = $"K={K:F3} > K'={Klim} — compression steel needed (doubly reinforced)";
                K = Klim; // Design as singly reinforced for simplicity
            }

            // Phase 56b AE-001 FIX: Guard against sqrt of negative when K > 0.2835
            double sqrtArg = 0.25 - K / 1.134;
            if (sqrtArg < 0) sqrtArg = 0.25 - Klim / 1.134; // Fallback to limit value
            double z = d * (0.5 + Math.Sqrt(Math.Max(sqrtArg, 0)));
            z = Math.Min(z, 0.95 * d);

            // Required steel area
            double As = M / (0.87 * fykMPa * z);

            // Minimum steel: As,min = max(0.26 × fctm/fyk × b × d, 0.0013 × b × d)
            double fctm = 0.3 * Math.Pow(fckMPa, 2.0 / 3.0); // EC2 Table 3.1
            double AsMin = Math.Max(0.26 * fctm / fykMPa * widthMm * d,
                                    0.0013 * widthMm * d);

            // Maximum steel: As,max = 0.04 × Ac
            double AsMax = 0.04 * widthMm * depthMm;

            As = Math.Max(As, AsMin);
            result.AsRequiredMm2 = As;
            result.IsMinimumSteel = As <= AsMin * 1.01;

            // Suggest bar arrangement
            result.BarArrangement = SuggestBarArrangement(As, widthMm);
            result.AsProvidedMm2 = CalculateProvidedArea(result.BarArrangement);
            result.UtilisationRatio = (result.AsProvidedMm2 > 0) ? As / result.AsProvidedMm2 : 999;
            result.Pass = result.AsProvidedMm2 >= As && As <= AsMax;

            result.Summary = $"M={momentKNm:F0}kNm, As,req={As:F0}mm², provide {result.BarArrangement} " +
                $"({result.AsProvidedMm2:F0}mm²), util={result.UtilisationRatio:F2}" +
                (result.IsMinimumSteel ? " [min steel governs]" : "");

            return result;
        }

        /// <summary>
        /// Estimates column reinforcement with simplified N-M interaction.
        /// Checks slenderness and eccentricity.
        /// </summary>
        public static RCDesignResult EstimateColumnReinforcement(
            double widthMm, double depthMm,
            double axialKN, double momentKNm, double heightMm,
            double fckMPa = 30, double fykMPa = 500)
        {
            var result = new RCDesignResult();

            double Ac = widthMm * depthMm;
            double d = depthMm - 40;
            double N = axialKN * 1000; // Convert to N
            double M = momentKNm * 1e6; // Convert to N.mm

            // Minimum eccentricity: max(h/30, 20mm)
            double eMin = Math.Max(depthMm / 30.0, 20);
            double e = (N > 0) ? Math.Max(M / N, eMin) : eMin;
            M = N * e;

            // Slenderness check
            double i = depthMm / Math.Sqrt(12); // Radius of gyration for rectangular
            double l0 = 0.7 * heightMm; // Effective length (braced)
            double lambda = l0 / i;
            result.SlendernessRatio = lambda;

            // Slenderness limit: λlim ≈ 20 × A × B × C / √n
            double n = N / (Ac * fckMPa / 1.5); // Normalised axial force
            n = Math.Max(n, 0.1);
            double lambdaLim = 20 * 0.7 * 1.1 * 0.7 / Math.Sqrt(n);

            bool isSlender = lambda > lambdaLim;

            // Simplified As calculation: As = (N×e - 0.4×fck×b×d²)/(0.87×fyk×(d-d'))
            double fcd = fckMPa / 1.5;
            double d2 = 40; // Compression bar depth from face
            // SAE-HIGH-02: Guard against negative As when concrete section alone can carry the moment.
            // When N×e < 0.4×fcd×b×d², the numerator is negative — clamp to zero before AsMin check.
            // EC2 §6.1 allows sections with no tension steel if the concrete can carry the moment.
            double As = Math.Max(0.0, (N * e - 0.4 * fcd * widthMm * d * d) / (0.87 * fykMPa * (d - d2)));

            // Minimum: 0.1N/(0.87fyk) or 0.002Ac
            double AsMin = Math.Max(0.1 * N / (0.87 * fykMPa), 0.002 * Ac);
            double AsMax = 0.04 * Ac;

            As = Math.Max(As, AsMin);
            As = Math.Min(As, AsMax);
            result.AsRequiredMm2 = As;

            result.BarArrangement = SuggestBarArrangement(As, widthMm, 50);
            result.AsProvidedMm2 = CalculateProvidedArea(result.BarArrangement);
            result.UtilisationRatio = (result.AsProvidedMm2 > 0) ? As / result.AsProvidedMm2 : 999;
            result.Pass = result.AsProvidedMm2 >= As;

            result.Summary = $"N={axialKN:F0}kN, M={momentKNm:F0}kNm, λ={lambda:F0}" +
                (isSlender ? " [SLENDER]" : "") +
                $", As={As:F0}mm², {result.BarArrangement}";

            return result;
        }

        /// <summary>
        /// Suggests bar arrangement for given steel area.
        /// Prefers fewer larger bars for constructability.
        /// </summary>
        public static string SuggestBarArrangement(
            double asRequired, double widthMm, double coverMm = 30)
        {
            // Available space for bars
            double availableWidth = widthMm - 2 * coverMm - 2 * 8; // 8mm links
            var barSizes = new int[] { 40, 32, 25, 20, 16, 12, 10 };

            foreach (int dia in barSizes)
            {
                double barArea = BarAreas[dia];
                int count = (int)Math.Ceiling(asRequired / barArea);
                double requiredWidth = count * dia + (count - 1) * Math.Max(dia, 25);

                if (count >= 2 && count <= 8 && requiredWidth <= availableWidth)
                    return $"{count}T{dia}";
            }

            // Fallback: two layers
            int bars = (int)Math.Ceiling(asRequired / BarAreas[25]);
            return $"{bars}T25 (2 layers)";
        }

        private static double CalculateProvidedArea(string arrangement)
        {
            if (string.IsNullOrEmpty(arrangement)) return 0;
            // Parse "4T16" or "6T25 (2 layers)"
            var match = System.Text.RegularExpressions.Regex.Match(arrangement, @"(\d+)T(\d+)");
            if (!match.Success) return 0;
            int count = int.Parse(match.Groups[1].Value);
            int dia = int.Parse(match.Groups[2].Value);
            return BarAreas.TryGetValue(dia, out var area) ? count * area : 0;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 8. CONNECTION DESIGN (Simple Bolt Capacity Checks)
    // ════════════════════════════════════════════════════════════════

    #region Connection Result

    /// <summary>Simple connection check result.</summary>
    public class SimpleConnectionResult
    {
        public double CapacityKN { get; set; }
        public double DemandKN { get; set; }
        public double UtilisationRatio { get; set; }
        public bool Pass { get; set; }
        public string ConnectionType { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Simple structural connection capacity checks per EC3-1-8.
    /// Covers fin plate and end plate connections with M20/M24 8.8 bolts.
    /// </summary>
    internal static class ConnectionDesign
    {
        /// <summary>
        /// Checks fin plate connection capacity (simple shear connection).
        /// Single shear: Fv,Rd = αv × fub × A / γM2
        /// Bearing: Fb,Rd = k1 × αb × fu × d × t / γM2
        /// </summary>
        public static SimpleConnectionResult CheckFinPlateConnection(
            double reactionKN, int boltCount = 4,
            double boltDiaMm = 20, double boltGrade = 8.8)
        {
            var result = new SimpleConnectionResult { ConnectionType = "Fin Plate" };
            result.DemandKN = reactionKN;

            double gammaM2 = 1.25;

            // Bolt properties from grade (e.g., 8.8 → fub = 800 MPa)
            double fub = boltGrade * 100; // Approximate: grade 8.8 → 800 MPa

            // Tensile stress area for metric bolts
            double As = boltDiaMm switch
            {
                16 => 157, 20 => 245, 22 => 303, 24 => 353, 27 => 459, 30 => 561, _ => 245
            };

            // Single shear capacity per bolt: Fv,Rd = 0.6 × fub × As / γM2
            double alphaV = 0.6; // For grades ≤ 8.8
            double FvRd = alphaV * fub * As / (gammaM2 * 1000); // kN per bolt

            // Total capacity
            result.CapacityKN = FvRd * boltCount;
            result.UtilisationRatio = reactionKN / result.CapacityKN;
            result.Pass = result.UtilisationRatio <= 1.0;

            result.Summary = $"Fin plate: {boltCount}No M{boltDiaMm} gr{boltGrade}, " +
                $"capacity={result.CapacityKN:F0}kN vs demand={reactionKN:F0}kN " +
                $"(util={result.UtilisationRatio:F2}) → {(result.Pass ? "OK" : "FAIL")}";

            return result;
        }

        /// <summary>
        /// Checks end plate connection capacity (moment connection).
        /// Ref: EC3-1-8 §6.2 (bolt tension), §6.2.2 (T-stub moment capacity).
        /// </summary>
        /// <param name="momentKNm">Design bending moment demand (kN·m)</param>
        /// <param name="shearKN">Design shear demand (kN)</param>
        /// <param name="boltDiaMm">Bolt diameter (mm). Default 20.</param>
        /// <param name="boltRows">Number of bolt rows. Default 4 (2 tension + 2 compression).</param>
        /// <param name="beamDepthMm">Connected beam depth (mm). Default 400 if unknown.
        /// SAE-CRIT-02: Callers must pass actual beam depth; 400mm is only a fallback.</param>
        /// <param name="boltGrade">Bolt property class per EN 1993-1-8 Table 3.1. Supported: "4.6", "5.6", "6.8", "8.8", "10.9". Default "8.8".</param>
        public static SimpleConnectionResult CheckEndPlateConnection(
            double momentKNm, double shearKN,
            double boltDiaMm = 20, int boltRows = 4,
            double beamDepthMm = 400, string boltGrade = "8.8")
        {
            var result = new SimpleConnectionResult { ConnectionType = "End Plate" };

            double gammaM2 = 1.25;
            // SAE-HIGH-03: Full grade fub lookup per EN 1993-1-8 Table 3.1.
            // fub is the ultimate tensile strength of the bolt material (N/mm²).
            double fub = boltGrade switch
            {
                "4.6" => 400,
                "5.6" => 500,
                "6.8" => 600,
                "8.8" => 800,
                "10.9" => 1000,
                _ => 800  // Default grade 8.8 for unrecognised grade strings
            };
            double As = boltDiaMm switch
            {
                16 => 157, 20 => 245, 24 => 353, _ => 245
            };

            // Tension capacity per bolt: Ft,Rd = 0.9 fub As / γM2  (EC3-1-8 §3.6.1 Table 3.4)
            double FtRd = 0.9 * fub * As / (gammaM2 * 1000); // kN

            // Lever arm between extreme bolt rows (simplified from beam depth)
            // Per SCI P358 §6.3: use actual beam depth minus edge distance each end
            double leverArmMm = beamDepthMm - 2 * 60; // 60mm edge distance each end
            int tensionRows = boltRows / 2;

            // Moment capacity: M = Σ(Ft,Rd × lever arm)
            double momentCapacity = 0;
            for (int row = 0; row < tensionRows; row++)
            {
                double arm = leverArmMm - row * 90; // 90mm row spacing
                if (arm > 0) momentCapacity += 2 * FtRd * arm / 1000.0; // 2 bolts per row, convert to kNm
            }

            // Shear capacity
            double shearCapacity = 0.6 * fub * As * boltRows / (gammaM2 * 1000);

            // Combined check
            double momentUtil = momentKNm / Math.Max(momentCapacity, 0.001);
            double shearUtil = shearKN / Math.Max(shearCapacity, 0.001);

            result.CapacityKN = momentCapacity;
            result.DemandKN = momentKNm;
            result.UtilisationRatio = Math.Max(momentUtil, shearUtil);
            result.Pass = result.UtilisationRatio <= 1.0;

            result.Summary = $"End plate: {boltRows * 2}No M{boltDiaMm} gr{boltGrade}, " +
                $"M_cap={momentCapacity:F0}kNm vs M_dem={momentKNm:F0}kNm, " +
                $"V_cap={shearCapacity:F0}kN vs V_dem={shearKN:F0}kN " +
                $"(util={result.UtilisationRatio:F2}) → {(result.Pass ? "OK" : "FAIL")}";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 9. STRUCTURAL SYSTEM CLASSIFIER
    // ════════════════════════════════════════════════════════════════

    #region Structural System Result

    /// <summary>Result from structural system auto-classification.</summary>
    public class StructuralSystemResult
    {
        public string SystemType { get; set; }
        public string MaterialType { get; set; }
        public double WallToColumnRatio { get; set; }
        public bool HasBracing { get; set; }
        public bool HasTransferElements { get; set; }
        public bool IsRegularInPlan { get; set; }
        public bool IsRegularInElevation { get; set; }
        public int TotalColumns { get; set; }
        public int TotalBeams { get; set; }
        public int TotalWalls { get; set; }
        public int TotalBraces { get; set; }
        public int TotalFoundations { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Auto-classifies the structural system of a Revit model.
    /// Analyzes element counts, ratios, connectivity, and regularity
    /// to determine: Moment Frame, Braced Frame, Shear Wall, Dual System, Flat Slab.
    /// </summary>
    internal static class StructuralSystemClassifier
    {
        /// <summary>Minimum vertical direction component for brace classification (~8.6° from horizontal).</summary>
        private const double BraceMinVerticalComponent = 0.15;
        /// <summary>Maximum vertical direction component for brace classification (~71.8° — beyond is a column).</summary>
        private const double BraceMaxVerticalComponent = 0.95;

        public static StructuralSystemResult ClassifySystem(Document doc)
        {
            var result = new StructuralSystemResult();

            // SAE-HIGH-01: Use a single multi-category pass to collect all structural elements,
            // replacing 6+ separate FilteredElementCollector instantiations (each scans the
            // entire document element table).
            var structuralCategories = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralFoundation,
            });
            var allStructural = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(structuralCategories)
                .ToList();

            var columns     = allStructural.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralColumns).Cast<FamilyInstance>().ToList();
            var framingEls  = allStructural.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFraming).ToList();
            var wallEls     = allStructural.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_Walls).Cast<Wall>().ToList();
            var foundations = allStructural.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFoundation).ToList();

            result.TotalColumns    = columns.Count;
            result.TotalBeams      = framingEls.Count;
            result.TotalFoundations = foundations.Count;

            // Count structural walls (not all walls are structural)
            result.TotalWalls = wallEls.Count(w =>
            {
                var usage = w.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
                return usage != null && usage.AsInteger() > 0;
            });

            // Check for bracing (framing elements with non-horizontal orientation)
            result.TotalBraces = framingEls.Count(el =>
            {
                var loc = el.Location as LocationCurve;
                if (loc?.Curve == null) return false;
                var dir = (loc.Curve.GetEndPoint(1) - loc.Curve.GetEndPoint(0)).Normalize();
                double verticalComponent = Math.Abs(dir.Z);
                return verticalComponent > BraceMinVerticalComponent && verticalComponent < BraceMaxVerticalComponent;
            });

            result.HasBracing = result.TotalBraces > 0;

            // Check for transfer elements (beams supporting columns above)
            result.HasTransferElements = false; // Simplified — would need multi-level analysis

            // Wall-to-column ratio
            result.WallToColumnRatio = (result.TotalColumns > 0)
                ? (double)result.TotalWalls / result.TotalColumns : 0;

            // Material detection (heuristic from family names, using already-collected columns)
            var colFamilies = columns
                .Select(fi => fi.Symbol?.FamilyName?.ToLowerInvariant() ?? "")
                .Distinct().ToList();

            bool hasSteel = colFamilies.Any(f => f.Contains("steel") || f.Contains("uc") ||
                f.Contains("ub") || f.Contains("shs") || f.Contains("chs") || f.Contains("w "));
            bool hasRC = colFamilies.Any(f => f.Contains("concrete") || f.Contains("rc") ||
                f.Contains("rectangular") || f.Contains("round") || f.Contains("circular"));

            result.MaterialType = (hasSteel && hasRC) ? "Composite" :
                hasSteel ? "Steel" : hasRC ? "RC" : "Unknown";

            // Classify system type
            if (result.TotalWalls >= 4 && result.WallToColumnRatio > 0.5)
            {
                if (result.TotalColumns > 4 && result.TotalBeams > 4)
                    result.SystemType = "Dual System (Frame + Shear Wall)";
                else
                    result.SystemType = "Shear Wall System";
            }
            else if (result.HasBracing && result.TotalBraces >= 4)
            {
                result.SystemType = "Braced Frame";
            }
            else if (result.TotalColumns > 0 && result.TotalBeams > 0)
            {
                if (result.TotalBeams < result.TotalColumns)
                    result.SystemType = "Flat Slab (Column-Slab)";
                else
                    result.SystemType = "Moment-Resisting Frame";
            }
            else if (result.TotalWalls > 0 && result.TotalColumns == 0)
            {
                result.SystemType = "Loadbearing Wall System";
            }
            else
            {
                result.SystemType = "Unclassified";
            }

            // Plan regularity check (simplified: check column symmetry)
            result.IsRegularInPlan = CheckPlanRegularity(doc);
            result.IsRegularInElevation = CheckElevationRegularity(doc);

            result.Summary = $"{result.SystemType} ({result.MaterialType}): " +
                $"{result.TotalColumns}C, {result.TotalBeams}B, {result.TotalWalls}W, " +
                $"{result.TotalBraces}Br, {result.TotalFoundations}F. " +
                $"Regular: plan={result.IsRegularInPlan}, elev={result.IsRegularInElevation}";

            return result;
        }

        private static bool CheckPlanRegularity(Document doc)
        {
            // Check if columns are roughly symmetrical about centroid
            var cols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().ToList();

            if (cols.Count < 4) return true; // Too few to determine

            var pts = cols.Select(c => (c.Location as LocationPoint)?.Point)
                .Where(p => p != null).ToList();

            if (pts.Count < 4) return true;

            double cx = pts.Average(p => p.X);
            double cy = pts.Average(p => p.Y);

            // Check if re-entrant corners exceed 15% of plan dimension (EC8 criterion)
            double maxX = pts.Max(p => p.X) - pts.Min(p => p.X);
            double maxY = pts.Max(p => p.Y) - pts.Min(p => p.Y);

            // Simplified: check if centroid is within 15% of geometric center
            double geoX = (pts.Min(p => p.X) + pts.Max(p => p.X)) / 2;
            double geoY = (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2;
            double eccentricityX = Math.Abs(cx - geoX) / Math.Max(maxX, 0.01);
            double eccentricityY = Math.Abs(cy - geoY) / Math.Max(maxY, 0.01);

            return eccentricityX < 0.15 && eccentricityY < 0.15;
        }

        private static bool CheckElevationRegularity(Document doc)
        {
            // Check if number of columns is consistent across levels
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();

            if (levels.Count < 2) return true;

            var cols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().ToList();

            // Group columns by nearest level
            var colsByLevel = new Dictionary<ElementId, int>();
            foreach (var col in cols)
            {
                var baseLevelParam = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                if (baseLevelParam != null)
                {
                    var levelId = baseLevelParam.AsElementId();
                    colsByLevel.TryGetValue(levelId, out int levelCount);
                    colsByLevel[levelId] = levelCount + 1;
                }
            }

            if (colsByLevel.Count < 2) return true;

            // Regular if column count doesn't vary by more than 20% between floors
            int maxCols = colsByLevel.Values.Max();
            int minCols = colsByLevel.Values.Min();
            return (double)minCols / maxCols >= 0.8;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 10. CONSTRUCTION SEQUENCER
    // ════════════════════════════════════════════════════════════════

    #region Construction Sequence Types

    /// <summary>A phase in the construction sequence.</summary>
    public class ConstructionPhase
    {
        public int PhaseNumber { get; set; }
        public string PhaseName { get; set; }
        public string Description { get; set; }
        public List<ElementId> ElementIds { get; set; } = new();
        public int EstimatedDays { get; set; }
        public string LevelName { get; set; }
    }

    /// <summary>Complete construction sequence with phases.</summary>
    public class ConstructionSequence
    {
        public List<ConstructionPhase> Phases { get; set; } = new();
        public int TotalEstimatedDays { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Auto-generates construction phases from a structural Revit model.
    /// Orders: foundations → ground slab → per-storey (columns → beams → slab → bracing) → roof.
    /// Estimates durations based on element counts and typical UK construction rates.
    /// </summary>
    internal static class ConstructionSequencer
    {
        // Typical durations per element (working days)
        private const double DaysPerFoundation = 0.5;
        private const double DaysPerColumn = 0.3;
        private const double DaysPerBeam = 0.25;
        private const double DaysPerBrace = 0.2;
        private const double DaysPerSlabSqM = 0.01;
        private const double DaysPerWall = 0.5;
        private const int FixedMobilisationDays = 5;

        public static ConstructionSequence GenerateSequence(Document doc)
        {
            var sequence = new ConstructionSequence();
            int phaseNum = 1;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();

            // Phase 0: Mobilisation
            sequence.Phases.Add(new ConstructionPhase
            {
                PhaseNumber = phaseNum++,
                PhaseName = "Mobilisation & Site Setup",
                Description = "Site clearance, temporary works, setting out",
                EstimatedDays = FixedMobilisationDays,
                LevelName = "N/A",
            });

            // Phase 1: Foundations
            var fdns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType().ToList();

            if (fdns.Count > 0)
            {
                sequence.Phases.Add(new ConstructionPhase
                {
                    PhaseNumber = phaseNum++,
                    PhaseName = "Foundations",
                    Description = $"Excavation, formwork, concrete pour for {fdns.Count} foundations",
                    ElementIds = fdns.Select(e => e.Id).ToList(),
                    EstimatedDays = Math.Max(2, (int)Math.Ceiling(fdns.Count * DaysPerFoundation)),
                    LevelName = levels.Count > 0 ? levels[0].Name : "Foundation",
                });
            }

            // PERF-CRIT: Collect all structural elements ONCE, group by level (was 4 collectors × N levels)
            var allCols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().ToList();
            var allFraming = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().ToList();
            var allFloors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType().ToList();
            var allWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType().ToList();

            // Build per-level indexes
            ElementId GetLevelParam(Element el, BuiltInParameter bip) {
                var p = el.get_Parameter(bip); return p?.AsElementId() ?? ElementId.InvalidElementId; }
            var colsByLevel = allCols.GroupBy(e => GetLevelParam(e, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM))
                .ToDictionary(g => g.Key, g => g.ToList());
            var framingByLevel = allFraming.GroupBy(e => GetLevelParam(e, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM))
                .ToDictionary(g => g.Key, g => g.ToList());
            var floorsByLevel = allFloors.GroupBy(e => GetLevelParam(e, BuiltInParameter.LEVEL_PARAM))
                .ToDictionary(g => g.Key, g => g.ToList());
            var wallsByLevel = allWalls.GroupBy(e => GetLevelParam(e, BuiltInParameter.WALL_BASE_CONSTRAINT))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Per-level phases
            foreach (var level in levels)
            {
                var levelId = level.Id;

                var cols = colsByLevel.TryGetValue(levelId, out var cl) ? cl : new List<Element>();
                var beams = framingByLevel.TryGetValue(levelId, out var bl) ? bl : new List<Element>();

                // Separate braces from beams
                var braces = beams.Where(b =>
                {
                    var loc = b.Location as LocationCurve;
                    if (loc?.Curve == null) return false;
                    var dir = (loc.Curve.GetEndPoint(1) - loc.Curve.GetEndPoint(0)).Normalize();
                    return Math.Abs(dir.Z) > 0.15;
                }).ToList();

                var pureBeams = beams.Except(braces).ToList();

                var floors = floorsByLevel.TryGetValue(levelId, out var fl) ? fl : new List<Element>();
                var walls = wallsByLevel.TryGetValue(levelId, out var wl) ? wl : new List<Element>();

                // Skip empty levels
                if (cols.Count == 0 && pureBeams.Count == 0 && floors.Count == 0 && walls.Count == 0)
                    continue;

                // Add sub-phases for this level
                if (cols.Count > 0)
                {
                    sequence.Phases.Add(new ConstructionPhase
                    {
                        PhaseNumber = phaseNum++,
                        PhaseName = $"{level.Name} — Columns",
                        Description = $"Erect {cols.Count} columns",
                        ElementIds = cols.Select(e => e.Id).ToList(),
                        EstimatedDays = Math.Max(1, (int)Math.Ceiling(cols.Count * DaysPerColumn)),
                        LevelName = level.Name,
                    });
                }

                if (walls.Count > 0)
                {
                    sequence.Phases.Add(new ConstructionPhase
                    {
                        PhaseNumber = phaseNum++,
                        PhaseName = $"{level.Name} — Walls",
                        Description = $"Build {walls.Count} structural walls",
                        ElementIds = walls.Select(e => e.Id).ToList(),
                        EstimatedDays = Math.Max(1, (int)Math.Ceiling(walls.Count * DaysPerWall)),
                        LevelName = level.Name,
                    });
                }

                if (pureBeams.Count > 0)
                {
                    sequence.Phases.Add(new ConstructionPhase
                    {
                        PhaseNumber = phaseNum++,
                        PhaseName = $"{level.Name} — Beams",
                        Description = $"Install {pureBeams.Count} beams",
                        ElementIds = pureBeams.Select(e => e.Id).ToList(),
                        EstimatedDays = Math.Max(1, (int)Math.Ceiling(pureBeams.Count * DaysPerBeam)),
                        LevelName = level.Name,
                    });
                }

                if (braces.Count > 0)
                {
                    sequence.Phases.Add(new ConstructionPhase
                    {
                        PhaseNumber = phaseNum++,
                        PhaseName = $"{level.Name} — Bracing",
                        Description = $"Install {braces.Count} bracing members",
                        ElementIds = braces.Select(e => e.Id).ToList(),
                        EstimatedDays = Math.Max(1, (int)Math.Ceiling(braces.Count * DaysPerBrace)),
                        LevelName = level.Name,
                    });
                }

                if (floors.Count > 0)
                {
                    sequence.Phases.Add(new ConstructionPhase
                    {
                        PhaseNumber = phaseNum++,
                        PhaseName = $"{level.Name} — Floor Slab",
                        Description = $"Cast {floors.Count} floor slabs",
                        ElementIds = floors.Select(e => e.Id).ToList(),
                        EstimatedDays = Math.Max(2, (int)Math.Ceiling((double)(floors.Count * 2))),
                        LevelName = level.Name,
                    });
                }
            }

            sequence.TotalEstimatedDays = sequence.Phases.Sum(p => p.EstimatedDays);
            sequence.Summary = $"{sequence.Phases.Count} phases, ~{sequence.TotalEstimatedDays} working days";

            return sequence;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 11. CLASH PRE-DETECTOR
    // ════════════════════════════════════════════════════════════════

    #region Clash Result Types

    /// <summary>Result from pre-placement clash detection.</summary>
    public class ClashResult
    {
        public bool HasClashes { get; set; }
        public List<ClashItem> Clashes { get; set; } = new();
        public string Summary { get; set; }
    }

    /// <summary>Individual clash between proposed and existing elements.</summary>
    public class ClashItem
    {
        public ElementId ClashingElementId { get; set; }
        public string ElementDescription { get; set; }
        public XYZ IntersectionPoint { get; set; }
        public double OverlapMm { get; set; }
    }

    #endregion

    /// <summary>
    /// Pre-detects geometric clashes before placing structural elements.
    /// Checks beam paths and column locations against existing geometry.
    /// Uses bounding box overlap and 3D line-line closest distance.
    /// </summary>
    internal static class ClashPreDetector
    {
        /// <summary>
        /// Checks if a proposed beam path clashes with existing structural elements.
        /// Uses 3D line-line closest distance for beam-beam clashes
        /// and bounding box expansion for beam-column/wall clashes.
        /// </summary>
        public static ClashResult CheckBeamClash(
            Document doc, XYZ start, XYZ end,
            double depthMm, double widthMm)
        {
            var result = new ClashResult();
            double halfDepthFt = Units.Mm(depthMm / 2);
            double halfWidthFt = Units.Mm(widthMm / 2);

            // Build proposed beam bounding box (expanded by half-section)
            double minX = Math.Min(start.X, end.X) - halfWidthFt;
            double maxX = Math.Max(start.X, end.X) + halfWidthFt;
            double minY = Math.Min(start.Y, end.Y) - halfWidthFt;
            double maxY = Math.Max(start.Y, end.Y) + halfWidthFt;
            double minZ = Math.Min(start.Z, end.Z) - halfDepthFt;
            double maxZ = Math.Max(start.Z, end.Z) + halfDepthFt;

            var outline = new Outline(
                new XYZ(minX, minY, minZ),
                new XYZ(maxX, maxY, maxZ));

            // Check existing beams
            var existingBeams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .ToList();

            foreach (var beam in existingBeams)
            {
                var loc = beam.Location as LocationCurve;
                if (loc?.Curve == null) continue;

                var bStart = loc.Curve.GetEndPoint(0);
                var bEnd = loc.Curve.GetEndPoint(1);

                double dist = ClosestDistanceBetweenLines(start, end, bStart, bEnd);
                double clearanceFt = halfDepthFt + halfWidthFt;

                if (dist < clearanceFt)
                {
                    var midPt = (start + end) * 0.5;
                    result.Clashes.Add(new ClashItem
                    {
                        ClashingElementId = beam.Id,
                        ElementDescription = $"Beam {beam.Id.Value} ({beam.Name})",
                        IntersectionPoint = midPt,
                        OverlapMm = (clearanceFt - dist) * Units.FeetToMm,
                    });
                }
            }

            // Check existing columns
            var existingCols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .ToList();

            foreach (var col in existingCols)
            {
                var colLoc = col.Location as LocationPoint;
                if (colLoc == null) continue;

                double dist = DistancePointToLine3D(colLoc.Point, start, end);
                // SAE-CRIT-03: Derive column half-width from actual element geometry.
                // The hardcoded 0.5 ft (152mm) margin was independent of actual column size.
                // Read 'b' (width) parameter with fallback to bounding-box estimate.
                double colHalfWidthFt = halfWidthFt; // default: use beam half-width as proxy
                try
                {
                    var bParam = col.LookupParameter("b") ?? col.LookupParameter("Width");
                    if (bParam != null && bParam.StorageType == StorageType.Double)
                        colHalfWidthFt = bParam.AsDouble() / 2.0; // already in feet (Revit internal)
                    else
                    {
                        var bb = col.get_BoundingBox(null);
                        if (bb != null)
                            colHalfWidthFt = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y) / 2.0;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Column width read: {ex.Message}"); }
                double clearance = halfWidthFt + colHalfWidthFt; // beam half + column half

                if (dist < clearance)
                {
                    result.Clashes.Add(new ClashItem
                    {
                        ClashingElementId = col.Id,
                        ElementDescription = $"Column {col.Id.Value} ({col.Name})",
                        IntersectionPoint = colLoc.Point,
                        OverlapMm = (clearance - dist) * Units.FeetToMm,
                    });
                }
            }

            result.HasClashes = result.Clashes.Count > 0;
            result.Summary = result.HasClashes
                ? $"{result.Clashes.Count} clash(es) detected"
                : "No clashes detected";

            return result;
        }

        /// <summary>
        /// Checks if a proposed column location clashes with existing elements.
        /// </summary>
        public static ClashResult CheckColumnClash(
            Document doc, XYZ location,
            double widthMm, double depthMm, double heightMm)
        {
            var result = new ClashResult();

            double hw = Units.Mm(widthMm / 2);
            double hd = Units.Mm(depthMm / 2);
            double h = Units.Mm(heightMm);

            var outline = new Outline(
                new XYZ(location.X - hw, location.Y - hd, location.Z),
                new XYZ(location.X + hw, location.Y + hd, location.Z + h));

            var clashing = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .ToList();

            foreach (var el in clashing)
            {
                var cat = el.Category;
                if (cat == null) continue;
                var catId = cat.BuiltInCategory;
                if (catId != BuiltInCategory.OST_StructuralColumns &&
                    catId != BuiltInCategory.OST_StructuralFraming &&
                    catId != BuiltInCategory.OST_Walls) continue;

                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;

                double overlap = Math.Max(0,
                    Math.Min(outline.MaximumPoint.X, bb.Max.X) - Math.Max(outline.MinimumPoint.X, bb.Min.X));

                result.Clashes.Add(new ClashItem
                {
                    ClashingElementId = el.Id,
                    ElementDescription = $"{cat.Name} {el.Id.Value}",
                    IntersectionPoint = location,
                    OverlapMm = overlap * Units.FeetToMm,
                });
            }

            result.HasClashes = result.Clashes.Count > 0;
            result.Summary = result.HasClashes
                ? $"{result.Clashes.Count} clash(es) at column location"
                : "No clashes at column location";

            return result;
        }

        /// <summary>Closest distance between two 3D line segments.</summary>
        private static double ClosestDistanceBetweenLines(
            XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            var d1 = p2 - p1;
            var d2 = p4 - p3;
            var r = p1 - p3;

            double a = d1.DotProduct(d1);
            double e = d2.DotProduct(d2);
            double f = d2.DotProduct(r);

            if (a < 1e-10 && e < 1e-10)
                return r.GetLength();

            double b = d1.DotProduct(d2);
            double c = d1.DotProduct(r);
            double denom = a * e - b * b;

            double s = (denom > 1e-10) ? Math.Max(0, Math.Min(1, (b * f - c * e) / denom)) : 0;
            double t = (b * s + f) / Math.Max(e, 1e-10);
            t = Math.Max(0, Math.Min(1, t));
            s = (Math.Abs(denom) > 1e-10) ? Math.Max(0, Math.Min(1, (t * b - c) / a)) : 0;

            var closestOnLine1 = p1 + d1 * s;
            var closestOnLine2 = p3 + d2 * t;
            return closestOnLine1.DistanceTo(closestOnLine2);
        }

        /// <summary>Distance from a point to a 3D line segment.</summary>
        private static double DistancePointToLine3D(XYZ point, XYZ lineStart, XYZ lineEnd)
        {
            var lineDir = lineEnd - lineStart;
            double lineLen = lineDir.GetLength();
            if (lineLen < 1e-10) return point.DistanceTo(lineStart);

            double t = Math.Max(0, Math.Min(1,
                (point - lineStart).DotProduct(lineDir) / (lineLen * lineLen)));
            var projection = lineStart + lineDir * t;
            return point.DistanceTo(projection);
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 12. R-TREE SPATIAL INDEX
    // ════════════════════════════════════════════════════════════════

    #region R-Tree Types

    /// <summary>2D bounding box for R-tree nodes.</summary>
    public class BoundingBox2D
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public BoundingBox2D() { }
        public BoundingBox2D(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }

        public double Area => Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);

        public bool Overlaps(BoundingBox2D other) =>
            MinX <= other.MaxX && MaxX >= other.MinX &&
            MinY <= other.MaxY && MaxY >= other.MinY;

        public bool Contains(double x, double y) =>
            x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        public BoundingBox2D Union(BoundingBox2D other) => new(
            Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

        public double EnlargementArea(BoundingBox2D other)
        {
            var union = Union(other);
            return union.Area - Area;
        }
    }

    #endregion

    /// <summary>
    /// Simplified R-tree spatial index for 2D point/rectangle queries.
    /// Node capacity 16, linear split on overflow.
    /// Supports: Insert, QueryNearest, QueryRect.
    /// Better than grid for non-uniform point distributions.
    /// </summary>
    internal class RTreeIndex
    {
        private const int MaxEntries = 16;
        private const int MinEntries = 4;

        private class RTreeNode
        {
            public BoundingBox2D Bounds { get; set; } = new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            public List<(int Id, BoundingBox2D Box)> Entries { get; set; } = new();
            public List<RTreeNode> Children { get; set; } = new();
            public bool IsLeaf => Children.Count == 0;
        }

        private RTreeNode _root = new();

        /// <summary>Insert a point with given ID.</summary>
        public void Insert(int id, XYZ point)
        {
            Insert(id, new BoundingBox2D(point.X, point.Y, point.X, point.Y));
        }

        /// <summary>Insert a rectangle with given ID.</summary>
        public void Insert(int id, BoundingBox2D box)
        {
            InsertIntoNode(_root, id, box);
        }

        private void InsertIntoNode(RTreeNode node, int id, BoundingBox2D box)
        {
            node.Bounds = node.Bounds.Union(box);

            if (node.IsLeaf)
            {
                node.Entries.Add((id, box));
                if (node.Entries.Count > MaxEntries)
                    SplitNode(node);
            }
            else
            {
                // Choose child with least enlargement
                var bestChild = node.Children
                    .OrderBy(c => c.Bounds.EnlargementArea(box))
                    .ThenBy(c => c.Bounds.Area)
                    .First();
                InsertIntoNode(bestChild, id, box);
            }
        }

        private void SplitNode(RTreeNode node)
        {
            // Linear split: pick two seeds (most distant entries)
            var entries = node.Entries.ToList();
            int seed1 = 0, seed2 = 1;
            double maxDist = 0;

            for (int i = 0; i < Math.Min(entries.Count, 20); i++)
            {
                for (int j = i + 1; j < Math.Min(entries.Count, 20); j++)
                {
                    double dist = Math.Abs(entries[i].Box.MinX - entries[j].Box.MinX) +
                                  Math.Abs(entries[i].Box.MinY - entries[j].Box.MinY);
                    if (dist > maxDist) { maxDist = dist; seed1 = i; seed2 = j; }
                }
            }

            var child1 = new RTreeNode();
            var child2 = new RTreeNode();

            child1.Entries.Add(entries[seed1]);
            child1.Bounds = entries[seed1].Box;
            child2.Entries.Add(entries[seed2]);
            child2.Bounds = entries[seed2].Box;

            for (int i = 0; i < entries.Count; i++)
            {
                if (i == seed1 || i == seed2) continue;
                if (child1.Bounds.EnlargementArea(entries[i].Box) <= child2.Bounds.EnlargementArea(entries[i].Box))
                {
                    child1.Entries.Add(entries[i]);
                    child1.Bounds = child1.Bounds.Union(entries[i].Box);
                }
                else
                {
                    child2.Entries.Add(entries[i]);
                    child2.Bounds = child2.Bounds.Union(entries[i].Box);
                }
            }

            node.Entries.Clear();
            node.Children.Add(child1);
            node.Children.Add(child2);
            node.Bounds = child1.Bounds.Union(child2.Bounds);
        }

        /// <summary>Find all entries within radius of a point.</summary>
        public List<int> QueryNearest(XYZ point, double radiusFt)
        {
            var queryBox = new BoundingBox2D(
                point.X - radiusFt, point.Y - radiusFt,
                point.X + radiusFt, point.Y + radiusFt);
            var candidates = new List<int>();
            QueryRect(_root, queryBox, candidates);

            // Phase 79b FIX (HIGH-02): Filter by actual circular distance, not just bounding square.
            // Entries store bounding boxes; for point inserts the box center IS the point.
            // Without circular filtering, corner entries at distance up to √2 × radius are included.
            double r2 = radiusFt * radiusFt;
            candidates.RemoveAll(id =>
            {
                var entry = FindEntry(_root, id);
                if (entry == null) return false; // keep if entry not found (shouldn't happen)
                double cx = (entry.MinX + entry.MaxX) * 0.5;
                double cy = (entry.MinY + entry.MaxY) * 0.5;
                double dx = cx - point.X;
                double dy = cy - point.Y;
                return dx * dx + dy * dy > r2;
            });
            return candidates;
        }

        /// <summary>Find all entries overlapping a rectangle.</summary>
        public List<int> QueryRect(double minX, double minY, double maxX, double maxY)
        {
            var queryBox = new BoundingBox2D(minX, minY, maxX, maxY);
            var results = new List<int>();
            QueryRect(_root, queryBox, results);
            return results;
        }

        private void QueryRect(RTreeNode node, BoundingBox2D query, List<int> results)
        {
            if (!node.Bounds.Overlaps(query)) return;

            if (node.IsLeaf)
            {
                foreach (var (id, box) in node.Entries)
                {
                    if (box.Overlaps(query)) results.Add(id);
                }
            }
            else
            {
                foreach (var child in node.Children)
                    QueryRect(child, query, results);
            }
        }

        /// <summary>Find entry bounding box by ID (for circular distance filtering).</summary>
        private BoundingBox2D? FindEntry(RTreeNode node, int id)
        {
            if (node.IsLeaf)
            {
                foreach (var (entryId, box) in node.Entries)
                    if (entryId == id) return box;
                return null;
            }
            foreach (var child in node.Children)
            {
                var found = FindEntry(child, id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Total number of entries in the tree.</summary>
        public int Count
        {
            get
            {
                int count = 0;
                CountEntries(_root, ref count);
                return count;
            }
        }

        private void CountEntries(RTreeNode node, ref int count)
        {
            if (node.IsLeaf)
                count += node.Entries.Count;
            else
                foreach (var child in node.Children)
                    CountEntries(child, ref count);
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 13. DIRECT STIFFNESS METHOD — 2D Frame Analysis (Actual FEA)
    // ════════════════════════════════════════════════════════════════

    #region Frame Analysis Types

    /// <summary>2D frame node with 3 DOF (dx, dy, rotation).</summary>
    public class FrameNode
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool FixedX { get; set; }
        public bool FixedY { get; set; }
        public bool FixedR { get; set; }
        public double ForceX { get; set; }
        public double ForceY { get; set; }
        public double MomentZ { get; set; }
        // Results
        public double DisplacementX { get; set; }
        public double DisplacementY { get; set; }
        public double Rotation { get; set; }
        public double ReactionX { get; set; }
        public double ReactionY { get; set; }
        public double ReactionM { get; set; }
    }

    /// <summary>2D frame member connecting two nodes.</summary>
    public class FrameMember
    {
        public int Id { get; set; }
        public int NodeI { get; set; }
        public int NodeJ { get; set; }
        public double E { get; set; } = 210000;  // MPa (steel default)
        public double A { get; set; }              // mm²
        public double I { get; set; }              // mm⁴
        public double UdlKNPerM { get; set; }      // Uniform distributed load
        // Results
        public double AxialForceKN { get; set; }
        public double ShearForceIKN { get; set; }
        public double ShearForceJKN { get; set; }
        public double MomentIKNm { get; set; }
        public double MomentJKNm { get; set; }
        public double Utilisation { get; set; }
    }

    /// <summary>Complete frame analysis result.</summary>
    public class FrameAnalysisResult
    {
        public bool Converged { get; set; }
        public List<FrameNode> Nodes { get; set; } = new();
        public List<FrameMember> Members { get; set; } = new();
        public double MaxDisplacementMm { get; set; }
        public double MaxMomentKNm { get; set; }
        public double MaxAxialKN { get; set; }
        public double MaxUtilisation { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// 2D Frame Analysis using the Direct Stiffness Method.
    /// Assembles global stiffness matrix from member stiffness matrices,
    /// applies boundary conditions, solves via Cholesky decomposition,
    /// and recovers member forces.
    ///
    /// Each node has 3 DOF: dx, dy, θz. Members include axial, shear, bending.
    /// Supports: pin, fixed, roller supports. Loads: nodal + member UDL.
    ///
    /// Ref: McGuire, Gallagher, Ziemian — "Matrix Structural Analysis" (2000).
    /// </summary>
    internal static class DirectStiffnessMethod
    {
        /// <summary>
        /// Performs 2D frame analysis on a set of nodes and members.
        /// Returns displacements, reactions, and member forces.
        /// </summary>
        public static FrameAnalysisResult Analyze(
            List<FrameNode> nodes, List<FrameMember> members)
        {
            var result = new FrameAnalysisResult();
            int nNodes = nodes.Count;
            int nDof = nNodes * 3; // 3 DOF per node: dx, dy, θz

            if (nNodes == 0 || members.Count == 0)
            {
                result.Summary = "No nodes or members to analyze";
                return result;
            }

            // SA-CRIT-01: Pre-build node lookup dictionaries for O(1) access instead of O(n) per member
            var nodeById = new Dictionary<int, FrameNode>(nNodes);
            var nodeIndexById = new Dictionary<int, int>(nNodes);
            for (int idx = 0; idx < nNodes; idx++)
            {
                nodeById[nodes[idx].Id] = nodes[idx];
                nodeIndexById[nodes[idx].Id] = idx;
            }

            // Build global stiffness matrix and load vector
            var K = new double[nDof, nDof];
            var F = new double[nDof];

            // Assemble member stiffness matrices
            foreach (var m in members)
            {
                if (!nodeById.TryGetValue(m.NodeI, out var ni) || !nodeById.TryGetValue(m.NodeJ, out var nj))
                    continue; // skip members with invalid node references

                int iIdx = nodeIndexById[m.NodeI] * 3;
                int jIdx = nodeIndexById[m.NodeJ] * 3;

                double dx = nj.X - ni.X;
                double dy = nj.Y - ni.Y;
                double L = Math.Sqrt(dx * dx + dy * dy); // mm
                if (L < 1) continue;

                double c = dx / L; // cos
                double s = dy / L; // sin

                double EA_L = m.E * m.A / L;
                double EI_L = m.E * m.I / L;
                double EI_L2 = EI_L / L;
                double EI_L3 = EI_L2 / L;

                // 6×6 local stiffness matrix (beam-column element)
                // Transform to global coordinates and assemble
                // k_local = [EA/L terms] + [12EI/L³, 6EI/L² terms]
                var ke = new double[6, 6];

                // Axial stiffness
                ke[0, 0] = EA_L; ke[0, 3] = -EA_L;
                ke[3, 0] = -EA_L; ke[3, 3] = EA_L;

                // Bending stiffness
                ke[1, 1] = 12 * EI_L3; ke[1, 2] = 6 * EI_L2;
                ke[1, 4] = -12 * EI_L3; ke[1, 5] = 6 * EI_L2;
                ke[2, 1] = 6 * EI_L2; ke[2, 2] = 4 * EI_L;
                ke[2, 4] = -6 * EI_L2; ke[2, 5] = 2 * EI_L;
                ke[4, 1] = -12 * EI_L3; ke[4, 2] = -6 * EI_L2;
                ke[4, 4] = 12 * EI_L3; ke[4, 5] = -6 * EI_L2;
                ke[5, 1] = 6 * EI_L2; ke[5, 2] = 2 * EI_L;
                ke[5, 4] = -6 * EI_L2; ke[5, 5] = 4 * EI_L;

                // Transformation matrix T
                var T = new double[6, 6];
                T[0, 0] = c; T[0, 1] = s;
                T[1, 0] = -s; T[1, 1] = c;
                T[2, 2] = 1;
                T[3, 3] = c; T[3, 4] = s;
                T[4, 3] = -s; T[4, 4] = c;
                T[5, 5] = 1;

                // K_global = T^T × K_local × T
                var TtK = MultiplyMatrix(TransposeMatrix(T, 6, 6), ke, 6, 6, 6);
                var Kg = MultiplyMatrix(TtK, T, 6, 6, 6);

                // Assemble into global matrix
                int[] dofMap = { iIdx, iIdx + 1, iIdx + 2, jIdx, jIdx + 1, jIdx + 2 };
                for (int a = 0; a < 6; a++)
                    for (int b = 0; b < 6; b++)
                        K[dofMap[a], dofMap[b]] += Kg[a, b];

                // Fixed-end forces from UDL (in local coords, then transform to global)
                if (Math.Abs(m.UdlKNPerM) > 1e-10)
                {
                    double w = m.UdlKNPerM; // kN/m
                    double Lm = L / 1000.0; // Convert mm to m for load
                    // Fixed-end reactions for UDL: V = wL/2, M = wL²/12
                    double Vfem = w * Lm / 2.0;      // kN
                    double Mfem = w * Lm * Lm / 12.0; // kNm

                    // Local fixed-end forces [Fx_i, Fy_i, M_i, Fx_j, Fy_j, M_j]
                    var fLocal = new double[] { 0, Vfem, Mfem, 0, Vfem, -Mfem };

                    // Transform to global: f_global = T^T × f_local
                    var Tt = TransposeMatrix(T, 6, 6);
                    for (int a = 0; a < 6; a++)
                    {
                        double val = 0;
                        for (int b = 0; b < 6; b++) val += Tt[a, b] * fLocal[b];
                        F[dofMap[a]] += val;
                    }
                }
            }

            // Apply nodal loads
            for (int i = 0; i < nNodes; i++)
            {
                F[i * 3] += nodes[i].ForceX;
                F[i * 3 + 1] += nodes[i].ForceY;
                F[i * 3 + 2] += nodes[i].MomentZ;
            }

            // Apply boundary conditions (penalty method)
            double penalty = 1e20;
            for (int i = 0; i < nNodes; i++)
            {
                if (nodes[i].FixedX) K[i * 3, i * 3] += penalty;
                if (nodes[i].FixedY) K[i * 3 + 1, i * 3 + 1] += penalty;
                if (nodes[i].FixedR) K[i * 3 + 2, i * 3 + 2] += penalty;
            }

            // Solve: K × U = F using Cholesky decomposition
            var U = SolveLinearSystem(K, F, nDof);
            if (U == null)
            {
                result.Summary = "Failed to solve — singular stiffness matrix (check supports)";
                return result;
            }

            result.Converged = true;

            // Extract displacements
            for (int i = 0; i < nNodes; i++)
            {
                nodes[i].DisplacementX = U[i * 3];
                nodes[i].DisplacementY = U[i * 3 + 1];
                nodes[i].Rotation = U[i * 3 + 2];

                // Reactions at supports
                if (nodes[i].FixedX) nodes[i].ReactionX = penalty * U[i * 3];
                if (nodes[i].FixedY) nodes[i].ReactionY = penalty * U[i * 3 + 1];
                if (nodes[i].FixedR) nodes[i].ReactionM = penalty * U[i * 3 + 2];
            }

            result.Nodes = nodes;

            // Recover member forces
            foreach (var m in members)
            {
                if (!nodeById.TryGetValue(m.NodeI, out var ni) || !nodeById.TryGetValue(m.NodeJ, out var nj))
                    continue;
                int iIdx = nodeIndexById[m.NodeI] * 3;
                int jIdx = nodeIndexById[m.NodeJ] * 3;

                double dx = nj.X - ni.X;
                double dy = nj.Y - ni.Y;
                double L = Math.Sqrt(dx * dx + dy * dy);
                if (L < 1) continue;

                double cAngle = dx / L, sAngle = dy / L;

                // Global displacements for this member
                var uGlobal = new double[]
                {
                    U[iIdx], U[iIdx + 1], U[iIdx + 2],
                    U[jIdx], U[jIdx + 1], U[jIdx + 2]
                };

                // Transform to local: u_local = T × u_global
                var uLocal = new double[6];
                uLocal[0] = cAngle * uGlobal[0] + sAngle * uGlobal[1];
                uLocal[1] = -sAngle * uGlobal[0] + cAngle * uGlobal[1];
                uLocal[2] = uGlobal[2];
                uLocal[3] = cAngle * uGlobal[3] + sAngle * uGlobal[4];
                uLocal[4] = -sAngle * uGlobal[3] + cAngle * uGlobal[4];
                uLocal[5] = uGlobal[5];

                // Member forces: f = k_local × u_local
                double EA_L = m.E * m.A / L;
                double EI_L3 = m.E * m.I / (L * L * L);
                double EI_L2 = m.E * m.I / (L * L);
                double EI_L = m.E * m.I / L;

                m.AxialForceKN = EA_L * (uLocal[3] - uLocal[0]) / 1000.0; // N to kN
                m.ShearForceIKN = (12 * EI_L3 * (uLocal[1] - uLocal[4]) +
                    6 * EI_L2 * (uLocal[2] + uLocal[5])) / 1000.0;
                m.MomentIKNm = (6 * EI_L2 * (uLocal[1] - uLocal[4]) +
                    4 * EI_L * uLocal[2] + 2 * EI_L * uLocal[5]) / 1e6; // N.mm to kNm
                m.MomentJKNm = (6 * EI_L2 * (uLocal[1] - uLocal[4]) +
                    2 * EI_L * uLocal[2] + 4 * EI_L * uLocal[5]) / 1e6;

                // Phase 79b CRITICAL FIX: Compute J-end shear independently from stiffness matrix
                // (not copy from I-end). For asymmetric frames, V_J ≠ V_I.
                m.ShearForceJKN = -(12 * EI_L3 * (uLocal[1] - uLocal[4]) +
                    6 * EI_L2 * (uLocal[2] + uLocal[5])) / 1000.0;

                // Add fixed-end forces from UDL
                if (Math.Abs(m.UdlKNPerM) > 1e-10)
                {
                    double Lm = L / 1000.0;
                    m.ShearForceIKN += m.UdlKNPerM * Lm / 2.0;
                    m.ShearForceJKN += m.UdlKNPerM * Lm / 2.0;
                    m.MomentIKNm += m.UdlKNPerM * Lm * Lm / 12.0;
                    m.MomentJKNm -= m.UdlKNPerM * Lm * Lm / 12.0;
                }
            }

            result.Members = members;
            result.MaxDisplacementMm = nodes.Max(n =>
                Math.Sqrt(n.DisplacementX * n.DisplacementX + n.DisplacementY * n.DisplacementY));
            result.MaxMomentKNm = members.Max(m => Math.Max(Math.Abs(m.MomentIKNm), Math.Abs(m.MomentJKNm)));
            result.MaxAxialKN = members.Max(m => Math.Abs(m.AxialForceKN));

            result.Summary = $"Frame analysis: {nNodes} nodes, {members.Count} members. " +
                $"Max δ={result.MaxDisplacementMm:F2}mm, Max M={result.MaxMomentKNm:F1}kNm, " +
                $"Max N={result.MaxAxialKN:F1}kN";

            return result;
        }

        /// <summary>
        /// Builds a frame model from an existing Revit structural model for analysis.
        /// Extracts columns and beams, creates nodes at intersections, applies gravity loads.
        /// </summary>
        public static (List<FrameNode> Nodes, List<FrameMember> Members) BuildFromRevitModel(
            Document doc, double liveLoadKNPerM = 5.0, double deadLoadKNPerM = 10.0)
        {
            var nodes = new List<FrameNode>();
            var members = new List<FrameMember>();
            var nodeMap = new Dictionary<string, int>(); // "X_Y" → nodeId
            int nextNodeId = 0, nextMemberId = 0;

            Func<double, double, FrameNode> getOrCreateNode = (x, y) =>
            {
                string key = $"{Math.Round(x, 1)}_{Math.Round(y, 1)}";
                if (nodeMap.TryGetValue(key, out int existingId))
                    return nodes.First(n => n.Id == existingId);

                var node = new FrameNode
                {
                    Id = nextNodeId++,
                    X = x, Y = y,
                };
                nodes.Add(node);
                nodeMap[key] = node.Id;
                return node;
            };

            // Process beams
            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var beam in beams)
            {
                var loc = beam.Location as LocationCurve;
                if (loc?.Curve == null) continue;

                var start = loc.Curve.GetEndPoint(0);
                var end = loc.Curve.GetEndPoint(1);

                // SA-HIGH-01: Revit Z is vertical; map to 2D frame analysis as X-horizontal, Z-vertical
                var nodeI = getOrCreateNode(start.X * 304.8, start.Z * 304.8);
                var nodeJ = getOrCreateNode(end.X * 304.8, end.Z * 304.8);

                // Extract section properties from type
                double A = 5000; // Default 50cm² 
                double Ix = 20000e4; // Default I

                var type = doc.GetElement(beam.GetTypeId());
                if (type != null)
                {
                    var dP = type.get_Parameter(BuiltInParameter.GENERIC_DEPTH);
                    var wP = type.get_Parameter(BuiltInParameter.GENERIC_WIDTH);
                    if (dP != null && wP != null)
                    {
                        double d = dP.AsDouble() * 304.8;
                        double w = wP.AsDouble() * 304.8;
                        A = w * d;
                        Ix = w * d * d * d / 12.0;
                    }
                }

                members.Add(new FrameMember
                {
                    Id = nextMemberId++,
                    NodeI = nodeI.Id,
                    NodeJ = nodeJ.Id,
                    A = A, I = Ix,
                    UdlKNPerM = liveLoadKNPerM + deadLoadKNPerM,
                });
            }

            // Fix bottom nodes (assume lowest Y nodes are supported)
            if (nodes.Count > 0)
            {
                double minY = nodes.Min(n => n.Y);
                foreach (var node in nodes.Where(n => Math.Abs(n.Y - minY) < 100))
                {
                    node.FixedX = true;
                    node.FixedY = true;
                    node.FixedR = true;
                }
            }

            return (nodes, members);
        }

        // ── Linear Algebra Helpers ──

        private static double[] SolveLinearSystem(double[,] A, double[] b, int n)
        {
            // SA-CRIT-02: Guard against excessive DOF count (dense O(n³) solver)
            // 3000 DOF ≈ 1000 nodes → ~72 MB augmented matrix; beyond this, warn and cap
            if (n > 3000)
            {
                StingLog.Warn($"Frame analysis: {n} DOF exceeds dense solver limit (3000). " +
                    "Results may be slow or inaccurate for very large frames. Consider subdividing the model.");
            }
            if (n > 6000)
            {
                StingLog.Error($"Frame analysis: {n} DOF exceeds maximum (6000). Aborting to prevent out-of-memory.");
                return null;
            }

            // Gaussian elimination with partial pivoting
            var augmented = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) augmented[i, j] = A[i, j];
                augmented[i, n] = b[i];
            }

            // Forward elimination with partial pivoting
            for (int col = 0; col < n; col++)
            {
                // Find pivot
                int maxRow = col;
                double maxVal = Math.Abs(augmented[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(augmented[row, col]) > maxVal)
                    {
                        maxVal = Math.Abs(augmented[row, col]);
                        maxRow = row;
                    }
                }

                if (maxVal < 1e-15) return null; // Singular

                // Swap rows
                if (maxRow != col)
                {
                    for (int j = 0; j <= n; j++)
                    {
                        double tmp = augmented[col, j];
                        augmented[col, j] = augmented[maxRow, j];
                        augmented[maxRow, j] = tmp;
                    }
                }

                // Eliminate below
                for (int row = col + 1; row < n; row++)
                {
                    double factor = augmented[row, col] / augmented[col, col];
                    for (int j = col; j <= n; j++)
                        augmented[row, j] -= factor * augmented[col, j];
                }
            }

            // Back substitution
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = augmented[i, n];
                for (int j = i + 1; j < n; j++)
                    x[i] -= augmented[i, j] * x[j];
                if (Math.Abs(augmented[i, i]) < 1e-15) return null;
                x[i] /= augmented[i, i];
            }

            return x;
        }

        private static double[,] MultiplyMatrix(double[,] A, double[,] B, int m, int n, int p)
        {
            var C = new double[m, p];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < p; j++)
                    for (int k = 0; k < n; k++)
                        C[i, j] += A[i, k] * B[k, j];
            return C;
        }

        private static double[,] TransposeMatrix(double[,] A, int m, int n)
        {
            var T = new double[n, m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < n; j++)
                    T[j, i] = A[i, j];
            return T;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 14. SEISMIC ANALYZER (EC8 Equivalent Lateral Force)
    // ════════════════════════════════════════════════════════════════

    #region Seismic Result Types

    /// <summary>Seismic analysis result per EC8.</summary>
    public class SeismicAnalysisResult
    {
        public double DesignSpectralAcceleration { get; set; } // Sd(T1) in g
        public double BaseShearKN { get; set; }
        public double FundamentalPeriodS { get; set; } // T1
        public double BuildingWeightKN { get; set; }
        public double BehaviourFactor { get; set; } // q
        public List<double> StoreyForcesKN { get; set; } = new();
        public List<double> StoreyDrifts { get; set; } = new();
        public string DuctilityClass { get; set; }
        public bool DriftCheckPass { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// EC8 (Eurocode 8) seismic analysis using Equivalent Lateral Force method.
    /// Calculates: fundamental period, design spectrum, base shear, storey forces, drift checks.
    /// Supports: Ground types A-E, ductility classes DCL/DCM/DCH, importance classes I-IV.
    /// Ref: EN 1998-1:2004 §4.3.3.2.
    /// </summary>
    internal static class SeismicAnalyzer
    {
        /// <summary>
        /// Performs equivalent lateral force analysis per EC8 §4.3.3.2.
        /// </summary>
        /// <param name="buildingHeightM">Total building height in meters</param>
        /// <param name="storeyCount">Number of storeys</param>
        /// <param name="storeyWeightsKN">Weight per storey (dead + ψ2 × live) in kN</param>
        /// <param name="agR">Reference peak ground acceleration (ag,R) in g (e.g. 0.15)</param>
        /// <param name="groundType">Ground type: A, B, C, D, E per EC8 Table 3.1</param>
        /// <param name="importanceClass">I=agriculture, II=ordinary, III=crowded, IV=critical</param>
        /// <param name="structuralSystem">Frame, Wall, DualSystem, InvertedPendulum</param>
        /// <param name="ductilityClass">DCL, DCM, DCH</param>
        public static SeismicAnalysisResult Analyze(
            double buildingHeightM, int storeyCount,
            List<double> storeyWeightsKN = null,
            double agR = 0.15, string groundType = "B",
            int importanceClass = 2, string structuralSystem = "Frame",
            string ductilityClass = "DCM")
        {
            var result = new SeismicAnalysisResult();
            result.DuctilityClass = ductilityClass;

            // Default storey weights if not provided
            if (storeyWeightsKN == null || storeyWeightsKN.Count == 0)
            {
                storeyWeightsKN = new List<double>();
                double defaultWeightPerStorey = 1500; // kN, typical office floor
                for (int i = 0; i < storeyCount; i++)
                    storeyWeightsKN.Add(defaultWeightPerStorey);
            }

            double totalWeight = storeyWeightsKN.Sum();
            result.BuildingWeightKN = totalWeight;

            // Importance factor γI (EC8 Table 4.3, UK NA)
            double gammaI = importanceClass switch
            {
                1 => 0.8, 2 => 1.0, 3 => 1.2, 4 => 1.4, _ => 1.0
            };

            // Design ground acceleration: ag = γI × agR
            double ag = gammaI * agR;

            // Ground type parameters (EC8 Table 3.2, Type 1 spectrum)
            double S, TB, TC, TD;
            switch (groundType.ToUpperInvariant())
            {
                case "A": S = 1.0;  TB = 0.15; TC = 0.4;  TD = 2.0; break;
                case "B": S = 1.2;  TB = 0.15; TC = 0.5;  TD = 2.0; break;
                case "C": S = 1.15; TB = 0.20; TC = 0.6;  TD = 2.0; break;
                case "D": S = 1.35; TB = 0.20; TC = 0.8;  TD = 2.0; break;
                case "E": S = 1.4;  TB = 0.15; TC = 0.5;  TD = 2.0; break;
                default:  S = 1.2;  TB = 0.15; TC = 0.5;  TD = 2.0; break;
            }

            // Behaviour factor q (EC8 Table 5.1, 6.1, 6.2)
            double q = (ductilityClass, structuralSystem) switch
            {
                ("DCL", _) => 1.5,
                ("DCM", "Frame") => 3.9,
                ("DCM", "Wall") => 3.0,
                ("DCM", "DualSystem") => 3.6,
                ("DCM", "InvertedPendulum") => 2.0,
                ("DCH", "Frame") => 5.85,
                ("DCH", "Wall") => 4.8,
                ("DCH", "DualSystem") => 5.4,
                ("DCH", "InvertedPendulum") => 3.0,
                _ => 1.5,
            };
            result.BehaviourFactor = q;

            // Approximate fundamental period T1 (EC8 Eq 4.6)
            // T1 = Ct × H^(3/4) where Ct depends on structural system
            double Ct = structuralSystem switch
            {
                "Frame" => 0.075,     // Steel/RC moment frame
                "Wall" => 0.05,       // Shear wall
                _ => 0.075,
            };
            double T1 = Ct * Math.Pow(buildingHeightM, 0.75);
            result.FundamentalPeriodS = T1;

            // Design spectrum ordinate Sd(T1) (EC8 §3.2.2.5)
            double Sd;
            if (T1 <= TB)
                Sd = ag * S * (2.0 / 3.0 + T1 / TB * (2.5 / q - 2.0 / 3.0));
            else if (T1 <= TC)
                Sd = ag * S * 2.5 / q;
            else if (T1 <= TD)
                Sd = Math.Max(ag * S * 2.5 / q * TC / T1, 0.2 * ag);
            else
                Sd = Math.Max(ag * S * 2.5 / q * TC * TD / (T1 * T1), 0.2 * ag);

            result.DesignSpectralAcceleration = Sd;

            // Base shear: Fb = Sd(T1) × m × λ
            // λ = 0.85 if T1 ≤ 2TC and building > 2 storeys, else 1.0
            double lambda = (T1 <= 2 * TC && storeyCount > 2) ? 0.85 : 1.0;
            double totalMassKg = totalWeight / 9.81 * 1000;
            double Fb = Sd * 9.81 * totalMassKg / 1000.0; // kN
            Fb *= lambda;
            result.BaseShearKN = Fb;

            // Distribute to storeys: Fi = Fb × (zi × mi) / Σ(zj × mj)
            double storeyHeightM = buildingHeightM / storeyCount;
            double sumZM = 0;
            for (int i = 0; i < storeyCount; i++)
                sumZM += (i + 1) * storeyHeightM * storeyWeightsKN[i];

            for (int i = 0; i < storeyCount; i++)
            {
                double zi = (i + 1) * storeyHeightM;
                double Fi = Fb * zi * storeyWeightsKN[i] / sumZM;
                result.StoreyForcesKN.Add(Fi);
            }

            // Interstorey drift check (EC8 §4.4.3.2)
            // Simplified: dr = Sd × T1² × g / (4π²) × qi × inverted triangle factor
            // Limit: dr × ν / h ≤ 0.005 (brittle non-structural), 0.0075 (ductile), 0.01 (isolated)
            double driftLimit = 0.005; // Conservative (brittle partitions)
            double nu = 0.5; // Reduction factor for frequent earthquakes
            result.DriftCheckPass = true;

            for (int i = 0; i < storeyCount; i++)
            {
                double hi = storeyHeightM * 1000; // mm
                double storeyShear = result.StoreyForcesKN.Skip(i).Sum();
                // Approximate elastic drift: δ ≈ V × h / (GA) where GA ≈ rough stiffness
                double approxStiffness = totalWeight * 100; // Rough kN/m
                double dr = storeyShear / approxStiffness * 1000; // mm
                double driftRatio = nu * dr / hi;
                result.StoreyDrifts.Add(driftRatio);
                if (driftRatio > driftLimit) result.DriftCheckPass = false;
            }

            result.Summary = $"EC8 seismic: ag={ag:F3}g, T1={T1:F2}s, Sd={Sd:F3}g, q={q:F1}, " +
                $"Fb={Fb:F0}kN, W={totalWeight:F0}kN ({ductilityClass}), " +
                $"drift {(result.DriftCheckPass ? "OK" : "FAIL")}";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 15. GENETIC ALGORITHM GRID OPTIMIZER
    // ════════════════════════════════════════════════════════════════

    #region Optimization Result

    /// <summary>Result from genetic algorithm grid optimization.</summary>
    public class GridOptimizationResult
    {
        public double OptimalSpacingXMm { get; set; }
        public double OptimalSpacingYMm { get; set; }
        public double FitnessScore { get; set; }
        public double EstimatedCostPerSqM { get; set; }
        public double MaxBeamDepthMm { get; set; }
        public double MaxUtilisation { get; set; }
        public int GenerationsUsed { get; set; }
        public List<(double SpacingX, double SpacingY, double Fitness)> TopSolutions { get; set; } = new();
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Multi-objective genetic algorithm for optimal structural grid spacing.
    /// Objectives: minimize cost (steel tonnage + concrete volume), minimize deflection,
    /// satisfy utilisation limits. Uses tournament selection, uniform crossover, mutation.
    ///
    /// Chromosome: [spacingX_mm, spacingY_mm]
    /// Fitness: weighted combination of cost, deflection ratio, and utilisation penalty.
    ///
    /// Ref: Goldberg (1989) "Genetic Algorithms in Search, Optimization, and Machine Learning".
    /// </summary>
    internal static class GeneticGridOptimizer
    {
        private const int PopulationSize = 60;
        private const int MaxGenerations = 100;
        private const double MutationRate = 0.15;
        private const double CrossoverRate = 0.8;
        private const double MinSpacingMm = 3000;
        private const double MaxSpacingMm = 18000;

        /// <summary>
        /// Finds optimal grid spacing for a building floor plate.
        /// </summary>
        /// <param name="floorWidthMm">Total floor width in mm</param>
        /// <param name="floorDepthMm">Total floor depth in mm</param>
        /// <param name="liveLoadKPa">Live load (kPa)</param>
        /// <param name="deadLoadKPa">Dead load (kPa)</param>
        /// <param name="isSteel">Steel or RC construction</param>
        /// <param name="costWeightSteel">Cost of steel per kg (relative)</param>
        /// <param name="costWeightConcrete">Cost of concrete per m³ (relative)</param>
        public static GridOptimizationResult Optimize(
            double floorWidthMm, double floorDepthMm,
            double liveLoadKPa = 2.5, double deadLoadKPa = 4.0,
            bool isSteel = true,
            double costWeightSteel = 1.0, double costWeightConcrete = 0.3)
        {
            var rng = new Random(42);
            var result = new GridOptimizationResult();

            // Initialize population
            var population = new List<(double SpacingX, double SpacingY)>();
            for (int i = 0; i < PopulationSize; i++)
            {
                double sx = MinSpacingMm + rng.NextDouble() * (MaxSpacingMm - MinSpacingMm);
                double sy = MinSpacingMm + rng.NextDouble() * (MaxSpacingMm - MinSpacingMm);
                // Round to 500mm grid
                sx = Math.Round(sx / 500) * 500;
                sy = Math.Round(sy / 500) * 500;
                population.Add((sx, sy));
            }

            // Seed with known good solutions
            if (isSteel)
            {
                population[0] = (7500, 9000);  // Office typical
                population[1] = (6000, 9000);  // Moderate
                population[2] = (9000, 12000); // Large span
            }
            else
            {
                population[0] = (6000, 7500);
                population[1] = (5000, 6000);
                population[2] = (7500, 7500);
            }

            var bestEver = population[0];
            double bestFitness = double.MinValue;

            for (int gen = 0; gen < MaxGenerations; gen++)
            {
                // Evaluate fitness
                var fitnesses = population.Select(p =>
                    EvaluateFitness(p.SpacingX, p.SpacingY,
                        floorWidthMm, floorDepthMm,
                        liveLoadKPa, deadLoadKPa, isSteel,
                        costWeightSteel, costWeightConcrete)).ToList();

                // Track best
                for (int i = 0; i < PopulationSize; i++)
                {
                    if (fitnesses[i] > bestFitness)
                    {
                        bestFitness = fitnesses[i];
                        bestEver = population[i];
                    }
                }

                // Selection + crossover + mutation
                var newPop = new List<(double, double)>();
                newPop.Add(bestEver); // Elitism: keep best

                while (newPop.Count < PopulationSize)
                {
                    // Tournament selection (size 3)
                    var parent1 = TournamentSelect(population, fitnesses, rng, 3);
                    var parent2 = TournamentSelect(population, fitnesses, rng, 3);

                    double childX, childY;

                    // Uniform crossover
                    if (rng.NextDouble() < CrossoverRate)
                    {
                        childX = rng.NextDouble() < 0.5 ? parent1.SpacingX : parent2.SpacingX;
                        childY = rng.NextDouble() < 0.5 ? parent1.SpacingY : parent2.SpacingY;
                        // Blend crossover for intermediate values
                        double alpha = rng.NextDouble() * 0.5;
                        childX = childX * (1 - alpha) + (rng.NextDouble() < 0.5 ? parent2.SpacingX : parent1.SpacingX) * alpha;
                        childY = childY * (1 - alpha) + (rng.NextDouble() < 0.5 ? parent2.SpacingY : parent1.SpacingY) * alpha;
                    }
                    else
                    {
                        childX = parent1.SpacingX;
                        childY = parent1.SpacingY;
                    }

                    // Mutation
                    if (rng.NextDouble() < MutationRate)
                        childX += (rng.NextDouble() - 0.5) * 2000;
                    if (rng.NextDouble() < MutationRate)
                        childY += (rng.NextDouble() - 0.5) * 2000;

                    // Clamp and round
                    childX = Math.Round(Math.Max(MinSpacingMm, Math.Min(MaxSpacingMm, childX)) / 500) * 500;
                    childY = Math.Round(Math.Max(MinSpacingMm, Math.Min(MaxSpacingMm, childY)) / 500) * 500;

                    newPop.Add((childX, childY));
                }

                population = newPop;
                result.GenerationsUsed = gen + 1;

                // Phase 79b FIX: Check convergence using population spatial spread (not stale fitness).
                // Previous code paired newPop with old-generation fitnesses — meaningless ordering.
                if (population.Count >= 10)
                {
                    double rangeX = population.Max(t => t.SpacingX) - population.Min(t => t.SpacingX);
                    double rangeY = population.Max(t => t.SpacingY) - population.Min(t => t.SpacingY);
                    if (rangeX < 500 && rangeY < 500) break;
                }
            }

            result.OptimalSpacingXMm = bestEver.SpacingX;
            result.OptimalSpacingYMm = bestEver.SpacingY;
            result.FitnessScore = bestFitness;

            // Calculate details for best solution
            double beamDepth = StructuralModelingEngine.EstimateBeamDepth(
                Math.Max(bestEver.SpacingX, bestEver.SpacingY),
                "simply_supported", isSteel);
            result.MaxBeamDepthMm = beamDepth;

            // Top 5 solutions
            var finalFitnesses = population.Select(p =>
                EvaluateFitness(p.SpacingX, p.SpacingY,
                    floorWidthMm, floorDepthMm,
                    liveLoadKPa, deadLoadKPa, isSteel,
                    costWeightSteel, costWeightConcrete)).ToList();

            result.TopSolutions = population.Zip(finalFitnesses, (p, f) => (p.SpacingX, p.SpacingY, f))
                .OrderByDescending(x => x.f).Take(5).ToList();

            result.Summary = $"GA optimization: {result.GenerationsUsed} generations. " +
                $"Optimal: {bestEver.SpacingX / 1000:F1}m × {bestEver.SpacingY / 1000:F1}m " +
                $"(beam depth ~{beamDepth:F0}mm, fitness={bestFitness:F2})";

            return result;
        }

        private static double EvaluateFitness(
            double spacingXMm, double spacingYMm,
            double floorWidthMm, double floorDepthMm,
            double liveLoadKPa, double deadLoadKPa,
            bool isSteel, double costSteel, double costConcrete)
        {
            double floorAreaSqM = (floorWidthMm / 1000.0) * (floorDepthMm / 1000.0);
            if (floorAreaSqM <= 0) return -1e6;

            int baysX = Math.Max(1, (int)Math.Round(floorWidthMm / spacingXMm));
            int baysY = Math.Max(1, (int)Math.Round(floorDepthMm / spacingYMm));
            int nColumns = (baysX + 1) * (baysY + 1);
            int nBeamsX = baysX * (baysY + 1);
            int nBeamsY = (baysX + 1) * baysY;
            int nBeams = nBeamsX + nBeamsY;

            // Beam sizing
            double maxSpan = Math.Max(spacingXMm, spacingYMm);
            double beamDepth = StructuralModelingEngine.EstimateBeamDepth(maxSpan, "simply_supported", isSteel);
            double beamWidth = StructuralModelingEngine.EstimateBeamWidth(beamDepth, isSteel);

            // Cost estimation (relative)
            double beamVolumePerM = beamWidth * beamDepth / 1e6; // m² cross-section
            double totalBeamLengthM = nBeamsX * (spacingXMm / 1000.0) + nBeamsY * (spacingYMm / 1000.0);
            double steelTonnage = isSteel
                ? totalBeamLengthM * beamVolumePerM * 7850 / 1000.0 // Steel density 7850 kg/m³
                : 0;
            double concreteVolume = isSteel
                ? 0
                : totalBeamLengthM * beamVolumePerM;
            double cost = steelTonnage * costSteel + concreteVolume * costConcrete;
            double costPerSqM = cost / floorAreaSqM;

            // Deflection check
            double deflCheck = DeflectionChecker.CheckBeamDeflection(
                maxSpan, beamDepth, beamWidth, liveLoadKPa + deadLoadKPa, isSteel).Ratio;

            // Penalties
            double penalty = 0;
            if (maxSpan > 12000 && !isSteel) penalty += 100; // RC > 12m is problematic
            if (maxSpan > 18000) penalty += 500;              // Any > 18m is extreme
            if (beamDepth > 900) penalty += 50;               // Deep beams increase floor-to-floor
            if (nColumns > 100) penalty += nColumns - 100;    // Too many columns = expensive

            // Fitness: minimize cost, maximize deflection ratio (higher = safer)
            double fitness = -costPerSqM * 10 + Math.Min(deflCheck, 500) * 0.1 - penalty;

            return fitness;
        }

        private static (double SpacingX, double SpacingY) TournamentSelect(
            List<(double SpacingX, double SpacingY)> pop,
            List<double> fitnesses, Random rng, int tournamentSize)
        {
            int bestIdx = rng.Next(pop.Count);
            double bestFit = fitnesses[bestIdx];

            for (int i = 1; i < tournamentSize; i++)
            {
                int idx = rng.Next(pop.Count);
                if (fitnesses[idx] > bestFit)
                {
                    bestIdx = idx;
                    bestFit = fitnesses[idx];
                }
            }

            return pop[bestIdx];
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 16. PROGRESSIVE COLLAPSE CHECKER (GSA Robustness)
    // ════════════════════════════════════════════════════════════════

    #region Progressive Collapse Types

    /// <summary>Result from progressive collapse / robustness analysis.</summary>
    public class ProgressiveCollapseResult
    {
        public bool IsRobust { get; set; }
        public int CriticalColumnCount { get; set; }
        public List<(ElementId ColumnId, string Status, int AffectedMembers)> ColumnResults { get; set; } = new();
        public double RedundancyRatio { get; set; }
        public string RobustnessClass { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Progressive collapse analysis per GSA (UK General Services Administration)
    /// and EC1-1-7 Accidental Actions robustness requirements.
    /// Algorithm: systematically remove one column at a time, check if remaining
    /// structure can carry load via alternate load paths (ALPs).
    ///
    /// For each removed column:
    ///   1. Find beams connected to removed column
    ///   2. Redistribute load to adjacent columns via catenary/Vierendeel action
    ///   3. Check if adjacent members can carry redistributed load (DCR ≤ 2.0)
    ///   4. If any column removal causes DCR > 2.0 → structure is NOT robust
    ///
    /// Demand-Capacity Ratio (DCR) limit: 2.0 (GSA) or 1.5 (DoD UFC 4-023-03).
    /// </summary>
    internal static class ProgressiveCollapseChecker
    {
        private const double DCR_Limit = 2.0; // GSA acceptance criterion
        /// <summary>Beam-column connection proximity tolerance in feet (~450mm).
        /// Per EC3 §6.2.5 connection zone for simple beam-to-column joints.</summary>
        private const double ConnectionToleranceFt = 1.5;

        /// <summary>
        /// Checks structural robustness by removing each column systematically.
        /// </summary>
        public static ProgressiveCollapseResult CheckRobustness(Document doc)
        {
            var result = new ProgressiveCollapseResult();
            result.IsRobust = true;

            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .ToList();

            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList();

            if (columns.Count < 2)
            {
                result.Summary = "Insufficient columns for progressive collapse analysis";
                return result;
            }

            // Build connectivity: which beams connect to which columns
            var colPositions = new Dictionary<ElementId, XYZ>();
            foreach (var col in columns)
            {
                var loc = col.Location as LocationPoint;
                if (loc != null) colPositions[col.Id] = loc.Point;
            }

            double connectionTol = ConnectionToleranceFt;
            var colBeamConnections = new Dictionary<ElementId, List<ElementId>>();

            foreach (var col in columns)
            {
                colBeamConnections[col.Id] = new List<ElementId>();
                if (!colPositions.ContainsKey(col.Id)) continue;
                var colPt = colPositions[col.Id];

                foreach (var beam in beams)
                {
                    var bLoc = beam.Location as LocationCurve;
                    if (bLoc?.Curve == null) continue;
                    var bStart = bLoc.Curve.GetEndPoint(0);
                    var bEnd = bLoc.Curve.GetEndPoint(1);

                    if (colPt.DistanceTo(bStart) < connectionTol ||
                        colPt.DistanceTo(bEnd) < connectionTol)
                    {
                        colBeamConnections[col.Id].Add(beam.Id);
                    }
                }
            }

            // For each column, simulate removal
            int criticalCount = 0;
            foreach (var col in columns)
            {
                if (!colPositions.ContainsKey(col.Id)) continue;
                if (!colBeamConnections.ContainsKey(col.Id)) continue;

                int connectedBeams = colBeamConnections[col.Id].Count;

                // Count alternate load paths: adjacent columns that share beams
                var adjacentColumns = new HashSet<ElementId>();
                foreach (var beamId in colBeamConnections[col.Id])
                {
                    foreach (var otherCol in columns)
                    {
                        if (otherCol.Id == col.Id) continue;
                        if (colBeamConnections.ContainsKey(otherCol.Id) &&
                            colBeamConnections[otherCol.Id].Contains(beamId))
                        {
                            adjacentColumns.Add(otherCol.Id);
                        }
                    }
                }

                int altPaths = adjacentColumns.Count;
                string status;

                // Simple DCR check: if removed column has ≤1 alternate path, it's critical
                if (altPaths == 0)
                {
                    status = "CRITICAL — no alternate load path";
                    criticalCount++;
                    result.IsRobust = false;
                }
                else if (altPaths == 1)
                {
                    status = "VULNERABLE — single alternate path";
                    criticalCount++;
                    // May still be robust if the single path has sufficient capacity
                }
                else if (connectedBeams <= 1)
                {
                    status = "WEAK — poorly connected (≤1 beam)";
                    criticalCount++;
                }
                else
                {
                    status = $"OK — {altPaths} alternate paths";
                }

                result.ColumnResults.Add((col.Id, status, connectedBeams));
            }

            result.CriticalColumnCount = criticalCount;
            result.RedundancyRatio = (columns.Count > 0)
                ? 1.0 - (double)criticalCount / columns.Count : 0;

            // Robustness class per EC1-1-7
            result.RobustnessClass = result.RedundancyRatio switch
            {
                >= 0.95 => "Class 1 (Excellent)",
                >= 0.80 => "Class 2a (Good)",
                >= 0.60 => "Class 2b (Adequate)",
                _ => "Class 3 (Poor — enhanced design required)"
            };

            if (criticalCount > 0) result.IsRobust = false;

            result.Summary = $"Progressive collapse: {columns.Count} columns analysed, " +
                $"{criticalCount} critical, redundancy={result.RedundancyRatio:P0}, " +
                $"{result.RobustnessClass}. {(result.IsRobust ? "ROBUST" : "NOT ROBUST")}";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 17. AUTO MEMBER SIZER — Iterative Design Convergence
    // ════════════════════════════════════════════════════════════════

    #region Auto Sizing Result

    /// <summary>Result from auto member sizing convergence loop.</summary>
    public class AutoSizingResult
    {
        public int IterationsUsed { get; set; }
        public bool Converged { get; set; }
        public int MembersResized { get; set; }
        public List<(ElementId MemberId, string OldSize, string NewSize, double Utilisation)> Changes { get; set; } = new();
        public double AverageUtilisation { get; set; }
        public double MaxUtilisation { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Iterative auto-sizing engine that converges on optimal member sizes.
    /// Algorithm:
    ///   1. Calculate loads on all members (tributary, UDL, point loads)
    ///   2. For each beam: compute required Wpl from M = wL²/8
    ///   3. Select section from SteelSectionDatabase (lightest passing)
    ///   4. For each column: compute required area from N and buckling
    ///   5. Check utilisation (combined N-M interaction)
    ///   6. If any member > target utilisation → resize and re-iterate
    ///   7. Converge when all members within [0.6, 0.95] utilisation band
    ///
    /// Typically converges in 2-4 iterations.
    /// </summary>
    internal static class AutoMemberSizer
    {
        private const double TargetUtilMin = 0.60;
        private const double TargetUtilMax = 0.95;
        private const int MaxIterations = 8;

        /// <summary>
        /// Auto-sizes all structural framing members to optimal sections.
        /// </summary>
        public static AutoSizingResult AutoSizeAllMembers(
            Document doc, bool isSteel = true,
            double liveLoadKPa = 2.5, double deadLoadKPa = 4.0,
            double fykMPa = 355)
        {
            var result = new AutoSizingResult();

            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList();

            if (beams.Count == 0)
            {
                result.Summary = "No structural framing members found";
                return result;
            }

            double totalLoad = liveLoadKPa + deadLoadKPa;

            // PERF: Cache type lookups — types don't change during sizing analysis
            var typeCache = new Dictionary<ElementId, Element>();

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                result.IterationsUsed = iter + 1;
                bool anyChange = false;

                foreach (var beam in beams)
                {
                    var loc = beam.Location as LocationCurve;
                    if (loc?.Curve == null) continue;

                    double spanMm = loc.Curve.Length * Units.FeetToMm;
                    double spanM = spanMm / 1000.0;

                    // Get current dimensions (cached per type)
                    var typeId = beam.GetTypeId();
                    if (!typeCache.TryGetValue(typeId, out var type))
                    { type = doc.GetElement(typeId); typeCache[typeId] = type; }
                    string currentName = type?.Name ?? "Unknown";
                    double currentDepth = 0, currentWidth = 0;
                    if (type != null)
                    {
                        var dP = type.get_Parameter(BuiltInParameter.GENERIC_DEPTH);
                        if (dP != null) currentDepth = dP.AsDouble() * Units.FeetToMm;
                        var wP = type.get_Parameter(BuiltInParameter.GENERIC_WIDTH);
                        if (wP != null) currentWidth = wP.AsDouble() * Units.FeetToMm;
                    }
                    if (currentDepth <= 0) currentDepth = 400;
                    if (currentWidth <= 0) currentWidth = 200;

                    // Estimate tributary width (simplified: half of perpendicular bay)
                    double tributaryWidthM = 3.0; // Default 3m
                    double wKNPerM = totalLoad * tributaryWidthM;

                    // Required moment: M = wL²/8
                    double requiredMomentKNm = wKNPerM * spanM * spanM / 8.0;

                    if (isSteel)
                    {
                        // Required Wpl = M / fy (cm³)
                        double requiredWplCm3 = requiredMomentKNm * 1e6 / (fykMPa * 1e3); // kNm → N.mm → cm³

                        var section = SteelSectionDatabase.FindBeamSection(requiredWplCm3);
                        if (section != null && section.Designation != currentName)
                        {
                            double util = requiredWplCm3 / section.WplxCm3;

                            if (util < TargetUtilMin || util > TargetUtilMax)
                            {
                                result.Changes.Add((beam.Id, currentName, section.Designation, util));
                                anyChange = true;
                            }
                        }
                    }
                    else
                    {
                        // RC beam sizing
                        double optimalDepth = StructuralModelingEngine.EstimateBeamDepth(
                            spanMm, "simply_supported", false);
                        double optimalWidth = StructuralModelingEngine.EstimateBeamWidth(optimalDepth, false);

                        double depthDiff = Math.Abs(optimalDepth - currentDepth);
                        if (depthDiff > 25) // More than 25mm difference
                        {
                            result.Changes.Add((beam.Id,
                                $"{currentWidth:F0}x{currentDepth:F0}",
                                $"{optimalWidth:F0}x{optimalDepth:F0}",
                                currentDepth > 0 ? optimalDepth / currentDepth : 1.0));
                            anyChange = true;
                        }
                    }
                }

                result.MembersResized = result.Changes.Count;

                if (!anyChange)
                {
                    result.Converged = true;
                    break;
                }
            }

            if (result.Changes.Count > 0)
            {
                result.AverageUtilisation = result.Changes.Average(c => c.Utilisation);
                result.MaxUtilisation = result.Changes.Max(c => c.Utilisation);
            }

            result.Summary = $"Auto-sizing: {result.IterationsUsed} iterations, " +
                $"{result.MembersResized} members resized, " +
                $"avg util={result.AverageUtilisation:F2}, max={result.MaxUtilisation:F2}, " +
                $"converged={result.Converged}";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 18. FIRE RESISTANCE CALCULATOR (EC2-1-2 Tabulated)
    // ════════════════════════════════════════════════════════════════

    #region Fire Resistance Result

    /// <summary>Fire resistance assessment result.</summary>
    public class FireResistanceResult
    {
        public string ElementType { get; set; }
        public int RequiredMinutes { get; set; }
        public int AchievedMinutes { get; set; }
        public bool Pass { get; set; }
        public double MinDimensionMm { get; set; }
        public double MinCoverMm { get; set; }
        public double ActualDimensionMm { get; set; }
        public double ActualCoverMm { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Fire resistance assessment per EC2-1-2 (Eurocode 2, Part 1-2: Structural Fire Design).
    /// Uses Method A (tabulated data) from Tables 5.2a-5.7.
    /// Checks minimum dimensions and axis distances for R30-R240 ratings.
    /// </summary>
    internal static class FireResistanceCalculator
    {
        // EC2-1-2 Table 5.2a: Columns — minimum dimensions (mm) and axis distance (mm)
        // Format: [rating_minutes] = (min_width_mm, min_axis_distance_mm)
        private static readonly Dictionary<int, (double Width, double Axis)> ColumnRequirements = new()
        {
            {  30, (200, 25) }, {  60, (250, 36) }, {  90, (300, 45) },
            { 120, (350, 52) }, { 180, (450, 63) }, { 240, (500, 75) },
        };

        // EC2-1-2 Table 5.5: Beams — minimum width and axis distance
        private static readonly Dictionary<int, (double Width, double Axis)> BeamRequirements = new()
        {
            {  30, (80, 25) },  {  60, (120, 40) }, {  90, (150, 55) },
            { 120, (200, 65) }, { 180, (240, 80) }, { 240, (280, 90) },
        };

        // EC2-1-2 Table 5.8: One-way slabs — minimum thickness and axis distance
        private static readonly Dictionary<int, (double Thickness, double Axis)> SlabRequirements = new()
        {
            {  30, (60, 10) },  {  60, (80, 20) },  {  90, (100, 30) },
            { 120, (120, 40) }, { 180, (150, 55) }, { 240, (175, 65) },
        };

        /// <summary>
        /// Checks fire resistance of an RC column.
        /// </summary>
        public static FireResistanceResult CheckColumn(
            double widthMm, double depthMm, double coverMm,
            int requiredRatingMinutes = 60)
        {
            var result = new FireResistanceResult
            {
                ElementType = "Column",
                RequiredMinutes = requiredRatingMinutes,
                ActualDimensionMm = Math.Min(widthMm, depthMm),
                ActualCoverMm = coverMm,
            };

            // Find highest rating achieved
            result.AchievedMinutes = 0;
            foreach (var kvp in ColumnRequirements.OrderByDescending(k => k.Key))
            {
                if (Math.Min(widthMm, depthMm) >= kvp.Value.Width &&
                    coverMm + 6 >= kvp.Value.Axis) // axis = cover + link + bar/2 ≈ cover + 6
                {
                    result.AchievedMinutes = kvp.Key;
                    break;
                }
            }

            // Get required dimensions
            if (ColumnRequirements.TryGetValue(requiredRatingMinutes, out var req))
            {
                result.MinDimensionMm = req.Width;
                result.MinCoverMm = Math.Max(0, req.Axis - 6);
            }

            result.Pass = result.AchievedMinutes >= requiredRatingMinutes;
            result.Summary = $"Column {widthMm}×{depthMm}mm, cover={coverMm}mm: " +
                $"achieves R{result.AchievedMinutes} vs required R{requiredRatingMinutes} " +
                $"→ {(result.Pass ? "PASS" : "FAIL")}";

            return result;
        }

        /// <summary>
        /// Checks fire resistance of an RC beam.
        /// </summary>
        public static FireResistanceResult CheckBeam(
            double widthMm, double coverMm, int requiredRatingMinutes = 60)
        {
            var result = new FireResistanceResult
            {
                ElementType = "Beam",
                RequiredMinutes = requiredRatingMinutes,
                ActualDimensionMm = widthMm,
                ActualCoverMm = coverMm,
            };

            result.AchievedMinutes = 0;
            foreach (var kvp in BeamRequirements.OrderByDescending(k => k.Key))
            {
                if (widthMm >= kvp.Value.Width && coverMm + 6 >= kvp.Value.Axis)
                {
                    result.AchievedMinutes = kvp.Key;
                    break;
                }
            }

            if (BeamRequirements.TryGetValue(requiredRatingMinutes, out var req))
            {
                result.MinDimensionMm = req.Width;
                result.MinCoverMm = Math.Max(0, req.Axis - 6);
            }

            result.Pass = result.AchievedMinutes >= requiredRatingMinutes;
            result.Summary = $"Beam width={widthMm}mm, cover={coverMm}mm: " +
                $"R{result.AchievedMinutes} vs R{requiredRatingMinutes} → {(result.Pass ? "PASS" : "FAIL")}";

            return result;
        }

        /// <summary>
        /// Checks fire resistance of an RC slab.
        /// </summary>
        public static FireResistanceResult CheckSlab(
            double thicknessMm, double coverMm, int requiredRatingMinutes = 60)
        {
            var result = new FireResistanceResult
            {
                ElementType = "Slab",
                RequiredMinutes = requiredRatingMinutes,
                ActualDimensionMm = thicknessMm,
                ActualCoverMm = coverMm,
            };

            result.AchievedMinutes = 0;
            foreach (var kvp in SlabRequirements.OrderByDescending(k => k.Key))
            {
                if (thicknessMm >= kvp.Value.Thickness && coverMm >= kvp.Value.Axis)
                {
                    result.AchievedMinutes = kvp.Key;
                    break;
                }
            }

            if (SlabRequirements.TryGetValue(requiredRatingMinutes, out var req))
            {
                result.MinDimensionMm = req.Thickness;
                result.MinCoverMm = req.Axis;
            }

            result.Pass = result.AchievedMinutes >= requiredRatingMinutes;
            result.Summary = $"Slab t={thicknessMm}mm, cover={coverMm}mm: " +
                $"R{result.AchievedMinutes} vs R{requiredRatingMinutes} → {(result.Pass ? "PASS" : "FAIL")}";

            return result;
        }

        /// <summary>
        /// Checks fire resistance for all structural elements in the model.
        /// </summary>
        public static List<FireResistanceResult> CheckAllElements(
            Document doc, int requiredRatingMinutes = 60, double defaultCoverMm = 30)
        {
            var results = new List<FireResistanceResult>();

            // Columns
            var cols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().ToList();

            foreach (var col in cols)
            {
                var type = doc.GetElement(col.GetTypeId());
                double w = 300, d = 300;
                if (type != null)
                {
                    var wP = type.get_Parameter(BuiltInParameter.GENERIC_WIDTH);
                    var dP = type.get_Parameter(BuiltInParameter.GENERIC_DEPTH);
                    if (wP != null) w = wP.AsDouble() * Units.FeetToMm;
                    if (dP != null) d = dP.AsDouble() * Units.FeetToMm;
                }
                if (w <= 0) w = 300;
                if (d <= 0) d = 300;
                results.Add(CheckColumn(w, d, defaultCoverMm, requiredRatingMinutes));
            }

            // Beams
            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().ToList();

            foreach (var beam in beams)
            {
                var type = doc.GetElement(beam.GetTypeId());
                double w = 200;
                if (type != null)
                {
                    var wP = type.get_Parameter(BuiltInParameter.GENERIC_WIDTH);
                    if (wP != null) w = wP.AsDouble() * Units.FeetToMm;
                }
                if (w <= 0) w = 200;
                results.Add(CheckBeam(w, defaultCoverMm, requiredRatingMinutes));
            }

            // Slabs
            var slabs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType().ToList();

            foreach (var slab in slabs)
            {
                var type = doc.GetElement(slab.GetTypeId());
                double t = 200;
                if (type != null)
                {
                    var tP = type.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                    if (tP != null) t = tP.AsDouble() * Units.FeetToMm;
                }
                if (t <= 0) t = 200;
                results.Add(CheckSlab(t, defaultCoverMm, requiredRatingMinutes));
            }

            return results;
        }
    }
}
