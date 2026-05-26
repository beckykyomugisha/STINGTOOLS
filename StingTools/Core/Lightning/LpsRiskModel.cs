// StingTools — LpsRiskModel.cs
//
// Full BS EN 62305-2:2012 risk model. Computes the four risks R1 (loss of
// human life), R2 (loss of service to the public), R3 (loss of cultural
// heritage) and R4 (economic loss) as the sum of the eight standard risk
// components, each of the form R_X = N_X · P_X · L_X:
//
//   R_A  flash to structure, injury (touch/step)         N_D · P_TA·P_B · L_T
//   R_B  flash to structure, physical damage / fire      N_D · P_B      · L_F
//   R_C  flash to structure, failure of internal systems N_D · P_SPD    · L_O
//   R_M  flash near structure, failure of int. systems   N_M · P_M      · L_O
//   R_U  flash to a line, injury (touch)                 N_L · P_TU·P_EB·P_LD · L_T
//   R_V  flash to a line, physical damage / fire         N_L · P_EB·P_LD      · L_F
//   R_W  flash to a line, failure of internal systems    N_L · P_SPD·P_LD     · L_O
//   R_Z  flash near a line, failure of internal systems  N_I · P_SPD·P_LI     · L_O
//
// Number of events (Annex A), probabilities (Annex B) and relative losses
// (Annex C) come from STING_LPS_RISK_TABLES.json.
//
// Probability sub-factors are DERIVED from the installation, not lumped:
//   • P_MS = (K_S1·K_S2·K_S3·K_S4)² with K_S1 = 0.12·w_m1, K_S2 = 0.12·w_m2
//     (spatial-shield mesh widths), K_S3 from the wiring-routing table, and
//     K_S4 = 1/U_w (Annex B.5).
//   • P_LD / P_LI = (line shielding/bonding factor, Table B.8/B.9 column) ×
//     (U_w withstand reduction, Table B.8/B.9 row) — so the equipment's
//     rated impulse withstand voltage genuinely moves the line risk.
//
// Protection selection follows the standard's actual logic, NOT a residual
// efficiency multiplier: the risk is RE-COMPUTED with each candidate LPS
// class (sets P_B) and SPD level (sets P_SPD / P_EB) substituted in, and the
// minimal (class, SPD level) combination whose recomputed R clears every
// tolerable threshold is recommended. Because the LPS class only touches the
// P_B components (R_A/R_B/R_V) and SPDs only the surge components, a
// surge-dominated R2/R4 is correctly cleared by SPDs, not by raising the
// air-termination class.
//
// Line parameters fall back to a representative power + telecom pair only
// when the caller supplies no line list; an explicit (even empty) list is
// honoured exactly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public static class LpsRiskModel
    {
        private static readonly object _lock = new object();
        private static JObject _tables;

        // Candidate protection measures, ordered least → most protective.
        private static readonly string[] ClassOrder = { "IV", "III", "II", "I" };
        private static readonly string[] SpdOrder   = { "NONE", "III-IV", "II", "I", "BETTER", "BEST" };

        // ── Public entry ──────────────────────────────────────────────

        public static LpsRiskResult Compute(LpsRiskInput input)
        {
            var result = new LpsRiskResult();
            try
            {
                if (input == null) { result.Notes = "No input provided"; return result; }
                EnsureTables();

                // ── Geometry → collection area A_D (Annex A.2) ────────
                double L = input.PlanLengthM, W = input.PlanWidthM, H = input.HeightM;
                if ((L <= 0 || W <= 0) && input.PlanAreaM2 > 0)
                {
                    L = Math.Sqrt(input.PlanAreaM2);
                    W = L;
                }
                if (H <= 0) H = 1.0;
                double Ad = input.AeOverrideM2 > 0
                    ? input.AeOverrideM2
                    : (L * W) + (2.0 * (3.0 * H) * (L + W)) + (Math.PI * Math.Pow(3.0 * H, 2));
                if (Ad < 1.0) Ad = 1.0;
                result.CollectionAreaM2 = Ad;

                double Ng = input.GroundFlashDensity > 0 ? input.GroundFlashDensity : 2.0;
                double Cd = input.LocationFactorCd > 0 ? input.LocationFactorCd : 1.0;

                // ── Event counts (Annex A) ────────────────────────────
                double Nd = Ng * Ad * Cd * 1e-6;                  // flashes to the structure
                result.AnnualStrikeFrequency = Nd;

                double nearR = GetD("near", "radiusM", 500.0);
                double aWithin = (L * W) + (2.0 * nearR * (L + W)) + (Math.PI * nearR * nearR);
                double Am = Math.Max(0.0, aWithin - Ad);
                double Nm = Ng * Am * 1e-6;                       // flashes near the structure

                // ── Installation-fixed probabilities ──────────────────
                double pTA = GetD("pTA", "NONE", 1.0);   // (no extra touch/step protection assumed)
                double pTU = GetD("pTU", "NONE", 1.0);
                double pMS = ComputePMS(input);          // (K_S1·K_S2·K_S3·K_S4)²  (Annex B.5)

                // ── Event terms independent of the chosen protection ──
                // (the protection-variable factors P_B / P_SPD / P_EB are
                //  applied later by the RiskKernel so the same structure can
                //  be re-evaluated for every candidate class / SPD level.)
                double gA = Nd * pTA;     // R_A = gA · P_B · L_T
                double gB = Nd;           // R_B = gB · P_B · L_F
                double gC = Nd;           // R_C = gC · P_SPD · L_O
                double gM = Nm * pMS;     // R_M = gM · P_SPD · L_O
                double gU = 0, gV = 0, gW = 0, gZ = 0;

                double uwRed = UwReduction(input.UwKv > 0 ? input.UwKv : GetD("ks", "defaultUwKv", 1.5));
                double aLper = GetD("line", "aL_perMetre", 40.0);
                double aIper = GetD("line", "aI_perMetre", 4000.0);
                foreach (var ln in ResolveLines(input))
                {
                    double LL = ln.LengthM > 0 ? ln.LengthM : GetD("line", "defaultLengthM", 1000.0);
                    double Ci = GetD("lineCi", Up(ln.Install),     1.0);
                    double Ce = GetD("lineCe", Up(ln.Environment), 0.5);
                    double Ct = GetD("lineCt", Up(ln.Transformer), 1.0);
                    double Nl = Ng * (aLper * LL) * Ci * Ce * Ct * 1e-6;   // flashes to the line
                    double Ni = Ng * (aIper * LL) * Ci * Ce * Ct * 1e-6;   // flashes near the line
                    // P_LD / P_LI = shielding column × U_w withstand reduction.
                    double pLD = GetD("pLD_shieldFactor", Up(ln.Shield), 1.0) * uwRed;
                    double pLI = GetD("pLI_shieldFactor", Up(ln.Shield), 0.3) * uwRed;
                    gU += Nl * pTU * pLD;   // R_U = gU · P_EB · L_T
                    gV += Nl * pLD;         // R_V = gV · P_EB · L_F
                    gW += Nl * pLD;         // R_W = gW · P_SPD · L_O
                    gZ += Ni * pLI;         // R_Z = gZ · P_SPD · L_O
                }

                // ── Loss model (Annex C) ──────────────────────────────
                var lp = ResolveLossProfile(input);
                double occ = Occupancy(input);

                var k = new RiskKernel
                {
                    GA = gA, GB = gB, GC = gC, GM = gM, GU = gU, GV = gV, GW = gW, GZ = gZ,
                    LA1 = lp.Rt * lp.Lt1 * occ,
                    LB1 = lp.Rp * lp.Rf * lp.Hz * lp.Lf1 * occ,           // h_z (L1 only)
                    LO1 = (lp.LifeEndangering ? lp.Lo1 : 0.0) * occ,
                    LB2 = lp.Rp * lp.Rf * lp.Lf2 * occ,
                    LO2 = lp.Lo2 * occ,
                    LB3 = lp.Rp * lp.Rf * lp.Lf3 * occ,
                    LA4 = lp.Rt * lp.Lt4 * occ,
                    LB4 = lp.Rp * lp.Rf * lp.Lf4 * occ,
                    LO4 = lp.Lo4 * occ,
                    L3Applies = lp.L3Applies
                };

                // ── Unprotected baseline (P_B = P_SPD = P_EB = 1) ─────
                var baseR = k.Eval(1.0, 1.0, 1.0);
                result.RiskByLossType["L1"] = baseR.R1;
                result.RiskByLossType["L2"] = baseR.R2;
                result.RiskByLossType["L3"] = baseR.R3;
                result.RiskByLossType["L4"] = baseR.R4;
                result.RiskComponents["R1_Direct"] = baseR.R1;   // legacy alias
                result.ComponentBreakdown["RA"] = gA * k.LA1;
                result.ComponentBreakdown["RB"] = gB * k.LB1;
                result.ComponentBreakdown["RC"] = gC * k.LO1;
                result.ComponentBreakdown["RM"] = gM * k.LO1;
                result.ComponentBreakdown["RU"] = gU * k.LA1;
                result.ComponentBreakdown["RV"] = gV * k.LB1;
                result.ComponentBreakdown["RW"] = gW * k.LO1;
                result.ComponentBreakdown["RZ"] = gZ * k.LO1;

                // ── Tolerable thresholds (Table 7) ────────────────────
                var rt = ToleranceMap();
                foreach (var kv in rt)
                {
                    result.TolerableByLossType[kv.Key] = kv.Value;
                    if (result.RiskByLossType.TryGetValue(kv.Key, out double rval))
                        result.ExceedsByLossType[kv.Key] = rval > kv.Value;
                }
                result.TolerableRisk = input.TolerableRisk > 0 ? input.TolerableRisk : rt["L1"];
                result.RequiresLps = result.ExceedsByLossType.Any(kv => kv.Value);

                // ── Protection selection by RE-COMPUTATION ────────────
                // Search the minimal (LPS class, SPD level) combination whose
                // recomputed risk clears every tolerable threshold. Ordered by
                // increasing combined protection so the least intervention wins.
                string recClass = "NONE", recSpd = "NONE";
                string residualNote = null;
                if (result.RequiresLps)
                {
                    bool found = false;
                    int maxTotal = (ClassOrder.Length - 1) + (SpdOrder.Length - 1);
                    for (int total = 0; total <= maxTotal && !found; total++)
                        for (int ci = 0; ci < ClassOrder.Length && !found; ci++)
                        {
                            int si = total - ci;
                            if (si < 0 || si >= SpdOrder.Length) continue;
                            if (Clears(k, ClassOrder[ci], SpdOrder[si], rt))
                            {
                                recClass = ClassOrder[ci];
                                recSpd = SpdOrder[si];
                                found = true;
                            }
                        }
                    if (!found)
                    {
                        recClass = "I"; recSpd = "BEST";
                        residualNote = " Even Class I with the best coordinated SPDs leaves residual risk above tolerable — review geometry / line routing / shielding.";
                    }
                }
                result.RecommendedClass = recClass;
                result.RecommendedSpdLevel = recSpd;

                // Residual risk per LPS class, recomputed at the recommended
                // SPD level (real protected risk, not an efficiency multiplier).
                double pSPDrec = GetD("pSPD_byLevel", recSpd, 1.0);
                double pEBrec  = GetD("pEB_byLevel",  recSpd, 1.0);
                foreach (var cls in new[] { "I", "II", "III", "IV" })
                {
                    double pB = GetD("pB_byClass", cls, 1.0);
                    var rc = k.Eval(pB, pSPDrec, pEBrec);
                    result.ResidualRiskByClass[cls] = Max4(rc);
                }

                var dom = result.ComponentBreakdown.OrderByDescending(x => x.Value).FirstOrDefault();
                result.Notes = string.Format(
                    "BS EN 62305-2 component model (R = ΣRA..RZ). Nd={0:F4} Nm={1:F4} flashes/yr; " +
                    "unprotected R1={2:E2} R2={3:E2} R3={4:E2} R4={5:E2}; dominant L1 component {6}={7:E2}; " +
                    "recommended LPS class {8} + SPD level {9}.{10} Sub-factors P_MS (K_S1..K_S4) and " +
                    "P_LD/P_LI (U_w withstand) are derived from the inputs; line and loss values use " +
                    "BS EN 62305-2 typical figures where not supplied — confirm with a full risk study.",
                    Nd, Nm, baseR.R1, baseR.R2, baseR.R3, baseR.R4, dom.Key ?? "—", dom.Value,
                    recClass, recSpd, residualNote ?? "");
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsRiskModel.Compute failed", ex);
                result.Notes = "Risk assessment failed: " + ex.Message;
            }
            return result;
        }

        // ── Risk kernel: re-evaluable for any (P_B, P_SPD, P_EB) ───────

        private struct Risks { public double R1, R2, R3, R4; }

        private struct RiskKernel
        {
            public double GA, GB, GC, GM, GU, GV, GW, GZ;
            public double LA1, LB1, LO1, LB2, LO2, LB3, LA4, LB4, LO4;
            public bool L3Applies;

            public Risks Eval(double pB, double pSPD, double pEB)
            {
                // Σ of internal-system event terms (all scale with P_SPD).
                double gInt = GC + GM + GW + GZ;
                var r = new Risks
                {
                    R1 = GA * pB * LA1 + GB * pB * LB1 + GU * pEB * LA1 + GV * pEB * LB1
                       + gInt * pSPD * LO1,
                    R2 = GB * pB * LB2 + GV * pEB * LB2 + gInt * pSPD * LO2,
                    R3 = L3Applies ? (GB * pB * LB3 + GV * pEB * LB3) : 0.0,
                    R4 = GA * pB * LA4 + GU * pEB * LA4 + GB * pB * LB4 + GV * pEB * LB4
                       + gInt * pSPD * LO4
                };
                return r;
            }
        }

        private static bool Clears(RiskKernel k, string lpsClass, string spdLevel, Dictionary<string, double> rt)
        {
            double pB   = GetD("pB_byClass",   lpsClass, 1.0);
            double pSPD = GetD("pSPD_byLevel", spdLevel, 1.0);
            double pEB  = GetD("pEB_byLevel",  spdLevel, 1.0);
            var r = k.Eval(pB, pSPD, pEB);
            return r.R1 <= Tol(rt, "L1") && r.R2 <= Tol(rt, "L2")
                && r.R3 <= Tol(rt, "L3") && r.R4 <= Tol(rt, "L4");
        }

        private static double Tol(Dictionary<string, double> rt, string k)
            => rt.TryGetValue(k, out var v) ? v : double.MaxValue;

        private static double Max4(Risks r) => Math.Max(Math.Max(r.R1, r.R2), Math.Max(r.R3, r.R4));

        private static Dictionary<string, double> ToleranceMap()
        {
            var rt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["L1"] = 1e-5, ["L2"] = 1e-3, ["L3"] = 1e-4, ["L4"] = 1e-3 };
            try
            {
                var arr = LpsEngine.GetRiskFactorLibrary()?["lossTypes"] as JArray;
                if (arr != null)
                    foreach (var lt in arr)
                    {
                        string id = lt["id"]?.ToString();
                        double v = lt["rt"]?.Value<double>() ?? 0.0;
                        if (!string.IsNullOrEmpty(id) && v > 0) rt[id] = v;
                    }
            }
            catch (Exception ex) { StingLog.Warn($"LossType Rt read: {ex.Message}"); }
            return rt;
        }

        // ── K_S derivation → P_MS (Annex B.5) ──────────────────────────

        private static double ComputePMS(LpsRiskInput input)
        {
            double per = GetD("ks", "ks1ks2_perMetre", 0.12);
            // K_S1 — large-scale spatial shield (LPS grid) mesh width w_m1.
            double ks1 = input.SpatialShieldMeshWidthM > 0
                ? Math.Min(1.0, per * input.SpatialShieldMeshWidthM)
                : (input.StructureShielded ? GetD("ks", "ks1_fallbackStructureShielded", 0.5) : 1.0);
            // K_S2 — inner-LPZ shield mesh width w_m2.
            double ks2 = input.InternalShieldMeshWidthM > 0
                ? Math.Min(1.0, per * input.InternalShieldMeshWidthM)
                : 1.0;
            // K_S3 — internal-wiring routing / shielding (Table B.5).
            string ks3key = !string.IsNullOrWhiteSpace(input.WiringType)
                ? Up(input.WiringType)
                : (input.WiringShielded ? "SHIELDED_RS_5_20" : "UNSHIELDED_NO_PRECAUTION");
            double ks3 = GetD("ks.ks3", ks3key, 1.0);
            // K_S4 = 1 / U_w (U_w in kV).
            double uw = input.UwKv > 0 ? input.UwKv : GetD("ks", "defaultUwKv", 1.5);
            double ks4 = Math.Min(1.0, 1.0 / uw);
            return Math.Min(1.0, Math.Pow(ks1 * ks2 * ks3 * ks4, 2.0));
        }

        /// <summary>U_w withstand reduction for P_LD / P_LI (Table B.8/B.9 row):
        /// the value for the largest tabulated U_w not exceeding the equipment's
        /// U_w (conservative for intermediate voltages).</summary>
        private static double UwReduction(double uwKv)
        {
            try
            {
                EnsureTables();
                var obj = _tables?["uwReduction"] as JObject;
                if (obj == null) return 1.0;
                double best = 1.0; double bestKey = -1;
                foreach (var kv in obj)
                {
                    if (kv.Key.StartsWith("_")) continue;
                    if (!double.TryParse(kv.Key, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double kKv)) continue;
                    if (kKv <= uwKv + 1e-9 && kKv > bestKey)
                    {
                        bestKey = kKv;
                        best = kv.Value?.Value<double>() ?? 1.0;
                    }
                }
                return best;
            }
            catch (Exception ex) { StingLog.Warn($"UwReduction: {ex.Message}"); return 1.0; }
        }

        // ── Line resolution ───────────────────────────────────────────

        private static IEnumerable<LpsServiceLine> ResolveLines(LpsRiskInput input)
        {
            // An explicit list (even empty) is authoritative: empty ⇒ no
            // connected lines ⇒ no line-related risk components.
            if (input.Lines != null) return input.Lines;

            var derived = new List<LpsServiceLine>();
            if (input.ConnectedServices != null)
            {
                foreach (var s in input.ConnectedServices)
                {
                    string id = (s ?? "").ToUpperInvariant();
                    if (id.StartsWith("POWER"))
                        derived.Add(new LpsServiceLine { Id = "POWER",
                            Install = id.Contains("UG") ? "BURIED" : "AERIAL" });
                    else if (id.StartsWith("TELECOM"))
                        derived.Add(new LpsServiceLine { Id = "TELECOM",
                            Install = id.Contains("UG") ? "BURIED" : "AERIAL" });
                    // GAS / WATER are bonded metallic services, not signal
                    // lines — excluded from the line-surge components.
                }
            }
            if (derived.Count > 0) return derived;

            // Default: a representative incoming power + telecom line.
            return new List<LpsServiceLine>
            {
                new LpsServiceLine { Id = "POWER" },
                new LpsServiceLine { Id = "TELECOM" }
            };
        }

        // ── Loss profile resolution (IDs → numeric → defaults) ─────────

        private class LossProfile
        {
            public double Rt = 0.001, Rp = 1.0, Rf = 0.01, Hz = 1.0;
            public double Lt1 = 0.01, Lt4 = 0.0001;
            public double Lf1 = 0.1, Lf2 = 0.1, Lf3 = 0.1, Lf4 = 0.5;
            public double Lo1 = 0.1, Lo2 = 0.01, Lo4 = 0.01;
            public bool LifeEndangering = false;
            public bool L3Applies = false;
        }

        private static LossProfile ResolveLossProfile(LpsRiskInput input)
        {
            var p = new LossProfile
            {
                Rt  = GetD("loss.rt", Up(input.SoilSurfaceType), 0.001),
                Rp  = GetD("loss.rp", Up(input.FireProtection),  1.0),
                Lt1 = GetD("loss.LT", "L1", 0.01),
                Lt4 = GetD("loss.LT", "L4", 0.0001),
                Lf1 = GetD("loss.LF", "L1", 0.1),
                Lf2 = GetD("loss.LF", "L2", 0.1),
                Lf3 = GetD("loss.LF", "L3", 0.1),
                Lf4 = GetD("loss.LF", "L4", 0.5),
                Lo1 = GetD("loss.LO", "L1_lifeEndangering", 0.1),
                Lo2 = GetD("loss.LO", "L2", 0.01),
                Lo4 = GetD("loss.LO", "L4", 0.01)
            };

            string bId = Up(input.BuildingTypeId);
            string cId = Up(input.InternalContentId);
            string oId = Up(input.OccupantHazardId);

            // r_f — fire/explosion risk (Table C.5)
            string rfKey = Up(input.FireRisk);
            if (string.IsNullOrEmpty(rfKey))
            {
                if (bId == "HAZARDOUS" || cId == "EXPLOSIVE") rfKey = "EXPLOSION";
                else if (bId == "INDUSTRIAL" || cId == "HIGH_FIRE") rfKey = "HIGH";
                else if (input.InternalContentCc >= 5.0) rfKey = "EXPLOSION";
                else if (input.InternalContentCc >= 2.5) rfKey = "HIGH";
                else rfKey = "ORDINARY";
            }
            p.Rf = GetD("loss.rf", rfKey, 0.01);

            // h_z — special hazard / panic (Table C.6), L1 only
            string hzKey = Up(input.SpecialHazard);
            if (string.IsNullOrEmpty(hzKey))
            {
                if (oId == "HIGH" || input.OccupantHazardCd >= 5.0) hzKey = "HIGH_PANIC";
                else if (bId == "HEALTHCARE") hzKey = "MEDIUM_PANIC";
                else if (oId == "MEDIUM" || bId == "EDUCATION" || bId == "CULTURAL"
                         || input.OccupantHazardCd >= 2.0) hzKey = "LOW_PANIC";
                else hzKey = "NONE";
            }
            p.Hz = GetD("loss.hz", hzKey, 1.0);

            // Life-endangering internal systems → R_C/R_M/R_W/R_Z feed R1.
            p.LifeEndangering = input.LifeEndangeringSystems
                ?? (bId == "HEALTHCARE" || bId == "HAZARDOUS" || cId == "EXPLOSIVE"
                    || input.BuildingTypeCb >= 2.5);

            // L3 (cultural heritage) relevance.
            p.L3Applies = bId == "CULTURAL" || cId == "IRREPLACEABLE"
                          || Math.Abs(input.BuildingTypeCb - 2.0) < 1e-6;

            return p;
        }

        private static double Occupancy(LpsRiskInput input)
        {
            double f = 1.0;
            if (input.PersonsInZone > 0 && input.PersonsTotal > 0)
                f *= input.PersonsInZone / input.PersonsTotal;
            if (input.OccupiedHoursPerYear > 0)
                f *= Math.Min(1.0, input.OccupiedHoursPerYear / 8760.0);
            return f;
        }

        // ── Table access ──────────────────────────────────────────────

        /// <summary>Read a numeric table value. <paramref name="section"/> may
        /// be dotted (e.g. "loss.rf" or "ks.ks3") to descend nested objects.</summary>
        private static double GetD(string section, string key, double fallback)
        {
            try
            {
                EnsureTables();
                JToken node = _tables;
                foreach (var part in section.Split('.'))
                {
                    node = node?[part];
                    if (node == null) return fallback;
                }
                var v = node[key];
                if (v == null || v.Type == JTokenType.Null) return fallback;
                return v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"LpsRiskModel.GetD {section}.{key}: {ex.Message}"); return fallback; }
        }

        private static string Up(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToUpperInvariant();

        private static void EnsureTables()
        {
            if (_tables != null) return;
            lock (_lock)
            {
                if (_tables != null) return;
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_LPS_RISK_TABLES.json");
                    _tables = (!string.IsNullOrEmpty(path) && File.Exists(path))
                        ? JObject.Parse(File.ReadAllText(path))
                        : new JObject();
                    if (_tables.Count == 0)
                        StingLog.Warn("LpsRiskModel: STING_LPS_RISK_TABLES.json not found — using built-in defaults.");
                }
                catch (Exception ex)
                {
                    StingLog.Error("LpsRiskModel: failed loading STING_LPS_RISK_TABLES.json", ex);
                    _tables = new JObject();
                }
            }
        }
    }
}
