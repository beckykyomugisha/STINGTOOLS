using System;
using System.Collections.Generic;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// IEEE 1584-2002 equipment class. Sets the enclosure coefficient (box / open air),
    /// the typical bus gap, the distance exponent x and the typical working distance
    /// (IEEE 1584-2002 Tables 3 and 4, 0.208–1 kV row).
    /// </summary>
    public enum ArcEquipmentClass
    {
        /// <summary>Open air: open-air K / K1, x = 2.000, gap 25 mm (range 10–40), D 455 mm.</summary>
        OpenAir,
        /// <summary>LV switchgear / switchboard: box, x = 1.473, gap 32 mm, D 610 mm.</summary>
        Switchgear,
        /// <summary>LV MCC and panelboard: box, x = 1.641, gap 25 mm, D 455 mm.</summary>
        PanelMcc,
        /// <summary>Cable: x = 2.000, gap 13 mm, D 455 mm. Box coefficients used (conservative).</summary>
        Cable
    }

    /// <summary>Inputs to one arc-flash calculation. SI / engineering units as named.</summary>
    public sealed class ArcFlashInput
    {
        /// <summary>Three-phase bolted fault current Ibf at the equipment, kA.</summary>
        public double BoltedFaultKa { get; set; }
        /// <summary>System line-to-line voltage, V.</summary>
        public double VoltageV { get; set; }
        /// <summary>Equipment class (sets K, K1, x, default gap and default working distance).</summary>
        public ArcEquipmentClass EquipmentClass { get; set; } = ArcEquipmentClass.PanelMcc;
        /// <summary>Working distance, mm. 0 = class default.</summary>
        public double WorkingDistanceMm { get; set; }
        /// <summary>Bus gap, mm. 0 = class default.</summary>
        public double GapMm { get; set; }
        /// <summary>
        /// True = solidly grounded (K2 = −0.113). False = ungrounded / high-resistance
        /// grounded (K2 = 0), which gives ~30 % more energy and is the conservative choice
        /// when the earthing arrangement is not confirmed.
        /// </summary>
        public bool SolidlyGrounded { get; set; }
        /// <summary>
        /// Fixed arc duration, s. Used for both the full and the 85 % arcing-current
        /// case when no clearing-time function is supplied.
        /// </summary>
        public double ClearingTimeS { get; set; }
    }

    /// <summary>Result of one arc-flash calculation. When <see cref="Calculated"/> is false
    /// no energy value exists and nothing numeric may be written.</summary>
    public sealed class ArcFlashResult
    {
        public bool   Calculated            { get; set; }
        public string NotCalculatedReason   { get; set; } = "";
        public double ArcingCurrentKa       { get; set; }
        public double ReducedArcingCurrentKa{ get; set; }
        public double NormalizedEnergyJcm2  { get; set; }
        public double ClearingTimeS         { get; set; }
        public double ReducedClearingTimeS  { get; set; }
        /// <summary>Arc duration of the governing (higher-energy) case, s.</summary>
        public double GoverningClearingTimeS{ get; set; }
        /// <summary>True when the 85 % arcing-current case gave the higher energy.</summary>
        public bool   ReducedCaseGoverns    { get; set; }
        public double IncidentEnergyJcm2    { get; set; }
        public double IncidentEnergyCalCm2  { get; set; }
        public double BoundaryMm            { get; set; }
        public double WorkingDistanceMm     { get; set; }
        public double GapMm                 { get; set; }
        public double DistanceExponent      { get; set; }
        public int    PpeCategory           { get; set; }
        public List<string> Notes           { get; set; } = new List<string>();
    }

    /// <summary>
    /// Pure-math arc-flash engine — no Revit API. Implements the IEEE 1584-2002
    /// empirical model for 0.208–1 kV three-phase systems.
    ///
    /// Why 2002 and not 2018: the 2018 model needs coefficient tables (k1..k13 per
    /// electrode configuration and voltage band, enclosure-size correction, variation
    /// factor) that cannot be reproduced reliably without the standard in hand. The
    /// previous "2018 regression" in this file was fabricated — its energy FELL as
    /// fault current rose (ROADMAP ELEC-1). The 2002 model is compact, fully published
    /// and physically monotonic. It is SUPERSEDED, so every number this engine produces
    /// is indicative only — see <see cref="Basis"/>.
    ///
    /// Scope refused rather than guessed: V &gt; 1 kV (MV model not implemented),
    /// V &lt; 208 V, and bolted fault outside 0.7–106 kA (outside the 2002 test range).
    /// </summary>
    public static class ArcFlashEngine
    {
        /// <summary>The calculation basis. Must accompany every value this engine produces.</summary>
        public const string Basis =
            "IEEE 1584-2002 (superseded by 2018 — indicative, verify with a licensed study before specifying PPE)";

        /// <summary>Short form for column headings and schedule names.</summary>
        public const string BasisShort = "IEEE 1584-2002 indicative";

        /// <summary>IEEE 1584-2002 calculation factor Cf for voltages ≤ 1 kV.</summary>
        public const double CfLowVoltage = 1.5;

        /// <summary>Incident energy at the arc-flash boundary, J/cm² (= 1.2 cal/cm²).</summary>
        public const double BoundaryEnergyJcm2 = 5.0;

        /// <summary>IEEE 1584-2002 B.1.2 guidance: 2 s is a reasonable maximum arc duration.</summary>
        public const double MaxArcDurationS = 2.0;

        public const double JoulesPerCalorie = 4.184;

        // NFPA 70E legacy hazard/risk category thresholds by incident energy (cal/cm²).
        private static readonly (double maxCal, int cat)[] PpeThresholds =
        {
            (1.2, 0), (4.0, 1), (8.0, 2), (25.0, 3), (40.0, 4)
        };

        // ── Equipment-class tables (IEEE 1584-2002 Tables 3 and 4, ≤ 1 kV) ────

        public static bool IsBox(ArcEquipmentClass c) => c != ArcEquipmentClass.OpenAir;

        public static double DistanceExponent(ArcEquipmentClass c) => c switch
        {
            ArcEquipmentClass.Switchgear => 1.473,
            ArcEquipmentClass.PanelMcc   => 1.641,
            _                            => 2.000   // open air, cable
        };

        public static double DefaultGapMm(ArcEquipmentClass c) => c switch
        {
            ArcEquipmentClass.Switchgear => 32,
            ArcEquipmentClass.Cable      => 13,
            _                            => 25      // panel / MCC, open air (typical)
        };

        public static double DefaultWorkingDistanceMm(ArcEquipmentClass c) =>
            c == ArcEquipmentClass.Switchgear ? 610 : 455;

        // ── Core equations ───────────────────────────────────────────────

        /// <summary>
        /// Arcing current, kA (IEEE 1584-2002 eq. 1, &lt; 1 kV):
        /// lg Ia = K + 0.662·lg Ibf + 0.0966·V + 0.000526·G + 0.5588·V·lg Ibf − 0.00304·G·lg Ibf
        /// with K = −0.153 open air, −0.097 box; Ibf kA, V kV, G mm.
        /// </summary>
        public static double ArcingCurrentKa(double boltedFaultKa, double voltageKv, double gapMm, bool box)
        {
            if (boltedFaultKa <= 0) return 0;
            double K = box ? -0.097 : -0.153;
            double lgIbf = Math.Log10(boltedFaultKa);
            double lgIa = K
                + 0.662 * lgIbf
                + 0.0966 * voltageKv
                + 0.000526 * gapMm
                + 0.5588 * voltageKv * lgIbf
                - 0.00304 * gapMm * lgIbf;
            return Math.Pow(10.0, lgIa);
        }

        /// <summary>
        /// Normalized incident energy En, J/cm², for 0.2 s at 610 mm (IEEE 1584-2002 eq. 3):
        /// lg En = K1 + K2 + 1.081·lg Ia + 0.0011·G,
        /// K1 = −0.792 open air / −0.555 box; K2 = 0 ungrounded or HRG / −0.113 grounded.
        /// </summary>
        public static double NormalizedEnergyJcm2(double arcingCurrentKa, double gapMm, bool box, bool solidlyGrounded)
        {
            if (arcingCurrentKa <= 0) return 0;
            double K1 = box ? -0.555 : -0.792;
            double K2 = solidlyGrounded ? -0.113 : 0.0;
            double lgEn = K1 + K2 + 1.081 * Math.Log10(arcingCurrentKa) + 0.0011 * gapMm;
            return Math.Pow(10.0, lgEn);
        }

        /// <summary>
        /// Incident energy, J/cm² (IEEE 1584-2002 eq. 5):
        /// E = 4.184·Cf·En·(t/0.2)·(610^x / D^x).
        /// </summary>
        public static double IncidentEnergyJcm2(double normalizedEnergyJcm2, double arcDurationS,
            double workingDistanceMm, double distanceExponent, double cf = CfLowVoltage)
        {
            if (normalizedEnergyJcm2 <= 0 || arcDurationS <= 0 || workingDistanceMm <= 0) return 0;
            return 4.184 * cf * normalizedEnergyJcm2 * (arcDurationS / 0.2)
                   * Math.Pow(610.0 / workingDistanceMm, distanceExponent);
        }

        /// <summary>
        /// Arc-flash boundary, mm (IEEE 1584-2002 eq. 7):
        /// DB = [4.184·Cf·En·(t/0.2)·(610^x / EB)]^(1/x), EB = 5.0 J/cm².
        /// </summary>
        public static double BoundaryMm(double normalizedEnergyJcm2, double arcDurationS,
            double distanceExponent, double cf = CfLowVoltage, double boundaryEnergyJcm2 = BoundaryEnergyJcm2)
        {
            if (normalizedEnergyJcm2 <= 0 || arcDurationS <= 0 || boundaryEnergyJcm2 <= 0) return 0;
            double inner = 4.184 * cf * normalizedEnergyJcm2 * (arcDurationS / 0.2)
                           * Math.Pow(610.0, distanceExponent) / boundaryEnergyJcm2;
            return Math.Pow(inner, 1.0 / distanceExponent);
        }

        /// <summary>Returns NFPA 70E PPE category (0–4) by incident energy, or −1 above 40 cal/cm².</summary>
        public static int PpeCategory(double incidentEnergyCalCm2)
        {
            if (incidentEnergyCalCm2 <= 0) return 0;
            foreach (var (maxCal, cat) in PpeThresholds)
                if (incidentEnergyCalCm2 <= maxCal) return cat;
            return -1;
        }

        // ── Full calculation ─────────────────────────────────────────────

        /// <summary>
        /// Runs the full IEEE 1584-2002 LV procedure: arcing current, the 85 % arcing-current
        /// second case, incident energy (worse case reported), boundary and PPE category.
        /// </summary>
        /// <param name="input">Equipment and system data.</param>
        /// <param name="clearingTimeAtArcingKa">
        /// Optional protective-device clearing time (s) as a function of the current (kA)
        /// the device sees. When supplied it is evaluated at Ia and at 0.85·Ia, as IEEE
        /// 1584-2002 requires. Return NaN or ≤ 0 for "unknown". When null,
        /// <see cref="ArcFlashInput.ClearingTimeS"/> is used for both cases.
        /// </param>
        public static ArcFlashResult Calculate(ArcFlashInput input, Func<double, double> clearingTimeAtArcingKa = null)
        {
            var r = new ArcFlashResult();
            if (input == null) return NotCalculated(r, "no input");

            double v = input.VoltageV;
            if (!(v > 0)) return NotCalculated(r, "system voltage unknown");
            if (v > 1000.0) return NotCalculated(r, $"{v:0} V is above 1 kV — MV model not implemented");
            if (v < 208.0) return NotCalculated(r, $"{v:0} V is below the IEEE 1584-2002 range (208 V – 15 kV)");
            double ibf = input.BoltedFaultKa;
            if (!(ibf > 0)) return NotCalculated(r, "bolted fault current unknown");
            if (ibf < 0.7 || ibf > 106.0)
                return NotCalculated(r, $"bolted fault {ibf:0.###} kA is outside the IEEE 1584-2002 range (0.7–106 kA)");

            var cls = input.EquipmentClass;
            bool box = IsBox(cls);
            double gap = input.GapMm > 0 ? input.GapMm : DefaultGapMm(cls);
            double dist = input.WorkingDistanceMm > 0 ? input.WorkingDistanceMm : DefaultWorkingDistanceMm(cls);
            double x = DistanceExponent(cls);
            r.GapMm = gap; r.WorkingDistanceMm = dist; r.DistanceExponent = x;
            if (cls == ArcEquipmentClass.Cable)
                r.Notes.Add("cable: box coefficients used (conservative)");
            if (!input.SolidlyGrounded)
                r.Notes.Add("K2 = 0 (ungrounded/HRG) — conservative; solidly grounded would be −0.113");

            double vKv = v / 1000.0;
            double ia = ArcingCurrentKa(ibf, vKv, gap, box);
            double iaReduced = 0.85 * ia;
            r.ArcingCurrentKa = ia;
            r.ReducedArcingCurrentKa = iaReduced;

            double t1, t2;
            if (clearingTimeAtArcingKa != null)
            {
                t1 = clearingTimeAtArcingKa(ia);
                t2 = clearingTimeAtArcingKa(iaReduced);
            }
            else
            {
                t1 = t2 = input.ClearingTimeS;
            }
            if (double.IsNaN(t1) || double.IsNaN(t2) || t1 <= 0 || t2 <= 0)
                return NotCalculated(r, "protective-device clearing time unknown");
            if (t1 > MaxArcDurationS || t2 > MaxArcDurationS)
                r.Notes.Add($"clearing time capped at {MaxArcDurationS:0} s (IEEE 1584-2002 B.1.2)");
            t1 = Math.Min(t1, MaxArcDurationS);
            t2 = Math.Min(t2, MaxArcDurationS);
            r.ClearingTimeS = t1;
            r.ReducedClearingTimeS = t2;

            double en1 = NormalizedEnergyJcm2(ia, gap, box, input.SolidlyGrounded);
            double en2 = NormalizedEnergyJcm2(iaReduced, gap, box, input.SolidlyGrounded);
            double e1 = IncidentEnergyJcm2(en1, t1, dist, x);
            double e2 = IncidentEnergyJcm2(en2, t2, dist, x);

            double en, t, e;
            if (e2 > e1) { en = en2; t = t2; e = e2; r.ReducedCaseGoverns = true; r.Notes.Add("85 % arcing-current case governs"); }
            else         { en = en1; t = t1; e = e1; }

            r.Calculated = true;
            r.NormalizedEnergyJcm2 = en;
            r.GoverningClearingTimeS = t;
            r.IncidentEnergyJcm2 = e;
            r.IncidentEnergyCalCm2 = e / JoulesPerCalorie;
            r.BoundaryMm = BoundaryMm(en, t, x);
            r.PpeCategory = PpeCategory(r.IncidentEnergyCalCm2);
            return r;
        }

        private static ArcFlashResult NotCalculated(ArcFlashResult r, string reason)
        {
            r.Calculated = false;
            r.NotCalculatedReason = reason;
            return r;
        }

        // ── Label text ───────────────────────────────────────────────────

        /// <summary>
        /// Multi-line label text for a Revit text note / parameter. Always carries
        /// <see cref="Basis"/>; a not-calculated result says so and carries no numbers.
        /// </summary>
        public static string FormatLabel(string panelName, double voltageV, ArcEquipmentClass cls,
            ArcFlashResult r, string clearingTimeSource)
        {
            if (r == null || !r.Calculated)
                return "ARC FLASH HAZARD — NOT CALCULATED\n" +
                       $"Panel: {panelName}\n" +
                       $"Reason: {r?.NotCalculatedReason ?? "no result"}\n" +
                       "A licensed arc-flash study is required.\n" +
                       $"Basis: {Basis}";

            string danger = r.PpeCategory < 0 ? "DANGER — EXCEEDS 40 cal/cm²" : $"PPE Category {r.PpeCategory} (by incident energy)";
            return "ARC FLASH HAZARD — INDICATIVE\n" +
                   $"Panel: {panelName}\n" +
                   $"Voltage: {voltageV:0} V   Class: {cls}\n" +
                   $"Incident Energy: {r.IncidentEnergyCalCm2:0.00} cal/cm² at {r.WorkingDistanceMm:0} mm\n" +
                   $"Arc Flash Boundary: {r.BoundaryMm:0} mm\n" +
                   $"Clearing time: {r.GoverningClearingTimeS * 1000:0} ms ({clearingTimeSource})\n" +
                   $"Gap: {r.GapMm:0} mm\n" +
                   $"{danger}\n" +
                   $"Basis: {Basis}";
        }
    }
}
