using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

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

    public enum ArcFlashMethod
    {
        /// <summary>IEEE 1584-2018 (0.208–15 kV, five electrode configurations, enclosure-size correction). Default.</summary>
        Ieee1584_2018,
        /// <summary>IEEE 1584-2002 LV model (superseded) — kept for comparison with older studies.</summary>
        Ieee1584_2002
    }

    /// <summary>Inputs to one arc-flash calculation. SI / engineering units as named.</summary>
    public sealed class ArcFlashInput
    {
        public ArcFlashMethod Method { get; set; } = ArcFlashMethod.Ieee1584_2018;
        /// <summary>IEEE 1584-2018 electrode configuration; null = VCB for enclosed classes, VOA for open air.</summary>
        public ElectrodeConfiguration? Electrode { get; set; }
        /// <summary>Enclosure height / width / depth, mm (IEEE 1584-2018). 0 = the class's typical size.</summary>
        public double EnclosureHeightMm { get; set; }
        public double EnclosureWidthMm { get; set; }
        public double EnclosureDepthMm { get; set; }
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
        /// IEEE 1584-2002 only (the 2018 model has no grounding term).
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
        /// <summary>The method and its standing — every value this result carries must be shown with it.</summary>
        public string Basis                 { get; set; } = ArcFlashEngine.Basis;
        public string ElectrodeConfiguration{ get; set; } = "";
        /// <summary>IEEE 1584-2018 enclosure-size correction factor (1 for open air / 2002).</summary>
        public double EnclosureCf           { get; set; } = 1.0;
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
    /// Pure-math arc-flash engine — no Revit API. Default method: IEEE 1584-2018
    /// (<see cref="Ieee1584_2018"/>), 0.208–15 kV three-phase, with the enclosure size and
    /// electrode configuration taken from the equipment class's typical values unless
    /// given. The IEEE 1584-2002 LV model below is kept as an option for comparison with
    /// older studies (ROADMAP ELEC-1 records why an earlier "2018 regression" was removed:
    /// it was fabricated; the 2018 coefficients now come from four agreeing transcriptions
    /// and reproduce the standard's Annex D examples).
    ///
    /// Scope refused rather than guessed: anything outside the model's clause 4.2 ranges,
    /// an unknown clearing time, an enclosure narrower than 4 × the gap.
    /// </summary>
    public static class ArcFlashEngine
    {
        /// <summary>The calculation basis of the default method. Must accompany every value produced.</summary>
        public const string Basis =
            "IEEE 1584-2018 (coefficients cross-checked against published implementations, not the printed standard — confirm with a licensed study before specifying PPE)";

        /// <summary>Short form for column headings and schedule names.</summary>
        public const string BasisShort = "IEEE 1584-2018";

        public const string Basis2002 =
            "IEEE 1584-2002 (superseded by 2018 — indicative, verify with a licensed study before specifying PPE)";

        /// <summary>IEEE 1584-2002 calculation factor Cf for voltages ≤ 1 kV.</summary>
        public const double CfLowVoltage = 1.5;

        /// <summary>Incident energy at the arc-flash boundary, J/cm² (= 1.2 cal/cm²).</summary>
        public const double BoundaryEnergyJcm2 = 5.0;

        /// <summary>IEEE 1584-2002 B.1.2 guidance: 2 s is a reasonable maximum arc duration.</summary>
        public const double MaxArcDurationS = 2.0;

        public const double JoulesPerCalorie = 4.184;

        // NFPA 70E legacy hazard/risk category thresholds by incident energy (cal/cm²).
        // Built-in fallback; STING_ARC_FLASH_PPE.json ppeCategories is read and used
        // only where it matches these (see ResolvePpeThresholds).
        internal static readonly (double maxCal, int cat)[] DefaultPpeThresholds =
        {
            (1.2, 0), (4.0, 1), (8.0, 2), (25.0, 3), (40.0, 4)
        };

        private static readonly Lazy<(double maxCal, int cat)[]> _ppeThresholds =
            new Lazy<(double maxCal, int cat)[]>(LoadPpeThresholds);

        private static (double maxCal, int cat)[] PpeThresholds => _ppeThresholds.Value;

        private static (double maxCal, int cat)[] LoadPpeThresholds()
        {
            JObject root = null;
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile("STING_ARC_FLASH_PPE.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    root = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ArcFlashEngine.LoadPpeThresholds: {ex.Message}");
            }
            return ResolvePpeThresholds(root);
        }

        /// <summary>
        /// PPE category thresholds from STING_ARC_FLASH_PPE.json → ppeCategories (rows with
        /// cat ≥ 0; the cat −1 row is "above the last threshold"). The data is used only when
        /// it matches the built-in NFPA 70E thresholds; a list that differs would move
        /// incidents between PPE categories, so it is logged and the built-in list stands.
        /// </summary>
        internal static (double maxCal, int cat)[] ResolvePpeThresholds(JObject root)
        {
            if (!(root?["ppeCategories"] is JArray arr)) return DefaultPpeThresholds;
            var data = new List<(double maxCal, int cat)>();
            foreach (var row in arr.OfType<JObject>())
            {
                var c = row["cat"]; var m = row["maxCalCm2"];
                if (c == null || m == null || c.Type != JTokenType.Integer ||
                    (m.Type != JTokenType.Float && m.Type != JTokenType.Integer)) return DefaultPpeThresholds;
                int cat = c.Value<int>();
                if (cat < 0) continue;
                data.Add((m.Value<double>(), cat));
            }
            data.Sort((a, b) => a.maxCal.CompareTo(b.maxCal));
            if (data.SequenceEqual(DefaultPpeThresholds)) return data.ToArray();
            StingTools.Core.StingLog.WarnRateLimited("ArcFlashEngine.PpeThresholds",
                "STING_ARC_FLASH_PPE.json ppeCategories thresholds [" +
                string.Join(", ", data.Select(d => $"cat {d.cat} <= {d.maxCal}")) +
                "] differ from the built-in NFPA 70E thresholds; the built-in thresholds are used.");
            return DefaultPpeThresholds;
        }

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
            if (input != null && input.Method == ArcFlashMethod.Ieee1584_2018)
                return Calculate2018(input, clearingTimeAtArcingKa);
            var r = new ArcFlashResult { Basis = Basis2002 };
            if (input == null) return NotCalculated(r, "no input");

            double v = input.VoltageV;
            if (!(v > 0)) return NotCalculated(r, "system voltage unknown");
            if (v > 1000.0) return NotCalculated(r, $"{v:0} V is above 1 kV — outside the 2002 LV model (use IEEE 1584-2018)");
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

        // ── IEEE 1584-2018 ───────────────────────────────────────────────

        /// <summary>Typical gap, enclosure H × W × D and working distance (mm) for a class at a
        /// voltage, from the IEEE 1584-2018 typical-equipment values (≥ 2 transcriptions agree).
        /// Depth 0 = not stated: treated as deeper than 203.2 mm, the higher-energy reading.
        /// Returns false when the standard gives no typical value (open air).</summary>
        public static bool Typical2018(ArcEquipmentClass c, double vocKv,
            out double gapMm, out double hMm, out double wMm, out double dMm, out double workMm)
        {
            gapMm = hMm = wMm = dMm = workMm = 0;
            bool lv = vocKv <= 0.6, mv5 = vocKv <= 5.0;
            switch (c)
            {
                case ArcEquipmentClass.Switchgear:
                    if (lv) { gapMm = 32; hMm = wMm = dMm = 508; workMm = 609.6; }
                    else if (mv5) { gapMm = 104; hMm = wMm = dMm = 914.4; workMm = 914.4; }
                    else { gapMm = 152; hMm = 1143; wMm = 762; dMm = 762; workMm = 914.4; }
                    return true;
                case ArcEquipmentClass.PanelMcc:
                    if (lv) { gapMm = 25; hMm = 355.6; wMm = 304.8; dMm = 0; workMm = 457.2; }
                    else if (mv5) { gapMm = 104; hMm = wMm = dMm = 660.4; workMm = 914.4; }
                    else { gapMm = 152; hMm = wMm = dMm = 914.4; workMm = 914.4; }
                    return true;
                case ArcEquipmentClass.Cable:
                    gapMm = 13; hMm = 355.6; wMm = 304.8; dMm = 0; workMm = 457.2;
                    return true;
                default:
                    return false;
            }
        }

        private static ArcFlashResult Calculate2018(ArcFlashInput input, Func<double, double> clearingTimeAtArcingKa)
        {
            var r = new ArcFlashResult { Basis = Basis };
            double v = input.VoltageV;
            if (!(v > 0)) return NotCalculated(r, "system voltage unknown");
            if (!(input.BoltedFaultKa > 0)) return NotCalculated(r, "bolted fault current unknown");
            double kv = v / 1000.0;
            var cls = input.EquipmentClass;
            var ec = input.Electrode ?? (cls == ArcEquipmentClass.OpenAir ? ElectrodeConfiguration.VOA : ElectrodeConfiguration.VCB);
            r.ElectrodeConfiguration = ec.ToString();
            if (input.Electrode == null)
                r.Notes.Add($"electrode configuration {ec} assumed — VCBB or HCB can give higher energy; set it where known");

            bool hasTypical = Typical2018(cls, kv, out double tg, out double th, out double tw, out double td, out double twd);
            double gap = input.GapMm > 0 ? input.GapMm : tg;
            double dist = input.WorkingDistanceMm > 0 ? input.WorkingDistanceMm : twd;
            if (gap <= 0) return NotCalculated(r, "no typical gap for this equipment class — set the gap");
            if (dist <= 0) return NotCalculated(r, "no typical working distance for this equipment class — set it");
            if (input.GapMm <= 0) r.Notes.Add($"typical {cls} gap {gap:0} mm assumed");
            double h = input.EnclosureHeightMm > 0 ? input.EnclosureHeightMm : th;
            double w = input.EnclosureWidthMm > 0 ? input.EnclosureWidthMm : tw;
            double d = input.EnclosureDepthMm > 0 ? input.EnclosureDepthMm : td;
            if (hasTypical && (input.EnclosureHeightMm <= 0 || input.EnclosureWidthMm <= 0))
                r.Notes.Add($"typical {cls} enclosure {h:0} × {w:0} mm assumed");
            if (d <= 0)
            {
                d = double.PositiveInfinity;
                r.Notes.Add("enclosure depth not stated — taken as deeper than 203.2 mm (the higher-energy reading)");
            }
            r.GapMm = gap; r.WorkingDistanceMm = dist;

            bool capped = false;
            Func<double, double> tMs = iaKa =>
            {
                double s = clearingTimeAtArcingKa != null ? clearingTimeAtArcingKa(iaKa) : input.ClearingTimeS;
                if (double.IsNaN(s) || s <= 0) return double.NaN;
                if (s > MaxArcDurationS) { capped = true; s = MaxArcDurationS; }
                return s * 1000.0;
            };
            var x = Ieee1584_2018.Calculate(ec, kv, input.BoltedFaultKa, gap, dist, h, w, d, tMs);
            if (!x.Calculated) return NotCalculated(r, x.NotCalculatedReason);
            if (capped) r.Notes.Add($"clearing time capped at {MaxArcDurationS:0} s (IEEE 1584 guidance)");
            if (input.SolidlyGrounded) r.Notes.Add("grounding has no term in IEEE 1584-2018");

            r.Calculated = true;
            r.EnclosureCf = x.EnclosureCf;
            r.ArcingCurrentKa = x.Full.ArcingCurrentKa;
            r.ReducedArcingCurrentKa = x.Reduced.ArcingCurrentKa;
            r.ClearingTimeS = x.Full.ArcDurationMs / 1000.0;
            r.ReducedClearingTimeS = x.Reduced.ArcDurationMs / 1000.0;
            r.ReducedCaseGoverns = x.ReducedCaseGovernsEnergy;
            if (r.ReducedCaseGoverns) r.Notes.Add("reduced arcing-current case governs");
            r.GoverningClearingTimeS = (r.ReducedCaseGoverns ? x.Reduced : x.Full).ArcDurationMs / 1000.0;
            r.IncidentEnergyJcm2 = x.IncidentEnergyJcm2;
            r.IncidentEnergyCalCm2 = x.IncidentEnergyJcm2 / JoulesPerCalorie;
            r.BoundaryMm = x.BoundaryMm;
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
                       $"Basis: {r?.Basis ?? Basis}";

            string danger = r.PpeCategory < 0 ? "DANGER — EXCEEDS 40 cal/cm²" : $"PPE Category {r.PpeCategory} (by incident energy)";
            return "ARC FLASH HAZARD — INDICATIVE\n" +
                   $"Panel: {panelName}\n" +
                   $"Voltage: {voltageV:0} V   Class: {cls}\n" +
                   $"Incident Energy: {r.IncidentEnergyCalCm2:0.00} cal/cm² at {r.WorkingDistanceMm:0} mm\n" +
                   $"Arc Flash Boundary: {r.BoundaryMm:0} mm\n" +
                   $"Clearing time: {r.GoverningClearingTimeS * 1000:0} ms ({clearingTimeSource})\n" +
                   $"Gap: {r.GapMm:0} mm" + (string.IsNullOrEmpty(r.ElectrodeConfiguration) ? "" : $"   Electrodes: {r.ElectrodeConfiguration}") + "\n" +
                   $"{danger}\n" +
                   $"Basis: {r.Basis}";
        }
    }
}
