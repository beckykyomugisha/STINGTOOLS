// FILE: NECStandards.cs
// LOCATION: StingTools.Standards/NEC2023/
// LINES: ~3000
// PURPOSE: NEC 2023 electrical standards compliance and calculations

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Standards.NEC2023
{
    #region Supporting Classes

    /// <summary>
    /// Conductor material types. Copper-clad aluminium (CCA) takes the "ALUMINUM OR
    /// COPPER-CLAD ALUMINUM" columns of NEC Table 310.16 and its own 240.4(D) limits
    /// (14 AWG 10 A, 12 AWG 15 A, 10 AWG 25 A). No BS 7671 / IEC path in StingTools has CCA
    /// data: those paths refuse it by name (<see cref="ConductorMaterialText.IsCopperClad"/>).
    /// </summary>
    public enum ConductorMaterial
    {
        Copper,
        Aluminum,
        CopperCladAluminum
    }

    /// <summary>
    /// The one reading of conductor-material text ("Cu", "Al", "CCA" and their spelled-out
    /// forms) into <see cref="ConductorMaterial"/>. Before it, every NEC caller tested
    /// <c>== "Al"</c> and took anything else as copper, so an unrecognised material was
    /// sized silently as copper. Blank is copper (the panels' and data's default);
    /// anything unrecognised is NOT parsed and the caller must refuse.
    /// </summary>
    public static class ConductorMaterialText
    {
        public const string CopperCladAluminiumLabel = "CCA";

        public static bool TryParse(string text, out ConductorMaterial material)
        {
            material = ConductorMaterial.Copper;
            string t = (text ?? "").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            switch (t)
            {
                case "": case "cu": case "copper":
                    material = ConductorMaterial.Copper; return true;
                case "al": case "aluminium": case "aluminum":
                    material = ConductorMaterial.Aluminum; return true;
                case "cca": case "copper-clad-aluminium": case "copper-clad-aluminum":
                case "copperclad-aluminium": case "copperclad-aluminum":
                    material = ConductorMaterial.CopperCladAluminum; return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the text names copper-clad aluminium.</summary>
        public static bool IsCopperClad(string text)
            => TryParse(text, out var m) && m == ConductorMaterial.CopperCladAluminum;

        /// <summary>Short label: "Cu", "Al" or "CCA".</summary>
        public static string Label(ConductorMaterial material) => material switch
        {
            ConductorMaterial.Aluminum => "Al",
            ConductorMaterial.CopperCladAluminum => CopperCladAluminiumLabel,
            _ => "Cu",
        };

        /// <summary>The refusal every BS 7671 / IEC path gives for CCA.</summary>
        public const string NoBsDataRefusal =
            "copper-clad aluminium (CCA) has no BS 7671 / IEC data in StingTools — not sized or calculated as copper or aluminium";

        /// <summary>True when the text names aluminium (not copper-clad aluminium).</summary>
        public static bool IsAluminium(string text)
            => TryParse(text, out var m) && m == ConductorMaterial.Aluminum;

        /// <summary>What <see cref="Resolve"/> returns when nothing is recorded or given.</summary>
        public const string CopperAssumedNote = "copper assumed — no conductor material recorded";

        /// <summary>
        /// The material a command works on, and why. Order: the material RECORDED on the
        /// element (e.g. ELC_WIRE_COND_MAT_TXT) — a fact about that element, so it wins;
        /// else the caller's explicit setting (a panel choice); else copper, ASSUMED, and
        /// <see cref="ResolvedConductorMaterial.Assumed"/> / <see cref="ResolvedConductorMaterial.Basis"/>
        /// say so, for the command to show. Text that is not Cu / Al / CCA is a refusal, never copper.
        /// </summary>
        public static ResolvedConductorMaterial Resolve(string recorded, string setting, string recordedSource = "ELC_WIRE_COND_MAT_TXT")
        {
            if (!string.IsNullOrWhiteSpace(recorded))
            {
                if (TryParse(recorded, out var m))
                    return new ResolvedConductorMaterial { Ok = true, Material = m, Basis = $"{Label(m)} recorded ({recordedSource})" };
                return new ResolvedConductorMaterial
                {
                    Ok = false,
                    Refusal = $"conductor material \"{recorded.Trim()}\" in {recordedSource} is not recognised (Cu, Al or CCA)",
                };
            }
            if (!string.IsNullOrWhiteSpace(setting))
            {
                if (TryParse(setting, out var m))
                    return new ResolvedConductorMaterial { Ok = true, Material = m, Basis = $"{Label(m)} (setting)" };
                return new ResolvedConductorMaterial
                {
                    Ok = false,
                    Refusal = $"conductor material setting \"{setting.Trim()}\" is not recognised (Cu, Al or CCA)",
                };
            }
            return new ResolvedConductorMaterial
            {
                Ok = true, Material = ConductorMaterial.Copper, Assumed = true, Basis = CopperAssumedNote,
            };
        }
    }

    /// <summary>The outcome of <see cref="ConductorMaterialText.Resolve"/>.</summary>
    public sealed class ResolvedConductorMaterial
    {
        /// <summary>False when the text was not recognised — the caller must refuse, with <see cref="Refusal"/>.</summary>
        public bool Ok { get; set; }
        public ConductorMaterial Material { get; set; }
        /// <summary>True when nothing was recorded or set and copper was assumed. Show it.</summary>
        public bool Assumed { get; set; }
        /// <summary>Where the material came from, e.g. "Al recorded (ELC_WIRE_COND_MAT_TXT)".</summary>
        public string Basis { get; set; } = "";
        public string Refusal { get; set; }
        /// <summary>"Cu", "Al" or "CCA"; null when not <see cref="Ok"/>.</summary>
        public string Label => Ok ? ConductorMaterialText.Label(Material) : null;
    }

    /// <summary>
    /// Insulation temperature ratings
    /// </summary>
    public enum TemperatureRating
    {
        Celsius60 = 60,
        Celsius75 = 75,
        Celsius90 = 90
    }

    /// <summary>
    /// Conduit types
    /// </summary>
    public enum ConduitType
    {
        RMC,  // Rigid Metal Conduit
        IMC,  // Intermediate Metal Conduit
        EMT,  // Electrical Metallic Tubing
        PVC   // Polyvinyl Chloride
    }

    /// <summary>
    /// Insulation types
    /// </summary>
    public enum InsulationType
    {
        THHN,
        THWN,
        XHHW,
        THW,
        RHW
    }

    /// <summary>
    /// Conductor information
    /// </summary>
    public class Conductor
    {
        public string Size { get; set; }
        public ConductorMaterial Material { get; set; }
        public InsulationType Insulation { get; set; }
        public int Count { get; set; } = 1;
    }

    /// <summary>
    /// Conduit fill calculation result
    /// </summary>
    public class ConduitFillResult
    {
        public string ConduitSize { get; set; }
        public double FillPercentage { get; set; }
        public double AllowedFillPercentage { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    #endregion

    /// <summary>
    /// NEC 2023 electrical standards implementation
    /// National Electrical Code compliance checking and calculations
    /// </summary>
    public static class NECStandards
    {
        public const string Version = "2023";

        #region Article 310 - Conductor Sizing

        /// <summary>
        /// NEC 2023 Table 310.16, Ampacities of Insulated Conductors with Not More Than Three
        /// Current-Carrying Conductors in Raceway, Cable, or Earth (Directly Buried); 60 / 75 /
        /// 90 °C columns. Checked row by row on 2026-10-02 against the 2023 table reproduced
        /// unmarked by NFPA as the base of Public Inputs 1432, 221 and 773-NFPA 70-2023
        /// [310.16] (all three agree), NEC CMP-6 First Draft public-input report pp. 78-79/307,
        /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P06_FD_PIResponses.pdf.
        /// That check corrected seven cells: Cu 3 AWG 90 °C 110→115, Cu 600 kcmil 60 °C
        /// 355→350, Cu 1500 kcmil 60 °C 520→525, Cu 2000 kcmil 60 °C 560→555, Al 8 AWG
        /// 60 °C 30→35, Al 300 kcmil 60/90 °C 190/255→195/260, Al 700 kcmil 60/90 °C
        /// 310/420→315/425. Pinned by StingTools.Tags.Tests/Nec310_16TableTests.
        /// </summary>
        private static readonly Dictionary<string, (int temp60C, int temp75C, int temp90C)> _copperAmpacityTable = new Dictionary<string, (int, int, int)>
        {
            { "14", (15, 20, 25) },
            { "12", (20, 25, 30) },
            { "10", (30, 35, 40) },
            { "8", (40, 50, 55) },
            { "6", (55, 65, 75) },
            { "4", (70, 85, 95) },
            { "3", (85, 100, 115) },
            { "2", (95, 115, 130) },
            { "1", (110, 130, 145) },
            { "1/0", (125, 150, 170) },
            { "2/0", (145, 175, 195) },
            { "3/0", (165, 200, 225) },
            { "4/0", (195, 230, 260) },
            { "250", (215, 255, 290) },
            { "300", (240, 285, 320) },
            { "350", (260, 310, 350) },
            { "400", (280, 335, 380) },
            { "500", (320, 380, 430) },
            { "600", (350, 420, 475) },
            { "700", (385, 460, 520) },
            { "750", (400, 475, 535) },
            { "800", (410, 490, 555) },
            { "900", (435, 520, 585) },
            { "1000", (455, 545, 615) },
            { "1250", (495, 590, 665) },
            { "1500", (525, 625, 705) },
            { "1750", (545, 650, 735) },
            { "2000", (555, 665, 750) }
        };

        private static readonly Dictionary<string, (int temp60C, int temp75C, int temp90C)> _aluminumAmpacityTable = new Dictionary<string, (int, int, int)>
        {
            { "12", (15, 20, 25) },
            { "10", (25, 30, 35) },
            { "8", (35, 40, 45) },
            { "6", (40, 50, 55) },
            { "4", (55, 65, 75) },
            { "3", (65, 75, 85) },
            { "2", (75, 90, 100) },
            { "1", (85, 100, 115) },
            { "1/0", (100, 120, 135) },
            { "2/0", (115, 135, 150) },
            { "3/0", (130, 155, 175) },
            { "4/0", (150, 180, 205) },
            { "250", (170, 205, 230) },
            { "300", (195, 230, 260) },
            { "350", (210, 250, 280) },
            { "400", (225, 270, 305) },
            { "500", (260, 310, 350) },
            { "600", (285, 340, 385) },
            { "700", (315, 375, 425) },
            { "750", (320, 385, 435) },
            { "800", (330, 395, 445) },
            { "900", (355, 425, 480) },
            { "1000", (375, 445, 500) },
            { "1250", (405, 485, 545) },
            { "1500", (435, 520, 585) },
            { "1750", (455, 545, 615) },
            { "2000", (470, 560, 630) }
        };

        /// <summary>
        /// Get conductor ampacity from NEC Table 310.16
        /// </summary>
        /// <param name="wireSize">Wire size (AWG or kcmil)</param>
        /// <param name="material">Conductor material</param>
        /// <param name="tempRating">Temperature rating (60°C, 75°C, or 90°C)</param>
        /// <returns>Ampacity in amperes</returns>
        /// <exception cref="ArgumentException">Invalid wire size or temperature rating</exception>
        /// <example>
        /// <code>
        /// int ampacity = ConductorSizing.GetConductorAmpacity("8", ConductorMaterial.Copper, 75);
        /// // Returns 50 amperes for 8 AWG copper at 75°C
        /// </code>
        /// </example>
        public static int GetConductorAmpacity(string wireSize, ConductorMaterial material, int tempRating)
        {
            // Table 310.16 heads its second column group "ALUMINUM OR COPPER-CLAD ALUMINUM":
            // Al and CCA share it (2023 leaves its 14 AWG row blank).
            var table = material == ConductorMaterial.Copper ? _copperAmpacityTable : _aluminumAmpacityTable;

            if (!table.ContainsKey(wireSize))
                throw new ArgumentException($"Invalid wire size: {wireSize}");

            var (temp60, temp75, temp90) = table[wireSize];

            return tempRating switch
            {
                60 => temp60,
                75 => temp75,
                90 => temp90,
                _ => throw new ArgumentException($"Invalid temperature rating: {tempRating}. Must be 60, 75, or 90.")
            };
        }

        /// <summary>
        /// NEC 2023 Table 310.15(B)(1)(1), Ambient Temperature Correction Factors Based on
        /// 30 °C (86 °F): upper bound of each band (°C) and the 60 / 75 / 90 °C factors; NaN is
        /// the table's "—" (no factor: the conductor may not be used at that ambient).
        /// Checked 2026-10-02 against the 2023 table reproduced unmarked by NFPA as the base of
        /// PIs 1309 and 960-NFPA 70-2023 [310.15(B)(2)], NEC CMP-6 First Draft public-input
        /// report pp. 54 and 59/307 (identical), https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P06_FD_PIResponses.pdf.
        /// The table held here before was per-degree and not the NEC's: 36 °C gave 0.91 for a
        /// 75 °C conductor (the table: 0.88), 46-50 °C 0.71-0.67 (0.75), and every ambient above
        /// 50 °C 0.67, where the table gives 0.67 / 0.58 / 0.47 / 0.33 and then no factor at all.
        /// </summary>
        private static readonly (double maxC, double f60, double f75, double f90)[] _ambientCorrection30C =
        {
            (10, 1.29, 1.20, 1.15), (15, 1.22, 1.15, 1.12), (20, 1.15, 1.11, 1.08), (25, 1.08, 1.05, 1.04),
            (30, 1.00, 1.00, 1.00), (35, 0.91, 0.94, 0.96), (40, 0.82, 0.88, 0.91), (45, 0.71, 0.82, 0.87),
            (50, 0.58, 0.75, 0.82), (55, 0.41, 0.67, 0.76), (60, double.NaN, 0.58, 0.71), (65, double.NaN, 0.47, 0.65),
            (70, double.NaN, 0.33, 0.58), (75, double.NaN, double.NaN, 0.50), (80, double.NaN, double.NaN, 0.41),
            (85, double.NaN, double.NaN, 0.29),
        };

        /// <summary>
        /// Table 310.15(B)(1)(1) factor for an ambient (°C) and a conductor temperature rating
        /// (60, 75 or 90). The bands are whole degrees; a fractional ambient takes the hotter
        /// band (30.5 °C is corrected as 31-35 °C). NaN when the table gives no factor ("—",
        /// or above 85 °C) — the conductor is not usable there and the caller must refuse.
        /// </summary>
        public static double GetTemperatureCorrectionFactor(double ambientC, int conductorTempRating)
        {
            if (conductorTempRating != 60 && conductorTempRating != 75 && conductorTempRating != 90)
                throw new ArgumentException($"Invalid temperature rating: {conductorTempRating}. Must be 60, 75, or 90.");
            foreach (var row in _ambientCorrection30C)
            {
                if (ambientC <= row.maxC)
                    return conductorTempRating == 60 ? row.f60 : conductorTempRating == 75 ? row.f75 : row.f90;
            }
            return double.NaN;
        }

        /// <summary>
        /// Ambient correction on the 75 °C column (the column the NEC sizer reads, 110.14(C)).
        /// Returns 0 when Table 310.15(B)(1)(1) gives no 75 °C factor (above 70 °C), so no
        /// conductor carries the load and the sizer refuses — never a capped factor.
        /// </summary>
        public static double ApplyTemperatureCorrection(double ampacity, double ambientTemp)
        {
            double f = GetTemperatureCorrectionFactor(ambientTemp, 75);
            return double.IsNaN(f) ? 0 : ampacity * f;
        }

        /// <summary>Chapter 9 Table 1 fill fraction: 1 conductor 53 %, 2 conductors 31 %,
        /// over 2 40 % (checked against the CMP-8 First Revision report p. 161/163,
        /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P08_FD_PrelimFR.pdf).</summary>
        public static double GetConduitFillFraction(int conductorCount)
            => _conduitFillPercentages[Math.Min(Math.Max(conductorCount, 1), 3)];

        /// <summary>
        /// Apply conductor bundling adjustment factor
        /// Reference: NEC 2023 Table 310.15(C)(1) (Table 310.15(B)(3)(a) in earlier editions):
        /// 4-6 80 %, 7-9 70 %, 10-20 50 %, 21-30 45 %, 31-40 40 %, 41 and above 35 %. Checked
        /// 2026-10-02 against PI 3958-NFPA 70-2023 [310.15(C)(1)], CMP-6 PI report p. 63/307,
        /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P06_FD_PIResponses.pdf.
        /// </summary>
        /// <param name="ampacity">Base ampacity</param>
        /// <param name="conductorCount">Number of current-carrying conductors</param>
        /// <returns>Adjusted ampacity</returns>
        public static double ApplyBundlingAdjustment(double ampacity, int conductorCount)
        {
            if (conductorCount <= 3)
                return ampacity;

            if (conductorCount <= 6)
                return ampacity * 0.80;

            if (conductorCount <= 9)
                return ampacity * 0.70;

            if (conductorCount <= 20)
                return ampacity * 0.50;

            if (conductorCount <= 30)
                return ampacity * 0.45;

            if (conductorCount <= 40)
                return ampacity * 0.40;

            return ampacity * 0.35;
        }

        /// <summary>
        /// Calculate voltage drop for conductor
        /// Reference: NEC 2023 recommendations (3% branch, 5% total)
        /// Formula: VD = (2 × K × I × L) / CM
        /// </summary>
        /// <param name="current">Current in amperes</param>
        /// <param name="length">One-way length in feet</param>
        /// <param name="wireSize">Wire size</param>
        /// <param name="voltage">System voltage</param>
        /// <returns>Voltage drop in volts</returns>
        public static double CalculateVoltageDrop(double current, double length, string wireSize, int voltage)
        {
            // K constant: 12.9 for copper, 21.2 for aluminum
            double K = 12.9; // Assuming copper
            double circularMils = GetCircularMils(wireSize);

            double voltageDrop = (2 * K * current * length) / circularMils;
            return voltageDrop;
        }

        /// <summary>
        /// Get minimum conductor size for given load
        /// </summary>
        /// <param name="load">Load in amperes</param>
        /// <param name="conductorCount">Number of conductors</param>
        /// <param name="ambientTemp">Ambient temperature in Celsius</param>
        /// <returns>Minimum wire size</returns>
        public static string GetMinimumConductorSize(double load, int conductorCount, double ambientTemp)
        {
            var wireSizes = new[] { "14", "12", "10", "8", "6", "4", "3", "2", "1", "1/0", "2/0", "3/0", "4/0", "250", "300", "350", "400", "500", "600", "700", "750", "800", "900", "1000" };

            foreach (var size in wireSizes)
            {
                double ampacity = GetConductorAmpacity(size, ConductorMaterial.Copper, 75);
                ampacity = ApplyTemperatureCorrection(ampacity, ambientTemp);
                ampacity = ApplyBundlingAdjustment(ampacity, conductorCount);

                if (ampacity >= load)
                    return size;
            }

            return "Oversized - use parallel conductors";
        }

        /// <summary>
        /// NEC 2023 Chapter 9 Table 8 circular-mil areas for AWG sizes; kcmil sizes are
        /// n x 1000 by definition. The ONE copy (CableSizerEngine reads this). Checked
        /// 2026-10-02 against the 2023 Table 8 reproduced by NFPA as the base of PIs 2158 and
        /// 259-NFPA 70-2023 [Chapter 9 Table 8], CMP-6 PI report pp. 301 and 305/307,
        /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P06_FD_PIResponses.pdf. 0 for a size Table 8 does not list.
        /// </summary>
        public static double GetCircularMils(string wireSize)
        {
            if (string.IsNullOrWhiteSpace(wireSize)) return 0;
            if (_awgCircularMils.TryGetValue(wireSize.Trim(), out double cm)) return cm;
            return _tableEightKcmil.Contains(wireSize.Trim()) ? double.Parse(wireSize.Trim(), System.Globalization.CultureInfo.InvariantCulture) * 1000.0 : 0;
        }

        private static readonly Dictionary<string, double> _awgCircularMils = new Dictionary<string, double>
        {
            { "18", 1620 }, { "16", 2580 }, { "14", 4110 }, { "12", 6530 }, { "10", 10380 }, { "8", 16510 },
            { "6", 26240 }, { "4", 41740 }, { "3", 52620 }, { "2", 66360 }, { "1", 83690 },
            { "1/0", 105600 }, { "2/0", 133100 }, { "3/0", 167800 }, { "4/0", 211600 },
        };

        /// <summary>The kcmil sizes Table 8 lists.</summary>
        private static readonly HashSet<string> _tableEightKcmil = new HashSet<string>
        {
            "250", "300", "350", "400", "500", "600", "700", "750", "800", "900", "1000", "1250", "1500", "1750", "2000",
        };

        #endregion

        #region Article 240 - Overcurrent Protection

        // NEC 240.6(A) standard ratings are NOT held here. Their one copy is
        // StingTools/Data/STING_WIRE_TABLES.json → breakerSizes.NEC_OCPD, read by
        // VoltageDropEngine.NextStandardBreakerSizeNEC (DSCH-25). The list that used to
        // sit here also returned the largest rating when nothing fitted.

        /// <summary>
        /// NEC 2023 240.4(D) small-conductor overcurrent limits, A — the ONLY sizes the rule
        /// covers (the table kept here before also carried 8 AWG to 4/0 values, e.g. 8 AWG
        /// 40 A, that are not in 240.4(D) and capped those conductors below their 75 °C
        /// ampacity). Copper 18/16/14/12/10 AWG: 7 / 10 / 15 / 20 / 30 A; aluminium and
        /// copper-clad aluminium 12/10 AWG: 15 / 25 A. Confirmed 2026-10-02 against the
        /// NFPA report reproducing the NFPA 70-2023 text: Public Input 705-NFPA 70-2023 [Section 240.4], NEC CMP-10 First Draft public-input report, pp. 321-322/533, https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P10_FD_PIResponses.pdf.
        /// The 18 / 16 AWG values carry conditions this table does not check (continuous load
        /// at most 5.6 / 8 A; a device listed and marked for the conductor, or Class CC / CF /
        /// J / T fuses). Copper-clad aluminium: 240.4(D)(3) 14 AWG 10 A, (D)(5) 12 AWG 15 A,
        /// (D)(7) 10 AWG 25 A (same source). 2023 Table 310.16 gives 14 AWG CCA no ampacity,
        /// so the sizer cannot pick it; the limit still applies to a 14 AWG CCA conductor
        /// already in the model.
        /// </summary>
        private static readonly Dictionary<string, int> _smallConductorMaxCopper = new Dictionary<string, int>
        {
            { "18", 7 }, { "16", 10 }, { "14", 15 }, { "12", 20 }, { "10", 30 },
        };
        private static readonly Dictionary<string, int> _smallConductorMaxAluminum = new Dictionary<string, int>
        {
            { "12", 15 }, { "10", 25 },
        };
        private static readonly Dictionary<string, int> _smallConductorMaxCopperCladAluminum = new Dictionary<string, int>
        {
            { "14", 10 }, { "12", 15 }, { "10", 25 },
        };

        /// <summary>240.4(D) limit for the size and material, A; 0 when 240.4(D) does not
        /// cover the size (the device is then governed by 240.4(B)/(C) on the ampacity).</summary>
        public static int GetSmallConductorMaxOcpd(string wireSize, ConductorMaterial material)
        {
            var table = material switch
            {
                ConductorMaterial.Copper => _smallConductorMaxCopper,
                ConductorMaterial.CopperCladAluminum => _smallConductorMaxCopperCladAluminum,
                _ => _smallConductorMaxAluminum,
            };
            return wireSize != null && table.TryGetValue(wireSize, out int value) ? value : 0;
        }

        /// <summary>
        /// Validate a breaker against the 240.4(D) small-conductor limit (copper). Sizes the
        /// rule does not cover are valid here with a warning — check 240.4(B)/(C) on the
        /// conductor ampacity (ProtectiveDeviceSelection in the plugin does).
        /// </summary>
        public static ValidationResult ValidateBreakerSize(string wireSize, int breakerAmps)
        {
            var result = new ValidationResult { IsValid = true };
            if (string.IsNullOrEmpty(wireSize) || !_copperAmpacityTable.ContainsKey(wireSize))
            {
                result.IsValid = false;
                result.Errors.Add($"Unknown wire size: {wireSize}");
                return result;
            }
            int maxBreaker = GetSmallConductorMaxOcpd(wireSize, ConductorMaterial.Copper);
            if (maxBreaker == 0)
                result.Warnings.Add($"{wireSize}: no 240.4(D) limit — check the device against the conductor ampacity per 240.4(B)/(C)");
            else if (breakerAmps > maxBreaker)
            {
                result.IsValid = false;
                result.Errors.Add($"Breaker size {breakerAmps}A exceeds the 240.4(D) maximum {maxBreaker}A for {wireSize} AWG copper");
            }
            return result;
        }

        /// <summary>240.4(D) limit for a COPPER conductor; 0 when the rule does not cover the size.</summary>
        public static int GetMaximumBreakerSize(string wireSize)
            => GetSmallConductorMaxOcpd(wireSize, ConductorMaterial.Copper);

        /// <summary>
        /// Check if GFCI protection is required
        /// Reference: NEC 2023 Section 210.8
        /// </summary>
        /// <param name="location">Location type</param>
        /// <param name="roomType">Room type</param>
        /// <returns>True if GFCI required</returns>
        public static bool RequiresGFCI(string location, string roomType)
        {
            var gfciLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bathroom", "kitchen", "garage", "outdoor", "basement", "crawlspace",
                "laundry", "utility", "unfinished area", "wet bar"
            };

            return gfciLocations.Contains(roomType.ToLower()) ||
                   gfciLocations.Contains(location.ToLower());
        }

        /// <summary>
        /// Check if AFCI protection is required
        /// Reference: NEC 2023 Section 210.12
        /// </summary>
        /// <param name="location">Location type</param>
        /// <param name="roomType">Room type</param>
        /// <returns>True if AFCI required</returns>
        public static bool RequiresAFCI(string location, string roomType)
        {
            // AFCI required for dwelling units in bedrooms and most habitable rooms (2023 update)
            var afciRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bedroom", "living room", "family room", "den", "library",
                "sunroom", "recreation room", "closet", "hallway", "dining room"
            };

            return afciRooms.Contains(roomType.ToLower());
        }

        #endregion

        #region Article 250 - Grounding and Bonding

        /// <summary>
        /// NEC 2023 Table 250.122, Minimum Size Equipment Grounding Conductors, COPPER column:
        /// OCPD rating "not exceeding" (A) → EGC size. Checked 2026-10-02 against the 2023 table
        /// reproduced unmarked by NFPA as the base of PI 3222-NFPA 70-2023 [250.122], CMP-5 PI
        /// report p. 302/383, https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P05_FD_PIResponses.pdf
        /// (30 A and 40 A rows that the table does not have were removed; they gave the same
        /// answer). The aluminium / copper-clad aluminium column is not held.
        /// </summary>
        private static readonly Dictionary<int, string> _equipmentGroundingConductorTable = new Dictionary<int, string>
        {
            { 15, "14" },
            { 20, "12" },
            { 60, "10" },
            { 100, "8" },
            { 200, "6" },
            { 300, "4" },
            { 400, "3" },
            { 500, "2" },
            { 600, "1" },
            { 800, "1/0" },
            { 1000, "2/0" },
            { 1200, "3/0" },
            { 1600, "4/0" },
            { 2000, "250" },
            { 2500, "350" },
            { 3000, "400" },
            { 4000, "500" },
            { 5000, "700" },
            { 6000, "800" }
        };

        /// <summary>
        /// Get equipment grounding conductor size
        /// Reference: NEC 2023 Table 250.122
        /// </summary>
        /// <param name="breakerSize">Circuit breaker size in amperes</param>
        /// <returns>Equipment grounding conductor size</returns>
        public static string GetEquipmentGroundingConductor(int breakerSize)
        {
            foreach (var kvp in _equipmentGroundingConductorTable.OrderBy(x => x.Key))
            {
                if (breakerSize <= kvp.Key)
                    return kvp.Value;
            }

            return "Consult NEC for sizes above 6000A";
        }

        /// <summary>
        /// NEC 2023 Table 250.66, Grounding Electrode Conductor for Alternating-Current Systems,
        /// copper service conductors → copper GEC, as (largest conductor up to, cmil; GEC):
        /// 2 or smaller 8; 1 or 1/0 6; 2/0 or 3/0 4; over 3/0 through 350 2; over 350 through
        /// 600 1/0; over 600 through 1100 2/0; over 1100 3/0. Checked 2026-10-02 against the 2023
        /// table reproduced unmarked by NFPA as the base of PI 1748-NFPA 70-2023 [250.66], CMP-5
        /// PI report p. 192/383, https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P05_FD_PIResponses.pdf.
        /// The size-keyed table held here before was wrong from 300 kcmil up (300 gave 1/0, the
        /// table 2; 400-600 2/0 or 3/0, the table 1/0; 700-1000 3/0 or 4/0, the table 2/0) and
        /// returned "consult" for sizes smaller than 2 AWG. The aluminium columns are not held.
        /// </summary>
        private static readonly (double maxCmil, string gec)[] _gecCopper =
        {
            (66360, "8"), (105600, "6"), (167800, "4"), (350000, "2"), (600000, "1/0"), (1100000, "2/0"),
            (double.MaxValue, "3/0"),
        };

        /// <summary>
        /// Get grounding electrode conductor size (copper) for the largest copper service-entrance
        /// conductor (AWG / kcmil trade size, or the equivalent area for parallel sets).
        /// Reference: NEC 2023 Table 250.66
        /// </summary>
        public static string GetGroundingElectrodeConductor(string serviceEntranceSize)
        {
            double cm = GetCircularMils(serviceEntranceSize);
            // An equivalent area for parallel sets (e.g. "1100") is any kcmil figure, not only a trade size.
            if (cm <= 0 && double.TryParse(serviceEntranceSize, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double kc) && kc >= 250) cm = kc * 1000.0;
            if (cm <= 0) return "Consult NEC Table 250.66";
            foreach (var row in _gecCopper)
                if (cm <= row.maxCmil) return row.gec;
            return "Consult NEC Table 250.66";
        }

        /// <summary>
        /// Validate grounding system
        /// </summary>
        /// <param name="breakerSize">Circuit breaker size</param>
        /// <param name="groundingConductorSize">Actual grounding conductor size</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateGroundingSystem(int breakerSize, string groundingConductorSize)
        {
            var result = new ValidationResult { IsValid = true };

            string required = GetEquipmentGroundingConductor(breakerSize);
            
            // Simple validation - in reality would need to compare wire sizes properly
            if (groundingConductorSize != required)
            {
                result.Warnings.Add($"Grounding conductor is {groundingConductorSize}, required size is {required} for {breakerSize}A breaker");
            }

            return result;
        }

        #endregion

        #region Chapter 9 - Conduit Fill

        /// <summary>
        /// Table 1 - Percent Fill for Conduit
        /// </summary>
        private static readonly Dictionary<int, double> _conduitFillPercentages = new Dictionary<int, double>
        {
            { 1, 0.53 },  // 1 conductor: 53%
            { 2, 0.31 },  // 2 conductors: 31%
            { 3, 0.40 }   // 3+ conductors: 40%
        };

        /// <summary>
        /// Table 4 - EMT conduit dimensions (internal area in square inches)
        /// VERIFY (2026-10-02): no NFPA committee report for the 2026 cycle reproduces the 2023
        /// Chapter 9 Table 4 EMT rows, so these values are NOT checked against the NFPA text.
        /// Confirm every row against the printed NFPA 70-2023 Chapter 9 Table 4 (Article 358).
        /// </summary>
        private static readonly Dictionary<string, double> _emtConduitAreas = new Dictionary<string, double>
        {
            { "1/2", 0.304 },
            { "3/4", 0.533 },
            { "1", 0.864 },
            { "1-1/4", 1.496 },
            { "1-1/2", 2.036 },
            { "2", 3.356 },
            { "2-1/2", 5.858 },
            { "3", 8.846 },
            { "3-1/2", 11.545 },
            { "4", 14.753 }
        };

        /// <summary>
        /// Wire cross-sectional areas with THHN insulation (square inches)
        /// VERIFY (2026-10-02): no NFPA committee report reproduces the 2023 Chapter 9 Table 5
        /// THHN rows, so these are NOT checked against the NFPA text. Confirm every row against
        /// the printed NFPA 70-2023 Chapter 9 Table 5 — in particular 300, 350, 700 and 750 kcmil.
        /// </summary>
        private static readonly Dictionary<string, double> _wireAreas = new Dictionary<string, double>
        {
            { "14", 0.0097 },
            { "12", 0.0133 },
            { "10", 0.0211 },
            { "8", 0.0366 },
            { "6", 0.0507 },
            { "4", 0.0824 },
            { "3", 0.0973 },
            { "2", 0.1158 },
            { "1", 0.1562 },
            { "1/0", 0.1855 },
            { "2/0", 0.2223 },
            { "3/0", 0.2679 },
            { "4/0", 0.3237 },
            { "250", 0.3970 },
            { "300", 0.4596 },
            { "350", 0.5281 },
            { "400", 0.5863 },
            { "500", 0.7073 },
            { "600", 0.8676 },
            { "700", 1.0252 },
            { "750", 1.0532 }
        };

        /// <summary>
        /// Calculate conduit fill percentage
        /// Reference: NEC 2023 Chapter 9 Tables 1, 4, and 5
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Conduit fill result</returns>
        public static ConduitFillResult CalculateConduitFill(List<Conductor> conductors, ConduitType type, string size)
        {
            var result = new ConduitFillResult
            {
                ConduitSize = size
            };

            // Get conduit area (currently only EMT supported)
            if (!_emtConduitAreas.TryGetValue(size, out double conduitArea))
            {
                result.Warnings.Add($"Conduit size {size} not found in tables");
                return result;
            }

            // Calculate total wire area
            double totalWireArea = 0;
            int totalConductors = 0;

            foreach (var conductor in conductors)
            {
                if (_wireAreas.TryGetValue(conductor.Size, out double wireArea))
                {
                    totalWireArea += wireArea * conductor.Count;
                    totalConductors += conductor.Count;
                }
                else
                {
                    result.Warnings.Add($"Wire size {conductor.Size} not found in tables");
                }
            }

            // Determine allowed fill percentage
            int conductorKey = totalConductors == 1 ? 1 : (totalConductors == 2 ? 2 : 3);
            result.AllowedFillPercentage = _conduitFillPercentages[conductorKey] * 100;

            // Calculate actual fill percentage
            result.FillPercentage = (totalWireArea / conduitArea) * 100;

            // Check compliance
            result.IsCompliant = result.FillPercentage <= result.AllowedFillPercentage;

            if (!result.IsCompliant)
            {
                result.Warnings.Add($"Conduit fill {result.FillPercentage:F1}% exceeds allowed {result.AllowedFillPercentage:F0}%");
            }

            return result;
        }

        /// <summary>
        /// Get minimum conduit size for conductors
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <returns>Minimum conduit size</returns>
        public static string GetMinimumConduitSize(List<Conductor> conductors, ConduitType type)
        {
            var conduitSizes = new[] { "1/2", "3/4", "1", "1-1/4", "1-1/2", "2", "2-1/2", "3", "3-1/2", "4" };

            foreach (var size in conduitSizes)
            {
                var fillResult = CalculateConduitFill(conductors, type, size);
                if (fillResult.IsCompliant)
                    return size;
            }

            return "Larger than 4\" - use multiple conduits";
        }

        /// <summary>
        /// Validate conduit fill
        /// </summary>
        /// <param name="conductors">List of conductors</param>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateConduitFill(List<Conductor> conductors, ConduitType type, string size)
        {
            var result = new ValidationResult { IsValid = true };

            var fillResult = CalculateConduitFill(conductors, type, size);

            if (!fillResult.IsCompliant)
            {
                result.IsValid = false;
                result.Errors.Add($"Conduit {size} is overfilled: {fillResult.FillPercentage:F1}% (allowed: {fillResult.AllowedFillPercentage:F0}%)");
            }

            result.Warnings.AddRange(fillResult.Warnings);

            return result;
        }

        /// <summary>
        /// Get conduit internal area
        /// </summary>
        /// <param name="type">Conduit type</param>
        /// <param name="size">Conduit size</param>
        /// <returns>Internal area in square inches</returns>
        public static double GetConduitArea(ConduitType type, string size)
        {
            // Currently only EMT supported
            return _emtConduitAreas.TryGetValue(size, out double value) ? value : 0;
        }

        /// <summary>
        /// Get wire cross-sectional area
        /// </summary>
        /// <param name="wireSize">Wire size</param>
        /// <param name="insulation">Insulation type</param>
        /// <returns>Area in square inches</returns>
        public static double GetWireArea(string wireSize, InsulationType insulation)
        {
            // Currently only THHN areas included
            return _wireAreas.TryGetValue(wireSize, out double value) ? value : 0;
        }

        #endregion

        #region Article 110 - General Requirements

        /// <summary>
        /// Generate panel schedule label
        /// Reference: NEC 2023 Article 110 labeling requirements
        /// </summary>
        /// <param name="panelName">Panel designation</param>
        /// <param name="voltage">System voltage</param>
        /// <param name="mainBreaker">Main breaker size</param>
        /// <returns>Label text</returns>
        public static string GeneratePanelScheduleLabel(string panelName, int voltage, int mainBreaker)
        {
            return $"PANEL: {panelName}\nVOLTAGE: {voltage}V\nMAIN: {mainBreaker}A\nNEC 2023 COMPLIANT";
        }

        /// <summary>
        /// Generate disconnect label
        /// </summary>
        /// <param name="equipmentName">Equipment name</param>
        /// <param name="voltage">Voltage</param>
        /// <param name="amperage">Amperage</param>
        /// <returns>Label text</returns>
        public static string GenerateDisconnectLabel(string equipmentName, int voltage, int amperage)
        {
            return $"DISCONNECT\n{equipmentName}\n{voltage}V, {amperage}A\nWARNING: ELECTRICAL HAZARD";
        }

        /// <summary>
        /// Validate labeling compliance
        /// </summary>
        /// <param name="hasLabel">Whether label exists</param>
        /// <param name="labelText">Label text content</param>
        /// <returns>Validation result</returns>
        public static ValidationResult ValidateLabelingCompliance(bool hasLabel, string labelText)
        {
            var result = new ValidationResult { IsValid = true };

            if (!hasLabel)
            {
                result.IsValid = false;
                result.Errors.Add("Required labeling is missing per NEC 110.21");
            }
            else if (string.IsNullOrWhiteSpace(labelText))
            {
                result.Warnings.Add("Label text is empty or incomplete");
            }

            return result;
        }

        #endregion

        #region Article 220 - Branch Circuit and Feeder Calculations

        /// <summary>
        /// Calculate general lighting load
        /// Reference: NEC 2023 Table 220.12
        /// </summary>
        /// <param name="squareFeet">Building area in square feet</param>
        /// <param name="occupancyType">Type of occupancy</param>
        /// <returns>Lighting load in VA</returns>
        public static double CalculateLightingLoad(double squareFeet, string occupancyType)
        {
            var loadFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "dwelling", 3.0 },
                { "hotel", 2.0 },
                { "warehouse", 0.25 },
                { "office", 1.0 },
                { "retail", 3.0 },
                { "school", 3.0 }
            };

            double vaPerSqFt = loadFactors.ContainsKey(occupancyType) 
                ? loadFactors[occupancyType] 
                : 3.0; // Default to dwelling

            return squareFeet * vaPerSqFt;
        }

        /// <summary>
        /// Apply demand factors to load calculation
        /// </summary>
        /// <param name="loadType">Type of load</param>
        /// <param name="quantity">Number of items</param>
        /// <returns>Demand factor (0.0 to 1.0)</returns>
        public static double ApplyDemandFactors(string loadType, int quantity)
        {
            // Simplified demand factor application
            // Full implementation would reference NEC Tables 220.42, 220.54, etc.
            
            if (loadType.Equals("range", StringComparison.OrdinalIgnoreCase))
            {
                // Table 220.55 - demand factors for ranges
                if (quantity <= 1) return 1.0;
                if (quantity <= 3) return 0.80;
                if (quantity <= 5) return 0.70;
                return 0.55;
            }

            return 1.0; // No demand factor
        }

        #endregion
    }
}
