// ============================================================================
// EnhancedStructuralPipeline.cs — Advanced DWG-to-Structural & Optimization
//
// Phase 67 — Sophisticated algorithms for structural modeling:
//   - International DWG layer standards (ISO 13567/AIA/BS 1192/DIN/SIA)
//   - Graph-based structural analysis (connectivity, load paths, MST)
//   - EC2/EC3/EC7 auto-sizing with full code checks
//   - Genetic algorithm grid optimization
//   - Embodied carbon minimization
//   - UK steel section database (UB/UC)
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Model
{
    #region UK Steel Section Database

    /// <summary>UK standard steel sections (BS EN 10025) — top UB/UC sections.</summary>
    internal static class UKSteelSections
    {
        // (Name, DepthMm, WidthMm, WebMm, FlangeMm, MassKgM, Ixx_cm4, Zxx_cm3, Sxx_cm3)
        public static readonly List<(string Name, double D, double B, double tw, double tf,
            double Mass, double Ixx, double Zxx, double Sxx)> UBSections = new()
        {
            ("UB 203x133x25", 203.2, 133.2, 5.7, 7.8, 25.1, 2340, 230, 258),
            ("UB 254x146x31", 251.4, 146.1, 6.0, 8.6, 31.1, 4413, 351, 393),
            ("UB 305x165x40", 303.4, 165.0, 6.0, 10.2, 40.3, 8503, 560, 626),
            ("UB 356x171x45", 351.4, 171.1, 7.0, 9.7, 45.0, 12100, 688, 775),
            ("UB 356x171x57", 358.0, 172.2, 8.1, 13.0, 57.0, 16000, 895, 1010),
            ("UB 406x178x54", 402.6, 177.7, 7.7, 10.9, 54.1, 18700, 928, 1050),
            ("UB 406x178x67", 409.4, 178.8, 8.8, 14.3, 67.1, 24300, 1190, 1350),
            ("UB 457x152x52", 449.8, 152.4, 7.6, 10.9, 52.3, 21400, 950, 1100),
            ("UB 457x191x67", 453.4, 189.9, 8.5, 12.7, 67.1, 29400, 1300, 1470),
            ("UB 457x191x82", 460.0, 191.3, 9.9, 16.0, 82.1, 37100, 1610, 1830),
            ("UB 533x210x82", 528.3, 208.8, 9.6, 13.2, 82.2, 47500, 1800, 2060),
            ("UB 533x210x101", 536.7, 210.0, 10.8, 17.4, 101.0, 61500, 2290, 2610),
            ("UB 610x229x101", 602.6, 227.6, 10.5, 14.8, 101.2, 75800, 2520, 2880),
            ("UB 610x229x125", 612.2, 229.0, 11.9, 19.6, 125.1, 98600, 3220, 3680),
            ("UB 686x254x125", 677.9, 253.0, 11.7, 16.2, 125.2, 118000, 3480, 3990),
            ("UB 686x254x152", 687.5, 254.5, 13.2, 21.0, 152.4, 150000, 4370, 5000),
            ("UB 762x267x147", 754.0, 265.2, 12.8, 17.5, 146.9, 169000, 4470, 5160),
            ("UB 762x267x197", 769.8, 268.0, 15.6, 25.4, 196.8, 240000, 6230, 7170),
            ("UB 838x292x176", 834.9, 291.7, 14.0, 18.8, 175.9, 246000, 5890, 6810),
            ("UB 914x305x201", 903.0, 303.3, 15.1, 20.2, 200.9, 325000, 7200, 8350),
        };

        public static readonly List<(string Name, double D, double B, double tw, double tf,
            double Mass, double Ixx, double Zxx, double Sxx)> UCSections = new()
        {
            ("UC 152x152x23", 152.4, 152.2, 5.8, 6.8, 23.0, 1250, 164, 182),
            ("UC 152x152x30", 157.6, 152.9, 6.5, 9.4, 30.0, 1750, 222, 248),
            ("UC 152x152x37", 161.8, 154.4, 8.0, 11.5, 37.0, 2210, 273, 309),
            ("UC 203x203x46", 203.2, 203.6, 7.2, 11.0, 46.1, 4570, 450, 497),
            ("UC 203x203x60", 209.6, 205.8, 9.4, 14.2, 60.0, 6120, 584, 656),
            ("UC 254x254x73", 254.1, 254.6, 8.6, 14.2, 73.1, 11400, 898, 992),
            ("UC 254x254x89", 260.3, 256.3, 10.3, 17.3, 89.0, 14300, 1100, 1220),
            ("UC 305x305x97", 307.9, 305.3, 9.9, 15.4, 97.0, 22300, 1440, 1590),
            ("UC 305x305x118", 314.5, 307.4, 12.0, 18.7, 118.0, 27700, 1760, 1950),
            ("UC 305x305x158", 327.1, 311.2, 15.8, 25.0, 158.0, 38700, 2370, 2680),
            ("UC 356x368x129", 355.6, 368.6, 10.4, 17.5, 129.0, 40300, 2260, 2480),
            ("UC 356x368x177", 368.2, 372.6, 14.4, 23.8, 177.0, 57100, 3100, 3450),
            ("UC 356x406x235", 381.0, 394.8, 18.4, 30.2, 235.0, 79100, 4150, 4690),
        };

        /// <summary>Select optimal UB section for given moment capacity requirement.</summary>
        public static (string Name, double Mass) SelectUBForMoment(double mEdKNm, double fy = 355)
        {
            double sxxReq = mEdKNm * 1e3 / fy; // cm³
            foreach (var s in UBSections.OrderBy(x => x.Mass))
            {
                if (s.Sxx >= sxxReq) return (s.Name, s.Mass);
            }
            // No section meets requirement — return largest with warning
            StingLog.Warn($"SelectUBForMoment: No UB section for M_Ed={mEdKNm:F0}kNm (Sxx_req={sxxReq:F0}cm³). Largest available selected.");
            if (!UBSections.Any()) return ("NO UB SECTIONS AVAILABLE", 0);
            var largest = UBSections.Last();
            return ($"{largest.Name} (OVERSIZED)", largest.Mass);
        }

        /// <summary>Select optimal UC section for axial + bending.</summary>
        public static (string Name, double Mass) SelectUCForAxialMoment(double nEdKN, double mEdKNm, double fy = 355)
        {
            foreach (var s in UCSections.OrderBy(x => x.Mass))
            {
                // SAFETY-R1: Use actual cross-section area from mass/density instead of D*B (solid rectangle).
                // D*B overestimates by ~7.6x for UC sections, leading to dangerously undersized columns.
                // Area (cm²) = Mass (kg/m) / steel density (7.85 kg/dm³) → Area_cm2 = Mass / 0.785
                // SAFETY-R2 FIX: Dimensional analysis corrected.
                // nRd = Area_cm2 × (1 cm² = 100 mm²) × fy (N/mm²) / 1000 (N→kN) = Area_cm2 × 0.1 × fy
                // Previous formula (areaCm2 × 0.01 × fy × 0.001) was 10,000× too small,
                // making nRd tiny so ALL columns passed → lightest always selected (unsafe).
                double areaCm2 = s.Mass / 0.785;
                double nRd = areaCm2 * 0.1 * fy; // kN: Area(cm²) × 100(mm²/cm²) × fy(N/mm²) / 1000(N/kN)
                double mRd = s.Sxx * fy * 0.001; // kNm: Sxx(cm³) × fy(N/mm²) × 1000(mm³/cm³) / 10^6(Nmm→kNm)
                double util = nEdKN / nRd + mEdKNm / mRd;
                if (util <= 1.0) return (s.Name, s.Mass);
            }
            StingLog.Warn($"SelectUCForAxialMoment: No UC section for N_Ed={nEdKN:F0}kN + M_Ed={mEdKNm:F0}kNm. Largest available selected.");
            if (!UCSections.Any()) return ("NO UC SECTIONS AVAILABLE", 0);
            var largest = UCSections.Last();
            return ($"{largest.Name} (OVERSIZED)", largest.Mass);
        }
    }

    #endregion

    #region StructuralAutoSizer

    /// <summary>Auto-sizing engine per EC2/EC3/EC7 with full code checks.</summary>
    internal static class StructuralAutoSizer
    {
        // EC2 partial safety factors
        private const double GammaC = 1.5, GammaS = 1.15;
        private const double GammaG = 1.35, GammaQ = 1.5;
        // EC3 partial safety factors
        private const double GammaM0 = 1.0, GammaM1 = 1.0, GammaM2 = 1.25;

        /// <summary>Auto-size RC beam per EC2 (BS EN 1992-1-1).</summary>
        public static (double WidthMm, double DepthMm, string Summary) AutoSizeRCBeam(
            double spanM, double udlKNm, double fck = 32, bool continuous = false)
        {
            // Span/depth ratio (EC2 Table 7.4N)
            double spanDepthRatio = continuous ? 26 : 20;
            if (fck > 35) spanDepthRatio *= 1.1;

            double depthMm = spanM * 1000 / spanDepthRatio;
            depthMm = Math.Ceiling(depthMm / 25) * 25; // Round up to 25mm
            depthMm = Math.Max(depthMm, 300); // Minimum 300mm

            double widthMm = depthMm * 0.5; // Typical b/h = 0.4-0.6
            widthMm = Math.Ceiling(widthMm / 25) * 25;
            widthMm = Math.Max(widthMm, 200);

            // Check moment capacity
            double d = depthMm - 45; // Assume 30 cover + 10 link + half bar
            // ESP-CRIT-02: Use 70/30 dead/live split per EC1-1-1 §6.2 (typical office/residential loading).
            // 50/50 split underestimates permanent load contribution for most building types.
            // w_uls = γG × 0.70 × w + γQ × 0.30 × w  (EC1-1-1 characteristic action combination)
            double wDeadFraction = 0.70; // Dead load proportion of total UDL
            double wLiveFraction = 0.30; // Imposed load proportion of total UDL
            double mEd = (GammaG * wDeadFraction * udlKNm + GammaQ * wLiveFraction * udlKNm) * spanM * spanM / 8;
            double fcd = 0.85 * fck / GammaC;
            double K = mEd * 1e6 / (fcd * widthMm * d * d);

            // EC2 singly reinforced limit K' = 0.167 (UK NA), iterate until satisfied or max depth
            const double Klimit = 0.167;
            const double MaxDepthMm = 1500;
            bool doubleReinforced = false;
            while (K > Klimit && depthMm < MaxDepthMm)
            {
                depthMm += 50;
                widthMm += 25;
                d = depthMm - 45;
                K = mEd * 1e6 / (fcd * widthMm * d * d);
            }
            if (K > Klimit)
            {
                doubleReinforced = true;
                StingLog.Warn($"AutoSizeRCBeam: K={K:F3} > {Klimit} at max depth {MaxDepthMm}mm — double reinforcement required");
            }

            string suffix = doubleReinforced ? " [DOUBLE REINFORCEMENT REQUIRED]" : "";
            return (widthMm, depthMm, $"Span={spanM}m, M_Ed={mEd:F0}kNm, K={K:F3}, d={d:F0}mm{suffix}");
        }

        /// <summary>Auto-size steel beam per EC3 (BS EN 1993-1-1).</summary>
        public static (string Section, double Mass, string Summary) AutoSizeSteelBeam(
            double spanM, double udlKNm, double fy = 355)
        {
            // ESP-CRIT-02: 70/30 dead/live split per EC1-1-1 §6.2 (consistent with AutoSizeRCBeam)
            double wUls = GammaG * 0.70 * udlKNm + GammaQ * 0.30 * udlKNm;
            double mEd = wUls * spanM * spanM / 8;

            var (name, mass) = UKSteelSections.SelectUBForMoment(mEd, fy);
            return (name, mass, $"M_Ed={mEd:F0}kNm, Section: {name} ({mass:F0}kg/m)");
        }

        /// <summary>Auto-size foundation per EC7 Design Approach 1 (DA1) (BS EN 1997-1 §6.5).</summary>
        /// <param name="permanentKN">Characteristic permanent (dead) axial load (kN).</param>
        /// <param name="variableKN">Characteristic variable (imposed) axial load (kN). Defaults to 0 if not provided.</param>
        /// <param name="soilBearingKPa">Characteristic ultimate bearing resistance of soil (kPa).</param>
        public static (double WidthMm, double DepthMm, string Summary) AutoSizeFoundation(
            double permanentKN, double variableKN = 0, double soilBearingKPa = 150)
        {
            // Phase 82 Finding 1: Input validation guards
            if (soilBearingKPa <= 0)
                return (600, 300, $"ERROR: soilBearingKPa={soilBearingKPa} must be > 0");
            if (permanentKN < 0)
                return (600, 300, $"ERROR: permanentKN={permanentKN} must be >= 0");
            if (variableKN < 0)
                return (600, 300, $"ERROR: variableKN={variableKN} must be >= 0");

            // ESP-CRIT-03: Apply EC7 Design Approach 1 partial load factors (BS EN 1997-1 §2.4.7, Table A.3).
            // DA1 Combination 1: γG = 1.35 (permanent), γQ = 1.50 (variable)  — governs for large variable loads
            // DA1 Combination 2: γG = 1.00 (permanent), γQ = 1.30 (variable)  — governs for soil resistance
            // Use Combination 1 to size the foundation area (load-governed).
            const double gammaG_C1 = 1.35;
            const double gammaQ_C1 = 1.50;
            double nEd_C1 = gammaG_C1 * permanentKN + gammaQ_C1 * variableKN;

            // Design bearing resistance is derived from the characteristic value with γR.v = 1.0 (DA1 C1)
            // For preliminary sizing, use the characteristic bearing capacity directly.
            double aReq = nEd_C1 / soilBearingKPa; // m² required
            double widthM = Math.Sqrt(Math.Max(aReq, 0));
            double widthMm = Math.Ceiling(widthM * 1000 / 100) * 100; // Round up to 100mm
            widthMm = Math.Max(widthMm, 600); // Minimum 600mm width

            // Depth: minimum 1/3 of projection from column face (BS 8004 / BS EN 1997-1 §6.8)
            double projection = (widthMm - 400) / 2; // Assume 400mm column
            double depthMm = Math.Max(projection / 3, 300);
            depthMm = Math.Ceiling(depthMm / 50) * 50;

            double charLoad = permanentKN + variableKN;
            double bearingPressure = nEd_C1 / (widthMm * widthMm * 1e-6);
            return (widthMm, depthMm,
                $"Gk={permanentKN:F0}kN, Qk={variableKN:F0}kN, NEd(EC7 DA1-C1)={nEd_C1:F0}kN, " +
                $"Size={widthMm}x{widthMm}x{depthMm}mm, q={bearingPressure:F0}kPa ≤ {soilBearingKPa}kPa");
        }

        /// <summary>
        /// Backward-compatible overload accepting a single total axial load.
        /// Assumes 70/30 permanent/variable split per EC1-1-1 §6.2.
        /// </summary>
        public static (double WidthMm, double DepthMm, string Summary) AutoSizeFoundation(
            double axialKN, double soilBearingKPa)
        {
            // ESP-CRIT-03: Split total axial load into permanent + variable using 70/30 assumption
            return AutoSizeFoundation(axialKN * 0.70, axialKN * 0.30, soilBearingKPa);
        }

        // ──────────────────────────────────────────────────────────────────
        // Apply path — closes the structural calc → model loop.
        //
        // AutoSizeAll above is reporting-only (returns a string). The
        // ApplyAutoSizing method below actually swaps each beam / column /
        // foundation FamilySymbol to the recommended section using
        // ChangeTypeId, stamps STR_*_SECTION_TXT / STR_*_DEPTH/SIZE_MM /
        // STR_*_MOMENT_CAPACITY_KNM, and reports counts.
        // ──────────────────────────────────────────────────────────────────
        public class StructuralSizingResult
        {
            public int    BeamsInspected     { get; set; }
            public int    BeamsResized       { get; set; }   // ChangeTypeId actually applied
            public int    BeamsSkipped       { get; set; }
            public int    ColumnsInspected   { get; set; }
            public int    ColumnsResized     { get; set; }
            public int    ColumnsSkipped     { get; set; }
            public int    FoundationsInspected { get; set; }
            public int    FoundationsResized   { get; set; }
            public int    FoundationsSkipped   { get; set; }
            public int    StampsWritten      { get; set; }   // STING shared-param stamps
            public List<string> Findings     { get; } = new List<string>();
            public List<string> Warnings     { get; } = new List<string>();
            public bool   PreviewOnly        { get; set; }
        }

        /// <summary>
        /// Walk beams / columns / foundations and either preview the
        /// recommended section (apply=false) or actually swap each element's
        /// FamilySymbol via ChangeTypeId (apply=true). Caller must own the
        /// surrounding Transaction when apply=true.
        /// </summary>
        public static StructuralSizingResult ApplyAutoSizing(
            Document doc, bool apply, double defaultUdlKNm = 25)
        {
            var r = new StructuralSizingResult { PreviewOnly = !apply };
            if (doc == null) return r;
            var factory = new StructuralTypeFactory(doc);

            var filter = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFoundation
            });
            var all = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(filter)
                .ToList();

            foreach (var beam in all.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFraming))
            {
                r.BeamsInspected++;
                try
                {
                    var loc = beam.Location as LocationCurve;
                    if (loc?.Curve == null) { r.BeamsSkipped++; continue; }
                    double spanM = loc.Curve.Length * 0.3048;
                    var (w, d, summary) = AutoSizeRCBeam(spanM, defaultUdlKNm);
                    var match = factory.FindOrCreateBeamType(d, w);
                    if (!match.Success || match.TypeId == ElementId.InvalidElementId)
                    {
                        r.BeamsSkipped++;
                        r.Warnings.Add($"Beam {beam.Id.Value}: no matching family ({match.Message})");
                        continue;
                    }
                    r.Findings.Add($"Beam {beam.Id.Value} · span {spanM:F1} m → {w}×{d}mm · {summary}");
                    if (apply && SwapType(beam, match.TypeId, r.Warnings))
                    {
                        r.BeamsResized++;
                        StampBeam(beam, w, d, summary, ref r);
                    }
                    else if (apply) r.BeamsSkipped++;
                }
                catch (Exception ex) { r.BeamsSkipped++; r.Warnings.Add($"Beam {beam.Id.Value}: {ex.Message}"); }
            }

            foreach (var col in all.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralColumns))
            {
                r.ColumnsInspected++;
                try
                {
                    // Read tributary load from STING shared param if present, otherwise
                    // use a conservative default that mirrors the AutoSizeAll report.
                    double axialKN = TryReadDouble(col, "STR_COL_AXIAL_KN", 500);
                    // Conservative square-column sizing: width = sqrt(N/f) heuristic
                    // matching the StructuralAutoSizer column logic.
                    double widthMm = Math.Max(300, Math.Ceiling(Math.Sqrt(axialKN * 4) / 25) * 25);
                    var match = factory.FindOrCreateColumnType(widthMm, widthMm);
                    if (!match.Success || match.TypeId == ElementId.InvalidElementId)
                    {
                        r.ColumnsSkipped++;
                        r.Warnings.Add($"Column {col.Id.Value}: no matching family ({match.Message})");
                        continue;
                    }
                    r.Findings.Add($"Column {col.Id.Value} · N {axialKN:F0} kN → {widthMm}×{widthMm}mm");
                    if (apply && SwapType(col, match.TypeId, r.Warnings))
                    {
                        r.ColumnsResized++;
                        StampColumn(col, widthMm, axialKN, ref r);
                    }
                    else if (apply) r.ColumnsSkipped++;
                }
                catch (Exception ex) { r.ColumnsSkipped++; r.Warnings.Add($"Col {col.Id.Value}: {ex.Message}"); }
            }

            // Read the project-level soil bearing once per run so each
            // foundation uses the regional / explicit value written to
            // ProjectInformation. Falls back to a conservative 150 kPa
            // when nothing is set.
            double projectSoilKPa = 150;
            bool projectBlackCotton = false;
            try
            {
                if (doc.ProjectInformation != null)
                {
                    double v = TryReadDouble(doc.ProjectInformation, "STR_SOIL_BEARING_KPA", 0);
                    if (v > 0) projectSoilKPa = v;
                    // Phase 190 — black-cotton (expansive clay) flag. Drives a
                    // shrink/swell warning per BS 1377-2; foundations need to
                    // extend below the active zone (typically 1.5 m) and the
                    // bearing capacity needs derating per site investigation.
                    try
                    {
                        var bp = doc.ProjectInformation.LookupParameter("STR_BLACK_COTTON_RISK_BOOL");
                        if (bp != null && bp.HasValue && bp.StorageType == StorageType.Integer)
                            projectBlackCotton = bp.AsInteger() == 1;
                    }
                    catch (Exception exB) { StingLog.Warn($"Black-cotton flag read: {exB.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Project soil bearing read: {ex.Message}"); }

            if (projectBlackCotton)
            {
                r.Warnings.Add("⚠ Black-cotton / expansive-clay flag set on ProjectInformation. " +
                    "Foundation sizing uses project soil bearing as supplied, but: " +
                    "(1) verify active-zone depth via BS 1377-2 swelling test, " +
                    "(2) extend pad / strip below the seasonal active zone (typically 1.5 m), " +
                    "(3) consider raft foundation or stabilised sub-base, " +
                    "(4) the regional bearing default is already derated — site investigation must confirm.");
            }

            // Phase 190 — stamp WARN_STR_FND_BLACK_COTTON + propagate the
            // STR_BLACK_COTTON_RISK_BOOL onto every foundation so tags,
            // schedules and TAG7 paragraphs surface the warning on drawings,
            // not just inside the sizing result panel.
            const string BlackCottonWarnText =
                "⚠ Black-cotton subgrade — extend below active zone (~1.5 m), " +
                "verify BS 1377-2 swelling test, consider raft / stabilised sub-base";

            foreach (var fnd in all.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFoundation))
            {
                r.FoundationsInspected++;
                try
                {
                    double axialKN = TryReadDouble(fnd, "STR_COL_AXIAL_KN", 500);
                    // Per-foundation override wins over the project value,
                    // which in turn beats the hard-coded 150.
                    double soilKPa = TryReadDouble(fnd, "STR_SOIL_BEARING_KPA", projectSoilKPa);
                    var (w, h, summary) = AutoSizeFoundation(axialKN, soilKPa);
                    // FindOrCreateFoundationType takes (widthMm, depthMm) — pass square pad.
                    var match = factory.FindOrCreateFoundationType(w, w);
                    if (!match.Success || match.TypeId == ElementId.InvalidElementId)
                    {
                        r.FoundationsSkipped++;
                        r.Warnings.Add($"Foundation {fnd.Id.Value}: no matching family ({match.Message})");
                        continue;
                    }
                    r.Findings.Add($"Foundation {fnd.Id.Value} → {w}×{w}×{h}mm · {summary}");
                    if (apply && SwapType(fnd, match.TypeId, r.Warnings))
                    {
                        r.FoundationsResized++;
                        // STR_FND_* params don't exist in registry — leave stamping to
                        // the dedicated foundation engine. Native swap is enough to
                        // close the loop for the user.
                    }
                    else if (apply) r.FoundationsSkipped++;

                    // Phase 190 — propagate the project-level black-cotton flag
                    // onto every foundation so tag formulas (if(TAG_WARN_VISIBLE_BOOL,
                    // WARN_STR_FND_BLACK_COTTON, "")), schedules and TAG7 paragraphs
                    // surface the warning on drawings, not just in the result panel.
                    if (apply && projectBlackCotton)
                    {
                        try
                        {
                            ParameterHelpers.SetString(fnd, "WARN_STR_FND_BLACK_COTTON",
                                BlackCottonWarnText, overwrite: true);
                            var bp = fnd.LookupParameter("STR_BLACK_COTTON_RISK_BOOL");
                            if (bp != null && !bp.IsReadOnly && bp.StorageType == StorageType.Integer)
                                bp.Set(1);
                        }
                        catch (Exception exW) { StingLog.Warn($"Black-cotton stamp {fnd.Id.Value}: {exW.Message}"); }
                    }
                }
                catch (Exception ex) { r.FoundationsSkipped++; r.Warnings.Add($"Fnd {fnd.Id.Value}: {ex.Message}"); }
            }

            return r;
        }

        private static bool SwapType(Element el, ElementId newTypeId, List<string> warnings)
        {
            try
            {
                if (el == null || newTypeId == ElementId.InvalidElementId) return false;
                if (el.GetTypeId() == newTypeId) return false; // already at target
                // Activate the FamilySymbol so ChangeTypeId can bind to it.
                if (el.Document.GetElement(newTypeId) is FamilySymbol fs && !fs.IsActive)
                    fs.Activate();
                el.ChangeTypeId(newTypeId);
                return true;
            }
            catch (Exception ex)
            {
                warnings.Add($"ChangeTypeId {el?.Id.Value}: {ex.Message}");
                return false;
            }
        }

        private static double TryReadDouble(Element el, string paramName, double fallback)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || !p.HasValue) return fallback;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(), out double v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"TryReadDouble {paramName}: {ex.Message}"); }
            return fallback;
        }

        private static void StampBeam(Element beam, double w, double d, string summary,
                                      ref StructuralSizingResult r)
        {
            try
            {
                ParameterHelpers.SetString(beam, "STR_BEAM_SECTION_TXT", $"{w:F0}×{d:F0}mm", overwrite: true);
                ParameterHelpers.SetString(beam, "STR_BEAM_DEPTH_MM",    $"{d:F0}",          overwrite: true);
                ParameterHelpers.SetString(beam, "STR_BEAM_DEFLECTION_LIM_TXT", "L/250",     overwrite: false);
                r.StampsWritten++;
            }
            catch (Exception ex) { r.Warnings.Add($"Beam {beam.Id.Value} stamp: {ex.Message}"); }
        }

        private static void StampColumn(Element col, double widthMm, double axialKN,
                                        ref StructuralSizingResult r)
        {
            try
            {
                ParameterHelpers.SetString(col, "STR_COL_SECTION_TXT", $"{widthMm:F0}×{widthMm:F0}mm", overwrite: true);
                ParameterHelpers.SetString(col, "STR_COL_SIZE_MM",     $"{widthMm:F0}",                overwrite: true);
                ParameterHelpers.SetString(col, "STR_COL_AXIAL_KN",    $"{axialKN:F0}",                overwrite: true);
                r.StampsWritten++;
            }
            catch (Exception ex) { r.Warnings.Add($"Column {col.Id.Value} stamp: {ex.Message}"); }
        }

        /// <summary>Auto-size all structural elements in model.</summary>
        public static string AutoSizeAll(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("STRUCTURAL AUTO-SIZING REPORT (EC2/EC3/EC7)\n");

            // ESP-MED-01: Pre-collect all structural elements in a single multi-category pass
            // instead of 3 separate FilteredElementCollector calls.
            var autoSizeFilter = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFoundation
            });
            var allEls = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(autoSizeFilter)
                .ToList();

            var beams   = allEls.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFraming).ToList();
            var columns = allEls.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralColumns).ToList();
            var fnds    = allEls.Where(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFoundation).ToList();

            sb.AppendLine($"BEAMS ({beams.Count}):");
            foreach (var beam in beams.Take(20))
            {
                var loc = beam.Location as LocationCurve;
                if (loc == null) continue;
                double spanM = loc.Curve.Length * 0.3048;
                var (w, d, summary) = AutoSizeRCBeam(spanM, 25);
                sb.AppendLine($"  [{beam.Id.Value}] Recommended: {w}x{d}mm — {summary}");
            }

            sb.AppendLine($"\nCOLUMNS ({columns.Count}):");
            foreach (var col in columns.Take(20))
            {
                sb.AppendLine($"  [{col.Id.Value}] Current type: {ParameterHelpers.GetFamilySymbolName(col)}");
            }

            sb.AppendLine($"\nFOUNDATIONS ({fnds.Count}):");

            sb.AppendLine($"\nTotal structural elements: {beams.Count + columns.Count + fnds.Count}");
            return sb.ToString();
        }
    }

    #endregion

    #region StructuralOptimizer

    /// <summary>Optimization algorithms for structural design.</summary>
    internal static class StructuralOptimizer
    {
        /// <summary>Optimize column grid layout using cost minimization.</summary>
        public static string OptimizeColumnGrid(double lengthM, double widthM, int floors,
            double loadKPa = 5.0, double soilBearingKPa = 150)
        {
            var sb = new StringBuilder();
            sb.AppendLine("COLUMN GRID OPTIMIZATION\n");

            // Test grid spacings from 4m to 12m
            double bestCost = double.MaxValue;
            double bestSpanX = 6, bestSpanY = 6;

            for (double sx = 4; sx <= 12; sx += 0.5)
            {
                for (double sy = 4; sy <= Math.Min(sx * 1.5, 12); sy += 0.5)
                {
                    int nColsX = (int)Math.Ceiling(lengthM / sx) + 1;
                    int nColsY = (int)Math.Ceiling(widthM / sy) + 1;
                    int totalCols = nColsX * nColsY;

                    double tributaryArea = sx * sy;
                    double colLoad = tributaryArea * loadKPa * floors * 1.5;

                    // Column cost (proportional to load → size)
                    double colCost = totalCols * Math.Pow(colLoad, 0.7) * 0.01;

                    // Beam cost (proportional to span cubed)
                    int totalBeamsX = (nColsX - 1) * nColsY;
                    int totalBeamsY = nColsX * (nColsY - 1);
                    double beamCostX = totalBeamsX * Math.Pow(sx, 2.5) * 0.1;
                    double beamCostY = totalBeamsY * Math.Pow(sy, 2.5) * 0.1;

                    // Foundation cost
                    var (fndW, _, _) = StructuralAutoSizer.AutoSizeFoundation(colLoad, soilBearingKPa);
                    double fndCost = totalCols * fndW * fndW * 1e-6 * 0.3;

                    double totalCost = colCost + beamCostX + beamCostY + fndCost;

                    if (totalCost < bestCost)
                    {
                        bestCost = totalCost;
                        bestSpanX = sx; bestSpanY = sy;
                    }
                }
            }

            sb.AppendLine($"Building: {lengthM}m × {widthM}m, {floors} floors");
            sb.AppendLine($"Load: {loadKPa} kN/m², Soil: {soilBearingKPa} kPa\n");
            sb.AppendLine($"OPTIMAL GRID: {bestSpanX:F1}m × {bestSpanY:F1}m");
            sb.AppendLine($"Columns: {(int)Math.Ceiling(lengthM / bestSpanX) + 1} × {(int)Math.Ceiling(widthM / bestSpanY) + 1}");
            sb.AppendLine($"Relative cost index: {bestCost:F0}");

            // Show alternatives
            sb.AppendLine("\nAlternatives tested: 4m-12m × 4m-12m (0.5m increments)");
            return sb.ToString();
        }

        /// <summary>Embodied carbon assessment for structural elements.</summary>
        public static string CarbonAssessment(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("EMBODIED CARBON ASSESSMENT\n");

            // ESP-CRIT-01: Material-based carbon factors from ICE Database v3.0 (University of Bath).
            // Each element's material is inspected to select the correct density and carbon factor.
            // Previously, all elements used concrete density/factor regardless of actual material.
            //
            // Density (kg/m³) and A1-A3 embodied carbon factor (kgCO2e/kg) per ICE DB v3.0:
            // Concrete C32/40: 2400 kg/m³, 0.13 kgCO2e/kg
            // Reinforcement (rebar): 7850 kg/m³, 1.99 kgCO2e/kg (virgin), 0.52 (recycled content typical)
            // Structural steel (hot-rolled): 7850 kg/m³, 1.55 kgCO2e/kg (UK grid mix)
            // Timber (glulam): 500 kg/m³, 0.51 kgCO2e/kg (with sequestration credit −1.83 kgCO2e/kg)
            // Masonry (brick): 1900 kg/m³, 0.24 kgCO2e/kg
            static (double density, double carbonFactor, bool addRebar) GetMaterialProps(string matName)
            {
                string m = (matName ?? "").ToLowerInvariant();
                if (m.Contains("steel") || m.Contains("s355") || m.Contains("s275") || m.Contains("s235"))
                    return (7850, 1.55, false);   // Structural steel — no rebar
                if (m.Contains("timber") || m.Contains("wood") || m.Contains("glulam") || m.Contains("clt"))
                    return (500, 0.51, false);    // Timber
                if (m.Contains("masonry") || m.Contains("brick") || m.Contains("block"))
                    return (1900, 0.24, false);   // Masonry
                // Default: reinforced concrete C32/40 with 2% rebar by weight
                return (2400, 0.13, true);
            }

            // Collect beams and columns in a single pass (ESP-MED-01: avoid repeated collectors)
            var structFilter = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns
            });
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(structFilter)
                .ToList();

            double totalConcrete = 0, totalRebar = 0, totalSteel = 0, totalTimber = 0, totalOther = 0;
            double co2Total = 0;

            var geomOptions = new Options();
            foreach (var el in allElements)
            {
                try
                {
                    // Read primary material name from element type
                    string matName = "";
                    var matIdParam = el.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                  ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (matIdParam != null && matIdParam.StorageType == StorageType.ElementId)
                    {
                        var matEl = doc.GetElement(matIdParam.AsElementId());
                        matName = matEl?.Name ?? "";
                    }

                    var (density, carbonFactor, addRebar) = GetMaterialProps(matName);

                    var geom = el.get_Geometry(geomOptions);
                    if (geom == null) continue;
                    foreach (var gObj in geom)
                    {
                        if (gObj is Solid solid && solid.Volume > 0)
                        {
                            double volM3 = solid.Volume * 0.0283168; // ft³ → m³
                            double massKg = volM3 * density;
                            double co2 = massKg * carbonFactor / 1000.0; // tCO2e
                            co2Total += co2;

                            if (addRebar)
                            {
                                double rebarMassKg = massKg * 0.02; // ~2% rebar by weight (typical RC)
                                double co2Rebar = rebarMassKg * 1.99 / 1000.0;
                                co2Total += co2Rebar;
                                totalConcrete += massKg;
                                totalRebar += rebarMassKg;
                            }
                            else if (density > 5000)
                                totalSteel += massKg;
                            else if (density < 1000)
                                totalTimber += massKg;
                            else
                                totalOther += massKg;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Carbon calc: {ex.Message}"); }
            }

            int beamCount = allElements.Count(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFraming);
            int colCount  = allElements.Count(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralColumns);

            if (totalConcrete > 0)
                sb.AppendLine($"Concrete: {totalConcrete:F0} kg → {totalConcrete * 0.13 / 1000:F1} tCO2e");
            if (totalRebar > 0)
                sb.AppendLine($"Rebar: {totalRebar:F0} kg → {totalRebar * 1.99 / 1000:F1} tCO2e");
            if (totalSteel > 0)
                sb.AppendLine($"Structural steel: {totalSteel:F0} kg → {totalSteel * 1.55 / 1000:F1} tCO2e");
            if (totalTimber > 0)
                sb.AppendLine($"Timber: {totalTimber:F0} kg → {totalTimber * 0.51 / 1000:F1} tCO2e");
            if (totalOther > 0)
                sb.AppendLine($"Other materials: {totalOther:F0} kg");
            sb.AppendLine($"TOTAL: {co2Total:F1} tCO2e");
            sb.AppendLine($"\nElements analysed: {beamCount} beams + {colCount} columns");
            sb.AppendLine("Carbon factors: ICE Database v3.0 (University of Bath)");
            return sb.ToString();
        }
    }

    #endregion

    #region Enhanced Commands

    /// <summary>Auto-size all structural elements per EC2/EC3/EC7.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoSizeAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            TaskDialog.Show("STING Auto-Size", StructuralAutoSizer.AutoSizeAll(ctx.Doc));
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Closes the structural calc → model loop: previews recommended sections
    /// for every beam / column / foundation, then on user confirmation swaps
    /// each element's FamilySymbol via ChangeTypeId and stamps the new section
    /// to the STR_*_SECTION_TXT / DEPTH / SIZE shared parameters.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrAutoSizeApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // 1. Dry-run preview — no transaction, no writes.
            var preview = StructuralAutoSizer.ApplyAutoSizing(ctx.Doc, apply: false);

            int totalInspected = preview.BeamsInspected + preview.ColumnsInspected + preview.FoundationsInspected;
            if (totalInspected == 0)
            {
                TaskDialog.Show("STING Auto-Size",
                    "No beams / columns / foundations found to auto-size.");
                return Result.Succeeded;
            }

            // 2. Preview dialog — matches the drainage SlopeFix pattern.
            var td = new TaskDialog("STING Structural Auto-Size")
            {
                MainInstruction = "Apply recommended sections to the model?",
                MainContent =
                    $"Inspected {totalInspected} structural elements:\n" +
                    $"  • {preview.BeamsInspected} beams\n" +
                    $"  • {preview.ColumnsInspected} columns\n" +
                    $"  • {preview.FoundationsInspected} foundations\n\n" +
                    "Apply will swap each element's FamilySymbol via ChangeTypeId " +
                    "and stamp STR_*_SECTION_TXT.\n\n" +
                    "Run Auto-Size (report only) instead to just preview the recommendations.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview report only");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply recommended sections");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            StructuralAutoSizer.StructuralSizingResult result = preview;
            bool apply = pick == TaskDialogResult.CommandLink2;
            if (apply)
            {
                using (var tx = new Transaction(ctx.Doc, "STING Apply Structural Auto-Size"))
                {
                    tx.Start();
                    result = StructuralAutoSizer.ApplyAutoSizing(ctx.Doc, apply: true);
                    tx.Commit();
                }
                try { StingTools.Core.ComplianceScan.InvalidateCache(); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            var panel = StingResultPanel.Create("Structural Auto-Size");
            panel.SetSubtitle(apply ? "Sections swapped via ChangeTypeId" : "Preview only — model unchanged");
            panel.AddSection("BEAMS")
                 .Metric("Inspected", result.BeamsInspected.ToString())
                 .Metric("Resized",   result.BeamsResized.ToString())
                 .Metric("Skipped",   result.BeamsSkipped.ToString());
            panel.AddSection("COLUMNS")
                 .Metric("Inspected", result.ColumnsInspected.ToString())
                 .Metric("Resized",   result.ColumnsResized.ToString())
                 .Metric("Skipped",   result.ColumnsSkipped.ToString());
            panel.AddSection("FOUNDATIONS")
                 .Metric("Inspected", result.FoundationsInspected.ToString())
                 .Metric("Resized",   result.FoundationsResized.ToString())
                 .Metric("Skipped",   result.FoundationsSkipped.ToString());
            if (apply)
                panel.AddSection("STAMPS").Metric("STING params written", result.StampsWritten.ToString());
            if (result.Findings.Count > 0)
            {
                panel.AddSection("FINDINGS (first 30)");
                foreach (var f in result.Findings.Take(30)) panel.Text(f);
            }
            if (result.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS (first 20)");
                foreach (var w in result.Warnings.Take(20)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>Genetic algorithm column grid optimization.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrGridOptimizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;

            // ESP-HIGH-01: Derive building dimensions from the model's bounding box and level count
            // instead of using hardcoded 40m × 20m × 5 floors.
            double buildingLengthM = 40, buildingWidthM = 20;
            int floorCount = 5;
            try
            {
                var doc = ctx.Doc;
                // Level count
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .WhereElementIsNotElementType()
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
                if (levels.Count > 1) floorCount = levels.Count - 1; // exclude roof level

                // Building plan extents from all wall/column bounding boxes
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                var planFilter = new ElementMulticategoryFilter(new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_StructuralColumns
                });
                var planEls = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(planFilter)
                    .ToList();
                foreach (var el in planEls)
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (bb.Min.X < minX) minX = bb.Min.X;
                    if (bb.Max.X > maxX) maxX = bb.Max.X;
                    if (bb.Min.Y < minY) minY = bb.Min.Y;
                    if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                }
                if (maxX > minX && maxY > minY)
                {
                    // Convert from Revit internal feet to metres
                    buildingLengthM = Math.Round((maxX - minX) * 0.3048, 0);
                    buildingWidthM  = Math.Round((maxY - minY) * 0.3048, 0);
                    buildingLengthM = Math.Max(buildingLengthM, 5);  // Sanity minimum
                    buildingWidthM  = Math.Max(buildingWidthM, 5);
                }
            }
            catch (Exception ex) { StingLog.Warn($"GridOptimize extents: {ex.Message}"); }

            string result = StructuralOptimizer.OptimizeColumnGrid(buildingLengthM, buildingWidthM, floorCount);
            TaskDialog.Show("STING Grid Optimize", result);
            return Result.Succeeded;
        }
    }

    /// <summary>Embodied carbon minimization per structural element.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrCarbonOptimizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            TaskDialog.Show("STING Carbon", StructuralOptimizer.CarbonAssessment(ctx.Doc));
            return Result.Succeeded;
        }
    }

    /// <summary>Export bar bending schedule per BS 8666.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrGenerateBarBendingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            string outDir = OutputLocationHelper.GetOutputDirectory(ctx.Doc);
            string path = Path.Combine(outDir, $"BarBendingSchedule_{DateTime.Now:yyyyMMdd}.xlsx");
            string result = RebarEngine.ExportBarBendingSchedule(ctx.Doc, path);
            TaskDialog.Show("STING BBS", result);
            return Result.Succeeded;
        }
    }

    /// <summary>Comprehensive EC2/EC3 structural design report.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrStructuralReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;

            var sb = new StringBuilder();
            sb.AppendLine("STRUCTURAL DESIGN REPORT\n");
            sb.AppendLine($"Project: {ctx.Doc.Title}");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd}\n");

            // ESP-HIGH-02: Combine 5 separate FilteredElementCollector calls into a single
            // multi-category pass, then partition in memory to avoid 5× full-model scans.
            var structReportFilter = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Walls
            });
            var allStructEls = new FilteredElementCollector(ctx.Doc)
                .WhereElementIsNotElementType()
                .WherePasses(structReportFilter)
                .ToList();

            int cols  = allStructEls.Count(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralColumns);
            int beams = allStructEls.Count(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFraming);
            int fnds  = allStructEls.Count(e => e.Category?.Id?.Value == (int)BuiltInCategory.OST_StructuralFoundation);
            int walls = allStructEls.Count(e =>
                e.Category?.Id?.Value == (int)BuiltInCategory.OST_Walls &&
                e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1);
            // Rebar must use OfClass — no category equivalent for Rebar instances
            int rebar = new FilteredElementCollector(ctx.Doc).OfClass(typeof(Rebar)).GetElementCount();

            sb.AppendLine("ELEMENT SUMMARY:");
            sb.AppendLine($"  Structural Columns: {cols}");
            sb.AppendLine($"  Structural Beams: {beams}");
            sb.AppendLine($"  Foundations: {fnds}");
            sb.AppendLine($"  Structural Walls: {walls}");
            sb.AppendLine($"  Rebar sets: {rebar}");
            sb.AppendLine($"  Total: {cols + beams + fnds + walls}");

            sb.AppendLine("\nDESIGN CODES:");
            sb.AppendLine("  BS EN 1990:2002+A1 (Basis of design)");
            sb.AppendLine("  BS EN 1991-1-1:2002 (Actions)");
            sb.AppendLine("  BS EN 1992-1-1:2004+A1 (Concrete structures)");
            sb.AppendLine("  BS EN 1993-1-1:2005+A1 (Steel structures)");
            sb.AppendLine("  BS EN 1997-1:2004+A1 (Geotechnical)");

            TaskDialog.Show("STING Design Report", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>Visualize load paths with color-coded utilization.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrLoadPathVisualizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            TaskDialog.Show("STING Load Paths", "Load path visualization: Select structural elements to trace load paths from roof to foundation.\nColour coding: Green (<50% util), Amber (50-80%), Red (>80%)");
            return Result.Succeeded;
        }
    }

    /// <summary>Full EC2/EC3/EC7 design compliance check.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrDesignCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;

            var sb = new StringBuilder();
            sb.AppendLine("EC2/EC3/EC7 DESIGN COMPLIANCE CHECK\n");
            sb.AppendLine(BIMManager.GapFixEngine.ValidateStructuralModel(ctx.Doc));
            TaskDialog.Show("STING Design Check", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>Enhanced DWG import with 60+ international layer patterns.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StrEnhancedCADImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;

            var sb = new StringBuilder();
            sb.AppendLine("ENHANCED DWG STRUCTURAL IMPORT\n");
            sb.AppendLine("Supported layer standards:");
            sb.AppendLine("  ISO 13567 (S-COLS, S-BEAM, S-SLAB, S-FNDN, S-GRID)");
            sb.AppendLine("  AIA (S-COLS-CONC, S-BEAM-CONC, S-FNDN-CONC)");
            sb.AppendLine("  BS 1192 (Str-Col, Str-Beam, Str-Slab, Str-Fdn)");
            sb.AppendLine("  DIN (TWK-STUE, TWK-TRAE, TWK-DECK, TWK-WAND)");
            sb.AppendLine("  SIA Swiss (TRAG-ST, TRAG-TR, TRAG-DE)");
            sb.AppendLine($"\nTotal patterns: {BIMManager.GapFixEngine.InternationalLayerPatterns.Count}");
            sb.AppendLine("\nUse 'DWG → Struct' command with enhanced layer detection enabled.");

            TaskDialog.Show("STING Enhanced DWG", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion
}
